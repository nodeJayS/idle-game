using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Priest kit: the party heal-over-time (Sanctify — a party-targeted
    /// buff granting HpRegenPct) and the Holy Smite AoE.</summary>
    public class PriestTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static CombatEntity Mk(string id, Team team, double x, double hp = 1000, double mana = 100,
                                       params string[] skills)
        {
            var st = new StatBlock
            {
                [StatKey.Hp] = hp, [StatKey.Atk] = 10, [StatKey.Def] = 0,
                [StatKey.AtkSpd] = 1, [StatKey.MoveSpd] = 3.0, [StatKey.CritChance] = 0, [StatKey.CritDmg] = 1.5,
            };
            return new CombatEntity
            {
                Id = id, Team = team, Pos = new Vec2(x, 0), Stats = st,
                Hp = hp, MaxHp = hp, Mana = mana, MaxMana = 200, AttackIntervalMs = 1000,
                RefKind = "test", RefId = id, Skills = new List<string>(skills),
            };
        }

        private static CombatState St(params CombatEntity[] e)
        {
            var s = new CombatState();
            s.Entities.AddRange(e);
            return s;
        }

        private static CombatEntity E(CombatState s, string id) => s.Entities.First(x => x.Id == id);

        /// <summary>Step the sim forward by whole seconds' worth of fixed steps.</summary>
        private static void Run(CombatState s, double seconds)
        {
            var rng = new Rng(1);
            int steps = (int)(seconds * 1000 / Combat.DefaultStepMs);
            for (int i = 0; i < steps; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, rng);
        }

        [Fact]
        public void SanctifyBuffsTheWholePartyAndHealsOverTime()
        {
            var priest = Mk("P", Team.Party, 0, skills: "sanctify");
            var ally = Mk("A", Team.Party, 1);
            ally.Hp = 300;                                  // hurt, so the HoT is worth casting
            var enemy = Mk("X", Team.Enemy, 200);           // far away: alive (cast gate) but harmless
            var s = St(priest, ally, enemy);

            var ev = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.Contains(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "sanctify" && e.SourceId == "P");
            Assert.Contains(E(s, "P").Buffs, b => b.Stat == StatKey.HpRegenPct); // party-wide, caster included
            Assert.Contains(E(s, "A").Buffs, b => b.Stat == StatKey.HpRegenPct);

            Run(s, 1.0); // ~1s of ticks at 20%/s of MaxHp(1000) = ~200 hp
            Assert.InRange(E(s, "A").Hp, 450, 550);

            Run(s, 3.0); // the 10s budget is 200% of MaxHp — fully topped up well before expiry
            Assert.Equal(1000, E(s, "A").Hp);
        }

        [Fact]
        public void SanctifyIsNotWastedAtFullHealth()
        {
            var priest = Mk("P", Team.Party, 0, skills: "sanctify");
            var ally = Mk("A", Team.Party, 1);              // untouched
            var enemy = Mk("X", Team.Enemy, 200);
            var s = St(priest, ally, enemy);

            var ev = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.DoesNotContain(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "sanctify");
            Assert.Empty(E(s, "A").Buffs);
        }

        [Fact]
        public void SanctifyExpiresAfterItsDuration()
        {
            var priest = Mk("P", Team.Party, 0, skills: "sanctify");
            var ally = Mk("A", Team.Party, 1);
            ally.Hp = 300;
            var enemy = Mk("X", Team.Enemy, 500);
            var s = St(priest, ally, enemy);

            Run(s, 0.1); // cast happens on the first step
            Assert.Contains(E(s, "A").Buffs, b => b.Stat == StatKey.HpRegenPct);

            Run(s, 11.0); // past the 10s duration
            Assert.DoesNotContain(E(s, "A").Buffs, b => b.Stat == StatKey.HpRegenPct);
            Assert.Equal(0.0, E(s, "A").EffectiveStat(StatKey.HpRegenPct));
        }

        [Fact]
        public void HolySmiteDamagesTheCluster()
        {
            var priest = Mk("P", Team.Party, 0, skills: "holysmite");
            var e1 = Mk("X", Team.Enemy, 0.6);
            var e2 = Mk("Y", Team.Enemy, 1.2);              // within AoeRadius 2.4 of the primary
            var s = St(priest, e1, e2);

            var ev = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.Contains(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "holysmite");
            Assert.Equal(1000 - 16, E(s, "X").Hp);          // 10 atk x 1.6, no crit
            Assert.Equal(1000 - 16, E(s, "Y").Hp);
        }

        [Fact]
        public void PriestFollowsTheTwoPlusTwoTemplate()
        {
            var hero = Cfg.Heroes["priest_basic"];
            Assert.Equal(4, hero.Skills.Count);
            Assert.False(Cfg.Skills[hero.Skills[0]].Passive); // sanctify   (L1 active)
            Assert.True(Cfg.Skills[hero.Skills[1]].Passive);  // devotion   (L5 passive)
            Assert.False(Cfg.Skills[hero.Skills[2]].Passive); // holysmite  (L10 active)
            Assert.True(Cfg.Skills[hero.Skills[3]].Passive);  // benediction(L15 passive)
            Assert.Equal("priest_basic", Cfg.HeroUnlocks[5]);
        }
    }
}
