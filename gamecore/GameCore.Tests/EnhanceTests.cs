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
