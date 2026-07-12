using System;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class DerivedStatsTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        [Fact]
        public void AttacksPerSecondDefaultsToOneWhenMissingOrZero()
        {
            Assert.Equal(1.0, DerivedStats.AttacksPerSecond(new StatBlock()));
            Assert.Equal(1.0, DerivedStats.AttacksPerSecond(new StatBlock { [StatKey.AtkSpd] = 0 }));
            Assert.Equal(1.4, DerivedStats.AttacksPerSecond(new StatBlock { [StatKey.AtkSpd] = 1.4 }));
        }

        [Fact]
        public void CritMultiplierIsExpectedValue()
        {
            // 1 + p*(critDmg-1) = 1 + 0.25*(2.0-1) = 1.25
            var s = new StatBlock { [StatKey.CritChance] = 0.25, [StatKey.CritDmg] = 2.0 };
            Assert.Equal(1.25, DerivedStats.CritMultiplier(s), 9);
        }

        [Fact]
        public void CritMultiplierClampsChanceAndFloorsCritDmg()
        {
            // CritChance > 1 clamps to certain crit; CritDmg < 1 floors to 1 (no penalty), matching
            // the sim's `dmg *= max(1, CritDmg)`.
            var always = new StatBlock { [StatKey.CritChance] = 2.0, [StatKey.CritDmg] = 1.8 };
            Assert.Equal(1.8, DerivedStats.CritMultiplier(always), 9);

            var weak = new StatBlock { [StatKey.CritChance] = 0.5, [StatKey.CritDmg] = 0.4 };
            Assert.Equal(1.0, DerivedStats.CritMultiplier(weak), 9); // critDmg floored to 1 => no gain
        }

        [Fact]
        public void DpsMultipliesAtkRateAndCrit()
        {
            // 20 atk * 1.5 aps * (1 + 0.1*(2-1)) = 20 * 1.5 * 1.1 = 33
            var s = new StatBlock
            {
                [StatKey.Atk] = 20, [StatKey.AtkSpd] = 1.5,
                [StatKey.CritChance] = 0.1, [StatKey.CritDmg] = 2.0,
            };
            Assert.Equal(33.0, DerivedStats.Dps(s), 9);
        }

        [Fact]
        public void EffectiveHpEqualsLifeWhenNoDef()
        {
            var s = new StatBlock { [StatKey.Hp] = 200, [StatKey.Def] = 0 };
            Assert.Equal(200.0, DerivedStats.EffectiveHp(s, referenceHit: 50), 9);
        }

        [Fact]
        public void EffectiveHpRewardsDefAgainstReferenceHit()
        {
            // R=50, Def=30 => per-hit cost 20; EHP = Hp*R/cost = 200*50/20 = 500.
            var s = new StatBlock { [StatKey.Hp] = 200, [StatKey.Def] = 30 };
            Assert.Equal(500.0, DerivedStats.EffectiveHp(s, referenceHit: 50), 9);
        }

        [Fact]
        public void EffectiveHpFloorsPerHitCostAtOne()
        {
            // Def >= R: the sim floors damage at 1/hit, so EHP = Hp*R (huge but finite).
            var s = new StatBlock { [StatKey.Hp] = 100, [StatKey.Def] = 999 };
            Assert.Equal(100.0 * 40.0, DerivedStats.EffectiveHp(s, referenceHit: 40), 9);
        }

        [Fact]
        public void StageReferenceHitScalesBossAtkGeometrically()
        {
            // goblin_king base Atk = 12; stage 1 (monsterLevel 1) => no scaling.
            Assert.Equal(12.0, DerivedStats.StageReferenceHit(Cfg, 1), 9);

            // stage 5 => 12 * 1.08^4
            double expected = 12.0 * Math.Pow(Cfg.Balance.MonsterDmgGrowth, 4);
            Assert.Equal(expected, DerivedStats.StageReferenceHit(Cfg, 5), 9);

            // deeper stage hits harder
            Assert.True(DerivedStats.StageReferenceHit(Cfg, 20) > DerivedStats.StageReferenceHit(Cfg, 5));
        }

        [Fact]
        public void StageReferenceHitScalesEndlessAndFallsBackForEmptyConfig()
        {
            // A stage past the table is a real endless row now (10.8): it scales UP, not back to stage 1.
            Assert.True(DerivedStats.StageReferenceHit(Cfg, 999) > DerivedStats.StageReferenceHit(Cfg, 100));

            // An empty config (no stages AND no zones) has no row to scale — falls back to 1.0, not throwing.
            var empty = GameConfig.Default();
            empty.Stages.Clear();
            empty.Zones.Clear();
            Assert.Equal(1.0, DerivedStats.StageReferenceHit(empty, 5), 9);
        }

        [Fact]
        public void DerivedStatsTrackARealHeroBuild()
        {
            // End-to-end against the actual starter Warrior: numbers stay positive and the
            // stage-aware EHP convenience agrees with the explicit reference-hit form.
            var save = Save.NewGame(1, Cfg, 0);
            var hero = save.Heroes[0];
            var stats = Stats.ComputeHeroStats(hero, Cfg, Stats.ResolveEquipped(save, hero));

            Assert.True(DerivedStats.Dps(stats) > 0);
            Assert.True(DerivedStats.EffectiveHp(stats, Cfg, 1) >= stats.Get(StatKey.Hp));
            Assert.Equal(
                DerivedStats.EffectiveHp(stats, DerivedStats.StageReferenceHit(Cfg, 3)),
                DerivedStats.EffectiveHp(stats, Cfg, 3), 9);
        }
    }
}
