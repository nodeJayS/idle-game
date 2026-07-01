using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class AchievementsTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static AchievementDef Def(string id) => Cfg.Achievements.First(a => a.Id == id);

        [Fact]
        public void NewGameStartsWithNoCountersOrClaims()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Empty(save.Progress.Achievements.Counters);
            Assert.Empty(save.Progress.Achievements.Claimed);
            Assert.Equal(0, Achievements.Counter(save, AchievementMetric.MonstersKilled));
            Assert.Equal(0, Achievements.TiersClaimed(save, Def("slayer")));
        }

        [Fact]
        public void AddMetricAccumulatesAndIsPure()
        {
            var save = Save.NewGame(1, Cfg, 0);

            var (a, _) = Achievements.Record(save, AchievementMetric.MonstersKilled, 30, Cfg);
            var (b, _) = Achievements.Record(a, AchievementMetric.MonstersKilled, 30, Cfg);

            Assert.Equal(0, Achievements.Counter(save, AchievementMetric.MonstersKilled)); // input untouched
            Assert.Equal(30, Achievements.Counter(a, AchievementMetric.MonstersKilled));
            Assert.Equal(60, Achievements.Counter(b, AchievementMetric.MonstersKilled));
        }

        [Fact]
        public void MaxMetricKeepsThePeakAndIgnoresRegressions()
        {
            var save = Save.NewGame(1, Cfg, 0);

            var (a, _) = Achievements.Record(save, AchievementMetric.HighestStage, 12, Cfg);
            var (b, comp) = Achievements.Record(a, AchievementMetric.HighestStage, 7, Cfg); // lower — no move

            Assert.Equal(12, Achievements.Counter(a, AchievementMetric.HighestStage));
            Assert.Same(a, b);       // non-improving MAX shares the ref (no-op)
            Assert.Empty(comp);
        }

        [Fact]
        public void NonPositiveAddIsANoOp()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var (a, comp) = Achievements.Record(save, AchievementMetric.MonstersKilled, 0, Cfg);
            Assert.Same(save, a);
            Assert.Empty(comp);
        }

        [Fact]
        public void CrossingATierPaysTheRewardOnce()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var slayer = Def("slayer");
            var tier0 = slayer.Tiers[0];

            // First cross: pays out and reports the completion.
            var (a, comp) = Achievements.Record(save, AchievementMetric.MonstersKilled, tier0.Threshold, Cfg);
            Assert.Single(comp);
            Assert.Equal("slayer", comp[0].AchId);
            Assert.Equal(0, comp[0].TierIndex);
            Assert.Equal(tier0.RewardGold, a.Currencies.GetValueOrDefault("gold"));
            Assert.Equal(tier0.RewardScrap, a.Currencies.GetValueOrDefault("scrap"));
            Assert.Equal(1, Achievements.TiersClaimed(a, slayer));

            // More kills that stay below tier 1: no further payout, gold unchanged.
            var (b, comp2) = Achievements.Record(a, AchievementMetric.MonstersKilled, 5, Cfg);
            Assert.Empty(comp2);
            Assert.Equal(tier0.RewardGold, b.Currencies.GetValueOrDefault("gold"));
            Assert.Equal(1, Achievements.TiersClaimed(b, slayer));
        }

        [Fact]
        public void OneBigJumpCompletesEveryCrossedTierAtOnce()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var slayer = Def("slayer");
            long huge = slayer.Tiers.Last().Threshold; // clears the whole ladder in one call

            var (a, comp) = Achievements.Record(save, AchievementMetric.MonstersKilled, huge, Cfg);

            Assert.Equal(slayer.Tiers.Count, comp.Count);
            Assert.Equal(slayer.Tiers.Count, Achievements.TiersClaimed(a, slayer));
            // Tiers reported in ascending order.
            Assert.Equal(Enumerable.Range(0, slayer.Tiers.Count), comp.Select(u => u.TierIndex));
            long expectedGold = slayer.Tiers.Sum(t => t.RewardGold);
            Assert.Equal(expectedGold, a.Currencies.GetValueOrDefault("gold"));
        }

        [Fact]
        public void FullyClaimedAchievementNeverPaysAgain()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var slayer = Def("slayer");
            long huge = slayer.Tiers.Last().Threshold;

            var (a, _) = Achievements.Record(save, AchievementMetric.MonstersKilled, huge, Cfg);
            long goldAfter = a.Currencies.GetValueOrDefault("gold");

            var (b, comp) = Achievements.Record(a, AchievementMetric.MonstersKilled, huge, Cfg); // way past the top
            Assert.Empty(comp);
            Assert.Equal(goldAfter, b.Currencies.GetValueOrDefault("gold")); // no double pay
        }

        [Fact]
        public void RecordOnlyTouchesItsOwnMetric()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var (a, comp) = Achievements.Record(save, AchievementMetric.HighestStage, 10, Cfg);

            // Explorer tier 0 fires; the kill-based "slayer" is untouched.
            Assert.Contains(comp, u => u.AchId == "explorer");
            Assert.DoesNotContain(comp, u => u.AchId == "slayer");
            Assert.Equal(0, Achievements.Counter(a, AchievementMetric.MonstersKilled));
        }

        [Fact]
        public void RewardXpLevelsThePartyThroughTheSharedReducer()
        {
            var save = Save.NewGame(1, Cfg, 0);
            long xpBefore = save.Heroes[0].Xp;
            int lvlBefore = save.Heroes[0].Level;

            // Slayer tier 0 grants party XP; the fielded warrior should gain it.
            var (a, _) = Achievements.Record(save, AchievementMetric.MonstersKilled, Def("slayer").Tiers[0].Threshold, Cfg);
            var warrior = a.Heroes[0];
            Assert.True(warrior.Level > lvlBefore || warrior.Xp > xpBefore);
        }

        [Fact]
        public void MigrateBackfillsAchievementStateOnOlderSaves()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save.Progress.Achievements = null!; // simulate a pre-Lever-4 payload

            var migrated = Save.Migrate(save);
            Assert.NotNull(migrated.Progress.Achievements);
            Assert.NotNull(migrated.Progress.Achievements.Counters);
            Assert.NotNull(migrated.Progress.Achievements.Claimed);
        }

        [Fact]
        public void EveryCatalogAchievementHasAscendingTiers()
        {
            Assert.NotEmpty(Cfg.Achievements);
            foreach (var def in Cfg.Achievements)
            {
                Assert.NotEmpty(def.Tiers);
                for (int i = 1; i < def.Tiers.Count; i++)
                    Assert.True(def.Tiers[i].Threshold > def.Tiers[i - 1].Threshold,
                        $"{def.Id} tier {i} threshold not ascending");
            }
        }
    }
}
