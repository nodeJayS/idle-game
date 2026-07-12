#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Lifetime achievement ladder (Lever 4 — the permanent "reason to keep going", distinct from the
    /// rolling goal board in <see cref="Quests"/>). The client feeds game events (the same ones the
    /// goal board taps) into <see cref="Record"/>; each metric accumulates (ADD) or tracks a peak
    /// (MAX), and any achievement TIER newly crossed pays a one-time reward (gold + scrap + party XP)
    /// through the existing <see cref="Progression"/> reducers. Pure (returns a new SaveState, input
    /// untouched), mirroring Quests/Modifiers. State lives under <see cref="ProgressState"/>.
    /// </summary>
    public static class Achievements
    {
        /// <summary>MAX-metrics keep the highest value ever seen; every other metric accumulates.</summary>
        public static bool IsMaxMetric(AchievementMetric m) =>
            m == AchievementMetric.HighestStage
            || m == AchievementMetric.HighestTowerFloor
            || m == AchievementMetric.HeroLevel;

        /// <summary>Current lifetime value of a metric (0 if never recorded).</summary>
        public static long Counter(SaveState save, AchievementMetric m) =>
            save.Progress.Achievements.Counters.TryGetValue(m, out var v) ? v : 0;

        /// <summary>How many tiers of <paramref name="def"/> have already been claimed (paid out).
        /// 0 = none yet; equal to Tiers.Count = fully complete. Also the index of the NEXT tier.</summary>
        public static int TiersClaimed(SaveState save, AchievementDef def) =>
            save.Progress.Achievements.Claimed.TryGetValue(def.Id, out var c) ? c : 0;

        /// <summary>
        /// Record a metric event: ADD-metrics add <paramref name="value"/>, MAX-metrics keep the peak.
        /// Any achievement tier the new value now satisfies pays out (gold + scrap + party XP) and is
        /// marked claimed so it never mints twice. Returns the updated save plus the tiers completed
        /// this call (for the client feed / juice). No-op (shares the ref) when the metric doesn't
        /// move — a non-positive ADD or a non-improving MAX. Pure.
        /// </summary>
        public static (SaveState save, List<AchievementUnlock> completed) Record(
            SaveState save, AchievementMetric metric, long value, GameConfig cfg)
        {
            var completed = new List<AchievementUnlock>();

            long cur = Counter(save, metric);
            long next = IsMaxMetric(metric) ? Math.Max(cur, value) : cur + Math.Max(0, value);
            if (next == cur) return (save, completed); // metric didn't move — nothing to do

            var counters = new Dictionary<AchievementMetric, long>(save.Progress.Achievements.Counters) { [metric] = next };
            var claimed = new Dictionary<string, int>(save.Progress.Achievements.Claimed);

            // Pay out every not-yet-claimed tier (on this metric) that `next` now satisfies. Tiers are
            // ascending, so a single big jump can complete several at once — hence the while loop.
            var result = save;
            foreach (var def in cfg.Achievements)
            {
                if (def.Metric != metric) continue;
                int have = claimed.TryGetValue(def.Id, out var c) ? c : 0;
                while (have < def.Tiers.Count && next >= def.Tiers[have].Threshold)
                {
                    var tier = def.Tiers[have];
                    completed.Add(new AchievementUnlock { AchId = def.Id, Name = def.Name, TierIndex = have, Tier = tier });
                    if (tier.RewardGold > 0)  result = Progression.GrantGold(result, tier.RewardGold);
                    if (tier.RewardScrap > 0) result = Progression.GrantScrap(result, tier.RewardScrap);
                    if (tier.RewardXp > 0)    result = Progression.GrantPartyXp(result, tier.RewardXp, cfg);
                    have++;
                }
                if (have > 0) claimed[def.Id] = have;
            }

            // Swap in the new achievement state LAST: the payout reducers carried Progress forward
            // unchanged, so writing it here is authoritative (mirrors Quests.Advance's final swap).
            return (WithAchievements(result, new AchievementState { Counters = counters, Claimed = claimed }), completed);
        }

        // Clone the save with a new AchievementState, nested under a cloned ProgressState (its
        // siblings share refs). Everything else shares refs — the pure-reducer convention.
        private static SaveState WithAchievements(SaveState save, AchievementState ach) => new SaveState
        {
            Version = save.Version,
            RngSeed = save.RngSeed,
            RngCursor = save.RngCursor,
            Heroes = save.Heroes,
            Party = save.Party,
            LeaderHeroId = save.LeaderHeroId,
            Inventory = save.Inventory,
            Currencies = save.Currencies,
            Progress = new ProgressState
            {
                HighestStage = save.Progress.HighestStage,
                CurrentStage = save.Progress.CurrentStage,
                AccountLevel = save.Progress.AccountLevel,
                EndlessBest = save.Progress.EndlessBest,
                Tower = save.Progress.Tower,
                Achievements = ach,
                Daily = save.Progress.Daily,
                Crypt = save.Progress.Crypt,
                Intro = save.Progress.Intro,
                Loot = save.Progress.Loot,
            },
            Quests = save.Quests,
            Modifiers = save.Modifiers,
            GachaPity = save.GachaPity,
            LastClaimAt = save.LastClaimAt,
        };
    }

    /// <summary>One achievement tier newly completed by a <see cref="Achievements.Record"/> call,
    /// returned for the client feed / juice ("🏆 Monster Slayer II").</summary>
    public struct AchievementUnlock
    {
        public string AchId;
        public string Name;
        public int TierIndex;      // 0-based tier that just completed
        public AchievementTier Tier; // its threshold + reward
    }
}
