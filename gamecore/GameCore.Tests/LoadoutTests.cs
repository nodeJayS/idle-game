using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// 10.5e loadout snapshots: SaveSnapshot copies the outfit; Apply re-equips every piece
    /// that survives the bag-integrity checks and never strips a slot; stale entries skip
    /// silently (the sweep contract); the snapshot survives the HeroInstance copy sites.
    /// </summary>
    public class LoadoutTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Item It(string id, string baseId) =>
            new Item { Id = id, BaseId = baseId, Rarity = Rarity.Rare, ItemLevel = 5 };

        private static SaveState Fresh(out string heroId)
        {
            var save = Save.NewGame(1, Cfg, 0);
            heroId = save.Heroes[0].Id;
            return save;
        }

        private static SaveState GiveAndEquip(SaveState save, string heroId, params (string id, string baseId)[] items)
        {
            var list = new List<Item>();
            foreach (var (id, baseId) in items) list.Add(It(id, baseId));
            save = Inventory.AddItems(save, list);
            foreach (var (id, _) in items) save = Inventory.EquipItem(save, heroId, id, Cfg);
            return save;
        }

        [Fact]
        public void SaveThenApplyRoundTripsAfterASwap()
        {
            var save = Fresh(out var h);
            save = GiveAndEquip(save, h, ("sw", "rusty_sword"), ("cap", "leather_cap"));
            save = Loadouts.SaveSnapshot(save, h);

            // Swap the weapon away, then wear the snapshot again.
            save = Inventory.AddItems(save, new[] { It("sw2", "rusty_sword") });
            save = Inventory.EquipItem(save, h, "sw2", Cfg);
            var (next, applied, skipped) = Loadouts.Apply(save, h, Cfg);

            Assert.Equal(1, applied); // the weapon came back; the cap never left (not counted)
            Assert.Equal(0, skipped);
            var hero = next.Heroes.Find(x => x.Id == h);
            Assert.Equal("sw", hero.Equipped[EquipSlot.Weapon]);
            Assert.Equal("cap", hero.Equipped[EquipSlot.Helm]);
        }

        [Fact]
        public void SnapshotIsACopyNotALiveReference()
        {
            var save = Fresh(out var h);
            save = GiveAndEquip(save, h, ("sw", "rusty_sword"));
            save = Loadouts.SaveSnapshot(save, h);

            save = Inventory.AddItems(save, new[] { It("sw2", "rusty_sword") });
            save = Inventory.EquipItem(save, h, "sw2", Cfg); // mutating the outfit…
            var hero = save.Heroes.Find(x => x.Id == h);
            Assert.Equal("sw", hero.Loadout[EquipSlot.Weapon]); // …must not rewrite the snapshot
        }

        [Fact]
        public void ApplySkipsSalvagedAndForeignWornPiecesButAppliesTheRest()
        {
            var save = Fresh(out var h);
            save = GiveAndEquip(save, h, ("sw", "rusty_sword"), ("cap", "leather_cap"), ("vest", "leather_vest"));
            save = Loadouts.SaveSnapshot(save, h);

            // Unequip everything, salvage the sword, and let another hero grab the cap.
            save = Inventory.UnequipItem(save, h, EquipSlot.Weapon);
            save = Inventory.UnequipItem(save, h, EquipSlot.Helm);
            save = Inventory.UnequipItem(save, h, EquipSlot.Chest);
            save = Inventory.SalvageItem(save, "sw", Cfg);
            save = Progression.SyncHeroUnlocks(save, Cfg); // make sure a second hero exists on a fresh save?
            string other = null;
            foreach (var hh in save.Heroes) if (hh.Id != h) other = hh.Id;
            if (other == null)
            {
                // fresh saves start with one hero — fabricate the conflict via a bare instance
                save.Heroes.Add(new HeroInstance { Id = "h_other", DefId = save.Heroes[0].DefId, Level = 1 });
                other = "h_other";
            }
            save = Inventory.EquipItem(save, other, "cap", Cfg);

            var (next, applied, skipped) = Loadouts.Apply(save, h, Cfg);
            Assert.Equal(1, applied);  // the vest
            Assert.Equal(2, skipped);  // salvaged sword + foreign-worn cap
            var hero = next.Heroes.Find(x => x.Id == h);
            Assert.Equal("vest", hero.Equipped[EquipSlot.Chest]);
            Assert.False(hero.Equipped.ContainsKey(EquipSlot.Weapon)); // apply never strips or steals
            Assert.False(hero.Equipped.ContainsKey(EquipSlot.Helm));
        }

        [Fact]
        public void ApplyWithoutASnapshotSharesTheInputRef()
        {
            var save = Fresh(out var h);
            var (next, applied, skipped) = Loadouts.Apply(save, h, Cfg);
            Assert.Same(save, next);
            Assert.Equal(0, applied);
            Assert.Equal(0, skipped);
        }

        [Fact]
        public void SnapshotSurvivesLevelUpEquipAndRankInvest()
        {
            var save = Fresh(out var h);
            save = GiveAndEquip(save, h, ("sw", "rusty_sword"));
            save = Loadouts.SaveSnapshot(save, h);

            save = Progression.GrantPartyXp(save, 10_000, Cfg);              // WithLevel clone
            save = Inventory.AddItems(save, new[] { It("cap", "leather_cap") });
            save = Inventory.EquipItem(save, h, "cap", Cfg);                  // CloneHero clone
            var hero = save.Heroes.Find(x => x.Id == h);
            Assert.NotNull(hero.Loadout); // both copy sites carried it
            Assert.Equal("sw", hero.Loadout[EquipSlot.Weapon]);
        }
    }
}
