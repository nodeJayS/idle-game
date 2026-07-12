#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;

namespace IdleGame.BalanceSim
{
    /// <summary>
    /// Crypt-mode scenario harness (§7.3): builds and runs ONE crypt floor at a given depth exactly as
    /// the client's <c>DungeonMode.Generate</c> + <c>Enter</c> do, but over pure GameCore so the
    /// BalanceSim can chart difficulty-vs-reward as the depth ramp climbs on a fixed-power party.
    ///
    /// The party is PINNED by stage/level/gear (built once via <see cref="Scenarios.BuildSave"/>); the
    /// only variable is the depth, which drives the floor's OWN <see cref="Crypt.StageEquivalent"/>
    /// anchor (user verdict 2026-07-12 — the party's stage pins only its build). Runs replicate
    /// the client's floor construction: RoomCount 12, Linear, the depth-tier theme/roster/boss, the
    /// depth-band encounter budget, the run-alignment reward vault on every CryptFloorsPerRun-th floor,
    /// and the mini-boss floor guardian on the others. Deterministic: the gen seed derives from the cell
    /// seed + depth, and the combat rng is the cell seed — no clock, no System.Random.
    /// </summary>
    public static class CryptScenarios
    {
        // Fallback cast mirrors DungeonMode's when a floor's tier is null (Default() config always
        // configures tiers, so this is only reached by a stripped config).
        private static readonly string[] FallbackRoster = { "cave_bat", "gloom_shade" };
        private const string FallbackBoss = "nightmare_maw";

        public sealed class CryptResult
        {
            public bool Won;          // reached the boss and cleared the floor
            public bool TimedOut;     // lost to the DungeonMaxRunSeconds failsafe with the party still alive
            public double Seconds;    // sweep length (TimeMs)
            public int RoomsCleared;
            public int ChestsOpened;
            public long Gold;
            public long Dust;
            public long Xp;
            public int Items;         // PendingLoot drops accrued this floor
            public int HeroDowns;     // hero deaths over the sweep (revive-and-continue counted each)
            public bool KeyHeld;      // Boss Key in hand at the end (the key bearer was slain)
        }

        /// <summary>Per-depth dungeon-gen seed, kept on its OWN stream from the combat rng — the sim's
        /// analogue of the client's split between <c>DungeonMode.NextSeed</c> (layout) and the combat
        /// <see cref="Rng"/>. Deterministic mix of the cell seed and the depth.</summary>
        private static int GenSeed(uint cellSeed, int depth) => unchecked((int)cellSeed + depth * 92821);

        /// <summary>Build the depth's floor and auto-resolve it. <paramref name="save"/> is the pinned
        /// party; <paramref name="cellSeed"/> drives both the layout (via <see cref="GenSeed"/>) and the
        /// combat rng, so a (baseSeed, stage, level, gear, trial) tuple reproduces the whole floor.</summary>
        public static CryptResult RunFloor(GameConfig cfg, SaveState save, int stage, int depth, uint cellSeed)
        {
            // Run alignment: runs start at record+1 and every CryptFloorsPerRun-th floor is a run's
            // FINAL floor — the one that grows the reward vault and fields the TRUE boss (§7.3).
            bool final = depth % Math.Max(1, cfg.Balance.CryptFloorsPerRun) == 0;
            var tier = Crypt.TierForFloor(depth, cfg);

            var dungeon = DungeonGen.Generate(new DungeonParams
            {
                Seed = GenSeed(cellSeed, depth),
                RoomCount = 12,
                LoopChance = 0.15,
                DecorDensity = 0.6,
                Theme = tier?.ThemeKey ?? "crypt",
                Linear = true,
                Encounter = Crypt.EncounterForFloor(depth, cfg),
                RewardRoom = final,
            });

            // Cast guards mirror DungeonMode.Enter: empty roster → fallback; a boss id the config doesn't
            // define → "" (the sim tolerates a bossless floor).
            IReadOnlyList<string> roster =
                tier != null && tier.TrashRoster.Count > 0 ? tier.TrashRoster : FallbackRoster;
            string boss = tier?.BossId ?? FallbackBoss;
            if (!cfg.Monsters.ContainsKey(boss)) boss = "";

            var rng = new Rng(cellSeed);
            // Own-depth curve (user verdict 2026-07-12): monsters scale to the floor's OWN
            // stage-equivalent; the caller's stage now pins only the PARTY's build, not the floor.
            var s = Combat.InitDungeon(Scenarios.FieldedParty(save), Crypt.StageEquivalent(depth, cfg),
                dungeon, roster, boss, cfg, rng,
                hpMult: Crypt.FloorHpMult(depth, cfg), dmgMult: Crypt.FloorDmgMult(depth, cfg),
                miniBoss: !final, rewardMult: Crypt.FloorRewardMult(depth, cfg));
            Combat.RefreshPartyStats(s, save, cfg); // fold in gear + account buffs, like the client's Begin

            int heroDowns = 0;
            foreach (var ev in Combat.RunToEnd(s, cfg, rng))
                if (ev.Type == CombatEventType.Death && ev.EntityId != null
                    && ev.EntityId.StartsWith("P", StringComparison.Ordinal))
                    heroDowns++; // hero ids are "P…"; every monster id is "E…" (SpawnPack/InitDungeon)

            bool partyAlive = s.Entities.Any(e => e.Team == Team.Party && e.Alive);
            return new CryptResult
            {
                Won = s.Status == CombatStatus.Won,
                TimedOut = s.Status == CombatStatus.Lost && partyAlive,
                Seconds = s.TimeMs / 1000.0,
                RoomsCleared = s.DungeonClearedRooms.Count,
                ChestsOpened = s.DungeonChestsOpened.Count,
                Gold = s.PendingGold,
                Dust = s.PendingDust,
                Xp = s.PendingXp,
                Items = s.PendingLoot.Count,
                HeroDowns = heroDowns,
                KeyHeld = s.DungeonBossKeyHeld,
            };
        }
    }
}
