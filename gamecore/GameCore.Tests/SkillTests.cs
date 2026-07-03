using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class SkillTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        // A controllable entity: Atk 10, no crit (deterministic damage), given loadout.
        // Skills are cooldown-only now (mana removed).
        private static CombatEntity Mk(string id, Team team, double x, double hp = 1000,
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
                Hp = hp, MaxHp = hp, AttackIntervalMs = 1000,
                RefKind = "test", RefId = id, Skills = new List<string>(skills),
            };
        }

        private static CombatState St(params CombatEntity[] e)
        {
            var s = new CombatState();
            s.Entities.AddRange(e);
            return s;
        }

        private static List<CombatEvent> Step(CombatState s, uint seed = 1) =>
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(seed));

        private static CombatEntity E(CombatState s, string id) => s.Entities.First(x => x.Id == id);

        [Fact]
        public void HeroCastsDamageSkillWhenOffCooldownAndSetsCooldown()
        {
            var caster = Mk("A", Team.Party, 0, skills: "firebolt"); // range 6, x1.8
            var enemy = Mk("B", Team.Enemy, 0.5);
            var s = St(caster, enemy);

            var ev = Step(s);

            Assert.Contains(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "firebolt" && e.SourceId == "A");
            Assert.Equal(1000 - 18, E(s, "B").Hp);          // 10 atk * 1.8 mult, no crit
            Assert.True(E(s, "A").SkillCdMs["firebolt"] > 0); // on cooldown now — the only gate
        }

        [Fact]
        public void SkillOnCooldownFallsBackToBasicAttack()
        {
            var caster = Mk("A", Team.Party, 0, skills: "firebolt");
            var enemy = Mk("B", Team.Enemy, 0.5); // within melee range too
            var s = St(caster, enemy);

            Step(s);                 // casts firebolt
            var ev2 = Step(s);       // firebolt now on cooldown

            Assert.DoesNotContain(ev2, e => e.Type == CombatEventType.SkillCast);
            Assert.Contains(ev2, e => e.Type == CombatEventType.Hit && e.SourceId == "A"); // basic attack instead
        }

        [Fact]
        public void SkillFiresAgainOnceCooldownElapses()
        {
            var caster = Mk("A", Team.Party, 0, skills: "firebolt"); // 3500ms cooldown
            var enemy = Mk("B", Team.Enemy, 0.5);
            var s = St(caster, enemy);

            var ev1 = Step(s); // first cast
            Assert.Contains(ev1, e => e.Type == CombatEventType.SkillCast && e.SkillId == "firebolt");

            // Cooldown-only gating: with no mana pool, the skill re-fires purely once its
            // cooldown drains. Step past 3500ms and it casts again.
            bool castAgain = false;
            for (int i = 0; i < 200 && !castAgain; i++)
            {
                var ev = Step(s);
                if (ev.Any(e => e.Type == CombatEventType.SkillCast && e.SkillId == "firebolt")) castAgain = true;
            }
            Assert.True(castAgain, "firebolt should re-fire once its cooldown elapses");
        }

        [Fact]
        public void AoeDamageSkillHitsEnemiesNearThePrimaryTarget()
        {
            var caster = Mk("A", Team.Party, 0, skills: "cleave"); // aoe, range 1.8, radius 1.6, mult 1.6
            var b = Mk("B", Team.Enemy, 1.0);   // primary (nearest, in range)
            var c = Mk("C", Team.Enemy, 2.2);   // 1.2 from B -> within radius
            var d = Mk("D", Team.Enemy, 10.0);  // far -> untouched
            var s = St(caster, b, c, d);

            var ev = Step(s);

            Assert.Contains(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "cleave");
            Assert.Equal(1000 - 16, E(s, "B").Hp); // 10 * 1.6
            Assert.Equal(1000 - 16, E(s, "C").Hp);
            Assert.Equal(1000, E(s, "D").Hp);
        }

        [Fact]
        public void DashSkillClosesTheGapAndStrikes()
        {
            var caster = Mk("A", Team.Party, 0, skills: "shieldcharge"); // dash, range 6, x2.0
            var enemy = Mk("B", Team.Enemy, 4.0);                        // well outside melee
            var s = St(caster, enemy);

            var ev = Step(s);

            Assert.Contains(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "shieldcharge" && e.SourceId == "A");
            Assert.Equal(1000 - 20, E(s, "B").Hp);                       // 10 atk * 2.0 on arrival
            // landing = body radii + 0.15 margin: touching, inside the 1.2 attack range
            double gap = Vec2.Distance(E(s, "A").Pos, E(s, "B").Pos);
            Assert.True(gap <= 1.1, $"expected melee contact after the dash, gap={gap}");
            Assert.True(E(s, "A").SkillCdMs["shieldcharge"] > 0);
        }

        [Fact]
        public void DashIsSavedWhenAlreadyInMeleeContact()
        {
            var caster = Mk("A", Team.Party, 0, skills: "shieldcharge");
            var enemy = Mk("B", Team.Enemy, 0.4); // already touching
            var s = St(caster, enemy);

            var ev = Step(s);

            Assert.DoesNotContain(ev, e => e.Type == CombatEventType.SkillCast);
            Assert.Contains(ev, e => e.Type == CombatEventType.Hit && e.SourceId == "A"); // basic attack instead
            // (position not asserted: overlapping bodies get nudged by separation physics)
        }

        [Fact]
        public void DashRespectsItsRange()
        {
            var caster = Mk("A", Team.Party, 0, skills: "shieldcharge");
            var enemy = Mk("B", Team.Enemy, 8.0); // beyond range 6
            var s = St(caster, enemy);

            var ev = Step(s);

            Assert.DoesNotContain(ev, e => e.Type == CombatEventType.SkillCast);
            Assert.Equal(1000, E(s, "B").Hp);
        }

        [Fact]
        public void HealSkillRestoresTheMostHurtAlly()
        {
            var healer = Mk("A", Team.Party, 0, skills: "mend"); // heal, lowestHp, range 8, mult 2.0
            var ally = Mk("ALLY", Team.Party, 2);
            ally.Hp = 10; ally.MaxHp = 100;
            var enemy = Mk("E", Team.Enemy, 50); // far; keeps the run going
            var s = St(healer, ally, enemy);

            var ev = Step(s);

            Assert.Contains(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "mend");
            Assert.Contains(ev, e => e.Type == CombatEventType.Heal && e.TargetId == "ALLY");
            Assert.Equal(30, E(s, "ALLY").Hp); // 10 + 10 atk * 2.0
        }

        [Fact]
        public void BuffSkillRaisesEffectiveStatThenExpires()
        {
            var caster = Mk("A", Team.Party, 0, skills: "warcry"); // +10 Atk for 6000ms
            var enemy = Mk("E", Team.Enemy, 50);                    // far -> can't kill, run stays alive
            var s = St(caster, enemy);

            var ev = Step(s); // casts warcry
            Assert.Contains(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "warcry");
            Assert.Equal(20, E(s, "A").EffectiveStat(StatKey.Atk)); // 10 base + 10 buff

            for (int i = 0; i < 220; i++) Step(s); // > 6s -> buff expires
            Assert.Equal(10, E(s, "A").EffectiveStat(StatKey.Atk));
            Assert.Empty(E(s, "A").Buffs);
        }

        [Fact]
        public void BossCastsItsSkill()
        {
            var boss = Mk("E", Team.Enemy, 0, skills: "boss_quake"); // aoe, range 3, radius 3
            var p1 = Mk("P1", Team.Party, 1);
            var p2 = Mk("P2", Team.Party, 2);
            var s = St(boss, p1, p2);

            var ev = Step(s);

            Assert.Contains(ev, e => e.Type == CombatEventType.SkillCast && e.SkillId == "boss_quake" && e.SourceId == "E");
            Assert.True(E(s, "P1").Hp < 1000);
            Assert.True(E(s, "P2").Hp < 1000);
        }

        [Fact]
        public void AtkSpdShortensSkillCooldown()
        {
            CombatState Setup(double atkSpd)
            {
                var c = Mk("A", Team.Party, 0, skills: "firebolt");
                c.Stats[StatKey.AtkSpd] = atkSpd;
                return St(c, Mk("B", Team.Enemy, 0.5));
            }

            var fast = Setup(2.0);
            var slow = Setup(0.5);
            Step(fast);
            Step(slow);

            // both cast firebolt; the faster caster's effective cooldown is shorter
            Assert.True(E(fast, "A").SkillCdMs["firebolt"] < E(slow, "A").SkillCdMs["firebolt"]);
        }

        [Fact]
        public void SkillCombatIsDeterministic()
        {
            CombatState Build()
            {
                var caster = Mk("A", Team.Party, 0, skills: "firebolt");
                caster.Stats[StatKey.CritChance] = 0.5; caster.Stats[StatKey.CritDmg] = 2.0; // exercise rng
                return St(caster, Mk("B", Team.Enemy, 0.5, hp: 400));
            }

            var s1 = Build(); var s2 = Build();
            var e1 = Combat.RunToEnd(s1, Cfg, new Rng(42));
            var e2 = Combat.RunToEnd(s2, Cfg, new Rng(42));

            Assert.Equal(s1.Status, s2.Status);
            Assert.Equal(E(s1, "B").Hp, E(s2, "B").Hp);
            Assert.Equal(e1.Count, e2.Count);
            Assert.Equal(e1.Count(e => e.Type == CombatEventType.SkillCast),
                         e2.Count(e => e.Type == CombatEventType.SkillCast));
        }
    }
}
