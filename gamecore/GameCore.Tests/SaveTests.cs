using System;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class SaveTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        [Fact]
        public void NewGameStartsWithOneWarriorInSlotZero()
        {
            var save = Save.NewGame(123, Cfg, 1000);
            Assert.Single(save.Heroes);
            Assert.Equal(Save.StarterHeroDef, save.Heroes[0].DefId);
            Assert.Equal(new string?[] { save.Heroes[0].Id, null, null, null }, save.Party);
            Assert.Equal(0, save.Currencies["gold"]);
            Assert.Equal(1000, save.LastClaimAt);
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
            var next = Party.SetPartySlot(save, 1, save.Heroes[0].Id);
            Assert.Equal(save.Heroes[0].Id, next.Party[1]);
            Assert.Null(save.Party[1]); // original untouched
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
        public void DefaultConfigHasExpectedContent()
        {
            Assert.Single(Cfg.Heroes);
            Assert.Equal(3, Cfg.Monsters.Count);
            Assert.Equal(50, Cfg.Stages.Count);
        }
    }
}
