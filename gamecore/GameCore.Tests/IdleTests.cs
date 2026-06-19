using System;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class IdleTests
    {
        private const long Hour = 3600_000L;

        private static SaveState SaveAt(int highest, long lastClaimAt)
        {
            var s = Save.NewGame(1, GameConfig.Default(), 0);
            s.Progress.HighestStage = highest;
            s.LastClaimAt = lastClaimAt;
            return s;
        }

        [Fact]
        public void NothingClearedYieldsNoIncome()
        {
            var report = Idle.Preview(SaveAt(highest: 0, lastClaimAt: 0), GameConfig.Default(), Hour);
            Assert.Equal(0, report.Gold);
            Assert.Equal(0, report.Xp);
            Assert.Equal(0, report.LootCount);
            Assert.True(report.IsEmpty);
            Assert.Equal(Hour, report.ElapsedMs); // time still elapsed, just no income
        }

        [Fact]
        public void ClockSkewYieldsEmptyReport()
        {
            var report = Idle.Preview(SaveAt(highest: 10, lastClaimAt: 5 * Hour), GameConfig.Default(), 2 * Hour);
            Assert.Equal(0, report.ElapsedMs);
            Assert.True(report.IsEmpty);
            Assert.False(report.Capped);
        }

        [Fact]
        public void ElapsedClampsToIdleCap()
        {
            var cfg = GameConfig.Default();
            long capMs = (long)(cfg.Balance.IdleCapHours * Hour);

            var report = Idle.Preview(SaveAt(highest: 10, lastClaimAt: 0), cfg, 100 * Hour);
            Assert.True(report.Capped);
            Assert.Equal(capMs, report.ElapsedMs);
        }

        [Fact]
        public void WithinCapIsNotMarkedCapped()
        {
            var report = Idle.Preview(SaveAt(highest: 10, lastClaimAt: 0), GameConfig.Default(), 2 * Hour);
            Assert.False(report.Capped);
            Assert.Equal(2 * Hour, report.ElapsedMs);
        }

        [Fact]
        public void GoldAndXpMatchOnlineRateTimesOfflineRate()
        {
            var cfg = GameConfig.Default();
            cfg.Balance.OfflineRate = 1.0; // exactly the online rate for an exact check

            var report = Idle.Preview(SaveAt(highest: 10, lastClaimAt: 0), cfg, 1 * Hour);

            Assert.Equal((long)Math.Floor(cfg.Balance.GoldPerSec(10) * 3600.0), report.Gold);
            Assert.Equal((long)Math.Floor(cfg.Balance.XpPerSec(10) * 3600.0), report.Xp);
            Assert.Equal((int)Math.Floor(cfg.Balance.LootRollsPerHour(10) * 1.0), report.LootCount);
        }

        [Fact]
        public void OfflineRateScalesYield()
        {
            var full = GameConfig.Default(); full.Balance.OfflineRate = 1.0;
            var half = GameConfig.Default(); half.Balance.OfflineRate = 0.5;

            var f = Idle.Preview(SaveAt(highest: 10, lastClaimAt: 0), full, 4 * Hour);
            var h = Idle.Preview(SaveAt(highest: 10, lastClaimAt: 0), half, 4 * Hour);

            Assert.Equal(f.Gold / 2, h.Gold);
            Assert.True(h.LootCount < f.LootCount);
        }

        [Fact]
        public void YieldScalesWithElapsedTime()
        {
            var cfg = GameConfig.Default();
            var oneH = Idle.Preview(SaveAt(highest: 10, lastClaimAt: 0), cfg, 1 * Hour);
            var twoH = Idle.Preview(SaveAt(highest: 10, lastClaimAt: 0), cfg, 2 * Hour);

            Assert.True(twoH.Gold > oneH.Gold);
            Assert.True(twoH.LootCount > oneH.LootCount);
        }

        [Fact]
        public void HigherStageYieldsMore()
        {
            var cfg = GameConfig.Default();
            var low = Idle.Preview(SaveAt(highest: 5, lastClaimAt: 0), cfg, 4 * Hour);
            var high = Idle.Preview(SaveAt(highest: 20, lastClaimAt: 0), cfg, 4 * Hour);

            Assert.True(high.Gold > low.Gold);
            Assert.True(high.Xp > low.Xp);
            Assert.True(high.LootCount > low.LootCount);
        }

        [Fact]
        public void PreviewDoesNotMutateSave()
        {
            var save = SaveAt(highest: 10, lastClaimAt: 1234);
            Idle.Preview(save, GameConfig.Default(), 10 * Hour);

            Assert.Equal(1234, save.LastClaimAt);                 // clock untouched
            Assert.Equal(0, save.Currencies.GetValueOrDefault("gold")); // no rewards applied
        }

        // --- M5.2: Claim ---

        [Fact]
        public void ClaimAppliesGoldXpItemsAndAdvancesClock()
        {
            var cfg = GameConfig.Default();
            var save = SaveAt(highest: 10, lastClaimAt: 0);
            int beforeInv = save.Inventory.Count;

            var (next, report) = Idle.Claim(save, cfg, 2 * Hour);

            Assert.True(report.Gold > 0 && report.LootCount > 0);
            Assert.Equal(report.Gold, next.Currencies["gold"]);
            Assert.Equal(report.LootCount, report.Items.Count); // all rolls reported
            Assert.Equal(beforeInv + report.LootCount, next.Inventory.Count); // idle overfills the bag, nothing dropped
            Assert.True(next.Heroes.Find(h => h.Id == "h1")!.Level > 1); // party hero leveled from idle XP
            Assert.Equal(2 * Hour, next.LastClaimAt);
        }

        [Fact]
        public void ClaimAdvancesClockToNowEvenWhenCapped()
        {
            var (next, report) = Idle.Claim(SaveAt(highest: 10, lastClaimAt: 0), GameConfig.Default(), 100 * Hour);
            Assert.True(report.Capped);
            Assert.Equal(100 * Hour, next.LastClaimAt); // excess past the cap is forfeited
        }

        [Fact]
        public void ClaimIsPure()
        {
            var save = SaveAt(highest: 10, lastClaimAt: 0);
            int origLevel = save.Heroes.Find(h => h.Id == "h1")!.Level;

            Idle.Claim(save, GameConfig.Default(), 5 * Hour);

            Assert.Equal(0, save.LastClaimAt);
            Assert.Equal(0, save.Currencies.GetValueOrDefault("gold"));
            Assert.Empty(save.Inventory);
            Assert.Equal(origLevel, save.Heroes.Find(h => h.Id == "h1")!.Level);
        }

        [Fact]
        public void ClaimIsDeterministic()
        {
            var cfg = GameConfig.Default();
            var (n1, r1) = Idle.Claim(SaveAt(highest: 10, lastClaimAt: 0), cfg, 3 * Hour);
            var (n2, r2) = Idle.Claim(SaveAt(highest: 10, lastClaimAt: 0), cfg, 3 * Hour);

            Assert.Equal(r1.Items.Count, r2.Items.Count);
            for (int i = 0; i < r1.Items.Count; i++)
                Assert.Equal(r1.Items[i].Id, r2.Items[i].Id);
            Assert.Equal(n1.Currencies["gold"], n2.Currencies["gold"]);
        }

        [Fact]
        public void ClaimWithClockSkewReturnsSameSave()
        {
            var save = SaveAt(highest: 10, lastClaimAt: 5 * Hour);
            var (next, report) = Idle.Claim(save, GameConfig.Default(), 2 * Hour);

            Assert.Same(save, next);
            Assert.True(report.IsEmpty);
        }

        [Fact]
        public void BackToBackClaimYieldsNothing()
        {
            var cfg = GameConfig.Default();
            var (afterFirst, _) = Idle.Claim(SaveAt(highest: 10, lastClaimAt: 0), cfg, 2 * Hour);

            // Immediately claiming again at the same instant should yield an empty report.
            var (afterSecond, report2) = Idle.Claim(afterFirst, cfg, 2 * Hour);

            Assert.True(report2.IsEmpty);
            Assert.Equal(afterFirst.Currencies["gold"], afterSecond.Currencies["gold"]);
            Assert.Equal(afterFirst.Inventory.Count, afterSecond.Inventory.Count);
        }
    }
}
