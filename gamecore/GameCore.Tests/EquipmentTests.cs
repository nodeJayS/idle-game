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
            var (save, heroId) = Setup(Mk("w1", "rusty_sword", Rarity.Magic, 5, (StatKey.Atk, 7)));
            double baseAtk = StatsOf(save, heroId).Get(StatKey.Atk);

            var after = Inventory.EquipItem(save, heroId, "w1", Cfg);

            // rusty_sword base Atk 6 + affix 7 = +13
            Assert.Equal(baseAtk + 13, StatsOf(after, heroId).Get(StatKey.Atk), 3);
            Assert.Empty(save.Heroes.Find(h => h.Id == heroId)!.Equipped); // input untouched
        }

        [Fact]
        public void UnequipRevertsStats()
        {
            var (save, heroId) = Setup(Mk("w1", "rusty_sword", Rarity.Magic, 5, (StatKey.Atk, 7)));
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
                Mk("w1", "rusty_sword", Rarity.Magic, 5, (StatKey.Atk, 2)),
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
            var (save, heroId) = Setup(Mk("w1", "rusty_sword", Rarity.Magic, 5, (StatKey.Atk, 7)));

            Assert.Throws<System.InvalidOperationException>(() => Inventory.EquipItem(save, "nope", "w1", Cfg));
            Assert.Throws<System.InvalidOperationException>(() => Inventory.EquipItem(save, heroId, "ghost", Cfg));

            var equipped = Inventory.EquipItem(save, heroId, "w1", Cfg);
            Assert.Throws<System.InvalidOperationException>(() => Inventory.EquipItem(equipped, heroId, "w1", Cfg));
        }

        [Fact]
        public void ResolveEquippedReturnsEquippedItems()
        {
            var (save, heroId) = Setup(
                Mk("w1", "rusty_sword", Rarity.Magic, 5, (StatKey.Atk, 7)),
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
            var candidate = Mk("c1", "rusty_sword", Rarity.Magic, 5, (StatKey.Atk, 7));
            Assert.Equal(13, Inventory.CompareForHero(save, heroId, candidate, Cfg).Get(StatKey.Atk), 3);

            // vs a worse weapon equipped: (6+7) - (6+2) = +5 atk
            var (save2, heroId2) = Setup(Mk("small", "rusty_sword", Rarity.Magic, 5, (StatKey.Atk, 2)));
            save2 = Inventory.EquipItem(save2, heroId2, "small", Cfg);
            Assert.Equal(5, Inventory.CompareForHero(save2, heroId2, candidate, Cfg).Get(StatKey.Atk), 3);
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
