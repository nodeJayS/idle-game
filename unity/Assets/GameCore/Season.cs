#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Season track (10.16, mobile arc MM4) — a calendar-MONTH battle pass (user verdict 2026-07-15).
    /// Points come from COMPLETED QUESTS only; ~<see cref="BalanceConstants.SeasonTierCount"/> free tiers
    /// pay out AUTOMATICALLY as the player crosses them (the <see cref="Achievements"/> no-Claimables
    /// philosophy — no manual claim, no <c>Goals.Claimables</c> entry).
    ///
    ///  - IDENTITY: a season is "yyyy-MM" in UTC (<see cref="CurrentId"/>). ROLLOVER is lazy: any read
    ///    or reducer that meets a stored id different from the current month treats the stored progress
    ///    as fresh (points 0, tiers 0) — a lapsed month simply STARTS OVER with no back-pay. The reset is
    ///    stamped the next time <see cref="AwardPoints"/> writes.
    ///  - POINTS: <see cref="AwardPoints"/> is the ONE seam. The client calls it right after
    ///    <see cref="Quests.Advance"/> with that call's completed-quest COUNT. Quests.Advance is the only
    ///    reducer that completes a quest (it returns the exact completed list), so awarding its count once
    ///    per call is EXACTLY-ONCE per completion — from every caller, present and future, without Season
    ///    needing to reach into Quests or Quests needing a clock. (There is no idle/offline quest path
    ///    today: the sole client choke point is CombatView.AdvanceQuest; if an offline path is ever added
    ///    it routes through the same Quests.Advance and the same AwardPoints call, so exactly-once holds.)
    ///  - TIERS: linear — tier N completes at N × <see cref="BalanceConstants.SeasonPointsPerTier"/>.
    ///    Rewards are a generated ladder (<see cref="Ladder"/>, content-as-data like the set tables):
    ///    every <see cref="BalanceConstants.SeasonMilestoneEvery"/>th tier pays gems, the rest pay gold
    ///    scaled to a CONTEMPORARY quest's reward at the player's stage.
    ///
    /// Pure reducers: new SaveState, untouched siblings shared by ref; a genuine no-op shares the whole
    /// save. Gold credited via <see cref="Progression.GrantGold"/>, gems via the premium-currency pattern
    /// (the <see cref="Tower"/>.WithFloor / <see cref="DailyLogin"/>.Apply precedent). Clock is <c>nowMs</c>
    /// (epoch ms), never the wall clock.
    /// </summary>
    public static class Season
    {
        // ---- identity + rollover ---------------------------------------------

        /// <summary>The season id for a timestamp: "yyyy-MM" in UTC, invariant culture (so a device's
        /// locale can't shift the month string).</summary>
        public static string CurrentId(long nowMs) =>
            DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        /// <summary>The EFFECTIVE season progress for <paramref name="nowMs"/>: the stored state when it
        /// matches the current month, else a fresh (0, 0) — the lazy rollover (no back-pay for a lapsed
        /// month). Both <see cref="AwardPoints"/> and <see cref="Snapshot"/> read through this so a stale
        /// stored month never leaks last month's points into this one.</summary>
        private static (string id, long points, int tiersPaid) Effective(SaveState save, long nowMs)
        {
            var s = save.Progress.Season;
            string cur = CurrentId(nowMs);
            return s.Id == cur ? (cur, s.Points, s.TiersPaid) : (cur, 0L, 0);
        }

        // ---- tier math + ladder ----------------------------------------------

        /// <summary>Tiers reached at <paramref name="points"/>: floor(points / PointsPerTier), capped at
        /// <see cref="BalanceConstants.SeasonTierCount"/> (the capstone).</summary>
        public static int TiersReached(long points, GameConfig cfg)
        {
            int per = Math.Max(1, cfg.Balance.SeasonPointsPerTier);
            int count = Math.Max(0, cfg.Balance.SeasonTierCount);
            long reached = Math.Max(0, points) / per;
            return (int)Math.Min(count, reached);
        }

        /// <summary>The reward for tier <paramref name="tier"/> (1-based): every
        /// <see cref="BalanceConstants.SeasonMilestoneEvery"/>th tier pays gems; the rest pay gold =
        /// <see cref="BalanceConstants.SeasonTierGoldMult"/> × a contemporary quest's gold reward at
        /// <paramref name="stage"/> (the Quests.NextQuest formula — ~45s of stage income). Gold FLOORS
        /// (the balances-floor display rule). <paramref name="stage"/> is the player's current stage, so
        /// the payout stays meaningful as the run grows (and so does the ladder preview).</summary>
        public static SeasonTierReward TierReward(int tier, GameConfig cfg, int stage)
        {
            var b = cfg.Balance;
            bool milestone = b.SeasonMilestoneEvery > 0 && tier % b.SeasonMilestoneEvery == 0;
            if (milestone)
                return new SeasonTierReward { Tier = tier, Gems = Math.Max(0, b.SeasonGemsPerMilestone), IsMilestone = true };

            long questGold = Math.Max(10, b.GoldPerSec(Math.Max(1, stage)) * 45); // == Quests.NextQuest's rewardGold
            long gold = (long)Math.Floor(questGold * b.SeasonTierGoldMult);
            return new SeasonTierReward { Tier = tier, Gold = gold };
        }

        /// <summary>The whole season reward ladder (tiers 1..SeasonTierCount) generated at
        /// <paramref name="stage"/> — the client's season tab renders this. Content-as-data: no stored
        /// ladder, just the generator over the config knobs.</summary>
        public static List<SeasonTierReward> Ladder(GameConfig cfg, int stage)
        {
            int count = Math.Max(0, cfg.Balance.SeasonTierCount);
            var list = new List<SeasonTierReward>(count);
            for (int t = 1; t <= count; t++) list.Add(TierReward(t, cfg, stage));
            return list;
        }

        // ---- the one reducer -------------------------------------------------

        /// <summary>
        /// Award season points for <paramref name="completedQuests"/> quests finished in one
        /// <see cref="Quests.Advance"/> call, crossing (and AUTO-PAYING) any tiers that clears. Applies
        /// month ROLLOVER first — a stored id from a past month resets to (current, 0, 0) before adding —
        /// then credits <c>completedQuests × SeasonPointsPerQuest</c>, pays each newly-crossed tier's
        /// reward (gold via <see cref="Progression.GrantGold"/>, gems via the premium currency), and
        /// writes the advanced state. Returns the updated save plus the tiers paid THIS call (for the
        /// client feed). Pure; a no-op in the same month with nothing to add shares the save ref.
        /// </summary>
        public static (SaveState save, List<SeasonTierReward> paid) AwardPoints(
            SaveState save, int completedQuests, GameConfig cfg, long nowMs)
        {
            var paid = new List<SeasonTierReward>();
            var (cur, basePoints, baseTiersPaid) = Effective(save, nowMs);
            bool sameMonth = save.Progress.Season.Id == cur;

            long add = Math.Max(0, (long)completedQuests) * Math.Max(0, cfg.Balance.SeasonPointsPerQuest);
            // Same month + nothing to add ⇒ genuinely nothing changes: share the ref. (A DIFFERENT month
            // with add==0 still falls through to stamp the reset, so a lapsed season doesn't linger.)
            if (add == 0 && sameMonth) return (save, paid);

            long points = basePoints + add;
            int reached = TiersReached(points, cfg);

            long goldTotal = 0, gemsTotal = 0;
            int stage = Math.Max(1, save.Progress.HighestStage);
            for (int t = baseTiersPaid + 1; t <= reached; t++)
            {
                var r = TierReward(t, cfg, stage);
                paid.Add(r);
                goldTotal += r.Gold;
                gemsTotal += r.Gems;
            }

            var withGold = goldTotal > 0 ? Progression.GrantGold(save, goldTotal) : save;
            var next = WithSeason(withGold, new SeasonState { Id = cur, Points = points, TiersPaid = reached }, cfg, gemsTotal);
            return (next, paid);
        }

        // ---- read model -------------------------------------------------------

        /// <summary>The client's season-tab snapshot at <paramref name="nowMs"/> (rollover-aware): the
        /// current season id, points, tiers paid/reached, the points at which the next tier unlocks
        /// (0 when all tiers are reached), whole days left in the month (CEIL — the countdown rule), and
        /// the full reward ladder at the player's current stage.</summary>
        public static SeasonSnapshot Snapshot(SaveState save, GameConfig cfg, long nowMs)
        {
            var (cur, points, tiersPaid) = Effective(save, nowMs);
            int reached = TiersReached(points, cfg);
            int per = Math.Max(1, cfg.Balance.SeasonPointsPerTier);
            int count = Math.Max(0, cfg.Balance.SeasonTierCount);
            long nextTierAt = reached >= count ? 0 : (long)(reached + 1) * per;

            return new SeasonSnapshot
            {
                Id = cur,
                Points = points,
                TiersPaid = tiersPaid,
                TiersReached = reached,
                NextTierAt = nextTierAt,
                DaysLeft = DaysLeftInMonth(nowMs),
                Ladder = Ladder(cfg, Math.Max(1, save.Progress.HighestStage)),
            };
        }

        /// <summary>Whole UTC days remaining until the month ends (start of next month), CEILED so a
        /// partial final day still shows as one day left — matching the countdown display rule.</summary>
        public static int DaysLeftInMonth(long nowMs)
        {
            var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;
            var firstOfNextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            long endMs = new DateTimeOffset(firstOfNextMonth, TimeSpan.Zero).ToUnixTimeMilliseconds();
            long remMs = Math.Max(0, endMs - nowMs);
            return (int)((remMs + DailyLogin.DayMs - 1) / DailyLogin.DayMs); // ceil
        }

        // ---- copy helper (Season lives under ProgressState) -------------------

        /// <summary>Clone the save with a new SeasonState (and optionally credit gems to the premium
        /// currency), nested under a cloned ProgressState whose siblings share refs — the DailyLogin.Apply
        /// / Tower.WithFloor pattern. Everything else shares refs (pure-reducer convention).</summary>
        private static SaveState WithSeason(SaveState save, SeasonState season, GameConfig cfg, long gems = 0)
        {
            var currencies = save.Currencies;
            if (gems != 0)
            {
                currencies = new Dictionary<string, long>(save.Currencies);
                string key = cfg.Balance.PremiumCurrency;
                currencies[key] = (currencies.TryGetValue(key, out var v) ? v : 0) + gems;
            }
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
                    EndlessBest = save.Progress.EndlessBest,
                    Tower = save.Progress.Tower,
                    Achievements = save.Progress.Achievements,
                    Daily = save.Progress.Daily,
                    Crypt = save.Progress.Crypt,
                    Intro = save.Progress.Intro,
                    Loot = save.Progress.Loot,
                    Codex = save.Progress.Codex,
                    Season = season,
                },
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }
    }

    /// <summary>One season tier's reward (<see cref="Season.TierReward"/> / <see cref="Season.Ladder"/>);
    /// also the "tier paid" record <see cref="Season.AwardPoints"/> returns for the client feed. A tier
    /// pays EITHER gold OR gems (never both): <see cref="IsMilestone"/> marks the gem tiers.</summary>
    public struct SeasonTierReward
    {
        public int Tier;        // 1-based tier number
        public long Gold;       // gold payout (0 on a milestone/gem tier)
        public long Gems;       // gem payout (0 on a gold tier)
        public bool IsMilestone; // true on the every-5th gem tiers
    }

    /// <summary>The season-tab read model (<see cref="Season.Snapshot"/>).</summary>
    public struct SeasonSnapshot
    {
        public string Id;                       // "yyyy-MM" (UTC) — the current month
        public long Points;                     // points this season (0 after a lazy rollover)
        public int TiersPaid;                   // tiers already auto-paid
        public int TiersReached;                // tiers unlocked by current points (== TiersPaid in steady state)
        public long NextTierAt;                 // points needed for the next tier (0 when all reached)
        public int DaysLeft;                    // whole UTC days left in the month (ceil)
        public List<SeasonTierReward> Ladder;   // the full reward ladder at the player's stage
    }
}
