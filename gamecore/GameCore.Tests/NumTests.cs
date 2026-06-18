using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class NumTests
    {
        [Theory]
        [InlineData(0, "0")]
        [InlineData(5, "5")]
        [InlineData(42, "42")]
        [InlineData(999, "999")]
        public void BelowThousandRendersPlainly(double value, string expected)
            => Assert.Equal(expected, Num.Compact(value));

        [Theory]
        [InlineData(1000, "1K")]
        [InlineData(1200, "1.2K")]
        [InlineData(1500, "1.5K")]
        [InlineData(12345, "12.3K")]
        [InlineData(1_000_000, "1M")]
        [InlineData(2_500_000, "2.5M")]
        [InlineData(1_000_000_000, "1B")]
        [InlineData(1_000_000_000_000, "1T")]
        public void LargeValuesUseSuffixes(double value, string expected)
            => Assert.Equal(expected, Num.Compact(value));

        [Fact]
        public void RoundsAwayFromZero()
        {
            Assert.Equal("1.3K", Num.Compact(1250)); // 1.25 -> 1.3 at one decimal
        }

        [Fact]
        public void RoundingBumpsToNextTier()
        {
            Assert.Equal("1M", Num.Compact(999_999)); // 999.999K rounds up into M
        }

        [Fact]
        public void HandlesNegatives()
        {
            Assert.Equal("-1.5K", Num.Compact(-1500));
            Assert.Equal("-42", Num.Compact(-42));
        }

        [Fact]
        public void RespectsDecimalsParam()
        {
            Assert.Equal("1.23K", Num.Compact(1234, decimals: 2));
            Assert.Equal("1K", Num.Compact(1234, decimals: 0));
        }

        [Fact]
        public void LongOverloadMatchesDouble()
        {
            Assert.Equal("2.5M", Num.Compact(2_500_000L));
            Assert.Equal("999", Num.Compact(999L));
        }
    }
}
