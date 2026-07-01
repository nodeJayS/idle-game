using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class DailyLoginTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();
        private const string Gems = "gems";

        // A timestamp partway (1h) into UTC day-index `day`.
        private static long At(long day) => day * DailyLogin.DayMs + 3_600_000L;

        private static long GemsOf(SaveState s) => s.Currencies.GetValueOrDefault(Gems);

        [Fact]
        public void NewGameHasNoStreakAndCanClaim()
        {
            var save = Save.NewGame(1, Cfg, At(20000));
            Assert.Equal(0, save.Progress.Daily.LastClaimDay);
            Assert.Equal(0, save.Progress.Daily.Streak);
            Assert.Equal(0, save.Progress.Daily.TotalClaims);
            Assert.True(DailyLogin.CanClaim(save, At(20000)));
            Assert.Equal(0, GemsOf(save));
        }

        [Fact]
        public void FirstClaimGrantsBaseGemsAndStartsStreakAtOne()
        {
            var save = Save.NewGame(1, Cfg, At(20000));
            var (next, gems, streak, claimed) = DailyLogin.Claim(save, Cfg, At(20000));

            Assert.True(claimed);
            Assert.Equal(1, streak);
            Assert.Equal(Cfg.Balance.DailyLoginBaseGems, gems);
            Assert.Equal(Cfg.Balance.DailyLoginBaseGems, GemsOf(next));
            Assert.Equal(20000, next.Progress.Daily.LastClaimDay);
            Assert.Equal(1, next.Progress.Daily.TotalClaims);
            Assert.False(DailyLogin.CanClaim(next, At(20000))); // same day → done
            Assert.Equal(0, GemsOf(save)); // input untouched (pure)
        }

        [Fact]
        public void SecondClaimSameDayIsANoOp()
        {
            var save = Save.NewGame(1, Cfg, At(20000));
            var (a, _, _, _) = DailyLogin.Claim(save, Cfg, At(20000));
            var (b, gems, streak, claimed) = DailyLogin.Claim(a, Cfg, At(20000));

            Assert.False(claimed);
            Assert.Equal(0, gems);
            Assert.Equal(1, streak);
            Assert.Same(a, b); // shares the ref, no double-credit
            Assert.Equal(Cfg.Balance.DailyLoginBaseGems, GemsOf(b));
        }

        [Fact]
        public void ConsecutiveDaysBuildTheStreak()
        {
            var save = Save.NewGame(1, Cfg, At(20000));
            var (d1, _, s1, _) = DailyLogin.Claim(save, Cfg, At(20000));
            var (d2, g2, s2, _) = DailyLogin.Claim(d1, Cfg, At(20001));
            var (d3, _, s3, _) = DailyLogin.Claim(d2, Cfg, At(20002));

            Assert.Equal(1, s1);
            Assert.Equal(2, s2);
            Assert.Equal(3, s3);
            Assert.Equal(DailyLogin.RewardFor(2, Cfg), g2);
            Assert.Equal(3, d3.Progress.Daily.TotalClaims);
        }

        [Fact]
        public void MissingADayResetsTheStreak()
        {
            var save = Save.NewGame(1, Cfg, At(20000));
            var (d1, _, _, _) = DailyLogin.Claim(save, Cfg, At(20000));
            var (d2, _, s2, _) = DailyLogin.Claim(d1, Cfg, At(20001)); // streak 2
            var (d3, _, s3, _) = DailyLogin.Claim(d2, Cfg, At(20005)); // skipped days → reset

            Assert.Equal(2, s2);
            Assert.Equal(1, s3);
        }

        [Fact]
        public void RolledBackClockCannotClaim()
        {
            var save = Save.NewGame(1, Cfg, At(20005));
            var (d1, _, _, _) = DailyLogin.Claim(save, Cfg, At(20005));
            Assert.False(DailyLogin.CanClaim(d1, At(20003)));           // earlier day
            var (d2, gems, _, claimed) = DailyLogin.Claim(d1, Cfg, At(20003));
            Assert.False(claimed);
            Assert.Equal(0, gems);
            Assert.Same(d1, d2);
        }

        [Fact]
        public void StreakBonusPlateausAtTheCap()
        {
            var cap = Cfg.Balance.DailyLoginStreakCap;
            long atCap = DailyLogin.RewardFor(cap, Cfg) - Cfg.Balance.DailyLoginMilestoneGems; // strip a possible milestone at the cap
            long pastCap = DailyLogin.RewardFor(cap + 1, Cfg); // cap+1 is not a milestone by default (8 % 7 != 0)
            long expectedPlateau = Cfg.Balance.DailyLoginBaseGems + Cfg.Balance.DailyLoginStreakBonus * (cap - 1);
            Assert.Equal(expectedPlateau, atCap);
            Assert.Equal(expectedPlateau, pastCap); // no further growth beyond the cap
        }

        [Fact]
        public void MilestoneDayPaysABonusOnTopOfTheStreakValue()
        {
            var b = Cfg.Balance;
            int every = b.DailyLoginMilestoneEvery;
            int capped = System.Math.Min(every, b.DailyLoginStreakCap);
            long streakValue = b.DailyLoginBaseGems + b.DailyLoginStreakBonus * (capped - 1);
            Assert.Equal(streakValue + b.DailyLoginMilestoneGems, DailyLogin.RewardFor(every, Cfg));
        }

        [Fact]
        public void ClaimTouchesOnlyGemsNotGoldOrScrap()
        {
            var save = Save.NewGame(1, Cfg, At(20000));
            save.Currencies["gold"] = 500;
            save.Currencies["scrap"] = 40;
            var (next, _, _, _) = DailyLogin.Claim(save, Cfg, At(20000));
            Assert.Equal(500, next.Currencies.GetValueOrDefault("gold"));
            Assert.Equal(40, next.Currencies.GetValueOrDefault("scrap"));
            Assert.True(GemsOf(next) > 0);
        }

        [Fact]
        public void PreviewMatchesClaimGrantAcrossStates()
        {
            var save = Save.NewGame(1, Cfg, At(20000));

            // Fresh save, consecutive day, a milestone day (7), and a streak reset after a gap —
            // in every state the preview must equal exactly what Claim then grants.
            long[] days = { 20000, 20001, 20002, 20003, 20004, 20005, 20006, 20010 };
            foreach (var day in days)
            {
                var (pGems, pStreak, canClaim) = DailyLogin.Preview(save, Cfg, At(day));
                var (next, gems, streak, claimed) = DailyLogin.Claim(save, Cfg, At(day));

                Assert.True(canClaim);
                Assert.True(claimed);
                Assert.Equal(pGems, gems);
                Assert.Equal(pStreak, streak);
                Assert.True(gems > 0); // a real claim is never worth 0 (base >= 10)
                save = next;
            }
        }

        [Fact]
        public void PreviewIsZeroGemsWhenAlreadyClaimedToday()
        {
            var save = Save.NewGame(1, Cfg, At(20000));
            var (claimedSave, _, _, _) = DailyLogin.Claim(save, Cfg, At(20000));

            // Same UTC day again: nothing to claim — preview must say 0/no-claim, and Claim must
            // grant exactly that 0 (shown amount == granted amount even in the ineligible state).
            var (pGems, pStreak, canClaim) = DailyLogin.Preview(claimedSave, Cfg, At(20000));
            var (_, gems, streak, claimed) = DailyLogin.Claim(claimedSave, Cfg, At(20000));

            Assert.False(canClaim);
            Assert.False(claimed);
            Assert.Equal(0, pGems);
            Assert.Equal(pGems, gems);
            Assert.Equal(pStreak, streak);
        }

        [Fact]
        public void PreviewIsZeroGemsOnRolledBackClock()
        {
            var save = Save.NewGame(1, Cfg, At(20005));
            var (claimedSave, _, _, _) = DailyLogin.Claim(save, Cfg, At(20005));

            var (pGems, _, canClaim) = DailyLogin.Preview(claimedSave, Cfg, At(20003));
            var (_, gems, _, claimed) = DailyLogin.Claim(claimedSave, Cfg, At(20003));

            Assert.False(canClaim);
            Assert.False(claimed);
            Assert.Equal(0, pGems);
            Assert.Equal(pGems, gems);
        }

        [Fact]
        public void MigrateBackfillsDailyStateOnOlderSaves()
        {
            var save = Save.NewGame(1, Cfg, At(20000));
            save.Progress.Daily = null!; // simulate a pre-daily-login payload
            var migrated = Save.Migrate(save);
            Assert.NotNull(migrated.Progress.Daily);
            Assert.Equal(0, migrated.Progress.Daily.Streak);
        }
    }
}
