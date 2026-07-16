#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>New-game creation + save schema migration.</summary>
    public static class Save
    {
        // v2 (2026-07-03): mana removed. StatKey values 9/10 (MaxMana/ManaRegen) retired;
        // v1 saved items can't carry them as affixes (the affix pool never rolled mana), but
        // Migrate defensively strips any affix whose StatKey no longer exists.
        // v3 (2026-07-11): endless mode — ProgressState.EndlessBest field + CurrentStage may
        // exceed the stage table. No data transform (EndlessBest deserializes to 0); the bump
        // exists so an endless save can't silently load in an old build that would cap it.
        public const int SaveVersion = 3;
        public const int PartySize = 3; // fielded party slots; the bag holds the rest
        public const string StarterHeroDef = "warrior_basic";
        public const string StarterMageDef = "magician_basic";

        /// <summary>
        /// A fresh game: just a Warrior in slot 0 (the rest empty). More heroes are
        /// earned through progression — e.g. the Magician unlocks at stage 3
        /// (<see cref="GameConfig.HeroUnlocks"/>). `now` (epoch ms) is passed in, not read
        /// from the clock, to keep game-core pure/testable.
        /// </summary>
        public static SaveState NewGame(uint seed, GameConfig cfg, long now)
        {
            var warrior = MakeHero("h1", StarterHeroDef, cfg);

            var party = new string?[PartySize];
            party[0] = warrior.Id; // Warrior fielded; remaining slots empty

            var save = new SaveState
            {
                Version = SaveVersion,
                RngSeed = seed,
                RngCursor = 0,
                Heroes = new List<HeroInstance> { warrior },
                Party = party,
                Inventory = new List<Item>(),
                Currencies = new Dictionary<string, long> { ["gold"] = 0 },
                // Intro.Armed = true is set ONLY here: a fresh game opts into the §7.4 staged reveal
                // (features hidden until earned) + guided intro. Every existing save deserializes
                // Armed=false and stays fully unlocked, forever.
                Progress = new ProgressState { HighestStage = 0, CurrentStage = 1, AccountLevel = 1, Intro = new IntroState { Armed = true } },
                LastClaimAt = now,
            };
            return Quests.EnsureBoard(save, cfg); // start with a full goal board
        }

        private static HeroInstance MakeHero(string id, string defId, GameConfig cfg)
        {
            if (!cfg.Heroes.TryGetValue(defId, out var def))
                throw new InvalidOperationException($"NewGame: missing hero def \"{defId}\"");
            return new HeroInstance
            {
                Id = id,
                DefId = def.DefId,
                Level = 1,
                Xp = 0,
                Equipped = new Dictionary<EquipSlot, string>(),
            };
        }

        /// <summary>
        /// Update the heartbeat: stamp <see cref="SaveState.LastClaimAt"/> to <c>now</c>
        /// (epoch ms) so online play isn't later mistaken for offline time by idle
        /// accrual. Pure (returns a new save); never moves the clock backward.
        /// </summary>
        public static SaveState Touch(SaveState save, long now)
        {
            long stamped = Math.Max(save.LastClaimAt, now);
            if (stamped == save.LastClaimAt) return save; // no-op, share the ref

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = stamped,
            };
        }

        /// <summary>
        /// Bring a loaded save up to the current version. Rejects a save newer than this
        /// build, and back-fills any null collections so a partial/older JSON payload
        /// can't NRE the rest of the game.
        /// </summary>
        public static SaveState Migrate(SaveState? save)
        {
            if (save == null) throw new ArgumentException("Migrate: null save");
            if (save.Version > SaveVersion)
                throw new InvalidOperationException($"Migrate: save version {save.Version} is newer than supported {SaveVersion}");
            if (save.Version < 1)
                throw new InvalidOperationException($"Migrate: unsupported version {save.Version} (expected 1..{SaveVersion})");

            // Defensive defaults: deserializers may leave collections null on partial input.
            save.Heroes ??= new List<HeroInstance>();
            save.Inventory ??= new List<Item>();

            // v1 -> v2 (mana removal): strip any affix whose StatKey no longer exists in the
            // enum. StatKey serializes numerically, and the retired MaxMana/ManaRegen values
            // (9/10) are left as an explicit gap so every other stat keeps its persisted int —
            // but if an old save somehow carried a 9/10 affix, drop it rather than let a
            // dangling (StatKey)9/10 leak into stat math. Covers bag AND equipped (equipped
            // items live in Inventory, referenced by id). Idempotent.
            if (save.Version < 2)
            {
                foreach (var item in save.Inventory)
                    item.Affixes?.RemoveAll(a => !Enum.IsDefined(typeof(StatKey), a.Stat));
                save.Version = 2;
            }

            // v2 -> v3 (endless mode): no data transform. EndlessBest deserializes to 0 on a
            // v2 payload; stamping the version keeps a v3 save from loading in a pre-endless build.
            if (save.Version < 3)
                save.Version = 3;
            save.Currencies ??= new Dictionary<string, long>();
            save.Progress ??= new ProgressState();
            save.Progress.Tower ??= new TowerState(); // older saves predate the Tower mode
            save.Progress.Achievements ??= new AchievementState(); // older saves predate the achievement ladder
            save.Progress.Achievements.Counters ??= new Dictionary<AchievementMetric, long>();
            save.Progress.Achievements.Claimed ??= new Dictionary<string, int>();
            save.Progress.Daily ??= new DailyLoginState(); // older saves predate the daily-login streak
            save.Progress.Crypt ??= new CryptState(); // older saves predate the crypt meta (keys/boons)
            save.Progress.Crypt.Boons ??= new Dictionary<string, int>();
            save.Progress.Intro ??= new IntroState(); // older saves predate FTUE arming (Armed=false => all unlocked)
            save.Progress.Intro.IntroClaimed ??= new Dictionary<string, bool>();
            save.Progress.Loot ??= new LootFilterState(); // older saves predate the loot filter (imprint guard defaults ON)
            save.Progress.Loot.SalvageMaxBySlot ??= new Dictionary<EquipSlot, Rarity>();
            save.Progress.Codex ??= new CodexState(); // older saves predate the codex/collection (10.15); empty = nothing collected
            save.Progress.Codex.Kills ??= new Dictionary<string, long>();
            save.Progress.Codex.SetSeen ??= new Dictionary<string, int>();
            save.Progress.Season ??= new SeasonState(); // older saves predate the season track (10.16); Id="" ⇒ any month resets it
            save.Progress.Ascension ??= new AscensionState(); // older saves predate hero ascension (10.17); empty = no shards banked
            save.Progress.Ascension.Shards ??= new Dictionary<string, long>();
            // (Retroactive set discovery for pre-codex bags lives in Codex.SyncFromInventory — it needs
            // cfg to resolve slots through ItemBases, which Migrate deliberately doesn't take. The client
            // runs it at load beside SyncHeroUnlocks/SyncToStage, the other cfg-aware retro-grants.)
            save.Quests ??= new QuestBoard(); // EnsureBoard (client, needs cfg) backfills the goals
            save.Modifiers ??= new MonsterModifiers(); // none owned on older saves until a boss grants one
            save.Modifiers.Tuning ??= new Dictionary<string, double>(); // modifier-shop tuning (new field)
            save.GachaPity ??= new Dictionary<string, int>(); // gacha pity counters (new field; no version bump)

            // Per-hero back-fills for older saves (Lever 3): no skill ranks invested yet.
            foreach (var h in save.Heroes)
            {
                h.SkillRanks ??= new Dictionary<string, int>();
                // Warrior kit rework (2026-07-02): cleave -> cycloneslash,
                // warcry -> shieldcharge. Invested ranks follow the new actives.
                TransferRank(h.SkillRanks, "cleave", "cycloneslash");
                TransferRank(h.SkillRanks, "warcry", "shieldcharge");
            }

            // Normalize the party to PartySize, preserving the first slots. An older save
            // with a longer party (the cap was once 4) keeps its first PartySize heroes;
            // anyone in an overflow slot is benched (they stay owned in Heroes).
            if (save.Party == null)
                save.Party = new string?[PartySize];
            else if (save.Party.Length != PartySize)
            {
                var resized = new string?[PartySize];
                int copy = Math.Min(PartySize, save.Party.Length);
                for (int i = 0; i < copy; i++) resized[i] = save.Party[i];
                save.Party = resized;
            }

            return save;
        }

        /// <summary>Move invested ranks from a retired kit skill onto its replacement
        /// (keeps spent points consistent; no-op if none invested or already moved).</summary>
        private static void TransferRank(Dictionary<string, int> ranks, string from, string to)
        {
            if (!ranks.TryGetValue(from, out var r) || r <= 0) { ranks.Remove(from); return; }
            ranks[to] = Math.Max(ranks.TryGetValue(to, out var existing) ? existing : 0, r);
            ranks.Remove(from);
        }
    }
}
