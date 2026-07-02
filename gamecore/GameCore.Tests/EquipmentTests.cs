using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class EquipmentTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Item Mk(string id, string baseId, Rarity rarity, int ilvl,
                               params (StatKey stat, double val)[] affixes)
        {
            var item = new Item { Id = id, BaseId = baseId, Rarity = rarity, ItemLevel = ilvl };
            foreach (var (s, v) in affixes) item.Affixes.Add(new Affix { Stat = s, Value = v });
            return item;
        }

        // a fresh save (warrior in party slot 0) with the given items in the pool
        private static (SaveState save, string heroId) Setup(params Item[] items)
        {
            var save = Inventory.AddItems(Save.NewGame(1, Cfg, 0), items);
            return (save, save.Heroes[0].Id);
        }

        private static StatBlock StatsOf(SaveState save, string heroId)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId)!;
            return Stats.ComputeHeroStats(hero, Cfg, Stats.ResolveEquipped(save, hero));
        }

        [Fact]
        public void EquipRaisesStatsAndIsPure()
        {
            var (save, heroId) = Setup(Mk("w1", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 7)));
            double baseAtk = StatsOf(save, heroId).Get(StatKey.Atk);

            var after = Inventory.EquipItem(save, heroId, "w1", Cfg);

            // rusty_sword base Atk 6 + affix 7 = +13
            Assert.Equal(baseAtk + 13, StatsOf(after, heroId).Get(StatKey.Atk), 3);
            Assert.Empty(save.Heroes.Find(h => h.Id == heroId)!.Equipped); // input untouched
        }

        [Fact]
        public void UnequipRevertsStats()
        {
            var (save, heroId) = Setup(Mk("w1", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 7)));
            double baseAtk = StatsOf(save, heroId).Get(StatKey.Atk);

            var equipped = Inventory.EquipItem(save, heroId, "w1", Cfg);
            var reverted = Inventory.UnequipItem(equipped, heroId, EquipSlot.Weapon);

            Assert.False(reverted.Heroes.Find(h => h.Id == heroId)!.Equipped.ContainsKey(EquipSlot.Weapon));
            Assert.Equal(baseAtk, StatsOf(reverted, heroId).Get(StatKey.Atk), 3);
        }

        [Fact]
        public void SlotIsDerivedFromItemBase()
        {
            var (save, heroId) = Setup(Mk("h1", "leather_cap", Rarity.Normal, 1));
            var after = Inventory.EquipItem(save, heroId, "h1", Cfg);
            var eq = after.Heroes.Find(h => h.Id == heroId)!.Equipped;
            Assert.Equal("h1", eq[EquipSlot.Helm]);
        }

        [Fact]
        public void EquipIntoOccupiedSlotReplacesAndFreesOldItem()
        {
            var (save, heroId) = Setup(
                Mk("w1", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 2)),
                Mk("w2", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 9)));

            var s1 = Inventory.EquipItem(save, heroId, "w1", Cfg);
            var s2 = Inventory.EquipItem(s1, heroId, "w2", Cfg);

            Assert.Equal("w2", s2.Heroes.Find(h => h.Id == heroId)!.Equipped[EquipSlot.Weapon]);
            Assert.Contains(s2.Inventory, i => i.Id == "w1");           // old item still in pool
            // w1 is no longer equipped, so it can be equipped again without throwing
            Inventory.EquipItem(s2, heroId, "w1", Cfg);
        }

        [Fact]
        public void EquipThrowsOnBadInputs()
        {
            var (save, heroId) = Setup(Mk("w1", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 7)));

            Assert.Throws<System.InvalidOperationException>(() => Inventory.EquipItem(save, "nope", "w1", Cfg));
            Assert.Throws<System.InvalidOperationException>(() => Inventory.EquipItem(save, heroId, "ghost", Cfg));

            var equipped = Inventory.EquipItem(save, heroId, "w1", Cfg);
            Assert.Throws<System.InvalidOperationException>(() => Inventory.EquipItem(equipped, heroId, "w1", Cfg));
        }

        [Fact]
        public void ResolveEquippedReturnsEquippedItems()
        {
            var (save, heroId) = Setup(
                Mk("w1", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 7)),
                Mk("h1", "leather_cap", Rarity.Normal, 1));

            var s = Inventory.EquipItem(save, heroId, "w1", Cfg);
            s = Inventory.EquipItem(s, heroId, "h1", Cfg);

            var resolved = Stats.ResolveEquipped(s, s.Heroes.Find(h => h.Id == heroId)!);
            var ids = new HashSet<string>();
            foreach (var it in resolved) ids.Add(it.Id);
            Assert.Equal(new HashSet<string> { "w1", "h1" }, ids);
        }

        [Fact]
        public void CompareForHeroComputesDelta()
        {
            // vs empty slot: +13 atk (base 6 + affix 7)
            var (save, heroId) = Setup();
            var candidate = Mk("c1", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 7));
            Assert.Equal(13, Inventory.CompareForHero(save, heroId, candidate, Cfg).Get(StatKey.Atk), 3);

            // vs a worse weapon equipped: (6+7) - (6+2) = +5 atk
            var (save2, heroId2) = Setup(Mk("small", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 2)));
            save2 = Inventory.EquipItem(save2, heroId2, "small", Cfg);
            Assert.Equal(5, Inventory.CompareForHero(save2, heroId2, candidate, Cfg).Get(StatKey.Atk), 3);
        }

        [Fact]
        public void ComparePairBackingMatchesDeltaAndDrivesDerivedStats()
        {
            // ComparePairForHero is the (before, after) basis behind CompareForHero + the B2
            // DPS/Life hover preview. after-before must agree with the raw delta, and a sword
            // with +Atk must raise derived DPS.
            var (save, heroId) = Setup();
            var candidate = Mk("c1", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 7));

            var (before, after) = Inventory.ComparePairForHero(save, heroId, candidate, Cfg);
            double rawDelta = Inventory.CompareForHero(save, heroId, candidate, Cfg).Get(StatKey.Atk);
            Assert.Equal(rawDelta, after.Get(StatKey.Atk) - before.Get(StatKey.Atk), 9);

            Assert.True(DerivedStats.Dps(after) > DerivedStats.Dps(before)); // more Atk => more DPS
        }

        // --- the 5-slot set (trimmed from 9, 2026-07-02) ---

        [Fact]
        public void EveryActiveSlotHasAtLeastOneItemBaseAndLegacySlotsHaveNone()
        {
            // loot picks bases uniformly, so each active slot needs gear to ever drop/equip
            Assert.Equal(5, EquipSlots.Active.Length); // Weapon/Helm/Chest/Boots/Gloves
            foreach (var slot in EquipSlots.Active)
                Assert.Contains(Cfg.ItemBases.Values, b => b.Slot == slot);
            // the retired members exist for save deserialization ONLY — nothing drops there
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
                if (System.Array.IndexOf(EquipSlots.Active, slot) < 0)
                    Assert.DoesNotContain(Cfg.ItemBases.Values, b => b.Slot == slot);
        }

        [Theory]
        [InlineData("rusty_sword", EquipSlot.Weapon)]
        [InlineData("leather_cap", EquipSlot.Helm)]
        [InlineData("leather_vest", EquipSlot.Chest)]
        [InlineData("leather_gloves", EquipSlot.Gloves)]
        [InlineData("leather_boots", EquipSlot.Boots)]
        public void NewSlotItemsEquipIntoTheirSlot(string baseId, EquipSlot slot)
        {
            var (save, heroId) = Setup(Mk("x", baseId, Rarity.Normal, 1));
            var after = Inventory.EquipItem(save, heroId, "x", Cfg);
            Assert.Equal("x", after.Heroes.Find(h => h.Id == heroId)!.Equipped[slot]);
        }

        [Fact]
        public void HeroCanFillMultipleSlotsFromTheSharedPool()
        {
            var (save, heroId) = Setup(
                Mk("a", "rusty_sword", Rarity.Normal, 1),
                Mk("b", "leather_vest", Rarity.Normal, 1),
                Mk("c", "leather_cap", Rarity.Normal, 1),
                Mk("d", "leather_gloves", Rarity.Normal, 1),
                Mk("e", "leather_boots", Rarity.Normal, 1));

            var s = save;
            foreach (var id in new[] { "a", "b", "c", "d", "e" }) s = Inventory.EquipItem(s, heroId, id, Cfg);

            var eq = s.Heroes.Find(h => h.Id == heroId)!.Equipped;
            Assert.Equal(5, eq.Count); // every slot filled at once
        }

        [Fact]
        public void TwoHeroesShareOnePoolButCannotWearTheSameItem()
        {
            // shared account inventory: equipping on hero A makes the item unavailable to B
            var save = Inventory.AddItems(Save.NewGame(1, Cfg, 0), new[] { Mk("w1", "rusty_sword", Rarity.Rare, 5) });
            save = Party.AcquireHero(save, "magician_basic", Cfg, "h2"); // a second owned hero
            string a = save.Heroes[0].Id, b = save.Heroes[1].Id;

            save = Inventory.EquipItem(save, a, "w1", Cfg);
            Assert.Throws<System.InvalidOperationException>(() => Inventory.EquipItem(save, b, "w1", Cfg));
        }

        [Fact]
        public void PartyPowerIncreasesAfterEquipping()
        {
            var (save, heroId) = Setup(Mk("w1", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 9)));
            double before = Stats.ComputePartyPower(save, Cfg);

            var after = Inventory.EquipItem(save, heroId, "w1", Cfg);
            Assert.True(Stats.ComputePartyPower(after, Cfg) > before);
        }
    }
}
