using System;
using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>FTUE slice (a): the staged-reveal feature-gating table
    /// (<see cref="Progression.FeatureUnlocked"/>) + the New-Game arming flag.</summary>
    public class FtueTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static readonly Feature[] AllFeatures =
        {
            Feature.AutoAdvance, Feature.IdleClaim, Feature.DailyLogin, Feature.Achievements,
            Feature.Modifiers, Feature.Modes, Feature.Gacha,
        };

        private static SaveState Fresh() => Save.NewGame(1234u, Cfg, 0L);

        // ---- unarmed (existing) saves see everything, forever ----

        [Fact]
        public void DefaultSaveIsUnarmed_UnlocksEverything()
        {
            var save = new SaveState(); // Intro.Armed defaults false, HighestStage 0
            Assert.False(save.Progress.Intro.Armed);
            foreach (var f in AllFeatures)
                Assert.True(Progression.FeatureUnlocked(f, save), $"{f} should be unlocked on an unarmed save");
        }

        [Fact]
        public void MigratedOldSave_UnarmedAndFullyUnlocked()
        {
            // A v1 save JSON that predates FTUE: Migrate backfills Intro (Armed=false).
            var old = new SaveState { Version = 1 };
            var migrated = Save.Migrate(old);
            Assert.NotNull(migrated.Progress.Intro);
            Assert.False(migrated.Progress.Intro.Armed);
            foreach (var f in AllFeatures)
                Assert.True(Progression.FeatureUnlocked(f, migrated));
        }

        [Fact]
        public void UnarmedSave_UnlockedEvenAtStageZero()
        {
            var save = new SaveState();
            save.Progress.HighestStage = 0;
            Assert.True(Progression.FeatureUnlocked(Feature.Gacha, save));
        }

        // ---- New Game arms; every existing save stays unarmed ----

        [Fact]
        public void NewGame_Arms()
        {
            Assert.True(Fresh().Progress.Intro.Armed);
        }

        // ---- an armed fresh save gates exactly per the §7.4 schedule ----

        [Theory]
        [InlineData(Feature.AutoAdvance, 2)]
        [InlineData(Feature.IdleClaim, 3)]
        [InlineData(Feature.DailyLogin, 3)]
        [InlineData(Feature.Achievements, 5)]
        [InlineData(Feature.Modifiers, 10)]
        [InlineData(Feature.Modes, 10)]
        [InlineData(Feature.Gacha, 12)]
        public void ArmedSave_BoundaryAtRevealStage(Feature feature, int revealStage)
        {
            var save = Fresh();

            save.Progress.HighestStage = revealStage - 1;
            Assert.False(Progression.FeatureUnlocked(feature, save), $"{feature} must be LOCKED at stage {revealStage - 1}");

            save.Progress.HighestStage = revealStage;
            Assert.True(Progression.FeatureUnlocked(feature, save), $"{feature} must UNLOCK at stage {revealStage}");
        }

        [Fact]
        public void ArmedFreshSave_LocksEverythingBeforeStageTwo()
        {
            var save = Fresh(); // HighestStage 0
            foreach (var f in AllFeatures)
                Assert.False(Progression.FeatureUnlocked(f, save), $"{f} should be hidden at the very start");
        }

        [Fact]
        public void ArmedSave_UnlocksEverythingByStageTwelve()
        {
            var save = Fresh();
            save.Progress.HighestStage = 12;
            foreach (var f in AllFeatures)
                Assert.True(Progression.FeatureUnlocked(f, save), $"{f} should be unlocked by stage 12");
        }

        [Fact]
        public void RevealStageTable_MatchesLockedSchedule()
        {
            Assert.Equal(2, Progression.FeatureRevealStage[Feature.AutoAdvance]);
            Assert.Equal(3, Progression.FeatureRevealStage[Feature.IdleClaim]);
            Assert.Equal(3, Progression.FeatureRevealStage[Feature.DailyLogin]);
            Assert.Equal(5, Progression.FeatureRevealStage[Feature.Achievements]);
            Assert.Equal(10, Progression.FeatureRevealStage[Feature.Modifiers]);
            Assert.Equal(10, Progression.FeatureRevealStage[Feature.Modes]);
            Assert.Equal(12, Progression.FeatureRevealStage[Feature.Gacha]);
        }

        // ---- arming survives reducers (every ProgressState copy site threads Intro) ----

        [Fact]
        public void Arming_SurvivesStageClear()
        {
            var save = Fresh();
            save = Progression.OnStageCleared(save, 1, Cfg);
            Assert.True(save.Progress.Intro.Armed, "OnStageCleared must carry Intro forward");
            // and gating still works off the new HighestStage (1): auto-advance still locked
            Assert.False(Progression.FeatureUnlocked(Feature.AutoAdvance, save));
            save = Progression.OnStageCleared(save, 2, Cfg);
            Assert.True(save.Progress.Intro.Armed);
            Assert.True(Progression.FeatureUnlocked(Feature.AutoAdvance, save));
        }

        [Fact]
        public void Arming_SurvivesSetStage()
        {
            var save = Fresh();
            save.Progress.HighestStage = 5;
            save = Progression.SetStage(save, 3, Cfg);
            Assert.True(save.Progress.Intro.Armed, "SetStage must carry Intro forward");
        }

        // ---- purity: no mutation, no rng, deterministic ----

        [Fact]
        public void FeatureUnlocked_IsPureAndDeterministic()
        {
            var save = Fresh();
            save.Progress.HighestStage = 4;
            int cursorBefore = save.RngCursor;

            bool a = Progression.FeatureUnlocked(Feature.Achievements, save);
            bool b = Progression.FeatureUnlocked(Feature.Achievements, save);

            Assert.Equal(a, b);
            Assert.False(a); // stage 4 < 5
            Assert.Equal(cursorBefore, save.RngCursor); // never advances rng
            Assert.Equal(4, save.Progress.HighestStage); // never mutates the save
        }
    }
}
