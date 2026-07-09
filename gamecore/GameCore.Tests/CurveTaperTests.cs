using System;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// The tapered monster HP/damage curve (§5.3, 10.1c): a per-10-stage-tier eased growth that
    /// replaces the flat MonsterHpGrowth^stage (which mathematically ended the ladder near stage 53).
    /// These pin the pure Balance functions' SHAPE — monotone, early tiers unchanged, deep tiers
    /// tapered — not the exact tuning table (which the walls chart owns).
    /// </summary>
    public class CurveTaperTests
    {
        private static readonly BalanceConstants B = GameConfig.Default().Balance;

        [Fact]
        public void HpMultIsOneAtLevelOneAndMonotoneIncreasing()
        {
            Assert.Equal(1.0, B.MonsterHpMult(1), 12);
            double prev = B.MonsterHpMult(1);
            for (int lvl = 2; lvl <= B.MaxLevel; lvl++)
            {
                double cur = B.MonsterHpMult(lvl);
                Assert.True(cur > prev, $"HP mult must strictly increase (stage {lvl}: {cur} vs {prev})");
                prev = cur;
            }
        }

        [Fact]
        public void EarlyTiersKeepTheFlatRateSoStages1To20AreUnchanged()
        {
            // Tiers 0 and 1 (stages 1..20) stay at the tier-0 anchor rate, so the cumulative HP there
            // equals the old flat MonsterHpGrowth^(stage-1) exactly — early game plays unchanged.
            double rate = B.MonsterHpGrowthByTier[0];
            for (int stage = 1; stage <= 20; stage++)
                Assert.Equal(Math.Pow(rate, stage - 1), B.MonsterHpMult(stage), 9);
        }

        [Fact]
        public void DeepPerStageGrowthIsGentlerThanEarly()
        {
            // The last-tier per-stage step is far below the early 1.18 — the whole point of the taper.
            double earlyStep = B.MonsterHpMult(2) / B.MonsterHpMult(1);   // stage-2 growth (tier 0)
            double deepStep = B.MonsterHpMult(100) / B.MonsterHpMult(99); // stage-100 growth (last tier)
            Assert.True(deepStep < earlyStep,
                $"deep step {deepStep} should be gentler than early {earlyStep}");
            Assert.True(deepStep < 1.05, $"deep HP growth should taper well under 1.05 (got {deepStep})");
        }

        [Fact]
        public void TaperCutsDeepHpMassivelyVsTheOldFlatCurve()
        {
            // At stage 100 the tapered curve must sit far below the old 1.18^99 wall that killed the
            // ladder — a large multiple smaller (the ladder is now finishable).
            double flat100 = Math.Pow(B.MonsterHpGrowth, 99);
            Assert.True(B.MonsterHpMult(100) < flat100 / 1000.0,
                $"tapered HP(100)={B.MonsterHpMult(100)} must be <<< flat {flat100}");
        }

        [Fact]
        public void DamageMultMirrorsHpShape()
        {
            Assert.Equal(1.0, B.MonsterDmgMult(1), 12);
            // Tier-0 stays MonsterDmgGrowth (so DerivedStats' early survival read is unchanged).
            double rate = B.MonsterDmgGrowthByTier[0];
            Assert.Equal(B.MonsterDmgGrowth, rate, 12);
            for (int stage = 1; stage <= 20; stage++)
                Assert.Equal(Math.Pow(rate, stage - 1), B.MonsterDmgMult(stage), 9);
            // Monotone and deep-tapered.
            Assert.True(B.MonsterDmgMult(100) > B.MonsterDmgMult(99));
            Assert.True(B.MonsterDmgMult(100) / B.MonsterDmgMult(99) < B.MonsterDmgMult(2) / B.MonsterDmgMult(1));
        }

        // --- major-boss taper (10.1 follow-up): the every-10th multiplier eases by tier too ---

        [Fact]
        public void MajorBossMultKeepsTheFullAnchorThroughStage40()
        {
            // Tiers 0-3 (majors 10/20/30/40) keep the flat MajorBossMult — early game unchanged.
            foreach (int stage in new[] { 10, 20, 30, 40 })
                Assert.Equal(B.MajorBossMult, B.MajorBossMultAt(stage), 12);
        }

        [Fact]
        public void MajorBossMultTapersInTheMidBand()
        {
            // Majors 50-90 sit BELOW the anchor (the on-curve soft-wall fix): with the tapered stage
            // curve the flat x2 major was the wall that stopped legendary+mid play dead at stage 50.
            foreach (int stage in new[] { 50, 60, 70, 80, 90 })
                Assert.True(B.MajorBossMultAt(stage) < B.MajorBossMult,
                    $"major at stage {stage} should taper below the anchor (got {B.MajorBossMultAt(stage)})");
            // ...and rises monotonically back toward the capstone (difficulty keeps climbing).
            for (int stage = 60; stage <= 100; stage += 10)
                Assert.True(B.MajorBossMultAt(stage) >= B.MajorBossMultAt(stage - 10),
                    $"major mult must not decline from {stage - 10} to {stage}");
        }

        [Fact]
        public void MajorBossMultRestoresTheCapstoneAtStage100()
        {
            // The last tier restores the full x2 — stage 100 stays the prestige gate (~L100
            // mythic + max stacks), bit-identical to the pre-taper fight.
            Assert.Equal(B.MajorBossMult, B.MajorBossMultAt(100), 12);
            // Beyond-100 stages (future endless mode) clamp to the last entry.
            Assert.Equal(B.MajorBossMultAt(100), B.MajorBossMultAt(150), 12);
        }
    }
}
