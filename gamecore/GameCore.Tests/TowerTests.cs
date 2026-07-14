using System.Collections.Generic;
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

        // ---- per-floor first-clear bundle (gold + boss loot) ----

        private static long Gold(SaveState save) => save.Currencies.TryGetValue("gold", out var v) ? v : 0;
        private static long Scrap(SaveState save) => save.Currencies.TryGetValue("scrap", out var v) ? v : 0;

        /// <summary>Every item the bundle rolled, whichever way the loot filter split it.</summary>
        private static List<Item> Bundle(Tower.TowerClearReward r)
        {
            var all = new List<Item>(r.Stored);
            all.AddRange(r.Salvaged);
            return all;
        }

        [Fact]
        public void FirstClearPaysGoldAndLootAndKeepsTheGemDrip()
        {
            var save = Save.NewGame(1, Cfg, 12345);
            long gemsBefore = Gems(save);
            long goldBefore = Gold(save);
            long cursorBefore = save.RngCursor;

            var after = Tower.RecordClear(save, 1, Cfg, out var reward);

            Assert.NotNull(reward);
            Assert.Equal(gemsBefore + Cfg.Balance.TowerGemsPerFloor, Gems(after)); // gems unchanged (still the drip)
            Assert.Equal(Cfg.Balance.TowerGemsPerFloor, reward!.Gems);
            Assert.True(reward.Gold > 0);                                          // gold bundle paid
            Assert.Equal(Tower.GoldBundle(1, Cfg), reward.Gold);                   // exactly the exposed formula
            Assert.Equal(goldBefore + reward.Gold, Gold(after));
            Assert.True(Bundle(reward).Count >= 1);                                // RollBossDrops guarantees a Unique+ bundle
            Assert.NotEqual(cursorBefore, after.RngCursor);                        // the roll persisted the advanced cursor
        }

        [Fact]
        public void NoOpClearsShareTheRefRewardNullAndPayNothing()
        {
            var save = ClearTo(Save.NewGame(1, Cfg, 7), 3); // floors 1-3 done, next = 4
            long gold = Gold(save);
            long cursor = save.RngCursor;
            int inv = save.Inventory.Count;

            var reclear = Tower.RecordClear(save, 2, Cfg, out var r1); // already past -> no-op
            Assert.Same(save, reclear);
            Assert.Null(r1);

            var skip = Tower.RecordClear(save, 10, Cfg, out var r2);   // skip ahead -> no-op
            Assert.Same(save, skip);
            Assert.Null(r2);

            Assert.Equal(gold, Gold(save));           // cursor / gold / inventory untouched
            Assert.Equal(cursor, save.RngCursor);
            Assert.Equal(inv, save.Inventory.Count);
        }

        [Fact]
        public void RecordClearIsDeterministicAndCannotReroll()
        {
            var save = Save.NewGame(1, Cfg, 999);
            Tower.RecordClear(save, 1, Cfg, out var a);
            var afterB = Tower.RecordClear(save, 1, Cfg, out var b); // SAME input save twice

            Assert.NotNull(a);
            Assert.NotNull(b);
            var ia = Bundle(a!);
            var ib = Bundle(b!);
            Assert.Equal(ia.Count, ib.Count);
            for (int i = 0; i < ia.Count; i++)
            {
                Assert.Equal(ia[i].BaseId, ib[i].BaseId);   // identical rolls (item ids are seed+cursor derived too)
                Assert.Equal(ia[i].Rarity, ib[i].Rarity);
                Assert.Equal(ia[i].Affixes.Count, ib[i].Affixes.Count);
                for (int j = 0; j < ia[i].Affixes.Count; j++)
                {
                    Assert.Equal(ia[i].Affixes[j].Stat, ib[i].Affixes[j].Stat);
                    Assert.Equal(ia[i].Affixes[j].Value, ib[i].Affixes[j].Value, 6);
                }
            }
            Assert.Equal(a!.Gold, b!.Gold);
            Assert.NotEqual(save.RngCursor, afterB.RngCursor); // the advanced cursor is persisted on the save
        }

        [Fact]
        public void MilestoneFloorRollsTheMajorBossBundle()
        {
            int m = Cfg.Balance.TowerMilestoneEvery;
            var save = ClearTo(Save.NewGame(1, Cfg, 4242), m - 1); // next attempt = the milestone floor
            Tower.RecordClear(save, m, Cfg, out var reward);

            Assert.NotNull(reward);
            int uniquePlus = 0;
            foreach (var it in Bundle(reward!)) if (it.Rarity >= Rarity.Unique) uniquePlus++;
            // A major boss bundle guarantees MajorBossUniques.min Unique+ items — strictly more than a
            // mini-boss floor could ever roll (MiniBossUniques.max), so isMajor is observable.
            Assert.True(uniquePlus >= Cfg.Balance.MajorBossUniques.min);
            Assert.True(uniquePlus > Cfg.Balance.MiniBossUniques.max);
        }

        [Fact]
        public void StageEquivalentAnchorsAndIsMonotonic()
        {
            Assert.Equal(1, Tower.StageEquivalent(1, Cfg));           // floor 1 is stage-1 hard
            Assert.InRange(Tower.StageEquivalent(30, Cfg), 95, 105);  // floor 30 ≈ the endgame

            int prev = 0;
            for (int f = 1; f <= Tower.MaxFloor(Cfg); f++)
            {
                int e = Tower.StageEquivalent(f, Cfg);
                Assert.True(e >= prev);
                prev = e;
            }
        }

        [Fact]
        public void GoldBundleIsMonotonicInFloorAndRoundsDown()
        {
            long prev = -1;
            for (int f = 1; f <= Tower.MaxFloor(Cfg); f++)
            {
                long g = Tower.GoldBundle(f, Cfg);
                Assert.True(g >= prev); // never decreases as floors deepen
                prev = g;
            }
            // Floored: exactly minutes of idle-rate gold at the equivalent stage, no rounding up.
            int equiv = Tower.StageEquivalent(5, Cfg);
            long expected = (long)System.Math.Floor(Cfg.Balance.GoldPerSec(equiv) * 60.0 * Cfg.Balance.TowerGoldBundleMinutes);
            Assert.Equal(expected, Tower.GoldBundle(5, Cfg));
        }

        [Fact]
        public void AutoSalvageFilterConvertsTheBundleToScrap()
        {
            // A filter that scraps every rarity in every slot turns the whole bundle into scrap.
            var save = Inventory.SetSalvageFloorAll(Save.NewGame(1, Cfg, 55), Rarity.Mythic);
            long scrapBefore = Scrap(save);
            int invBefore = save.Inventory.Count;

            var after = Tower.RecordClear(save, 1, Cfg, out var reward);

            Assert.NotNull(reward);
            Assert.True(reward!.ScrapGained > 0);                          // the bundle became scrap
            Assert.Empty(reward.Stored);                                   // nothing kept
            Assert.Equal(scrapBefore + reward.ScrapGained, Scrap(after));
            Assert.Equal(invBefore, after.Inventory.Count);               // nothing entered the bag
        }
    }
}
