using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class ProgressionTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static HeroInstance Hero(int level = 1, int xp = 0) =>
            new HeroInstance { Id = "h", DefId = "warrior_basic", Level = level, Xp = xp };

        [Fact]
        public void BelowThresholdDoesNotLevel()
        {
            // grant below XpCurve(1) stays level 1, banking the remainder
            var h = Progression.GrantXp(Hero(), 50, Cfg);
            Assert.Equal(1, h.Level);
            Assert.Equal(50L, h.Xp);
        }

        [Fact]
        public void SingleLevelUpCarriesRemainder()
        {
            // one XpCurve(1) plus a remainder -> level 2 carrying the leftover
            long amount = Cfg.Balance.XpCurve(1) + 30;
            var h = Progression.GrantXp(Hero(), amount, Cfg);
            Assert.Equal(2, h.Level);
            Assert.Equal(30L, h.Xp);
        }

        [Fact]
        public void ExactMultiLevelLandsCleanly()
        {
            // XpCurve(1)+XpCurve(2) exactly -> level 3, 0 remainder
            long amount = Cfg.Balance.XpCurve(1) + Cfg.Balance.XpCurve(2);
            var h = Progression.GrantXp(Hero(), amount, Cfg);
            Assert.Equal(3, h.Level);
            Assert.Equal(0L, h.Xp);
        }

        [Fact]
        public void StopsAtMaxLevelAndDiscardsExcess()
        {
            var atCap = Progression.GrantXp(Hero(level: Cfg.Balance.MaxLevel), 1_000_000, Cfg);
            Assert.Equal(Cfg.Balance.MaxLevel, atCap.Level);
            Assert.Equal(0L, atCap.Xp);

            // enough to clear the (now billions-deep) last level
            var nearCap = Progression.GrantXp(Hero(level: Cfg.Balance.MaxLevel - 1), 1_000_000_000_000L, Cfg);
            Assert.Equal(Cfg.Balance.MaxLevel, nearCap.Level);
        }

        [Fact]
        public void GrantXpIsPure()
        {
            var original = Hero();
            var leveled = Progression.GrantXp(original, 500, Cfg);

            Assert.Equal(1, original.Level);   // input untouched
            Assert.Equal(0L, original.Xp);
            Assert.NotSame(original, leveled);
        }

        [Fact]
        public void LevelingRaisesStats()
        {
            var h0 = Hero();
            var h1 = Progression.GrantXp(h0, 5000, Cfg);

            double hpBefore = Stats.ComputeHeroStats(h0, Cfg).Get(StatKey.Hp);
            double hpAfter = Stats.ComputeHeroStats(h1, Cfg).Get(StatKey.Hp);
            Assert.True(h1.Level > 1);
            Assert.True(hpAfter > hpBefore);
        }

        [Fact]
        public void XpCurveIsAMonthsLongClimbAndOverflowSafe()
        {
            var b = Cfg.Balance;
            for (int l = 1; l < b.MaxLevel - 1; l++)
                Assert.True(b.XpCurve(l + 1) > b.XpCurve(l)); // strictly increasing

            long total = 0;
            for (int l = 1; l < b.MaxLevel; l++) total += b.XpCurve(l);
            Assert.True(total > 50_000_000_000L);                 // tens of billions — a long-haul chase
            Assert.True(b.XpCurve(b.MaxLevel - 1) < long.MaxValue / 1000); // deepest level still has headroom
        }

        [Fact]
        public void AHugeLongGrantReachesMaxLevelWithoutOverflow()
        {
            // a single half-trillion grant clears the whole (long) curve and caps cleanly at MaxLevel
            var h = Progression.GrantXp(Hero(), 500_000_000_000L, Cfg);
            Assert.Equal(Cfg.Balance.MaxLevel, h.Level);
            Assert.Equal(0L, h.Xp);
        }

        [Fact]
        public void GrantGoldAddsToAccountAndIsPure()
        {
            var save = Save.NewGame(1, Cfg, 0); // gold 0
            var after = Progression.GrantGold(save, 250);

            Assert.Equal(250, after.Currencies["gold"]);
            Assert.Equal(0, save.Currencies["gold"]); // original untouched
            Assert.NotSame(save, after);
        }

        [Fact]
        public void GrantGoldZeroIsNoop()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Same(save, Progression.GrantGold(save, 0));
        }

        [Fact]
        public void GrantPartyXpLevelsPartyNotBench()
        {
            var save = Save.NewGame(1, Cfg, 0);            // h1 + h2 fielded
            save.Heroes.Add(new HeroInstance { Id = "hb", DefId = "warrior_basic", Level = 1, Xp = 0 }); // benched

            var after = Progression.GrantPartyXp(save, 2000, Cfg);

            var h1 = after.Heroes.Find(h => h.Id == "h1")!;
            var hb = after.Heroes.Find(h => h.Id == "hb")!;
            Assert.True(h1.Level > 1);                       // party hero leveled
            Assert.Equal(1, hb.Level);                       // bench untouched
            Assert.Equal(1, save.Heroes.Find(h => h.Id == "h1")!.Level); // input untouched
        }
    }
}
