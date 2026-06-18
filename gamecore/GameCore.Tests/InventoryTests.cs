using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class InventoryTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Item It(string id) =>
            new Item { Id = id, BaseId = "rusty_sword", Rarity = Rarity.Magic, ItemLevel = 1 };

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
    }
}
