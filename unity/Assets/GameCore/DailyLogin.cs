#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Daily-login streak (Lever 4 — the premium-currency hook). Once per UTC calendar day the player
    /// can <see cref="Claim"/> a reward of GEMS (the premium currency, held in
    /// <see cref="SaveState.Currencies"/> under <see cref="BalanceConstants.PremiumCurrency"/> and, for
    /// now, earnable no other way — the seed of the future gacha/microtransaction economy). Consecutive
    /// days build a <see cref="DailyLoginState.Streak"/> for bigger rewards; a missed day resets it.
    /// Pure reducers — the client passes <c>now</c> (epoch ms), never the wall clock, keeping this
    /// deterministic/testable like the rest of GameCore. State lives under <see cref="ProgressState"/>.
    /// </summary>
    public static class DailyLogin
    {
        public const long DayMs = 86_400_000L; // 24h; UTC day boundaries (a later slice can add a tz offset)

        /// <summary>The UTC day-index for an epoch-ms timestamp (days since the epoch).</summary>
        public static long DayIndex(long nowMs) => nowMs / DayMs;

        /// <summary>True if a claim is available: it's a later day than the last claim (also true when
        /// never claimed, since LastClaimDay = 0). A rolled-back clock (dayNow ≤ last) can't claim.</summary>
        public static bool CanClaim(SaveState save, long nowMs) =>
            DayIndex(nowMs) > save.Progress.Daily.LastClaimDay;

        /// <summary>The streak a claim right now would produce: 1 on the first claim or after a missed
        /// day, else the current streak + 1 for a consecutive day. (Meaningful only when CanClaim.)</summary>
        public static int StreakIfClaimed(SaveState save, long nowMs)
        {
            var d = save.Progress.Daily;
            if (d.LastClaimDay == 0) return 1;                 // never claimed
            if (DayIndex(nowMs) == d.LastClaimDay + 1) return d.Streak + 1; // consecutive day
            return 1;                                          // missed one or more days
        }

        /// <summary>The gem reward for a given streak length (pure — used for both the payout and the
        /// client's "tomorrow you'll get…" preview).</summary>
        public static long RewardFor(int streak, GameConfig cfg)
        {
            var b = cfg.Balance;
            int capped = Math.Min(Math.Max(1, streak), Math.Max(1, b.DailyLoginStreakCap));
            long gems = b.DailyLoginBaseGems + b.DailyLoginStreakBonus * (capped - 1);
            if (b.DailyLoginMilestoneEvery > 0 && streak % b.DailyLoginMilestoneEvery == 0)
                gems += b.DailyLoginMilestoneGems;
            return Math.Max(0, gems);
        }

        /// <summary>
        /// Dry-run of <see cref="Claim"/>: the exact gems/streak a claim at <paramref name="nowMs"/>
        /// would grant, or (0, current streak, false) when no claim is available (already claimed this
        /// UTC day, or a rolled-back clock). The client's reward preview MUST use this — never
        /// recompute the reward independently — so the shown amount can't diverge from the grant.
        /// </summary>
        public static (long gems, int streak, bool canClaim) Preview(SaveState save, GameConfig cfg, long nowMs)
        {
            if (!CanClaim(save, nowMs)) return (0, save.Progress.Daily.Streak, false);
            int streak = StreakIfClaimed(save, nowMs);
            return (RewardFor(streak, cfg), streak, true);
        }

        /// <summary>
        /// Claim today's daily reward: advance the streak, credit gems, and stamp the claim day. No-op
        /// (shares the ref, <c>claimed = false</c>) if a claim isn't available yet. Grants exactly what
        /// <see cref="Preview"/> reported for the same save/now. Returns the updated save plus the gems
        /// granted, the new streak, and whether a claim actually happened. Pure.
        /// </summary>
        public static (SaveState save, long gems, int streak, bool claimed) Claim(SaveState save, GameConfig cfg, long nowMs)
        {
            var (gems, streak, canClaim) = Preview(save, cfg, nowMs);
            if (!canClaim) return (save, 0, streak, false);

            var daily = new DailyLoginState
            {
                LastClaimDay = DayIndex(nowMs),
                Streak = streak,
                TotalClaims = save.Progress.Daily.TotalClaims + 1,
            };
            return (Apply(save, cfg.Balance.PremiumCurrency, gems, daily), gems, streak, true);
        }

        // Clone the save crediting `gems` to the premium currency AND swapping in the new daily state
        // (nested under a cloned ProgressState whose siblings share refs). Everything else shares refs.
        private static SaveState Apply(SaveState save, string currencyKey, long gems, DailyLoginState daily)
        {
            var currencies = new Dictionary<string, long>(save.Currencies);
            if (gems != 0) currencies[currencyKey] = (currencies.TryGetValue(currencyKey, out var v) ? v : 0) + gems;

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = currencies,
                Progress = new ProgressState
                {
                    HighestStage = save.Progress.HighestStage,
                    CurrentStage = save.Progress.CurrentStage,
                    AccountLevel = save.Progress.AccountLevel,
                    Tower = save.Progress.Tower,
                    Achievements = save.Progress.Achievements,
                    Daily = daily,
                    Crypt = save.Progress.Crypt,
                    Intro = save.Progress.Intro,
                },
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }
    }
}
