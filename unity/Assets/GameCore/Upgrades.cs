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
            public double DeltaPercent;  // (after − before) / before; 0.05 = +5% power (blended, for badges)
            public Verdict Verdict;

            // Auto-equip ranking axes (design: damage gain wins; effective-life gain is the fallback).
            // These decompose the SAME before/after blocks the blended PowerScore averages, so the
            // one-verdict badge and where auto-equip sends the item never contradict each other.
            public double DamageDelta;  // after DPS − before DPS
            public double EhpDelta;     // after Effective-Life − before Effective-Life
        }

        /// <summary>
        /// One legible power scalar for a stat block: the geometric mean of offense (DPS) and
        /// survivability (Effective Life vs the stage's reference hit). Geometric (not additive) so
        /// neither half can be ignored — doubling DPS while halving Eff-Life is a wash — which makes
        /// "+X% power" honest about glass-cannon swaps. Mirrors the sim's own derived readouts.
        /// </summary>
        public static double PowerScore(StatBlock s, GameConfig cfg, int stage)
        {
            var (dps, ehp) = PowerParts(s, cfg, stage);
            return Math.Sqrt(dps * ehp);
        }

        /// <summary>
        /// The two halves the blended <see cref="PowerScore"/> geometric-means: offense (DPS) and
        /// survivability (Effective Life). Exposed so auto-equip can rank on DAMAGE first and fall
        /// back to effective-life — using the exact same components as the badge, never a parallel
        /// formula — while <see cref="PowerScore"/> keeps the blended number for ▲ verdicts.
        /// </summary>
        public static (double dps, double ehp) PowerParts(StatBlock s, GameConfig cfg, int stage)
        {
            double dps = Math.Max(0.0, DerivedStats.Dps(s));
            double ehp = Math.Max(0.0, DerivedStats.EffectiveHp(s, cfg, stage));
            return (dps, ehp);
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
            var (beforeDps, beforeEhp) = PowerParts(before, cfg, stage);
            var (afterDps, afterEhp) = PowerParts(after, cfg, stage);
            double bp = Math.Sqrt(beforeDps * beforeEhp);
            double ap = Math.Sqrt(afterDps * afterEhp);
            double delta = bp > 0 ? (ap - bp) / bp : (ap > 0 ? 1.0 : 0.0);

            double band = cfg.Balance.UpgradeBandPct;
            var verdict = delta > band ? Verdict.Upgrade
                        : delta < -band ? Verdict.Downgrade
                        : Verdict.Sidegrade;

            return new ItemEval
            {
                HeroId = heroId,
                BeforePower = bp,
                AfterPower = ap,
                DeltaPercent = delta,
                Verdict = verdict,
                DamageDelta = afterDps - beforeDps,
                EhpDelta = afterEhp - beforeEhp,
            };
        }

        /// <summary>
        /// The best hero to give this item to. Ranking (design directive): equip where the item
        /// produces the largest DAMAGE gain; only if NO candidate hero gains damage from it does it
        /// fall back to the largest EFFECTIVE-LIFE gain. Ties break on the blended
        /// <see cref="ItemEval.DeltaPercent"/> then hero order, so the result is deterministic.
        ///
        /// One-verdict guarantee (CLAUDE.md): if the item is a genuine <see cref="Verdict.Upgrade"/>
        /// for ANY candidate hero, the returned eval is one of those upgraders — so the ▲ badge/feed
        /// (which reads this same call) and auto-equip can never disagree about whether a drop is an
        /// upgrade or who it goes to. Only when nobody sees an upgrade does it fall through to ranking
        /// the whole party (so the panel can still show the best-fit Sidegrade/Downgrade). Returns
        /// null only when there are no candidate heroes. <paramref name="heroIds"/> scopes the search
        /// (e.g. fielded party only); null = every owned hero.
        /// </summary>
        public static ItemEval? BestForItem(SaveState save, Item candidate, GameConfig cfg, int stage,
                                            IEnumerable<string>? heroIds = null)
        {
            var evals = new List<ItemEval>();
            foreach (var id in heroIds ?? AllHeroIds(save))
            {
                if (save.Heroes.Find(h => h.Id == id) == null) continue; // skip empty party slots / stale ids
                evals.Add(EvaluateForHero(save, id, candidate, cfg, stage));
            }
            if (evals.Count == 0) return null;

            // Prefer the pool of heroes for whom this is an actual Upgrade; if none, rank everyone
            // (so a best-fit sidegrade can still surface). Auto-equip gates on the verdict either way.
            var upgraders = evals.FindAll(e => e.Verdict == Verdict.Upgrade);
            var pool = upgraders.Count > 0 ? upgraders : evals;

            // Damage-first axis: use it when SOME hero in the pool gains damage; otherwise fall back
            // to effective-life. (Epsilon keeps float noise from reading as a "damage gain".)
            const double eps = 1e-9;
            bool byDamage = pool.Exists(e => e.DamageDelta > eps);

            ItemEval? best = null;
            foreach (var eval in pool) // preserves hero iteration order for deterministic ties
                if (best == null || Beats(eval, best, byDamage)) best = eval;
            return best;
        }

        /// <summary>Ranking comparator: on the primary axis (damage if any hero gains damage, else
        /// effective-life), strictly greater wins; ties break on the blended power delta, so equal
        /// candidates keep the earlier hero in iteration order (determinism).</summary>
        private static bool Beats(ItemEval a, ItemEval b, bool byDamage)
        {
            double pa = byDamage ? a.DamageDelta : a.EhpDelta;
            double pb = byDamage ? b.DamageDelta : b.EhpDelta;
            if (pa != pb) return pa > pb;
            return a.DeltaPercent > b.DeltaPercent;
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
