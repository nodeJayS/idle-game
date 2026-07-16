using System;
using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// Gacha.Roll (roadmap 3, slice 1): the gem SINK. Covers the spend, persisted-cursor determinism
    /// (no re-roll on reload), weighted picks, pity (forced featured + resets), new-hero minting, dupe →
    /// ascension shards (10.17 — replaced the old XP/scrap payout), the SyncHeroUnlocks interaction, and
    /// the additive-field backfill.
    /// </summary>
    public class GachaTests
    {
        private const string Featured = "icemage_basic";  // banner headliner (shelved def, a real pool hero)
        private const string Filler = "priest_basic";     // the off-banner pool hero
        private const long Cost = 100;
        private const int Pity = 10;

        // A config whose only banner is a 2-hero pool. Built off Default() so hero defs resolve.
        private static GameConfig CfgWithBanner(double featuredWeight = 1, double fillerWeight = 1)
        {
            var cfg = GameConfig.Default();
            cfg.Banners["b1"] = new GachaBannerDef
            {
                Id = "b1", Name = "Test Banner", CostGems = Cost, FeaturedHeroDefId = Featured,
                PityCount = Pity,
                Pool = new List<GachaPoolEntry>
                {
                    new GachaPoolEntry { HeroDefId = Featured, Weight = featuredWeight },
                    new GachaPoolEntry { HeroDefId = Filler, Weight = fillerWeight },
                },
            };
            return cfg;
        }

        private static readonly GameConfig Cfg = CfgWithBanner();

        // A fresh starter-only save (Knight in slot 0) with `gems` premium currency to spend.
        private static SaveState SaveWithGems(long gems, uint seed = 1)
        {
            var save = Save.NewGame(seed, Cfg, 0);
            save.Currencies[Cfg.Balance.PremiumCurrency] = gems;
            return save;
        }

        private static long GemsOf(SaveState s) => s.Currencies.GetValueOrDefault("gems");

        // ---- cost / affordability ---------------------------------------------------------------

        [Fact]
        public void RollSpendsExactlyTheBannerCost()
        {
            var save = SaveWithGems(1000);
            var r = Gacha.Roll(save, Cfg, "b1");

            Assert.True(r.Rolled);
            Assert.Equal(1000 - Cost, GemsOf(r.Save));
            Assert.Equal(1000, GemsOf(save)); // input untouched (pure)
        }

        [Fact]
        public void CannotAffordIsANoOp()
        {
            var save = SaveWithGems(Cost - 1);
            Assert.False(Gacha.CanRoll(save, Cfg, "b1"));

            var r = Gacha.Roll(save, Cfg, "b1");
            Assert.False(r.Rolled);
            Assert.Same(save, r.Save);            // shares the ref, no spend
            Assert.Equal(Cost - 1, GemsOf(r.Save));
            Assert.Equal(save.Heroes.Count, r.Save.Heroes.Count);
        }

        [Fact]
        public void UnknownBannerIsANoOp()
        {
            var save = SaveWithGems(1000);
            Assert.False(Gacha.CanRoll(save, Cfg, "nope"));
            var r = Gacha.Roll(save, Cfg, "nope");
            Assert.False(r.Rolled);
            Assert.Same(save, r.Save);
        }

        [Fact]
        public void ExactlyAffordableCanRoll()
        {
            var save = SaveWithGems(Cost);
            Assert.True(Gacha.CanRoll(save, Cfg, "b1"));
            var r = Gacha.Roll(save, Cfg, "b1");
            Assert.True(r.Rolled);
            Assert.Equal(0, GemsOf(r.Save));
        }

        // ---- determinism / no re-roll ------------------------------------------------------------

        [Fact]
        public void SameSaveAndSeedGivesIdenticalOutcome()
        {
            var a = Gacha.Roll(SaveWithGems(1000, seed: 42), Cfg, "b1");
            var b = Gacha.Roll(SaveWithGems(1000, seed: 42), Cfg, "b1");

            Assert.Equal(a.HeroDefId, b.HeroDefId);
            Assert.Equal(a.IsNew, b.IsNew);
            Assert.Equal(a.Save.RngCursor, b.Save.RngCursor);
        }

        [Fact]
        public void CursorAdvancesSoSequentialRollsCanDiffer()
        {
            var save = SaveWithGems(100000, seed: 7);
            var seen = new List<string>();
            int cursor = save.RngCursor;
            for (int i = 0; i < 30; i++)
            {
                var r = Gacha.Roll(save, Cfg, "b1");
                Assert.True(r.Save.RngCursor > cursor); // cursor moved forward each roll
                cursor = r.Save.RngCursor;
                seen.Add(r.HeroDefId);
                save = r.Save;
            }
            // A 50/50 pool over 30 rolls must have produced both heroes at least once.
            Assert.Contains(Featured, seen);
            Assert.Contains(Filler, seen);
        }

        [Fact]
        public void RollingTwiceFromTheSamePreRollSaveCannotReRoll()
        {
            // THE anti-re-roll guarantee: a reload (same pre-roll save) can't fish for a better hero.
            var save = SaveWithGems(1000, seed: 99);
            var first = Gacha.Roll(save, Cfg, "b1");
            var again = Gacha.Roll(save, Cfg, "b1"); // same input save — a "reload"

            Assert.Equal(first.HeroDefId, again.HeroDefId);
            Assert.Equal(first.Save.RngCursor, again.Save.RngCursor);
        }

        // ---- weighted distribution sanity --------------------------------------------------------

        [Fact]
        public void FeaturedRateTracksItsWeightWithinLooseBounds()
        {
            // 1:3 featured:filler with pity disabled → ~25% featured. Loose bounds (deterministic seed).
            var cfg = CfgWithBanner(featuredWeight: 1, fillerWeight: 3);
            cfg.Banners["b1"].PityCount = 0; // isolate raw weights from pity forcing
            var save = Save.NewGame(12345, cfg, 0);
            save.Currencies["gems"] = 1_000_000;

            int featured = 0, n = 4000;
            for (int i = 0; i < n; i++)
            {
                var r = Gacha.Roll(save, cfg, "b1");
                if (r.HeroDefId == Featured) featured++;
                save = r.Save;
            }
            double rate = (double)featured / n;
            Assert.InRange(rate, 0.20, 0.30); // expected 0.25
        }

        // ---- pity --------------------------------------------------------------------------------

        [Fact]
        public void PityForcesFeaturedAtExactlyPityCountAndResets()
        {
            // Filler-only weights so the featured hero is NEVER drawn naturally — only pity can force it.
            var cfg = CfgWithBanner(featuredWeight: 0.0001, fillerWeight: 1000);
            var save = Save.NewGame(3, cfg, 0);
            save.Currencies["gems"] = 1_000_000;

            for (int i = 1; i <= Pity; i++)
            {
                var r = Gacha.Roll(save, cfg, "b1");
                save = r.Save;
                if (i < Pity)
                {
                    Assert.False(r.PityTriggered);
                    Assert.NotEqual(Featured, r.HeroDefId);   // filler
                    Assert.Equal(i, Gacha.PityOf(save, "b1")); // counter climbs
                }
                else
                {
                    Assert.True(r.PityTriggered);              // the PityCount-th roll is forced
                    Assert.Equal(Featured, r.HeroDefId);
                    Assert.True(r.IsFeatured);
                    Assert.Equal(0, Gacha.PityOf(save, "b1")); // reset
                }
            }
        }

        [Fact]
        public void NaturalFeaturedResetsPity()
        {
            // Featured-heavy weights: an early roll draws it naturally (not via pity) and resets to 0.
            var cfg = CfgWithBanner(featuredWeight: 1000, fillerWeight: 0.0001);
            var save = Save.NewGame(5, cfg, 0);
            save.Currencies["gems"] = 1_000_000;

            var r = Gacha.Roll(save, cfg, "b1");
            Assert.Equal(Featured, r.HeroDefId);
            Assert.False(r.PityTriggered);         // drawn naturally, well before PityCount
            Assert.True(r.IsFeatured);
            Assert.Equal(0, Gacha.PityOf(r.Save, "b1"));
        }

        // ---- new hero ----------------------------------------------------------------------------

        [Fact]
        public void NewHeroLandsInSaveHeroesExactlyOnce()
        {
            var cfg = CfgWithBanner(featuredWeight: 1000, fillerWeight: 0.0001);
            var save = Save.NewGame(5, cfg, 0);
            save.Currencies["gems"] = 1000;

            var r = Gacha.Roll(save, cfg, "b1");
            Assert.True(r.IsNew);
            Assert.Equal(Featured, r.HeroDefId);
            Assert.Single(r.Save.Heroes, h => h.DefId == Featured);
            Assert.Equal(save.Heroes.Count + 1, r.Save.Heroes.Count);
            // Minted fresh, not auto-fielded.
            var minted = r.Save.Heroes.First(h => h.DefId == Featured);
            Assert.Equal(1, minted.Level);
            Assert.DoesNotContain(r.Save.Party, id => id == minted.Id);
        }

        // ---- dupe --------------------------------------------------------------------------------

        [Fact]
        public void DupeGrantsHeroShardsAndAddsNoDuplicateHero()
        {
            var cfg = CfgWithBanner(featuredWeight: 1000, fillerWeight: 0.0001);
            var save = Save.NewGame(5, cfg, 0);
            save.Currencies["gems"] = 1000;

            var first = Gacha.Roll(save, cfg, "b1"); // new Ice Mage
            Assert.True(first.IsNew);
            long shardsBefore = Ascension.ShardsFor(first.Save, Featured);
            long xpBefore = first.Save.Heroes.First(h => h.DefId == Featured).Xp;
            int heroCount = first.Save.Heroes.Count;

            var dupe = Gacha.Roll(first.Save, cfg, "b1"); // dupe Ice Mage
            Assert.False(dupe.IsNew);
            Assert.Equal(Featured, dupe.HeroDefId);
            Assert.Equal(cfg.Balance.AscensionShardsPerDupe, dupe.DupeShards); // shards announced

            Assert.Equal(heroCount, dupe.Save.Heroes.Count);                 // no duplicate instance
            Assert.Single(dupe.Save.Heroes, h => h.DefId == Featured);
            // Shards credited to THAT hero's wallet — not XP/scrap (the 10.17 swap).
            Assert.Equal(shardsBefore + cfg.Balance.AscensionShardsPerDupe, Ascension.ShardsFor(dupe.Save, Featured));
            Assert.Equal(xpBefore, dupe.Save.Heroes.First(h => h.DefId == Featured).Xp); // XP untouched now
        }

        // ---- SyncHeroUnlocks interaction (the DANGER) --------------------------------------------

        [Fact]
        public void SyncHeroUnlocksKeepsAGachaGrantedHeroWhoseBannerIsLive()
        {
            // A gacha-granted hero has NO stage unlock. Without the banner-pool fix, SyncHeroUnlocks
            // would SHELVE it (same path that shelves the Ice Mage). With a live banner it stays.
            var cfg = CfgWithBanner(featuredWeight: 1000, fillerWeight: 0.0001);
            var save = Save.NewGame(5, cfg, 0);
            save.Currencies["gems"] = 1000;

            var r = Gacha.Roll(save, cfg, "b1"); // grants Ice Mage
            Assert.Contains(r.Save.Heroes, h => h.DefId == Featured);
            int before = r.Save.Heroes.Count;

            var synced = Progression.SyncHeroUnlocks(r.Save, cfg);
            Assert.Contains(synced.Heroes, h => h.DefId == Featured);       // NOT removed
            Assert.Equal(before, synced.Heroes.Count);                       // NOT duplicated

            // Idempotent: a second sync is a no-op on the roster.
            var twice = Progression.SyncHeroUnlocks(synced, cfg);
            Assert.Equal(before, twice.Heroes.Count);
            Assert.Single(twice.Heroes, h => h.DefId == Featured);
        }

        [Fact]
        public void GachaGrantedHeroIsShelvedIfItsBannerLeavesConfig()
        {
            // Sanity on the mechanism: with NO banner in config, the Ice Mage IS shelved — i.e.
            // the protection comes strictly from the live banner pool, not a permanent grant.
            // (Default() now ships the live "Winter's Return" banner with the Ice Mage in its
            // pool, so we strip ALL banners to get a banner-less config rather than using it.)
            var cfg = CfgWithBanner(featuredWeight: 1000, fillerWeight: 0.0001);
            var save = Save.NewGame(5, cfg, 0);
            save.Currencies["gems"] = 1000;
            var r = Gacha.Roll(save, cfg, "b1");
            Assert.Contains(r.Save.Heroes, h => h.DefId == Featured);

            var noBannerCfg = GameConfig.Default();
            noBannerCfg.Banners.Clear();           // banner gone
            var synced = Progression.SyncHeroUnlocks(r.Save, noBannerCfg);
            Assert.DoesNotContain(synced.Heroes, h => h.DefId == Featured); // shelved as designed
        }

        // ---- backfill ----------------------------------------------------------------------------

        [Fact]
        public void MigrateBackfillsGachaPityOnOlderSaves()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save.GachaPity = null!; // simulate a pre-gacha payload
            var migrated = Save.Migrate(save);
            Assert.NotNull(migrated.GachaPity);
            Assert.Empty(migrated.GachaPity);
        }

        [Fact]
        public void RollOnASaveMissingGachaStateDoesNotCrash()
        {
            var save = SaveWithGems(1000);
            save.GachaPity = null!;              // an un-migrated save reaching a roll
            var migrated = Save.Migrate(save);   // load path backfills it
            var r = Gacha.Roll(migrated, Cfg, "b1");
            Assert.True(r.Rolled);
            Assert.NotNull(r.Save.GachaPity);
            Assert.InRange(Gacha.PityOf(r.Save, "b1"), 0, 1); // 0 if the roll was featured, else 1 — coherent either way
        }

        [Fact]
        public void NewGameStartsWithEmptyPity()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.NotNull(save.GachaPity);
            Assert.Empty(save.GachaPity);
            Assert.Equal(0, Gacha.PityOf(save, "b1"));
        }

        // ---- the LIVE "Winter's Return" banner in Default() (slice 3) -----------------------------

        [Fact]
        public void DefaultShipsWintersReturnWithTheExactEconomy()
        {
            var cfg = GameConfig.Default();
            Assert.True(cfg.Banners.TryGetValue("winters_return", out var banner));

            Assert.Equal("winters_return", banner!.Id);
            Assert.Equal("Winter's Return", banner.Name);
            Assert.Equal(10, banner.CostGems);              // one day-1 daily login
            Assert.Equal("icemage_basic", banner.FeaturedHeroDefId);
            Assert.Equal(20, banner.PityCount);
            // (Dupe payout is now universal — BalanceConstants.AscensionShardsPerDupe, 10.17 — not per-banner.)
        }

        [Fact]
        public void WintersReturnPoolHasTheExactWeightsAndAllHeroesExist()
        {
            var cfg = GameConfig.Default();
            var banner = cfg.Banners["winters_return"];

            var weights = banner.Pool.ToDictionary(e => e.HeroDefId, e => e.Weight);
            Assert.Equal(5, banner.Pool.Count);
            Assert.Equal(1, weights["icemage_basic"]);
            Assert.Equal(3, weights["warrior_basic"]);
            Assert.Equal(3, weights["magician_basic"]);
            Assert.Equal(3, weights["thief_basic"]);
            Assert.Equal(3, weights["priest_basic"]);

            // Every pool hero resolves to a real HeroDef (a bad id would crash a real roll).
            foreach (var entry in banner.Pool)
                Assert.True(cfg.Heroes.ContainsKey(entry.HeroDefId), $"pool hero missing: {entry.HeroDefId}");

            // Natural featured rate ≈ 1/13 ≈ 7.7% (weight 1 vs total 13).
            double total = banner.Pool.Sum(e => e.Weight);
            Assert.Equal(1.0 / 13.0, weights["icemage_basic"] / total, 4);
        }

        [Fact]
        public void WintersReturnFeaturedHeroIsInItsOwnPool()
        {
            var cfg = GameConfig.Default();
            var banner = cfg.Banners["winters_return"];
            Assert.Contains(banner.Pool, e => e.HeroDefId == banner.FeaturedHeroDefId);
        }

        [Fact]
        public void IcemageIsObtainableUnderDefaultConfigAndSurvivesASync()
        {
            // With the LIVE Default() banner, a roll-granted Ice Mage stays through SyncHeroUnlocks
            // (banner-pool heroes are obtainable while the banner is live). No test fixture here —
            // this exercises the real shipped config end to end.
            var cfg = GameConfig.Default();
            var save = Save.NewGame(5, cfg, 0);
            save.Currencies["gems"] = 1000;

            // Force the featured pull deterministically without editing the shipped weights.
            cfg.Banners["winters_return"].PityCount = 1;
            var r = Gacha.Roll(save, cfg, "winters_return");
            Assert.Equal("icemage_basic", r.HeroDefId);
            Assert.Contains(r.Save.Heroes, h => h.DefId == "icemage_basic");

            var synced = Progression.SyncHeroUnlocks(r.Save, cfg);
            Assert.Contains(synced.Heroes, h => h.DefId == "icemage_basic"); // NOT shelved
        }
    }
}
