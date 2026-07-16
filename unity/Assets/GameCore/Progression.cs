#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Leveling / progression reducers — pure: each returns a NEW object, input
    /// untouched (same convention as <see cref="Party"/> / <see cref="Inventory"/>).
    /// XP semantics: Balance.XpCurve(level) is the XP needed to go from `level` to
    /// `level+1`, and HeroInstance.Xp stores the remainder toward the next level.
    /// </summary>
    public static class Progression
    {
        /// <summary>
        /// FTUE staged-reveal schedule (§7.4, user pick FAST — everything by ~S12): the highest
        /// CLEARED stage a feature is revealed at. Schedule-as-data (this table), not a switch of magic
        /// numbers, so the client and a future server read the same gate. A feature not in the table is
        /// treated as always-on (need = 0).
        /// </summary>
        public static readonly IReadOnlyDictionary<Feature, int> FeatureRevealStage = new Dictionary<Feature, int>
        {
            [Feature.AutoAdvance]  = 2,   // after the first boss
            [Feature.IdleClaim]    = 3,   // kills the offline-accrual launch popup in minute one
            [Feature.DailyLogin]   = 3,   // kills the login popup in minute one; streaks still start same session
            [Feature.Achievements] = 5,
            [Feature.Modifiers]    = 10,  // = where the first real modifier unlocks
            [Feature.Modes]        = 10,  // Tower + Crypt menu
            [Feature.Gacha]        = 12,
        };

        /// <summary>
        /// Is a HUD feature/panel revealed for this save yet (§7.4 staged reveal)? Gates by the save's
        /// highest CLEARED stage against <see cref="FeatureRevealStage"/> — reveals fire on clears.
        /// FRESH GAMES ONLY: an UNARMED save (every save that existed before FTUE, plus any save not
        /// created via New Game) returns true for everything, forever — the reveal is opt-in via the
        /// New-Game arming flag, so no existing player ever loses access. Pure; no clock, no rng.
        /// </summary>
        public static bool FeatureUnlocked(Feature feature, SaveState save)
        {
            if (!save.Progress.Intro.Armed) return true; // unarmed (pre-FTUE / non-New-Game) saves see all
            int need = FeatureRevealStage.TryGetValue(feature, out var s) ? s : 0;
            return save.Progress.HighestStage >= need;
        }

        /// <summary>Grant XP to a single hero, applying any level-ups (capped at Balance.MaxLevel).</summary>
        public static HeroInstance GrantXp(HeroInstance hero, long amount, GameConfig cfg)
        {
            int level = hero.Level;
            long xp = hero.Xp + Math.Max(0L, amount);
            int maxLevel = cfg.Balance.MaxLevel;

            while (level < maxLevel && xp >= cfg.Balance.XpCurve(level))
            {
                xp -= cfg.Balance.XpCurve(level);
                level++;
            }
            if (level >= maxLevel) { level = maxLevel; xp = 0; } // no next level past the cap

            return WithLevel(hero, level, xp);
        }

        /// <summary>Grant XP to every hero currently in the party (benched heroes don't level).</summary>
        public static SaveState GrantPartyXp(SaveState save, long amount, GameConfig cfg)
        {
            var partyIds = new HashSet<string>();
            foreach (var id in save.Party)
                if (id != null) partyIds.Add(id);

            var nextHeroes = new List<HeroInstance>(save.Heroes.Count);
            foreach (var h in save.Heroes)
                nextHeroes.Add(partyIds.Contains(h.Id) ? GrantXp(h, amount, cfg) : h);

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = nextHeroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }

        /// <summary>Credit any currency (gold, scrap, grave dust, gems…) to the account balance.
        /// Pure; no-op share for amount ≤ 0. The generic reducer behind GrantGold/GrantScrap —
        /// §7.3 dungeon-chest dust banks through this.</summary>
        public static SaveState GrantCurrency(SaveState save, string key, long amount)
        {
            if (amount <= 0) return save;

            var currencies = new Dictionary<string, long>(save.Currencies);
            currencies[key] = (currencies.TryGetValue(key, out var v) ? v : 0) + amount;

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = currencies,
                Progress = save.Progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }

        /// <summary>Credit gold to the account balance (Currencies["gold"]). Pure.</summary>
        public static SaveState GrantGold(SaveState save, long amount)
        {
            if (amount <= 0) return save;

            var currencies = new Dictionary<string, long>(save.Currencies);
            currencies["gold"] = (currencies.TryGetValue("gold", out var g) ? g : 0) + amount;

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = currencies,
                Progress = save.Progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }

        /// <summary>Credit scrap to the account balance (Currencies["scrap"]). Pure. Mirrors
        /// <see cref="GrantGold"/> — used by achievement/milestone reward payouts.</summary>
        public static SaveState GrantScrap(SaveState save, long amount)
        {
            if (amount <= 0) return save;

            var currencies = new Dictionary<string, long>(save.Currencies);
            currencies["scrap"] = (currencies.TryGetValue("scrap", out var s) ? s : 0) + amount;

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = currencies,
                Progress = save.Progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }

        /// <summary>
        /// Record a stage clear: bump HighestStage if this is the deepest yet, auto-advance
        /// CurrentStage, and grant any hero unlocks now satisfied
        /// (<see cref="GameConfig.HeroUnlocks"/>) — each acquired once and auto-fielded into
        /// the first empty party slot. Replaying an already-cleared stage is fine —
        /// HighestStage only moves forward and owned unlocks are skipped. Endless first-clears
        /// pay a gem drip every <see cref="BalanceConstants.EndlessGemsEvery"/>th NEW depth
        /// (EndlessBest advance only — Tower/crypt-style receipts, re-clears grant nothing). Pure.
        /// </summary>
        public static SaveState OnStageCleared(SaveState save, int stage, GameConfig cfg)
        {
            // HighestStage caps at the campaign table height; endless depth (stage past the
            // table) rides EndlessBest, which never regresses. CurrentStage stays uncapped so
            // the party keeps advancing into endless.
            int highest = Math.Max(save.Progress.HighestStage, Math.Min(stage, cfg.Stages.Count));
            int endlessBest = stage > cfg.Stages.Count
                ? Math.Max(save.Progress.EndlessBest, stage - cfg.Stages.Count)
                : save.Progress.EndlessBest;
            // A NEW endless depth that lands on the every-Nth milestone pays gems AND (10.17) a drip of
            // UNIVERSAL ascension shards — the base roster's shard source (they never roll gacha dupes).
            bool milestone = endlessBest > save.Progress.EndlessBest
                             && endlessBest % Math.Max(1, cfg.Balance.EndlessGemsEvery) == 0;
            long gems = milestone ? cfg.Balance.EndlessGemsPerMilestone : 0;
            long shards = milestone ? cfg.Balance.AscensionShardsPerEndlessMilestone : 0;
            var next = WithProgress(save, new ProgressState
            {
                HighestStage = highest,
                CurrentStage = stage + 1,
                AccountLevel = save.Progress.AccountLevel,
                EndlessBest = endlessBest,
                Tower = save.Progress.Tower,
                Achievements = save.Progress.Achievements,
                Daily = save.Progress.Daily,
                Crypt = save.Progress.Crypt,
                Intro = save.Progress.Intro,
                Loot = save.Progress.Loot,
                Codex = save.Progress.Codex,
                Season = save.Progress.Season,
                Ascension = save.Progress.Ascension,
            }, gems, cfg, shards);

            next = SyncHeroUnlocks(next, cfg);

            // Farm depth drives the Modifiers (Lever 1): unlock/upgrade owned modifiers for the new
            // highest stage. Idempotent, so replaying a cleared stage is a no-op.
            return Modifiers.SyncToStage(next, cfg);
        }

        /// <summary>
        /// Align the owned roster to the unlock table for the save's HighestStage:
        /// every unlock at or below it is granted (level 1, no gear, auto-fielded into
        /// the first empty slot) — retroactive, so a table change reaches existing
        /// saves at load, not just on the next boss clear. Heroes whose def has LEFT
        /// the table (disabled content, e.g. the shelved Ice Mage) are removed from
        /// the roster and their party slot freed. Idempotent; the starter is exempt.
        /// </summary>
        public static SaveState SyncHeroUnlocks(SaveState save, GameConfig cfg)
        {
            var next = save;

            // Sorted so multi-unlock grants mint hero ids deterministically.
            foreach (var unlock in cfg.HeroUnlocks.OrderBy(u => u.Key))
            {
                if (unlock.Key > next.Progress.HighestStage) continue;           // not reached yet
                if (next.Heroes.Exists(h => h.DefId == unlock.Value)) continue;  // already owned

                string heroId = "h" + (next.Heroes.Count + 1);
                while (next.Heroes.Exists(h => h.Id == heroId)) heroId += "x";   // removed rows leave id gaps
                next = Party.AcquireHero(next, unlock.Value, cfg, heroId);
                int empty = Array.IndexOf(next.Party, (string?)null);
                if (empty >= 0) next = Party.FieldHero(next, empty, heroId);      // join the party
            }

            // Disabled content: obtainable = starter + the unlock table + every hero on a live gacha
            // banner's pool. A gacha-granted hero has NO stage unlock (roadmap 3), so without the banner
            // pools here the next SyncHeroUnlocks would SHELVE it (the same removal path that shelves the
            // Ice Mage). Keying off the banner pool — not a per-hero "granted" flag — keeps the hero
            // obtainable exactly as long as its banner exists, needs no save-schema change, and is
            // idempotent. Slice 1 ships no live banner, so this set is unchanged for existing content.
            var obtainable = new HashSet<string>(cfg.HeroUnlocks.Values) { Save.StarterHeroDef };
            foreach (var banner in cfg.Banners.Values)
                foreach (var entry in banner.Pool)
                    obtainable.Add(entry.HeroDefId);
            if (next.Heroes.Exists(h => !obtainable.Contains(h.DefId)))
            {
                var gone = new HashSet<string>();
                var kept = new List<HeroInstance>();
                foreach (var h in next.Heroes)
                    if (obtainable.Contains(h.DefId)) kept.Add(h); else gone.Add(h.Id);
                var party = (string?[])next.Party.Clone();
                for (int i = 0; i < party.Length; i++)
                    if (party[i] != null && gone.Contains(party[i]!)) party[i] = null;
                string? leader = next.LeaderHeroId != null && gone.Contains(next.LeaderHeroId)
                    ? null : next.LeaderHeroId;
                next = new SaveState
                {
                    Version = next.Version,
                    RngSeed = next.RngSeed,
                    RngCursor = next.RngCursor,
                    Heroes = kept,
                    Party = party,
                    LeaderHeroId = leader,
                    Inventory = next.Inventory,
                    Currencies = next.Currencies,
                    Progress = next.Progress,
                    Quests = next.Quests,
                    Modifiers = next.Modifiers,
                    GachaPity = next.GachaPity,
                    LastClaimAt = next.LastClaimAt,
                };
                if (Array.TrueForAll(next.Party, s => s == null) && next.Heroes.Count > 0)
                    next = Party.FieldHero(next, 0, next.Heroes[0].Id); // never leave an empty field
            }
            return next;
        }

        /// <summary>
        /// Select the stage to play (farm or retry). Within the campaign the valid range is
        /// 1 ≤ stage ≤ HighestStage + 1 (attempt the next uncleared stage, can't skip ahead),
        /// capped to the number of defined stages. Once the campaign is complete
        /// (HighestStage ≥ Stages.Count) endless opens: the ceiling becomes
        /// Stages.Count + EndlessBest + 1 (the next uncleared endless depth).
        /// </summary>
        public static SaveState SetStage(SaveState save, int stage, GameConfig cfg)
        {
            int maxSelectable = MaxSelectableStage(save, cfg);
            if (stage < 1 || stage > maxSelectable)
                throw new ArgumentOutOfRangeException(nameof(stage),
                    $"SetStage: {stage} out of range (1..{maxSelectable})");

            return WithProgress(save, new ProgressState
            {
                HighestStage = save.Progress.HighestStage,
                CurrentStage = stage,
                AccountLevel = save.Progress.AccountLevel,
                EndlessBest = save.Progress.EndlessBest,
                Tower = save.Progress.Tower,
                Achievements = save.Progress.Achievements,
                Daily = save.Progress.Daily,
                Crypt = save.Progress.Crypt,
                Intro = save.Progress.Intro,
                Loot = save.Progress.Loot,
                Codex = save.Progress.Codex,
                Season = save.Progress.Season,
                Ascension = save.Progress.Ascension,
            });
        }

        /// <summary>The deepest stage <see cref="SetStage"/> accepts — the ONE selection rule,
        /// shared with the client's stage nav so the ▶ arrow and the reducer can't drift apart.
        /// Campaign: HighestStage + 1, capped to the table. Campaign complete: the next
        /// uncleared endless depth (Stages.Count + EndlessBest + 1).</summary>
        public static int MaxSelectableStage(SaveState save, GameConfig cfg) =>
            save.Progress.HighestStage >= cfg.Stages.Count
                ? cfg.Stages.Count + save.Progress.EndlessBest + 1
                : Math.Min(save.Progress.HighestStage + 1, cfg.Stages.Count);

        // Clone the save with a new ProgressState; everything else shares refs. A non-zero gems and/or
        // shards credit clones the currencies dict too (the Tower.WithFloor premium pattern). Shards are
        // UNIVERSAL ascension shards (10.17), paid on the same endless milestone as the gems.
        private static SaveState WithProgress(SaveState save, ProgressState progress,
                                              long gems = 0, GameConfig? cfg = null, long shards = 0)
        {
            var currencies = save.Currencies;
            if ((gems != 0 || shards != 0) && cfg != null)
            {
                currencies = new Dictionary<string, long>(save.Currencies);
                if (gems != 0)
                {
                    string key = cfg.Balance.PremiumCurrency;
                    currencies[key] = (currencies.TryGetValue(key, out var v) ? v : 0) + gems;
                }
                if (shards != 0)
                    currencies[Ascension.UniversalShardCurrency] =
                        (currencies.TryGetValue(Ascension.UniversalShardCurrency, out var sv) ? sv : 0) + shards;
            }
            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = currencies,
                Progress = progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }

        // Equipped / SkillRanks / Loadout are unchanged here, so the new hero shares those refs.
        private static HeroInstance WithLevel(HeroInstance hero, int level, long xp) => new HeroInstance
        {
            Id = hero.Id,
            DefId = hero.DefId,
            Level = level,
            Xp = xp,
            Equipped = hero.Equipped,
            SkillRanks = hero.SkillRanks,
            Loadout = hero.Loadout,
            Stars = hero.Stars,
        };
    }
}
