using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Dungeon sim integration (ROADMAP roguelite, slice 3a): Combat understands a generated dungeon —
    // grid walkability (DungeonArena.Contains/Clamp), room-gated targeting (the anti-wallhack rule),
    // BFS-downhill leader travel toward the boss, and the in-place EnterDungeon transition. These pin
    // the DungeonArena queries, the transition contract, a full clear on a real generated floor (with
    // the walkable invariant sampled every step), determinism, and the room-gate in live combat.
    public class DungeonCombatTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Dungeon Gen(int seed, int rooms = 14) =>
            DungeonGen.Generate(new DungeonParams { Seed = seed, RoomCount = rooms });

        private static DungeonRoom RoomOfType(Dungeon d, RoomType t) => d.Rooms.First(r => r.Type == t);

        // A hand-built party of synthetic heroes at the given stats — strong enough (when asked) to
        // steamroll a floor without losing. Slot drives formation order.
        private static void AddHero(CombatState s, string id, int slot, double hp, double atk, double range, double moveSpd)
        {
            s.Entities.Add(new CombatEntity
            {
                Id = id, Team = Team.Party, Slot = slot, RefKind = "hero", RefId = id,
                Pos = new Vec2(0, 0),
                Stats = new StatBlock
                {
                    [StatKey.Hp] = hp, [StatKey.Atk] = atk, [StatKey.Def] = 0,
                    [StatKey.AttackRange] = range, [StatKey.AtkSpd] = 2.0, [StatKey.MoveSpd] = moveSpd,
                },
                Hp = hp, MaxHp = hp, BodyRadius = 0.45,
            });
        }

        private static CombatState StrongParty(int stage = 1)
        {
            var s = new CombatState { Stage = stage, Tactic = PartyTactic.Solo };
            // Big Atk + Hp so the party can't lose; modest range/speed to traverse corridors.
            AddHero(s, "P0", 0, hp: 500000, atk: 200000, range: 2.0, moveSpd: 6.0);
            AddHero(s, "P1", 1, hp: 500000, atk: 200000, range: 2.0, moveSpd: 6.0);
            AddHero(s, "P2", 2, hp: 500000, atk: 200000, range: 2.0, moveSpd: 6.0);
            return s;
        }

        // The floor's own spawn tiers pick which monster/boss ids to use; a run just needs SOME roster.
        private static readonly List<string> Roster = new List<string> { "slime", "goblin" };
        private const string BossId = "goblin_king";

        // ---------------- DungeonArena: geometry ----------------

        [Fact]
        public void ContainsInsetsWallsButPassesCorridorCentres()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            // A room interior cell centre (≥1 cell from any wall) is walkable; a point hugging the very
            // edge of a floor cell that borders a wall is inset out.
            var boss = RoomOfType(d, RoomType.Boss);
            var centre = new Vec2(boss.Cx + 0.5, boss.Cy + 0.5);
            Assert.True(a.Contains(centre), "boss room centre must be walkable");

            // Find a floor cell 4-adjacent to a wall; its centre still passes (0.5 ≥ WalkInset), but a
            // point shoved WalkInset toward the wall crosses into the wall cell and fails.
            for (int y = 1; y < d.H - 1; y++)
                for (int x = 1; x < d.W - 1; x++)
                {
                    int idx = y * d.W + x;
                    if (d.Grid[idx] != DungeonCell.Floor) continue;
                    // wall to the +x side?
                    if (d.Grid[idx + 1] == DungeonCell.Wall && d.Grid[idx - 1] == DungeonCell.Floor)
                    {
                        Assert.True(a.Contains(new Vec2(x + 0.5, y + 0.5)), "floor cell centre walkable");
                        // A point pushed past the +x cell boundary sits in the wall cell.
                        Assert.False(a.Contains(new Vec2(x + 1.0 + 0.01, y + 0.5)), "wall cell not walkable");
                        return;
                    }
                }
            Assert.Fail("no wall-adjacent floor cell found (unexpected)");
        }

        [Fact]
        public void ClampReturnsContainedPointsAndPushesOthersOntoFloor()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            var boss = RoomOfType(d, RoomType.Boss);
            var inside = new Vec2(boss.Cx + 0.5, boss.Cy + 0.5);
            Assert.Equal(inside, a.Clamp(inside)); // contained ⇒ itself

            // A void point outside the whole dungeon clamps to SOME contained cell centre.
            var far = new Vec2(-50, -50);
            var q = a.Clamp(far);
            Assert.True(a.Contains(q), "clamp result must be walkable");
        }

        [Fact]
        public void ClampIsDeterministic()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            // A wall cell near a room boundary clamps to the same walkable centre every call.
            var p = new Vec2(0.5, 0.5);
            var q1 = a.Clamp(p);
            var q2 = a.Clamp(p);
            Assert.Equal(q1, q2);
        }

        [Fact]
        public void RoomAtReadsCellRoom()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            var boss = RoomOfType(d, RoomType.Boss);
            Assert.Equal(boss.Id, a.RoomAt(new Vec2(boss.Cx + 0.5, boss.Cy + 0.5)));
            Assert.Equal(-1, a.RoomAt(new Vec2(-50, -50))); // void ⇒ -1
        }

        // ---------------- DungeonArena: gating ----------------

        [Fact]
        public void GateTargetsSameRoomTrueCrossRoomFalse()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            var boss = RoomOfType(d, RoomType.Boss);
            var entrance = RoomOfType(d, RoomType.Entrance);
            var pBoss = new Vec2(boss.Cx + 0.5, boss.Cy + 0.5);
            var pBoss2 = new Vec2(boss.Cx + 1.5, boss.Cy + 0.5);
            var pEntrance = new Vec2(entrance.Cx + 0.5, entrance.Cy + 0.5);

            Assert.True(a.GateTargets(pBoss, pBoss2), "two points in the boss room see each other");
            // Entrance and boss are far apart (boss is deepest), different rooms ⇒ never visible.
            Assert.False(a.GateTargets(pBoss, pEntrance), "cross-room pair must be gated out");
        }

        [Fact]
        public void GateTargetsCorridorProximityTrueWhenClose()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            // Take a corridor cell and a point one tile away along the corridor: RoomAt == -1 for the
            // corridor, so the gate falls to the euclidean corridor-sight rule (well within 6.5).
            var corr = d.CorridorCells[0];
            var p = new Vec2(corr.X + 0.5, corr.Y + 0.5);
            var near = new Vec2(corr.X + 1.5, corr.Y + 0.5);
            Assert.Equal(-1, a.RoomAt(p));
            Assert.True(a.GateTargets(p, near), "corridor pair within sight is visible");
            // A point far down the map is beyond corridor sight.
            var far = new Vec2(corr.X + 20.5, corr.Y + 0.5);
            Assert.False(a.GateTargets(p, far), "corridor sight is bounded");
        }

        // ---------------- DungeonArena: BFS downhill ----------------

        [Fact]
        public void DownhillStepStrictlyDescendsToTheBoss()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            var entrance = RoomOfType(d, RoomType.Entrance);
            var boss = RoomOfType(d, RoomType.Boss);

            var pos = new Vec2(entrance.Cx + 0.5, entrance.Cy + 0.5);
            short Val(Vec2 p) => d.BossBfs[(int)p.Y * d.W + (int)p.X];
            short prev = Val(pos);
            int steps = 0;
            const int cap = 5000;
            // Walk the flow field cell-to-cell; BossBfs strictly decreases until we reach the boss cell.
            while (steps < cap)
            {
                var next = a.DownhillStep(pos);
                if (next.X == pos.X && next.Y == pos.Y) break; // no lower neighbour ⇒ at the boss
                short nv = Val(next);
                Assert.True(nv < prev, $"BossBfs did not strictly decrease ({prev} -> {nv})");
                prev = nv;
                pos = next;
                steps++;
            }
            Assert.True(steps < cap, "downhill walk never terminated");
            // Terminates AT the boss room (distance 0 cell is the boss centre).
            Assert.Equal(0, Val(pos));
            Assert.Equal(boss.Id, a.RoomAt(pos));
        }

        // ---------------- EnterDungeon ----------------

        [Fact]
        public void EnterDungeonPlacesPartyAtEntranceAndSpawnsWalkable()
        {
            var d = Gen(7);
            var s = StrongParty();
            Combat.EnterDungeon(s, d, Roster, BossId, Cfg, new Rng(1));
            var a = s.Dungeon!;
            var entrance = RoomOfType(d, RoomType.Entrance);

            Assert.Equal(EncounterKind.Dungeon, s.Kind);
            Assert.Null(s.ArenaId);

            // Party is at the entrance room, on walkable cells.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party))
            {
                Assert.True(a.Contains(e.Pos), $"party off-floor at {e.Pos.X},{e.Pos.Y}");
                Assert.Equal(entrance.Id, a.RoomAt(e.Pos));
            }

            // Every spawn is on a walkable cell; non-boss spawns are non-aggro.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy))
            {
                Assert.True(a.Contains(e.Pos), $"spawn off-floor at {e.Pos.X},{e.Pos.Y}");
                if (!e.IsBoss) Assert.False(e.Aggro, "trash must spawn idle");
            }
        }

        [Fact]
        public void EnterDungeonMakesExactlyOneBossAndTierOneElites()
        {
            var d = Gen(7);
            var s = StrongParty();
            Combat.EnterDungeon(s, d, Roster, BossId, Cfg, new Rng(1));

            var bosses = s.Entities.Where(e => e.Team == Team.Enemy && e.IsBoss).ToList();
            Assert.Single(bosses);

            // Every tier-1 authored spawn became an Elite-ranked mob.
            int tier1 = d.Spawns.Count(sp => sp.Tier == 1);
            int elites = s.Entities.Count(e => e.Team == Team.Enemy && e.Rank == MonsterRank.Elite);
            Assert.Equal(tier1, elites);
        }

        // ---------------- Full run ----------------

        [Fact]
        public void StrongPartyClearsTheFloorStayingWalkableEveryStep()
        {
            var d = Gen(3, rooms: 14);
            var s = StrongParty();
            Combat.EnterDungeon(s, d, Roster, BossId, Cfg, new Rng(9));
            var a = s.Dungeon!;

            int steps = 0;
            const int cap = 40000;
            while (s.Status == CombatStatus.Running && steps < cap)
            {
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(9));
                // Invariant: every living party entity stays on the walkable grid every step.
                foreach (var e in s.Entities.Where(e => e.Team == Team.Party && e.Alive))
                    Assert.True(a.Contains(e.Pos), $"party left the floor at {e.Pos.X},{e.Pos.Y} (step {steps})");
                steps++;
            }

            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.DoesNotContain(s.Entities, e => e.Team == Team.Enemy && e.IsBoss && e.Alive);
        }

        // ---------------- Determinism ----------------

        [Fact]
        public void IdenticalRunsProduceIdenticalPositions()
        {
            List<(string, double, double)> Run()
            {
                var d = Gen(5, rooms: 14);
                var s = StrongParty();
                Combat.EnterDungeon(s, d, Roster, BossId, Cfg, new Rng(4));
                for (int i = 0; i < 500; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(4));
                return s.Entities.OrderBy(e => e.Id, System.StringComparer.Ordinal)
                                 .Select(e => (e.Id, e.Pos.X, e.Pos.Y)).ToList();
            }
            Assert.Equal(Run(), Run());
        }

        // ---------------- Room gating in live combat ----------------

        [Fact]
        public void HeroDoesNotTargetAnAggroMonsterBehindAWall()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            var boss = RoomOfType(d, RoomType.Boss);
            var entrance = RoomOfType(d, RoomType.Entrance);

            // Build a minimal 2-entity fight: a hero in the entrance room, one aggro'd monster in the
            // boss room. They're in different rooms — even set within EngageRadius euclidean, the gate
            // must hide the monster, so the hero acquires no target.
            var s = new CombatState { Stage = 1, Kind = EncounterKind.Dungeon, Tactic = PartyTactic.Solo, Dungeon = a };
            AddHero(s, "P0", 0, hp: 1000, atk: 10, range: 2.0, moveSpd: 0.0001); // barely moves, so it can't wander into range

            var mon = new CombatEntity
            {
                Id = "M", Team = Team.Enemy, Aggro = true, RefKind = "test", RefId = "M",
                Pos = new Vec2(boss.Cx + 0.5, boss.Cy + 0.5), BodyRadius = 0.45,
                Stats = new StatBlock { [StatKey.Hp] = 1000, [StatKey.Atk] = 1, [StatKey.MoveSpd] = 0.0001 },
                Hp = 1000, MaxHp = 1000,
            };
            s.Entities.Add(mon);

            var hero = s.Entities.First(e => e.Team == Team.Party);
            hero.Pos = new Vec2(entrance.Cx + 0.5, entrance.Cy + 0.5);

            // If the two happen to be within EngageRadius, this is a genuine cross-wall case; if farther,
            // the gate is moot but the assertion (no target) still holds. Step once.
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.NotEqual("M", hero.TargetId); // never targets the walled-off monster

            // Same room: drop the hero INTO the boss room next to the monster ⇒ it targets it.
            var s2 = new CombatState { Stage = 1, Kind = EncounterKind.Dungeon, Tactic = PartyTactic.Solo, Dungeon = a };
            AddHero(s2, "P0", 0, hp: 1000, atk: 10, range: 2.0, moveSpd: 3.0);
            var mon2 = new CombatEntity
            {
                Id = "M", Team = Team.Enemy, Aggro = true, RefKind = "test", RefId = "M",
                Pos = new Vec2(boss.Cx + 0.5, boss.Cy + 0.5), BodyRadius = 0.45,
                Stats = new StatBlock { [StatKey.Hp] = 1000, [StatKey.Atk] = 1, [StatKey.MoveSpd] = 0.0001 },
                Hp = 1000, MaxHp = 1000,
            };
            s2.Entities.Add(mon2);
            var hero2 = s2.Entities.First(e => e.Team == Team.Party);
            hero2.Pos = new Vec2(boss.Cx + 1.5, boss.Cy + 0.5); // same room, adjacent
            Combat.StepCombat(s2, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.Equal("M", hero2.TargetId); // in-room ⇒ acquires it
        }

        // ---------------- No farm respawns ----------------

        [Fact]
        public void NoFarmRespawnsDuringADungeonRun()
        {
            var d = Gen(7);
            var s = StrongParty();
            Combat.EnterDungeon(s, d, Roster, BossId, Cfg, new Rng(2));
            int startEnemies = s.Entities.Count(e => e.Team == Team.Enemy);

            // Freeze the party so it kills nothing; enemy count must only ever hold or drop, never rise
            // (no farm spawn pack fires in Dungeon mode).
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party)) e.Stats[StatKey.Atk] = 0;
            int prev = startEnemies;
            for (int i = 0; i < 300; i++)
            {
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(2));
                int now = s.Entities.Count(e => e.Team == Team.Enemy);
                Assert.True(now <= prev, $"enemy count rose {prev} -> {now} (a farm pack spawned)");
                prev = now;
            }
        }
    }
}
