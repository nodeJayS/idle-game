#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Hero gacha — the gem SINK the economy has been missing (roadmap 3). Daily login is the only gem
    /// SOURCE; this is the first SPEND. One <see cref="Roll"/> costs <see cref="GachaBannerDef.CostGems"/>
    /// of the premium currency and weighted-picks a hero from the banner pool.
    ///
    /// Why it's built this way:
    ///  - Persisted-cursor RNG (design principle 4): a roll rides the SAME save rng cursor the other
    ///    gamble reducers use (Inventory.Enhance / Reforge, Modifiers.UpgradeModifier) and PERSISTS the
    ///    advanced cursor. So same save + same seed ⇒ the same hero, and a re-load can NEVER re-roll a
    ///    result (rolling twice from the SAME pre-roll save is identical; the cursor only moves when the
    ///    roll is actually committed to a returned save). Every roll draws EXACTLY one rng value
    ///    (via <see cref="Rng.WeightedPick{T}"/>), so pity-forced and normal rolls advance the cursor the
    ///    same amount and the stream stays aligned across mixed sequences.
    ///  - Pity (<see cref="GachaBannerDef.PityCount"/>): a per-banner counter in the save
    ///    (<see cref="SaveState.GachaPity"/>) increments on every roll that does NOT yield the featured
    ///    hero; hitting PityCount FORCES the featured hero and resets to 0. Drawing the featured hero
    ///    naturally also resets it. This is the anti-frustration guarantee — you can't roll forever
    ///    without the headliner.
    ///  - New vs dupe: a hero you don't own joins the roster (via <see cref="Party.AcquireHero"/>, the
    ///    same mint path stage-unlocks use — NOT auto-fielded). A dupe converts to
    ///    <see cref="BalanceConstants.AscensionShardsPerDupe"/> ASCENSION SHARDS on the rolled hero's wallet
    ///    (<see cref="Ascension.GrantHeroShards"/>, 10.17 — replacing the old XP/scrap dupe payout per the
    ///    2026-07-15 verdict), the fuel for that hero's star track.
    ///
    /// Pure reducers: the input save is never mutated; <see cref="Roll"/> returns a fresh save inside the
    /// result. Unaffordable/unknown-banner rolls are a NO-OP (share the ref) — mirroring Enhance/Reforge,
    /// which no-op rather than throw when the player can't pay.
    /// </summary>
    public static class Gacha
    {
        /// <summary>One roll's outcome (the save inside is post-roll). <see cref="Rolled"/> is false on a
        /// no-op (unknown banner / can't afford) — everything else is then default and the save is the
        /// input, unchanged.</summary>
        public sealed class RollResult
        {
            public SaveState Save = null!;
            public bool Rolled;            // false = no-op (couldn't afford / unknown banner)
            public string HeroDefId = "";  // the hero def the roll landed on
            public bool IsNew;             // true = joined the roster; false = a dupe
            public bool IsFeatured;        // the rolled hero is the banner's featured hero
            public bool PityTriggered;     // this roll hit PityCount and was FORCED to the featured hero
            public long DupeShards;        // ascension shards granted to the rolled hero's wallet on a dupe (0 when new)
        }

        /// <summary>Current pity count on a banner (rolls made without drawing its featured hero). 0 when
        /// the banner has never been rolled or was just reset.</summary>
        public static int PityOf(SaveState save, string bannerId) =>
            save.GachaPity.TryGetValue(bannerId, out var p) ? p : 0;

        /// <summary>The player's premium-currency (gem) balance.</summary>
        private static long Gems(SaveState save, GameConfig cfg) =>
            save.Currencies.TryGetValue(cfg.Balance.PremiumCurrency, out var g) ? g : 0;

        /// <summary>True if the banner exists (with a non-empty pool) and the player can afford one roll.
        /// Same affordability gate <see cref="Roll"/> uses.</summary>
        public static bool CanRoll(SaveState save, GameConfig cfg, string bannerId)
        {
            if (!cfg.Banners.TryGetValue(bannerId, out var banner)) return false;
            if (banner.Pool.Count == 0) return false;
            return Gems(save, cfg) >= banner.CostGems;
        }

        /// <summary>
        /// Spend gems on one roll of a banner. Weighted-picks a hero from the pool (persisted-cursor RNG);
        /// pity forces the featured hero at <see cref="GachaBannerDef.PityCount"/> and resets, and a
        /// natural featured draw resets it too. A new hero joins the roster; a dupe grants the rolled hero
        /// XP + account scrap. No-op (<see cref="RollResult.Rolled"/> = false, shares the input save) when
        /// the banner is unknown or unaffordable — same as an unaffordable Enhance/Reforge. Pure.
        /// </summary>
        public static RollResult Roll(SaveState save, GameConfig cfg, string bannerId)
        {
            if (!CanRoll(save, cfg, bannerId))
                return new RollResult { Save = save, Rolled = false };

            var banner = cfg.Banners[bannerId];

            // Draw EXACTLY one rng value regardless of the outcome path (so pity-forced and normal rolls
            // advance the cursor identically). Pity, when it triggers, overrides the drawn pick with the
            // featured hero.
            var rng = new Rng(save.RngSeed, save.RngCursor);
            var entries = new List<(string item, double weight)>(banner.Pool.Count);
            foreach (var e in banner.Pool) entries.Add((e.HeroDefId, e.Weight));
            string drawn = rng.WeightedPick(entries);

            int pity = PityOf(save, bannerId);
            bool pityTriggered = banner.PityCount > 0 && pity + 1 >= banner.PityCount;
            string heroDefId = pityTriggered ? banner.FeaturedHeroDefId : drawn;
            bool isFeatured = heroDefId == banner.FeaturedHeroDefId;

            // Pity resets on ANY featured result (forced or natural), else increments.
            int nextPity = isFeatured ? 0 : pity + 1;
            var gachaPity = new Dictionary<string, int>(save.GachaPity) { [bannerId] = nextPity };

            // Spend the gems.
            var currencies = new Dictionary<string, long>(save.Currencies);
            currencies[cfg.Balance.PremiumCurrency] = Gems(save, cfg) - banner.CostGems;

            bool isNew = !save.Heroes.Exists(h => h.DefId == heroDefId);

            var result = new RollResult
            {
                Rolled = true,
                HeroDefId = heroDefId,
                IsNew = isNew,
                IsFeatured = isFeatured,
                PityTriggered = pityTriggered,
            };

            // Base save with the spend + pity + advanced cursor committed; the grant is layered on below.
            var next = new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = rng.Cursor, // persist so the roll can't be re-rolled
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = currencies,
                Progress = save.Progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                GachaPity = gachaPity,
                LastClaimAt = save.LastClaimAt,
            };

            if (isNew)
            {
                // Mint the hero via the shared acquisition path (same id scheme as SyncHeroUnlocks).
                string heroId = "h" + (next.Heroes.Count + 1);
                while (next.Heroes.Exists(h => h.Id == heroId)) heroId += "x";
                next = Party.AcquireHero(next, heroDefId, cfg, heroId);
                // Party.AcquireHero rebuilds the save from `next` — GachaPity/currencies carry through.
            }
            else
            {
                // Dupe: ascension shards to the rolled hero's wallet (10.17 — replaced the XP/scrap payout).
                // The grant is keyed by DEF, not instance, so it doesn't matter which owned copy rolled.
                result.DupeShards = cfg.Balance.AscensionShardsPerDupe;
                next = Ascension.GrantHeroShards(next, heroDefId, cfg.Balance.AscensionShardsPerDupe);
            }

            result.Save = next;
            return result;
        }
    }
}
