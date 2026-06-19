using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class InventoryTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Item It(string id, Rarity rarity = Rarity.Magic, string baseId = "rusty_sword", int ilvl = 1) =>
            new Item { Id = id, BaseId = baseId, Rarity = rarity, ItemLevel = ilvl };

        private static GameConfig CapCfg(int cap)
        {
            var c = GameConfig.Default();
            c.Balance.InventoryCap = cap;
            return c;
        }

        [Fact]
        public void AddItemsAppendsAndIsPure()
        {
            var save = Save.NewGame(1, Cfg, 0);
            int before = save.Inventory.Count;
            var items = new List<Item> { It("a"), It("b") };

            var next = Inventory.AddItems(save, items);

            Assert.Equal(before + 2, next.Inventory.Count);
            Assert.Equal(before, save.Inventory.Count);     // input untouched
            Assert.NotSame(save.Inventory, next.Inventory);  // new list, not aliased
            Assert.Contains(next.Inventory, i => i.Id == "a");
            Assert.Contains(next.Inventory, i => i.Id == "b");
        }

        [Fact]
        public void AddItemsEmptyIsNoOpEquivalent()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var next = Inventory.AddItems(save, new List<Item>());

            Assert.Equal(save.Inventory.Count, next.Inventory.Count);
            Assert.NotSame(save.Inventory, next.Inventory);
        }

        // --- M10.2: inventory cap + auto-salvage ---

        [Fact]
        public void AddLootStoresItemsWhenRoom()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var res = Inventory.AddLoot(save, new[] { It("a"), It("b") }, Cfg, null);

            Assert.Equal(2, res.Stored.Count);
            Assert.Empty(res.Salvaged);
            Assert.Empty(res.Rejected);
            Assert.Equal(2, res.Save.Inventory.Count);
        }

        [Fact]
        public void AddLootEnforcesCapAndRejectsOverflow()
        {
            var cfg = CapCfg(2);
            var save = Save.NewGame(1, cfg, 0);
            var res = Inventory.AddLoot(save, new[] { It("a"), It("b"), It("c") }, cfg, null);

            Assert.Equal(2, res.Stored.Count);
            Assert.Single(res.Rejected);
            Assert.Equal("c", res.Rejected[0].Id); // last drop left behind
            Assert.Equal(2, res.Save.Inventory.Count);
            Assert.True(res.BagFull);
        }

        [Fact]
        public void AddLootAutoSalvagesAtOrBelowThreshold()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var items = new[] { It("n", Rarity.Normal), It("m", Rarity.Magic), It("r", Rarity.Rare) };

            var res = Inventory.AddLoot(save, items, Cfg, Rarity.Magic); // salvage Normal + Magic, keep Rare

            Assert.Equal(2, res.Salvaged.Count);
            Assert.Single(res.Stored);
            Assert.Equal("r", res.Stored[0].Id);
            Assert.True(res.ScrapGained > 0);
            Assert.Equal(res.ScrapGained, res.Save.Currencies["scrap"]);
        }

        [Fact]
        public void AutoSalvageNeverDestroysOwnedItemsWhenFull()
        {
            // bag full of legendaries; nothing already owned may be removed
            var cfg = CapCfg(2);
            var save = Save.NewGame(1, cfg, 0);
            save = Inventory.AddItems(save, new[] { It("L1", Rarity.Legendary), It("L2", Rarity.Legendary) });

            // incoming Normal at/below threshold => salvaged to scrap, legendaries intact
            var res = Inventory.AddLoot(save, new[] { It("n", Rarity.Normal) }, cfg, Rarity.Normal);
            Assert.Single(res.Salvaged);
            Assert.Empty(res.Rejected);
            Assert.Equal(2, res.Save.Inventory.Count);

            // a Unique (above threshold) with a full bag => rejected, still nothing destroyed
            var res2 = Inventory.AddLoot(save, new[] { It("u", Rarity.Unique) }, cfg, Rarity.Normal);
            Assert.Single(res2.Rejected);
            Assert.Equal(2, res2.Save.Inventory.Count);
            Assert.Contains(res2.Save.Inventory, i => i.Id == "L1");
            Assert.Contains(res2.Save.Inventory, i => i.Id == "L2");
        }

        [Fact]
        public void EquippedItemsDoNotCountTowardCap()
        {
            var cfg = CapCfg(1);
            var save = Save.NewGame(1, cfg, 0);
            save = Inventory.AddItems(save, new[] { It("w", Rarity.Rare, "rusty_sword") });
            save = Inventory.EquipItem(save, "h1", "w", cfg);

            Assert.Equal(0, Inventory.LooseCount(save)); // equipped item isn't loose

            var res = Inventory.AddLoot(save, new[] { It("a", Rarity.Rare) }, cfg, null);
            Assert.Single(res.Stored); // cap-1 still has room because the worn sword doesn't count
            Assert.Empty(res.Rejected);
        }

        [Fact]
        public void AddLootOverflowStoresPastCap()
        {
            var cfg = CapCfg(2);
            var save = Save.NewGame(1, cfg, 0);

            // idle / boss / special-stage rewards: allowOverflow stores everything past the cap
            var res = Inventory.AddLoot(save, new[] { It("a"), It("b"), It("c"), It("d") }, cfg, null, allowOverflow: true);

            Assert.Equal(4, res.Stored.Count);
            Assert.Empty(res.Rejected);
            Assert.Equal(4, res.Save.Inventory.Count); // 4 > cap 2, allowed to overfill
        }

        [Fact]
        public void AddLootIsPure()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var res = Inventory.AddLoot(save, new[] { It("a") }, Cfg, null);

            Assert.Empty(save.Inventory);                       // input untouched
            Assert.NotSame(save.Inventory, res.Save.Inventory);
            Assert.NotSame(save.Currencies, res.Save.Currencies);
        }

        [Fact]
        public void SalvageItemConvertsLooseItemToScrap()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("a", Rarity.Rare, "rusty_sword", 5) });
            long expect = Cfg.Balance.ScrapValue(Rarity.Rare, 5);

            var next = Inventory.SalvageItem(save, "a", Cfg);

            Assert.DoesNotContain(next.Inventory, i => i.Id == "a");
            Assert.Equal(expect, next.Currencies["scrap"]);
            Assert.Single(save.Inventory); // original untouched
        }

        [Fact]
        public void SalvageItemRefusesEquipped()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("w", Rarity.Rare, "rusty_sword") });
            save = Inventory.EquipItem(save, "h1", "w", Cfg);

            Assert.Throws<System.InvalidOperationException>(() => Inventory.SalvageItem(save, "w", Cfg));
        }
    }
}
