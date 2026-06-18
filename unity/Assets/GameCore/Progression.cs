#nullable enable
using System;
using System.Collections.Generic;

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
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                LastClaimAt = save.LastClaimAt,
            };
        }

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
