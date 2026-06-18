using System;
using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class LootTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static LootContext Ctx(double mult, int itemLevel = 1) =>
            new LootContext { ItemLevel = itemLevel, DropRateMult = mult };

        private static Dictionary<Rarity, int> Sample(double mult, int n, uint seed = 99)
        {
            var rng = new Rng(seed);
            var ctx = Ctx(mult);
            var counts = new Dictionary<Rarity, int>();
            foreach (Rarity r in Enum.GetValues(typeof(Rarity))) counts[r] = 0;
            for (int i = 0; i < n; i++) counts[Loot.RollRarity(rng, ctx, Cfg)]++;
            return counts;
        }

        [Fact]
        public void BaseDistributionMatchesWeightsWhenMultIsOne()
        {
            const int n = 200_000;
            var counts = Sample(1.0, n);

            var w = Cfg.Balance.RarityBaseWeights;
            double total = 0;
            foreach (var x in w) total += x;

            foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
            {
                double expected = w[(int)r] / total;
                double actual = counts[r] / (double)n;
                // generous tolerance; even Legendary (~0.26%) is stable at 200k samples
                Assert.True(Math.Abs(actual - expected) < 0.01 + expected * 0.1,
                    $"{r}: expected ~{expected:F4}, got {actual:F4}");
            }
        }

        [Fact]
        public void HigherDropRateMultShiftsRarityUp()
        {
            const int n = 200_000;
            var low = Sample(1.0, n);
            var high = Sample(2.0, n);

            // a richer rift should yield fewer commons and more high-end items
            Assert.True(high[Rarity.Normal] < low[Rarity.Normal], "expected fewer Normals at higher mult");
            Assert.True(high[Rarity.Unique] > low[Rarity.Unique], "expected more Uniques at higher mult");
            Assert.True(high[Rarity.Legendary] > low[Rarity.Legendary], "expected more Legendaries at higher mult");
        }

        [Fact]
        public void AllFiveRaritiesAreReachable()
        {
            var counts = Sample(2.0, 200_000);
            foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
                Assert.True(counts[r] > 0, $"{r} never dropped");
        }

        [Fact]
        public void DeterministicForSameSeed()
        {
            var a = new Rng(7);
            var b = new Rng(7);
            var ctx = Ctx(1.5);
            for (int i = 0; i < 1000; i++)
                Assert.Equal(Loot.RollRarity(a, ctx, Cfg), Loot.RollRarity(b, ctx, Cfg));
        }

        // --- M2.2: affixes ---

        private static ItemBaseDef Weapon => Cfg.ItemBases["rusty_sword"];
        private static AffixDef Def(StatKey s) => Cfg.AffixPool.Find(d => d.Stat == s)!;

        [Fact]
        public void NormalItemHasNoAffixes()
        {
            var aff = Loot.RollAffixes(new Rng(1), Weapon, Rarity.Normal, 10, Cfg);
            Assert.Empty(aff);
        }

        [Fact]
        public void AffixCountWithinBoundsCappedByPool()
        {
            // Rare weapon: balance says 3-4, eligible pool is 4 -> count in [3,4]
            var rng = new Rng(3);
            for (int i = 0; i < 500; i++)
            {
                var aff = Loot.RollAffixes(rng, Weapon, Rarity.Rare, 5, Cfg);
                Assert.InRange(aff.Count, 3, 4);
            }
        }

        [Fact]
        public void OnlyAllowedStatsAppear()
        {
            var rng = new Rng(4);
            for (int i = 0; i < 500; i++)
                foreach (var a in Loot.RollAffixes(rng, Weapon, Rarity.Legendary, 8, Cfg))
                    Assert.Contains(a.Stat, Weapon.AllowedAffixes);
        }

        [Fact]
        public void RarityFloorRespected()
        {
            // Magic weapon: crit/spd are Rare-floor, so only Atk (Magic-floor) is eligible
            var rng = new Rng(5);
            for (int i = 0; i < 500; i++)
                foreach (var a in Loot.RollAffixes(rng, Weapon, Rarity.Magic, 6, Cfg))
                    Assert.True((int)Def(a.Stat).RarityFloor <= (int)Rarity.Magic);
        }

        [Fact]
        public void NoDuplicateStatsOnOneItem()
        {
            var rng = new Rng(6);
            for (int i = 0; i < 500; i++)
            {
                var aff = Loot.RollAffixes(rng, Weapon, Rarity.Legendary, 8, Cfg);
                Assert.Equal(aff.Count, new HashSet<StatKey>(aff.ConvertAll(a => a.Stat)).Count);
            }
        }

        [Fact]
        public void ValueScalesWithItemLevelAndIsInRange()
        {
            const int il = 10;
            var rng = new Rng(8);
            for (int i = 0; i < 500; i++)
                foreach (var a in Loot.RollAffixes(rng, Weapon, Rarity.Rare, il, Cfg))
                {
                    var d = Def(a.Stat);
                    Assert.InRange(a.Value, d.ValueMinPerItemLevel * il, d.ValueMaxPerItemLevel * il);
                }
        }

        [Fact]
        public void AffixesDeterministicForSameSeed()
        {
            var a = new Rng(5);
            var b = new Rng(5);
            for (int i = 0; i < 200; i++)
            {
                var x = Loot.RollAffixes(a, Weapon, Rarity.Rare, 7, Cfg);
                var y = Loot.RollAffixes(b, Weapon, Rarity.Rare, 7, Cfg);
                Assert.Equal(x.Count, y.Count);
                for (int k = 0; k < x.Count; k++)
                {
                    Assert.Equal(x[k].Stat, y[k].Stat);
                    Assert.Equal(x[k].Value, y[k].Value);
                }
            }
        }

        // --- M2.3: item assembly + drop ---

        private static MonsterDef Common => Cfg.Monsters["slime"];
        private static MonsterDef Boss => Cfg.Monsters["goblin_king"]; // LootTableId "boss"

        [Fact]
        public void RollItemIsWellFormed()
        {
            var item = Loot.RollItem(new Rng(1), "rusty_sword", 5, Rarity.Rare, Cfg);
            Assert.False(string.IsNullOrEmpty(item.Id));
            Assert.Equal("rusty_sword", item.BaseId);
            Assert.Equal(5, item.ItemLevel);
            Assert.Equal(Rarity.Rare, item.Rarity);
            Assert.InRange(item.Affixes.Count, 3, 4);

            var normal = Loot.RollItem(new Rng(1), "rusty_sword", 5, Rarity.Normal, Cfg);
            Assert.Empty(normal.Affixes);
        }

        [Fact]
        public void RollItemThrowsOnUnknownBase()
        {
            Assert.Throws<ArgumentException>(() => Loot.RollItem(new Rng(1), "nope", 5, Rarity.Magic, Cfg));
        }

        [Fact]
        public void CommonDropCanBeNothingOrItem()
        {
            var rng = new Rng(2);
            var ctx = Ctx(1.5, 5);
            bool sawNull = false, sawItem = false;
            for (int i = 0; i < 500; i++)
            {
                if (Loot.RollDrop(rng, Common, ctx, Cfg) == null) sawNull = true; else sawItem = true;
            }
            Assert.True(sawNull && sawItem);
        }

        [Fact]
        public void BossAlwaysDrops()
        {
            var rng = new Rng(3);
            var ctx = Ctx(1.5, 5);
            for (int i = 0; i < 200; i++)
                Assert.NotNull(Loot.RollDrop(rng, Boss, ctx, Cfg));
        }

        [Fact]
        public void CommonDropRateMatchesChance()
        {
            const int n = 50_000;
            var rng = new Rng(4);
            var ctx = Ctx(1.5, 5);
            int drops = 0;
            for (int i = 0; i < n; i++)
                if (Loot.RollDrop(rng, Common, ctx, Cfg) != null) drops++;

            double actual = drops / (double)n;
            Assert.True(Math.Abs(actual - Cfg.Balance.DropChance) < 0.02,
                $"expected ~{Cfg.Balance.DropChance}, got {actual:F4}");
        }

        [Fact]
        public void DroppedItemIsWellFormed()
        {
            var rng = new Rng(5);
            var ctx = Ctx(2.0, 7);
            for (int i = 0; i < 1000; i++)
            {
                var item = Loot.RollDrop(rng, Common, ctx, Cfg);
                if (item == null) continue;
                Assert.True(Cfg.ItemBases.ContainsKey(item.BaseId));
                Assert.Equal(ctx.ItemLevel, item.ItemLevel);
                var allowed = Cfg.ItemBases[item.BaseId].AllowedAffixes;
                foreach (var a in item.Affixes)
                {
                    Assert.Contains(a.Stat, allowed);
                    Assert.True((int)Def(a.Stat).RarityFloor <= (int)item.Rarity);
                }
            }
        }

        [Fact]
        public void DropDeterministicForSameSeed()
        {
            var a = new Rng(9);
            var b = new Rng(9);
            var ctx = Ctx(1.8, 6);
            for (int i = 0; i < 300; i++)
            {
                var x = Loot.RollDrop(a, Common, ctx, Cfg);
                var y = Loot.RollDrop(b, Common, ctx, Cfg);
                Assert.Equal(x == null, y == null);
                if (x == null) continue;
                Assert.Equal(x.Id, y!.Id);
                Assert.Equal(x.BaseId, y.BaseId);
                Assert.Equal(x.Rarity, y.Rarity);
                Assert.Equal(x.Affixes.Count, y.Affixes.Count);
            }
        }
    }
}
