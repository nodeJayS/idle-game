#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>Derives effective stats from base + growth*level + equipped gear.</summary>
    public static class Stats
    {
        /// <summary>
        /// Effective stats = hero def base + growthPerLevel*(level-1) + the sum of
        /// each equipped item's base stats and affixes.
        /// `equipped` is the resolved list of Items the hero has on (caller looks
        /// them up; full item-storage resolution lands in M2).
        /// </summary>
        public static StatBlock ComputeHeroStats(HeroInstance hero, GameConfig cfg, IReadOnlyList<Item> equipped)
        {
            if (!cfg.Heroes.TryGetValue(hero.DefId, out var def))
                throw new InvalidOperationException($"ComputeHeroStats: unknown hero def \"{hero.DefId}\"");

            var result = new StatBlock();
            foreach (var kv in def.BaseStats) result[kv.Key] = kv.Value;

            int levels = Math.Max(0, hero.Level - 1);
            foreach (var kv in def.GrowthPerLevel)
                result[kv.Key] = result.Get(kv.Key) + kv.Value * levels;

            foreach (var item in equipped)
            {
                if (cfg.ItemBases.TryGetValue(item.BaseId, out var baseDef))
                    foreach (var kv in baseDef.BaseStats)
                        result[kv.Key] = result.Get(kv.Key) + kv.Value;

                foreach (var affix in item.Affixes)
                    result[affix.Stat] = result.Get(affix.Stat) + affix.Value;
            }

            // Passive skill nodes: each invested rank adds StatPerRank to its PassiveStat, with the
            // MaxRank mastery bump (§7.2) via EffectiveRank. This is the single stat seam, so a
            // ranked passive flows on into DerivedStats, DPS/Eff-Life, and the Lever 2 power
            // compare automatically. Rank 0 = +0.
            foreach (var skillId in def.Skills)
            {
                if (!cfg.Skills.TryGetValue(skillId, out var sk) || !sk.Passive) continue;
                int rank = hero.SkillRanks.TryGetValue(skillId, out var r) ? r : 0;
                if (rank <= 0) continue;
                result[sk.PassiveStat] = result.Get(sk.PassiveStat) + sk.StatPerRank * Skills.EffectiveRank(rank, sk, cfg);
            }

            return result;
        }

        /// <summary>Convenience overload for a hero with no resolved gear.</summary>
        public static StatBlock ComputeHeroStats(HeroInstance hero, GameConfig cfg)
            => ComputeHeroStats(hero, cfg, Array.Empty<Item>());

        /// <summary>
        /// Resolve a hero's equipped slot→itemId references into the actual Items by
        /// looking them up in the inventory pool. Lives here (not Inventory) so Stats
        /// has no dependency back on Inventory. Ids with no matching item are skipped.
        /// </summary>
        public static IReadOnlyList<Item> ResolveEquipped(SaveState save, HeroInstance hero)
        {
            var items = new List<Item>(hero.Equipped.Count);
            foreach (var itemId in hero.Equipped.Values)
            {
                var item = save.Inventory.Find(i => i.Id == itemId);
                if (item != null) items.Add(item);
            }
            return items;
        }

        /// <summary>A single rolled-up "power" number for UI / future matchmaking.</summary>
        public static double ComputePartyPower(SaveState save, GameConfig cfg)
        {
            double power = 0;
            foreach (var heroId in save.Party)
            {
                if (heroId == null) continue;
                var hero = save.Heroes.Find(h => h.Id == heroId);
                if (hero == null) continue;
                var st = ComputeHeroStats(hero, cfg, ResolveEquipped(save, hero));
                power += st.Get(StatKey.Hp) * 0.5
                       + st.Get(StatKey.Atk) * 5.0
                       + st.Get(StatKey.Def) * 3.0;
            }
            return power;
        }
    }
}
