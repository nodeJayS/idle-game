#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Hero ascension (10.17, mobile arc MM5) — the endgame SHARD sink, the ~6-month spend horizon that
    /// sits past the enhance/reforge/boon lanes. A universal per-hero STAR track: each hero climbs
    /// 0..<see cref="BalanceConstants.AscensionMaxStars"/> stars, and every star folds
    /// +<see cref="BalanceConstants.AscensionStarPct"/> Hp/Atk/Def into THAT hero's
    /// <see cref="Stats.ComputeHeroStats"/> — HERO-LOCAL, deliberately unlike the account-wide Tower
    /// milestone buffs (<see cref="Tower.ApplyAccountBuffs"/>) and codex drip (<see cref="Codex.ApplyAccountBuffs"/>)
    /// that fold across the whole party in <see cref="Combat.RefreshPartyStats"/>.
    ///
    /// TWO shard wallets, spent hero-first:
    ///  - PER-HERO shards (<see cref="ProgressState.Ascension"/> → <see cref="AscensionState.Shards"/>,
    ///    keyed by hero DEF id) come from GACHA DUPES (<see cref="Gacha"/> grants
    ///    <see cref="BalanceConstants.AscensionShardsPerDupe"/> to the rolled hero's wallet, replacing the
    ///    old XP/scrap dupe payout — user verdict 2026-07-15). They can only star up that hero.
    ///  - UNIVERSAL shards live in <see cref="SaveState.Currencies"/> under
    ///    <see cref="UniversalShardCurrency"/> and can star up ANY hero. The base roster (who never rolls
    ///    dupes) earns them from the endless-depth milestone (<see cref="Progression.OnStageCleared"/> pays
    ///    <see cref="BalanceConstants.AscensionShardsPerEndlessMilestone"/> beside the endless gems, on the
    ///    same every-<see cref="BalanceConstants.EndlessGemsEvery"/>th-depth gate).
    ///
    /// <see cref="StarUp"/> spends the hero's OWN wallet FIRST, then dips into universal for the remainder
    /// — so dupe shards stay earmarked to their hero and universal shards flex to whoever needs them.
    /// Deterministic (no rng — a pure priced purchase, unlike the enhance/reforge gambles). Read models are
    /// pure projections; <see cref="StarUp"/> returns a fresh save, and a no-op (maxed / unaffordable /
    /// unknown hero) shares the input ref with a null result — the Enhance/Gacha convention.
    /// </summary>
    public static class Ascension
    {
        /// <summary>The <see cref="SaveState.Currencies"/> key holding UNIVERSAL ascension shards (spendable
        /// on any hero). The per-hero dupe wallets live separately in <see cref="AscensionState.Shards"/>.</summary>
        public const string UniversalShardCurrency = "hero_shards";

        // ---- read models (pure projections) --------------------------------

        /// <summary>Ascension shards banked for a specific hero DEF (the per-hero dupe wallet). 0 when the
        /// hero has never dupe'd.</summary>
        public static long ShardsFor(SaveState save, string defId) =>
            save.Progress.Ascension.Shards.TryGetValue(defId, out var s) ? s : 0;

        /// <summary>The account's UNIVERSAL ascension-shard balance (spendable on any hero).</summary>
        public static long UniversalShards(SaveState save) =>
            save.Currencies.TryGetValue(UniversalShardCurrency, out var s) ? s : 0;

        /// <summary>The shard cost of a hero's NEXT star (star <paramref name="currentStars"/>+1), or -1 if
        /// already maxed. Indexed off the current star count into <see cref="BalanceConstants.AscensionStarCosts"/>
        /// (cost[0] = first star). Defensive against a costs table shorter than AscensionMaxStars.</summary>
        public static long NextStarCost(int currentStars, GameConfig cfg)
        {
            var costs = cfg.Balance.AscensionStarCosts;
            if (currentStars < 0 || currentStars >= cfg.Balance.AscensionMaxStars || currentStars >= costs.Length)
                return -1;
            return costs[currentStars];
        }

        /// <summary>True when <paramref name="heroId"/> is owned, below the star cap, and the combined
        /// (per-hero + universal) wallet can pay the next star's cost. The exact gate <see cref="StarUp"/>
        /// enforces.</summary>
        public static bool CanStarUp(SaveState save, string heroId, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero == null) return false;
            long cost = NextStarCost(hero.Stars, cfg);
            if (cost < 0) return false; // maxed
            return ShardsFor(save, hero.DefId) + UniversalShards(save) >= cost;
        }

        // ---- the one reducer -----------------------------------------------

        /// <summary>
        /// Spend one star's worth of shards on a hero, HERO WALLET FIRST then universal. Bumps the hero's
        /// <see cref="HeroInstance.Stars"/> by 1 through a threaded copy, debits the per-hero wallet up to
        /// the cost, then the universal currency for any shortfall (deterministic split — no rng). Returns
        /// the fresh save plus a <see cref="StarUpResult"/> for the client feed. A NO-OP (unknown hero /
        /// maxed / can't afford) shares the input ref and returns a null result — the Enhance/Gacha idiom.
        /// Pure: the input save is never mutated.
        /// </summary>
        public static (SaveState save, StarUpResult? result) StarUp(SaveState save, string heroId, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero == null) return (save, null);
            long cost = NextStarCost(hero.Stars, cfg);
            if (cost < 0) return (save, null); // maxed

            long heroWallet = ShardsFor(save, hero.DefId);
            long universal = UniversalShards(save);
            if (heroWallet + universal < cost) return (save, null); // can't afford — share the ref

            // Spend the hero's own shards first, then universal for the remainder.
            long spentHero = Math.Min(heroWallet, cost);
            long spentUniversal = cost - spentHero;

            // Per-hero wallet (nested under ProgressState.Ascension) — clone the dict, debit or clear.
            var shards = new Dictionary<string, long>(save.Progress.Ascension.Shards);
            if (spentHero > 0)
            {
                long left = heroWallet - spentHero;
                if (left > 0) shards[hero.DefId] = left; else shards.Remove(hero.DefId);
            }

            // Universal currency — clone + debit only when we actually dip into it.
            var currencies = save.Currencies;
            if (spentUniversal > 0)
            {
                currencies = new Dictionary<string, long>(save.Currencies);
                currencies[UniversalShardCurrency] = universal - spentUniversal;
            }

            // Bump the hero's stars through a full HeroInstance copy (threads every field — the 10.5 rule).
            int newStars = hero.Stars + 1;
            var nextHeroes = new List<HeroInstance>(save.Heroes.Count);
            foreach (var h in save.Heroes)
                nextHeroes.Add(ReferenceEquals(h, hero) ? CopyWithStars(h, newStars) : h);

            var next = new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = nextHeroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = currencies,
                Progress = WithShards(save.Progress, shards),
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };

            var result = new StarUpResult
            {
                DefId = hero.DefId,
                NewStars = newStars,
                SpentHero = spentHero,
                SpentUniversal = spentUniversal,
            };
            return (next, result);
        }

        /// <summary>Credit <paramref name="amount"/> ascension shards to a hero DEF's wallet (the gacha-dupe
        /// grant seam — see <see cref="Gacha"/>). Clones the Ascension state under a cloned ProgressState;
        /// everything else shares refs. No-op share when amount ≤ 0. Pure.</summary>
        public static SaveState GrantHeroShards(SaveState save, string defId, long amount)
        {
            if (amount <= 0) return save;
            var shards = new Dictionary<string, long>(save.Progress.Ascension.Shards);
            shards[defId] = (shards.TryGetValue(defId, out var s) ? s : 0) + amount;
            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = WithShards(save.Progress, shards),
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = save.GachaPity,
                LastClaimAt = save.LastClaimAt,
            };
        }

        // ---- copy helpers --------------------------------------------------

        /// <summary>Clone a hero with a new star count, threading every field (the CloneHero convention —
        /// a dropped field silently resets it).</summary>
        private static HeroInstance CopyWithStars(HeroInstance hero, int stars) => new HeroInstance
        {
            Id = hero.Id,
            DefId = hero.DefId,
            Level = hero.Level,
            Xp = hero.Xp,
            Equipped = hero.Equipped,
            SkillRanks = hero.SkillRanks,
            Loadout = hero.Loadout,
            Stars = stars,
        };

        /// <summary>Clone a ProgressState swapping in a new AscensionState (siblings share refs) — the
        /// <see cref="Codex"/>.WithCodex pattern.</summary>
        private static ProgressState WithShards(ProgressState progress, Dictionary<string, long> shards) => new ProgressState
        {
            HighestStage = progress.HighestStage,
            CurrentStage = progress.CurrentStage,
            AccountLevel = progress.AccountLevel,
            EndlessBest = progress.EndlessBest,
            Tower = progress.Tower,
            Achievements = progress.Achievements,
            Daily = progress.Daily,
            Crypt = progress.Crypt,
            Intro = progress.Intro,
            Loot = progress.Loot,
            Codex = progress.Codex,
            Season = progress.Season,
            Ascension = new AscensionState { Shards = shards },
        };
    }

    /// <summary>A completed star-up (<see cref="Ascension.StarUp"/>), returned for the client feed/toast
    /// ("Knight — ★3"). <see cref="SpentHero"/> + <see cref="SpentUniversal"/> = the star's total cost.</summary>
    public struct StarUpResult
    {
        public string DefId;        // the hero def that ascended
        public int NewStars;        // its star count AFTER the spend
        public long SpentHero;      // shards drawn from the hero's own wallet (spent first)
        public long SpentUniversal; // shards drawn from the universal wallet (the remainder)
    }
}
