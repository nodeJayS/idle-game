using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Gear enhancement (+N, 2026-07-02): +5% of the item BASE's stats per
    /// level (affixes untouched — rolls are Reforge's domain), escalating scrap cost,
    /// safe to +5, failable to +9, level-dropping fails from +10. Rng-cursor gamble.</summary>
    public class EnhanceTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static SaveState WithItem(int enhance = 0, long scrap = 1_000_000)
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { new Item
            {
                Id = "w", BaseId = "rusty_sword", Rarity = Rarity.Rare, ItemLevel = 10,
                Affixes = new List<Affix> { new Affix { Stat = StatKey.Atk, Value = 7 } },
                Enhance = enhance,
            }});
            save.Currencies["scrap"] = scrap;
            return save;
        }

        [Fact]
        public void EnhanceScalesBaseStatsOnlyInHeroStats()
        {
            var save = WithItem(enhance: 10); // +10 => base x1.5
            save = Inventory.EquipItem(save, save.Heroes[0].Id, "w", Cfg);
            var hero = save.Heroes[0];

            var st = Stats.ComputeHeroStats(hero, Cfg, Stats.ResolveEquipped(save, hero));
            double heroBase = Cfg.Heroes[hero.DefId].BaseStats.Get(StatKey.Atk);
            double swordBase = Cfg.ItemBases["rusty_sword"].BaseStats.Get(StatKey.Atk); // 6

            // base x1.5, affix (+7) NOT multiplied
            Assert.Equal(heroBase + swordBase * 1.5 + 7, st.Get(StatKey.Atk), 9);
        }

        [Fact]
        public void SafeLevelsAlwaysLandAndSpendScrap()
        {
            var save = WithItem();
            long scrap0 = save.Currencies["scrap"];
            long expectedCost = Cfg.Balance.EnhanceCost(save.Inventory[0]);

            var r = Inventory.Enhance(save, "w", Cfg)!;
            Assert.True(r.Success);           // +1 is in the guaranteed band
            Assert.Equal(1, r.Level);
            Assert.Equal(expectedCost, r.Cost);
            Assert.Equal(scrap0 - expectedCost, r.Save.Currencies["scrap"]);
            Assert.Equal(1, r.Save.Inventory.Find(i => i.Id == "w")!.Enhance);
            Assert.True(r.Save.RngCursor > save.RngCursor); // gamble consumed the cursor
        }

        [Fact]
        public void CostEscalatesPerLevel()
        {
            var s0 = WithItem(enhance: 0);
            var s9 = WithItem(enhance: 9);
            Assert.True(Cfg.Balance.EnhanceCost(s9.Inventory[0]) > Cfg.Balance.EnhanceCost(s0.Inventory[0]) * 5);
        }

        [Fact]
        public void MidTierFailureKeepsTheLevelHighTierFailureDropsIt()
        {
            // Drive attempts with many different seeds until both outcomes appear —
            // deterministic per seed, so this can't flake once the seeds are fixed.
            bool sawMidFail = false, sawHighDrop = false;
            for (uint seed = 1; seed <= 200 && !(sawMidFail && sawHighDrop); seed++)
            {
                var mid = WithItem(enhance: 8);   // attempting +9: failable, no drop
                mid.RngSeed = seed;
                var rm = Inventory.Enhance(mid, "w", Cfg)!;
                if (!rm.Success)
                {
                    Assert.False(rm.Dropped);
                    Assert.Equal(8, rm.Level);
                    Assert.Equal(8, rm.Save.Inventory.Find(i => i.Id == "w")!.Enhance);
                    sawMidFail = true;
                }

                var high = WithItem(enhance: 12); // attempting +13: fails drop a level
                high.RngSeed = seed;
                var rh = Inventory.Enhance(high, "w", Cfg)!;
                if (!rh.Success)
                {
                    Assert.True(rh.Dropped);
                    Assert.Equal(11, rh.Level);
                    sawHighDrop = true;
                }
            }
            Assert.True(sawMidFail && sawHighDrop, "expected both outcome kinds across 200 seeds");
        }

        [Fact]
        public void RefusesAtMaxOrWhenBroke()
        {
            Assert.Null(Inventory.Enhance(WithItem(enhance: Cfg.Balance.EnhanceMax), "w", Cfg));
            Assert.Null(Inventory.Enhance(WithItem(scrap: 0), "w", Cfg));
            Assert.False(CanEnhanceBroke());
        }

        private static bool CanEnhanceBroke() => Inventory.CanEnhance(WithItem(scrap: 0), "w", Cfg);

        [Fact]
        public void Plus15LiftOverPlus9StaysProportionate()
        {
            // The +15 extension (10.17) must not introduce a stat CLIFF: enhance scales the item BASE
            // linearly (1 + EnhanceBasePctPerLevel·N), so +15/+9 lift is exactly (1+.05·15)/(1+.05·9)
            // = 1.75/1.45 ≈ 1.207 — a smooth continuation, not a spike.
            double lift(int n) => 1.0 + Cfg.Balance.EnhanceBasePctPerLevel * n;
            double ratio = lift(15) / lift(9);
            Assert.Equal(1.75 / 1.45, ratio, 9);
            Assert.InRange(ratio, 1.15, 1.30); // proportionate step, no cliff

            // And the odds/cost tables actually reach +15: one entry per level, cost strictly climbing.
            Assert.Equal(Cfg.Balance.EnhanceMax, Cfg.Balance.EnhanceSuccess.Length);
            for (int e = 1; e < Cfg.Balance.EnhanceMax; e++)
            {
                var lo = new Item { Rarity = Rarity.Mythic, ItemLevel = 100, Enhance = e - 1 };
                var hi = new Item { Rarity = Rarity.Mythic, ItemLevel = 100, Enhance = e };
                Assert.True(Cfg.Balance.EnhanceCost(hi) > Cfg.Balance.EnhanceCost(lo), $"cost must climb at +{e + 1}");
            }
        }

        [Fact]
        public void ReachesPlus15WithDropOnFailStillActive()
        {
            // A +14 item CAN attempt +15 (not capped early), and a fail at that high tier still DROPS a
            // level (the drop-on-fail band extends past +9 unchanged).
            bool sawSuccess = false, sawDrop = false;
            for (uint seed = 1; seed <= 200 && !(sawSuccess && sawDrop); seed++)
            {
                var s = WithItem(enhance: 14); // attempting +15
                s.RngSeed = seed;
                Assert.True(Inventory.CanEnhance(s, "w", Cfg)); // +14 is below the cap, attemptable
                var r = Inventory.Enhance(s, "w", Cfg)!;
                if (r.Success) { Assert.Equal(15, r.Level); sawSuccess = true; }
                else { Assert.True(r.Dropped); Assert.Equal(13, r.Level); sawDrop = true; }
            }
            Assert.True(sawSuccess && sawDrop, "expected both a +15 land and a drop-on-fail across 200 seeds");
        }

        [Fact]
        public void ReforgePreservesTheEnhanceLevel()
        {
            var save = WithItem(enhance: 7);
            save.Currencies["gold"] = 1_000_000;
            var after = Inventory.Reforge(save, "w", Cfg);
            Assert.Equal(7, after.Inventory.Find(i => i.Id == "w")!.Enhance);
        }

        [Fact]
        public void EnhanceSurvivesSaveRoundTrip()
        {
            var save = WithItem(enhance: 5);
            var reloaded = Persistence.Deserialize(Persistence.Serialize(save));
            Assert.Equal(5, reloaded.Inventory.Find(i => i.Id == "w")!.Enhance);
        }
    }
}
