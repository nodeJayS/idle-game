using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class StatsTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static HeroInstance Warrior(int level = 1) =>
            new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = level };

        [Fact]
        public void BaseStatsAtLevelOne()
        {
            var st = Stats.ComputeHeroStats(Warrior(1), Cfg);
            Assert.Equal(120, st.Get(StatKey.Hp));
            Assert.Equal(14, st.Get(StatKey.Atk));
            Assert.Equal(8, st.Get(StatKey.Def));
        }

        [Fact]
        public void WarriorHasSmallHpRegen()
        {
            var st = Stats.ComputeHeroStats(Warrior(1), Cfg);
            Assert.Equal(1.5, st.Get(StatKey.HpRegen));
        }

        [Fact]
        public void GrowthScalesWithLevel()
        {
            var st = Stats.ComputeHeroStats(Warrior(5), Cfg); // +4 levels of growth
            Assert.Equal(120 + 18 * 4, st.Get(StatKey.Hp)); // 192
            Assert.Equal(14 + 3 * 4, st.Get(StatKey.Atk));  // 26
            Assert.Equal(8 + 1.5 * 4, st.Get(StatKey.Def));  // 14
        }

        [Fact]
        public void EquippedGearAddsBaseStatsAndAffixes()
        {
            var sword = new Item
            {
                Id = "i1", BaseId = "rusty_sword", Rarity = Rarity.Rare, ItemLevel = 1,
                Affixes = new List<Affix> { new Affix { Stat = StatKey.Atk, Value = 5 } },
            };
            var st = Stats.ComputeHeroStats(Warrior(1), Cfg, new[] { sword });
            Assert.Equal(14 + 6 + 5, st.Get(StatKey.Atk)); // base 14 + sword 6 + affix 5
        }

        [Fact]
        public void PartyPowerIsPositiveForNewGame()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.True(Stats.ComputePartyPower(save, Cfg) > 0);
        }
    }
}
