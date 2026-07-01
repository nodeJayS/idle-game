#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace IdleGame.GameCore
{
    /// <summary>
    /// The 2+2 hero kit (design §7.2): every hero has exactly 2 active + 2 passive skills
    /// (<see cref="HeroDef.Skills"/>), revealed by <see cref="SkillDef.UnlockLevel"/> and always
    /// on once revealed — there is no loadout choice and no prereq tree. Build depth is point
    /// ORDERING: 1 point per <see cref="BalanceConstants.SkillPointsEveryLevels"/> hero levels
    /// (derived, never persisted), each skill capped at its MaxRank; at MaxRank the skill
    /// masters (<see cref="EffectiveRank"/>). Pure reducers, mirroring Inventory/Party.
    /// </summary>
    public static class Skills
    {
        /// <summary>The hero's full kit — 2 actives + 2 passives, including not-yet-revealed ones.</summary>
        public static IReadOnlyList<string> Known(HeroInstance hero, GameConfig cfg)
            => cfg.Heroes.TryGetValue(hero.DefId, out var def) ? def.Skills : (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>Is this a passive node (always-on stat, never cast)?</summary>
        public static bool IsPassive(string skillId, GameConfig cfg)
            => cfg.Skills.TryGetValue(skillId, out var def) && def.Passive;

        /// <summary>The kit's active skills (revealed or not) — for the hero sheet.</summary>
        public static IReadOnlyList<string> KnownActive(HeroInstance hero, GameConfig cfg)
            => Known(hero, cfg).Where(id => !IsPassive(id, cfg)).ToList();

        /// <summary>The kit's passive nodes (revealed or not) — for the hero sheet.</summary>
        public static IReadOnlyList<string> KnownPassive(HeroInstance hero, GameConfig cfg)
            => Known(hero, cfg).Where(id => IsPassive(id, cfg)).ToList();

        /// <summary>
        /// The actives this hero casts in combat RIGHT NOW: the kit's active skills whose
        /// UnlockLevel the hero has reached. This is what combat entities carry (there is no
        /// player-chosen loadout); leveling past an UnlockLevel adds the skill on the next
        /// party refresh/reconcile.
        /// </summary>
        public static List<string> ActiveKit(HeroInstance hero, GameConfig cfg)
            => Known(hero, cfg).Where(id => !IsPassive(id, cfg) && IsUnlocked(hero, id, cfg)).ToList();

        // ---- Skill points: spend level-earned points to rank up the kit ----

        /// <summary>Total skill points earned: one per <see cref="BalanceConstants.SkillPointsEveryLevels"/>
        /// full hero levels (level 5 = 1 point … level 100 = 20 = exactly a maxed 2+2 kit).</summary>
        public static int PointsEarned(HeroInstance hero, GameConfig cfg)
            => hero.Level / Math.Max(1, cfg.Balance.SkillPointsEveryLevels);

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

        /// <summary>
        /// Mastery (§7.2): the rank a skill's effect actually scales by. Below MaxRank it is the
        /// invested rank; AT MaxRank the skill masters and counts as
        /// rank + <see cref="BalanceConstants.MasteryBonusRanks"/> — the chunky payoff for pushing
        /// one skill to the cap instead of spreading points evenly.
        /// </summary>
        public static int EffectiveRank(int rank, SkillDef def, GameConfig cfg)
            => rank >= def.MaxRank ? rank + Math.Max(0, cfg.Balance.MasteryBonusRanks) : rank;

        /// <summary>Is this kit skill revealed (hero at/above its UnlockLevel)? Gates both casting
        /// (<see cref="ActiveKit"/>) and point investment. No prereqs — the kit is flat.</summary>
        public static bool IsUnlocked(HeroInstance hero, string skillId, GameConfig cfg)
            => cfg.Skills.TryGetValue(skillId, out var def) && hero.Level >= def.UnlockLevel;

        /// <summary>Can this hero invest a point into <paramref name="skillId"/> right now? Requires
        /// the skill be in the kit, revealed (<see cref="IsUnlocked"/>), below its
        /// <see cref="SkillDef.MaxRank"/>, and an unspent point.</summary>
        public static bool CanInvest(SaveState save, string heroId, string skillId, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero == null) return false;
            if (!Known(hero, cfg).Contains(skillId)) return false;
            if (!cfg.Skills.TryGetValue(skillId, out var def)) return false;
            if (!IsUnlocked(hero, skillId, cfg)) return false;
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
                Equipped = hero.Equipped, SkillRanks = ranks,
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
