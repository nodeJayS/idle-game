using System;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class StageTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static SaveState Save(int highest = 0, int current = 1)
        {
            var s = Save0();
            s.Progress.HighestStage = highest;
            s.Progress.CurrentStage = current;
            return s;
        }

        private static SaveState Save0() => IdleGame.GameCore.Save.NewGame(1, Cfg, 0);

        [Fact]
        public void OnStageClearedBumpsAndAdvances()
        {
            var after = Progression.OnStageCleared(Save(highest: 0, current: 1), 1);
            Assert.Equal(1, after.Progress.HighestStage);
            Assert.Equal(2, after.Progress.CurrentStage);
        }

        [Fact]
        public void OnStageClearedReplayDoesNotLowerHighest()
        {
            // Already cleared up to 5; replaying stage 2 keeps HighestStage at 5.
            var after = Progression.OnStageCleared(Save(highest: 5, current: 2), 2);
            Assert.Equal(5, after.Progress.HighestStage);
            Assert.Equal(3, after.Progress.CurrentStage);
        }

        [Fact]
        public void OnStageClearedIsPure()
        {
            var before = Save(highest: 0, current: 1);
            var after = Progression.OnStageCleared(before, 1);

            Assert.Equal(0, before.Progress.HighestStage); // input untouched
            Assert.Equal(1, before.Progress.CurrentStage);
            Assert.NotSame(before, after);
            Assert.NotSame(before.Progress, after.Progress);
        }

        [Fact]
        public void SetStageAllowsReplayAndNextUncleared()
        {
            var save = Save(highest: 3, current: 4);

            Assert.Equal(1, Progression.SetStage(save, 1, Cfg).Progress.CurrentStage); // replay
            Assert.Equal(3, Progression.SetStage(save, 3, Cfg).Progress.CurrentStage); // cleared
            Assert.Equal(4, Progression.SetStage(save, 4, Cfg).Progress.CurrentStage); // next uncleared
        }

        [Fact]
        public void SetStageCannotSkipAhead()
        {
            var save = Save(highest: 3, current: 4);
            Assert.Throws<ArgumentOutOfRangeException>(() => Progression.SetStage(save, 5, Cfg));
            Assert.Throws<ArgumentOutOfRangeException>(() => Progression.SetStage(save, 0, Cfg));
        }

        [Fact]
        public void SetStageIsPure()
        {
            var before = Save(highest: 3, current: 4);
            var after = Progression.SetStage(before, 2, Cfg);

            Assert.Equal(4, before.Progress.CurrentStage); // input untouched
            Assert.NotSame(before, after);
            Assert.Equal(3, after.Progress.HighestStage);  // highest preserved
        }

        [Fact]
        public void EveryTenthStageIsMajorBoss()
        {
            Assert.True(Cfg.Stages.First(s => s.Stage == 10).IsMajorBoss);
            Assert.True(Cfg.Stages.First(s => s.Stage == 20).IsMajorBoss);
            Assert.False(Cfg.Stages.First(s => s.Stage == 1).IsMajorBoss);
            Assert.False(Cfg.Stages.First(s => s.Stage == 15).IsMajorBoss);
        }

        // --- M8.3: boss-gate progression contract ---
        // Beating the current stage's timed boss challenge is what advances progression:
        // OnStageCleared(stage) raises HighestStage to that stage and moves to the next.

        [Fact]
        public void BeatingTheBossGateAdvancesProgression()
        {
            var save = Save0(); // HighestStage 0, CurrentStage 1
            var party = save.Heroes.ToArray();

            var fight = Combat.InitBossChallenge(party, save.Progress.CurrentStage, Cfg, new Rng(1));
            fight.Entities.First(e => e.IsBoss).Stats[StatKey.Hp] = 1; // trivial kill
            foreach (var e in fight.Entities)
                if (e.Team == Team.Party) e.Stats[StatKey.Atk] = 100000;
            Combat.RunToEnd(fight, Cfg, new Rng(1));
            Assert.Equal(CombatStatus.Won, fight.Status);

            var after = Progression.OnStageCleared(save, fight.Stage);
            Assert.Equal(1, after.Progress.HighestStage);
            Assert.Equal(2, after.Progress.CurrentStage);

            // and the newly-unlocked frontier (HighestStage + 1) is selectable to farm
            Assert.Equal(2, Progression.SetStage(after, 2, Cfg).Progress.CurrentStage);
        }

        [Fact]
        public void MajorBossIsTougherThanRegularStageBoss()
        {
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };

            double BossHp(int stage)
            {
                var s = Combat.InitCombat(party, stage, Cfg, new Rng(1));
                return s.Entities.First(e => e.IsBoss).MaxHp;
            }

            // Compare the major boss at stage 10 to the same boss def at the
            // adjacent non-major stages, ruling out plain monster-level scaling.
            double major = BossHp(10);
            Assert.True(major > BossHp(9));
            Assert.True(major > BossHp(11));
        }
    }
}
