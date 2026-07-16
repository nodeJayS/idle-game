using System.Linq;
using IdleGame.BalanceSim;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// Smoke tests for the balance-sim scenario harness (gamecore/BalanceSim). The sim's value
    /// is that its verdicts describe the real game — so these pin the two properties that make
    /// that true: the synthetic save is assembled through the live reducers, and every run is
    /// deterministic under its cell seed.
    /// </summary>
    public class BalanceSimTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        [Fact]
        public void CellSeedIsDeterministicAndVariesPerCell()
        {
            Assert.Equal(Scenarios.CellSeed(1, 10, 20, 2, 0), Scenarios.CellSeed(1, 10, 20, 2, 0));
            Assert.NotEqual(Scenarios.CellSeed(1, 10, 20, 2, 0), Scenarios.CellSeed(1, 10, 20, 2, 1));
            Assert.NotEqual(Scenarios.CellSeed(1, 10, 20, 2, 0), Scenarios.CellSeed(1, 11, 20, 2, 0));
            Assert.NotEqual(Scenarios.CellSeed(1, 10, 20, 2, 0), Scenarios.CellSeed(2, 10, 20, 2, 0));
        }

        [Fact]
        public void BuildSaveGrantsUnlocksLevelsAndSpendsEveryPoint()
        {
            // Stage 30 => stages 1..29 cleared => the full unlock roster is in.
            var save = Scenarios.BuildSave(Cfg, 30, 100, null, 7);

            var party = Scenarios.FieldedParty(save);
            Assert.True(party.Count > 1, "stage-30 roster should have unlocked beyond the starter");
            Assert.All(party, h => Assert.Equal(100, h.Level));
            // Lv100 exactly maxes the 2+2 kit (CLAUDE §heroes), so auto-invest must land on 0 unspent.
            Assert.All(party, h => Assert.Equal(0, Skills.UnspentPoints(h, Cfg)));
        }

        [Fact]
        public void BuildSaveEquipsAFullSetOfTheRequestedRarity()
        {
            var save = Scenarios.BuildSave(Cfg, 15, 30, Rarity.Rare, 7);

            foreach (var hero in Scenarios.FieldedParty(save))
            {
                var items = Stats.ResolveEquipped(save, hero);
                Assert.Equal(EquipSlots.Active.Length, items.Count);
                Assert.All(items, i => Assert.Equal(Rarity.Rare, i.Rarity));
            }
        }

        [Fact]
        public void GearRaisesPartyPower()
        {
            var bare = Scenarios.BuildSave(Cfg, 15, 30, null, 7);
            var geared = Scenarios.BuildSave(Cfg, 15, 30, Rarity.Legendary, 7);
            Assert.True(Stats.ComputePartyPower(geared, Cfg) > Stats.ComputePartyPower(bare, Cfg));
        }

        [Fact]
        public void RunBossIsDeterministicUnderTheSameSeed()
        {
            var save = Scenarios.BuildSave(Cfg, 12, 15, Rarity.Normal, 42);
            var a = Scenarios.RunBoss(Cfg, save, 12, 42);
            var b = Scenarios.RunBoss(Cfg, save, 12, 42);
            Assert.Equal(a.Won, b.Won);
            Assert.Equal(a.Seconds, b.Seconds);
        }

        [Fact]
        public void CappedPartyClearsStageOneAndFreshPartyFailsDeepStage()
        {
            var strong = Scenarios.BuildSave(Cfg, 1, 100, Rarity.Legendary, 3);
            Assert.True(Scenarios.RunBoss(Cfg, strong, 1, 3).Won);

            var weak = Scenarios.BuildSave(Cfg, 50, 1, null, 3);
            Assert.False(Scenarios.RunBoss(Cfg, weak, 50, 3).Won);
        }

        [Fact]
        public void RunFarmMeasuresKillsWithinTheWindow()
        {
            var save = Scenarios.BuildSave(Cfg, 5, 60, Rarity.Rare, 9);
            var r = Scenarios.RunFarm(Cfg, save, 5, 30, 9);

            Assert.True(r.Kills > 0, "an over-leveled party should kill trash");
            Assert.True(r.WindowSeconds <= 30 + 0.001);
            Assert.True(r.HeroHits >= r.Kills, "every kill takes at least one hit");
            Assert.False(r.Wiped);
            Assert.True(r.Xp > 0);
        }

        [Fact]
        public void SinkHorizonClearsTheSixMonthTarget()
        {
            // 10.17 acceptance: the endgame spend horizon lasts ~6 months for a daily player. The binding
            // sink is gem-gated ascension (at maxed endgame the farm floods gold+scrap, so enhance/reforge
            // saturate in days). RETUNE CONSCIOUSLY — if this drops, the sinks got too cheap OR the income
            // model too generous (SinkModelParams / the new AscensionStar* constants).
            var h = Scenarios.ComputeSinkHorizon(Cfg, seed: 1);

            Assert.True(h.HorizonWeeks >= 20,
                $"endgame spend horizon {h.HorizonWeeks:0.0} wk is under the ~6-month (20-week) target");
            // Ascension is the long pole; enhance/reforge fall well inside it.
            Assert.True(h.StarWeeks >= h.EnhanceWeeks, "ascension should outlast the enhance sink");
            Assert.True(h.StarWeeks >= h.ReforgeWeeks, "ascension should outlast the reforge sink");
            // Sanity on the model: real positive income and a full-roster star cost.
            Assert.True(h.ShardsPerWeek > 0 && h.ScrapPerWeek > 0);
            Assert.True(h.StarShardCost > 0);
        }

        [Fact]
        public void MinLevelToClearFindsTheFrontierBoundary()
        {
            int? min = Scenarios.MinLevelToClear(Cfg, 20, Rarity.Normal, trials: 1, baseSeed: 5);

            Assert.True(min.HasValue, "stage 20 must be clearable at some level with normal gear");
            Assert.InRange(min!.Value, 1, Cfg.Balance.MaxLevel);
            // The frontier is real: the found level clears, and (if above 1) it beats going in bare
            // at the same level check one lower.
            Assert.True(Scenarios.ClearsBoss(Cfg, 20, min.Value, Rarity.Normal, 1, 5));
            if (min.Value > 1)
                Assert.False(Scenarios.ClearsBoss(Cfg, 20, min.Value - 1, Rarity.Normal, 1, 5));
        }
    }
}
