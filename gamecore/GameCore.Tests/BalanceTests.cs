using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class BalanceTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();
        private static readonly BalanceConstants B = Cfg.Balance;

        [Theory]
        [InlineData(1, 0)]
        [InlineData(10, 0)]
        [InlineData(11, 1)]
        [InlineData(20, 1)]
        [InlineData(21, 2)]
        public void TierBucketsEveryTenStages(int stage, int tier)
            => Assert.Equal(tier, B.Tier(stage));

        [Fact]
        public void IdleRatesAreMonotonicAcrossStages()
        {
            for (int s = 2; s <= 50; s++)
            {
                Assert.True(B.GoldPerSec(s) >= B.GoldPerSec(s - 1));
                Assert.True(B.XpPerSec(s) >= B.XpPerSec(s - 1));
                Assert.True(B.LootRollsPerHour(s) >= B.LootRollsPerHour(s - 1));
            }
        }

        [Fact]
        public void CrossingARealBossJumpsRatesMoreThanAWithinTierStep()
        {
            // within-tier step (9 -> 10) vs tier crossing (10 -> 11)
            double withinTier = (double)B.GoldPerSec(10) / B.GoldPerSec(9);
            double tierCross = (double)B.GoldPerSec(11) / B.GoldPerSec(10);

            Assert.True(tierCross > withinTier);  // the boss crossing is the bigger jump
            Assert.True(tierCross > 1.8);          // and it's a "significant" jump
        }

        [Fact]
        public void ItemPowerAndDropRateJumpPerTier()
        {
            StageDef Stage(int n) => Cfg.Stages.First(s => s.Stage == n);

            // item level: incremental within a tier, a bigger bump crossing one
            int withinTier = Stage(10).AffixItemLevel - Stage(9).AffixItemLevel; // +1
            int tierCross = Stage(11).AffixItemLevel - Stage(10).AffixItemLevel;  // +1 +ItemLevelTierBonus
            Assert.True(tierCross > withinTier);

            // rarity bias rises and steps up across the tier boundary too
            Assert.True(Stage(11).DropRateMult > Stage(10).DropRateMult);
            Assert.True(Stage(50).DropRateMult > Stage(1).DropRateMult);
        }
    }
}
