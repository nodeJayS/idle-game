using System;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Progression.SyncHeroUnlocks: retroactive unlock grants on load, and
    /// removal of shelved heroes whose def left the unlock table.</summary>
    public class RosterSyncTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static SaveState AtStage(int highest)
        {
            var save = Save.NewGame(1, Cfg, 0);
            save.Progress.HighestStage = highest;
            return save;
        }

        [Fact]
        public void RetroactivelyGrantsEveryUnlockAtOrBelowHighestStage()
        {
            var save = AtStage(22); // starter-only roster, deep progress
            var next = Progression.SyncHeroUnlocks(save, Cfg);

            Assert.Contains(next.Heroes, h => h.DefId == "magician_basic"); // stage 3
            Assert.Contains(next.Heroes, h => h.DefId == "priest_basic");   // stage 5
            Assert.Contains(next.Heroes, h => h.DefId == "thief_basic");    // stage 10
            Assert.Equal(4, next.Heroes.Count);

            // granted fresh: level 1, nothing equipped
            foreach (var h in next.Heroes.Where(h => h.DefId != Save.StarterHeroDef))
            {
                Assert.Equal(1, h.Level);
                Assert.Empty(h.Equipped);
            }
        }

        [Fact]
        public void DoesNotGrantBeyondProgressAndIsIdempotent()
        {
            var save = AtStage(4); // only the stage-3 magician is due
            var once = Progression.SyncHeroUnlocks(save, Cfg);
            Assert.Equal(2, once.Heroes.Count);
            Assert.DoesNotContain(once.Heroes, h => h.DefId == "priest_basic");

            var twice = Progression.SyncHeroUnlocks(once, Cfg);
            Assert.Equal(2, twice.Heroes.Count); // no duplicates on re-sync
        }

        [Fact]
        public void RemovesShelvedHeroesAndFreesTheirPartySlot()
        {
            // The Ice Mage is banner-gated in Default() (the live "Winter's Return" banner keeps
            // her obtainable), so shelving requires a banner-less config: strip the banners and the
            // same removal path fires. Proves the shelve-and-free-slot mechanism, not the banner.
            var cfg = GameConfig.Default();
            cfg.Banners.Clear();

            var save = AtStage(22);
            save = Party.AcquireHero(save, "icemage_basic", cfg, "h_ice"); // legacy placeholder
            save = Party.FieldHero(save, 1, "h_ice");

            var next = Progression.SyncHeroUnlocks(save, cfg);

            Assert.DoesNotContain(next.Heroes, h => h.DefId == "icemage_basic");
            Assert.DoesNotContain(next.Party, id => id == "h_ice");
            Assert.Contains(next.Party, id => id != null); // party never left empty
        }

        [Fact]
        public void UnlockTableMatchesTheDesign()
        {
            Assert.Equal("magician_basic", Cfg.HeroUnlocks[3]);
            Assert.Equal("priest_basic", Cfg.HeroUnlocks[5]);
            Assert.Equal("thief_basic", Cfg.HeroUnlocks[10]);
            Assert.DoesNotContain("icemage_basic", Cfg.HeroUnlocks.Values); // shelved
            Assert.Equal("Knight", Cfg.Heroes["warrior_basic"].Name);      // display renames
            Assert.Equal("Assassin", Cfg.Heroes["thief_basic"].Name);
        }
    }
}
