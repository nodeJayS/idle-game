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
        public static HeroInstance GrantXp(HeroInstance hero, int amount, GameConfig cfg)
        {
            int level = hero.Level;
            long xp = hero.Xp + Math.Max(0, amount);
            int maxLevel = cfg.Balance.MaxLevel;

            while (level < maxLevel && xp >= cfg.Balance.XpCurve(level))
            {
                xp -= cfg.Balance.XpCurve(level);
                level++;
            }
            if (level >= maxLevel) { level = maxLevel; xp = 0; } // no next level past the cap

            return WithLevel(hero, level, (int)xp);
        }

        /// <summary>Grant XP to every hero currently in the party (benched heroes don't level).</summary>
        public static SaveState GrantPartyXp(SaveState save, int amount, GameConfig cfg)
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
            });

            // Sorted so multi-unlock grants mint hero ids deterministically.
            foreach (var unlock in cfg.HeroUnlocks.OrderBy(u => u.Key))
            {
                if (unlock.Key > highest) continue;                              // not reached yet
                if (next.Heroes.Exists(h => h.DefId == unlock.Value)) continue;  // already owned

                string heroId = "h" + (next.Heroes.Count + 1);
                next = Party.AcquireHero(next, unlock.Value, cfg, heroId);
                int empty = Array.IndexOf(next.Party, (string?)null);
                if (empty >= 0) next = Party.FieldHero(next, empty, heroId);      // join the party
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
            LastClaimAt = save.LastClaimAt,
        };

        // Equipped / SkillLoadout are unchanged here, so the new hero shares those refs.
        private static HeroInstance WithLevel(HeroInstance hero, int level, int xp) => new HeroInstance
        {
            Id = hero.Id,
            DefId = hero.DefId,
            Level = level,
            Xp = xp,
            Equipped = hero.Equipped,
            SkillLoadout = hero.SkillLoadout,
        };
    }
}
