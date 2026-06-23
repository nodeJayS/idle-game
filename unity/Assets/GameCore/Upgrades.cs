#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// At-a-glance loot legibility (Lever 2): collapse a candidate item's full stat swap into a
    /// single "is this better, and for whom?" verdict so the bag can badge drops ▲/=/▼ without the
    /// player hovering every item on the Heroes screen. Pure GameCore — it layers on
    /// <see cref="Inventory.ComparePairForHero"/> (the before/after stat blocks) and
    /// <see cref="DerivedStats"/> (DPS / Effective-Life), so a drop's worth is judged by the same
    /// combat math the sim runs. Also powers optional auto-equip-if-better.
    /// </summary>
    public static class Upgrades
    {
        public enum Verdict { Downgrade, Sidegrade, Upgrade }

        /// <summary>One hero's read on a candidate item: the power swap, the % change, and the
        /// banded verdict. <see cref="DeltaPercent"/> is the headline number the badge shows.</summary>
        public sealed class ItemEval
        {
            public string HeroId = "";
            public double BeforePower;
            public double AfterPower;
            public double DeltaPercent;  // (after − before) / before; 0.05 = +5% power
            public Verdict Verdict;
        }

        /// <summary>
        /// One legible power scalar for a stat block: the geometric mean of offense (DPS) and
        /// survivability (Effective Life vs the stage's reference hit). Geometric (not additive) so
        /// neither half can be ignored — doubling DPS while halving Eff-Life is a wash — which makes
        /// "+X% power" honest about glass-cannon swaps. Mirrors the sim's own derived readouts.
        /// </summary>
        public static double PowerScore(StatBlock s, GameConfig cfg, int stage)
        {
            double dps = Math.Max(0.0, DerivedStats.Dps(s));
            double ehp = Math.Max(0.0, DerivedStats.EffectiveHp(s, cfg, stage));
            return Math.Sqrt(dps * ehp);
        }

        /// <summary>
        /// Evaluate equipping <paramref name="candidate"/> on one hero (swapping into its slot):
        /// the power before/after, the % delta, and a verdict banded by
        /// <see cref="BalanceConstants.UpgradeBandPct"/> (tiny swings read as Sidegrade so noise
        /// doesn't masquerade as an upgrade). <paramref name="stage"/> sets the survivability
        /// reference — pass the stage the player is on.
        /// </summary>
        public static ItemEval EvaluateForHero(SaveState save, string heroId, Item candidate, GameConfig cfg, int stage)
        {
            var (before, after) = Inventory.ComparePairForHero(save, heroId, candidate, cfg);
            double bp = PowerScore(before, cfg, stage);
            double ap = PowerScore(after, cfg, stage);
            double delta = bp > 0 ? (ap - bp) / bp : (ap > 0 ? 1.0 : 0.0);

            double band = cfg.Balance.UpgradeBandPct;
            var verdict = delta > band ? Verdict.Upgrade
                        : delta < -band ? Verdict.Downgrade
                        : Verdict.Sidegrade;

            return new ItemEval { HeroId = heroId, BeforePower = bp, AfterPower = ap, DeltaPercent = delta, Verdict = verdict };
        }

        /// <summary>
        /// The best hero to give this item to: the one who gains the most power (highest
        /// <see cref="ItemEval.DeltaPercent"/>). Returns null only when there are no candidate heroes.
        /// <paramref name="heroIds"/> scopes the search (e.g. fielded party only); null = every owned
        /// hero. The result can still be a Sidegrade/Downgrade — callers gate on
        /// <see cref="ItemEval.Verdict"/> for "is it an upgrade for anyone?".
        /// </summary>
        public static ItemEval? BestForItem(SaveState save, Item candidate, GameConfig cfg, int stage,
                                            IEnumerable<string>? heroIds = null)
        {
            ItemEval? best = null;
            foreach (var id in heroIds ?? AllHeroIds(save))
            {
                if (save.Heroes.Find(h => h.Id == id) == null) continue; // skip empty party slots / stale ids
                var eval = EvaluateForHero(save, id, candidate, cfg, stage);
                if (best == null || eval.DeltaPercent > best.DeltaPercent) best = eval;
            }
            return best;
        }

        /// <summary>
        /// Equip <paramref name="item"/> on whichever candidate hero gains the most — but only if
        /// it's a genuine Upgrade for them. No-op (returns the input save, null eval) otherwise, so
        /// it's safe to fire on every drop. The item must already be in the bag. Pure: returns a new
        /// save when it equips.
        /// </summary>
        public static (SaveState save, ItemEval? equipped) AutoEquipIfBetter(SaveState save, Item item, GameConfig cfg, int stage,
                                                                             IEnumerable<string>? heroIds = null)
        {
            var best = BestForItem(save, item, cfg, stage, heroIds);
            if (best == null || best.Verdict != Verdict.Upgrade) return (save, null);
            return (Inventory.EquipItem(save, best.HeroId, item.Id, cfg), best);
        }

        private static IEnumerable<string> AllHeroIds(SaveState save)
        {
            foreach (var h in save.Heroes) yield return h.Id;
        }
    }
}
