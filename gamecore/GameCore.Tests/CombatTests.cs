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
            var h2 = Assert.Single(s.Entities, e => e.RefKind == "hero" && e.RefId == "h2");
            // A freshly-added hero must spawn at full HP (not 0). This is the contract the
            // boss-win resume relies on when OnStageCleared fields a newly-unlocked hero.
            Assert.True(h2.MaxHp > 0 && h2.Hp == h2.MaxHp, $"re-fielded hero spawned at {h2.Hp}/{h2.MaxHp} HP");
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
        public void SoloFollowersRegroupBehindLeaderInsteadOfScattering()
        {
            var cfg = GameConfig.Default();
            var party = new[]
            {
                new HeroInstance { Id = "h1", DefId = "warrior_basic",  Level = 1 }, // slot 0 -> leader
                new HeroInstance { Id = "h2", DefId = "magician_basic", Level = 1 },
                new HeroInstance { Id = "h3", DefId = "thief_basic",    Level = 1 },
            };
            var s = Combat.InitFarm(party, 1, cfg, new Rng(1));
            s.Tactic = PartyTactic.Solo;
            s.Entities.RemoveAll(e => e.Team == Team.Enemy); // isolate formation travel from combat
            s.SpawnTimerMs = double.MaxValue;                // freeze trash spawns during the test

            var lead = s.Entities.First(e => e.RefId == "h1");
            var f2 = s.Entities.First(e => e.RefId == "h2");
            var f3 = s.Entities.First(e => e.RefId == "h3");
            lead.Pos = new Vec2(0, 0);
            f2.Pos = new Vec2(14, 9);    // scattered far from the leader, opposite sides
            f3.Pos = new Vec2(-11, -8);
            double before2 = Vec2.Distance(f2.Pos, lead.Pos);
            double before3 = Vec2.Distance(f3.Pos, lead.Pos);

            for (int i = 0; i < 300; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1)); // ~10s

            // Both wings collapse from far away into the triangle behind the (idle) leader,
            // rather than each wandering off on its own.
            Assert.True(Vec2.Distance(f2.Pos, lead.Pos) < before2 && Vec2.Distance(f3.Pos, lead.Pos) < before3);
            // f2 is the Fire Mage (ranged) so it parks at FormationRangedBack — the deepest slot,
            // which bounds the worst-case span for either wing.
            double span = System.Math.Sqrt(cfg.Balance.FormationRangedBack * cfg.Balance.FormationRangedBack
                                            + cfg.Balance.FormationSide * cfg.Balance.FormationSide) + 1.0;
            Assert.True(Vec2.Distance(f2.Pos, lead.Pos) <= span, $"f2 still scattered: {Vec2.Distance(f2.Pos, lead.Pos):0.0}");
            Assert.True(Vec2.Distance(f3.Pos, lead.Pos) <= span, $"f3 still scattered: {Vec2.Distance(f3.Pos, lead.Pos):0.0}");
            // And the wings sit BEHIND the leader (negative along the default heading +Y).
            Assert.True(f2.Pos.Y < lead.Pos.Y && f3.Pos.Y < lead.Pos.Y);
        }

        [Fact]
        public void ChosenLeaderLeadsTheFormationNotSlotZero()
        {
            var cfg = GameConfig.Default();
            var party = new[]
            {
                new HeroInstance { Id = "h1", DefId = "warrior_basic",  Level = 1 },
                new HeroInstance { Id = "h2", DefId = "magician_basic", Level = 1 },
                new HeroInstance { Id = "h3", DefId = "thief_basic",    Level = 1 },
            };
            var s = Combat.InitFarm(party, 1, cfg, new Rng(1));
            s.Tactic = PartyTactic.Solo;
            s.LeaderRefId = "h3";   // slot 2 leads, not slot 0
            s.Entities.RemoveAll(e => e.Team == Team.Enemy);
            s.SpawnTimerMs = double.MaxValue;

            var h1 = s.Entities.First(e => e.RefId == "h1");
            var h2 = s.Entities.First(e => e.RefId == "h2");
            var leader = s.Entities.First(e => e.RefId == "h3");
            leader.Pos = new Vec2(0, 0);
            h1.Pos = new Vec2(13, 10);
            h2.Pos = new Vec2(-12, -9);
            var leaderStart = leader.Pos;
            double b1 = Vec2.Distance(h1.Pos, leader.Pos), b2 = Vec2.Distance(h2.Pos, leader.Pos);

            for (int i = 0; i < 300; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            // The two non-leaders fall in behind the CHOSEN leader (h3), which itself idles.
            Assert.True(Vec2.Distance(h1.Pos, leader.Pos) < b1 && Vec2.Distance(h2.Pos, leader.Pos) < b2);
            // h2 is the Fire Mage (ranged) so it parks at FormationRangedBack — the deepest slot,
            // which bounds the worst-case span for either follower.
            double span = System.Math.Sqrt(cfg.Balance.FormationRangedBack * cfg.Balance.FormationRangedBack
                                            + cfg.Balance.FormationSide * cfg.Balance.FormationSide) + 1.0;
            Assert.True(Vec2.Distance(h1.Pos, leader.Pos) <= span && Vec2.Distance(h2.Pos, leader.Pos) <= span);
            // Leader has no pack to chase, so it holds near its start (only small collision
            // nudges from the wings settling in behind it — it never marches off).
            Assert.True(Vec2.Distance(leader.Pos, leaderStart) < 2.0, $"leader wandered to {leader.Pos.X:0.0},{leader.Pos.Y:0.0}");
        }

        [Fact]
        public void FollowerHoldsSlotAndIgnoresEnemyFarFromIt()
        {
            // Repro for "the Thief wanders off": a follower used to chase any enemy near ITSELF,
            // drifting away while the leader fought a different pack. Now it's leashed to its
            // slot, so a stray enemy away from that slot must not pull it out of formation.
            var leaderE = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leaderE.Slot = 0;
            // The follower starts already drifted far from the team, with stray enemy B right on
            // top of it but ~12 tiles from its formation slot (near the origin, behind the leader).
            var follower = Ent("P1", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: -10);
            follower.Slot = 1;
            // Tanky, damage-less enemies so nobody dies and the matchup stays put: A keeps the
            // leader anchored near the origin; B is the lone bait next to the drifted follower.
            var a = Ent("A", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 2, y: 0);
            var b = Ent("B", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -11);
            var s = State(leaderE, follower, a, b);
            s.Tactic = PartyTactic.Solo;

            for (int i = 0; i < 150; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1)); // ~5s

            // The follower climbs back from y=-10 and rejoins the leader near the origin, rather
            // than sticking to bait B far from its slot (which the old self-anchored engage did).
            double distToLeader = Vec2.Distance(follower.Pos, leaderE.Pos);
            Assert.True(follower.Pos.Y > -3.0 && distToLeader < 4.0,
                $"follower=({follower.Pos.X:0.0},{follower.Pos.Y:0.0}) didn't rejoin leader=({leaderE.Pos.X:0.0},{leaderE.Pos.Y:0.0}) distLeader={distToLeader:0.0}");
        }

        // --- Regroup hustle: a follower stranded far behind sprints at max(own, leader) speed so a
        // geared leader can't outrun an ungeared follower between packs. ---

        // Leader idles at the origin (no enemies), so heading = +Y and the melee rank-0 slot sits
        // at a fixed point behind it. FormationHome(leader@0,0, heading=+Y, melee, roleRank 0) —
        // melee followers now flank at FormationMeleeBack, not FormationBack.
        private static Vec2 Rank0Home(CombatEntity leader, GameConfig cfg) =>
            new Vec2(leader.Pos.X - cfg.Balance.FormationSide,
                     leader.Pos.Y - cfg.Balance.FormationMeleeBack);

        // A leader + one follower in Solo, no enemies (so the leader idles and the slot is fixed),
        // spawns frozen. Follower placed straight below its home so its move is a clean +Y step.
        private static (CombatState s, CombatEntity leader, CombatEntity follower) HustleSetup(
            GameConfig cfg, double ownMoveSpd, double leaderMoveSpd, double distBelowHome)
        {
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            leader.Stats[StatKey.MoveSpd] = leaderMoveSpd;
            var home = Rank0Home(leader, cfg);
            var follower = Ent("P1", Team.Party, hp: 200, atk: 0, def: 0, x: home.X, y: home.Y - distBelowHome);
            follower.Slot = 1;
            follower.Stats[StatKey.MoveSpd] = ownMoveSpd;
            var s = State(leader, follower);
            s.Tactic = PartyTactic.Solo;
            return (s, leader, follower);
        }

        [Fact]
        public void FarFollowerHustlesAtMaxOfOwnAndLeaderSpeed()
        {
            var cfg = GameConfig.Default();
            double own = 2.0, lead = 10.0;
            // 30 tiles below home => well past FormationBreakRadius (6). Leader idles (no enemies),
            // so its slot doesn't move and the whole step is a clean +Y translation.
            var (s, leader, follower) = HustleSetup(cfg, own, lead, distBelowHome: 30.0);
            var before = follower.Pos;

            double dt = Combat.DefaultStepMs;
            Combat.StepCombat(s, dt, cfg, new Rng(1));

            double moved = Vec2.Distance(follower.Pos, before);
            double expected = System.Math.Max(own, lead) * cfg.Balance.RegroupHustleMult * dt / 1000.0;
            Assert.True(leader.Pos.X == 0 && leader.Pos.Y == 0, $"leader drifted to {leader.Pos.X:0.00},{leader.Pos.Y:0.00}");
            Assert.True(System.Math.Abs(moved - expected) < 1e-6, $"moved {moved:0.0000}, expected {expected:0.0000}");
        }

        [Fact]
        public void NearFollowerMovesAtOwnSpeedNotHustle()
        {
            var cfg = GameConfig.Default();
            double own = 2.0, lead = 10.0;
            // 3 tiles below home: past the deadzone (0.6) but within FormationBreakRadius (6), so no
            // hustle — it eases in at its OWN speed even though the leader is far faster.
            var (s, leader, follower) = HustleSetup(cfg, own, lead, distBelowHome: 3.0);
            var before = follower.Pos;

            double dt = Combat.DefaultStepMs;
            Combat.StepCombat(s, dt, cfg, new Rng(1));

            double moved = Vec2.Distance(follower.Pos, before);
            double expected = own * dt / 1000.0;
            Assert.True(System.Math.Abs(moved - expected) < 1e-6, $"moved {moved:0.0000}, expected {expected:0.0000}");
        }

        [Fact]
        public void HustleUsesOwnSpeedWhenItExceedsLeaders()
        {
            var cfg = GameConfig.Default();
            double own = 8.0, lead = 3.0; // follower is the faster one
            var (s, leader, follower) = HustleSetup(cfg, own, lead, distBelowHome: 30.0);
            var before = follower.Pos;

            double dt = Combat.DefaultStepMs;
            Combat.StepCombat(s, dt, cfg, new Rng(1));

            double moved = Vec2.Distance(follower.Pos, before);
            double expected = own * cfg.Balance.RegroupHustleMult * dt / 1000.0; // max(own,leader)=own
            Assert.True(System.Math.Abs(moved - expected) < 1e-6, $"moved {moved:0.0000}, expected {expected:0.0000}");
        }

        [Fact]
        public void FollowerWithTargetNearSlotChasesAtOwnSpeedNotHustle()
        {
            var cfg = GameConfig.Default();
            double own = 2.0, lead = 10.0;
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            leader.Stats[StatKey.MoveSpd] = lead;
            // A tanky, harmless enemy sits in melee range directly in front of the leader (+Y), so
            // the leader stays put attacking it and the heading is a clean +Y — the rank-0 slot lands
            // at the same fixed point as the idle case. The enemy is within FormationBreakRadius of
            // that slot, so the follower has a target and must chase at its OWN speed (hustle never
            // applies to targeted movement). The enemy can't hurt anyone, so nothing dies or drifts.
            var enemy = Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 1.4);
            var home = Rank0Home(leader, cfg);
            // Follower 5 tiles below its slot: past the break radius from the slot, but it has the
            // enemy as a target, so the far-follower hustle must NOT kick in. It chases at own speed.
            var follower = Ent("P1", Team.Party, hp: 200, atk: 0, def: 0, x: home.X, y: home.Y - 5.0);
            follower.Slot = 1;
            follower.Stats[StatKey.MoveSpd] = own;
            var s = State(leader, follower, enemy);
            s.Tactic = PartyTactic.Solo;
            var before = follower.Pos;

            double dt = Combat.DefaultStepMs;
            Combat.StepCombat(s, dt, cfg, new Rng(1));

            // The follower advanced toward the enemy (up, +Y) at exactly its own speed — no hustle.
            double moved = Vec2.Distance(follower.Pos, before);
            double expected = own * dt / 1000.0;
            Assert.True(follower.Pos.Y > before.Y, "follower should advance toward the enemy above it");
            Assert.True(System.Math.Abs(moved - expected) < 1e-6, $"moved {moved:0.0000}, expected {expected:0.0000}");
            Assert.Equal("E", follower.TargetId);
        }

        // --- Role-aware formation + ranged fire-in-transit + panic kite ---------------------

        [Fact]
        public void MeleeFollowerHomesToShoulderSlot()
        {
            var cfg = GameConfig.Default();
            // Leader idles at origin (no enemies) => heading +Y; melee rank-0 slot is
            // (-FormationSide, -FormationMeleeBack). Follower placed far below walks +Y toward it.
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            var follower = Ent("P1", Team.Party, hp: 200, atk: 0, def: 0,
                               x: -cfg.Balance.FormationSide, y: -cfg.Balance.FormationMeleeBack - 10.0);
            follower.Slot = 1; // RangedRole stays false (melee)
            var s = State(leader, follower);
            s.Tactic = PartyTactic.Solo;
            s.Kind = EncounterKind.Farm; // no enemies -> Farm so an empty field doesn't auto-win
            s.SpawnTimerMs = double.MaxValue; // freeze trash spawns so we isolate formation travel
            var before = follower.Pos;

            for (int i = 0; i < 120; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            var home = new Vec2(-cfg.Balance.FormationSide, -cfg.Balance.FormationMeleeBack);
            Assert.True(follower.Pos.Y > before.Y, "melee follower should walk up toward its slot");
            Assert.True(Vec2.Distance(follower.Pos, home) < cfg.Balance.FormationDeadzone + 0.5,
                $"melee follower didn't settle at shoulder slot: ({follower.Pos.X:0.00},{follower.Pos.Y:0.00})");
        }

        [Fact]
        public void RangedFollowerHomesToCastingDistanceSlot()
        {
            var cfg = GameConfig.Default();
            // Same as above but RangedRole => the slot uses FormationRangedBack, farther back.
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            var follower = Ent("P1", Team.Party, hp: 200, atk: 0, def: 0,
                               x: -cfg.Balance.FormationSide, y: -cfg.Balance.FormationRangedBack - 10.0);
            follower.Slot = 1;
            follower.RangedRole = true;
            var s = State(leader, follower);
            s.Tactic = PartyTactic.Solo;
            s.Kind = EncounterKind.Farm;
            s.SpawnTimerMs = double.MaxValue;

            for (int i = 0; i < 120; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            var home = new Vec2(-cfg.Balance.FormationSide, -cfg.Balance.FormationRangedBack);
            Assert.True(Vec2.Distance(follower.Pos, home) < cfg.Balance.FormationDeadzone + 0.5,
                $"ranged follower didn't park at casting distance: ({follower.Pos.X:0.00},{follower.Pos.Y:0.00})");
        }

        [Fact]
        public void MixedPartyRanksFollowersWithinTheirRole()
        {
            var cfg = GameConfig.Default();
            // Leader (melee) + one melee + two ranged. Melee is roleRank 0 (side +). The two ranged
            // are roleRank 0 and 1 (sides + and -, same back row). Each starts far below its own slot
            // so its FIRST step direction reveals which slot it homes to (leader idles => heading +Y).
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            var mel = Ent("P1", Team.Party, hp: 200, atk: 0, def: 0, x: -3, y: -15); // melee roleRank 0
            mel.Slot = 1;
            var r0 = Ent("P2", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: -15);  // ranged roleRank 0
            r0.Slot = 2; r0.RangedRole = true;
            var r1 = Ent("P3", Team.Party, hp: 200, atk: 0, def: 0, x: 3, y: -15);  // ranged roleRank 1
            r1.Slot = 3; r1.RangedRole = true;
            var s = State(leader, mel, r0, r1);
            s.Tactic = PartyTactic.Solo;
            s.Kind = EncounterKind.Farm;
            s.SpawnTimerMs = double.MaxValue;

            // Expected homes: melee roleRank0 -> (-side, -meleeBack); ranged roleRank0 -> (-side,
            // -rangedBack); ranged roleRank1 -> (+side, -rangedBack). (perp = (-1,0), sideSign +/-.)
            double side = cfg.Balance.FormationSide;
            var homeMel = new Vec2(-side, -cfg.Balance.FormationMeleeBack);
            var homeR0 = new Vec2(-side, -cfg.Balance.FormationRangedBack);
            var homeR1 = new Vec2(+side, -cfg.Balance.FormationRangedBack);

            for (int i = 0; i < 300; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.True(Vec2.Distance(mel.Pos, homeMel) < 0.8, $"melee slot wrong: ({mel.Pos.X:0.0},{mel.Pos.Y:0.0})");
            Assert.True(Vec2.Distance(r0.Pos, homeR0) < 0.8, $"ranged rank0 slot wrong: ({r0.Pos.X:0.0},{r0.Pos.Y:0.0})");
            Assert.True(Vec2.Distance(r1.Pos, homeR1) < 0.8, $"ranged rank1 slot wrong: ({r1.Pos.X:0.0},{r1.Pos.Y:0.0})");
            // rank0 and rank1 ranged share the same back row (same Y), opposite sides.
            Assert.True(System.Math.Abs(r0.Pos.Y - r1.Pos.Y) < 0.6 && r0.Pos.X < 0 && r1.Pos.X > 0);
        }

        [Fact]
        public void SoftLeaderDefaultsToFirstMeleeAndHonorsExplicitRanged()
        {
            var cfg = GameConfig.Default();
            // line = [ranged(slot0), melee(slot1), ranged(slot2)]. No LeaderRefId => the MELEE
            // entity leads: it advances on a distant pack while the two ranged hold formation.
            var r0 = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            r0.Slot = 0; r0.RangedRole = true;
            var mel = Ent("P1", Team.Party, hp: 200, atk: 5, def: 0, x: 0.2, y: 0);
            mel.Slot = 1; // melee leader-to-be
            var r2 = Ent("P2", Team.Party, hp: 200, atk: 0, def: 0, x: -0.2, y: 0);
            r2.Slot = 2; r2.RangedRole = true;
            // A distant, harmless pack far up +Y for the leader to march toward (out of everyone's reach).
            var pack = Ent("Z", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 25);
            var s = State(r0, mel, r2, pack);
            s.Tactic = PartyTactic.Solo;
            double melBefore = mel.Pos.Y;

            for (int i = 0; i < 5; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            // The melee entity is the leader: it advanced toward the distant pack (+Y).
            Assert.True(mel.Pos.Y > melBefore + 0.1, $"melee didn't lead toward pack: y={mel.Pos.Y:0.00}");
            // The two ranged hold formation slots computed off the melee leader: both are BEHIND it.
            Assert.True(r0.Pos.Y < mel.Pos.Y && r2.Pos.Y < mel.Pos.Y, "ranged should trail the melee leader");

            // Explicit pick honors a ranged hero even though a melee exists.
            var s2 = State(
                Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0),
                Ent("P1", Team.Party, hp: 200, atk: 0, def: 0, x: 0.2, y: 0),
                Ent("P2", Team.Party, hp: 200, atk: 0, def: 0, x: -0.2, y: 0),
                Ent("Z", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 25));
            s2.Entities[0].Slot = 0; s2.Entities[0].RangedRole = true;
            s2.Entities[1].Slot = 1; // melee
            s2.Entities[2].Slot = 2; s2.Entities[2].RangedRole = true;
            s2.Tactic = PartyTactic.Solo;
            s2.LeaderRefId = "P0"; // the ranged one, explicitly
            double rLeaderBefore = s2.Entities[0].Pos.Y;
            for (int i = 0; i < 5; i++) Combat.StepCombat(s2, Combat.DefaultStepMs, cfg, new Rng(1));
            // The chosen ranged leader marches on the pack; the melee now trails it.
            Assert.True(s2.Entities[0].Pos.Y > rLeaderBefore + 0.1, "explicit ranged leader should lead");
            Assert.True(s2.Entities[1].Pos.Y < s2.Entities[0].Pos.Y, "melee should trail the chosen ranged leader");
        }

        [Fact]
        public void RangedFollowerFiresInTransitWithoutChasing()
        {
            var cfg = GameConfig.Default();
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            // Ranged follower stranded far from its slot (regrouping), reach 2.0. An aggro'd enemy
            // sits 1.5 tiles away (within reach 2.0 + bodyRadius 0.45) but ~12 tiles from the slot.
            var follower = Ent("P1", Team.Party, hp: 200, atk: 10, def: 0, x: 0, y: -12);
            follower.Slot = 1; follower.RangedRole = true;
            follower.Stats[StatKey.AttackRange] = 2.0;
            var enemy = Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -13.5); // 1.5 below follower
            enemy.Aggro = true;
            var s = State(leader, follower, enemy);
            s.Tactic = PartyTactic.Solo;
            double hpBefore = enemy.Hp;
            double fyBefore = follower.Pos.Y;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.True(enemy.Hp < hpBefore, "ranged follower should fire at the in-reach enemy");
            // It did NOT move toward the enemy (which is BELOW it at -13.5); it does not chase.
            Assert.True(follower.Pos.Y >= fyBefore - 1e-6, $"ranged follower chased down toward enemy: y={follower.Pos.Y:0.000}");

            // Same setup, melee follower => no attack; it just heads home (up, +Y), ignoring the enemy.
            var s2 = State(
                Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0),
                Ent("P1", Team.Party, hp: 200, atk: 10, def: 0, x: 0, y: -12),
                Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -13.5));
            s2.Entities[0].Slot = 0;
            s2.Entities[1].Slot = 1; // melee (RangedRole false)
            s2.Entities[1].Stats[StatKey.AttackRange] = 2.0;
            s2.Entities[2].Aggro = true;
            s2.Tactic = PartyTactic.Solo;
            double hp2 = s2.Entities[2].Hp;
            double y2 = s2.Entities[1].Pos.Y;
            Combat.StepCombat(s2, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.Equal(hp2, s2.Entities[2].Hp); // melee never fires in transit
            Assert.True(s2.Entities[1].Pos.Y > y2, "melee follower should move toward home (up), not attack");
        }

        [Fact]
        public void RangedFollowerDoesNotChaseEnemyBeyondReach()
        {
            var cfg = GameConfig.Default();
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            // Ranged follower regrouping, reach 2.0. Enemy is 3.45 below => 1 tile BEYOND reach
            // (2.0 + bodyRadius 0.45 = 2.45). It must NOT fire and must NOT chase — just home-move.
            var follower = Ent("P1", Team.Party, hp: 200, atk: 10, def: 0, x: 0, y: -12);
            follower.Slot = 1; follower.RangedRole = true;
            follower.Stats[StatKey.AttackRange] = 2.0;
            var enemy = Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -15.45); // 3.45 below
            enemy.Aggro = true;
            var s = State(leader, follower, enemy);
            s.Tactic = PartyTactic.Solo;
            double hpBefore = enemy.Hp;
            double yBefore = follower.Pos.Y;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal(hpBefore, enemy.Hp); // out of reach => no shot
            Assert.True(follower.Pos.Y > yBefore, "follower should move home (up), not chase the out-of-reach enemy");
            // Past the break radius (12 > 6) => it hustles home; confirm it moved MORE than own speed.
            double moved = Vec2.Distance(follower.Pos, new Vec2(0, yBefore));
            double ownStep = follower.Stats.Get(StatKey.MoveSpd) * Combat.DefaultStepMs / 1000.0;
            Assert.True(moved > ownStep + 1e-9, $"hustle should still apply: moved {moved:0.000}, own {ownStep:0.000}");
        }

        [Fact]
        public void RangedFollowerPanicRetreatsTowardLeaderWhileFiring()
        {
            var cfg = GameConfig.Default();
            // Leader parked far off (+X) so it neither collides with the follower nor engages the
            // enemy (enemy is >EngageRadius away from it) — isolates the retreat to a clean step.
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 30, y: 0);
            leader.Slot = 0;
            // Ranged follower with a valid in-reach target 1.0 below it (< PanicRadius 1.8). It fires
            // AND runs TOWARD the leader (not away from the threat, which used to run it off screen).
            var follower = Ent("P1", Team.Party, hp: 200, atk: 10, def: 0, x: 0, y: 0);
            follower.Slot = 1; follower.RangedRole = true;
            follower.Stats[StatKey.AttackRange] = 2.0;
            var enemy = Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -1.0);
            enemy.Aggro = true;
            var s = State(leader, follower, enemy);
            s.Tactic = PartyTactic.Solo;
            double hpBefore = enemy.Hp;
            var before = follower.Pos;
            double distLeaderBefore = Vec2.Distance(follower.Pos, leader.Pos);

            double dt = Combat.DefaultStepMs;
            Combat.StepCombat(s, dt, cfg, new Rng(1));

            Assert.True(enemy.Hp < hpBefore, "retreat must not cost DPS — the shot still lands");
            // Distance to the LEADER shrank (she ran to the party, not off into the wild).
            Assert.True(Vec2.Distance(follower.Pos, leader.Pos) < distLeaderBefore,
                $"follower should close on the leader: {Vec2.Distance(follower.Pos, leader.Pos):0.000} vs {distLeaderBefore:0.000}");
            // Step length = own MoveSpd * dt (MoveToward would only clamp shorter near arrival; leader is far).
            double expected = follower.Stats.Get(StatKey.MoveSpd) * dt / 1000.0;
            double moved = Vec2.Distance(follower.Pos, before);
            Assert.True(moved <= expected + 1e-6, $"retreat over-stepped: moved {moved:0.0000}, cap {expected:0.0000}");
            Assert.True(System.Math.Abs(moved - expected) < 1e-6, $"retreat moved {moved:0.0000}, expected {expected:0.0000}");
        }

        [Fact]
        public void PanickedCasterHoldsWithinHoldDistOfLeader()
        {
            var cfg = GameConfig.Default();
            // Follower already within PanicHoldDist (2.0) of the leader: it must NOT move this step
            // (hold position and keep firing).
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 1.0, y: 0); // 1.0 < 2.0 away
            leader.Slot = 0;
            var follower = Ent("P1", Team.Party, hp: 200, atk: 10, def: 0, x: 0, y: 0);
            follower.Slot = 1; follower.RangedRole = true;
            follower.Stats[StatKey.AttackRange] = 2.0;
            var enemy = Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -1.0); // inside PanicRadius
            enemy.Aggro = true;
            var s = State(leader, follower, enemy);
            s.Tactic = PartyTactic.Solo;
            double hpBefore = enemy.Hp;
            var before = follower.Pos;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.True(enemy.Hp < hpBefore, "held caster still fires");
            Assert.True(Vec2.Distance(follower.Pos, before) < 1e-6,
                $"caster within hold dist must not move: ({follower.Pos.X:0.000},{follower.Pos.Y:0.000})");
        }

        [Fact]
        public void PanicRetreatIsBoundedTowardLeader()
        {
            var cfg = GameConfig.Default();
            // Caster far behind the leader with the threat adjacent on the FAR side — the old
            // away-vector would run her ever farther from the party. Assert dist-to-leader shrinks
            // each step until <= PanicHoldDist, then stays bounded (no runaway).
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            var follower = Ent("P1", Team.Party, hp: 200, atk: 10, def: 0, x: 0, y: 10); // far behind (+Y)
            follower.Slot = 1; follower.RangedRole = true;
            follower.Stats[StatKey.AttackRange] = 2.0;
            // Threat sits just beyond the caster on the far side (+Y), inside PanicRadius of her.
            var enemy = Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 11.0);
            enemy.Aggro = true;
            var s = State(leader, follower, enemy);
            s.Tactic = PartyTactic.Solo;

            double start = Vec2.Distance(follower.Pos, leader.Pos);
            double prev = start;
            double hold = cfg.Balance.PanicHoldDist;
            for (int i = 0; i < 40; i++)
            {
                Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
                double d = Vec2.Distance(follower.Pos, leader.Pos);
                Assert.True(d <= start + 1e-6, $"step {i}: ran away from party (d={d:0.000} > start={start:0.000})");
                if (prev > hold + 1e-6)
                    Assert.True(d < prev + 1e-6, $"step {i}: dist to leader did not shrink ({d:0.000} vs {prev:0.000})");
                else
                    Assert.True(d <= hold + 0.5, $"step {i}: overshot hold band (d={d:0.000})");
                prev = d;
            }
            Assert.True(prev <= hold + 0.5, $"final dist to leader {prev:0.000} not held near {hold}");
        }

        [Fact]
        public void MeleeFollowerNeverPanicKites()
        {
            var cfg = GameConfig.Default();
            var leader = Ent("P0", Team.Party, hp: 200, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            // Like the kite test but the follower is MELEE: with an in-reach, aggro'd enemy right by
            // it, it stands and attacks — it must NOT back away. Follower sits AT its melee slot with
            // the enemy 1.0 below (inside PanicRadius and the slot's break radius) so it acquires and
            // attacks normally; a ranged hero would kite here, a melee must not.
            var home = new Vec2(-cfg.Balance.FormationSide, -cfg.Balance.FormationMeleeBack);
            var follower = Ent("P1", Team.Party, hp: 200, atk: 10, def: 0, x: home.X, y: home.Y);
            follower.Slot = 1; // melee
            follower.Stats[StatKey.AttackRange] = 2.0;
            var enemy = Ent("E", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: home.X, y: home.Y - 1.0);
            enemy.Aggro = true;
            var s = State(leader, follower, enemy);
            s.Tactic = PartyTactic.Solo;
            double hpBefore = enemy.Hp;
            var before = follower.Pos;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.True(enemy.Hp < hpBefore, "melee follower should attack the adjacent enemy");
            // No backpedal: the melee follower did not move away (+Y) from the enemy below it.
            Assert.True(follower.Pos.Y <= before.Y + 1e-6, $"melee should not kite: y={follower.Pos.Y:0.0000}");
        }

        // --- Melee-peel: a melee hero prefers an enemy attacking a ranged ally (Change 2) ---
        // Enemy TargetId is set by the sim each step BEFORE the party heroes acquire (actors run in
        // Ordinal Id order, so "E*" enemies act ahead of "P*" heroes). We therefore never pre-set
        // TargetId; instead we place each enemy so it NATURALLY targets the intended hero via
        // FindNearestEnemy (nearest, with a melee hero reading TankAggroBias=2.0 tiles closer). A
        // caster-attacker sits nearest the ranged ally; a leader-attacker sits nearest the melee hero.

        [Fact]
        public void MeleeLeaderPeelsForCasterOverNearerEnemy()
        {
            var cfg = GameConfig.Default();
            // Melee leader at origin; ranged caster ally up +Y. Two aggro'd enemies inside
            // EngageRadius: E0 nearer the leader (targets it), E1 nearer the caster (targets it).
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0; // melee (RangedRole false)
            var caster = Ent("P1", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 8);
            caster.Slot = 1; caster.RangedRole = true;
            var eLeader = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 2, y: 0);   // nearest leader
            eLeader.Aggro = true;
            var eCaster = Ent("E1", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 6);   // nearest caster
            eCaster.Aggro = true;
            var s = State(leader, caster, eLeader, eCaster);
            s.Tactic = PartyTactic.Solo;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal("P0", eLeader.TargetId);  // sanity: enemy targeting matched the geometry
            Assert.Equal("P1", eCaster.TargetId);
            // The leader peels to the FARTHER caster-attacker rather than the nearer enemy on itself.
            Assert.Equal("E1", leader.TargetId);
        }

        [Fact]
        public void MeleeFollowerPeelsForCasterOverNearerEnemy()
        {
            var cfg = GameConfig.Default();
            // A melee FOLLOWER (leashed to its slot) prefers a caster-attacker within the SAME slot
            // radius over a nearer enemy on itself. Leader at origin; its nearest enemy (E0, straight
            // +Y) fixes heading = +Y, so the melee follower's slot lands at (-Side,-MeleeBack) — the
            // same slot the panic test uses. Both peel enemies sit within FormationBreakRadius of it.
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0; // melee leader
            // Follower ENTITY placed away from the caster-attacker so the melee TankAggroBias can't
            // steal E1 onto it; its acquisition uses the slot, not this position.
            var follower = Ent("P1", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0.3, y: 1.5);
            follower.Slot = 1; // melee follower
            var caster = Ent("P2", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0.3, y: 4.3);
            caster.Slot = 2; caster.RangedRole = true;
            // E0 nearest the leader (fixes +Y heading) and nearest the follower entity (targets P1).
            var eFollower = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 2.5);
            eFollower.Aggro = true;
            // E1 unambiguously nearest the caster (targets P2); farther from the slot than E0, so a
            // plain-nearest pick would take E0 — the peel must override to E1.
            var eCaster = Ent("E1", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 4.0);
            eCaster.Aggro = true;
            var s = State(leader, follower, caster, eFollower, eCaster);
            s.Tactic = PartyTactic.Solo;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal("P1", eFollower.TargetId); // sanity: geometry pinned the enemy targeting
            Assert.Equal("P2", eCaster.TargetId);   // the caster-attacker naturally targets the ranged ally
            // The follower peels to the caster-attacker (E1) over the nearer enemy on its slot (E0).
            Assert.Equal("E1", follower.TargetId);
        }

        [Fact]
        public void NoDefenseNeededFallsBackToNearest()
        {
            var cfg = GameConfig.Default();
            // No enemy targets a ranged ally (the party is all-melee) => leader and melee follower
            // both pick exactly the nearest enemy, today's behavior.
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0; // melee leader
            var follower = Ent("P1", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -1);
            follower.Slot = 1; // melee follower
            var near = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 1, y: 0);   // nearest leader
            near.Aggro = true;
            var far = Ent("E1", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 5, y: 0);
            far.Aggro = true;
            var s = State(leader, follower, near, far);
            s.Tactic = PartyTactic.Solo;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal("E0", leader.TargetId); // nearest, no peel preference to override it
        }

        [Fact]
        public void RangedFollowerIgnoresPeelPreference()
        {
            var cfg = GameConfig.Default();
            // A RANGED follower must acquire by pure nearest (no peel). Construct a case that would
            // FLIP if the preference leaked: a nearer enemy on the ranged follower itself, and a
            // farther enemy attacking ANOTHER ranged ally. Pure-nearest picks the near one.
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0; // melee leader far from the action below
            // Two ranged allies down at -Y; the follower under test is P1.
            var follower = Ent("P1", Team.Party, hp: 1_000_000, atk: 10, def: 0, x: 0, y: -20);
            follower.Slot = 1; follower.RangedRole = true;
            follower.Stats[StatKey.AttackRange] = 2.0;
            var otherCaster = Ent("P2", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -24);
            otherCaster.Slot = 2; otherCaster.RangedRole = true;
            // Near enemy on the follower's slot (which is behind the far-off leader, so the follower is
            // stranded and regrouping). To exercise slot acquisition, put the follower AT its slot.
            // Simpler: give the follower a target set via its OWN slot region. Place the near enemy
            // right on the follower and the "defender" enemy nearer the other caster.
            var nearEnemy = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -21.5); // ~1.5 below follower
            nearEnemy.Aggro = true;
            var defenderEnemy = Ent("E1", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: -23.8); // nearest other caster
            defenderEnemy.Aggro = true;
            var s = State(leader, follower, otherCaster, nearEnemy, defenderEnemy);
            s.Tactic = PartyTactic.Solo;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal("P2", defenderEnemy.TargetId); // the defender-enemy attacks the OTHER caster
            // The ranged follower fires at the NEAREST in-reach enemy (fire-in-transit), NOT the
            // farther caster-attacker — the peel preference does not leak to ranged acquisition.
            Assert.Equal("E0", follower.TargetId);
        }

        // --- Sticky heading + sticky leader target + arrival cap (stutter fix) ---

        [Fact]
        public void FormationHeadingFreezesWhileLeaderEngaged()
        {
            var cfg = GameConfig.Default();
            // Lone leader (=> it IS the leader) engaged: E0 sits inside EngageRadius, so the leader
            // is fighting, not traveling. The stored heading must not budge even when a second enemy
            // becomes the nearest pack on the OPPOSITE side across steps (the old per-step recompute
            // whipped it around the noisy ~1-unit vector).
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            var e0 = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 2);  // nearest, fixes +Y
            e0.Aggro = true;
            var e1 = Ent("E1", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 12); // farther, same side for now
            e1.Aggro = true;
            var s = State(leader, e0, e1);
            s.Tactic = PartyTactic.Solo;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            var frozen = s.FormationHeading;
            Assert.True(frozen.Y > 0.9, $"heading should point +Y toward E0, got ({frozen.X:0.00},{frozen.Y:0.00})");

            // Now yank E1 to the opposite side and make it the nearest pack; leader stays engaged (E0
            // still ~2 tiles away < EngageRadius), so the heading must stay frozen.
            e1.Pos = new Vec2(0, -1);
            for (int i = 0; i < 5; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal(frozen.X, s.FormationHeading.X, 6);
            Assert.Equal(frozen.Y, s.FormationHeading.Y, 6);
        }

        [Fact]
        public void FormationHeadingUpdatesWhileTraveling()
        {
            var cfg = GameConfig.Default();
            // Leader with its nearest pack BEYOND EngageRadius (14) is traveling, not fighting — the
            // long leader→pack vector is stable, so the heading tracks it.
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            leader.Stats[StatKey.MoveSpd] = 0.0001; // effectively pinned so the vector stays clean this step
            var pack = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 20, y: 0); // 20 > EngageRadius
            pack.Aggro = true;
            var s = State(leader, pack);
            s.Tactic = PartyTactic.Solo;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            double hx = pack.Pos.X - leader.Pos.X, hy = pack.Pos.Y - leader.Pos.Y;
            double hl = System.Math.Sqrt(hx * hx + hy * hy);
            Assert.Equal(hx / hl, s.FormationHeading.X, 4);
            Assert.Equal(hy / hl, s.FormationHeading.Y, 4);
        }

        [Fact]
        public void FreshStateAdoptsHeadingEvenWhenLeaderEngaged()
        {
            var cfg = GameConfig.Default();
            // On the FIRST step of a fresh CombatState the stored heading is (0,0); it must adopt
            // whatever first candidate appears even though the leader is already engaged (uninitialized
            // => adopt regardless of travel distance).
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            var e0 = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 3, y: 0); // within EngageRadius
            e0.Aggro = true;
            var s = State(leader, e0);
            s.Tactic = PartyTactic.Solo;
            Assert.True(s.FormationHeading.X == 0 && s.FormationHeading.Y == 0); // uninitialized

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.False(s.FormationHeading.X == 0 && s.FormationHeading.Y == 0);
            Assert.True(s.FormationHeading.X > 0.9, $"heading should adopt +X toward E0, got ({s.FormationHeading.X:0.00},{s.FormationHeading.Y:0.00})");
        }

        [Fact]
        public void LeaderKeepsCurrentTargetUntilItLeavesReach()
        {
            var cfg = GameConfig.Default();
            // Leader hitting A; B then becomes strictly nearer. The leader must KEEP A (sticky target)
            // rather than flap to B — re-acquiring "nearest" per step dragged the whole wing around.
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0;
            var a = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 3, y: 0);
            a.Aggro = true;
            var b = Ent("E1", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 10, y: 0); // farther for now
            b.Aggro = true;
            var s = State(leader, a, b);
            s.Tactic = PartyTactic.Solo;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.Equal("E0", leader.TargetId); // acquired A

            b.Pos = new Vec2(1, 0); // B now strictly nearer than A
            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.Equal("E0", leader.TargetId); // still A — sticky, not the nearer B

            // A dies/leaves reach => re-acquire (B).
            a.Hp = 0;
            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.Equal("E1", leader.TargetId);
        }

        [Fact]
        public void LeaderPeelOverridesStickyCurrentTarget()
        {
            var cfg = GameConfig.Default();
            // Leader locked onto A (not attacking a caster). B (also in reach) attacks a ranged ally.
            // Peel must still win over the sticky-target hold: the leader switches to the caster-attacker.
            var leader = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            leader.Slot = 0; // melee
            var caster = Ent("P1", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 8);
            caster.Slot = 1; caster.RangedRole = true;
            var a = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 2, y: 0); // nearest leader => targets P0
            a.Aggro = true;
            var s = State(leader, caster, a);
            s.Tactic = PartyTactic.Solo;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.Equal("E0", leader.TargetId); // locked onto A

            // Now introduce B nearest the caster (so it naturally targets the ranged ally): peel wins.
            var b = Ent("E1", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 6);
            b.Aggro = true;
            s.Entities.Add(b);
            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal("P1", b.TargetId);       // sanity: B attacks the caster
            Assert.Equal("E1", leader.TargetId);  // peel overrides the sticky hold on A
        }

        [Fact]
        public void ApproachingAttackerStopsJustInsideReachWithoutOvershoot()
        {
            var cfg = GameConfig.Default();
            // A lone attacker approaching a distant target must settle at dist ≈ range - ArriveDepth
            // and then hold (no in/out flap) while it keeps attacking. Ranged attacker (AttackRange 3
            // => centre-seeking, so the settle distance is radial and exact) with a tanky target so
            // nothing dies and the bodies never overlap (no collision perturbation). Group tactic so
            // this is a plain acquire-and-approach with no formation slotting.
            var attacker = Ent("P0", Team.Party, hp: 1_000_000, atk: 0, def: 0, x: 0, y: 0);
            attacker.Stats[StatKey.AttackRange] = 3.0; // > 2 => not melee => aims at target centre
            attacker.RangedRole = true;
            // Target stays PUT so we isolate the ATTACKER's approach: the first shot wakes it (aggro),
            // after which it would normally close the gap — so pin its move speed to a hair above 0
            // (a real 0 hits the MoveSpeed fallback), leaving it effectively stationary over the run.
            var target = Ent("E0", Team.Enemy, hp: 1_000_000, atk: 0, def: 0, x: 20, y: 0);
            target.Stats[StatKey.MoveSpd] = 1e-6;
            var s = State(attacker, target);
            s.Tactic = PartyTactic.Group;

            for (int i = 0; i < 200; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            double range = attacker.Stats.Get(StatKey.AttackRange) + target.BodyRadius;
            double dist = Vec2.Distance(attacker.Pos, target.Pos);
            Assert.True(System.Math.Abs(dist - (range - 0.05)) < 1e-4,
                $"attacker settled at dist={dist:0.000}, expected ≈ {range - 0.05:0.000}");
            Assert.True(dist <= range, "attacker must be in reach so it keeps attacking");

            // Next step moves it no farther (the flap is gone).
            var pos = attacker.Pos;
            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.True(Vec2.Distance(attacker.Pos, pos) < 1e-6,
                $"attacker still shuffling: moved {Vec2.Distance(attacker.Pos, pos):0.000000}");
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

        // --- Pack variety: elite/rare mobs (Lever 1) ---

        // RollRank always consumes exactly one rng draw regardless of outcome, so forcing the
        // chances doesn't shift the rng stream — base stats/positions match a normal spawn and
        // only the rank multiplier differs. That's what lets these compare cleanly by seed.
        private static GameConfig RankCfg(double elite, double rare)
        {
            var c = GameConfig.Default();
            c.Balance.EliteChance = elite;
            c.Balance.RareChance = rare;
            return c;
        }

        [Fact]
        public void NoRankChanceLeavesAllTrashNormal()
        {
            var s = Combat.InitFarm(new[] { Champ() }, 5, RankCfg(0, 0), new Rng(1));
            Assert.All(s.Entities.Where(e => e.Team == Team.Enemy),
                       e => Assert.Equal(MonsterRank.Normal, e.Rank));
        }

        [Fact]
        public void EliteIsTougherAndBiggerThanNormalTrash()
        {
            var party = new[] { Champ() };
            var normal = Combat.InitFarm(party, 5, RankCfg(0, 0), new Rng(1)).Entities.First(e => e.Team == Team.Enemy);
            var elite = Combat.InitFarm(party, 5, RankCfg(1.0, 0), new Rng(1)).Entities.First(e => e.Team == Team.Enemy);

            Assert.Equal(MonsterRank.Elite, elite.Rank);
            Assert.Equal(normal.MaxHp * Cfg.Balance.EliteHpMult, elite.MaxHp, 3);
            Assert.Equal(elite.MaxHp, elite.Hp, 3);                 // spawns at full
            Assert.True(elite.BodyRadius > normal.BodyRadius);
        }

        [Fact]
        public void RareIsTougherThanElite()
        {
            var party = new[] { Champ() };
            var elite = Combat.InitFarm(party, 5, RankCfg(1.0, 0), new Rng(1)).Entities.First(e => e.Team == Team.Enemy);
            var rare = Combat.InitFarm(party, 5, RankCfg(0, 1.0), new Rng(1)).Entities.First(e => e.Team == Team.Enemy);

            Assert.Equal(MonsterRank.Rare, rare.Rank);
            Assert.True(rare.MaxHp > elite.MaxHp);
            Assert.True(rare.BodyRadius > elite.BodyRadius);
        }

        [Fact]
        public void RankDropsAreGuaranteedBundleAndDeterministic()
        {
            var ctx = LootContext.ForStage(Cfg.Stages[9], Cfg); // a deeper stage for richer rolls
            var a = Loot.RollRankDrops(new Rng(7), ctx, Cfg, count: 4, rateMult: 5.0);
            var b = Loot.RollRankDrops(new Rng(7), ctx, Cfg, count: 4, rateMult: 5.0);

            Assert.Equal(4, a.Count);
            Assert.Equal(a.Select(i => i.Rarity), b.Select(i => i.Rarity)); // same seed => same bundle
            Assert.All(a, i => Assert.True(i.Rarity <= Rarity.Rare));        // trash ceiling holds
        }

        [Fact]
        public void RefreshPartyStatsSyncsNewlyRevealedKitActives()
        {
            // Leveling past a kit skill's UnlockLevel takes effect live: RefreshPartyStats
            // re-derives the entity's actives from the kit, so no run restart is needed.
            var cfg = GameConfig.Default();
            var save = Save.NewGame(1, cfg, 0);
            var heroId = save.Heroes[0].Id;
            var s = Combat.InitFarm(new[] { save.Heroes[0] }, 1, cfg, new Rng(1));

            var ent = s.Entities.First(e => e.Team == Team.Party && e.RefId == heroId);
            Assert.Equal(new[] { "cycloneslash" }, ent.Skills); // level 1: only the first active is revealed

            save = Progression.GrantPartyXp(save, 2_000_000, cfg); // well past shieldcharge's UnlockLevel 10
            Combat.RefreshPartyStats(s, save, cfg);
            Assert.Equal(new[] { "cycloneslash", "shieldcharge" }, ent.Skills);
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
        public void SoloTacticLeaderLeadsFollowersDoNotPeelOff()
        {
            var s = TacticSetup();
            s.Tactic = PartyTactic.Solo;
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            // The leader (lowest slot/id = P0) engages the enemy nearest it...
            Assert.Equal("EA", s.Entities.First(e => e.Id == "P0").TargetId);
            // ...while the follower holds formation rather than peeling off to EB — the enemy
            // nearest ITSELF — which was the old "each hero targets individually" behavior.
            Assert.NotEqual("EB", s.Entities.First(e => e.Id == "P1").TargetId);
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
            Assert.Equal((long)(Cfg.Monsters["slime"].XpReward + Cfg.Monsters["goblin_king"].XpReward), s.PendingXp);
        }

        [Fact]
        public void SyntheticTestEntitiesGiveNoXp()
        {
            var s = State(
                Ent("A", Team.Party, hp: 1000, atk: 500, def: 0),
                Ent("B", Team.Enemy, hp: 10, atk: 0, def: 0)); // RefKind "test"
            Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Equal(0L, s.PendingXp);
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
            Assert.Equal(0L, save.Heroes.Find(h => h.Id == "h1")!.Xp); // original untouched
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
        public void BossChallengeHeroDoesNotRespawn()
        {
            // A hero downed during a boss challenge stays dead (no respawn timer) — the boss
            // is a real wall, unlike farm where heroes go down and come back.
            var hero = Ent("P", Team.Party, hp: 5, atk: 0, def: 0, x: 0);
            hero.RespawnDurationMs = 100; // would respawn quickly in farm
            var tank = Ent("P_tank", Team.Party, hp: 100000, atk: 0, def: 0, x: -50); // keeps the run alive
            var s = State(hero, tank,
                Ent("EBOSS", Team.Enemy, hp: 1000, atk: 100, def: 0, spd: 5, x: 0.5));
            s.Kind = EncounterKind.BossChallenge;

            for (int i = 0; i < 10; i++) // well past the 100ms farm respawn window
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));

            var p = s.Entities.First(e => e.Id == "P");
            Assert.False(p.Alive);
            Assert.False(p.Downed);          // no frozen respawn timer — just dead
            Assert.Equal(0, p.RespawnMs);
        }

        [Fact]
        public void BossChallengeWipesWhenEveryHeroDies()
        {
            // No respawns during a boss => once every hero is down the run is Lost.
            var s = State(
                Ent("P1", Team.Party, hp: 5, atk: 0, def: 0, x: 0.5),
                Ent("P2", Team.Party, hp: 5, atk: 0, def: 0, x: 0.6),
                Ent("EBOSS", Team.Enemy, hp: 100000, atk: 100, def: 0, spd: 5, x: 0));
            s.Kind = EncounterKind.BossChallenge;

            Combat.RunToEnd(s, Cfg, new Rng(1), maxSteps: 5000);

            Assert.Equal(CombatStatus.Lost, s.Status);
            Assert.DoesNotContain(s.Entities, e => e.Team == Team.Party && e.Alive);
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

        // ---- Tank aggro bias + melee surround ring ----------------------------------------

        // A melee hero flagged as a tank soaks a monster's aggro even when a ranged hero is
        // slightly closer — the monster reads the melee hero TankAggroBias tiles nearer.
        [Fact]
        public void MonsterPrefersMeleeHeroWithinBiasMargin()
        {
            var cfg = GameConfig.Default();
            double bias = cfg.Balance.TankAggroBias; // 2.0
            // Ranged hero is 0.5 tiles CLOSER than the melee hero — but that's inside the bias
            // margin, so the biased distance (melee − bias) still wins. Monster at origin.
            var melee = Ent("HMELEE", Team.Party, hp: 500, atk: 0, def: 0, x: -6.0, y: 0);
            melee.RangedRole = false;
            var ranged = Ent("HRANGED", Team.Party, hp: 500, atk: 0, def: 0, x: 5.5, y: 0);
            ranged.RangedRole = true;
            var mob = Ent("EMOB", Team.Enemy, hp: 500, atk: 0, def: 0, x: 0, y: 0);
            mob.Aggro = true;
            var s = State(melee, ranged, mob);
            s.Kind = EncounterKind.Farm;      // no auto-win with enemies alive
            s.SpawnTimerMs = double.MaxValue; // freeze spawns

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal("HMELEE", mob.TargetId);
        }

        // Beyond the bias margin (ranged much closer) the monster still picks the ranged hero —
        // the bias is a nudge, not an override.
        [Fact]
        public void MonsterPicksRangedHeroBeyondBiasMargin()
        {
            var cfg = GameConfig.Default();
            var melee = Ent("HMELEE", Team.Party, hp: 500, atk: 0, def: 0, x: -6.0, y: 0);
            melee.RangedRole = false;
            // Ranged hero is 5 tiles closer than the melee — well past the 2.0 bias margin.
            var ranged = Ent("HRANGED", Team.Party, hp: 500, atk: 0, def: 0, x: 1.0, y: 0);
            ranged.RangedRole = true;
            var mob = Ent("EMOB", Team.Enemy, hp: 500, atk: 0, def: 0, x: 0, y: 0);
            mob.Aggro = true;
            var s = State(melee, ranged, mob);
            s.Kind = EncounterKind.Farm;
            s.SpawnTimerMs = double.MaxValue;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal("HRANGED", mob.TargetId);
        }

        // The bias must NEVER leak to hero-side acquisition: a hero picks purely by distance.
        // Setup would FLIP if the bias applied — the farther enemy is the melee-flagged one, so a
        // leaked "melee reads closer" bias would wrongly select it. The hero targets the nearer.
        [Fact]
        public void HeroTargetingIsPureNearestNoBiasLeak()
        {
            var cfg = GameConfig.Default();
            var hero = Ent("P0", Team.Party, hp: 500, atk: 0, def: 0, x: 0, y: 0);
            hero.Slot = 0;
            // near enemy is RangedRole (would keep raw distance), far enemy is melee-flagged
            // (would get −bias if the bias leaked). Both within the leader EngageRadius.
            var near = Ent("ENEAR", Team.Enemy, hp: 500, atk: 0, def: 0, x: 3.0, y: 0);
            near.RangedRole = true;
            var far = Ent("EFAR", Team.Enemy, hp: 500, atk: 0, def: 0, x: -4.0, y: 0);
            far.RangedRole = false;
            var s = State(hero, near, far);
            s.Tactic = PartyTactic.Solo; // lone hero => leader, uses EngageRadius acquisition
            s.Kind = EncounterKind.Farm;
            s.SpawnTimerMs = double.MaxValue;

            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.Equal("ENEAR", hero.TargetId); // nearest, bias absent hero-side
        }

        // Two melee attackers on the same target, both starting from the SAME spot, fan out to
        // DISTINCT rim contact points instead of stacking on the target's centre.
        [Fact]
        public void MeleeAttackersSurroundToDistinctPoints()
        {
            var cfg = GameConfig.Default();
            var target = Ent("ETGT", Team.Enemy, hp: 100000, atk: 0, def: 0, x: 0, y: 0);
            // Two monster attackers (so acquisition is the plain FindNearestEnemy path), both
            // melee (no AttackRange stat => MeleeRange), starting 5 tiles out at nearly the same
            // spot. Aggro so they seek immediately.
            var a = Ent("EA", Team.Enemy, hp: 500, atk: 0, def: 0, x: -5.0, y: 0.0);
            var b = Ent("EB", Team.Enemy, hp: 500, atk: 0, def: 0, x: -5.0, y: 0.01);
            var hero = Ent("PHERO", Team.Party, hp: 100000, atk: 0, def: 0, x: 0, y: 0);
            hero.RangedRole = false;
            // Retarget the monsters onto the party hero at origin; give the "target" dummy a huge
            // hp and no team conflict — actually both monsters target the hero. Use hero as centre.
            var s = State(hero, a, b);
            s.Kind = EncounterKind.Farm;
            s.SpawnTimerMs = double.MaxValue;
            a.Aggro = true; b.Aggro = true;

            // Walk until both are within reach of the hero at origin.
            for (int i = 0; i < 400; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            // Distinct approach directions => their rim points differ; assert real separation.
            double sep = Vec2.Distance(a.Pos, b.Pos);
            Assert.True(sep > 0.3, $"melee attackers stacked on centre (sep={sep:0.000})");
        }

        // Same surround scenario twice from identical states => byte-identical positions.
        [Fact]
        public void SurroundIsDeterministic()
        {
            CombatState Build()
            {
                var cfg = GameConfig.Default();
                var hero = Ent("PHERO", Team.Party, hp: 100000, atk: 0, def: 0, x: 0, y: 0);
                var a = Ent("EA", Team.Enemy, hp: 500, atk: 0, def: 0, x: -5.0, y: 0.0);
                var b = Ent("EB", Team.Enemy, hp: 500, atk: 0, def: 0, x: -5.0, y: 0.01);
                a.Aggro = true; b.Aggro = true;
                var s = State(hero, a, b);
                s.Kind = EncounterKind.Farm;
                s.SpawnTimerMs = double.MaxValue;
                return s;
            }
            var cfg = GameConfig.Default();
            var s1 = Build();
            var s2 = Build();
            for (int i = 0; i < 200; i++)
            {
                Combat.StepCombat(s1, Combat.DefaultStepMs, cfg, new Rng(1));
                Combat.StepCombat(s2, Combat.DefaultStepMs, cfg, new Rng(1));
            }
            for (int i = 0; i < s1.Entities.Count; i++)
            {
                Assert.Equal(s1.Entities[i].Pos.X, s2.Entities[i].Pos.X);
                Assert.Equal(s1.Entities[i].Pos.Y, s2.Entities[i].Pos.Y);
            }
        }

        // Contact-point seeking still closes: a lone melee attacker 5 tiles out reaches reach and
        // lands a hit within a generous step bound.
        [Fact]
        public void MeleeAttackerClosesAndHits()
        {
            var cfg = GameConfig.Default();
            var hero = Ent("PHERO", Team.Party, hp: 100000, atk: 0, def: 0, x: 0, y: 0);
            var mob = Ent("EMOB", Team.Enemy, hp: 500, atk: 20, def: 0, x: 5.0, y: 0);
            mob.Aggro = true;
            var s = State(hero, mob);
            s.Kind = EncounterKind.Farm;
            s.SpawnTimerMs = double.MaxValue;

            bool hit = false;
            for (int i = 0; i < 300 && !hit; i++)
            {
                var evs = Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
                if (evs.Any(e => e.Type == CombatEventType.Hit && e.SourceId == "EMOB")) hit = true;
            }
            Assert.True(hit, "melee attacker never closed to attack via the contact point");
        }

        // Ranged attackers keep CENTRE-seeking: a ranged mover beyond its range heads straight at
        // the target's centre line, not a rim point (its heading points AT the target).
        [Fact]
        public void RangedAttackerSeeksCenterLine()
        {
            var cfg = GameConfig.Default();
            // Ranged attacker: AttackRange stat = 6 (> 2.0 melee cutoff). Start 10 tiles out on the
            // +X axis of the target so a centre-seek move keeps it exactly on the axis (y stays 0),
            // whereas a rim-offset would knock y off-axis.
            var target = Ent("ETGT", Team.Party, hp: 100000, atk: 0, def: 0, x: 0, y: 0);
            var shooter = Ent("ESHOOT", Team.Enemy, hp: 500, atk: 0, def: 0, x: 10.0, y: 0);
            shooter.Stats[StatKey.AttackRange] = 6.0;
            shooter.Aggro = true;
            var s = State(target, shooter);
            s.Kind = EncounterKind.Farm;
            s.SpawnTimerMs = double.MaxValue;
            double y0 = shooter.Pos.Y;

            // One step of movement: it must move toward the target (x decreases) and stay on the
            // centre line (y unchanged) — proving it seeks the centre, not an off-axis rim point.
            Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            Assert.True(shooter.Pos.X < 10.0, "ranged attacker didn't advance toward target");
            Assert.Equal(y0, shooter.Pos.Y, 6); // stayed on the target's centre line
        }
    }
}
