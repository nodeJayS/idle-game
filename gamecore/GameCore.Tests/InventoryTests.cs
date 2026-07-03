using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class InventoryTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Item It(string id, Rarity rarity = Rarity.Rare, string baseId = "rusty_sword", int ilvl = 1) =>
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

        // --- Reforge (item shop) ---

        private static SaveState RichWith(Item item)
        {
            var s = Save.NewGame(1, Cfg, 0);
            s.Inventory.Add(item);
            s.Currencies["gold"] = 100_000_000;
            s.Currencies["scrap"] = 100_000_000;
            return s;
        }

        private static Item AtkItem(double value, int ilvl = 10)
        {
            var it = It("rf", Rarity.Rare, "rusty_sword", ilvl);
            it.Affixes.Add(new Affix { Stat = StatKey.Atk, Value = value });
            return it;
        }

        [Fact]
        public void ReforgeSpendsGoldAndScrap()
        {
            var s = RichWith(AtkItem(5));
            var (g, sc) = Inventory.ReforgeCost(s.Inventory[s.Inventory.Count - 1], Cfg);
            var after = Inventory.Reforge(s, "rf", Cfg);
            Assert.Equal(100_000_000 - g, after.Currencies["gold"]);
            Assert.Equal(100_000_000 - sc, after.Currencies["scrap"]);
        }

        [Fact]
        public void ReforgeIsNoopWhenUnaffordable()
        {
            var s = RichWith(AtkItem(5));
            s.Currencies["gold"] = 0; s.Currencies["scrap"] = 0;
            Assert.Same(s, Inventory.Reforge(s, "rf", Cfg)); // can't afford -> shares ref
        }

        [Fact]
        public void ReforgeKeepsAffixesWithinLegitRange()
        {
            var def = Cfg.AffixPool.Find(d => d.Stat == StatKey.Atk)!;
            double min = def.ValueMinPerItemLevel * 10, max = def.ValueMaxPerItemLevel * 10;
            var s = RichWith(AtkItem(max, 10)); // start at the ceiling
            for (int i = 0; i < 40; i++) s = Inventory.Reforge(s, "rf", Cfg);
            double v = s.Inventory.Find(i => i.Id == "rf")!.Affixes.Find(a => a.Stat == StatKey.Atk)!.Value;
            Assert.InRange(v, min, max); // random-walk stays clamped to the item's legit roll range
        }

        [Fact]
        public void ReforgeLeavesImprintAffixesUntouched()
        {
            var it = AtkItem(5, 10);
            it.Affixes.Add(new Affix { Stat = StatKey.SplashRadius, Value = 1.2 }); // imprint (not in pool)
            var after = Inventory.Reforge(RichWith(it), "rf", Cfg);
            double splash = after.Inventory.Find(i => i.Id == "rf")!.Affixes.Find(a => a.Stat == StatKey.SplashRadius)!.Value;
            Assert.Equal(1.2, splash); // imprint preserved
        }

        [Fact]
        public void CannotReforgeAnItemWithNoNormalAffixes()
        {
            var it = It("imp", Rarity.Rare, "rusty_sword", 10);
            it.Affixes.Add(new Affix { Stat = StatKey.SplashRadius, Value = 1.2 }); // imprint only
            Assert.False(Inventory.CanReforge(RichWith(it), "imp", Cfg));
        }

        [Fact]
        public void ReforgeIsPureAndAdvancesTheCursor()
        {
            var s = RichWith(AtkItem(5));
            var after = Inventory.Reforge(s, "rf", Cfg);
            Assert.NotEqual(s.RngCursor, after.RngCursor);                 // roll persisted
            Assert.NotSame(s.Inventory, after.Inventory);                  // new list
            Assert.Equal(5, s.Inventory.Find(i => i.Id == "rf")!.Affixes.Find(a => a.Stat == StatKey.Atk)!.Value); // input untouched
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
            var items = new[] { It("n", Rarity.Normal), It("r", Rarity.Rare), It("u", Rarity.Unique) };

            var res = Inventory.AddLoot(save, items, Cfg, Rarity.Rare); // salvage Normal + Rare, keep Unique

            Assert.Equal(2, res.Salvaged.Count);
            Assert.Single(res.Stored);
            Assert.Equal("u", res.Stored[0].Id);
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

        [Fact]
        public void SalvageAllScrapsEveryLooseUnlockedItemRegardlessOfRarity()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[]
            {
                It("n", Rarity.Normal, "rusty_sword", 2),
                It("r", Rarity.Rare, "rusty_sword", 3),
                It("u", Rarity.Unique, "rusty_sword", 4),
                It("L", Rarity.Legendary, "rusty_sword", 5),
            });
            long expect = Cfg.Balance.ScrapValue(Rarity.Normal, 2) + Cfg.Balance.ScrapValue(Rarity.Rare, 3)
                        + Cfg.Balance.ScrapValue(Rarity.Unique, 4) + Cfg.Balance.ScrapValue(Rarity.Legendary, 5);

            var (next, count, scrap) = Inventory.SalvageAll(save, Cfg);

            Assert.Equal(4, count); // uncapped: even the Legendary goes
            Assert.Equal(expect, scrap);
            Assert.Equal(expect, next.Currencies["scrap"]);
            Assert.Empty(next.Inventory);
            Assert.Equal(4, save.Inventory.Count); // input untouched (pure)
        }

        [Fact]
        public void SalvageAllNeverTouchesEquippedGear()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[]
            {
                It("worn", Rarity.Normal, "rusty_sword"),
                It("loose", Rarity.Legendary, "leather_cap"),
            });
            save = Inventory.EquipItem(save, "h1", "worn", Cfg);

            var (next, count, _) = Inventory.SalvageAll(save, Cfg);

            Assert.Equal(1, count);
            Assert.Contains(next.Inventory, i => i.Id == "worn");      // equipped survives
            Assert.DoesNotContain(next.Inventory, i => i.Id == "loose");
        }

        [Fact]
        public void SalvageAllSkipsLockedItemButScrapsUnlockedTwin()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[]
            {
                It("keep", Rarity.Legendary, "rusty_sword", 5),
                It("dies", Rarity.Legendary, "rusty_sword", 5),
            });
            save = Inventory.ToggleLock(save, "keep"); // lock one of the identical twins
            long expect = Cfg.Balance.ScrapValue(Rarity.Legendary, 5); // only the unlocked one

            var (next, count, scrap) = Inventory.SalvageAll(save, Cfg);

            Assert.Equal(1, count);
            Assert.Equal(expect, scrap);
            Assert.Contains(next.Inventory, i => i.Id == "keep");      // locked survives
            Assert.DoesNotContain(next.Inventory, i => i.Id == "dies"); // unlocked twin dies
        }

        [Fact]
        public void SalvageAllWithNoMatchesIsANoOp()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("u", Rarity.Unique) });
            save = Inventory.ToggleLock(save, "u"); // only item is locked => nothing to salvage

            var (next, count, scrap) = Inventory.SalvageAll(save, Cfg);

            Assert.Equal(0, count);
            Assert.Equal(0, scrap);
            Assert.Same(save, next); // unchanged input handed back
        }

        // --- lock (per-item salvage protection) ---

        [Fact]
        public void ToggleLockFlipsAndIsPure()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("a") });

            var locked = Inventory.ToggleLock(save, "a");
            Assert.True(locked.Inventory.Find(i => i.Id == "a")!.Locked);
            Assert.False(save.Inventory.Find(i => i.Id == "a")!.Locked); // input untouched
            Assert.NotSame(save.Inventory, locked.Inventory);

            var unlocked = Inventory.ToggleLock(locked, "a"); // round-trip back to false
            Assert.False(unlocked.Inventory.Find(i => i.Id == "a")!.Locked);
        }

        [Fact]
        public void ToggleLockUnknownIdIsNoOp()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("a") });
            var next = Inventory.ToggleLock(save, "nope");
            Assert.Same(save, next);
        }

        [Fact]
        public void ToggleLockWorksOnEquippedGearAndSurvivesUnequip()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("w", Rarity.Rare, "rusty_sword") });
            save = Inventory.EquipItem(save, "h1", "w", Cfg);

            save = Inventory.ToggleLock(save, "w"); // lock it while equipped
            Assert.True(save.Inventory.Find(i => i.Id == "w")!.Locked);

            save = Inventory.UnequipItem(save, "h1", EquipSlot.Weapon); // back to the bag
            Assert.True(save.Inventory.Find(i => i.Id == "w")!.Locked); // still locked
        }

        [Fact]
        public void ToggleLockPersistsThroughSaveLoadJson()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("a") });
            save = Inventory.ToggleLock(save, "a");

            var json = Persistence.Serialize(save);
            var round = Persistence.Deserialize(json);

            Assert.True(round.Inventory.Find(i => i.Id == "a")!.Locked);
        }

        [Fact]
        public void OldSaveWithoutLockedFieldDeserializesUnlocked()
        {
            // An item JSON that predates the Locked field must default to false (no SaveVersion bump).
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("a") });
            var json = Persistence.Serialize(save).Replace(",\"Locked\":false", ""); // strip the new field
            var round = Persistence.Deserialize(json);
            Assert.False(round.Inventory.Find(i => i.Id == "a")!.Locked);
        }

        [Fact]
        public void SalvageItemRefusesLockedItem()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { It("a") });
            save = Inventory.ToggleLock(save, "a");
            Assert.Throws<System.InvalidOperationException>(() => Inventory.SalvageItem(save, "a", Cfg));
        }

        [Fact]
        public void AutoSalvageThresholdSkipsLockedDrop()
        {
            // The auto-salvage-on-drop path (AddLoot) must not scrap a locked item, even below threshold.
            var save = Save.NewGame(1, Cfg, 0);
            var locked = It("drop", Rarity.Normal, "rusty_sword", 1);
            locked.Locked = true;
            var loot = Inventory.AddLoot(save, new[] { locked }, Cfg, Rarity.Rare); // threshold would scrap a Normal

            Assert.Empty(loot.Salvaged);                          // locked => not auto-salvaged
            Assert.Contains(loot.Stored, i => i.Id == "drop");    // stored in the bag instead
            Assert.Contains(loot.Save.Inventory, i => i.Id == "drop");
        }

        [Fact]
        public void DisplacedLockedItemReturnsToBagStillLocked()
        {
            // Auto-equip displaces the item in the target slot back to the pool; a locked one stays locked.
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[]
            {
                It("old", Rarity.Rare, "rusty_sword"),
                It("new", Rarity.Rare, "rusty_sword"),
            });
            save = Inventory.EquipItem(save, "h1", "old", Cfg);
            save = Inventory.ToggleLock(save, "old"); // lock the equipped item

            save = Inventory.EquipItem(save, "h1", "new", Cfg); // displaces "old" back to the pool

            Assert.True(save.Inventory.Find(i => i.Id == "old")!.Locked); // still locked in the bag
        }
    }
}
