using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Tower of Ascension slice 1: a one-clear-per-floor track with derived, permanent
    /// account-wide milestone buffs. Sequential, forward-only progression; nothing extra persisted
    /// beyond the floor count; survives the unrelated reducers that rebuild ProgressState.</summary>
    public class TowerTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static SaveState ClearTo(SaveState save, int floor)
        {
            while (Tower.HighestFloor(save) < floor)
                save = Tower.RecordClear(save, Tower.NextFloor(save), Cfg);
            return save;
        }

        // ---- progression ----

        [Fact]
        public void NewGameStartsAtFloorZero()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Equal(0, Tower.HighestFloor(save));
            Assert.Equal(1, Tower.NextFloor(save));
            Assert.False(Tower.IsComplete(save, Cfg));
        }

        [Fact]
        public void RecordClearAdvancesTheNextFloorOnly()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Tower.RecordClear(save, 1, Cfg);
            Assert.Equal(1, Tower.HighestFloor(save));
            save = Tower.RecordClear(save, 2, Cfg);
            Assert.Equal(2, Tower.HighestFloor(save));
        }

        [Fact]
        public void CannotSkipAhead()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.False(Tower.CanAttempt(save, 2, Cfg));        // floor 1 not cleared yet
            Assert.Same(save, Tower.RecordClear(save, 2, Cfg));  // skip -> no-op
        }

        [Fact]
        public void ReclearingAnOldFloorIsANoOp()
        {
            var save = ClearTo(Save.NewGame(1, Cfg, 0), 3);
            Assert.Same(save, Tower.RecordClear(save, 2, Cfg)); // already past -> no-op
            Assert.Equal(3, Tower.HighestFloor(save));
        }

        // ---- per-floor gem reward ----

        private static long Gems(SaveState save)
            => save.Currencies.TryGetValue(Cfg.Balance.PremiumCurrency, out var v) ? v : 0;

        [Fact]
        public void ClearingAFloorGrantsExactlyTowerGemsPerFloor()
        {
            var save = Save.NewGame(1, Cfg, 0);
            long before = Gems(save);

            var after = Tower.RecordClear(save, 1, Cfg);
            Assert.Equal(1, Tower.HighestFloor(after));                       // advances as before
            Assert.Equal(before + Cfg.Balance.TowerGemsPerFloor, Gems(after)); // exactly one floor's gems
        }

        [Fact]
        public void ReclearingGrantsNoGems()
        {
            var save = ClearTo(Save.NewGame(1, Cfg, 0), 3);
            long before = Gems(save);
            var noop = Tower.RecordClear(save, 2, Cfg); // already past -> no advance, no gems
            Assert.Same(save, noop);
            Assert.Equal(before, Gems(noop));
        }

        [Fact]
        public void GemsAccumulateAcrossFloors()
        {
            var save = ClearTo(Save.NewGame(1, Cfg, 0), 5);
            Assert.Equal(5L * Cfg.Balance.TowerGemsPerFloor, Gems(save)); // 5 first-time clears
        }

        [Fact]
        public void CannotClearPastTheTop()
        {
            var save = ClearTo(Save.NewGame(1, Cfg, 0), Tower.MaxFloor(Cfg));
            Assert.True(Tower.IsComplete(save, Cfg));
            Assert.Same(save, Tower.RecordClear(save, Tower.MaxFloor(Cfg) + 1, Cfg));
        }

        // ---- derived milestone buffs ----

        [Fact]
        public void MilestonesAndBuffPctDeriveFromFloor()
        {
            var below = ClearTo(Save.NewGame(1, Cfg, 0), Cfg.Balance.TowerMilestoneEvery - 1);
            Assert.Equal(0, Tower.MilestonesCleared(below, Cfg));
            Assert.Equal(0.0, Tower.AccountBuffPct(below, Cfg), 6);

            var oneMilestone = ClearTo(below, Cfg.Balance.TowerMilestoneEvery);
            Assert.Equal(1, Tower.MilestonesCleared(oneMilestone, Cfg));
            Assert.Equal(Cfg.Balance.TowerMilestoneStatPct, Tower.AccountBuffPct(oneMilestone, Cfg), 6);

            var twoMilestones = ClearTo(oneMilestone, Cfg.Balance.TowerMilestoneEvery * 2);
            Assert.Equal(2 * Cfg.Balance.TowerMilestoneStatPct, Tower.AccountBuffPct(twoMilestones, Cfg), 6);
        }

        [Fact]
        public void AccountBuffScalesCoreStatsOnly()
        {
            var save = ClearTo(Save.NewGame(1, Cfg, 0), Cfg.Balance.TowerMilestoneEvery); // +5%
            double pct = Tower.AccountBuffPct(save, Cfg);
            var s = new StatBlock { [StatKey.Hp] = 100, [StatKey.Atk] = 10, [StatKey.Def] = 5, [StatKey.MoveSpd] = 3 };

            var buffed = Tower.ApplyAccountBuffs(s, save, Cfg);
            Assert.Equal(100 * (1 + pct), buffed.Get(StatKey.Hp), 6);
            Assert.Equal(10 * (1 + pct), buffed.Get(StatKey.Atk), 6);
            Assert.Equal(5 * (1 + pct), buffed.Get(StatKey.Def), 6);
            Assert.Equal(3, buffed.Get(StatKey.MoveSpd), 6); // non-core stat untouched
        }

        [Fact]
        public void AccountBuffIsANoOpWithoutAMilestone()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var s = new StatBlock { [StatKey.Hp] = 100 };
            Assert.Same(s, Tower.ApplyAccountBuffs(s, save, Cfg)); // no milestone -> same ref
        }

        // ---- save-threading guard ----

        [Fact]
        public void TowerFloorSurvivesProgressRebuildingReducers()
        {
            var save = Tower.RecordClear(Save.NewGame(1, Cfg, 0), 1, Cfg);
            Assert.Equal(1, Tower.HighestFloor(save));

            Assert.Equal(1, Tower.HighestFloor(Progression.OnStageCleared(save, 1, Cfg))); // rebuilds ProgressState
            Assert.Equal(1, Tower.HighestFloor(Progression.SetStage(save, 1, Cfg)));        // rebuilds ProgressState
            Assert.Equal(1, Tower.HighestFloor(Progression.GrantGold(save, 50)));           // shares Progress ref
        }

        [Fact]
        public void MigrateBackfillsTowerForOldSaves()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save.Progress.Tower = null!;                  // simulate a pre-Tower save payload
            var migrated = Save.Migrate(save);
            Assert.NotNull(migrated.Progress.Tower);
            Assert.Equal(0, Tower.HighestFloor(migrated));
        }
    }
}
