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
            // XpCurve(1) = 100; grant 50 stays level 1
            var h = Progression.GrantXp(Hero(), 50, Cfg);
            Assert.Equal(1, h.Level);
            Assert.Equal(50, h.Xp);
        }

        [Fact]
        public void SingleLevelUpCarriesRemainder()
        {
            // 130 XP from level 1: -100 -> level 2 with 30 left
            var h = Progression.GrantXp(Hero(), 130, Cfg);
            Assert.Equal(2, h.Level);
            Assert.Equal(30, h.Xp);
        }

        [Fact]
        public void ExactMultiLevelLandsCleanly()
        {
            // XpCurve(1)+XpCurve(2) = 100 + 115 = 215 -> exactly level 3, 0 xp
            int amount = (int)(Cfg.Balance.XpCurve(1) + Cfg.Balance.XpCurve(2));
            var h = Progression.GrantXp(Hero(), amount, Cfg);
            Assert.Equal(3, h.Level);
            Assert.Equal(0, h.Xp);
        }

        [Fact]
        public void StopsAtMaxLevelAndDiscardsExcess()
        {
            var atCap = Progression.GrantXp(Hero(level: Cfg.Balance.MaxLevel), 1_000_000, Cfg);
            Assert.Equal(Cfg.Balance.MaxLevel, atCap.Level);
            Assert.Equal(0, atCap.Xp);

            var nearCap = Progression.GrantXp(Hero(level: Cfg.Balance.MaxLevel - 1), int.MaxValue, Cfg);
            Assert.Equal(Cfg.Balance.MaxLevel, nearCap.Level);
        }

        [Fact]
        public void GrantXpIsPure()
        {
            var original = Hero();
            var leveled = Progression.GrantXp(original, 500, Cfg);

            Assert.Equal(1, original.Level);   // input untouched
            Assert.Equal(0, original.Xp);
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
            var save = Save.NewGame(1, Cfg, 0);            // h1 in party slot 0
            save.Heroes.Add(new HeroInstance { Id = "h2", DefId = "warrior_basic", Level = 1, Xp = 0 }); // benched

            var after = Progression.GrantPartyXp(save, 250, Cfg);

            var h1 = after.Heroes.Find(h => h.Id == "h1")!;
            var h2 = after.Heroes.Find(h => h.Id == "h2")!;
            Assert.True(h1.Level > 1);                       // party hero leveled
            Assert.Equal(1, h2.Level);                       // bench untouched
            Assert.Equal(1, save.Heroes.Find(h => h.Id == "h1")!.Level); // input untouched
        }
    }
}
