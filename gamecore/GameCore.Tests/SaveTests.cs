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
            var save = TwoHeroSave(); // h1 slot 0, h2 slot 1 — clearing one leaves the party non-empty
            Assert.Null(Party.SetPartySlot(save, 0, null).Party[0]);
        }

        [Fact]
        public void SetPartySlotClearingLastFieldedHeroIsNoOp()
        {
            // Guard rail: a fresh save has exactly one fielded hero. Clearing it would strand
            // the party empty, so the reducer no-ops and returns the SAME reference.
            var save = Save.NewGame(1, Cfg, 0);
            var next = Party.SetPartySlot(save, 0, null);
            Assert.Same(save, next);                 // no-op shares the ref
            Assert.Equal(save.Heroes[0].Id, next.Party[0]); // hero still fielded
        }

        [Fact]
        public void SetPartySlotClearsOneOfTwoFieldedHeroes()
        {
            var save = TwoHeroSave(); // h1 slot 0, h2 slot 1
            var next = Party.SetPartySlot(save, 0, null); // clear h1; h2 remains fielded
            Assert.Null(next.Party[0]);
            Assert.Equal("h2", next.Party[1]);
            Assert.Equal(save.Heroes[0].Id, save.Party[0]); // original untouched (pure)
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

        // h1 (warrior) slot 0, h2 (magician) slot 1, h3 (thief) owned but benched.
        private static SaveState TwoFieldedOneBenched()
        {
            var save = Party.AcquireHero(TwoHeroSave(), "thief_basic", Cfg, "h3");
            return save;
        }

        [Fact]
        public void SwapHeroPutsBenchedHeroInOutgoingHerosExactSlot()
        {
            var save = TwoFieldedOneBenched(); // h1@0, h2@1, h3 benched
            var next = Party.SwapHero(save, "h3", "h2"); // h3 in for h2 (slot 1)

            Assert.Equal("h3", next.Party[1]);          // incoming took the exact slot
            Assert.Equal(save.Party[0], next.Party[0]);  // other slot unchanged
            Assert.DoesNotContain("h2", next.Party);     // outgoing no longer fielded
            Assert.Contains(next.Heroes, h => h.Id == "h2"); // but still owned
            Assert.Equal("h2", save.Party[1]);           // original untouched (pure)
        }

        [Fact]
        public void SwapHeroOnLeaderClearsLeaderToNull()
        {
            var save = Party.SetLeader(TwoFieldedOneBenched(), "h2"); // h2 is leader
            var next = Party.SwapHero(save, "h3", "h2");              // bench the leader
            Assert.Null(next.LeaderHeroId); // reverts to auto, does NOT transfer to h3
        }

        [Fact]
        public void SwapHeroPreservesUnrelatedLeader()
        {
            var save = Party.SetLeader(TwoFieldedOneBenched(), "h1"); // h1 leads
            var next = Party.SwapHero(save, "h3", "h2");              // swap the non-leader
            Assert.Equal("h1", next.LeaderHeroId); // leadership preserved
        }

        [Fact]
        public void SwapHeroNoOpsOnInvalidArgs()
        {
            var save = TwoFieldedOneBenched(); // h1@0, h2@1, h3 benched

            Assert.Same(save, Party.SwapHero(save, "h1", "h2")); // benched id already fielded
            Assert.Same(save, Party.SwapHero(save, "h3", "h3")); // fielded id not in party (h3 benched)
            Assert.Same(save, Party.SwapHero(save, "nope", "h2")); // unknown benched id
            Assert.Same(save, Party.SwapHero(save, "h3", "nope")); // unknown fielded id
            Assert.Same(save, Party.SwapHero(save, "h2", "h2"));   // same id
        }

        [Fact]
        public void EffectiveLeaderHonorsExplicitFieldedPick()
        {
            var save = Party.SetLeader(TwoHeroSave(), "h2"); // h2 (magician/ranged) explicitly leads
            Assert.Equal("h2", Party.EffectiveLeader(save, Cfg)); // explicit pick wins, even ranged
        }

        [Fact]
        public void EffectiveLeaderFallsThroughWhenPickBenched()
        {
            // h1 (warrior/melee) slot 0, h2 (magician/ranged) slot 1; pick h2, then bench it.
            var save = Party.SetLeader(TwoHeroSave(), "h2");
            save = Party.SetPartySlot(save, 1, null); // h2 no longer fielded (leader stays set)
            Assert.Equal("h2", save.LeaderHeroId);     // still the stored pick...
            Assert.Equal("h1", Party.EffectiveLeader(save, Cfg)); // ...but display falls to melee-first
        }

        [Fact]
        public void EffectiveLeaderAutoPicksFirstMeleeOverEarlierRanged()
        {
            // Ranged hero in an EARLIER slot than a melee hero; with no explicit pick the melee
            // hero should lead (mirrors the sim's !RangedRole default), not the lower slot.
            var save = Save.NewGame(1, Cfg, 0);            // h1 (warrior/melee) in slot 0
            save = Party.AcquireHero(save, "magician_basic", Cfg, "hm"); // ranged
            save = Party.AcquireHero(save, "thief_basic", Cfg, "ht");    // melee
            save = Party.FieldHero(save, 0, "hm");         // ranged in slot 0 (earlier)
            save = Party.FieldHero(save, 1, "ht");         // melee in slot 1 (later)
            Assert.Null(save.LeaderHeroId);
            Assert.Equal("ht", Party.EffectiveLeader(save, Cfg)); // melee wins over earlier ranged
        }

        [Fact]
        public void EffectiveLeaderAllRangedPicksFirstFielded()
        {
            // All-ranged party, no explicit pick: falls back to the first fielded hero.
            var save = Save.NewGame(1, Cfg, 0);
            save = Party.AcquireHero(save, "magician_basic", Cfg, "hm");
            save = Party.AcquireHero(save, "priest_basic", Cfg, "hp");
            save = Party.FieldHero(save, 0, "hm"); // ranged slot 0
            save = Party.FieldHero(save, 1, "hp"); // ranged slot 1
            Assert.Null(save.LeaderHeroId);
            Assert.Equal("hm", Party.EffectiveLeader(save, Cfg)); // first fielded hero leads
        }

        [Fact]
        public void EffectiveLeaderIsNullOnEmptyParty()
        {
            // The last-hero guard refuses to strand the party via the reducer, so clear the
            // slots by hand to exercise the empty-party branch directly.
            var save = Save.NewGame(1, Cfg, 0);
            for (int i = 0; i < save.Party.Length; i++) save.Party[i] = null;
            Assert.Null(Party.EffectiveLeader(save, Cfg));
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
        public void MigrateUpgradesV1SaveToCurrentVersion()
        {
            // A legacy v1 save (pre-mana-removal) loads and is stamped to the current version.
            var json = "{\"Version\":1,\"Party\":[\"h1\"]}";
            var save = Persistence.Deserialize(json);
            Assert.Equal(Save.SaveVersion, save.Version);
        }

        [Fact]
        public void MigrateStripsRetiredManaAffixesFromV1Items()
        {
            // A v1 item that (hypothetically) carried a MaxMana affix: StatKey serialized
            // numerically, and the retired MaxMana/ManaRegen values were 9/10. Migration must
            // drop the dangling affix but keep the still-valid AtkSpd (11) affix intact —
            // covers both bag and equipped (equipped items live in Inventory).
            var json =
                "{\"Version\":1,\"Inventory\":[{\"Id\":\"i1\",\"BaseId\":\"weapon\",\"Affixes\":[" +
                "{\"Stat\":9,\"Value\":50}," +   // retired MaxMana
                "{\"Stat\":10,\"Value\":3}," +   // retired ManaRegen
                "{\"Stat\":11,\"Value\":0.05}," + // AtkSpd — still valid
                "{\"Stat\":1,\"Value\":4.5}]}]}"; // Atk — still valid

            var save = Persistence.Deserialize(json); // must not throw on old-shape JSON

            var item = save.Inventory.Single(i => i.Id == "i1");
            Assert.Equal(2, item.Affixes.Count);
            Assert.All(item.Affixes, a => Assert.True(System.Enum.IsDefined(typeof(StatKey), a.Stat)));
            Assert.Contains(item.Affixes, a => a.Stat == StatKey.AtkSpd);
            Assert.Contains(item.Affixes, a => a.Stat == StatKey.Atk);
            Assert.Equal(Save.SaveVersion, save.Version);
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
            Assert.Equal(5, Cfg.Heroes.Count); // Warrior, Magician, Thief, Ice Mage, Priest
            Assert.Equal(33, Cfg.Monsters.Count); // 10 zones x (2 trash + 1 boss) + 3 swamp blob-shell trash (ROADMAP 4 slice 3)
            Assert.Equal(100, Cfg.Stages.Count);
            // every unlockable hero id must resolve to a real def
            foreach (var kv in Cfg.HeroUnlocks) Assert.True(Cfg.Heroes.ContainsKey(kv.Value));
        }
    }
}
