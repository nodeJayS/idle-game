using System.Collections.Generic;
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
                [StatKey.AtkSpd] = spd, [StatKey.MoveSpd] = 3.0, // spd param drives attack rate; movement matches old constant
                [StatKey.CritChance] = critChance, [StatKey.CritDmg] = critDmg,
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

        // Two fielded heroes (warrior h1 + an acquired magician h2) for swap tests, since a
        // fresh save now starts with just the warrior.
        private static SaveState TwoHeroSave()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Party.AcquireHero(save, "magician_basic", Cfg, "h2");
            return Party.FieldHero(save, 1, "h2"); // h1 in slot 0, h2 in slot 1
        }

        [Fact]
        public void ReconcilePartyRemovesBenchedHero()
        {
            var save = TwoHeroSave();
            var s = Combat.InitFarm(save.Heroes, 1, Cfg, new Rng(1));
            Assert.Equal(2, s.Entities.Count(e => e.Team == Team.Party));

            Combat.ReconcileParty(s, Party.SetPartySlot(save, 1, null), Cfg); // bench hero 2

            Assert.Equal(1, s.Entities.Count(e => e.Team == Team.Party));
            Assert.DoesNotContain(s.Entities, e => e.RefKind == "hero" && e.RefId == "h2");
            Assert.Contains(s.Entities, e => e.RefKind == "hero" && e.RefId == "h1");
        }

        [Fact]
        public void ReconcilePartyAddsFieldedHeroWithoutDuplicating()
        {
            var save = TwoHeroSave();
            var s = Combat.InitFarm(save.Heroes, 1, Cfg, new Rng(1));

            var benched = Party.SetPartySlot(save, 1, null);
            Combat.ReconcileParty(s, benched, Cfg);
            Assert.Equal(1, s.Entities.Count(e => e.Team == Team.Party));

            var refielded = Party.FieldHero(benched, 2, "h2"); // bring hero 2 back
            Combat.ReconcileParty(s, refielded, Cfg);

            Assert.Equal(2, s.Entities.Count(e => e.Team == Team.Party));
            Assert.Single(s.Entities, e => e.RefKind == "hero" && e.RefId == "h2");
        }

        [Fact]
        public void ReconcilePartyKeepsExistingHeroLiveState()
        {
            var save = TwoHeroSave();
            var s = Combat.InitFarm(save.Heroes, 1, Cfg, new Rng(1));
            s.Entities.First(e => e.RefId == "h1").Hp = 5; // took damage mid-run

            Combat.ReconcileParty(s, Party.SetPartySlot(save, 1, null), Cfg); // bench the other hero

            Assert.Equal(5, s.Entities.First(e => e.RefId == "h1").Hp); // untouched by the swap
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

        // --- M8.1: farm encounter (endless spawning) ---

        private static GameConfig FarmCfg(int cap, double intervalMs)
        {
            var cfg = GameConfig.Default();
            cfg.Balance.MobCap = cap;
            cfg.Balance.SpawnIntervalMs = intervalMs;
            return cfg;
        }

        private static int AliveEnemies(CombatState s) =>
            s.Entities.Count(e => e.Team == Team.Enemy && e.Alive);

        [Fact]
        public void InitFarmStartsWithTrashAndNoBoss()
        {
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };
            var s = Combat.InitFarm(party, 1, GameConfig.Default(), new Rng(1));

            Assert.Equal(EncounterKind.Farm, s.Kind);
            Assert.True(AliveEnemies(s) > 0);
            Assert.DoesNotContain(s.Entities, e => e.IsBoss);
        }

        [Fact]
        public void SpawnedFarmTrashStartsNonAggro()
        {
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };
            var s = Combat.InitFarm(party, 1, GameConfig.Default(), new Rng(1));
            Assert.True(s.Entities.Count(e => e.Team == Team.Enemy) > 0);
            Assert.All(s.Entities.Where(e => e.Team == Team.Enemy), e => Assert.False(e.Aggro)); // ambles until hit
        }

        [Fact]
        public void FarmSpawnsTrashAroundTheParty()
        {
            var cfg = GameConfig.Default();
            var s = Combat.InitFarm(new[] { Champ() }, 1, cfg, new Rng(3));
            Assert.True(s.Entities.Count(e => e.Team == Team.Enemy) > 0);
            // party starts near the origin; the pack should ring it (centre in the ring +
            // each mob within PackRadius of that centre), not scatter across the map
            double max = cfg.Balance.SpawnRingOuter + cfg.Balance.PackRadius + 3;
            foreach (var e in s.Entities)
                if (e.Team == Team.Enemy)
                    Assert.True(Vec2.Distance(e.Pos, new Vec2(0, 0)) <= max);
        }

        [Fact]
        public void TrashGetsTankierWithStage()
        {
            var cfg = GameConfig.Default();
            double FirstMobHp(int stage) =>
                Combat.InitFarm(new[] { Champ() }, stage, cfg, new Rng(1)).Entities.First(e => e.Team == Team.Enemy).MaxHp;
            // steep HP curve so leveled heroes still need several hits at depth
            Assert.True(FirstMobHp(10) > FirstMobHp(1) * 3);
        }

        [Fact]
        public void BossStartsAggro()
        {
            var s = Combat.InitBossChallenge(new[] { Champ() }, 1, GameConfig.Default(), new Rng(1));
            Assert.All(s.Entities.Where(e => e.Team == Team.Enemy), e => Assert.True(e.Aggro));
        }

        [Fact]
        public void AtkSpdBuffSpeedsBasicAttacks()
        {
            // A +AtkSpd buff (e.g. Frenzy/Haste) shortens the basic-attack cooldown, not just skills.
            double CdAfterAttack(double atkSpdBuff)
            {
                var atk = Ent("A", Team.Party, hp: 100, atk: 10, def: 0, spd: 1.0, x: 0);
                var tgt = Ent("B", Team.Enemy, hp: 100000, atk: 0, def: 0, x: 0); // adjacent, tanky
                if (atkSpdBuff > 0)
                    atk.Buffs.Add(new ActiveBuff { Stat = StatKey.AtkSpd, Amount = atkSpdBuff, RemainingMs = 100000 });
                var s = State(atk, tgt);
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1)); // A attacks B
                return s.Entities.First(e => e.Id == "A").AttackCdMs;
            }

            Assert.True(CdAfterAttack(1.0) < CdAfterAttack(0.0)); // buffed cooldown is shorter
        }

        [Fact]
        public void HittingAnEnemyAggrosIt()
        {
            var hero = Ent("A", Team.Party, hp: 100, atk: 5, def: 0, x: 0);
            var mob = Ent("B", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 0); // adjacent + tanky
            mob.Aggro = false;
            var s = State(hero, mob);

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.True(s.Entities.First(e => e.Id == "B").Aggro); // woke up after being hit
        }

        [Fact]
        public void WanderingTrashStaysWithinMapBounds()
        {
            var cfg = GameConfig.Default();
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };
            var s = Combat.InitFarm(party, 1, cfg, new Rng(7));
            s.Entities[0].Stats[StatKey.Atk] = 0; // nothing dies -> trash persists and wanders

            for (int i = 0; i < 300; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(7));

            foreach (var e in s.Entities)
            {
                if (e.Team != Team.Enemy) continue;
                Assert.True(System.Math.Abs(e.Pos.X) <= cfg.Balance.MapHalfWidth);
                Assert.True(System.Math.Abs(e.Pos.Y) <= cfg.Balance.MapHalfDepth);
            }
        }

        [Fact]
        public void FarmRefillsTrashUpToCapAndNoFurther()
        {
            // a party that can't kill anything (atk 0) lets trash accumulate to the cap
            var cfg = FarmCfg(cap: 5, intervalMs: 50);
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };
            var s = Combat.InitFarm(party, 1, cfg, new Rng(1));
            s.Entities[0].Stats[StatKey.Atk] = 0; // party deals no damage

            for (int i = 0; i < 400; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal(5, AliveEnemies(s));         // filled to cap
            Assert.Equal(CombatStatus.Running, s.Status); // never auto-wins
        }

        [Fact]
        public void FarmNeverAutoWinsEvenWhenCleared()
        {
            // a one-shot party clears trash constantly; farm must stay Running, not Won
            var cfg = FarmCfg(cap: 30, intervalMs: 2000);
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 50 } };
            var s = Combat.InitFarm(party, 1, cfg, new Rng(1));
            s.Entities[0].Stats[StatKey.Atk] = 100000;

            for (int i = 0; i < 500; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.NotEqual(CombatStatus.Won, s.Status);
            Assert.Equal(CombatStatus.Running, s.Status);
        }

        [Fact]
        public void FarmWipeLoses()
        {
            var cfg = FarmCfg(cap: 30, intervalMs: 2000);
            var s = Combat.InitFarm(
                new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } }, 1, cfg, new Rng(1));
            // make the hero a glass cannon-less target that simply dies and can't respawn
            var hero = s.Entities[0];
            hero.MaxHp = hero.Hp = 1;
            hero.Stats[StatKey.Atk] = 0;
            hero.Stats[StatKey.HpRegen] = 0;
            hero.RespawnDurationMs = 0;

            Combat.RunToEnd(s, cfg, new Rng(1), maxSteps: 5000);
            Assert.Equal(CombatStatus.Lost, s.Status);
        }

        [Fact]
        public void FarmSpawnsInBatchesNotOneAtATime()
        {
            var cfg = GameConfig.Default();
            cfg.Balance.MobCap = 30;
            cfg.Balance.SpawnBatchSize = 10;
            cfg.Balance.SpawnIntervalMs = 1000;
            var s = Combat.InitFarm(new[] { Champ() }, 1, cfg, new Rng(1));
            s.Entities[0].Stats[StatKey.Atk] = 0; // nothing dies

            Assert.Equal(10, AliveEnemies(s)); // initial wave is a batch

            for (int i = 0; i < 45; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1)); // one interval
            Assert.Equal(20, AliveEnemies(s)); // jumped by a full batch, not by 1
        }

        [Fact]
        public void BatchSpawnRespectsCap()
        {
            var cfg = GameConfig.Default();
            cfg.Balance.MobCap = 25;
            cfg.Balance.SpawnBatchSize = 10;
            cfg.Balance.SpawnIntervalMs = 100;
            var s = Combat.InitFarm(new[] { Champ() }, 1, cfg, new Rng(1));
            s.Entities[0].Stats[StatKey.Atk] = 0;

            for (int i = 0; i < 60; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1)); // many waves
            Assert.Equal(25, AliveEnemies(s)); // partial final wave, never over cap
        }

        [Fact]
        public void FarmSpawnsWithinMapBounds()
        {
            var cfg = GameConfig.Default();
            var s = Combat.InitFarm(new[] { Champ() }, 1, cfg, new Rng(5));
            // a few more waves on top of the initial batch
            for (int i = 0; i < 60; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(5));

            foreach (var e in s.Entities)
            {
                if (e.Team != Team.Enemy) continue;
                Assert.True(System.Math.Abs(e.Pos.X) <= cfg.Balance.MapHalfWidth);
                Assert.True(System.Math.Abs(e.Pos.Y) <= cfg.Balance.MapHalfDepth);
            }
        }

        [Fact]
        public void FarmPrunesDeadTrash()
        {
            // a killing party keeps clearing spawns; dead enemies must not accumulate
            var cfg = FarmCfg(cap: 5, intervalMs: 50);
            var s = Combat.InitFarm(new[] { Champ(50) }, 1, cfg, new Rng(1));
            s.Entities[0].Stats[StatKey.Atk] = 100000;

            for (int i = 0; i < 500; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            // party (1) + at most the cap of living trash; no graveyard of corpses
            Assert.True(s.Entities.Count <= 1 + cfg.Balance.MobCap);
        }

        [Fact]
        public void FarmIsDeterministic()
        {
            var cfg = FarmCfg(cap: 8, intervalMs: 100);
            CombatState Build()
            {
                var s = Combat.InitFarm(
                    new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } }, 1, cfg, new Rng(9));
                s.Entities[0].Stats[StatKey.Atk] = 0;
                return s;
            }

            var s1 = Build();
            var s2 = Build();
            for (int i = 0; i < 300; i++)
            {
                Combat.StepCombat(s1, Combat.DefaultStepMs, cfg, new Rng(9));
                Combat.StepCombat(s2, Combat.DefaultStepMs, cfg, new Rng(9));
            }

            Assert.Equal(s1.SpawnCount, s2.SpawnCount);
            Assert.Equal(s1.Entities.Count, s2.Entities.Count);
            Assert.Equal(AliveEnemies(s1), AliveEnemies(s2));
        }

        // --- M8.2: boss challenge (timed gate) ---

        private static HeroInstance Champ(int level = 1) =>
            new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = level };

        [Fact]
        public void BossChallengeSpawnsLoneBoss()
        {
            var s = Combat.InitBossChallenge(new[] { Champ() }, 1, GameConfig.Default(), new Rng(1));

            Assert.Equal(EncounterKind.BossChallenge, s.Kind);
            Assert.Single(s.Entities, e => e.Team == Team.Enemy);
            Assert.Single(s.Entities, e => e.IsBoss);
        }

        [Fact]
        public void BossChallengeWonWhenBossKilledInTime()
        {
            var cfg = GameConfig.Default();
            var s = Combat.InitBossChallenge(new[] { Champ(50) }, 1, cfg, new Rng(1));
            s.Entities.Find(e => e.IsBoss)!.Stats[StatKey.Hp] = 1; // trivial to kill
            foreach (var p in s.Entities) if (p.Team == Team.Party) p.Stats[StatKey.Atk] = 100000;

            Combat.RunToEnd(s, cfg, new Rng(1));
            Assert.Equal(CombatStatus.Won, s.Status);
        }

        [Fact]
        public void BossChallengeLostWhenTimerExpires()
        {
            var cfg = GameConfig.Default();
            cfg.Balance.BossChallengeSeconds = 1; // expire fast
            var s = Combat.InitBossChallenge(new[] { Champ() }, 1, cfg, new Rng(1));
            foreach (var p in s.Entities) if (p.Team == Team.Party) p.Stats[StatKey.Atk] = 0; // can't kill

            Combat.RunToEnd(s, cfg, new Rng(1), maxSteps: 5000);
            Assert.Equal(CombatStatus.Lost, s.Status);
            Assert.True(s.TimeMs >= 1000);
        }

        [Fact]
        public void BossChallengeMajorBossIsTougher()
        {
            var cfg = GameConfig.Default();
            double Hp(int stage) =>
                Combat.InitBossChallenge(new[] { Champ() }, stage, cfg, new Rng(1)).Entities.First(e => e.IsBoss).MaxHp;

            Assert.True(Hp(10) > Hp(9));  // major boss at stage 10
            Assert.True(Hp(10) > Hp(11));
        }

        [Fact]
        public void BossChallengeIsDeterministic()
        {
            var cfg = GameConfig.Default();
            CombatState Build() => Combat.InitBossChallenge(new[] { Champ(8) }, 5, cfg, new Rng(3));
            var s1 = Build(); var s2 = Build();
            Combat.RunToEnd(s1, cfg, new Rng(3));
            Combat.RunToEnd(s2, cfg, new Rng(3));
            Assert.Equal(s1.Status, s2.Status);
            Assert.Equal(s1.TimeMs, s2.TimeMs);
        }

        // --- C1: in-place boss challenge (same map, no arena swap) ---

        [Fact]
        public void EnterBossChallengeSwapsTrashForBossInPlace()
        {
            var cfg = GameConfig.Default();
            var s = Combat.InitFarm(new[] { Champ() }, 3, cfg, new Rng(1));
            Assert.Contains(s.Entities, e => e.Team == Team.Enemy && !e.IsBoss); // farm has trash
            var hero = s.Entities.First(e => e.Team == Team.Party);
            var heroPos = hero.Pos;

            Combat.EnterBossChallenge(s, cfg);

            Assert.Equal(EncounterKind.BossChallenge, s.Kind);
            Assert.Equal(0, s.TimeMs);                                   // challenge timer reset
            Assert.Single(s.Entities, e => e.Team == Team.Enemy);        // only the boss remains
            Assert.Single(s.Entities, e => e.IsBoss);
            Assert.Equal(heroPos.X, hero.Pos.X, 6);                      // party stays put (same map)
            Assert.Equal(heroPos.Y, hero.Pos.Y, 6);
            var boss = s.Entities.First(e => e.IsBoss);
            Assert.Equal(heroPos.X + cfg.Balance.BossSpawnDistance, boss.Pos.X, 6); // boss appears just ahead
        }

        [Fact]
        public void EnterBossChallengeRestoresDownedParty()
        {
            var cfg = GameConfig.Default();
            var s = Combat.InitFarm(new[] { Champ() }, 1, cfg, new Rng(1));
            var hero = s.Entities.First(e => e.Team == Team.Party);
            hero.Hp = 0; hero.RespawnMs = 2000; // downed during farm

            Combat.EnterBossChallenge(s, cfg);

            Assert.Equal(hero.MaxHp, hero.Hp); // healed for a clean boss fight
            Assert.Equal(0, hero.RespawnMs);
        }

        [Fact]
        public void ResumeFarmDespawnsBossAndGatesNextPackByCooldown()
        {
            var cfg = GameConfig.Default();
            var s = Combat.InitFarm(new[] { Champ() }, 2, cfg, new Rng(1));
            Combat.EnterBossChallenge(s, cfg);
            Assert.Single(s.Entities, e => e.IsBoss);

            double cooldown = cfg.Balance.BossFleeCooldownMs; // 4s anti-spam lull
            Combat.ResumeFarm(s, 2, cfg, cooldown);

            Assert.Equal(EncounterKind.Farm, s.Kind);
            Assert.DoesNotContain(s.Entities, e => e.Team == Team.Enemy); // boss gone, no instant trash
            Assert.Equal(cooldown, s.SpawnTimerMs, 6);

            // step ~3s (< cooldown): still no trash — flee-spam can't refresh packs
            for (int i = 0; i < 90; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.DoesNotContain(s.Entities, e => e.Team == Team.Enemy);

            // step past the cooldown: a pack finally repopulates
            for (int i = 0; i < 60; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.Contains(s.Entities, e => e.Team == Team.Enemy);
        }

        // --- M8: live stat refresh (real-time leveling / gear) ---

        [Fact]
        public void RefreshPartyStatsAppliesLevelUpLive()
        {
            var save = Save.NewGame(1, Cfg, 0); // warrior h1, level 1
            var s = Combat.InitFarm(save.Heroes, 1, Cfg, new Rng(1));
            var hero = s.Entities.First(e => e.Team == Team.Party);
            double maxBefore = hero.MaxHp;

            var leveled = Progression.GrantPartyXp(save, 1_000_000, Cfg); // many levels
            Combat.RefreshPartyStats(s, leveled, Cfg);

            Assert.True(hero.MaxHp > maxBefore);     // tougher immediately
            Assert.True(hero.Hp > 0);                // healed by the gain, not reset
        }

        [Fact]
        public void RefreshPartyStatsAppliesEquippedGearLive()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var sword = new Item { Id = "i1", BaseId = "rusty_sword", Rarity = Rarity.Normal, ItemLevel = 1 };
            save = Inventory.AddItems(save, new[] { sword });

            var s = Combat.InitFarm(save.Heroes, 1, Cfg, new Rng(1));
            var hero = s.Entities.First(e => e.Team == Team.Party);
            double atkBefore = hero.Stats.Get(StatKey.Atk);

            save = Inventory.EquipItem(save, "h1", "i1", Cfg);
            Combat.RefreshPartyStats(s, save, Cfg);

            Assert.True(hero.Stats.Get(StatKey.Atk) > atkBefore); // weapon applied live
        }

        [Fact]
        public void RefreshPartyStatsDoesNotReviveDownedHero()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var s = Combat.InitFarm(save.Heroes, 1, Cfg, new Rng(1));
            var hero = s.Entities.First(e => e.Team == Team.Party);
            hero.Hp = 0; hero.RespawnMs = 2000; // downed

            Combat.RefreshPartyStats(s, Progression.GrantPartyXp(save, 1_000_000, Cfg), Cfg);

            Assert.Equal(0, hero.Hp);  // still down; respawn restores it to the new max
            Assert.True(hero.Downed);
        }

        // --- M8: per-kill gold + stage-scaled rewards ---

        [Fact]
        public void MonsterKillsAccruePendingGold()
        {
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Monster("E0", "slime", hp: 10),
                Monster("EBOSS", "goblin_king", hp: 10, boss: true));
            // default State Stage == 0 => KillRewardMult == 1
            Combat.RunToEnd(s, Cfg, new Rng(1));

            Assert.Equal(Cfg.Monsters["slime"].GoldReward + Cfg.Monsters["goblin_king"].GoldReward, s.PendingGold);
        }

        [Fact]
        public void DeeperStagesPayMorePerKill()
        {
            CombatState Build(int stage)
            {
                var s = State(Ent("A", Team.Party, hp: 1000, atk: 500, def: 0), Monster("E0", "slime", hp: 10));
                s.Stage = stage;
                return s;
            }

            var low = Build(1); Combat.RunToEnd(low, Cfg, new Rng(1));
            var high = Build(25); Combat.RunToEnd(high, Cfg, new Rng(1));

            Assert.True(high.PendingGold > low.PendingGold);
            Assert.True(high.PendingXp > low.PendingXp);
        }

        // --- HP regen ---

        [Fact]
        public void HpRegenHealsAliveEntitiesUpToMax()
        {
            // Far apart so nothing attacks; the run stays Running while A regenerates.
            var a = Ent("A", Team.Party, hp: 100, atk: 0, def: 0, x: -50);
            a.Hp = 50; a.Stats[StatKey.HpRegen] = 10; // 10 hp/sec
            var b = Ent("B", Team.Enemy, hp: 100, atk: 0, def: 0, x: 50);
            b.Hp = 50; // no regen
            var s = State(a, b);

            for (int i = 0; i < 60; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.True(Hp(s, "A") > 50);          // healed
            Assert.True(Hp(s, "A") <= 100);        // never above max
            Assert.Equal(50, Hp(s, "B"));          // no regen stat -> unchanged
        }

        [Fact]
        public void HpRegenNeverExceedsMaxHp()
        {
            var a = Ent("A", Team.Party, hp: 100, atk: 0, def: 0, x: -50);
            a.Hp = 99; a.Stats[StatKey.HpRegen] = 10000;
            var b = Ent("B", Team.Enemy, hp: 100, atk: 0, def: 0, x: 50);
            var s = State(a, b);

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.Equal(100, Hp(s, "A"));
        }

        // --- M10.1: mana resource ---

        [Fact]
        public void PartyHeroesStartWithFullMana()
        {
            var party = new[] { new HeroInstance { Id = "h1", DefId = "magician_basic", Level = 1 } };
            var s = Combat.InitCombat(party, 1, Cfg, new Rng(1));
            var hero = s.Entities.First(e => e.Team == Team.Party);

            Assert.True(hero.MaxMana > 0);
            Assert.Equal(hero.MaxMana, hero.Mana);
        }

        [Fact]
        public void MonstersHaveNoManaPool()
        {
            var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };
            var s = Combat.InitCombat(party, 1, Cfg, new Rng(1));
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy))
                Assert.Equal(0, e.MaxMana);
        }

        [Fact]
        public void ManaRegenFillsTowardMaxAndClamps()
        {
            // Far apart so nothing attacks; A regenerates mana while the run stays Running.
            var a = Ent("A", Team.Party, hp: 100, atk: 0, def: 0, x: -50);
            a.MaxMana = 100; a.Mana = 50; a.Stats[StatKey.ManaRegen] = 10; // 10 mana/sec
            var b = Ent("B", Team.Enemy, hp: 100, atk: 0, def: 0, x: 50);
            var s = State(a, b);

            for (int i = 0; i < 60; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            double mana = s.Entities.First(e => e.Id == "A").Mana;
            Assert.True(mana > 50);    // regenerated
            Assert.True(mana <= 100);  // never above max
        }

        [Fact]
        public void ManaRegenNeverExceedsMax()
        {
            var a = Ent("A", Team.Party, hp: 100, atk: 0, def: 0, x: -50);
            a.MaxMana = 100; a.Mana = 99; a.Stats[StatKey.ManaRegen] = 100000;
            var b = Ent("B", Team.Enemy, hp: 100, atk: 0, def: 0, x: 50);
            var s = State(a, b);

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.Equal(100, s.Entities.First(e => e.Id == "A").Mana);
        }

        [Fact]
        public void NoManaPoolMeansNoRegen()
        {
            var a = Ent("A", Team.Party, hp: 100, atk: 0, def: 0, x: -50);
            a.Stats[StatKey.ManaRegen] = 10; // regen stat but MaxMana stays 0
            var b = Ent("B", Team.Enemy, hp: 100, atk: 0, def: 0, x: 50);
            var s = State(a, b);

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.Equal(0, s.Entities.First(e => e.Id == "A").Mana);
        }

        [Fact]
        public void RefreshPartyStatsGrowsMaxManaOnLevelUp()
        {
            var save = Save.NewGame(1, Cfg, 0); // warrior h1, gains MaxMana per level
            var s = Combat.InitFarm(save.Heroes, 1, Cfg, new Rng(1));
            var hero = s.Entities.First(e => e.RefId == "h1");
            double manaBefore = hero.MaxMana;

            var leveled = Progression.GrantPartyXp(save, 1_000_000, Cfg);
            Combat.RefreshPartyStats(s, leveled, Cfg);

            Assert.True(hero.MaxMana > manaBefore);
        }

        [Fact]
        public void RespawnRefillsMana()
        {
            var tank = Ent("P_tank", Team.Party, hp: 1000, atk: 0, def: 0, x: -50);
            var enemy = Ent("E", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 50);
            var glass = Downed("P_glass", maxHp: 50, respawnMs: 100);
            glass.MaxMana = 80; glass.Mana = 0; // drained while downed
            var s = State(tank, enemy, glass);

            // 100ms / 30ms-step => respawns on the 4th step.
            for (int i = 0; i < 4; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            var g = s.Entities.First(e => e.Id == "P_glass");
            Assert.True(g.Alive);
            Assert.Equal(80, g.Mana); // came back ready to cast
        }

        // --- M11.2: attack speed (AtkSpd) vs movement speed (MoveSpd) ---

        [Fact]
        public void MagicianActsFasterThanWarrior()
        {
            var party = new[]
            {
                new HeroInstance { Id = "w", DefId = "warrior_basic", Level = 1 },
                new HeroInstance { Id = "m", DefId = "magician_basic", Level = 1 },
            };
            var s = Combat.InitFarm(party, 1, Cfg, new Rng(1));
            var w = s.Entities.First(e => e.RefId == "w");
            var m = s.Entities.First(e => e.RefId == "m");

            Assert.True(m.AttackIntervalMs < w.AttackIntervalMs); // higher AtkSpd => shorter interval
        }

        [Fact]
        public void HigherMoveSpdCoversMoreGround()
        {
            CombatState Walk(double moveSpd)
            {
                var p = Ent("P", Team.Party, hp: 100, atk: 0, def: 0, x: 0);
                p.Stats[StatKey.MoveSpd] = moveSpd;
                var e = Ent("E", Team.Enemy, hp: 100, atk: 0, def: 0, x: 50); // far -> P walks toward
                return State(p, e);
            }

            var fast = Walk(6.0);
            var slow = Walk(2.0);
            Combat.StepCombat(fast, Combat.DefaultStepMs, Cfg, new Rng(1));
            Combat.StepCombat(slow, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.True(fast.Entities.First(e => e.Id == "P").Pos.X > slow.Entities.First(e => e.Id == "P").Pos.X);
        }

        // --- soft-body collision: units occupy space and can't stack ---

        [Fact]
        public void OverlappingUnitsArePushedApart()
        {
            // two allies spawned on the exact same point (no enemies, so neither moves);
            // the separation pass shoves them apart to at least the sum of their radii
            var a = Ent("P0", Team.Party, hp: 100, atk: 0, def: 0, x: 0, y: 0);
            var b = Ent("P1", Team.Party, hp: 100, atk: 0, def: 0, x: 0, y: 0);
            var s = State(a, b);

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            double sep = Vec2.Distance(s.Entities.First(e => e.Id == "P0").Pos,
                                       s.Entities.First(e => e.Id == "P1").Pos);
            Assert.True(sep >= a.BodyRadius + b.BodyRadius - 1e-6);
        }

        [Fact]
        public void HeavierBodyMovesLessWhenSeparating()
        {
            // a chunky body (boss-sized) barely budges; the small one is shoved clear
            var small = Ent("A", Team.Party, hp: 100, atk: 0, def: 0, x: 0, y: 0);
            var big = Ent("B", Team.Party, hp: 100, atk: 0, def: 0, x: 0.5, y: 0);
            small.BodyRadius = 0.45;
            big.BodyRadius = 1.3;
            var s = State(small, big);

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            double aMoved = System.Math.Abs(s.Entities.First(e => e.Id == "A").Pos.X - 0.0);
            double bMoved = System.Math.Abs(s.Entities.First(e => e.Id == "B").Pos.X - 0.5);
            Assert.True(aMoved > bMoved);
        }

        [Fact]
        public void MeleeReachesABodiedTarget()
        {
            // a melee attacker (range 1.0) hits a chunky target 2.0 away at the centre:
            // range counts from the body edge (1.0 + 1.3), so the big body stays meleeable
            var a = Ent("A", Team.Party, hp: 100, atk: 10, def: 0, x: 0);
            var b = Ent("B", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 2.0);
            b.BodyRadius = 1.3;
            var s = State(a, b);

            var ev = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.Contains(ev, e => e.Type == CombatEventType.Hit && e.SourceId == "A" && e.TargetId == "B");
        }

        // --- M9.4: group vs solo party tactic ---

        // party spread vertically; one enemy nearest each hero, one nearest the centre
        private static CombatState TacticSetup() => State(
            Ent("P0", Team.Party, hp: 1000, atk: 0, def: 0, x: 0, y: 0),
            Ent("P1", Team.Party, hp: 1000, atk: 0, def: 0, x: 0, y: 10),
            Ent("EA", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 1, y: 2),  // near P0 + nearest centre (0,5)
            Ent("EB", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 1, y: 9)); // near P1

        [Fact]
        public void SoloTacticTargetsIndividually()
        {
            var s = TacticSetup();
            s.Tactic = PartyTactic.Solo;
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.Equal("EA", s.Entities.First(e => e.Id == "P0").TargetId);
            Assert.Equal("EB", s.Entities.First(e => e.Id == "P1").TargetId);
        }

        [Fact]
        public void GroupTacticFocusFiresSharedTarget()
        {
            var s = TacticSetup();
            s.Tactic = PartyTactic.Group;
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            // both heroes converge on the enemy nearest the party centre (EA)
            Assert.Equal("EA", s.Entities.First(e => e.Id == "P0").TargetId);
            Assert.Equal("EA", s.Entities.First(e => e.Id == "P1").TargetId);
        }

        // --- M9.3: ranged attacks + splash AoE ---

        [Fact]
        public void RangedAttackerHitsWithoutClosingToMelee()
        {
            // attacker with range 5 hits a target 4 away and stays put (no move toward)
            var a = Ent("A", Team.Party, hp: 100, atk: 10, def: 0, x: 0);
            a.Stats[StatKey.AttackRange] = 5;
            var b = Ent("B", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 4);
            var s = State(a, b);

            var events = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.Contains(events, e => e.Type == CombatEventType.Hit && e.SourceId == "A" && e.TargetId == "B");
            Assert.Equal(0, s.Entities.First(e => e.Id == "A").Pos.X); // didn't move
        }

        [Fact]
        public void MeleeAttackerWithoutRangeStatStillWorks()
        {
            // no AttackRange stat -> falls back to melee; far target gets approached, not hit
            var a = Ent("A", Team.Party, hp: 100, atk: 10, def: 0, x: 0);
            var b = Ent("B", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 5);
            var s = State(a, b);

            var events = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.DoesNotContain(events, e => e.Type == CombatEventType.Hit); // out of melee
            Assert.True(s.Entities.First(e => e.Id == "A").Pos.X > 0);          // moved closer
        }

        [Fact]
        public void SplashHitsNearbyEnemies()
        {
            // A strikes B; C is within splash radius of B and also takes a hit
            var a = Ent("A", Team.Party, hp: 100, atk: 50, def: 0, x: 0);
            a.Stats[StatKey.SplashRadius] = 2.0;
            var b = Ent("B", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 0.5);
            var c = Ent("C", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 1.5); // 1.0 from B
            var d = Ent("D", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 10);  // far away
            var s = State(a, b, c, d);

            var events = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            Assert.Contains(events, e => e.Type == CombatEventType.Hit && e.TargetId == "B");
            Assert.Contains(events, e => e.Type == CombatEventType.Hit && e.TargetId == "C"); // splashed
            Assert.DoesNotContain(events, e => e.Type == CombatEventType.Hit && e.TargetId == "D");
            Assert.True(Hp(s, "C") < 1000); // took damage
        }

        [Fact]
        public void NoSplashWithoutRadius()
        {
            var a = Ent("A", Team.Party, hp: 100, atk: 50, def: 0, x: 0); // no SplashRadius
            var b = Ent("B", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 0.5);
            var c = Ent("C", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 1.0);
            var s = State(a, b, c);

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.Equal(1000, Hp(s, "C")); // untouched
        }

        [Fact]
        public void MagicianIsFragileRangedHitter()
        {
            var w = Stats.ComputeHeroStats(new HeroInstance { Id = "w", DefId = "warrior_basic", Level = 1 }, Cfg);
            var m = Stats.ComputeHeroStats(new HeroInstance { Id = "m", DefId = "magician_basic", Level = 1 }, Cfg);

            Assert.True(m.Get(StatKey.Hp) < w.Get(StatKey.Hp));        // fragile
            Assert.True(m.Get(StatKey.Atk) > w.Get(StatKey.Atk));      // hits harder
            Assert.Equal(6.0, m.Get(StatKey.AttackRange));            // ranged
            Assert.True(m.Get(StatKey.AttackRange) > w.Get(StatKey.AttackRange));
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

        [Fact]
        public void PendingXpCommitsToPartyOnWin()
        {
            // a run yields PendingXp; committing it advances the real save's party hero
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Monster("EBOSS", "goblin_king", hp: 10, boss: true));
            Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.True(s.PendingXp > 0);

            var save = Save.NewGame(1, Cfg, 0);                 // warrior h1 in party, level 1, xp 0
            var after = Progression.GrantPartyXp(save, s.PendingXp, Cfg);

            var h1 = after.Heroes.Find(h => h.Id == "h1")!;
            Assert.Equal(s.PendingXp, h1.Xp);                    // goblin_king XP (60) < XpCurve(1) -> xp advances
            Assert.Equal(0, save.Heroes.Find(h => h.Id == "h1")!.Xp); // original untouched
        }

        // --- M4.3: hero downing + respawn ---

        // A downed party hero (Hp 0, RespawnMs pending) that doesn't act/get targeted.
        private static CombatEntity Downed(string id, double maxHp, double respawnMs, double x = 0)
        {
            var e = Ent(id, Team.Party, hp: maxHp, atk: 0, def: 0, x: x);
            e.Hp = 0;
            e.RespawnDurationMs = respawnMs;
            e.RespawnMs = respawnMs;
            return e;
        }

        [Fact]
        public void DownedHeroRespawnsToFullHpAfterTimer()
        {
            // Tank + far-away enemy keep the run Running and out of attack range so
            // nothing dies; the glass hero counts down and respawns.
            var s = State(
                Ent("P_tank", Team.Party, hp: 1000, atk: 0, def: 0, x: -50),
                Ent("E", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 50),
                Downed("P_glass", maxHp: 50, respawnMs: 100));

            // 100ms / 30ms-step => respawns on the 4th step (TimeMs 120).
            List<CombatEvent> all = new();
            for (int i = 0; i < 4; i++)
                all.AddRange(Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1)));

            var glass = s.Entities.First(e => e.Id == "P_glass");
            Assert.True(glass.Alive);
            Assert.Equal(50, glass.Hp);
            Assert.Equal(0, glass.RespawnMs);
            Assert.Contains(all, e => e.Type == CombatEventType.Respawn && e.EntityId == "P_glass");
            Assert.Equal(CombatStatus.Running, s.Status);
        }

        [Fact]
        public void DownedHeroDoesNotActOrGetTargetedWhileDown()
        {
            var enemy = Ent("E", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 0.5);
            var s = State(
                Ent("P_tank", Team.Party, hp: 1000, atk: 0, def: 0, x: -50),
                Downed("P_glass", maxHp: 50, respawnMs: 100000, x: 0),
                enemy);

            var events = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            // The in-range enemy targets the distant tank, never the adjacent downed hero,
            // and the downed hero never attacks.
            Assert.DoesNotContain(events, e => e.Type == CombatEventType.Hit &&
                                               (e.TargetId == "P_glass" || e.SourceId == "P_glass"));
        }

        [Fact]
        public void SimultaneousWipeLosesEvenThoughHeroIsDowned()
        {
            // Single party hero downed this step => no one alive => Lost.
            var hero = Ent("P", Team.Party, hp: 5, atk: 0, def: 0, x: 0);
            hero.RespawnDurationMs = 5000;
            var s = State(
                hero,
                Ent("E", Team.Enemy, hp: 1000, atk: 100, def: 0, spd: 5, x: 0.5));

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            var p = s.Entities.First(e => e.Id == "P");
            Assert.Equal(CombatStatus.Lost, s.Status);
            Assert.True(p.Downed);            // it was downed, not just killed...
            Assert.Equal(0, p.Hp);            // ...but the all-at-once wipe still lost the run
        }

        [Fact]
        public void RunTimesOutToLoss()
        {
            var cfg = GameConfig.Default();
            cfg.Balance.MaxRunSeconds = 0.05; // 50ms

            // Both teams alive and out of range => stalemate that should time out.
            var s = State(
                Ent("P", Team.Party, hp: 1000, atk: 0, def: 0, x: -50),
                Ent("E", Team.Enemy, hp: 1000, atk: 0, def: 0, x: 50));

            Combat.RunToEnd(s, cfg, new Rng(1));

            Assert.Equal(CombatStatus.Lost, s.Status);
            Assert.True(s.TimeMs >= 50);
        }

        [Fact]
        public void MonstersDoNotRespawn()
        {
            // Party kills a weak enemy; a tank enemy keeps the run going. The dead
            // enemy must stay dead (never gets a respawn timer).
            var s = State(
                Ent("P", Team.Party, hp: 1000, atk: 500, def: 0, x: 0),
                Ent("E_weak", Team.Enemy, hp: 10, atk: 0, def: 0, x: 0.5),
                Ent("E_tank", Team.Enemy, hp: 100000, atk: 0, def: 0, x: 50));

            for (int i = 0; i < 50; i++)
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            var weak = s.Entities.First(e => e.Id == "E_weak");
            Assert.False(weak.Alive);
            Assert.False(weak.Downed);
            Assert.Equal(0, weak.RespawnMs);
        }

        [Fact]
        public void DowningAndRespawnAreDeterministic()
        {
            // A lone level-1 hero into a tough stage will get downed and the run
            // resolves identically across seeds-equal runs.
            CombatState Build()
            {
                var party = new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };
                return Combat.InitCombat(party, 10, Cfg, new Rng(42));
            }

            var s1 = Build();
            var s2 = Build();
            var e1 = Combat.RunToEnd(s1, Cfg, new Rng(42));
            var e2 = Combat.RunToEnd(s2, Cfg, new Rng(42));

            Assert.Equal(s1.Status, s2.Status);
            Assert.Equal(s1.TimeMs, s2.TimeMs);
            Assert.Equal(e1.Count, e2.Count);
            Assert.Equal(e1.Count(e => e.Type == CombatEventType.Respawn),
                         e2.Count(e => e.Type == CombatEventType.Respawn));
        }
    }
}
