#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Live-ops events (10.16, mobile arc MM4) — recurring, time-boxed boosts that give a reason to
    /// open the app on a schedule. Two events ship (user verdict 2026-07-15, SMALL 10% magnitudes —
    /// the 50% pitch was explicitly rejected):
    ///
    ///  - WEEKEND ZONE BOOST: Saturday 00:00 → Monday 00:00 UTC, one ROTATING zone earns
    ///    +<see cref="BalanceConstants.EventZoneBoostMult"/> gold, XP AND drop rate. Farm-only
    ///    (campaign surfaces — the boosted zone is a stage band; Tower/Dungeon are deliberately untouched).
    ///  - MUTATED CRYPT: Wednesday 00:00 → Friday 00:00 UTC, crypt floors wear ONE extra modifier from
    ///    the cycle and chests pay +<see cref="BalanceConstants.EventCryptDustMult"/> grave dust.
    ///
    /// THE DURABLE SEAM is the SCHEDULE RULE: the active window and the rotating target are PURE FUNCTIONS
    /// of <c>nowMs</c> (epoch ms) — no persisted state, no save reducer, no server. Determinism is the
    /// usual GameCore contract: same <c>nowMs</c> ⇒ same answer. This is exactly the local stand-in that
    /// a remote-config / server event schedule replaces in design §9 (the door this leaves open): swap
    /// these code-const windows for downloaded ones and every effect path keeps working unchanged.
    ///
    /// All date math is UTC, off <see cref="DateTimeOffset.FromUnixTimeMilliseconds"/>. Effects snapshot
    /// AT FIGHT INIT (Combat.Init*), so a fight's boosts are fixed for its duration; idle accrual is
    /// deliberately NOT boosted (offline time is clock-gameable — see Combat/Idle). Windows are CODE
    /// RULES, not config numbers, so they live here as named consts (only the magnitudes are config).
    /// </summary>
    public static class Events
    {
        // ---- window rules (UTC, day-of-week boundaries at 00:00) --------------
        // Half-open [start, end): the start day is IN, the end day is OUT. Sat→Mon = {Sat, Sun};
        // Wed→Fri = {Wed, Thu}. Both spans are two days wide.
        private const DayOfWeek WeekendBoostStart = DayOfWeek.Saturday;
        private const DayOfWeek WeekendBoostEnd   = DayOfWeek.Monday;    // exclusive
        private const DayOfWeek CryptMutationStart = DayOfWeek.Wednesday;
        private const DayOfWeek CryptMutationEnd   = DayOfWeek.Friday;   // exclusive

        // Event ids (stable — the client banner keys off these; a future remote schedule reuses them).
        public const string WeekendZoneBoostId = "weekend_zone_boost";
        public const string MutatedCryptId     = "mutated_crypt";

        // ---- weekend zone boost ----------------------------------------------

        /// <summary>
        /// The weekend zone boost active at <paramref name="nowMs"/>, or null when the window is closed
        /// (or the config has no zones). The boosted zone ROTATES deterministically by ISO-ish week
        /// bucket: <c>zoneIndex = (epochDays / 7) % zoneCount</c>. Epoch day 0 is a Thursday, so every
        /// weekend (Sat = day 7k+1, Sun = day 7k+2) shares one /7 bucket — the zone is stable across the
        /// whole active window and advances by one each week. <see cref="ZoneBoostInfo.EndMs"/> is the
        /// Monday-00:00-UTC end the client CEILS into a countdown (the countdown display rule).
        /// </summary>
        public static ZoneBoostInfo? ZoneBoost(GameConfig cfg, long nowMs)
        {
            int zoneCount = cfg.Zones.Count;
            if (zoneCount <= 0) return null;
            var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;
            if (!InWindow(now, WeekendBoostStart, WeekendBoostEnd, out long endMs)) return null;

            long weekBucket = (nowMs / DailyLogin.DayMs) / 7; // nowMs ≥ 0 on any real clock; floor division
            int zoneIndex = (int)(((weekBucket % zoneCount) + zoneCount) % zoneCount);

            // The boosted zone is a StagesPerTier stage band (zone t = stages [t·per+1, (t+1)·per]),
            // matching GameConfig.ZoneForStage's table bands — the client can name it AND a fight decides
            // membership by comparing its own zone index (see EventEffects.ZoneBoostFor).
            int per = Math.Max(1, cfg.Balance.StagesPerTier);
            return new ZoneBoostInfo
            {
                ZoneIndex = zoneIndex,
                StageFrom = zoneIndex * per + 1,
                StageTo   = (zoneIndex + 1) * per,
                EndMs = endMs,
            };
        }

        // ---- mutated crypt ----------------------------------------------------

        /// <summary>The mutated-crypt event active at <paramref name="nowMs"/>, or null off-window.
        /// <see cref="CryptMutationInfo.EndMs"/> is the Friday-00:00-UTC end (countdown ceils).</summary>
        public static CryptMutationInfo? CryptMutation(GameConfig cfg, long nowMs)
        {
            var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;
            if (!InWindow(now, CryptMutationStart, CryptMutationEnd, out long endMs)) return null;
            return new CryptMutationInfo { EndMs = endMs };
        }

        /// <summary>
        /// The extra modifier id a mutated-crypt floor wears, or null when the event is off (or no
        /// modifiers are configured). Chosen DETERMINISTICALLY off depth + week — NOT rng, so a floor's
        /// mutation never touches the save's rng cursor (no re-roll games): <c>(depth + weekBucket) %
        /// cycleCount</c>. The same week rotates a stable modifier through consecutive floors; the same
        /// floor changes modifier week to week (the "mutation" churn). Combat.InitDungeon resolves the
        /// id against cfg.Modifiers and applies it at full strength beside the tower-floor modifier loop.
        /// </summary>
        public static string? MutationModifierId(GameConfig cfg, int depth, long nowMs)
        {
            var ev = CryptMutation(cfg, nowMs);
            if (ev == null) return null;
            int n = cfg.ModifierCycle.Count;
            if (n == 0) return null;
            // Key the week off the window's END (shared by every ms in the window) rather than the raw
            // /7 bucket — the Wed→Fri span straddles the epoch's Thursday /7 boundary, so bucketing on
            // nowMs would flip a floor's mutation mid-window. EndMs is constant across the window and
            // distinct each week, so the pick is stable within a window and rotates week to week.
            long week = ev.Value.EndMs / (7L * DailyLogin.DayMs);
            long key = Math.Max(1, depth) + week;
            int i = (int)(((key % n) + n) % n);
            return cfg.ModifierCycle[i];
        }

        // ---- client banner ----------------------------------------------------

        /// <summary>Every event active right now as a flat descriptor list for the client banner
        /// (id, display name, and the UTC end epoch-ms the client CEILS into a countdown). Empty when
        /// nothing is live. Order is stable: weekend boost, then mutated crypt.</summary>
        public static List<EventInfo> Active(GameConfig cfg, long nowMs)
        {
            var list = new List<EventInfo>();
            var zb = ZoneBoost(cfg, nowMs);
            if (zb != null)
            {
                string zoneName = zb.Value.ZoneIndex < cfg.Zones.Count ? cfg.Zones[zb.Value.ZoneIndex].Name : "Zone";
                list.Add(new EventInfo { Id = WeekendZoneBoostId, Name = zoneName + " Weekend Boost", EndMs = zb.Value.EndMs });
            }
            var cm = CryptMutation(cfg, nowMs);
            if (cm != null)
                list.Add(new EventInfo { Id = MutatedCryptId, Name = "Mutated Crypt", EndMs = cm.Value.EndMs });
            return list;
        }

        // ---- schedule primitive ----------------------------------------------

        /// <summary>
        /// True when <paramref name="nowUtc"/>'s day-of-week falls in the half-open weekly span
        /// [<paramref name="start"/>, <paramref name="endExclusive"/>), wrapping the week. On a hit,
        /// <paramref name="endMs"/> is the epoch-ms of the span's end boundary (midnight UTC of the
        /// end day-of-week, this same week) — what the client ceils into a countdown. Off-window it's 0.
        /// </summary>
        private static bool InWindow(DateTime nowUtc, DayOfWeek start, DayOfWeek endExclusive, out long endMs)
        {
            endMs = 0;
            int span   = (((int)endExclusive - (int)start) + 7) % 7; // days the window spans (2 for both events)
            int offset = (((int)nowUtc.DayOfWeek - (int)start) + 7) % 7; // days since the window opened
            if (offset >= span) return false;                        // outside [start, endExclusive)

            // Window start = today's UTC midnight rolled back `offset` days; end = start + span days.
            var startMidnight = nowUtc.Date.AddDays(-offset);        // Date keeps Kind = Utc
            var endMidnight   = startMidnight.AddDays(span);
            endMs = new DateTimeOffset(endMidnight, TimeSpan.Zero).ToUnixTimeMilliseconds();
            return true;
        }
    }

    /// <summary>The active weekend zone boost (<see cref="Events.ZoneBoost"/>): which zone is boosted
    /// (index + its stage band), and the UTC end the client ceils into a countdown.</summary>
    public struct ZoneBoostInfo
    {
        public int ZoneIndex;  // 0-based zone (into cfg.Zones); a fight is boosted iff its zone matches
        public int StageFrom;  // first stage of the boosted band (inclusive)
        public int StageTo;    // last stage of the boosted band (inclusive)
        public long EndMs;     // Monday 00:00 UTC end (countdown ceils)
    }

    /// <summary>The active mutated-crypt event (<see cref="Events.CryptMutation"/>).</summary>
    public struct CryptMutationInfo
    {
        public long EndMs; // Friday 00:00 UTC end (countdown ceils)
    }

    /// <summary>One active event as a client-banner descriptor (<see cref="Events.Active"/>).</summary>
    public struct EventInfo
    {
        public string Id;
        public string Name;
        public long EndMs; // UTC end epoch-ms; the client CEILS (EndMs - now) into a countdown
    }

    /// <summary>
    /// A fight's snapshotted live-ops multipliers, stored transiently on <see cref="CombatState.Events"/>
    /// and read in Combat.HandleDeath / PayChest. Stored as ADDITIVE BONUSES (0 = no event) so a default
    /// value — every hand-built CombatState, every legacy/no-clock init — is a clean 1.0 no-op with no
    /// allocation. The multipliers are (1 + bonus). Zone boost fills Gold/Xp/Drop; mutated crypt fills Dust.
    /// </summary>
    public struct EventMults
    {
        public double GoldBonus;
        public double XpBonus;
        public double DropBonus;
        public double DustBonus;

        public double GoldMult => 1.0 + GoldBonus;
        public double XpMult   => 1.0 + XpBonus;
        public double DropMult => 1.0 + DropBonus;
        public double DustMult => 1.0 + DustBonus;
    }
}
