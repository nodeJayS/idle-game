using System;
using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Live-ops events (10.16, mobile arc MM4): two recurring, UTC-scheduled boosts computed as PURE
    // functions of nowMs (no persisted state, no server — the seam remote config replaces in §9). Weekend
    // zone boost (Sat 00:00–Mon 00:00 UTC, +10% gold/xp/drop in one rotating zone, farm-only) and mutated
    // crypt (Wed 00:00–Fri 00:00 UTC, one extra floor modifier + 10% chest dust). These pin the window
    // edges, the deterministic rotation, the client banner, the fight-init effect snapshot, and the
    // composed kill/dust math. PURE C# sim only.
    public class EventsTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static long Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
            new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        // A known week in UTC: 2024-01-03 Wed, -04 Thu, -05 Fri, -06 Sat, -07 Sun, -08 Mon.
        private const int Y = 2024, M = 1;

        private static HeroInstance Champ() => new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 };

        // ============================ weekend zone boost: window edges ============================

        [Fact]
        public void WeekendBoostOffFridayOnSaturdayAndSundayOffMonday()
        {
            Assert.Null(Events.ZoneBoost(Cfg, Utc(Y, M, 5, 23, 59)));  // Fri 23:59 — off
            Assert.NotNull(Events.ZoneBoost(Cfg, Utc(Y, M, 6, 0, 0))); // Sat 00:00 — on
            Assert.NotNull(Events.ZoneBoost(Cfg, Utc(Y, M, 7, 23, 59)));// Sun 23:59 — on
            Assert.Null(Events.ZoneBoost(Cfg, Utc(Y, M, 8, 0, 0)));    // Mon 00:00 — off
        }

        [Fact]
        public void WeekendBoostEndMsIsMondayMidnightUtc()
        {
            long endExpected = Utc(Y, M, 8, 0, 0); // Monday 00:00 UTC
            Assert.Equal(endExpected, Events.ZoneBoost(Cfg, Utc(Y, M, 6, 3, 0)).Value.EndMs);   // from Sat
            Assert.Equal(endExpected, Events.ZoneBoost(Cfg, Utc(Y, M, 7, 20, 0)).Value.EndMs);  // from Sun — same end
        }

        // ============================ zone rotation ============================

        [Fact]
        public void ZoneBoostIsDeterministicForTheSameNow()
        {
            long now = Utc(Y, M, 6, 12, 0);
            Assert.Equal(Events.ZoneBoost(Cfg, now).Value.ZoneIndex, Events.ZoneBoost(Cfg, now).Value.ZoneIndex);
        }

        [Fact]
        public void ZoneBoostIsStableAcrossASingleWeekend()
        {
            // Saturday and Sunday of the same weekend share the /7 week bucket ⇒ the SAME zone.
            int sat = Events.ZoneBoost(Cfg, Utc(Y, M, 6, 1, 0)).Value.ZoneIndex;
            int sun = Events.ZoneBoost(Cfg, Utc(Y, M, 7, 22, 0)).Value.ZoneIndex;
            Assert.Equal(sat, sun);
        }

        [Fact]
        public void ZoneBoostRotatesByOneEachWeekAndCoversEveryZone()
        {
            // Ten consecutive Saturdays (starting 2024-01-06) must visit all ten zones exactly once, each
            // exactly one index past the previous (mod zoneCount) — the (epochDays/7) % zoneCount rotation.
            int zoneCount = Cfg.Zones.Count;
            var seen = new List<int>();
            int prev = -1;
            for (int w = 0; w < zoneCount; w++)
            {
                long sat = Utc(Y, M, 6, 12, 0) + w * 7L * DailyLogin.DayMs;
                int idx = Events.ZoneBoost(Cfg, sat).Value.ZoneIndex;
                if (prev >= 0) Assert.Equal((prev + 1) % zoneCount, idx);
                prev = idx;
                seen.Add(idx);
            }
            Assert.Equal(zoneCount, seen.Distinct().Count()); // a full permutation — every zone covered
        }

        [Fact]
        public void ZoneBoostBandMatchesZoneForStage()
        {
            var zb = Events.ZoneBoost(Cfg, Utc(Y, M, 6, 12, 0)).Value;
            // StageFrom/StageTo are the StagesPerTier band, matching GameConfig.ZoneForStage's tier map.
            Assert.Equal(zb.ZoneIndex, Cfg.Balance.Tier(zb.StageFrom) % Cfg.Zones.Count);
            Assert.Equal(zb.ZoneIndex, Cfg.Balance.Tier(zb.StageTo) % Cfg.Zones.Count);
            Assert.Equal(zb.StageFrom + Cfg.Balance.StagesPerTier - 1, zb.StageTo);
        }

        // ============================ mutated crypt: window edges ============================

        [Fact]
        public void CryptMutationOffTuesdayOnWedThuOffFriday()
        {
            Assert.Null(Events.CryptMutation(Cfg, Utc(Y, M, 2, 23, 59)));  // Tue 23:59 — off
            Assert.NotNull(Events.CryptMutation(Cfg, Utc(Y, M, 3, 0, 0))); // Wed 00:00 — on
            Assert.NotNull(Events.CryptMutation(Cfg, Utc(Y, M, 4, 23, 59)));// Thu 23:59 — on
            Assert.Null(Events.CryptMutation(Cfg, Utc(Y, M, 5, 0, 0)));    // Fri 00:00 — off
        }

        [Fact]
        public void CryptMutationEndMsIsFridayMidnightUtc()
        {
            long endExpected = Utc(Y, M, 5, 0, 0); // Friday 00:00 UTC
            Assert.Equal(endExpected, Events.CryptMutation(Cfg, Utc(Y, M, 3, 6, 0)).Value.EndMs);
            Assert.Equal(endExpected, Events.CryptMutation(Cfg, Utc(Y, M, 4, 18, 0)).Value.EndMs);
        }

        [Fact]
        public void MutationModifierIdIsDeterministicAndFromTheCycleAndStableInWindow()
        {
            long wed = Utc(Y, M, 3, 2, 0), thu = Utc(Y, M, 4, 20, 0);
            string? a = Events.MutationModifierId(Cfg, 7, wed);
            Assert.NotNull(a);
            Assert.Contains(a, Cfg.ModifierCycle);
            Assert.Equal(a, Events.MutationModifierId(Cfg, 7, wed));  // same now/depth ⇒ same pick
            Assert.Equal(a, Events.MutationModifierId(Cfg, 7, thu));  // stable across the whole window
            Assert.Null(Events.MutationModifierId(Cfg, 7, Utc(Y, M, 6, 0, 0))); // off-window ⇒ null
        }

        // ============================ client banner ============================

        [Fact]
        public void ActiveListsWhicheverEventsAreLiveWithCeilableEndMs()
        {
            var weekend = Events.Active(Cfg, Utc(Y, M, 6, 12, 0));
            Assert.Single(weekend);
            Assert.Equal(Events.WeekendZoneBoostId, weekend[0].Id);
            Assert.Equal(Utc(Y, M, 8, 0, 0), weekend[0].EndMs);

            var crypt = Events.Active(Cfg, Utc(Y, M, 3, 12, 0));
            Assert.Single(crypt);
            Assert.Equal(Events.MutatedCryptId, crypt[0].Id);

            // A quiet weekday (Monday) — no events live.
            Assert.Empty(Events.Active(Cfg, Utc(Y, M, 8, 12, 0)));
        }

        // ============================ effect snapshot at fight init ============================

        [Fact]
        public void FarmFightInTheBoostedZoneGetsTheMultsAdjacentDoesNot()
        {
            long now = Utc(Y, M, 6, 12, 0);
            var zb = Events.ZoneBoost(Cfg, now).Value;
            double m = 1.0 + Cfg.Balance.EventZoneBoostMult;

            var boosted = Combat.InitFarm(new[] { Champ() }, zb.StageFrom, Cfg, new Rng(1), null, now);
            Assert.Equal(m, boosted.Events.GoldMult, 6);
            Assert.Equal(m, boosted.Events.XpMult, 6);
            Assert.Equal(m, boosted.Events.DropMult, 6);

            int adjacentStage = zb.StageTo + 1; // the next zone band — not boosted
            var adjacent = Combat.InitFarm(new[] { Champ() }, adjacentStage, Cfg, new Rng(1), null, now);
            Assert.Equal(1.0, adjacent.Events.GoldMult, 6);
        }

        [Fact]
        public void NowZeroIsTheAllOffLegacyPath()
        {
            var s = Combat.InitFarm(new[] { Champ() }, 3, Cfg, new Rng(1)); // no nowMs ⇒ 0
            Assert.Equal(1.0, s.Events.GoldMult, 6);
            Assert.Equal(1.0, s.Events.DropMult, 6);
            Assert.Equal(1.0, s.Events.DustMult, 6);
        }

        [Fact]
        public void TowerFightIgnoresTheZoneBoost()
        {
            long now = Utc(Y, M, 6, 12, 0);
            var zb = Events.ZoneBoost(Cfg, now).Value;
            // InitTower takes no clock and never snapshots the zone boost — the mode is deliberately excluded.
            var t = Combat.InitTower(new[] { Champ() }, Math.Max(1, zb.StageFrom), Cfg, new Rng(1));
            Assert.Equal(1.0, t.Events.GoldMult, 6);
        }

        // ============================ kill-path math ============================

        [Fact]
        public void KillPayoutIsExactlyBaseTimes110Floored()
        {
            // Drive one slime kill through the real HandleDeath path with and without the boost, and assert
            // the boosted payout equals ⌊unfloored base × 1.1⌋ (flooring happens at accrual, per the display
            // rule) — computed independently from the monster def + KillRewardMult.
            const int stage = 5;
            (long gold, long xp) Run(EventMults ev)
            {
                var slime = new CombatEntity
                {
                    Id = "E", Team = Team.Enemy, Pos = new Vec2(0.4, 0), RefKind = "monster", RefId = "slime",
                    Stats = new StatBlock { [StatKey.Hp] = 5, [StatKey.Atk] = 0, [StatKey.Def] = 0, [StatKey.AtkSpd] = 1 },
                    Hp = 5, MaxHp = 5, AttackIntervalMs = 1000,
                };
                var hero = new CombatEntity
                {
                    Id = "P", Team = Team.Party, Pos = new Vec2(0, 0), RefKind = "hero", RefId = "P",
                    Stats = new StatBlock { [StatKey.Hp] = 1000, [StatKey.Atk] = 500, [StatKey.Def] = 0, [StatKey.AtkSpd] = 1, [StatKey.MoveSpd] = 3 },
                    Hp = 1000, MaxHp = 1000, AttackIntervalMs = 1000,
                };
                var s = new CombatState { Stage = stage, Events = ev };
                s.Entities.Add(slime);
                s.Entities.Add(hero);
                for (int i = 0; i < 60 && s.PendingGold == 0; i++)
                    Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
                return (s.PendingGold, s.PendingXp);
            }

            var (baseGold, baseXp) = Run(default); // 1.0 no-op
            var (boostGold, boostXp) = Run(new EventMults { GoldBonus = 0.10, XpBonus = 0.10 });

            Assert.True(baseGold > 0 && baseXp > 0);

            var mdef = Cfg.Monsters["slime"];
            double mult = Cfg.Balance.KillRewardMult(stage);
            Assert.Equal((long)Math.Floor(mdef.GoldReward * mult), baseGold);            // base flooring location
            Assert.Equal((long)Math.Floor(mdef.GoldReward * mult * 1.10), boostGold);    // composed +10%, floored
            Assert.Equal((long)Math.Floor(mdef.XpReward * mult * 1.10), boostXp);
        }

        // ============================ mutated crypt effect ============================

        [Fact]
        public void MutatedCryptAddsOneCycleModifierAndBoostsDust()
        {
            long now = Utc(Y, M, 3, 12, 0); // Wednesday — mutation live
            const int stage = 6;
            var d = DungeonGen.Generate(new DungeonParams { Seed = 3, RoomCount = 14 });
            string expectedMod = Events.MutationModifierId(Cfg, stage, now);

            var mutated = Combat.InitDungeon(new List<HeroInstance>(), stage, d,
                new List<string> { "slime", "goblin" }, "goblin_king", Cfg, new Rng(1), nowMs: now);

            // Every enemy wears exactly the one deterministic cycle modifier (a fresh dungeon has none otherwise).
            var enemies = mutated.Entities.Where(e => e.Team == Team.Enemy).ToList();
            Assert.NotEmpty(enemies);
            Assert.All(enemies, e => Assert.Contains(expectedMod, e.ModTypes));
            // Chest dust pays +EventCryptDustMult.
            Assert.Equal(1.0 + Cfg.Balance.EventCryptDustMult, mutated.Events.DustMult, 6);
        }

        [Fact]
        public void OffWindowCryptIsUntouched()
        {
            long now = Utc(Y, M, 6, 12, 0); // Saturday — no crypt mutation
            var d = DungeonGen.Generate(new DungeonParams { Seed = 3, RoomCount = 14 });
            var plain = Combat.InitDungeon(new List<HeroInstance>(), 6, d,
                new List<string> { "slime", "goblin" }, "goblin_king", Cfg, new Rng(1), nowMs: now);

            Assert.All(plain.Entities.Where(e => e.Team == Team.Enemy), e => Assert.Empty(e.ModTypes));
            Assert.Equal(1.0, plain.Events.DustMult, 6);

            // And nowMs=0 (legacy) leaves the dungeon unmutated too.
            var legacy = Combat.InitDungeon(new List<HeroInstance>(), 6, d,
                new List<string> { "slime", "goblin" }, "goblin_king", Cfg, new Rng(1));
            Assert.All(legacy.Entities.Where(e => e.Team == Team.Enemy), e => Assert.Empty(e.ModTypes));
            Assert.Equal(1.0, legacy.Events.DustMult, 6);
        }
    }
}
