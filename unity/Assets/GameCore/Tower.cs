#nullable enable
using System;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Tower of Ascension (alt mode) — a separate, one-clear-per-floor track that is *not* farmed
    /// like the stage ladder. Floors scale STEEPER than the ladder on both HP and damage
    /// (<see cref="BalanceConstants.TowerHpGrowth"/> / <see cref="BalanceConstants.TowerDmgGrowth"/>),
    /// carry a rotating per-floor modifier (the puzzle layer, reusing the Lever-1 modifier catalog),
    /// and grant a permanent **account-wide** buff every <see cref="BalanceConstants.TowerMilestoneEvery"/>
    /// floors. Progress is a single int (<see cref="TowerState.HighestFloor"/>); milestone buffs are
    /// *derived* from it, so nothing extra is persisted. Pure reducers (return a new SaveState).
    ///
    /// Slice 1 = data + progression + derived buffs (this file, tested in isolation). The live "tower
    /// fight" that calls <see cref="RecordClear"/> on a win, and folding <see cref="ApplyAccountBuffs"/>
    /// into the combat stat build, land in slice 2.
    /// </summary>
    public static class Tower
    {
        /// <summary>Deepest floor cleared (0 = none).</summary>
        public static int HighestFloor(SaveState save) => save.Progress.Tower.HighestFloor;

        /// <summary>The next attemptable floor (one past the highest cleared).</summary>
        public static int NextFloor(SaveState save) => HighestFloor(save) + 1;

        /// <summary>Top floor of the tower at this content height.</summary>
        public static int MaxFloor(GameConfig cfg) => cfg.Balance.TowerFloors;

        /// <summary>True once every floor has been cleared.</summary>
        public static bool IsComplete(SaveState save, GameConfig cfg) => HighestFloor(save) >= MaxFloor(cfg);

        /// <summary>You may only attempt the next uncleared floor (sequential, no skipping), and only
        /// within the defined tower height.</summary>
        public static bool CanAttempt(SaveState save, int floor, GameConfig cfg)
            => floor == NextFloor(save) && floor >= 1 && floor <= MaxFloor(cfg);

        /// <summary>Record a floor clear: advances <see cref="TowerState.HighestFloor"/> by one. No-op
        /// (shares the save ref) unless <paramref name="floor"/> is exactly the next sequential floor —
        /// so re-clearing an old floor or skipping ahead does nothing. Forward-only. Pure.</summary>
        public static SaveState RecordClear(SaveState save, int floor, GameConfig cfg)
        {
            if (!CanAttempt(save, floor, cfg)) return save;
            return WithFloor(save, floor);
        }

        // ---- derived milestone / account-buff payoff ----

        /// <summary>How many milestone buffs have been earned (one per TowerMilestoneEvery floors).</summary>
        public static int MilestonesCleared(SaveState save, GameConfig cfg)
            => HighestFloor(save) / Math.Max(1, cfg.Balance.TowerMilestoneEvery);

        /// <summary>The permanent account-wide stat bonus from cleared milestones, as a fraction
        /// (0.10 = +10%). Each milestone adds <see cref="BalanceConstants.TowerMilestoneStatPct"/>.</summary>
        public static double AccountBuffPct(SaveState save, GameConfig cfg)
            => MilestonesCleared(save, cfg) * cfg.Balance.TowerMilestoneStatPct;

        /// <summary>Apply the derived account buff to a computed stat block: scales the core power
        /// stats (Hp/Atk/Def) by (1 + <see cref="AccountBuffPct"/>). Pure (returns a new block);
        /// a no-op share when no milestone is earned. Folded into the combat stat build in slice 2.</summary>
        public static StatBlock ApplyAccountBuffs(StatBlock s, SaveState save, GameConfig cfg)
        {
            double pct = AccountBuffPct(save, cfg);
            if (pct <= 0) return s;
            var r = new StatBlock(s);
            r[StatKey.Hp] = r.Get(StatKey.Hp) * (1 + pct);
            r[StatKey.Atk] = r.Get(StatKey.Atk) * (1 + pct);
            r[StatKey.Def] = r.Get(StatKey.Def) * (1 + pct);
            return r;
        }

        // ---- per-floor difficulty (used by the slice-2 tower fight) ----

        /// <summary>Geometric HP multiplier for a floor's monsters vs a floor-1 baseline.</summary>
        public static double FloorHpMult(int floor, GameConfig cfg)
            => Math.Pow(cfg.Balance.TowerHpGrowth, Math.Max(0, floor - 1));

        /// <summary>Geometric atk/def multiplier for a floor's monsters vs a floor-1 baseline.</summary>
        public static double FloorDmgMult(int floor, GameConfig cfg)
            => Math.Pow(cfg.Balance.TowerDmgGrowth, Math.Max(0, floor - 1));

        /// <summary>The modifier this floor exhibits (or null in the early ramp).</summary>
        public static string? FloorModifier(int floor, GameConfig cfg) => cfg.TowerModifierForFloor(floor);

        // ---- copy helper (Tower progress lives under ProgressState) ----

        private static SaveState WithFloor(SaveState save, int floor)
        {
            var progress = new ProgressState
            {
                HighestStage = save.Progress.HighestStage,
                CurrentStage = save.Progress.CurrentStage,
                AccountLevel = save.Progress.AccountLevel,
                Tower = new TowerState { HighestFloor = floor },
            };
            return new SaveState
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
        }
    }
}
