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
        public void InitCombatBuildsPartyAndStageEnemies()
        {
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };
            var s = Combat.InitCombat(party, 1, Cfg, new Rng(1));

            Assert.Single(s.Entities, e => e.Team == Team.Party);
            // stage 1: packCount = 3 + 1/5 = 3, plus 1 boss => 4 enemies
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
            s.Stage = 3;

            var events = Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Contains(events, e => e.Type == CombatEventType.BossDefeated && e.Stage == 3);
        }

        // --- M2.4: loot into combat ---

        private static CombatEntity Monster(string id, string defId, double hp, bool boss = false)
        {
            var e = Ent(id, Team.Enemy, hp: hp, atk: 0, def: 0);
            e.RefKind = "monster"; e.RefId = defId; e.IsBoss = boss;
            return e;
        }

        [Fact]
        public void EnemyMonsterDeathProducesLoot()
        {
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Monster("EBOSS", "goblin_king", hp: 10, boss: true)); // boss => always drops
            s.Loot = new LootContext { ItemLevel = 5, DropRateMult = 1.5 };

            var events = Combat.RunToEnd(s, Cfg, new Rng(1));

            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.True(s.PendingLoot.Count >= 1);
            Assert.Equal(s.PendingLoot.Count, events.Count(e => e.Type == CombatEventType.LootDrop));
            foreach (var it in s.PendingLoot)
                Assert.True(Cfg.ItemBases.ContainsKey(it.BaseId));
        }

        [Fact]
        public void SyntheticTestEntitiesDropNothing()
        {
            // default Ent has RefKind "test" -> must not roll loot, even on enemy death
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Ent("B", Team.Enemy, hp: 10, atk: 0, def: 0));
            Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Empty(s.PendingLoot);
        }

        [Fact]
        public void PendingLootCommitsToInventory()
        {
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Monster("EBOSS", "goblin_king", hp: 10, boss: true));
            s.Loot = new LootContext { ItemLevel = 5, DropRateMult = 1.5 };
            Combat.RunToEnd(s, Cfg, new Rng(1));

            var save = Save.NewGame(1, Cfg, 0);
            int before = save.Inventory.Count;
            var after = Inventory.AddItems(save, s.PendingLoot);

            Assert.Equal(before + s.PendingLoot.Count, after.Inventory.Count);
            Assert.Equal(before, save.Inventory.Count); // original untouched
        }

        [Fact]
        public void LootIsDeterministicForSameSeed()
        {
            CombatState Build()
            {
                var s = State(
                    Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                    Monster("EBOSS", "goblin_king", hp: 10, boss: true));
                s.Loot = new LootContext { ItemLevel = 5, DropRateMult = 1.5 };
                return s;
            }

            var s1 = Build();
            var s2 = Build();
            Combat.RunToEnd(s1, Cfg, new Rng(7));
            Combat.RunToEnd(s2, Cfg, new Rng(7));

            Assert.Equal(s1.PendingLoot.Count, s2.PendingLoot.Count);
            for (int i = 0; i < s1.PendingLoot.Count; i++)
                Assert.Equal(s1.PendingLoot[i].Id, s2.PendingLoot[i].Id);
        }

        // --- M3.2: XP accrual in combat ---

        [Fact]
        public void MonsterKillsAccruePendingXp()
        {
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Monster("E0", "slime", hp: 10),
                Monster("EBOSS", "goblin_king", hp: 10, boss: true));
            Combat.RunToEnd(s, Cfg, new Rng(1));

            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.Equal(Cfg.Monsters["slime"].XpReward + Cfg.Monsters["goblin_king"].XpReward, s.PendingXp);
        }

        [Fact]
        public void SyntheticTestEntitiesGiveNoXp()
        {
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Ent("B", Team.Enemy, hp: 10, atk: 0, def: 0)); // RefKind "test"
            Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Equal(0, s.PendingXp);
        }

        [Fact]
        public void PendingXpIsDeterministic()
        {
            CombatState Build() => State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Monster("E0", "slime", hp: 10),
                Monster("EBOSS", "goblin_king", hp: 10, boss: true));

            var s1 = Build();
            var s2 = Build();
            Combat.RunToEnd(s1, Cfg, new Rng(7));
            Combat.RunToEnd(s2, Cfg, new Rng(7));
            Assert.Equal(s1.PendingXp, s2.PendingXp);
        }
    }
}
