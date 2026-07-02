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
                LastClaimAt = save.LastClaimAt,
            };
        }

        /// <summary>
        /// Record a stage clear: bump HighestStage if this is the deepest yet, auto-advance
        /// CurrentStage, and grant any hero unlocks now satisfied
        /// (<see cref="GameConfig.HeroUnlocks"/>) — each acquired once and auto-fielded into
        /// the first empty party slot. Replaying an already-cleared stage is fine —
        /// HighestStage only moves forward and owned unlocks are skipped. Pure.
        /// </summary>
        public static SaveState OnStageCleared(SaveState save, int stage, GameConfig cfg)
        {
            int highest = Math.Max(save.Progress.HighestStage, stage);
            var next = WithProgress(save, new ProgressState
            {
                HighestStage = highest,
                CurrentStage = stage + 1,
                AccountLevel = save.Progress.AccountLevel,
                Tower = save.Progress.Tower,
                Achievements = save.Progress.Achievements,
                Daily = save.Progress.Daily,
            });

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

            // Disabled content: obtainable = starter + the unlock table.
            var obtainable = new HashSet<string>(cfg.HeroUnlocks.Values) { Save.StarterHeroDef };
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
                    LastClaimAt = next.LastClaimAt,
                };
                if (Array.TrueForAll(next.Party, s => s == null) && next.Heroes.Count > 0)
                    next = Party.FieldHero(next, 0, next.Heroes[0].Id); // never leave an empty field
            }
            return next;
        }

        /// <summary>
        /// Select the stage to play (farm or retry). Valid range is
        /// 1 ≤ stage ≤ HighestStage + 1 (you may attempt the next uncleared stage but
        /// can't skip ahead), further capped to the number of defined stages.
        /// </summary>
        public static SaveState SetStage(SaveState save, int stage, GameConfig cfg)
        {
            int maxSelectable = Math.Min(save.Progress.HighestStage + 1, cfg.Stages.Count);
            if (stage < 1 || stage > maxSelectable)
                throw new ArgumentOutOfRangeException(nameof(stage),
                    $"SetStage: {stage} out of range (1..{maxSelectable})");

            return WithProgress(save, new ProgressState
            {
                HighestStage = save.Progress.HighestStage,
                CurrentStage = stage,
                AccountLevel = save.Progress.AccountLevel,
                Tower = save.Progress.Tower,
                Achievements = save.Progress.Achievements,
                Daily = save.Progress.Daily,
            });
        }

        // Clone the save with a new ProgressState; everything else shares refs.
        private static SaveState WithProgress(SaveState save, ProgressState progress) => new SaveState
        {
            Version = save.Version,
            RngSeed = save.RngSeed,
            RngCursor = save.RngCursor,
            Heroes = save.Heroes,
            Party = save.Party,
            LeaderHeroId = save.LeaderHeroId,
            Inventory = save.Inventory,
            Currencies = save.Currencies,
            Progress = progress,
            Quests = save.Quests,
            Modifiers = save.Modifiers,
            LastClaimAt = save.LastClaimAt,
        };

        // Equipped / SkillRanks are unchanged here, so the new hero shares those refs.
        private static HeroInstance WithLevel(HeroInstance hero, int level, long xp) => new HeroInstance
        {
            Id = hero.Id,
            DefId = hero.DefId,
            Level = level,
            Xp = xp,
            Equipped = hero.Equipped,
            SkillRanks = hero.SkillRanks,
        };
    }
}
