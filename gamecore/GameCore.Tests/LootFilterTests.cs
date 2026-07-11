using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// 10.5a loot filter: per-slot auto-salvage floors + the never-salvage-imprinted guard.
    /// The one predicate is <see cref="Inventory.WouldAutoSalvage"/>; the guard mirrors the
    /// Item.Locked rule — ALL salvage paths refuse while it's on.
    /// </summary>
    public class LootFilterTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Item It(string id, Rarity rarity = Rarity.Rare, string baseId = "rusty_sword", int ilvl = 1) =>
            new Item { Id = id, BaseId = baseId, Rarity = rarity, ItemLevel = ilvl };

        // Any affix whose stat is outside cfg.AffixPool reads as imprinted (SplashRadius is the
        // established not-in-pool stat — same as the Reforge imprint tests).
        private static Item Imprinted(string id, Rarity rarity = Rarity.Rare)
        {
            var it = It(id, rarity);
            it.Affixes.Add(new Affix { Stat = StatKey.SplashRadius, Value = 1.2 });
            return it;
        }

        // ---- per-slot floors ----

        [Fact]
        public void FloorIsPerSlot()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.SetSalvageFloor(save, EquipSlot.Weapon, Rarity.Rare);

            // Same rarity, different slots: the weapon drop scraps, the helm drop stays.
            var res = Inventory.AddLoot(save, new[]
            {
                It("sword", Rarity.Rare, "rusty_sword"),
                It("cap", Rarity.Rare, "leather_cap"),
            }, Cfg);

            Assert.Single(res.Salvaged);
            Assert.Equal("sword", res.Salvaged[0].Id);
            Assert.Single(res.Stored);
            Assert.Equal("cap", res.Stored[0].Id);
        }

        [Fact]
        public void FloorBoundaryIsInclusive()
        {
            var save = Inventory.SetSalvageFloor(Save.NewGame(1, Cfg, 0), EquipSlot.Weapon, Rarity.Rare);

            Assert.True(Inventory.WouldAutoSalvage(save, It("n", Rarity.Normal), Cfg));
            Assert.True(Inventory.WouldAutoSalvage(save, It("r", Rarity.Rare), Cfg));   // "Rare & below" includes Rare
            Assert.False(Inventory.WouldAutoSalvage(save, It("u", Rarity.Unique), Cfg)); // above the floor
        }

        [Fact]
        public void SlotWithoutAFloorNeverAutoSalvages()
        {
            var save = Inventory.SetSalvageFloor(Save.NewGame(1, Cfg, 0), EquipSlot.Weapon, Rarity.Unique);
            Assert.False(Inventory.WouldAutoSalvage(save, It("cap", Rarity.Normal, "leather_cap"), Cfg));
        }

        [Fact]
        public void UnknownBaseNeverAutoSalvages()
        {
            var save = Inventory.SetSalvageFloorAll(Save.NewGame(1, Cfg, 0), Rarity.Unique);
            var mystery = It("x", Rarity.Normal, baseId: "no_such_base");

            Assert.False(Inventory.WouldAutoSalvage(save, mystery, Cfg)); // can't classify => keep
            var res = Inventory.AddLoot(save, new[] { mystery }, Cfg);
            Assert.Empty(res.Salvaged);
            Assert.Single(res.Stored);
        }

        [Fact]
        public void LockedDropIsNeverAutoSalvaged()
        {
            var save = Inventory.SetSalvageFloorAll(Save.NewGame(1, Cfg, 0), Rarity.Unique);
            var locked = It("keep", Rarity.Normal);
            locked.Locked = true;
            Assert.False(Inventory.WouldAutoSalvage(save, locked, Cfg));
        }

        // ---- the imprint guard (all salvage paths refuse, like Item.Locked) ----

        [Fact]
        public void GuardSparesImprintedDropInAddLoot()
        {
            // Guard defaults ON (NewGame + old-save backfill alike).
            var save = Inventory.SetSalvageFloorAll(Save.NewGame(1, Cfg, 0), Rarity.Unique);

            var res = Inventory.AddLoot(save, new[] { Imprinted("imp", Rarity.Rare) }, Cfg);
            Assert.Empty(res.Salvaged);
            Assert.Contains(res.Stored, i => i.Id == "imp");

            // Guard off: the same drop scraps like any other at/below the floor.
            var off = Inventory.SetImprintGuard(save, false);
            var res2 = Inventory.AddLoot(off, new[] { Imprinted("imp2", Rarity.Rare) }, Cfg);
            Assert.Single(res2.Salvaged);
        }

        [Fact]
        public void GuardSparesImprintedInSalvageAll()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { Imprinted("imp"), It("plain") });

            var (next, count, _) = Inventory.SalvageAll(save, Cfg);
            Assert.Equal(1, count);
            Assert.Contains(next.Inventory, i => i.Id == "imp");        // guarded survivor
            Assert.DoesNotContain(next.Inventory, i => i.Id == "plain");

            var off = Inventory.SetImprintGuard(save, false);
            var (next2, count2, _) = Inventory.SalvageAll(off, Cfg);
            Assert.Equal(2, count2);                                     // guard off: both go
            Assert.Empty(next2.Inventory);
        }

        [Fact]
        public void SalvageItemThrowsOnGuardedImprintSucceedsWithGuardOff()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.AddItems(save, new[] { Imprinted("imp") });

            Assert.Throws<System.InvalidOperationException>(() => Inventory.SalvageItem(save, "imp", Cfg));

            var off = Inventory.SetImprintGuard(save, false);
            var next = Inventory.SalvageItem(off, "imp", Cfg);
            Assert.DoesNotContain(next.Inventory, i => i.Id == "imp");
        }

        [Fact]
        public void LockedStillRefusesEverythingRegardlessOfGuard()
        {
            // Lock and guard are independent protections: guard OFF + locked still refuses.
            var save = Inventory.SetImprintGuard(Save.NewGame(1, Cfg, 0), false);
            save = Inventory.AddItems(save, new[] { Imprinted("imp") });
            save = Inventory.ToggleLock(save, "imp");

            Assert.Throws<System.InvalidOperationException>(() => Inventory.SalvageItem(save, "imp", Cfg));
            var (next, count, _) = Inventory.SalvageAll(save, Cfg);
            Assert.Equal(0, count);
            Assert.Same(save, next);
        }

        // ---- reducers: pure, and the state rides Progress threading ----

        [Fact]
        public void ReducersArePure()
        {
            var save = Save.NewGame(1, Cfg, 0);

            var floored = Inventory.SetSalvageFloor(save, EquipSlot.Weapon, Rarity.Rare);
            Assert.Empty(save.Progress.Loot.SalvageMaxBySlot);           // input untouched
            Assert.Single(floored.Progress.Loot.SalvageMaxBySlot);
            Assert.NotSame(save.Progress.Loot, floored.Progress.Loot);

            var off = Inventory.SetImprintGuard(save, false);
            Assert.True(save.Progress.Loot.NeverSalvageImprinted);       // input untouched
            Assert.False(off.Progress.Loot.NeverSalvageImprinted);
        }

        [Fact]
        public void SetSalvageFloorNullRemovesTheEntry()
        {
            var save = Inventory.SetSalvageFloor(Save.NewGame(1, Cfg, 0), EquipSlot.Weapon, Rarity.Rare);
            var cleared = Inventory.SetSalvageFloor(save, EquipSlot.Weapon, null);
            Assert.Empty(cleared.Progress.Loot.SalvageMaxBySlot);
        }

        [Fact]
        public void SetSalvageFloorAllSetsEveryActiveSlotAndNullClears()
        {
            var save = Inventory.SetSalvageFloorAll(Save.NewGame(1, Cfg, 0), Rarity.Normal);
            Assert.Equal(EquipSlots.Active.Length, save.Progress.Loot.SalvageMaxBySlot.Count);
            foreach (var slot in EquipSlots.Active)
                Assert.Equal(Rarity.Normal, save.Progress.Loot.SalvageMaxBySlot[slot]);

            var cleared = Inventory.SetSalvageFloorAll(save, null);
            Assert.Empty(cleared.Progress.Loot.SalvageMaxBySlot);
        }

        [Fact]
        public void FiltersSurviveOtherReducers()
        {
            // The filter rides ProgressState by ref through unrelated save copies.
            var save = Inventory.SetSalvageFloor(Save.NewGame(1, Cfg, 0), EquipSlot.Helm, Rarity.Normal);
            save = Inventory.SetImprintGuard(save, false);

            save = Save.Touch(save, 123);
            save = Progression.GrantPartyXp(save, 1000, Cfg);

            Assert.Equal(Rarity.Normal, save.Progress.Loot.SalvageMaxBySlot[EquipSlot.Helm]);
            Assert.False(save.Progress.Loot.NeverSalvageImprinted);
        }

        // ---- migration / serialization ----

        [Fact]
        public void MigrateBackfillsMissingLootFilterWithGuardOn()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save.Progress.Loot = null!; // a pre-10.5a save deserializes with no Loot payload

            var migrated = Save.Migrate(save);

            Assert.NotNull(migrated.Progress.Loot);
            Assert.True(migrated.Progress.Loot.NeverSalvageImprinted); // guard defaults ON for old saves
            Assert.Empty(migrated.Progress.Loot.SalvageMaxBySlot);     // no floors until the player sets them
        }

        [Fact]
        public void FilterRoundTripsThroughJson()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Inventory.SetSalvageFloor(save, EquipSlot.Gloves, Rarity.Unique); // enum-keyed entry
            save = Inventory.SetImprintGuard(save, false);

            var round = Persistence.Deserialize(Persistence.Serialize(save));

            Assert.Equal(Rarity.Unique, round.Progress.Loot.SalvageMaxBySlot[EquipSlot.Gloves]);
            Assert.False(round.Progress.Loot.NeverSalvageImprinted);
        }

        // ---- idle claim honors the filter ----

        [Fact]
        public void IdleClaimRespectsTheFilter()
        {
            var save = Save.NewGame(1, GameConfig.Default(), 0);
            save.Progress.HighestStage = 10;
            save.LastClaimAt = 0;
            save = Inventory.SetSalvageFloorAll(save, Rarity.Unique);

            var (next, report) = Idle.Claim(save, GameConfig.Default(), 2 * 3600_000L);

            Assert.True(report.LootCount > 0);
            Assert.True(report.ScrapGained > 0); // low-rarity offline drops scrapped by the filter
            // Only drops ABOVE the floor take bag slots; the salvaged rarities add nothing.
            int kept = report.Items.Count(i => i.Rarity > Rarity.Unique);
            Assert.Equal(kept, next.Inventory.Count);
        }
    }
}
