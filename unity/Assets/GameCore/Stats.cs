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
                {
                    // enhancement (+N) multiplies the BASE's stats only; affixes as rolled
                    double enh = 1.0 + cfg.Balance.EnhanceBasePctPerLevel * item.Enhance;
                    foreach (var kv in baseDef.BaseStats)
                        result[kv.Key] = result.Get(kv.Key) + kv.Value * enh;
                }

                foreach (var affix in item.Affixes)
                    result[affix.Stat] = result.Get(affix.Stat) + affix.Value;
            }

            // Set bonuses (§6.2): count equipped pieces per set — 2 add Piece2, 4 add Piece4
            // ON TOP (2pc stays active at 4). Flat adds through the same seam as affixes, so
            // DerivedStats / PowerScore / CompareCard price sets with zero extra wiring.
            Dictionary<string, int>? setCounts = null;
            foreach (var item in equipped)
                if (item.SetId != null)
                {
                    setCounts ??= new Dictionary<string, int>();
                    setCounts[item.SetId] = (setCounts.TryGetValue(item.SetId, out var n) ? n : 0) + 1;
                }
            if (setCounts != null)
                foreach (var kv in setCounts)
                {
                    if (!cfg.Sets.TryGetValue(kv.Key, out var set)) continue; // stale id: content trimmed
                    if (kv.Value >= 2)
                        foreach (var s in set.Piece2) result[s.Key] = result.Get(s.Key) + s.Value;
                    if (kv.Value >= 4)
                        foreach (var s in set.Piece4) result[s.Key] = result.Get(s.Key) + s.Value;
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

            // Ascension stars (10.17): a HERO-LOCAL multiplier on core power — +AscensionStarPct Hp/Atk/Def
            // per star, folded HERE in the per-hero build (deliberately unlike the Tower/codex ACCOUNT
            // buffs, which fold in Combat.RefreshPartyStats across the whole party). Applied last, over
            // gear + set + passive totals, so a star scales the hero's fully-built power. 0 stars = no-op.
            if (hero.Stars > 0)
            {
                double starMult = 1.0 + hero.Stars * cfg.Balance.AscensionStarPct;
                result[StatKey.Hp] = result.Get(StatKey.Hp) * starMult;
                result[StatKey.Atk] = result.Get(StatKey.Atk) * starMult;
                result[StatKey.Def] = result.Get(StatKey.Def) * starMult;
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
