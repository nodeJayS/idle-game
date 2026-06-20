using System;
using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class SkillsTests
    {
        // A config whose Warrior knows 5 skills (> the 4-slot cap) so loadout choice is exercised.
        private static GameConfig BigPoolCfg()
        {
            var cfg = GameConfig.Default();
            cfg.Heroes["warrior_basic"].Skills =
                new List<string> { "cleave", "warcry", "firebolt", "mend", "boss_quake" };
            return cfg;
        }

        private static (SaveState save, string heroId, GameConfig cfg) Setup()
        {
            var cfg = BigPoolCfg();
            var save = Save.NewGame(1, cfg, 0);
            return (save, save.Heroes[0].Id, cfg);
        }

        private static List<string> Loadout(SaveState s, string heroId) =>
            s.Heroes.First(h => h.Id == heroId).SkillLoadout;

        [Fact]
        public void DefaultLoadoutCapsAtMaxActiveSkills()
        {
            var cfg = BigPoolCfg();
            var save = Save.NewGame(1, cfg, 0);
            var loadout = Loadout(save, save.Heroes[0].Id);

            Assert.Equal(cfg.Balance.MaxActiveSkills, loadout.Count);            // exactly the cap
            Assert.Equal(new[] { "cleave", "warcry", "firebolt", "mend" }, loadout); // first N of the pool
        }

        [Fact]
        public void DefaultLoadoutTakesWholePoolWhenUnderCap()
        {
            // A class knowing fewer skills than the cap starts with all of them.
            var cfg = GameConfig.Default();
            var def = new HeroDef { DefId = "x", Skills = new List<string> { "cleave", "warcry" } };
            Assert.Equal(def.Skills, Skills.DefaultLoadout(def, cfg));
        }

        [Fact]
        public void RealHeroesKnowMoreThanTheyCanSlot()
        {
            // The expanded kits (6 known) exceed the 4-slot cap, so the loadout is a real choice.
            var cfg = GameConfig.Default();
            Assert.True(cfg.Heroes["warrior_basic"].Skills.Count > cfg.Balance.MaxActiveSkills);
            Assert.True(cfg.Heroes["magician_basic"].Skills.Count > cfg.Balance.MaxActiveSkills);

            var save = Save.NewGame(1, cfg, 0);
            Assert.Equal(cfg.Balance.MaxActiveSkills, Loadout(save, save.Heroes[0].Id).Count);
        }

        [Fact]
        public void ToggleRemovesAndAddsKnownSkill()
        {
            var (save, heroId, cfg) = Setup(); // loadout full: cleave/warcry/firebolt/mend

            var off = Skills.ToggleSkill(save, heroId, "cleave", cfg);
            Assert.DoesNotContain("cleave", Loadout(off, heroId));
            Assert.Equal(3, Loadout(off, heroId).Count);

            var on = Skills.ToggleSkill(off, heroId, "boss_quake", cfg); // slot the 5th into the free slot
            Assert.Contains("boss_quake", Loadout(on, heroId));
            Assert.Equal(4, Loadout(on, heroId).Count);
        }

        [Fact]
        public void ToggleNoOpsWhenBarIsFull()
        {
            var (save, heroId, cfg) = Setup(); // 4/4 slots used
            var next = Skills.ToggleSkill(save, heroId, "boss_quake", cfg); // known, not slotted, no room

            Assert.Same(save, next);                                  // no-op shares the ref
            Assert.DoesNotContain("boss_quake", Loadout(next, heroId));
        }

        [Fact]
        public void ToggleNoOpsForUnknownSkill()
        {
            var (save, heroId, cfg) = Setup();
            Assert.Same(save, Skills.ToggleSkill(save, heroId, "not_a_skill", cfg));
        }

        [Fact]
        public void ToggleIsPure()
        {
            var (save, heroId, cfg) = Setup();
            var before = new List<string>(Loadout(save, heroId));
            Skills.ToggleSkill(save, heroId, "cleave", cfg);
            Assert.Equal(before, Loadout(save, heroId)); // original untouched
        }

        [Fact]
        public void SetLoadoutAcceptsValidSubset()
        {
            var (save, heroId, cfg) = Setup();
            var next = Skills.SetLoadout(save, heroId, new[] { "mend", "cleave" }, cfg);
            Assert.Equal(new[] { "mend", "cleave" }, Loadout(next, heroId)); // order preserved
        }

        [Fact]
        public void SetLoadoutRejectsUnknownDuplicateAndOverCap()
        {
            var (save, heroId, cfg) = Setup();
            Assert.Throws<InvalidOperationException>(() => Skills.SetLoadout(save, heroId, new[] { "nope" }, cfg));
            Assert.Throws<InvalidOperationException>(() => Skills.SetLoadout(save, heroId, new[] { "cleave", "cleave" }, cfg));
            Assert.Throws<InvalidOperationException>(() =>
                Skills.SetLoadout(save, heroId, new[] { "cleave", "warcry", "firebolt", "mend", "boss_quake" }, cfg)); // 5 > cap
        }
    }
}
