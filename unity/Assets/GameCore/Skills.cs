#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Active-skill loadout (Lever 3): which of a hero's known skills are slotted to auto-cast in
    /// combat, capped at <see cref="BalanceConstants.MaxActiveSkills"/>. <see cref="HeroDef.Skills"/>
    /// is the KNOWN pool; <see cref="HeroInstance.SkillLoadout"/> is the player-chosen active subset.
    /// Pure reducers (return a new SaveState), mirroring Inventory/Party. The combat sim already
    /// reads SkillLoadout, so a change takes effect on the next run / ReconcileParty.
    /// </summary>
    public static class Skills
    {
        /// <summary>The skills a hero's class knows — the selectable pool.</summary>
        public static IReadOnlyList<string> Known(HeroInstance hero, GameConfig cfg)
            => cfg.Heroes.TryGetValue(hero.DefId, out var def) ? def.Skills : (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>The default active loadout for a freshly-minted hero: the first
        /// MaxActiveSkills of its known pool, so a class with more skills than slots still
        /// starts with a full, valid bar.</summary>
        public static List<string> DefaultLoadout(HeroDef def, GameConfig cfg)
            => def.Skills.Take(Math.Max(0, cfg.Balance.MaxActiveSkills)).ToList();

        /// <summary>
        /// Toggle a known skill in a hero's active loadout: slot it if it's off (and a slot is
        /// free), or unslot it if it's on. No-op (returns the same save ref) when the skill isn't
        /// known, or when slotting it would exceed MaxActiveSkills. Pure.
        /// </summary>
        public static SaveState ToggleSkill(SaveState save, string heroId, string skillId, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId)
                ?? throw new InvalidOperationException($"ToggleSkill: hero \"{heroId}\" not owned");
            if (!Known(hero, cfg).Contains(skillId)) return save;        // not a known skill -> no-op

            var next = new List<string>(hero.SkillLoadout);
            if (next.Contains(skillId)) next.Remove(skillId);            // unslot
            else if (next.Count < cfg.Balance.MaxActiveSkills) next.Add(skillId); // slot if room
            else return save;                                            // bar full -> no-op

            return WithLoadout(save, hero, next);
        }

        /// <summary>
        /// Replace a hero's active loadout wholesale. Validates: every id is known by the hero, no
        /// duplicates, and the count is within MaxActiveSkills (given order preserved). Pure.
        /// </summary>
        public static SaveState SetLoadout(SaveState save, string heroId, IEnumerable<string> active, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId)
                ?? throw new InvalidOperationException($"SetLoadout: hero \"{heroId}\" not owned");
            var known = Known(hero, cfg);

            var list = new List<string>();
            foreach (var id in active)
            {
                if (!known.Contains(id))
                    throw new InvalidOperationException($"SetLoadout: skill \"{id}\" not known by hero \"{heroId}\"");
                if (list.Contains(id))
                    throw new InvalidOperationException($"SetLoadout: duplicate skill \"{id}\"");
                list.Add(id);
            }
            if (list.Count > cfg.Balance.MaxActiveSkills)
                throw new InvalidOperationException($"SetLoadout: {list.Count} skills exceeds cap {cfg.Balance.MaxActiveSkills}");

            return WithLoadout(save, hero, list);
        }

        private static SaveState WithLoadout(SaveState save, HeroInstance hero, List<string> loadout)
        {
            var updated = new HeroInstance
            {
                Id = hero.Id, DefId = hero.DefId, Level = hero.Level, Xp = hero.Xp,
                Equipped = hero.Equipped, SkillLoadout = loadout,
            };
            var heroes = new List<HeroInstance>(save.Heroes.Count);
            foreach (var h in save.Heroes) heroes.Add(ReferenceEquals(h, hero) ? updated : h);

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                LastClaimAt = save.LastClaimAt,
            };
        }
    }
}
