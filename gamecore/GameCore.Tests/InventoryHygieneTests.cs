using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>PruneUnknownGear (the slot-trim migration: legacy items dissolve into a
    /// scrap refund) and Sort (the bag's persisted reading order).</summary>
    public class InventoryHygieneTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Item Mk(string id, string baseId, Rarity r, int ilvl) => new Item
        {
            Id = id, BaseId = baseId, Rarity = r, ItemLevel = ilvl,
        };

        [Fact]
        public void PruneDissolvesUnknownBasesIntoScrapAndUnequipsThem()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[]
            {
                Mk("keep", "rusty_sword", Rarity.Rare, 5),
                Mk("dead1", "copper_ring", Rarity.Rare, 5),    // base deleted in the slot trim
                Mk("dead2", "bone_amulet", Rarity.Unique, 9),
            });
            save = Inventory.EquipItem(save, save.Heroes[0].Id, "keep", Cfg);
            // legacy save state: a removed-slot item sits equipped (slot value 4 = old Ring)
            save.Heroes[0].Equipped[(EquipSlot)4] = "dead1";

            long scrapBefore = save.Currencies.TryGetValue("scrap", out var s0) ? s0 : 0;
            var next = Inventory.PruneUnknownGear(save, Cfg);

            Assert.Single(next.Inventory, i => i.Id == "keep");
            Assert.DoesNotContain(next.Inventory, i => i.Id == "dead1" || i.Id == "dead2");
            Assert.DoesNotContain(next.Heroes[0].Equipped.Values, v => v == "dead1");
            Assert.Equal("keep", next.Heroes[0].Equipped[EquipSlot.Weapon]); // survivors untouched

            long expectedRefund = Cfg.Balance.ScrapValue(Rarity.Rare, 5)
                                + Cfg.Balance.ScrapValue(Rarity.Unique, 9);
            Assert.Equal(scrapBefore + expectedRefund, next.Currencies["scrap"]);

            Assert.Same(next, Inventory.PruneUnknownGear(next, Cfg)); // clean save: no-op
        }

        [Fact]
        public void LegacySlotKeysStillDeserialize()
        {
            // The 2026-07-02 slot trim retired Ring/Amulet/Offhand/Cape, but saves
            // persist Equipped keys by enum NAME — deleting the members made old saves
            // fail to parse and silently fall back to New Game. The members stay
            // declared (load-compat only); this guards the round-trip.
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { Mk("legacy", "copper_ring", Rarity.Rare, 5) });
            save.Heroes[0].Equipped[EquipSlot.Ring] = "legacy";

            var reloaded = Persistence.Deserialize(Persistence.Serialize(save));
            Assert.Equal("legacy", reloaded.Heroes[0].Equipped[EquipSlot.Ring]);

            var pruned = Inventory.PruneUnknownGear(reloaded, Cfg);
            Assert.Empty(pruned.Heroes[0].Equipped);
            Assert.Empty(pruned.Inventory);
        }

        [Fact]
        public void SortOrdersByRarityThenItemLevelAndIsStable()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[]
            {
                Mk("c", "rusty_sword", Rarity.Normal, 12),
                Mk("a", "leather_cap", Rarity.Legendary, 3),
                Mk("d", "leather_boots", Rarity.Rare, 3),
                Mk("b", "leather_vest", Rarity.Rare, 9),
            });

            var sorted = Inventory.Sort(save, Cfg);
            Assert.Equal(new[] { "a", "b", "d", "c" }, sorted.Inventory.Select(i => i.Id).ToArray());

            var again = Inventory.Sort(sorted, Cfg); // idempotent order
            Assert.Equal(sorted.Inventory.Select(i => i.Id), again.Inventory.Select(i => i.Id));
        }
    }
}
