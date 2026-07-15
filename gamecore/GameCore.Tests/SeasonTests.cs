using System;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Season track (10.16, mobile arc MM4): a calendar-MONTH battle pass — points from completed quests
    // only, ~30 auto-paying free tiers, lazy month rollover with no back-pay. These pin the exactly-once
    // point/tier accounting (incl. multi-tier jumps), the gem-vs-gold ladder shape, the rollover reset,
    // the attainability math, the threading sweep, the Migrate backfill, and purity. PURE C# sim only.
    public class SeasonTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static long Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
            new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        private static readonly long Now = Utc(2024, 1, 15, 12, 0);

        private static SaveState Fresh() => Save.NewGame(1, Cfg, Now);
        private static long Gold(SaveState s) => s.Currencies.TryGetValue("gold", out var g) ? g : 0;
        private static long Gems(SaveState s) => s.Currencies.TryGetValue(Cfg.Balance.PremiumCurrency, out var g) ? g : 0;
        // Quests to complete a whole number of tiers (PointsPerTier / PointsPerQuest quests per tier).
        private static int QuestsForTiers(int tiers) => tiers * Cfg.Balance.SeasonPointsPerTier / Cfg.Balance.SeasonPointsPerQuest;

        // ============================ defaults ============================

        [Fact]
        public void NewGameHasAnEmptySeason()
        {
            var s = Fresh();
            Assert.Equal("", s.Progress.Season.Id);
            Assert.Equal(0, s.Progress.Season.Points);
            Assert.Equal(0, s.Progress.Season.TiersPaid);

            var snap = Season.Snapshot(s, Cfg, Now);
            Assert.Equal("2024-01", snap.Id);
            Assert.Equal(0, snap.Points);
            Assert.Equal(Cfg.Balance.SeasonPointsPerTier, snap.NextTierAt); // tier 1 threshold
            Assert.Equal(Cfg.Balance.SeasonTierCount, snap.Ladder.Count);
        }

        // ============================ points: exactly-once ============================

        [Fact]
        public void AwardAddsExactlyPointsPerQuestPerCompletion()
        {
            var s = Fresh();
            var (a, _) = Season.AwardPoints(s, 3, Cfg, Now);
            Assert.Equal(3L * Cfg.Balance.SeasonPointsPerQuest, a.Progress.Season.Points);
            Assert.Equal("2024-01", a.Progress.Season.Id);
            Assert.Equal(0, s.Progress.Season.Points); // input untouched (pure)
        }

        [Fact]
        public void ZeroCompletionsSameMonthSharesTheRef()
        {
            // Establish the current month first (a fresh save's Id="" reads as a different month, which
            // legitimately writes the reset stamp). Once stamped, a 0-completion award is a true no-op.
            var (s, _) = Season.AwardPoints(Fresh(), 1, Cfg, Now);
            var (a, paid) = Season.AwardPoints(s, 0, Cfg, Now);
            Assert.Same(s, a);      // genuine no-op shares the whole save
            Assert.Empty(paid);
        }

        // ============================ tiers: crossing pays once ============================

        [Fact]
        public void CrossingATierPaysItExactlyOnce()
        {
            var s = Fresh();
            long gold0 = Gold(s);

            // Complete exactly one tier's worth of quests.
            var (a, paidA) = Season.AwardPoints(s, QuestsForTiers(1), Cfg, Now);
            Assert.Single(paidA);
            Assert.Equal(1, paidA[0].Tier);
            Assert.Equal(1, a.Progress.Season.TiersPaid);
            Assert.True(Gold(a) > gold0); // tier 1 is a gold tier — paid

            // More points that DON'T cross the next tier pay nothing new.
            var (b, paidB) = Season.AwardPoints(a, 1, Cfg, Now);
            Assert.Empty(paidB);
            Assert.Equal(1, b.Progress.Season.TiersPaid);
            Assert.Equal(Gold(a), Gold(b));
        }

        [Fact]
        public void MultiTierJumpPaysEveryCrossedTierOnce()
        {
            var s = Fresh();
            // Jump straight through tiers 1..5 in one award.
            var (a, paid) = Season.AwardPoints(s, QuestsForTiers(5), Cfg, Now);
            Assert.Equal(5, paid.Count);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, paid.Select(p => p.Tier).ToArray());
            Assert.Equal(5, a.Progress.Season.TiersPaid);
        }

        // ============================ gem-vs-gold ladder ============================

        [Fact]
        public void EveryFifthTierPaysGemsTheRestPayGold()
        {
            var s = Fresh();
            var (a, paid) = Season.AwardPoints(s, QuestsForTiers(5), Cfg, Now);

            // Tiers 1–4 gold, tier 5 a gem milestone.
            for (int i = 0; i < 4; i++)
            {
                Assert.False(paid[i].IsMilestone);
                Assert.True(paid[i].Gold > 0);
                Assert.Equal(0, paid[i].Gems);
            }
            Assert.True(paid[4].IsMilestone);
            Assert.Equal(Cfg.Balance.SeasonGemsPerMilestone, paid[4].Gems);
            Assert.Equal(0, paid[4].Gold);

            // Currencies actually moved: gold from the four gold tiers, gems from the one milestone.
            Assert.Equal(paid.Take(4).Sum(p => p.Gold), Gold(a) - Gold(s));
            Assert.Equal((long)Cfg.Balance.SeasonGemsPerMilestone, Gems(a) - Gems(s));
        }

        [Fact]
        public void LadderShapeMatchesTheMilestoneCadence()
        {
            var ladder = Season.Ladder(Cfg, 10);
            Assert.Equal(Cfg.Balance.SeasonTierCount, ladder.Count);
            foreach (var r in ladder)
            {
                bool milestone = r.Tier % Cfg.Balance.SeasonMilestoneEvery == 0;
                Assert.Equal(milestone, r.IsMilestone);
                if (milestone) { Assert.True(r.Gems > 0); Assert.Equal(0, r.Gold); }
                else { Assert.True(r.Gold > 0); Assert.Equal(0, r.Gems); }
            }
        }

        [Fact]
        public void GoldTierScalesWithTheContemporaryQuestReward()
        {
            // A gold tier pays SeasonTierGoldMult × a quest's gold reward at the player's stage.
            int stage = 30;
            long questGold = Math.Max(10, Cfg.Balance.GoldPerSec(stage) * 45);
            long expected = (long)Math.Floor(questGold * Cfg.Balance.SeasonTierGoldMult);
            Assert.Equal(expected, Season.TierReward(1, Cfg, stage).Gold);
            // Deeper stage ⇒ richer tier gold (contemporary scaling).
            Assert.True(Season.TierReward(1, Cfg, 60).Gold > Season.TierReward(1, Cfg, 5).Gold);
        }

        // ============================ month rollover ============================

        [Fact]
        public void RolloverResetsOnMonthChangeWithNoBackPay()
        {
            long jan = Utc(2024, 1, 20, 12, 0), feb = Utc(2024, 2, 3, 12, 0);
            var s = Fresh();
            var (janSave, _) = Season.AwardPoints(s, QuestsForTiers(3), Cfg, jan);
            Assert.Equal(3, janSave.Progress.Season.TiersPaid);

            // A read in February sees a fresh season (no carried points/tiers).
            var febSnap = Season.Snapshot(janSave, Cfg, feb);
            Assert.Equal("2024-02", febSnap.Id);
            Assert.Equal(0, febSnap.Points);
            Assert.Equal(0, febSnap.TiersReached);

            // Awarding in February starts the new season from scratch and re-pays tier 1.
            var (febSave, paid) = Season.AwardPoints(janSave, QuestsForTiers(1), Cfg, feb);
            Assert.Equal("2024-02", febSave.Progress.Season.Id);
            Assert.Equal(QuestsForTiers(1) * Cfg.Balance.SeasonPointsPerQuest, (int)febSave.Progress.Season.Points);
            Assert.Equal(1, febSave.Progress.Season.TiersPaid);
            Assert.Single(paid);
            Assert.Equal(1, paid[0].Tier);
        }

        // ============================ snapshot read model ============================

        [Fact]
        public void SnapshotReportsNextTierAndDaysLeft()
        {
            var s = Fresh();
            var (a, _) = Season.AwardPoints(s, QuestsForTiers(1), Cfg, Now);
            var snap = Season.Snapshot(a, Cfg, Now);
            Assert.Equal(1, snap.TiersPaid);
            Assert.Equal(1, snap.TiersReached);
            Assert.Equal(2L * Cfg.Balance.SeasonPointsPerTier, snap.NextTierAt); // tier 2 threshold

            // Days left ceils: from 2024-01-20 12:00 UTC to Feb 1 00:00 is 11.5 days ⇒ 12.
            Assert.Equal(12, Season.DaysLeftInMonth(Utc(2024, 1, 20, 12, 0)));
        }

        // ============================ attainability ============================

        [Fact]
        public void Tier30IsReachableInsideAMonthAtTheDerivedThroughput()
        {
            // 30 tiers × 40 pts ÷ 10 pts/quest = 120 quests to max. The quest board is 3 slots with
            // INSTANT refill (no timed reroll cadence — see Quests.Advance), so a daily player finishing
            // most of the board clears it ~1.5–2×/day ≈ 4.3–5.5 quests/day. That lands tier 30 in the
            // day 22–28 target window; SeasonTests pins the bounds so tuning can't drift the pass.
            long totalQuests = (long)Cfg.Balance.SeasonTierCount * Cfg.Balance.SeasonPointsPerTier / Cfg.Balance.SeasonPointsPerQuest;
            Assert.Equal(120, totalQuests);

            double daysSlow = totalQuests / 4.3; // ≈ 27.9 — the light daily player
            double daysFast = totalQuests / 5.5; // ≈ 21.8 — the diligent daily player
            Assert.True(daysSlow <= 28.0, $"tier 30 must land by day 28 at 4.3 quests/day (got {daysSlow:0.0})");
            Assert.True(daysFast >= 21.0, $"tier 30 shouldn't be trivial (got {daysFast:0.0} days at 5.5 quests/day)");
        }

        // ============================ threading sweep ============================

        [Fact]
        public void SeasonSurvivesEveryReducerThatCopiesProgress()
        {
            // A dropped Season copy site would substitute a fresh (empty) SeasonState, so a distinctive
            // marker must survive every ProgressState rebuilder (mirrors CodexSurvivesEveryReducer...).
            var s = Fresh();
            s.Progress.Season = new SeasonState { Id = "2024-01", Points = 77, TiersPaid = 1 };
            long Marker(SaveState x) => x.Progress.Season.Points;

            Assert.Equal(77, Marker(Progression.SetStage(s, 1, Cfg)));
            Assert.Equal(77, Marker(Progression.OnStageCleared(s, 3, Cfg)));
            Assert.Equal(77, Marker(Tower.RecordClear(s, 1, Cfg)));
            Assert.Equal(77, Marker(Crypt.RecordFloorClear(s, 1, Cfg)));
            Assert.Equal(77, Marker(Inventory.SetImprintGuard(s, true)));
            Assert.Equal(77, Marker(Achievements.Record(s, AchievementMetric.MonstersKilled, 5, Cfg).save));
            Assert.Equal(77, Marker(DailyLogin.Claim(s, Cfg, Now).save));
            Assert.Equal(77, Marker(Codex.BankKills(s, new System.Collections.Generic.Dictionary<string, int> { ["slime"] = 1 }, Cfg).save));
        }

        // ============================ migrate ============================

        [Fact]
        public void MigrateBackfillsANullSeason()
        {
            var s = Fresh();
            s.Progress.Season = null!; // simulate a pre-10.16 payload
            var migrated = Save.Migrate(s);
            Assert.NotNull(migrated.Progress.Season);
            Assert.Equal("", migrated.Progress.Season.Id);
        }
    }
}
