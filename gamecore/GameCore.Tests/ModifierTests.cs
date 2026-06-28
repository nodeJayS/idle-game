using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class ModifierTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static SaveState NewSave() => Save.NewGame(1, Cfg, 0);
        private static HeroInstance Champ() => new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 };

        // A save synced to a given farm depth (the new stage-driven acquisition).
        private static SaveState AtStage(int highestStage)
        {
            var s = NewSave();
            s.Progress.HighestStage = highestStage;
            return Modifiers.SyncToStage(s, Cfg);
        }

        // Minimal combat entity for the behavior tests (RefKind "test" unless overridden).
        private static CombatEntity Ent(string id, Team team, double hp, double atk, double def, double x)
        {
            var st = new StatBlock { [StatKey.Hp] = hp, [StatKey.Atk] = atk, [StatKey.Def] = def, [StatKey.AtkSpd] = 1, [StatKey.MoveSpd] = 3.0 };
            return new CombatEntity { Id = id, Team = team, Pos = new Vec2(x, 0), Stats = st, Hp = hp, MaxHp = hp, AttackIntervalMs = 1000, RefKind = "test", RefId = id };
        }

        private static CombatState State(params CombatEntity[] ents)
        {
            var s = new CombatState();
            s.Entities.AddRange(ents);
            return s;
        }

        // Ranks off so base trash stats are comparable across runs (RollRank still draws one rng
        // value either way, so the stream matches between modified and unmodified InitFarm calls).
        private static GameConfig NoRankCfg()
        {
            var c = GameConfig.Default();
            c.Balance.EliteChance = 0; c.Balance.RareChance = 0;
            return c;
        }

        // --- stage-driven acquisition + upgrade (pure reducers) ---

        [Fact]
        public void DepthBelowFirstUnlockOwnsNothing()
        {
            Assert.Empty(AtStage(Cfg.Balance.ModifierNewEveryStages - 1).Modifiers.Owned);
        }

        [Fact]
        public void SyncOwnsModifiersByDepthAtUniformStrength()
        {
            var s = AtStage(25); // 25/10 = 2 owned; 25/5 = 5 strength
            Assert.Equal(2, s.Modifiers.Owned.Count);
            Assert.Equal(new[] { "prosperous", "studious" }, s.Modifiers.Owned.Keys.OrderBy(k => k).ToArray());
            Assert.All(s.Modifiers.Owned.Values, v => Assert.Equal(5, v));
        }

        [Fact]
        public void PushingDeeperUpgradesAllOwned()
        {
            Assert.Equal(2, AtStage(10).Modifiers.Owned["prosperous"]);   // 10/5
            var s20 = AtStage(20);                                        // 20/5 = 4, and a 2nd unlock
            Assert.Equal(4, s20.Modifiers.Owned["prosperous"]);           // the existing one upgraded
            Assert.Equal(4, s20.Modifiers.Owned["studious"]);
        }

        [Fact]
        public void SyncPrunesActiveToOwnedAndCap()
        {
            var s = AtStage(70); // owns all 7
            s.Modifiers.Active.Clear();
            s.Modifiers.Active.AddRange(new[] { "prosperous", "studious", "bountiful", "armored", "ghost" });
            var synced = Modifiers.SyncToStage(s, Cfg);
            Assert.True(synced.Modifiers.Active.Count <= Cfg.Balance.MaxActiveModifiers);
            Assert.DoesNotContain("ghost", synced.Modifiers.Active); // unowned pruned
        }

        // --- loadout (capped toggle) ---

        [Fact]
        public void SetActiveRequiresOwnershipAndToggles()
        {
            var save = Modifiers.SetActive(NewSave(), "prosperous", true, Cfg); // not owned at stage 0 -> no-op
            Assert.Empty(save.Modifiers.Active);

            save = AtStage(10);
            save = Modifiers.SetActive(save, "prosperous", true, Cfg);
            Assert.Contains("prosperous", save.Modifiers.Active);

            save = Modifiers.SetActive(save, "prosperous", false, Cfg);
            Assert.DoesNotContain("prosperous", save.Modifiers.Active);
        }

        [Fact]
        public void LoadoutCapBlocksBeyondMax()
        {
            var s = AtStage(70); // owns all 7
            foreach (var id in new[] { "prosperous", "studious", "bountiful" })
                s = Modifiers.SetActive(s, id, true, Cfg);
            Assert.Equal(Cfg.Balance.MaxActiveModifiers, s.Modifiers.Active.Count);

            var over = Modifiers.SetActive(s, "armored", true, Cfg); // would exceed the cap
            Assert.Same(s, over);                                    // no-op shares the ref
        }

        [Fact]
        public void ResolveActiveReturnsDefAndStrength()
        {
            var save = AtStage(25); // owns prosperous + studious at strength 5
            save = Modifiers.SetActive(save, "studious", true, Cfg);
            var active = Modifiers.ResolveActive(save, Cfg);
            Assert.Single(active);
            Assert.Equal("studious", active[0].Def.Id);
            Assert.Equal(5, active[0].Strength);
        }

        // --- application to combat ---

        [Fact]
        public void ActiveModifierBuffsFarmTrashStatsAndBehavior()
        {
            var cfg = NoRankCfg();
            var party = new[] { Champ() };
            double baseHp = Combat.InitFarm(party, 5, cfg, new Rng(1)).Entities.First(e => e.Team == Team.Enemy).MaxHp;

            var vamp = new[] { new ModifierInstance { Def = cfg.Modifiers["vampiric"], Strength = 10 } };
            var mob = Combat.InitFarm(party, 5, cfg, new Rng(1), vamp).Entities.First(e => e.Team == Team.Enemy);

            Assert.Contains("vampiric", mob.ModTypes);
            Assert.Equal(baseHp * 1.5, mob.MaxHp, 3); // Hp coeff 0.05 * str 10 = +50%
            Assert.True(mob.Lifesteal > 0);            // behavior precomputed
            Assert.True(mob.GoldMult > 1.0);           // gold reward buff folded in
        }

        [Fact]
        public void BossExhibitsInherentModifierBehaviorOnly()
        {
            var party = new[] { Champ() };
            // No-cycle config => the boss gets no inherent modifier (baseline HP).
            var noMod = GameConfig.Default();
            noMod.ModifierCycle = new List<string>();
            double baseBossHp = Combat.InitBossChallenge(party, 1, noMod, new Rng(1)).Entities.First(e => e.IsBoss).MaxHp;

            var boss = Combat.InitBossChallenge(party, 1, Cfg, new Rng(1)).Entities.First(e => e.IsBoss); // stage 1 -> vampiric
            Assert.Contains("vampiric", boss.ModTypes);
            Assert.True(boss.Lifesteal > 0);            // exhibits the behavior...
            Assert.Equal(baseBossHp, boss.MaxHp, 3);    // ...but NOT the stat buff (timer stays fair)
        }

        [Fact]
        public void VampiricMonsterHealsWhenItDamagesAHero()
        {
            var enemy = Ent("E", Team.Enemy, hp: 1000, atk: 50, def: 0, x: 0.4);
            enemy.Hp = 100;          // pre-damaged so the heal is observable (clamps to MaxHp)
            enemy.Lifesteal = 0.5;
            var hero = Ent("P", Team.Party, hp: 100000, atk: 0, def: 0, x: 0); // can't kill the enemy
            var s = State(enemy, hero);

            for (int i = 0; i < 90; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.True(s.Entities.First(e => e.Id == "E").Hp > 100); // healed from lifesteal
        }

        [Fact]
        public void ThornsMonsterReflectsDamageToAttackingHero()
        {
            var enemy = Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0.4);
            enemy.ThornsReflect = 0.5;
            var hero = Ent("P", Team.Party, hp: 1000, atk: 100, def: 0, x: 0);
            var s = State(enemy, hero);

            for (int i = 0; i < 90; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.True(s.Entities.First(e => e.Id == "P").Hp < 1000); // took reflected damage
        }

        [Fact]
        public void ModifierGoldBuffMultipliesKillPayout()
        {
            long GoldFor(double goldMult)
            {
                var slime = Ent("E", Team.Enemy, hp: 5, atk: 0, def: 0, x: 0.4);
                slime.RefKind = "monster"; slime.RefId = "slime"; slime.GoldMult = goldMult;
                var hero = Ent("P", Team.Party, hp: 1000, atk: 500, def: 0, x: 0);
                var s = State(slime, hero);
                for (int i = 0; i < 60 && s.Status == CombatStatus.Running; i++)
                    Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
                return s.PendingGold;
            }

            long basePay = GoldFor(1.0);
            long buffed = GoldFor(3.0);
            Assert.True(basePay > 0);
            Assert.Equal(basePay * 3, buffed); // 3x gold mult -> 3x payout
        }

        [Fact]
        public void ModifiersSurviveUnrelatedReducers()
        {
            var save = AtStage(10);                                  // own prosperous str 2
            save = Modifiers.SetActive(save, "prosperous", true, Cfg);

            // Run a spread of unrelated reducers (each rebuilds the SaveState; none touch HighestStage).
            save = Progression.GrantGold(save, 100);
            save = Progression.GrantPartyXp(save, 50, Cfg);
            save = Party.SetLeader(save, save.Party[0]);
            save = Save.Touch(save, 999999);

            Assert.Equal(2, save.Modifiers.Owned["prosperous"]); // threaded through every copy site
            Assert.Contains("prosperous", save.Modifiers.Active);
        }
    }
}
