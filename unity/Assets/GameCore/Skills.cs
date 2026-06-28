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
        /// <summary>The skills a hero's class knows — the selectable pool (actives + passives).</summary>
        public static IReadOnlyList<string> Known(HeroInstance hero, GameConfig cfg)
            => cfg.Heroes.TryGetValue(hero.DefId, out var def) ? def.Skills : (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>Is this a passive node (always-on stat, never cast / never slotted)?</summary>
        public static bool IsPassive(string skillId, GameConfig cfg)
            => cfg.Skills.TryGetValue(skillId, out var def) && def.Passive;

        /// <summary>Known active skills — the subset eligible for the active bar.</summary>
        public static IReadOnlyList<string> KnownActive(HeroInstance hero, GameConfig cfg)
            => Known(hero, cfg).Where(id => !IsPassive(id, cfg)).ToList();

        /// <summary>Known passive nodes — ranked for stats, never slotted in the active bar.</summary>
        public static IReadOnlyList<string> KnownPassive(HeroInstance hero, GameConfig cfg)
            => Known(hero, cfg).Where(id => IsPassive(id, cfg)).ToList();

        /// <summary>The default active loadout for a freshly-minted hero: the first
        /// MaxActiveSkills of its known ACTIVE skills, so a class with more skills than slots still
        /// starts with a full, valid bar (passives never occupy an active slot).</summary>
        public static List<string> DefaultLoadout(HeroDef def, GameConfig cfg)
            => def.Skills.Where(id => !IsPassive(id, cfg)).Take(Math.Max(0, cfg.Balance.MaxActiveSkills)).ToList();

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
            if (IsPassive(skillId, cfg)) return save;                    // passives can't be slotted

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
                if (IsPassive(id, cfg))
                    throw new InvalidOperationException($"SetLoadout: skill \"{id}\" is passive and cannot be slotted");
                if (list.Contains(id))
                    throw new InvalidOperationException($"SetLoadout: duplicate skill \"{id}\"");
                list.Add(id);
            }
            if (list.Count > cfg.Balance.MaxActiveSkills)
                throw new InvalidOperationException($"SetLoadout: {list.Count} skills exceeds cap {cfg.Balance.MaxActiveSkills}");

            return WithLoadout(save, hero, list);
        }

        // ---- Skill ranks (Lever 3 build depth): spend level-earned points to rank up skills ----

        /// <summary>Total skill points a hero has earned: one per level gained
        /// (<see cref="BalanceConstants.SkillPointsPerLevel"/>). Level 1 = 0.</summary>
        public static int PointsEarned(HeroInstance hero, GameConfig cfg)
            => Math.Max(0, hero.Level - 1) * cfg.Balance.SkillPointsPerLevel;

        /// <summary>Points already invested across all of a hero's skills.</summary>
        public static int PointsSpent(HeroInstance hero)
        {
            int n = 0;
            foreach (var v in hero.SkillRanks.Values) n += v;
            return n;
        }

        /// <summary>Points available to invest (earned − spent). Derived, never persisted.</summary>
        public static int UnspentPoints(HeroInstance hero, GameConfig cfg)
            => PointsEarned(hero, cfg) - PointsSpent(hero);

        /// <summary>A skill's invested rank (0 if never invested).</summary>
        public static int RankOf(HeroInstance hero, string skillId)
            => hero.SkillRanks.TryGetValue(skillId, out var r) ? r : 0;

        /// <summary>Can this hero invest a point into <paramref name="skillId"/> right now? Requires
        /// the skill be known, below its <see cref="SkillDef.MaxRank"/>, and an unspent point.</summary>
        public static bool CanInvest(SaveState save, string heroId, string skillId, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero == null) return false;
            if (!Known(hero, cfg).Contains(skillId)) return false;
            if (!cfg.Skills.TryGetValue(skillId, out var def)) return false;
            if (RankOf(hero, skillId) >= def.MaxRank) return false;
            return UnspentPoints(hero, cfg) > 0;
        }

        /// <summary>Spend one point to raise a skill's rank by 1. No-op (same save ref) when
        /// <see cref="CanInvest"/> is false. Pure.</summary>
        public static SaveState InvestSkill(SaveState save, string heroId, string skillId, GameConfig cfg)
        {
            if (!CanInvest(save, heroId, skillId, cfg)) return save;
            var hero = save.Heroes.Find(h => h.Id == heroId)!;

            var next = new Dictionary<string, int>(hero.SkillRanks);
            next[skillId] = RankOf(hero, skillId) + 1;
            return WithRanks(save, hero, next);
        }

        /// <summary>Refund every invested point (clear all ranks) — free respec, points become
        /// re-spendable. No-op when nothing is invested. Pure.</summary>
        public static SaveState RespecHero(SaveState save, string heroId, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId)
                ?? throw new InvalidOperationException($"RespecHero: hero \"{heroId}\" not owned");
            if (hero.SkillRanks.Count == 0) return save;
            return WithRanks(save, hero, new Dictionary<string, int>());
        }

        private static SaveState WithRanks(SaveState save, HeroInstance hero, Dictionary<string, int> ranks)
        {
            var updated = new HeroInstance
            {
                Id = hero.Id, DefId = hero.DefId, Level = hero.Level, Xp = hero.Xp,
                Equipped = hero.Equipped, SkillLoadout = hero.SkillLoadout, SkillRanks = ranks,
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
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                LastClaimAt = save.LastClaimAt,
            };
        }

        private static SaveState WithLoadout(SaveState save, HeroInstance hero, List<string> loadout)
        {
            var updated = new HeroInstance
            {
                Id = hero.Id, DefId = hero.DefId, Level = hero.Level, Xp = hero.Xp,
                Equipped = hero.Equipped, SkillLoadout = loadout, SkillRanks = hero.SkillRanks,
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
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                LastClaimAt = save.LastClaimAt,
            };
        }
    }
}
