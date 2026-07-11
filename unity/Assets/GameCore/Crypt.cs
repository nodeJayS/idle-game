#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Crypt meta (the roguelite mode's reason to exist — user-approved design 2026-07-06).
    ///
    ///  - KEYS gate entry: one recharges per UTC calendar day (DailyLogin's day-index convention),
    ///    banked up to <see cref="BalanceConstants.CryptKeyBank"/>. <see cref="TickKeys"/> is the
    ///    recharge reducer (the client calls it with `now` on menu open); <see cref="StartRun"/>
    ///    consumes one key.
    ///  - A RUN descends <see cref="BalanceConstants.CryptFloorsPerRun"/> floors back-to-back,
    ///    starting at <see cref="CryptState.DepthRecord"/>+1. Because <see cref="RecordFloorClear"/>
    ///    advances the record behind the party, every floor of a run is a FIRST clear — each pays
    ///    <see cref="BalanceConstants.CryptGemsPerFloor"/> gems, Tower-style.
    ///  - REWARDS accrue DURING the sweep: the §7.3 reward vault's chests pay gold/grave dust — the
    ///    crypt-only currency (<see cref="BalanceConstants.CryptDustCurrency"/> in Currencies) — and
    ///    loot into the run's pending totals as the party walks them. A wipe forfeits whatever is
    ///    still unwalked; anything already dropped stays (the design's death rule).
    ///  - Dust buys BOONS (<see cref="BuyBoon"/>): permanent account-wide stat buffs with a
    ///    geometric cost curve, folded into hero stats via <see cref="ApplyBoons"/> exactly where
    ///    Tower's milestone buffs fold in.
    ///  - Floors ramp difficulty geometrically (<see cref="FloorHpMult"/>/<see cref="FloorDmgMult"/>)
    ///    ON TOP of current-stage monster scaling, and travel themed TIERS
    ///    (<see cref="TierForFloor"/>: crypt → molten → frost, cycling) whose rosters/bosses feed
    ///    Combat.InitDungeon.
    ///
    /// All reducers are pure (new SaveState, untouched siblings shared by ref; no-ops share the
    /// whole save ref) and take `now` as epoch ms — never the wall clock.
    /// </summary>
    public static class Crypt
    {
        // ---- keys ----------------------------------------------------------

        /// <summary>Banked keys right now (before any tick).</summary>
        public static int Keys(SaveState save) => save.Progress.Crypt.Keys;

        /// <summary>
        /// Recharge reducer: grant one key per UTC day elapsed since the last grant, clamped to the
        /// bank cap. The very first tick ever (LastKeyDay 0) grants a key immediately — a new player
        /// can try the mode without waiting a day. The day stamp always advances to TODAY on a grant
        /// day, so a full bank doesn't accrue back-pay (idle-standard: keys only accrue below cap).
        /// No-op (shares the save ref) when no day has passed or the clock rolled back.
        /// </summary>
        public static SaveState TickKeys(SaveState save, GameConfig cfg, long nowMs)
        {
            var c = save.Progress.Crypt;
            long today = DailyLogin.DayIndex(nowMs);
            int bank = Math.Max(1, cfg.Balance.CryptKeyBank);

            int grant;
            if (c.LastKeyDay == 0) grant = 1;                       // first tick ever
            else if (today <= c.LastKeyDay) return save;            // same day / rolled-back clock
            else grant = (int)Math.Min(today - c.LastKeyDay, bank); // one per elapsed day

            int keys = Math.Min(bank, c.Keys + grant);
            return WithCrypt(save, new CryptState
            {
                Keys = keys,
                LastKeyDay = today,
                DepthRecord = c.DepthRecord,
                Boons = c.Boons,
                ActiveRun = c.ActiveRun,
            }, cfg);
        }

        /// <summary>Epoch ms when the next key finishes recharging (start of the next UTC day after
        /// the last grant). Meaningful only while below the bank cap; countdowns display-ceil.</summary>
        public static long NextKeyAtMs(SaveState save) =>
            (save.Progress.Crypt.LastKeyDay + 1) * DailyLogin.DayMs;

        // ---- run lifecycle -------------------------------------------------

        /// <summary>Deepest floor ever cleared (0 = none).</summary>
        public static int DepthRecord(SaveState save) => save.Progress.Crypt.DepthRecord;

        /// <summary>The floor the next run (or the next floor of the current run) starts at.</summary>
        public static int NextFloor(SaveState save) => DepthRecord(save) + 1;

        /// <summary>True when the crypt's content height is fully cleared.</summary>
        public static bool IsComplete(SaveState save, GameConfig cfg) =>
            DepthRecord(save) >= cfg.Balance.CryptMaxDepth;

        /// <summary>A run may start with a banked key and depth left to clear.</summary>
        public static bool CanStart(SaveState save, GameConfig cfg) =>
            Keys(save) >= 1 && !IsComplete(save, cfg);

        /// <summary>Consume one key to start a run. No-op (shares the ref, started = false) when
        /// <see cref="CanStart"/> fails — the client must not build a run state in that case.</summary>
        public static (SaveState save, bool started) StartRun(SaveState save, GameConfig cfg)
        {
            if (!CanStart(save, cfg)) return (save, false);
            var c = save.Progress.Crypt;
            var next = WithCrypt(save, new CryptState
            {
                Keys = c.Keys - 1,
                LastKeyDay = c.LastKeyDay,
                DepthRecord = c.DepthRecord,
                Boons = c.Boons,
                ActiveRun = c.ActiveRun, // BeginRunFloor sets it in the SAME save write (no lost-key window)
            }, cfg);
            return (next, true);
        }

        // ---- mid-run persistence (§7.3) ------------------------------------

        /// <summary>The suspended run, or null when idle.</summary>
        public static CryptRunState? ActiveRun(SaveState save) => save.Progress.Crypt.ActiveRun;

        /// <summary>True when a run is in progress (a quit left it resumable).</summary>
        public static bool HasActiveRun(SaveState save) => save.Progress.Crypt.ActiveRun != null;

        /// <summary>
        /// Record the floor now being attempted (the run's first floor at start, or the next floor on
        /// a descend): persists its <paramref name="seed"/> so a resume reproduces the exact layout,
        /// plus how many descends remain. Meant to share the SAME save write as <see cref="StartRun"/>
        /// so a crash can't spend a key without leaving a resumable run. Pure.
        /// </summary>
        public static SaveState BeginRunFloor(SaveState save, int floor, int floorsLeft, int seed,
                                              bool finalFloor, GameConfig cfg)
        {
            var c = save.Progress.Crypt;
            return WithCrypt(save, new CryptState
            {
                Keys = c.Keys,
                LastKeyDay = c.LastKeyDay,
                DepthRecord = c.DepthRecord,
                Boons = c.Boons,
                ActiveRun = new CryptRunState
                {
                    Floor = floor,
                    FloorsLeft = Math.Max(0, floorsLeft),
                    Seed = seed,
                    FinalFloor = finalFloor,
                },
            }, cfg);
        }

        /// <summary>Clear the suspended run (completed, abandoned, or wiped). No-op share when idle.</summary>
        public static SaveState EndRun(SaveState save, GameConfig cfg)
        {
            var c = save.Progress.Crypt;
            if (c.ActiveRun == null) return save;
            return WithCrypt(save, new CryptState
            {
                Keys = c.Keys,
                LastKeyDay = c.LastKeyDay,
                DepthRecord = c.DepthRecord,
                Boons = c.Boons,
                ActiveRun = null,
            }, cfg);
        }

        /// <summary>
        /// Record a floor clear: advances the depth record by one AND pays the first-clear gem
        /// reward — mirroring <see cref="Tower.RecordClear"/> exactly. No-op (shares the ref, pays
        /// nothing) unless <paramref name="floor"/> is exactly the next sequential depth within the
        /// content height, so a re-clear or a skip grants nothing. Pure.
        /// </summary>
        public static SaveState RecordFloorClear(SaveState save, int floor, GameConfig cfg)
        {
            if (floor != NextFloor(save) || floor < 1 || floor > cfg.Balance.CryptMaxDepth) return save;
            var c = save.Progress.Crypt;
            return WithCrypt(save, new CryptState
            {
                Keys = c.Keys,
                LastKeyDay = c.LastKeyDay,
                DepthRecord = floor,
                Boons = c.Boons,
                ActiveRun = c.ActiveRun, // a mid-run clear keeps the run alive (descend/end updates it)
            }, cfg, gems: cfg.Balance.CryptGemsPerFloor);
        }

        /// <summary>Grave-dust balance (the chest currency).</summary>
        public static long Dust(SaveState save, GameConfig cfg) =>
            save.Currencies.TryGetValue(cfg.Balance.CryptDustCurrency, out var v) ? v : 0;

        // ---- per-floor difficulty + theme -----------------------------------

        /// <summary>Geometric HP multiplier for a floor's monsters, applied ON TOP of the current-stage
        /// scaling (a crypt floor tracks the player's power; the depth ladder adds ramp).</summary>
        public static double FloorHpMult(int floor, GameConfig cfg)
            => Math.Pow(cfg.Balance.CryptHpGrowth, Math.Max(0, floor - 1));

        /// <summary>Geometric atk multiplier for a floor's monsters (same convention as HP).</summary>
        public static double FloorDmgMult(int floor, GameConfig cfg)
            => Math.Pow(cfg.Balance.CryptDmgGrowth, Math.Max(0, floor - 1));

        /// <summary>The depth tier a floor belongs to: CryptTierFloors-floor bands cycling the
        /// configured tiers (crypt → molten → frost → crypt…). Null when no tiers are configured.</summary>
        public static CryptTierDef? TierForFloor(int floor, GameConfig cfg)
        {
            if (cfg.CryptTiers.Count == 0) return null;
            int band = Math.Max(0, floor - 1) / Math.Max(1, cfg.Balance.CryptTierFloors);
            return cfg.CryptTiers[band % cfg.CryptTiers.Count];
        }

        /// <summary>The §7.3 encounter-table row for a floor — the DEEPEST configured band whose
        /// MinDepth ≤ floor (config order scanned in full, so the list needn't be sorted) — mapped
        /// onto the DungeonGen spec. Null when no bands are configured or none is shallow enough
        /// (generation then falls back to its legacy area counts).</summary>
        public static DungeonEncounterSpec? EncounterForFloor(int floor, GameConfig cfg)
        {
            CryptEncounterDef? best = null;
            foreach (var e in cfg.CryptEncounters)
                if (e.MinDepth <= floor && (best == null || e.MinDepth > best.MinDepth))
                    best = e;
            if (best == null) return null;
            return new DungeonEncounterSpec
            {
                CombatWaves = best.CombatWaves,
                MobsPerWave = best.MobsPerWave,
                EliteCount = best.EliteCount,
                EliteEscort = best.EliteEscort,
                BossAdds = best.BossAdds,
                ChestCount = best.ChestCount,
                ChestWeightWooden = best.ChestWeightWooden,
                ChestWeightIron = best.ChestWeightIron,
                ChestWeightGolden = best.ChestWeightGolden,
                MimicChance = best.MimicChance,
                GoldenMythicChance = best.GoldenMythicChance,
            };
        }

        // ---- boons (the dust sink) ------------------------------------------

        /// <summary>Purchased rank of a boon (0 = none).</summary>
        public static int BoonRank(SaveState save, string boonId) =>
            save.Progress.Crypt.Boons.TryGetValue(boonId, out var r) ? r : 0;

        /// <summary>Dust cost of buying rank <paramref name="rank"/>+1 when currently at
        /// <paramref name="rank"/>: ceil(Base × Growth^rank) — costs ceil (display rule).</summary>
        public static long BoonCost(int rank, GameConfig cfg) =>
            (long)Math.Ceiling(cfg.Balance.CryptBoonBaseCost
                               * Math.Pow(cfg.Balance.CryptBoonCostGrowth, Math.Max(0, rank)));

        /// <summary>
        /// Buy one rank of a boon: deduct dust, bump the rank. No-op (shares the ref, bought = false)
        /// for an unknown boon id, a maxed rank, or unaffordable dust. Pure.
        /// </summary>
        public static (SaveState save, bool bought) BuyBoon(SaveState save, string boonId, GameConfig cfg)
        {
            if (cfg.CryptBoons.Find(b => b.Id == boonId) == null) return (save, false);
            int rank = BoonRank(save, boonId);
            if (rank >= cfg.Balance.CryptBoonMaxRank) return (save, false);
            long cost = BoonCost(rank, cfg);
            if (Dust(save, cfg) < cost) return (save, false);

            var c = save.Progress.Crypt;
            var boons = new Dictionary<string, int>(c.Boons) { [boonId] = rank + 1 };
            var next = WithCrypt(save, new CryptState
            {
                Keys = c.Keys,
                LastKeyDay = c.LastKeyDay,
                DepthRecord = c.DepthRecord,
                Boons = boons,
                ActiveRun = c.ActiveRun,
            }, cfg, dustDelta: -cost);
            return (next, true);
        }

        /// <summary>Apply the purchased boons to a computed stat block: each boon multiplies its stat
        /// by (1 + rank × CryptBoonStatPct). Pure (new block); a no-op share when nothing is owned.
        /// Folded into the combat stat build right beside <see cref="Tower.ApplyAccountBuffs"/>.</summary>
        public static StatBlock ApplyBoons(StatBlock s, SaveState save, GameConfig cfg)
        {
            var boons = save.Progress.Crypt.Boons;
            if (boons.Count == 0) return s;
            StatBlock? r = null;
            foreach (var def in cfg.CryptBoons) // config order — deterministic iteration
            {
                int rank = boons.TryGetValue(def.Id, out var v) ? v : 0;
                if (rank <= 0) continue;
                r ??= new StatBlock(s);
                r[def.Stat] = r.Get(def.Stat) * (1 + rank * cfg.Balance.CryptBoonStatPct);
            }
            return r ?? s;
        }

        // ---- copy helper (Crypt progress lives under ProgressState) ----------

        /// <summary>Clone the save with a new CryptState (and optionally credit gems / adjust dust);
        /// every sibling shares refs — the DailyLogin.Apply / Tower.WithFloor pattern.</summary>
        private static SaveState WithCrypt(SaveState save, CryptState crypt, GameConfig cfg,
                                           long gems = 0, long dustDelta = 0)
        {
            var currencies = save.Currencies;
            if (gems != 0 || dustDelta != 0)
            {
                currencies = new Dictionary<string, long>(save.Currencies);
                if (gems != 0)
                {
                    string k = cfg.Balance.PremiumCurrency;
                    currencies[k] = (currencies.TryGetValue(k, out var v) ? v : 0) + gems;
                }
                if (dustDelta != 0)
                {
                    string k = cfg.Balance.CryptDustCurrency;
                    currencies[k] = (currencies.TryGetValue(k, out var v) ? v : 0) + dustDelta;
                }
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
                    Tower = save.Progress.Tower,
                    Achievements = save.Progress.Achievements,
                    Daily = save.Progress.Daily,
                    Crypt = crypt,
                    Intro = save.Progress.Intro,
                    Loot = save.Progress.Loot,
                },
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }
    }
}
