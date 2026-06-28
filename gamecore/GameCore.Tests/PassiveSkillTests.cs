using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Lever 3 slice 2: passive skill nodes. Passives are known + investable but never
    /// slotted in the active bar; each rank folds into Stats.ComputeHeroStats (and thus the Lever 2
    /// power compare). Rank 0 = +0 = today's behavior.</summary>
    public class PassiveSkillTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        // A fielded solo warrior granted enough XP to have leveled (and earned skill points).
        private static (SaveState save, string heroId) LeveledWarrior(int grantXp = 200000)
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
        public void DefaultLoadoutHasNoPassivesAndStaysFull()
        {
            var def = Cfg.Heroes["warrior_basic"];
            var loadout = Skills.DefaultLoadout(def, Cfg);

            Assert.Equal(Cfg.Balance.MaxActiveSkills, loadout.Count);
            Assert.DoesNotContain(loadout, id => Skills.IsPassive(id, Cfg));
        }

        // ---- can't be slotted in the active bar ----

        [Fact]
        public void TogglingAPassiveIsANoOp()
        {
            var (save, id) = LeveledWarrior();
            Assert.Same(save, Skills.ToggleSkill(save, id, "toughness", Cfg));
        }

        [Fact]
        public void SetLoadoutRejectsAPassive()
        {
            var (save, id) = LeveledWarrior();
            Assert.Throws<System.InvalidOperationException>(
                () => Skills.SetLoadout(save, id, new[] { "cleave", "toughness" }, Cfg));
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
