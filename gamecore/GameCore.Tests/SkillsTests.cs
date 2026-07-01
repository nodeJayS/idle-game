using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>The 2+2 hero kit (design §7.2): every hero has exactly 2 actives + 2 passives,
    /// revealed by UnlockLevel and always on once revealed — no loadout choice, no prereq tree.</summary>
    public class SkillsTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static HeroInstance HeroAt(string defId, int level) =>
            new HeroInstance { Id = "h", DefId = defId, Level = level };

        [Fact]
        public void EveryHeroKitIsExactlyTwoActivesPlusTwoPassives()
        {
            foreach (var def in Cfg.Heroes.Values)
            {
                var hero = HeroAt(def.DefId, 1);
                Assert.Equal(4, Skills.Known(hero, Cfg).Count);
                Assert.Equal(2, Skills.KnownActive(hero, Cfg).Count);
                Assert.Equal(2, Skills.KnownPassive(hero, Cfg).Count);
            }
        }

        [Fact]
        public void EveryKitSkillHasADefinition()
        {
            foreach (var def in Cfg.Heroes.Values)
                foreach (var id in def.Skills)
                    Assert.True(Cfg.Skills.ContainsKey(id), $"{def.DefId}: kit skill '{id}' has no SkillDef");
        }

        [Fact]
        public void ActiveKitGrowsWithTheRevealCadence()
        {
            // §7.2 cadence: active1 L1 · passive1 L5 · active2 L10 · passive2 L15.
            Assert.Equal(new[] { "cleave" }, Skills.ActiveKit(HeroAt("warrior_basic", 1), Cfg));
            Assert.Equal(new[] { "cleave" }, Skills.ActiveKit(HeroAt("warrior_basic", 9), Cfg));
            Assert.Equal(new[] { "cleave", "warcry" }, Skills.ActiveKit(HeroAt("warrior_basic", 10), Cfg));
        }

        [Fact]
        public void KitPointBudgetExactlyMaxesTheKitAtLevelCap()
        {
            // 20 points at level 100 == sum of MaxRank across the 4 kit skills, for every hero.
            foreach (var def in Cfg.Heroes.Values)
            {
                int kitCapacity = def.Skills.Sum(id => Cfg.Skills[id].MaxRank);
                int pointsAtCap = Skills.PointsEarned(HeroAt(def.DefId, Cfg.Balance.MaxLevel), Cfg);
                Assert.Equal(kitCapacity, pointsAtCap);
            }
        }

        [Fact]
        public void UnlockLevelGatesInvestmentNotJustCasting()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var id = save.Heroes[0].Id;
            // Give levels past 5 (toughness unlock) but below 15 (vitality unlock).
            save = Progression.GrantPartyXp(save, 10_000, Cfg);
            var hero = save.Heroes[0];
            Assert.InRange(hero.Level, 6, 14);

            Assert.True(Skills.CanInvest(save, id, "toughness", Cfg));
            Assert.False(Skills.CanInvest(save, id, "vitality", Cfg));        // not revealed yet
            Assert.Same(save, Skills.InvestSkill(save, id, "vitality", Cfg)); // no-op
        }
    }
}
