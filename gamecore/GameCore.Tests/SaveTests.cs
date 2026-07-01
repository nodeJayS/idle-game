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
            Assert.Equal(Save.PartySize, save.Party.Length);
            Assert.Equal(save.Heroes[0].Id, save.Party[0]); // Warrior fielded in slot 0
            for (int i = 1; i < save.Party.Length; i++) Assert.Null(save.Party[i]); // rest empty
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

        // h1 (warrior) in slot 0, h2 (magician) in slot 1 — for leader/party tests.
        private static SaveState TwoHeroSave()
        {
            var save = Party.AcquireHero(Save.NewGame(1, Cfg, 0), "magician_basic", Cfg, "h2");
            return Party.FieldHero(save, 1, "h2");
        }

        [Fact]
        public void SetLeaderRequiresFieldedHeroAndClearsToNull()
        {
            var save = TwoHeroSave(); // h1 slot 0, h2 slot 1

            var led = Party.SetLeader(save, "h2");
            Assert.Equal("h2", led.LeaderHeroId);
            Assert.Null(save.LeaderHeroId); // pure: original untouched

            Assert.Null(Party.SetLeader(led, null).LeaderHeroId); // null reverts to auto

            var benched = Party.SetPartySlot(save, 0, null); // h1 no longer fielded
            Assert.Throws<InvalidOperationException>(() => Party.SetLeader(benched, "h1"));
        }

        [Fact]
        public void LeaderSurvivesUnrelatedReducers()
        {
            // Guards the LeaderHeroId copy-through in every SaveState reducer: an unrelated
            // change (gold, a respec) must not silently reset the chosen leader.
            var save = Party.SetLeader(TwoHeroSave(), "h2");
            Assert.Equal("h2", Progression.GrantGold(save, 10).LeaderHeroId);
            Assert.Equal("h2", Progression.GrantPartyXp(save, 50, Cfg).LeaderHeroId);
            Assert.Equal("h2", Skills.RespecHero(save, "h2", Cfg).LeaderHeroId);
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
            Assert.Equal(Save.PartySize, save.Party.Length);
        }

        [Fact]
        public void MigrateNormalizesPartyLength()
        {
            var json = $"{{\"Version\":{Save.SaveVersion},\"Party\":[]}}";
            var save = Persistence.Deserialize(json);
            Assert.Equal(Save.PartySize, save.Party.Length);
        }

        [Fact]
        public void MigrateShrinksLongPartyBenchingOverflow()
        {
            // A legacy save from when the party cap was 4: four fielded heroes. Migration
            // keeps the first PartySize slots and benches the overflow (still owned).
            var json = $"{{\"Version\":{Save.SaveVersion},\"Party\":[\"h1\",\"h2\",\"h3\",\"h4\"]}}";
            var save = Persistence.Deserialize(json);

            Assert.Equal(Save.PartySize, save.Party.Length);
            Assert.Equal("h1", save.Party[0]);
            Assert.Equal("h2", save.Party[1]);
            Assert.Equal("h3", save.Party[2]);
            Assert.DoesNotContain("h4", save.Party); // overflow hero benched, not fielded
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
            Assert.Equal(4, Cfg.Heroes.Count); // Warrior, Magician, Thief, Ice Mage
            Assert.Equal(3, Cfg.Monsters.Count);
            Assert.Equal(100, Cfg.Stages.Count);
            // every unlockable hero id must resolve to a real def
            foreach (var kv in Cfg.HeroUnlocks) Assert.True(Cfg.Heroes.ContainsKey(kv.Value));
        }
    }
}
