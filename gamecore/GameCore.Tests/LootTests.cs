using System;
using System.Collections.Generic;
using System.Linq;
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
                // generous tolerance; even Mythic (~0.08%) is stable at 200k samples
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

            // a richer stage should yield fewer commons and more high-end items
            Assert.True(high[Rarity.Normal] < low[Rarity.Normal], "expected fewer Normals at higher mult");
            Assert.True(high[Rarity.Unique] > low[Rarity.Unique], "expected more Uniques at higher mult");
            Assert.True(high[Rarity.Legendary] > low[Rarity.Legendary], "expected more Legendaries at higher mult");
            Assert.True(high[Rarity.Mythic] > low[Rarity.Mythic], "expected more Mythics at higher mult");
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
            // Rare weapon: balance says 2-3, eligible pool is 4 -> count in [2,3];
            // Mythic says 6-7 but the pool caps it at 4.
            var rng = new Rng(3);
            for (int i = 0; i < 500; i++)
            {
                var aff = Loot.RollAffixes(rng, Weapon, Rarity.Rare, 5, Cfg);
                Assert.InRange(aff.Count, 2, 3);
                Assert.Equal(4, Loot.RollAffixes(rng, Weapon, Rarity.Mythic, 5, Cfg).Count);
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
        public void RareRollsTheWholeEligiblePool()
        {
            // All affix floors sit at Rare — a Rare item (the trash ceiling) can roll every
            // allowed stat, so no affix is boss-gated by rarity floor.
            var rng = new Rng(5);
            var seen = new HashSet<StatKey>();
            for (int i = 0; i < 500; i++)
                foreach (var a in Loot.RollAffixes(rng, Weapon, Rarity.Rare, 6, Cfg))
                {
                    Assert.True((int)Def(a.Stat).RarityFloor <= (int)Rarity.Rare);
                    seen.Add(a.Stat);
                }
            Assert.Equal(new HashSet<StatKey>(Weapon.AllowedAffixes), seen);
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
            Assert.InRange(item.Affixes.Count, 2, 3);

            var normal = Loot.RollItem(new Rng(1), "rusty_sword", 5, Rarity.Normal, Cfg);
            Assert.Empty(normal.Affixes);
        }

        [Fact]
        public void RollItemThrowsOnUnknownBase()
        {
            Assert.Throws<ArgumentException>(() => Loot.RollItem(new Rng(1), "nope", 5, Rarity.Rare, Cfg));
        }

        [Fact]
        public void CommonDropCanBeNothingOrItem()
        {
            var rng = new Rng(2);
            var ctx = Ctx(1.5, 5);
            bool sawNull = false, sawItem = false;
            // DropChance is deliberately tiny now, so sample plenty to see at least one drop.
            for (int i = 0; i < 20000; i++)
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

        // --- M10.3: rarity cap + boss guaranteed bundles ---

        [Fact]
        public void RollRarityRespectsMaxRarityCap()
        {
            var ctx = new LootContext { ItemLevel = 5, DropRateMult = 5.0, MaxRarity = Rarity.Rare }; // heavy upward bias
            var rng = new Rng(11);
            for (int i = 0; i < 5000; i++)
                Assert.True((int)Loot.RollRarity(rng, ctx, Cfg) <= (int)Rarity.Rare);
        }

        [Fact]
        public void ForStageCapsTrashRarityAtRare()
        {
            var ctx = LootContext.ForStage(Cfg.Stages.First(s => s.Stage == 50)); // deepest, richest stage
            var rng = new Rng(12);
            for (int i = 0; i < 5000; i++)
                Assert.True((int)Loot.RollRarity(rng, ctx, Cfg) <= (int)Rarity.Rare);
        }

        private static LootContext BossCtx => new LootContext { ItemLevel = 10, DropRateMult = 1.0, MaxRarity = Rarity.Rare };

        [Fact]
        public void MajorBossDropsGuaranteedUniqueLegendaryBundlePlusExtras()
        {
            var b = Cfg.Balance;
            var drops = Loot.RollBossDrops(new Rng(1), BossCtx, Cfg, isMajor: true);

            int hi = drops.Count(d => d.Rarity >= Rarity.Unique); // Unique/Legendary/Mythic
            Assert.InRange(hi, b.MajorBossUniques.min, b.MajorBossUniques.max); // guaranteed chase items
            Assert.Equal(hi + b.MajorBossExtras, drops.Count);                  // plus ordinary extras
            // extras can never be Unique+ (ctx-capped at Rare)
            Assert.Equal(b.MajorBossExtras, drops.Count(d => (int)d.Rarity <= (int)Rarity.Rare));
        }

        [Fact]
        public void BossBundleRollsMythicAtItsChance()
        {
            // Force the long-tail: with BossMythicChance = 1 every guaranteed bundle item
            // is Mythic (the extras stay ctx-capped at Rare).
            var cfg = GameConfig.Default();
            cfg.Balance.BossMythicChance = 1.0;
            var drops = Loot.RollBossDrops(new Rng(3), BossCtx, cfg, isMajor: true);

            int mythics = drops.Count(d => d.Rarity == Rarity.Mythic);
            Assert.InRange(mythics, cfg.Balance.MajorBossUniques.min, cfg.Balance.MajorBossUniques.max);
            Assert.Equal(drops.Count - cfg.Balance.MajorBossExtras, mythics);
        }

        [Fact]
        public void MythicIsTheTopTierInEveryBalanceTable()
        {
            // The invariants the backlog asked of the new tier: one entry per rarity in each
            // table; Mythic drops rarest, rolls the most affixes, salvages for the most scrap.
            var b = Cfg.Balance;
            int top = (int)Rarity.Mythic;
            Assert.Equal(top + 1, b.RarityBaseWeights.Length);
            Assert.Equal(top + 1, b.AffixCountByRarity.Length);
            Assert.Equal(top + 1, b.ScrapValueByRarity.Length);
            for (int r = 0; r < top; r++)
            {
                Assert.True(b.RarityBaseWeights[top] < b.RarityBaseWeights[r], "Mythic must be the rarest drop");
                Assert.True(b.AffixCountByRarity[top].max >= b.AffixCountByRarity[r].max, "Mythic must roll the most affixes");
                Assert.True(b.ScrapValueByRarity[top] > b.ScrapValueByRarity[r], "Mythic must salvage highest");
            }
        }

        [Fact]
        public void MiniBossDropsFewerThanMajorBoss()
        {
            var major = Loot.RollBossDrops(new Rng(2), BossCtx, Cfg, isMajor: true);
            var mini = Loot.RollBossDrops(new Rng(2), BossCtx, Cfg, isMajor: false);
            Assert.True(major.Count > mini.Count);
        }

        [Fact]
        public void BossDropsAreDeterministic()
        {
            var a = Loot.RollBossDrops(new Rng(7), BossCtx, Cfg, isMajor: true);
            var b = Loot.RollBossDrops(new Rng(7), BossCtx, Cfg, isMajor: true);

            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Id, b[i].Id);
                Assert.Equal(a[i].Rarity, b[i].Rarity);
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
