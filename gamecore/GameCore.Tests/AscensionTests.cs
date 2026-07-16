using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// Hero ascension (10.17, mobile arc MM5): the endgame shard sink. Per-hero star track — dupe-fed
    /// per-hero wallets + endless-milestone UNIVERSAL shards, spent hero-first; +4%/star hero-local stat
    /// fold. Covers the StarUp wallet math, CanStarUp gates, no-op ref-shares + purity, the +4% fold, the
    /// HeroInstance.Stars threading sweep, the endless-milestone universal-shard grant, and Migrate/round-trip.
    /// </summary>
    public class AscensionTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();
        private const long Now = 1_700_000_000_000;

        private static SaveState Fresh() => Save.NewGame(1, Cfg, Now);
        private static string StarterDef(SaveState s) => s.Heroes[0].DefId;
        private static string StarterId(SaveState s) => s.Heroes[0].Id;

        private static SaveState WithHeroShards(SaveState s, long hero, long universal)
        {
            s.Progress.Ascension.Shards[StarterDef(s)] = hero;
            s.Currencies[Ascension.UniversalShardCurrency] = universal;
            return s;
        }

        // ---- StarUp math ---------------------------------------------------------------

        [Fact]
        public void StarUpSpendsHeroWalletFirstThenUniversal()
        {
            var s = WithHeroShards(Fresh(), hero: 15, universal: 100);
            string id = StarterId(s), def = StarterDef(s);

            // Star 1 costs 10 (AscensionStarCosts[0]): drawn entirely from the hero wallet.
            var (s1, r1) = Ascension.StarUp(s, id, Cfg);
            Assert.NotNull(r1);
            Assert.Equal(def, r1!.Value.DefId);
            Assert.Equal(1, r1.Value.NewStars);
            Assert.Equal(10, r1.Value.SpentHero);
            Assert.Equal(0, r1.Value.SpentUniversal);
            Assert.Equal(1, s1.Heroes.First(h => h.Id == id).Stars);
            Assert.Equal(5, Ascension.ShardsFor(s1, def));      // 15 - 10
            Assert.Equal(100, Ascension.UniversalShards(s1));   // untouched

            // Star 2 costs 20: 5 left in the hero wallet, so 5 hero + 15 universal (multi-source).
            var (s2, r2) = Ascension.StarUp(s1, id, Cfg);
            Assert.Equal(2, r2!.Value.NewStars);
            Assert.Equal(5, r2.Value.SpentHero);
            Assert.Equal(15, r2.Value.SpentUniversal);
            Assert.Equal(0, Ascension.ShardsFor(s2, def));      // wallet emptied (key removed)
            Assert.False(s2.Progress.Ascension.Shards.ContainsKey(def));
            Assert.Equal(85, Ascension.UniversalShards(s2));    // 100 - 15
            Assert.Equal(2, s2.Heroes.First(h => h.Id == id).Stars);
        }

        [Fact]
        public void StarUpFromUniversalOnlyWhenHeroWalletEmpty()
        {
            var s = WithHeroShards(Fresh(), hero: 0, universal: 50);
            var (n, r) = Ascension.StarUp(s, StarterId(s), Cfg);
            Assert.Equal(0, r!.Value.SpentHero);
            Assert.Equal(10, r.Value.SpentUniversal);
            Assert.Equal(40, Ascension.UniversalShards(n));
        }

        [Fact]
        public void StarUpIsPure_InputUntouched()
        {
            var s = WithHeroShards(Fresh(), hero: 100, universal: 0);
            var (n, _) = Ascension.StarUp(s, StarterId(s), Cfg);
            Assert.NotSame(s, n);
            Assert.Equal(0, s.Heroes[0].Stars);                 // input hero unchanged
            Assert.Equal(100, Ascension.ShardsFor(s, StarterDef(s))); // input wallet unchanged
        }

        // ---- gates / no-ops ------------------------------------------------------------

        [Fact]
        public void CannotAffordIsANoOpShareTheRef()
        {
            var s = WithHeroShards(Fresh(), hero: 4, universal: 5); // 9 < 10, the first star's cost
            Assert.False(Ascension.CanStarUp(s, StarterId(s), Cfg));
            var (n, r) = Ascension.StarUp(s, StarterId(s), Cfg);
            Assert.Null(r);
            Assert.Same(s, n);
        }

        [Fact]
        public void UnknownHeroIsANoOp()
        {
            var s = WithHeroShards(Fresh(), hero: 1000, universal: 1000);
            var (n, r) = Ascension.StarUp(s, "nope", Cfg);
            Assert.Null(r);
            Assert.Same(s, n);
            Assert.False(Ascension.CanStarUp(s, "nope", Cfg));
        }

        [Fact]
        public void MaxedHeroCannotStarUpAndSharesTheRef()
        {
            var s = WithHeroShards(Fresh(), hero: 100_000, universal: 100_000);
            string id = StarterId(s);
            for (int i = 0; i < Cfg.Balance.AscensionMaxStars; i++)
                (s, _) = Ascension.StarUp(s, id, Cfg);
            Assert.Equal(Cfg.Balance.AscensionMaxStars, s.Heroes.First(h => h.Id == id).Stars);
            Assert.False(Ascension.CanStarUp(s, id, Cfg));
            Assert.Equal(-1, Ascension.NextStarCost(Cfg.Balance.AscensionMaxStars, Cfg));

            var (n, r) = Ascension.StarUp(s, id, Cfg); // one past the cap
            Assert.Null(r);
            Assert.Same(s, n);
        }

        [Fact]
        public void TotalSpendToMaxEqualsTheSumOfStarCosts()
        {
            long total = Cfg.Balance.AscensionStarCosts.Sum();
            var s = WithHeroShards(Fresh(), hero: 0, universal: total);
            string id = StarterId(s);
            for (int i = 0; i < Cfg.Balance.AscensionMaxStars; i++)
                (s, _) = Ascension.StarUp(s, id, Cfg);
            Assert.Equal(0, Ascension.UniversalShards(s));           // exact drain — no over/undercharge
            Assert.Equal(Cfg.Balance.AscensionMaxStars, s.Heroes.First(h => h.Id == id).Stars);
        }

        // ---- the +4%/star fold ---------------------------------------------------------

        [Fact]
        public void StarsFoldFourPercentPerStarHeroLocal()
        {
            var s = Fresh();
            var hero0 = s.Heroes[0];
            var baseStats = Stats.ComputeHeroStats(hero0, Cfg);

            hero0.Stars = 3; // +12%
            var starred = Stats.ComputeHeroStats(hero0, Cfg);
            double mult = 1.0 + 3 * Cfg.Balance.AscensionStarPct;
            Assert.Equal(baseStats.Get(StatKey.Hp) * mult, starred.Get(StatKey.Hp), 6);
            Assert.Equal(baseStats.Get(StatKey.Atk) * mult, starred.Get(StatKey.Atk), 6);
            Assert.Equal(baseStats.Get(StatKey.Def) * mult, starred.Get(StatKey.Def), 6);
        }

        [Fact]
        public void ZeroStarsIsANoOpOnStats()
        {
            var s = Fresh();
            var hero0 = s.Heroes[0];
            var a = Stats.ComputeHeroStats(hero0, Cfg);
            Assert.Equal(0, hero0.Stars);
            var b = Stats.ComputeHeroStats(hero0, Cfg);
            Assert.Equal(a.Get(StatKey.Hp), b.Get(StatKey.Hp), 9); // identical — no fold at 0 stars
        }

        // ---- HeroInstance.Stars threading sweep ----------------------------------------

        [Fact]
        public void StarsSurviveEveryHeroCopyingReducer()
        {
            // Mirrors LoadoutTests' snapshot sweep: a dropped Stars in any HeroInstance copy site would
            // silently reset ascension. Drive a starred hero through every hero-rebuilding reducer.
            var save = Progression.GrantPartyXp(Fresh(), 200_000, Cfg); // level the party so points exist
            string id = StarterId(save);
            save.Heroes.First(h => h.Id == id).Stars = 2;

            int Stars(SaveState s) => s.Heroes.First(h => h.Id == id).Stars;

            // WithLevel (Progression) — more XP levels the hero again.
            Assert.Equal(2, Stars(Progression.GrantPartyXp(save, 50_000, Cfg)));

            // CloneHero (Inventory.EquipItem).
            var equipped = Inventory.AddItems(save, new[] { new Item { Id = "cap", BaseId = "leather_cap", Rarity = Rarity.Normal, ItemLevel = 1 } });
            equipped = Inventory.EquipItem(equipped, id, "cap", Cfg);
            Assert.Equal(2, Stars(equipped));

            // WithHero (Loadouts.SaveSnapshot).
            Assert.Equal(2, Stars(Loadouts.SaveSnapshot(equipped, id)));

            // WithRanks (Skills.InvestSkill) — invest the first investable kit node.
            var def = Cfg.Heroes[StarterDef(save)];
            string skill = def.Skills.First(sk => Skills.CanInvest(save, id, sk, Cfg));
            Assert.Equal(2, Stars(Skills.InvestSkill(save, id, skill, Cfg)));

            // And StarUp itself bumps this hero's stars through its own copy (star 3 from 2 costs 30).
            var withShards = WithHeroShards(save, hero: 30, universal: 0);
            var (upped, _) = Ascension.StarUp(withShards, id, Cfg);
            Assert.Equal(3, Stars(upped)); // 2 -> 3
        }

        // ---- endless milestone pays UNIVERSAL shards -----------------------------------

        [Fact]
        public void EndlessMilestonePaysUniversalShardsBesideGems()
        {
            var s = Fresh();
            s.Progress.HighestStage = Cfg.Stages.Count; // in endless
            s.Progress.EndlessBest = 4;                  // next depth (5) is the every-5th milestone
            long gems0 = s.Currencies.GetValueOrDefault(Cfg.Balance.PremiumCurrency);
            long shards0 = Ascension.UniversalShards(s);

            var n = Progression.OnStageCleared(s, Cfg.Stages.Count + 5, Cfg); // endlessBest 4 -> 5 (milestone)
            Assert.Equal(5, n.Progress.EndlessBest);
            Assert.Equal(gems0 + Cfg.Balance.EndlessGemsPerMilestone,
                         n.Currencies.GetValueOrDefault(Cfg.Balance.PremiumCurrency));
            Assert.Equal(shards0 + Cfg.Balance.AscensionShardsPerEndlessMilestone, Ascension.UniversalShards(n));
        }

        [Fact]
        public void NonMilestoneEndlessDepthPaysNoShards()
        {
            var s = Fresh();
            s.Progress.HighestStage = Cfg.Stages.Count;
            s.Progress.EndlessBest = 5;                  // next depth (6) is NOT a milestone
            var n = Progression.OnStageCleared(s, Cfg.Stages.Count + 6, Cfg);
            Assert.Equal(6, n.Progress.EndlessBest);
            Assert.Equal(0, Ascension.UniversalShards(n));
        }

        // ---- migrate + round-trip ------------------------------------------------------

        [Fact]
        public void MigrateBackfillsAscensionOnOlderSaves()
        {
            var s = Fresh();
            s.Progress.Ascension = null!;        // simulate a pre-10.17 payload
            var migrated = Save.Migrate(s);
            Assert.NotNull(migrated.Progress.Ascension);
            Assert.NotNull(migrated.Progress.Ascension.Shards);
            Assert.Empty(migrated.Progress.Ascension.Shards);
        }

        [Fact]
        public void StarsAndShardsSurviveSaveRoundTrip()
        {
            var s = Fresh();
            s.Heroes[0].Stars = 4;
            s.Progress.Ascension.Shards[StarterDef(s)] = 42;
            s.Currencies[Ascension.UniversalShardCurrency] = 77;

            var reloaded = Persistence.Deserialize(Persistence.Serialize(s));
            Assert.Equal(4, reloaded.Heroes[0].Stars);
            Assert.Equal(42, Ascension.ShardsFor(reloaded, StarterDef(s)));
            Assert.Equal(77, Ascension.UniversalShards(reloaded));
        }

        [Fact]
        public void NewGameStartsUnascended()
        {
            var s = Fresh();
            Assert.All(s.Heroes, h => Assert.Equal(0, h.Stars));
            Assert.Empty(s.Progress.Ascension.Shards);
            Assert.Equal(0, Ascension.UniversalShards(s));
        }
    }
}
