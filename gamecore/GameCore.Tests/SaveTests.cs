using System;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class SaveTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        [Fact]
        public void NewGameStartsWithWarriorOnly()
        {
            var save = Save.NewGame(123, Cfg, 1000);
            Assert.Single(save.Heroes);
            Assert.Equal(Save.StarterHeroDef, save.Heroes[0].DefId);
            Assert.Equal(new string?[] { save.Heroes[0].Id, null, null, null }, save.Party);
            Assert.Equal(0, save.Currencies["gold"]);
            Assert.Equal(1000, save.LastClaimAt);
        }

        [Fact]
        public void AcquireHeroAddsOwnedHeroAndIsPure()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var next = Party.AcquireHero(save, "magician_basic", Cfg, "h2");

            Assert.Equal(2, next.Heroes.Count);
            Assert.Contains(next.Heroes, h => h.Id == "h2" && h.DefId == "magician_basic");
            Assert.Single(save.Heroes); // original untouched
        }

        [Fact]
        public void AcquireHeroRejectsDuplicateIdAndUnknownDef()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Throws<InvalidOperationException>(() => Party.AcquireHero(save, "magician_basic", Cfg, "h1")); // dup id
            Assert.Throws<InvalidOperationException>(() => Party.AcquireHero(save, "nope", Cfg, "hX"));           // unknown def
        }

        [Fact]
        public void RoundTripsThroughSerializeDeserialize()
        {
            var save = Save.NewGame(7, Cfg, 42);
            var json = Persistence.Serialize(save);
            var again = Persistence.Serialize(Persistence.Deserialize(json));
            Assert.Equal(json, again);
        }

        [Fact]
        public void SetPartySlotPlacesOwnedHeroAndIsPure()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var next = Party.SetPartySlot(save, 2, save.Heroes[0].Id); // slot 2 is empty
            Assert.Equal(save.Heroes[0].Id, next.Party[2]);
            Assert.Null(save.Party[2]); // original untouched
        }

        [Fact]
        public void SetPartySlotClearsWithNull()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Null(Party.SetPartySlot(save, 0, null).Party[0]);
        }

        [Fact]
        public void SetPartySlotRejectsOutOfRange()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => Party.SetPartySlot(save, 9, save.Heroes[0].Id));
        }

        [Fact]
        public void SetPartySlotRejectsUnownedHero()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Throws<InvalidOperationException>(() => Party.SetPartySlot(save, 0, "nope"));
        }

        [Fact]
        public void FieldHeroMovesHeroWithoutDuplicating()
        {
            var save = Save.NewGame(1, Cfg, 0);
            string h1 = save.Heroes[0].Id; // starts in slot 0
            var next = Party.FieldHero(save, 2, h1); // move into empty slot 2

            Assert.Null(next.Party[0]);             // pulled out of its old slot
            Assert.Equal(h1, next.Party[2]);
            Assert.Single(System.Array.FindAll(next.Party, id => id == h1)); // exactly once
            Assert.Equal(h1, save.Party[0]);        // original untouched (pure)
        }

        [Fact]
        public void FieldHeroBenchesPreviousOccupant()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Party.AcquireHero(save, "magician_basic", Cfg, "h2");
            save = Party.FieldHero(save, 1, "h2"); // h1 in slot 0, h2 in slot 1
            string h1 = save.Heroes[0].Id, h2 = "h2";

            var next = Party.FieldHero(save, 1, h1); // field h1 where h2 was

            Assert.Equal(h1, next.Party[1]);
            Assert.Null(next.Party[0]);
            Assert.DoesNotContain(h2, next.Party); // h2 benched
        }

        [Fact]
        public void FieldHeroRejectsOutOfRangeAndUnowned()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => Party.FieldHero(save, 9, save.Heroes[0].Id));
            Assert.Throws<InvalidOperationException>(() => Party.FieldHero(save, 0, "nope"));
        }

        [Fact]
        public void TouchStampsLastClaimAtAndIsPure()
        {
            var save = Save.NewGame(1, Cfg, 1000);
            var next = Save.Touch(save, 5000);

            Assert.Equal(5000, next.LastClaimAt);
            Assert.Equal(1000, save.LastClaimAt); // original untouched
            Assert.NotSame(save, next);
        }

        [Fact]
        public void TouchNeverMovesClockBackward()
        {
            var save = Save.NewGame(1, Cfg, 5000);
            var next = Save.Touch(save, 1000);

            Assert.Equal(5000, next.LastClaimAt);
            Assert.Same(save, next); // no-op shares the reference
        }

        [Fact]
        public void MigrateRejectsFutureVersion()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save.Version = Save.SaveVersion + 1;
            Assert.Throws<InvalidOperationException>(() => Save.Migrate(save));
        }

        [Fact]
        public void MigrateBackfillsNullCollections()
        {
            // A partial payload with explicit nulls must not NRE the rest of the game.
            var json = $"{{\"Version\":{Save.SaveVersion},\"Heroes\":null,\"Currencies\":null,\"Party\":null,\"Progress\":null}}";
            var save = Persistence.Deserialize(json); // Deserialize runs Migrate

            Assert.NotNull(save.Heroes);
            Assert.NotNull(save.Inventory);
            Assert.NotNull(save.Currencies);
            Assert.NotNull(save.Progress);
            Assert.Equal(4, save.Party.Length);
        }

        [Fact]
        public void MigrateNormalizesPartyLength()
        {
            var json = $"{{\"Version\":{Save.SaveVersion},\"Party\":[]}}";
            var save = Persistence.Deserialize(json);
            Assert.Equal(4, save.Party.Length);
        }

        [Fact]
        public void RoundTripsRichStateStably()
        {
            var save = Save.NewGame(7, Cfg, 42);
            save.Currencies["gold"] = 12345;
            save.Inventory.Add(new Item { Id = "i1", BaseId = "rusty_sword", Rarity = Rarity.Rare, ItemLevel = 9,
                                          Affixes = { new Affix { Stat = StatKey.Atk, Value = 4.5 } } });
            save.Heroes[0].Equipped[EquipSlot.Weapon] = "i1";
            save.Heroes[0].Level = 8;
            save.Progress.HighestStage = 11;
            save.Progress.CurrentStage = 12;

            var json = Persistence.Serialize(save);
            var again = Persistence.Serialize(Persistence.Deserialize(json));
            Assert.Equal(json, again);
        }

        [Fact]
        public void DefaultConfigHasExpectedContent()
        {
            Assert.Equal(2, Cfg.Heroes.Count);
            Assert.Equal(3, Cfg.Monsters.Count);
            Assert.Equal(50, Cfg.Stages.Count);
        }
    }
}
