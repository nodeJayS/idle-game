using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Goals hub read model (§7.5): the ONE list of manual claims (today: daily login),
    /// ClaimAll routing through the existing reducers, and the next-day login preview. The core
    /// invariant everywhere: shown amounts come from the owning system's preview, so list, feed,
    /// and grant can never disagree.</summary>
    public class GoalsTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();
        private const long Day = DailyLogin.DayMs;
        private const long Now = 5 * Day + 12_345; // mid-day, UTC day-index 5

        private static SaveState Fresh() => Save.NewGame(777u, Cfg, 0L);

        private static long Gems(SaveState s) =>
            s.Currencies.TryGetValue(Cfg.Balance.PremiumCurrency, out var g) ? g : 0;

        // ---- Claimables mirrors CanClaim ----

        [Fact]
        public void Claimable_PresentIffCanClaim()
        {
            var save = Fresh();

            // Day 0 (epoch day): DayIndex(0) == LastClaimDay == 0, so no claim — and no claimable.
            Assert.False(DailyLogin.CanClaim(save, 0L));
            Assert.Empty(Goals.Claimables(save, Cfg, 0L));

            // A later day: claimable appears, tagged as the daily login.
            Assert.True(DailyLogin.CanClaim(save, Now));
            var c = Assert.Single(Goals.Claimables(save, Cfg, Now));
            Assert.Equal(Goals.KindDailyLogin, c.Kind);

            // Claimed: the list empties for the rest of the day.
            var (after, _) = Goals.ClaimAll(save, Cfg, Now);
            Assert.False(DailyLogin.CanClaim(after, Now));
            Assert.Empty(Goals.Claimables(after, Cfg, Now));
        }

        [Fact]
        public void Claimable_GemsAndDay_ComeFromPreview()
        {
            var save = Fresh();
            var (gems, streak, canClaim) = DailyLogin.Preview(save, Cfg, Now);
            Assert.True(canClaim);

            var c = Assert.Single(Goals.Claimables(save, Cfg, Now));
            Assert.Equal(gems, c.Gems); // EXACTLY the preview — never recomputed
            Assert.Contains($"day {streak}", c.Label);
        }

        // ---- ClaimAll applies via the existing reducer ----

        [Fact]
        public void ClaimAll_MarksClaimed_CreditsGems_ReportsTheClaim()
        {
            var save = Fresh();
            long before = Gems(save);
            var promised = Assert.Single(Goals.Claimables(save, Cfg, Now));

            var (after, claimed) = Goals.ClaimAll(save, Cfg, Now);

            Assert.Equal(DailyLogin.DayIndex(Now), after.Progress.Daily.LastClaimDay);
            Assert.Equal(before + promised.Gems, Gems(after));

            var paid = Assert.Single(claimed);
            Assert.Equal(promised.Kind, paid.Kind);
            Assert.Equal(promised.Gems, paid.Gems);
            Assert.Equal(promised.Label, paid.Label); // the feed line matches what the list promised
        }

        [Fact]
        public void ClaimAll_SecondSameDay_SharesRef_ClaimsNothing()
        {
            var (once, _) = Goals.ClaimAll(Fresh(), Cfg, Now);
            var (twice, claimed) = Goals.ClaimAll(once, Cfg, Now + 1_000); // later the same UTC day
            Assert.Same(once, twice);
            Assert.Empty(claimed);
        }

        [Fact]
        public void ClockRollback_NothingClaimable_ClaimAllNoOps()
        {
            var (save, _) = Goals.ClaimAll(Fresh(), Cfg, Now);
            Assert.Empty(Goals.Claimables(save, Cfg, Now - Day));      // clock rolled back a day
            Assert.Empty(Goals.Claimables(save, Cfg, Now - 2 * Day));

            var (same, claimed) = Goals.ClaimAll(save, Cfg, Now - Day);
            Assert.Same(save, same);
            Assert.Empty(claimed);
        }

        // ---- PreviewNext: tomorrow's claim, derived from the same code path as the real one ----

        [Fact]
        public void PreviewNext_MatchesActualClaimTomorrow_AfterTodayClaimed()
        {
            var (save, _) = Goals.ClaimAll(Fresh(), Cfg, Now);
            var (pGems, pStreak) = DailyLogin.PreviewNext(save, Cfg, Now);

            // The strongest assertion: actually claim tomorrow and compare the grant.
            var (_, gems, streak, ok) = DailyLogin.Claim(save, Cfg, Now + Day);
            Assert.True(ok);
            Assert.Equal(gems, pGems);
            Assert.Equal(streak, pStreak);
            Assert.Equal(save.Progress.Daily.Streak + 1, pStreak); // consecutive day continues the streak
        }

        [Fact]
        public void PreviewNext_MatchesActualClaimTomorrow_WhenTodayNeverClaimed()
        {
            // Today's claim untouched: tomorrow's claim is still a first claim (streak 1) —
            // skipping today doesn't bank it.
            var save = Fresh();
            var (pGems, pStreak) = DailyLogin.PreviewNext(save, Cfg, Now);
            var (_, gems, streak, ok) = DailyLogin.Claim(save, Cfg, Now + Day);
            Assert.True(ok);
            Assert.Equal(gems, pGems);
            Assert.Equal(streak, pStreak);
            Assert.Equal(1, pStreak);
        }

        [Fact]
        public void PreviewNext_BreaksStreak_WhenTodayIsSkipped()
        {
            // Claimed yesterday on a 4-day streak; today (Now) stays unclaimed. A claim tomorrow
            // lands two days after the last claim — the streak resets to 1, exactly as DayIndex
            // math dictates.
            var save = Fresh();
            save.Progress.Daily.LastClaimDay = DailyLogin.DayIndex(Now) - 1;
            save.Progress.Daily.Streak = 4;

            var (pGems, pStreak) = DailyLogin.PreviewNext(save, Cfg, Now);
            Assert.Equal(1, pStreak);

            var (_, gems, streak, ok) = DailyLogin.Claim(save, Cfg, Now + Day);
            Assert.True(ok);
            Assert.Equal(gems, pGems);
            Assert.Equal(streak, pStreak);
        }

        [Fact]
        public void PreviewNext_ContinuesStreak_WhenTodayIsClaimed()
        {
            // Mid-streak: claimed today (day 5) on a 3-day streak ⇒ tomorrow previews day 4.
            var save = Fresh();
            save.Progress.Daily.LastClaimDay = DailyLogin.DayIndex(Now);
            save.Progress.Daily.Streak = 3;

            var (pGems, pStreak) = DailyLogin.PreviewNext(save, Cfg, Now);
            Assert.Equal(4, pStreak);
            Assert.Equal(DailyLogin.RewardFor(4, Cfg), pGems); // same table the real claim uses
        }

        [Fact]
        public void PreviewNext_RolledBackClock_PreviewsNothing()
        {
            // LastClaimDay stamped beyond even tomorrow (clock rolled back ≥2 days): tomorrow's
            // claim is impossible, so the preview reports 0 rather than a phantom reward.
            var save = Fresh();
            save.Progress.Daily.LastClaimDay = DailyLogin.DayIndex(Now) + 2;
            save.Progress.Daily.Streak = 6;

            var (gems, streak) = DailyLogin.PreviewNext(save, Cfg, Now);
            Assert.Equal(0, gems);
            Assert.Equal(6, streak); // Preview's no-claim shape: the current streak, unchanged
        }
    }
}
