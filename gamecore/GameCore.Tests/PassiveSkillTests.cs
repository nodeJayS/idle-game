using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Passive skill nodes (2+2 kit, §7.2): passives are in the kit + investable but never
    /// cast; each rank folds into Stats.ComputeHeroStats (and thus the Lever 2 power compare),
    /// with the MaxRank mastery bump. Rank 0 = +0.</summary>
    public class PassiveSkillTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        // A fielded solo warrior granted enough XP to have leveled (and earned skill points).
        private static (SaveState save, string heroId) LeveledWarrior(int grantXp = 2_000_000)
        {
            var save = Progression.GrantPartyXp(Save.NewGame(1, Cfg, 0), grantXp, Cfg);
            return (save, save.Heroes[0].Id);
        }

        private static HeroInstance Hero(SaveState s, string id) => s.Heroes.First(h => h.Id == id);

        // ---- classification ----

        [Fact]
        public void PassivesAreKnownButExcludedFromActives()
        {
            var (save, id) = LeveledWarrior();
            var hero = Hero(save, id);

            Assert.True(Skills.IsPassive("toughness", Cfg));
            Assert.False(Skills.IsPassive("cleave", Cfg));

            Assert.Contains("toughness", Skills.Known(hero, Cfg));
            Assert.DoesNotContain("toughness", Skills.KnownActive(hero, Cfg));
            Assert.Contains("toughness", Skills.KnownPassive(hero, Cfg));
            Assert.Contains("cleave", Skills.KnownActive(hero, Cfg));
        }

        [Fact]
        public void ActiveKitNeverContainsPassives()
        {
            var (save, id) = LeveledWarrior();
            var kit = Skills.ActiveKit(Hero(save, id), Cfg);
            Assert.NotEmpty(kit);
            Assert.DoesNotContain(kit, sk => Skills.IsPassive(sk, Cfg));
        }

        // ---- investing (reuses the slice-1 rank spine) ----

        [Fact]
        public void CanInvestInAPassive()
        {
            var (save, id) = LeveledWarrior();
            Assert.True(Skills.CanInvest(save, id, "toughness", Cfg));

            var next = Skills.InvestSkill(save, id, "toughness", Cfg);
            Assert.Equal(1, Skills.RankOf(Hero(next, id), "toughness"));
        }

        // ---- stat folding (the slice-2 payoff) ----

        [Fact]
        public void RankedPassiveRaisesTheComputedStat()
        {
            var (save, id) = LeveledWarrior();
            double baseDef = Stats.ComputeHeroStats(Hero(save, id), Cfg).Get(StatKey.Def);

            save = Skills.InvestSkill(save, id, "toughness", Cfg);
            save = Skills.InvestSkill(save, id, "toughness", Cfg); // rank 2

            double perRank = Cfg.Skills["toughness"].StatPerRank;
            double rankedDef = Stats.ComputeHeroStats(Hero(save, id), Cfg).Get(StatKey.Def);
            Assert.Equal(baseDef + 2 * perRank, rankedDef, 6);
        }

        [Fact]
        public void MaxRankPassiveMasters()
        {
            // At MaxRank (5) the passive counts as rank 5 + MasteryBonusRanks (2) = 7 ranks of stat.
            var (save, id) = LeveledWarrior();
            double baseDef = Stats.ComputeHeroStats(Hero(save, id), Cfg).Get(StatKey.Def);

            var sk = Cfg.Skills["toughness"];
            for (int i = 0; i < sk.MaxRank; i++) save = Skills.InvestSkill(save, id, "toughness", Cfg);

            double expected = baseDef + (sk.MaxRank + Cfg.Balance.MasteryBonusRanks) * sk.StatPerRank;
            Assert.Equal(expected, Stats.ComputeHeroStats(Hero(save, id), Cfg).Get(StatKey.Def), 6);
        }

        [Fact]
        public void Rank0PassiveLeavesStatsUnchanged()
        {
            // Back-compat: a hero who never invests sees exactly the pre-slice-2 stat sheet.
            var (save, id) = LeveledWarrior();
            var hero = Hero(save, id);
            var def = Cfg.Heroes[hero.DefId];

            double expectedDef = def.BaseStats.Get(StatKey.Def)
                               + def.GrowthPerLevel.Get(StatKey.Def) * (hero.Level - 1);
            Assert.Equal(expectedDef, Stats.ComputeHeroStats(hero, Cfg).Get(StatKey.Def), 6);
        }

        // ---- flows into the Lever 2 power compare for free ----

        [Fact]
        public void PassiveFlowsIntoPowerScore()
        {
            // Vitality (+Hp) always lifts Effective Life, so the Lever 2 power scalar must rise —
            // proving a ranked passive reaches the compare with no extra wiring.
            var (save, id) = LeveledWarrior();
            double before = Upgrades.PowerScore(Stats.ComputeHeroStats(Hero(save, id), Cfg), Cfg, 1);

            for (int i = 0; i < Cfg.Skills["vitality"].MaxRank; i++)
                save = Skills.InvestSkill(save, id, "vitality", Cfg);

            double after = Upgrades.PowerScore(Stats.ComputeHeroStats(Hero(save, id), Cfg), Cfg, 1);
            Assert.True(after > before, $"expected power to rise with a +Hp passive ({before} -> {after})");
        }
    }
}
