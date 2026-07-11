#nullable enable
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// The Goals hub's sim-side read model (game-design §7.5): ONE list of everything the player
    /// can manually claim, across every retention system. Quests and achievements deliberately
    /// AUTO-PAY (no claim chores in an idle game), so today the list holds at most the daily
    /// login — but the client renders whatever appears here, so a future manual-claim system
    /// (events, mail, …) joins by adding entries, not UI. Pure like every reducer; amounts come
    /// from the owning system's EXISTING preview/claim reducers (never recomputed here), so the
    /// shown reward can't diverge from the grant.
    /// </summary>
    public static class Goals
    {
        /// <summary>Kind tag for the daily-login claimable (the only manual claim today).</summary>
        public const string KindDailyLogin = "daily_login";

        /// <summary>Everything the player could manually claim right now (possibly empty). The
        /// control-bar pip and the hub's Claim-all button key off exactly this list.</summary>
        public static List<GoalClaim> Claimables(SaveState save, GameConfig cfg, long nowMs)
        {
            var list = new List<GoalClaim>();
            var (gems, streak, canClaim) = DailyLogin.Preview(save, cfg, nowMs);
            if (canClaim) list.Add(DailyClaim(streak, gems));
            return list;
        }

        /// <summary>Apply every claimable through its EXISTING reducer (today: DailyLogin.Claim)
        /// and report what was claimed for the client's feed. Shares the ref and returns an empty
        /// list when nothing is claimable. Pure.</summary>
        public static (SaveState save, List<GoalClaim> claimed) ClaimAll(SaveState save, GameConfig cfg, long nowMs)
        {
            var claimed = new List<GoalClaim>();
            var (next, gems, streak, ok) = DailyLogin.Claim(save, cfg, nowMs);
            if (ok)
            {
                save = next;
                claimed.Add(DailyClaim(streak, gems));
            }
            return (save, claimed);
        }

        // One label builder shared by the pending and the just-claimed shapes, so the feed line
        // always matches what the list promised.
        private static GoalClaim DailyClaim(int streak, long gems) =>
            new GoalClaim { Kind = KindDailyLogin, Label = $"Daily login — day {streak}", Gems = gems };
    }

    /// <summary>One pending (or just-applied) manual claim in the Goals hub: a kind tag for logic,
    /// a display label, and the gem amount — sourced from the owning system's preview so display
    /// and grant are the same number by construction.</summary>
    public struct GoalClaim
    {
        public string Kind;
        public string Label;
        public long Gems;
    }
}
