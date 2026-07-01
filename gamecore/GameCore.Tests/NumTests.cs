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

        // --- directional rounding (design §7: display correctness) ---

        // A resource the player HAS must never DISPLAY more than they own (floor).
        [Theory]
        [InlineData(1299, "1.2K")]  // round would give 1.3K — floor must not overstate
        [InlineData(1250, "1.2K")]
        [InlineData(999_999, "999.9K")]
        [InlineData(950, "950")]
        public void CompactFloorNeverOverstates(long owned, string expected)
        {
            Assert.Equal(expected, Num.CompactFloor(owned));
            Assert.True(MagnitudeOf(Num.CompactFloor(owned)) <= owned); // display <= truth
        }

        // A COST must never DISPLAY less than what will be charged (ceil).
        [Theory]
        [InlineData(1201, "1.3K")]  // round would give 1.2K — ceil must not understate
        [InlineData(1250, "1.3K")]
        [InlineData(999_999, "1M")]
        [InlineData(1000, "1K")]
        public void CompactCeilNeverUnderstates(long cost, string expected)
        {
            Assert.Equal(expected, Num.CompactCeil(cost));
            Assert.True(MagnitudeOf(Num.CompactCeil(cost)) >= cost); // display >= truth
        }

        // Parse "1.2K" back to an integer magnitude for the invariant checks above.
        private static long MagnitudeOf(string s)
        {
            long mult = 1;
            char last = s[s.Length - 1];
            if (!char.IsDigit(last))
            {
                s = s.Substring(0, s.Length - 1);
                mult = last switch { 'K' => 1_000, 'M' => 1_000_000, 'B' => 1_000_000_000, _ => 1 };
            }
            return (long)(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture) * mult);
        }
    }
}
