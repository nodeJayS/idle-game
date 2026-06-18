using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class CombatTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static CombatEntity Ent(string id, Team team, double hp, double atk, double def,
                                        double spd = 1, double critChance = 0, double critDmg = 1.5,
                                        double x = 0, double y = 0)
        {
            var st = new StatBlock
            {
                [StatKey.Hp] = hp, [StatKey.Atk] = atk, [StatKey.Def] = def,
                [StatKey.Spd] = spd, [StatKey.CritChance] = critChance, [StatKey.CritDmg] = critDmg,
            };
            return new CombatEntity
            {
                Id = id, Team = team, Pos = new Vec2(x, y), Stats = st,
                Hp = hp, MaxHp = hp, AttackIntervalMs = 1000.0 / spd, RefKind = "test", RefId = id,
            };
        }

        private static CombatState State(params CombatEntity[] ents)
        {
            var s = new CombatState();
            s.Entities.AddRange(ents);
            return s;
        }

        private static double Hp(CombatState s, string id) => s.Entities.First(e => e.Id == id).Hp;

        [Fact]
        public void InitCombatBuildsPartyAndTierEnemies()
        {
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };
            var s = Combat.InitCombat(party, 1, Cfg, new Rng(1));

            Assert.Single(s.Entities, e => e.Team == Team.Party);
            // tier 1: packCount = 3 + 1/5 = 3, plus 1 boss => 4 enemies
            Assert.Equal(4, s.Entities.Count(e => e.Team == Team.Enemy));
            Assert.Single(s.Entities, e => e.IsBoss);
            Assert.True(s.Entities.First(e => e.Team == Team.Party).Pos.X < 0);
        }

        [Fact]
        public void StrongPartyBeatsWeakEnemy()
        {
            var s = State(
                Ent("A", Team.Party, hp: 200, atk: 100, def: 10),
                Ent("B", Team.Enemy, hp: 30, atk: 5, def: 0));
            Combat.RunToEnd(s, Cfg, new Rng(1));

            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.False(s.Entities.First(e => e.Id == "B").Alive);
            Assert.True(s.Entities.First(e => e.Id == "A").Alive);
        }

        [Fact]
        public void WeakPartyLoses()
        {
            var s = State(
                Ent("A", Team.Party, hp: 5, atk: 1, def: 0),
                Ent("B", Team.Enemy, hp: 1000, atk: 100, def: 50));
            Combat.RunToEnd(s, Cfg, new Rng(1));

            Assert.Equal(CombatStatus.Lost, s.Status);
        }

        [Fact]
        public void IsDeterministicForSameSeed()
        {
            CombatState Build() => State(
                Ent("A", Team.Party, hp: 200, atk: 20, def: 0, critChance: 0.5, critDmg: 2.0),
                Ent("B", Team.Enemy, hp: 200, atk: 15, def: 0, critChance: 0.3, critDmg: 1.8));

            var s1 = Build();
            var s2 = Build();
            var e1 = Combat.RunToEnd(s1, Cfg, new Rng(123));
            var e2 = Combat.RunToEnd(s2, Cfg, new Rng(123));

            Assert.Equal(e1.Count, e2.Count);
            Assert.Equal(s1.Status, s2.Status);
            Assert.Equal(Hp(s1, "A"), Hp(s2, "A"));
            Assert.Equal(Hp(s1, "B"), Hp(s2, "B"));
        }

        [Fact]
        public void CritAppliesDamageMultiplier()
        {
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 10, def: 0, critChance: 1.0, critDmg: 2.0),
                Ent("B", Team.Enemy, hp: 1000, atk: 0, def: 0));

            var events = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            var hit = events.First(e => e.Type == CombatEventType.Hit && e.SourceId == "A");

            Assert.True(hit.Crit);
            Assert.Equal(20, hit.Amount); // (10 atk - 0 def) * 2.0 critDmg
        }

        [Fact]
        public void BossDefeatedEventFiresWhenBossDies()
        {
            var boss = Ent("EBOSS", Team.Enemy, hp: 1, atk: 0, def: 0);
            boss.IsBoss = true;
            var s = State(Ent("A", Team.Party, hp: 100, atk: 50, def: 0), boss);
            s.Tier = 3;

            var events = Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Contains(events, e => e.Type == CombatEventType.BossDefeated && e.Tier == 3);
        }
    }
}
