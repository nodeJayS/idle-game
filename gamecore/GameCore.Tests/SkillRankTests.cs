using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Lever 3 build depth: skill points earned from leveling, spent to rank up skills,
    /// scaling their combat magnitude. Rank 0 must reproduce today's behavior (back-compat).</summary>
    public class SkillRankTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        // A fielded solo warrior granted enough XP to have leveled (and thus earned skill points).
        private static (SaveState save, string heroId) LeveledWarrior(long grantXp = 2_000_000)
        {
            var save = Progression.GrantPartyXp(Save.NewGame(1, Cfg, 0), grantXp, Cfg);
            return (save, save.Heroes[0].Id);
        }

        private static HeroInstance Hero(SaveState s, string id) => s.Heroes.First(h => h.Id == id);

        // ---- point math ----

        [Fact]
        public void PointsEarnedIsOnePerLevelGained()
        {
            Assert.Equal(0, Skills.PointsEarned(new HeroInstance { Level = 1 }, Cfg));
            Assert.Equal(9 * Cfg.Balance.SkillPointsPerLevel, Skills.PointsEarned(new HeroInstance { Level = 10 }, Cfg));
        }

        [Fact]
        public void UnspentReflectsLevelMinusSpending()
        {
            var (save, id) = LeveledWarrior();
            int earned = Skills.PointsEarned(Hero(save, id), Cfg);
            Assert.True(earned > 0);
            Assert.Equal(earned, Skills.UnspentPoints(Hero(save, id), Cfg)); // nothing spent yet

            save = Skills.InvestSkill(save, id, "cleave", Cfg);
            Assert.Equal(earned - 1, Skills.UnspentPoints(Hero(save, id), Cfg));
        }

        // ---- invest / respec ----

        [Fact]
        public void InvestIncrementsRankAndConsumesAPoint()
        {
            var (save, id) = LeveledWarrior();
            int before = Skills.UnspentPoints(Hero(save, id), Cfg);

            var next = Skills.InvestSkill(save, id, "cleave", Cfg);

            Assert.Equal(1, Skills.RankOf(Hero(next, id), "cleave"));
            Assert.Equal(before - 1, Skills.UnspentPoints(Hero(next, id), Cfg));
            Assert.Equal(0, Skills.RankOf(Hero(save, id), "cleave")); // input untouched (pure)
        }

        [Fact]
        public void CannotInvestWithoutPoints()
        {
            var save = Save.NewGame(1, Cfg, 0);   // level 1 -> 0 points
            var id = save.Heroes[0].Id;
            Assert.False(Skills.CanInvest(save, id, "cleave", Cfg));
            Assert.Same(save, Skills.InvestSkill(save, id, "cleave", Cfg)); // no-op shares the ref
        }

        [Fact]
        public void CannotInvestAnUnknownSkill()
        {
            var (save, id) = LeveledWarrior();
            Assert.False(Skills.CanInvest(save, id, "firebolt", Cfg)); // warrior doesn't know it
            Assert.Same(save, Skills.InvestSkill(save, id, "firebolt", Cfg));
        }

        [Fact]
        public void CannotExceedMaxRank()
        {
            var (save, id) = LeveledWarrior();
            int max = Cfg.Skills["cleave"].MaxRank;
            for (int i = 0; i < max; i++) save = Skills.InvestSkill(save, id, "cleave", Cfg);

            Assert.Equal(max, Skills.RankOf(Hero(save, id), "cleave"));
            Assert.False(Skills.CanInvest(save, id, "cleave", Cfg));
            Assert.Same(save, Skills.InvestSkill(save, id, "cleave", Cfg)); // capped -> no-op
        }

        [Fact]
        public void RespecRefundsEveryPoint()
        {
            var (save, id) = LeveledWarrior();
            int earned = Skills.PointsEarned(Hero(save, id), Cfg);
            save = Skills.InvestSkill(save, id, "cleave", Cfg);
            save = Skills.InvestSkill(save, id, "bash", Cfg);

            var respecced = Skills.RespecHero(save, id, Cfg);

            Assert.Equal(0, Skills.PointsSpent(Hero(respecced, id)));
            Assert.Equal(earned, Skills.UnspentPoints(Hero(respecced, id), Cfg));
            Assert.Equal(0, Skills.RankOf(Hero(respecced, id), "cleave"));
        }

        // ---- save threading ----

        [Fact]
        public void SkillRanksSurviveUnrelatedReducers()
        {
            var (save, id) = LeveledWarrior();
            save = Skills.InvestSkill(save, id, "cleave", Cfg);

            var afterGold = Progression.GrantGold(save, 100);
            Assert.Equal(1, Skills.RankOf(Hero(afterGold, id), "cleave"));

            var afterXp = Progression.GrantPartyXp(save, 50000, Cfg); // level-up keeps ranks
            Assert.Equal(1, Skills.RankOf(Hero(afterXp, id), "cleave"));
        }

        // ---- combat scaling ----

        private static CombatEntity Caster(int rank)
        {
            var st = new StatBlock
            {
                [StatKey.Hp] = 1000, [StatKey.Atk] = 10, [StatKey.Def] = 0,
                [StatKey.AtkSpd] = 1, [StatKey.MoveSpd] = 3.0, [StatKey.CritChance] = 0, [StatKey.CritDmg] = 1.5,
            };
            var e = new CombatEntity
            {
                Id = "A", Team = Team.Party, Pos = new Vec2(0, 0), Stats = st,
                Hp = 1000, MaxHp = 1000, Mana = 100, MaxMana = 100, AttackIntervalMs = 1000,
                RefKind = "test", RefId = "A", Skills = new List<string> { "firebolt" },
            };
            if (rank > 0) e.SkillRanks["firebolt"] = rank;
            return e;
        }

        private static CombatEntity Dummy()
        {
            var st = new StatBlock { [StatKey.Hp] = 1000, [StatKey.Atk] = 1, [StatKey.Def] = 0, [StatKey.AtkSpd] = 1, [StatKey.MoveSpd] = 3.0 };
            return new CombatEntity { Id = "B", Team = Team.Enemy, Pos = new Vec2(0.5, 0), Stats = st, Hp = 1000, MaxHp = 1000, AttackIntervalMs = 1000, RefKind = "test", RefId = "B" };
        }

        private static double DamageFromOneCast(int rank)
        {
            var s = new CombatState();
            s.Entities.Add(Caster(rank));
            s.Entities.Add(Dummy());
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            return 1000 - s.Entities.First(e => e.Id == "B").Hp;
        }

        [Fact]
        public void Rank0ReproducesBaselineDamage()
        {
            // firebolt: 10 Atk * 1.8 mult, no crit, no rank bonus = 18 (matches SkillTests baseline).
            Assert.Equal(18.0, DamageFromOneCast(0), 3);
        }

        [Fact]
        public void HigherRankDealsMoreSkillDamage()
        {
            double r0 = DamageFromOneCast(0);
            double r5 = DamageFromOneCast(5);

            // factor 1 + 0.12*5 = 1.6 -> 18 * 1.6 = 28.8
            Assert.Equal(28.8, r5, 3);
            Assert.True(r5 > r0);
        }
    }
}
