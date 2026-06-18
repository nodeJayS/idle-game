using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class RngTests
    {
        [Fact]
        public void IsDeterministicForSameSeed()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);
            Assert.Equal(new[] { a.Next(), a.Next(), a.Next() },
                         new[] { b.Next(), b.Next(), b.Next() });
        }

        [Fact]
        public void CanResumeFromCursor()
        {
            var full = new Rng(999);
            full.Next();
            full.Next();
            var expected = full.Next();

            var resumed = new Rng(999, 2); // skip the first two
            Assert.Equal(expected, resumed.Next());
        }

        [Fact]
        public void WeightedPickRespectsWeights()
        {
            var rng = new Rng(42);
            var entries = new List<(string, double)> { ("common", 90), ("rare", 10) };
            var counts = new Dictionary<string, int> { ["common"] = 0, ["rare"] = 0 };
            for (int i = 0; i < 10000; i++) counts[rng.WeightedPick(entries)]++;

            Assert.True(counts["common"] > 8500, $"common={counts["common"]}");
            Assert.True(counts["rare"] > 700 && counts["rare"] < 1300, $"rare={counts["rare"]}");
        }

        [Fact]
        public void RandIntStaysInRange()
        {
            var rng = new Rng(7);
            for (int i = 0; i < 1000; i++)
            {
                int v = rng.RandInt(3, 6);
                Assert.InRange(v, 3, 6);
            }
        }
    }
}
