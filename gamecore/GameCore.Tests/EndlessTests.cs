using System;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Endless mode (10.8a): the campaign table caps at 100 stages, but StageFor generates rows past it
    // from the same boot-loop formulas (monster scaling continues at the gentle last-tier rate),
    // ZoneForStage cycles the zone list, HighestStage caps at the table while EndlessBest carries the
    // uncapped push, SetStage opens endless only after the campaign is done, and SaveVersion bumps to 3.
    // The sweep guards the EndlessBest field against a dropped copy site (the 10.5d SetId bug class).
    public class EndlessTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();
        private const long Now = 1_780_000_000_000L;

        private static SaveState Fresh() => Save.NewGame(1, Cfg, Now);

        // ---------------- StageFor ----------------

        [Fact]
        public void StageForReturnsTheConfiguredRowInstance()
        {
            Assert.Same(Cfg.Stages.Find(r => r.Stage == 1), Cfg.StageFor(1));
            Assert.Same(Cfg.Stages.Find(r => r.Stage == 100), Cfg.StageFor(100));
        }

        [Fact]
        public void StageForGeneratesAnEndlessRowMatchingTheBootFormulas()
        {
            const int stage = 137;
            int tier = Cfg.Balance.Tier(stage);
            var row = Cfg.StageFor(137);
            Assert.NotNull(row);
            Assert.Equal(137, row!.MonsterLevel);
            Assert.Equal(3 + 137 / 5, row.PackCount);
            Assert.Equal(1 + Cfg.Balance.DropRatePerStage * (stage - 1) + Cfg.Balance.DropRateTierBonus * tier,
                row.DropRateMult, 10);
            Assert.Equal(stage + Cfg.Balance.ItemLevelTierBonus * tier, row.AffixItemLevel);
            Assert.Equal(Cfg.ZoneForStage(137)!.BossId, row.BossId);

            Assert.Same(row, Cfg.StageFor(137)); // memoized: same instance on repeat
        }

        // ---------------- ZoneForStage cycling ----------------

        [Fact]
        public void ZoneForStageCyclesOnlyPastTheTable()
        {
            Assert.Same(Cfg.Zones[0], Cfg.ZoneForStage(5));    // within the table, tier 0
            Assert.Same(Cfg.Zones[9], Cfg.ZoneForStage(95));   // within the table, tier 9 (last)
            Assert.Same(Cfg.Zones[0], Cfg.ZoneForStage(101));  // tier 10 wraps to zone 0
            Assert.Same(Cfg.Zones[1], Cfg.ZoneForStage(111));  // tier 11 wraps to zone 1
        }

        // ---------------- no scaling cliff ----------------

        [Fact]
        public void MonsterScalingHasNoCliffPastTheTable()
        {
            double lastHp = Cfg.Balance.MonsterHpGrowthByTier[Cfg.Balance.MonsterHpGrowthByTier.Length - 1];
            double lastDmg = Cfg.Balance.MonsterDmgGrowthByTier[Cfg.Balance.MonsterDmgGrowthByTier.Length - 1];
            Assert.Equal(lastHp, Cfg.Balance.MonsterHpMult(101) / Cfg.Balance.MonsterHpMult(100), 10);
            Assert.Equal(lastDmg, Cfg.Balance.MonsterDmgMult(101) / Cfg.Balance.MonsterDmgMult(100), 10);
        }

        // ---------------- OnStageCleared ----------------

        [Fact]
        public void OnStageClearedCapsHighestAndTracksEndlessDepth()
        {
            var s = Fresh();
            s.Progress.HighestStage = 100;

            var a = Progression.OnStageCleared(s, 101, Cfg);
            Assert.Equal(100, a.Progress.HighestStage);   // caps at the table height
            Assert.Equal(1, a.Progress.EndlessBest);       // depth 101 - 100
            Assert.Equal(102, a.Progress.CurrentStage);    // forward is uncapped

            var deep = Progression.OnStageCleared(a, 105, Cfg);
            Assert.Equal(5, deep.Progress.EndlessBest);
            var back = Progression.OnStageCleared(deep, 103, Cfg);
            Assert.Equal(5, back.Progress.EndlessBest);    // never regresses
        }

        [Fact]
        public void OnStageClearedBelowTheTableIsUnchanged()
        {
            var s = Progression.OnStageCleared(Fresh(), 50, Cfg);
            Assert.Equal(50, s.Progress.HighestStage);
            Assert.Equal(0, s.Progress.EndlessBest);
            Assert.Equal(51, s.Progress.CurrentStage);
        }

        // ---------------- SetStage ----------------

        [Fact]
        public void SetStageOpensEndlessOnlyAfterTheCampaign()
        {
            var done = Fresh();
            done.Progress.HighestStage = 100;
            done.Progress.EndlessBest = 5;
            Assert.Equal(106, Progression.SetStage(done, 106, Cfg).Progress.CurrentStage); // next endless depth
            Assert.Throws<ArgumentOutOfRangeException>(() => Progression.SetStage(done, 107, Cfg));

            var midCampaign = Fresh();
            midCampaign.Progress.HighestStage = 99;
            Assert.Throws<ArgumentOutOfRangeException>(() => Progression.SetStage(midCampaign, 101, Cfg));

            var low = Fresh();
            low.Progress.HighestStage = 50;
            Assert.Equal(51, Progression.SetStage(low, 51, Cfg).Progress.CurrentStage); // next uncleared
            Assert.Throws<ArgumentOutOfRangeException>(() => Progression.SetStage(low, 52, Cfg));
        }

        // ---------------- migration ----------------

        [Fact]
        public void MigrationStampsV2ToV3AndRejectsNewer()
        {
            var v2 = Fresh();
            v2.Version = 2;
            var m = Save.Migrate(v2);
            Assert.Equal(3, m.Version);
            Assert.Equal(0, m.Progress.EndlessBest);

            var v4 = Fresh();
            v4.Version = 4;
            Assert.Throws<InvalidOperationException>(() => Save.Migrate(v4));
        }

        // ---------------- the sweep ----------------

        [Fact]
        public void EndlessBestSurvivesEveryReducerThatCopiesProgress()
        {
            var s = Fresh();
            s.Progress.EndlessBest = 7;

            Assert.Equal(7, Progression.SetStage(s, 1, Cfg).Progress.EndlessBest);
            Assert.Equal(7, Progression.OnStageCleared(s, 3, Cfg).Progress.EndlessBest);
            Assert.Equal(7, Tower.RecordClear(s, 1, Cfg).Progress.EndlessBest);
            Assert.Equal(7, Crypt.RecordFloorClear(s, 1, Cfg).Progress.EndlessBest);
            Assert.Equal(7, Inventory.SetImprintGuard(s, true).Progress.EndlessBest);
            Assert.Equal(7, Achievements.Record(s, AchievementMetric.MonstersKilled, 5, Cfg).save.Progress.EndlessBest);
            Assert.Equal(7, DailyLogin.Claim(s, Cfg, Now).save.Progress.EndlessBest);

            // Intro pays its first beat once lifetime kills clear SlayTarget — drive it through WithIntro.
            var slain = Achievements.Record(s, AchievementMetric.MonstersKilled, 100, Cfg).save;
            Assert.Equal(7, IntroQuests.Sync(slain, Cfg).save.Progress.EndlessBest);
        }
    }
}
