#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Tower of Ascension (alt mode) — a separate, one-clear-per-floor track that is *not* farmed
    /// like the stage ladder. Floors scale STEEPER than the ladder on both HP and damage
    /// (<see cref="BalanceConstants.TowerHpGrowth"/> / <see cref="BalanceConstants.TowerDmgGrowth"/>),
    /// carry a rotating per-floor modifier (the puzzle layer, reusing the Lever-1 modifier catalog),
    /// and grant a permanent **account-wide** buff every <see cref="BalanceConstants.TowerMilestoneEvery"/>
    /// floors. Progress is a single int (<see cref="TowerState.HighestFloor"/>); milestone buffs are
    /// *derived* from it, so nothing extra is persisted. Pure reducers (return a new SaveState).
    ///
    /// Each floor's FIRST clear pays gems + a one-time gold + boss-loot bundle, all banked in
    /// <see cref="RecordClear"/> — the single exploit-proof gate, since Tower kills pay nothing. The bundle
    /// anchors to the floor's difficulty-equivalent stage (<see cref="StageEquivalent"/>), and milestone
    /// floors pay a MAJOR boss bundle on top of the permanent <see cref="ApplyAccountBuffs"/> buff.
    /// </summary>
    public static class Tower
    {
        /// <summary>Deepest floor cleared (0 = none).</summary>
        public static int HighestFloor(SaveState save) => save.Progress.Tower.HighestFloor;

        /// <summary>The next attemptable floor (one past the highest cleared).</summary>
        public static int NextFloor(SaveState save) => HighestFloor(save) + 1;

        /// <summary>Top floor of the tower at this content height.</summary>
        public static int MaxFloor(GameConfig cfg) => cfg.Balance.TowerFloors;

        /// <summary>True once every floor has been cleared.</summary>
        public static bool IsComplete(SaveState save, GameConfig cfg) => HighestFloor(save) >= MaxFloor(cfg);

        /// <summary>You may only attempt the next uncleared floor (sequential, no skipping), and only
        /// within the defined tower height.</summary>
        public static bool CanAttempt(SaveState save, int floor, GameConfig cfg)
            => floor == NextFloor(save) && floor >= 1 && floor <= MaxFloor(cfg);

        /// <summary>Record a floor clear: advances <see cref="TowerState.HighestFloor"/> by one AND grants
        /// <see cref="BalanceConstants.TowerGemsPerFloor"/> gems (the per-floor reward). No-op (shares the
        /// save ref, grants NOTHING) unless <paramref name="floor"/> is exactly the next sequential floor —
        /// so re-clearing an old floor or skipping ahead pays nothing. Forward-only. Pure.
        ///
        /// Reward convention: EVERY floor pays gems + a one-time GOLD + BOSS-LOOT bundle on its first clear
        /// (rolled + banked here in <see cref="RecordClear"/> — the single exploit-proof gate; Tower kills
        /// pay NOTHING, so a failed grind earns nothing). The bundle anchors to the floor's difficulty-
        /// equivalent stage (<see cref="StageEquivalent"/>), and milestone floors pay a MAJOR boss bundle.
        /// The floors on the <see cref="BalanceConstants.TowerMilestoneEvery"/> interval are the MILESTONE
        /// floors — they also add the permanent account-wide buff (derived in <see cref="MilestonesCleared"/>)
        /// and are where any configured rare-mod pair unlocks (<see cref="ModifierDef.TowerUnlockFloor"/>,
        /// re-derived by <see cref="Modifiers.SyncToStage"/> from the floor count — so unlock floors must NOT
        /// move or owned mods would be revoked). The gem + bundle grant is on TOP of those milestone payoffs.</summary>
        public static SaveState RecordClear(SaveState save, int floor, GameConfig cfg)
            => RecordClear(save, floor, cfg, out _);

        /// <summary>Record a floor clear AND surface the full first-clear payout. Same gate as the
        /// reward-less overload: a re-clear or a skip (<see cref="CanAttempt"/> false) shares the input save
        /// ref and reports <paramref name="reward"/> = null — that contract keeps the mode exploit-proof.
        /// On the real advance path this rolls the bundle on the SAVE rng (persisted-cursor pattern, so the
        /// roll can't be re-rolled — see Gacha.cs) anchored to <see cref="StageEquivalent"/>, banks gems +
        /// gold + the boss loot (auto-salvage filter applies), and fills <paramref name="reward"/>. Pure.</summary>
        public static SaveState RecordClear(SaveState save, int floor, GameConfig cfg, out TowerClearReward? reward)
        {
            if (!CanAttempt(save, floor, cfg)) { reward = null; return save; } // re-clears / skips pay nothing

            // The floor's bundle is AS HARD AS its difficulty-equivalent stage, not its floor number — the
            // loot item level and the gold rate both ride that stage. Milestone (guardian) floors pay a
            // MAJOR boss bundle; every other floor a mini-boss one.
            int equiv = StageEquivalent(floor, cfg);
            bool major = floor % Math.Max(1, cfg.Balance.TowerMilestoneEvery) == 0;

            // Roll on the save's own RNG and PERSIST the advanced cursor (design rule: rolls on the save RNG
            // thread the cursor forward so they can't re-roll — the canonical Gacha.cs pattern).
            var rng = new Rng(save.RngSeed, save.RngCursor);
            var rolls = Loot.RollBossDrops(rng, LootContext.ForStage(cfg.StageFor(equiv)!, cfg), cfg, isMajor: major);
            long gold = GoldBundle(floor, cfg); // the ONE gold formula — TowerView's preview reads the same

            // Compose pure reducers: WithFloor (floor + gems, now threading the advanced cursor) →
            // GrantGold (the canonical gold credit, carries the cursor through) → AddLoot (allowOverflow:
            // a boss bundle may push past the bag cap — the doc on AddLoot names boss rewards as that case).
            var next = WithFloor(save, floor, cfg, rng.Cursor);
            next = Progression.GrantGold(next, gold);
            var result = Inventory.AddLoot(next, rolls, cfg, allowOverflow: true);

            reward = new TowerClearReward
            {
                Gems = cfg.Balance.TowerGemsPerFloor,
                Gold = gold,
                Stored = result.Stored,
                Salvaged = result.Salvaged,
                ScrapGained = result.ScrapGained,
            };
            return result.Save;
        }

        /// <summary>The full payout of a floor's first clear — the flat per-floor gem drip plus the one-time
        /// gold + boss-loot bundle (<see cref="Stored"/> kept in the bag, <see cref="Salvaged"/> scrapped by
        /// the loot filter, <see cref="ScrapGained"/> from that). Surfaced to the client feed.</summary>
        public sealed class TowerClearReward
        {
            public long Gems;
            public long Gold;
            public List<Item> Stored = new List<Item>();
            public List<Item> Salvaged = new List<Item>();
            public long ScrapGained;
        }

        /// <summary>The floor's DIFFICULTY-EQUIVALENT stage: loot ilvl and the gold bundle anchor to the
        /// stage the floor is AS HARD AS, not the floor number. A floor's monsters are stage-F fights layered
        /// with <see cref="BalanceConstants.TowerHpGrowth"/>^(F-1), so floor F outweighs stage F — the linear
        /// anchor 1+(F−1)·<see cref="BalanceConstants.TowerLootStagePerFloor"/> approximates that on the
        /// ladder (floor 1→1, floor 30→~100). Mirrors <see cref="Crypt.StageEquivalent"/>.</summary>
        public static int StageEquivalent(int floor, GameConfig cfg) =>
            Math.Max(1, (int)Math.Round(1 + (Math.Max(1, floor) - 1) * cfg.Balance.TowerLootStagePerFloor));

        /// <summary>The one-time gold a floor's first clear pays: <see cref="BalanceConstants.TowerGoldBundleMinutes"/>
        /// minutes of idle-rate gold at the floor's <see cref="StageEquivalent"/>, floored (balances round
        /// down). NO OfflineRate discount — this is an active-play reward. Exposed so the client preview
        /// reads the same formula <see cref="RecordClear"/> banks rather than duplicating it.</summary>
        public static long GoldBundle(int floor, GameConfig cfg)
        {
            int equiv = StageEquivalent(floor, cfg);
            return (long)Math.Floor(cfg.Balance.GoldPerSec(equiv) * 60.0 * cfg.Balance.TowerGoldBundleMinutes);
        }

        // ---- derived milestone / account-buff payoff ----

        /// <summary>How many milestone buffs have been earned (one per TowerMilestoneEvery floors).</summary>
        public static int MilestonesCleared(SaveState save, GameConfig cfg)
            => HighestFloor(save) / Math.Max(1, cfg.Balance.TowerMilestoneEvery);

        /// <summary>The permanent account-wide stat bonus from cleared milestones, as a fraction
        /// (0.10 = +10%). Each milestone adds <see cref="BalanceConstants.TowerMilestoneStatPct"/>.</summary>
        public static double AccountBuffPct(SaveState save, GameConfig cfg)
            => MilestonesCleared(save, cfg) * cfg.Balance.TowerMilestoneStatPct;

        /// <summary>Apply the derived account buff to a computed stat block: scales the core power
        /// stats (Hp/Atk/Def) by (1 + <see cref="AccountBuffPct"/>). Pure (returns a new block);
        /// a no-op share when no milestone is earned. Folded into the combat stat build in slice 2.</summary>
        public static StatBlock ApplyAccountBuffs(StatBlock s, SaveState save, GameConfig cfg)
        {
            double pct = AccountBuffPct(save, cfg);
            if (pct <= 0) return s;
            var r = new StatBlock(s);
            r[StatKey.Hp] = r.Get(StatKey.Hp) * (1 + pct);
            r[StatKey.Atk] = r.Get(StatKey.Atk) * (1 + pct);
            r[StatKey.Def] = r.Get(StatKey.Def) * (1 + pct);
            return r;
        }

        // ---- per-floor difficulty (used by the slice-2 tower fight) ----

        /// <summary>Geometric HP multiplier for a floor's monsters vs a floor-1 baseline.</summary>
        public static double FloorHpMult(int floor, GameConfig cfg)
            => Math.Pow(cfg.Balance.TowerHpGrowth, Math.Max(0, floor - 1));

        /// <summary>Geometric atk/def multiplier for a floor's monsters vs a floor-1 baseline.</summary>
        public static double FloorDmgMult(int floor, GameConfig cfg)
            => Math.Pow(cfg.Balance.TowerDmgGrowth, Math.Max(0, floor - 1));

        /// <summary>The modifier this floor exhibits (or null in the early ramp).</summary>
        public static string? FloorModifier(int floor, GameConfig cfg) => cfg.TowerModifierForFloor(floor);

        // ---- copy helper (Tower progress lives under ProgressState) ----

        private static SaveState WithFloor(SaveState save, int floor, GameConfig cfg, int rngCursor)
        {
            var progress = new ProgressState
            {
                HighestStage = save.Progress.HighestStage,
                CurrentStage = save.Progress.CurrentStage,
                AccountLevel = save.Progress.AccountLevel,
                EndlessBest = save.Progress.EndlessBest,
                Tower = new TowerState { HighestFloor = floor },
                Achievements = save.Progress.Achievements,
                Daily = save.Progress.Daily,
                Crypt = save.Progress.Crypt,
                Intro = save.Progress.Intro,
                Loot = save.Progress.Loot,
                Codex = save.Progress.Codex,
                Season = save.Progress.Season,
            };
            // Grant the per-floor gem reward — mirror DailyLogin.Apply's premium-currency credit exactly
            // (clone the currencies dict, add to Currencies[PremiumCurrency]). Only ever reached on the
            // real advance path (CanAttempt already gated the no-op), so re-clears grant nothing.
            long gems = cfg.Balance.TowerGemsPerFloor;
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
                RngCursor = rngCursor, // the bundle roll advanced the cursor — persist it so it can't re-roll
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = currencies,
                Progress = progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }
    }
}
