using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Dungeon sim integration (ROADMAP roguelite, slice 3a; reworked after live feedback): Combat
    // understands a generated dungeon — grid walkability (DungeonArena.Contains/Clamp), room-gated
    // targeting (the anti-wallhack rule), per-room BFS-downhill leader travel (the room SWEEP), and the
    // FRESH-STATE InitDungeon init (fully separate from the live farm). These pin the DungeonArena
    // queries, the init contract + state independence, a FULL CLEAR on a real generated floor (every
    // enemy dead, walkable invariant sampled every step, sweep order nondecreasing in entrance depth),
    // the win/lose shape, determinism, and the room-gate in live combat.
    public class DungeonCombatTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Dungeon Gen(int seed, int rooms = 14) =>
            DungeonGen.Generate(new DungeonParams { Seed = seed, RoomCount = rooms });

        private static DungeonRoom RoomOfType(Dungeon d, RoomType t) => d.Rooms.First(r => r.Type == t);

        // A synthetic super-statted hero entity — strong enough to steamroll a floor without losing.
        // Slot drives formation order. Built directly (not through ComputeHeroStats) so the party's
        // power is guaranteed regardless of config balance.
        private static CombatEntity Hero(string id, int slot, double hp, double atk, double range, double moveSpd)
            => new CombatEntity
            {
                Id = id, Team = Team.Party, Slot = slot, RefKind = "hero", RefId = id,
                Pos = new Vec2(0, 0),
                Stats = new StatBlock
                {
                    [StatKey.Hp] = hp, [StatKey.Atk] = atk, [StatKey.Def] = 0,
                    [StatKey.AttackRange] = range, [StatKey.AtkSpd] = 2.0, [StatKey.MoveSpd] = moveSpd,
                },
                Hp = hp, MaxHp = hp, BodyRadius = 0.45,
            };

        private static void AddHero(CombatState s, string id, int slot, double hp, double atk, double range, double moveSpd)
            => s.Entities.Add(Hero(id, slot, hp, atk, range, moveSpd));

        // The floor's own spawn tiers pick which monster/boss ids to use; a run just needs SOME roster.
        private static readonly List<string> Roster = new List<string> { "slime", "goblin" };
        private const string BossId = "goblin_king";

        // Build a fresh dungeon state via the real InitDungeon path (empty HeroInstance party → only the
        // authored spawns are seeded; the party-placement loop is a no-op), then inject synthetic
        // super-heroes at the entrance room centre (Clamp'd onto the grid) so the party can't lose. This
        // exercises the real spawn-seeding while keeping a guaranteed-steamroll party for the sweep test.
        private static CombatState StrongDungeon(Dungeon d, int stage = 1, uint rngSeed = 1)
        {
            var s = Combat.InitDungeon(new List<HeroInstance>(), stage, d, Roster, BossId, Cfg, new Rng(rngSeed));
            s.Tactic = PartyTactic.Solo;
            var a = s.Dungeon!;
            var entrance = a.EntranceCentre();
            // Big Atk + Hp so the party can't lose; modest range/speed to traverse corridors.
            for (int i = 0; i < 3; i++)
            {
                var h = Hero("P" + i, i, hp: 500000, atk: 200000, range: 2.0, moveSpd: 6.0);
                // Cluster at the entrance, mirroring InitDungeon's placement offsets.
                double ox = ((i % 2) - 0.5) * 1.4, oy = ((i / 2) - 0.5) * 1.4;
                h.Pos = a.Clamp(new Vec2(entrance.X + ox, entrance.Y + oy));
                s.Entities.Add(h);
            }
            return s;
        }

        // ---------------- DungeonArena: geometry ----------------

        [Fact]
        public void ContainsInsetsWallsButPassesCorridorCentres()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            var boss = RoomOfType(d, RoomType.Boss);
            var centre = new Vec2(boss.Cx + 0.5, boss.Cy + 0.5);
            Assert.True(a.Contains(centre), "boss room centre must be walkable");

            for (int y = 1; y < d.H - 1; y++)
                for (int x = 1; x < d.W - 1; x++)
                {
                    int idx = y * d.W + x;
                    if (d.Grid[idx] != DungeonCell.Floor) continue;
                    if (d.Grid[idx + 1] == DungeonCell.Wall && d.Grid[idx - 1] == DungeonCell.Floor)
                    {
                        Assert.True(a.Contains(new Vec2(x + 0.5, y + 0.5)), "floor cell centre walkable");
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

            var far = new Vec2(-50, -50);
            var q = a.Clamp(far);
            Assert.True(a.Contains(q), "clamp result must be walkable");
        }

        [Fact]
        public void ClampIsDeterministic()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
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
            Assert.False(a.GateTargets(pBoss, pEntrance), "cross-room pair must be gated out");
        }

        [Fact]
        public void GateTargetsCorridorProximityTrueWhenClose()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            var corr = d.CorridorCells[0];
            var p = new Vec2(corr.X + 0.5, corr.Y + 0.5);
            var near = new Vec2(corr.X + 1.5, corr.Y + 0.5);
            Assert.Equal(-1, a.RoomAt(p));
            Assert.True(a.GateTargets(p, near), "corridor pair within sight is visible");
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
            Assert.Equal(0, Val(pos));
            Assert.Equal(boss.Id, a.RoomAt(pos));
        }

        [Fact]
        public void FieldForStrictlyDescendsToTheTargetRoom()
        {
            var d = Gen(7);
            var a = new DungeonArena(d);
            var entrance = RoomOfType(d, RoomType.Entrance);
            // Route from the boss room toward the ENTRANCE room's field: the per-room flow field must
            // strictly decrease cell-to-cell and terminate at the entrance centre (distance 0).
            var boss = RoomOfType(d, RoomType.Boss);
            var field = a.FieldFor(entrance.Id);
            short Val(Vec2 p) => field[(int)p.Y * d.W + (int)p.X];

            var pos = new Vec2(boss.Cx + 0.5, boss.Cy + 0.5);
            short prev = Val(pos);
            Assert.True(prev >= 0, "boss room must be reachable from the entrance field");
            int steps = 0;
            const int cap = 5000;
            while (steps < cap)
            {
                var next = a.DownhillStep(pos, entrance.Id);
                if (next.X == pos.X && next.Y == pos.Y) break;
                short nv = Val(next);
                Assert.True(nv < prev, $"entrance field did not strictly decrease ({prev} -> {nv})");
                prev = nv;
                pos = next;
                steps++;
            }
            Assert.True(steps < cap, "downhill walk never terminated");
            Assert.Equal(0, Val(pos));
            Assert.Equal(entrance.Id, a.RoomAt(pos));
        }

        // ---------------- InitDungeon (fresh state) ----------------

        [Fact]
        public void InitDungeonPlacesPartyAtEntranceAndSpawnsWalkable()
        {
            var d = Gen(7);
            var s = StrongDungeon(d);
            var a = s.Dungeon!;
            var entrance = RoomOfType(d, RoomType.Entrance);

            Assert.Equal(EncounterKind.Dungeon, s.Kind);
            Assert.Null(s.ArenaId);
            Assert.Equal(CombatStatus.Running, s.Status);

            // Party is at the entrance room, on walkable cells.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party))
            {
                Assert.True(a.Contains(e.Pos), $"party off-floor at {e.Pos.X},{e.Pos.Y}");
                Assert.Equal(entrance.Id, a.RoomAt(e.Pos));
            }

            // Every spawn is on a walkable cell; non-boss spawns are non-aggro; each carries its room id.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy))
            {
                Assert.True(a.Contains(e.Pos), $"spawn off-floor at {e.Pos.X},{e.Pos.Y}");
                Assert.True(e.DungeonRoomId >= 0, "enemy must carry its spawn room id");
                if (!e.IsBoss) Assert.False(e.Aggro, "trash must spawn idle");
            }
        }

        [Fact]
        public void InitDungeonMakesExactlyOneBossAndTierOneElites()
        {
            var d = Gen(7);
            var s = StrongDungeon(d);

            var bosses = s.Entities.Where(e => e.Team == Team.Enemy && e.IsBoss).ToList();
            Assert.Single(bosses);

            int tier1 = d.Spawns.Count(sp => sp.Tier == 1);
            int elites = s.Entities.Count(e => e.Team == Team.Enemy && e.Rank == MonsterRank.Elite);
            Assert.Equal(tier1, elites);
        }

        [Fact]
        public void TwoInitDungeonStatesAreIndependent()
        {
            var d = Gen(7);
            var s1 = Combat.InitDungeon(new List<HeroInstance>(), 1, d, Roster, BossId, Cfg, new Rng(1));
            var s2 = Combat.InitDungeon(new List<HeroInstance>(), 1, d, Roster, BossId, Cfg, new Rng(1));

            // Distinct object graphs: different states, entity lists, and arenas.
            Assert.NotSame(s1, s2);
            Assert.NotSame(s1.Entities, s2.Entities);
            Assert.NotSame(s1.Dungeon, s2.Dungeon);

            // Mutating one must not touch the other.
            int before = s2.Entities.Count;
            s1.Entities.Clear();
            s1.Stage = 999;
            s1.Status = CombatStatus.Lost;
            Assert.Equal(before, s2.Entities.Count);
            Assert.Equal(1, s2.Stage);
            Assert.Equal(CombatStatus.Running, s2.Status);
        }

        // ---------------- Full clear + sweep ----------------

        [Fact]
        public void StrongPartyFullClearsTheFloorStayingWalkableEveryStep()
        {
            var d = Gen(3, rooms: 14);
            var s = StrongDungeon(d, rngSeed: 9);
            var a = s.Dungeon!;

            int steps = 0;
            const int cap = 60000;
            while (s.Status == CombatStatus.Running && steps < cap)
            {
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(9));
                foreach (var e in s.Entities.Where(e => e.Team == Team.Party && e.Alive))
                    Assert.True(a.Contains(e.Pos), $"party left the floor at {e.Pos.X},{e.Pos.Y} (step {steps})");
                steps++;
            }

            // FULL CLEAR: won only when EVERY enemy is dead — the boss AND all trash, including a leaf-room
            // mob off the critical path (which a boss-kill-ends-the-run would have left alive).
            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.DoesNotContain(s.Entities, e => e.Team == Team.Enemy && e.Alive);
        }

        [Fact]
        public void SweepVisitsRoomsInNondecreasingEntranceDepthOrder()
        {
            var d = Gen(3, rooms: 14);
            var s = StrongDungeon(d, rngSeed: 9);
            var a = s.Dungeon!;

            // Track the objective room's entrance-depth each step; the sweep clears shallow rooms before
            // deep ones, so the depth of the CURRENT objective must be nondecreasing over the run (a room
            // only becomes the objective once every shallower room is emptied). We read the objective the
            // same way Combat does — the shallowest living-enemy room — via a local recompute.
            short lastDepth = -1;
            int distinctRoomsSwept = 0;
            int? lastRoom = null;
            int steps = 0;
            const int cap = 60000;
            while (s.Status == CombatStatus.Running && steps < cap)
            {
                int? obj = ObjectiveRoom(s);
                if (obj is int room)
                {
                    short depth = a.RoomEntranceDepth(room);
                    Assert.True(depth >= lastDepth,
                        $"objective depth went backwards ({lastDepth} -> {depth}) at step {steps}");
                    lastDepth = depth;
                    if (lastRoom != room) { distinctRoomsSwept++; lastRoom = room; }
                }
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(9));
                steps++;
            }

            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.True(distinctRoomsSwept >= 2, "the sweep should traverse multiple rooms in order");
        }

        [Fact]
        public void LinearRunSweepsForwardOnlyAndFullClears()
        {
            // The user-facing contract of Linear layouts: an auto-battled run NEVER backtracks. On a
            // chain, room depth == chain index, so the objective (shallowest living-enemy room) must
            // march monotonically forward through the whole crawl until everything is dead.
            var d = DungeonGen.Generate(new DungeonParams { Seed = 5, RoomCount = 14, Linear = true });
            var s = StrongDungeon(d, rngSeed: 11);
            var a = s.Dungeon!;

            short lastDepth = -1;
            var sweptDepths = new List<short>();
            int steps = 0;
            const int cap = 80000;
            while (s.Status == CombatStatus.Running && steps < cap)
            {
                int? obj = ObjectiveRoom(s);
                if (obj is int room)
                {
                    short depth = a.RoomEntranceDepth(room);
                    Assert.True(depth >= lastDepth,
                        $"linear sweep went BACKWARD ({lastDepth} -> {depth}) at step {steps}");
                    if (sweptDepths.Count == 0 || sweptDepths[sweptDepths.Count - 1] != depth) sweptDepths.Add(depth);
                    lastDepth = depth;
                }
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(11));
                foreach (var e in s.Entities.Where(e => e.Team == Team.Party && e.Alive))
                    Assert.True(a.Contains(e.Pos), $"party left the floor at step {steps}");
                steps++;
            }

            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.DoesNotContain(s.Entities, e => e.Team == Team.Enemy && e.Alive);
            // The crawl really traversed the chain (several distinct objective rooms, strictly ascending).
            Assert.True(sweptDepths.Count >= 5, $"expected a multi-room crawl, saw {sweptDepths.Count} objectives");
            for (int i = 1; i < sweptDepths.Count; i++)
                Assert.True(sweptDepths[i] > sweptDepths[i - 1], "objective repeated or regressed");
        }

        // Mirror of Combat.DungeonObjectiveRoom (private): the shallowest living-enemy room, ties → lower
        // id, attributing each mob to its spawn room. Used only to observe sweep order from the test side.
        private static int? ObjectiveRoom(CombatState s)
        {
            var a = s.Dungeon!;
            int? best = null; short bestDepth = short.MaxValue;
            foreach (var e in s.Entities)
            {
                if (e.Team != Team.Enemy || !e.Alive || e.DungeonRoomId < 0) continue;
                short depth = a.RoomEntranceDepth(e.DungeonRoomId);
                if (best == null || depth < bestDepth || (depth == bestDepth && e.DungeonRoomId < best.Value))
                { bestDepth = depth; best = e.DungeonRoomId; }
            }
            return best;
        }

        // ---------------- Win/lose shape ----------------

        [Fact]
        public void BossDeadButTrashAliveStaysRunningAllDeadWins()
        {
            var d = Gen(7);
            var s = StrongDungeon(d);

            // Kill ONLY the boss, leave trash alive, step once: the run must still be Running (full clear,
            // not a boss-kill win).
            var boss = s.Entities.First(e => e.Team == Team.Enemy && e.IsBoss);
            boss.Hp = 0;
            // Freeze the party so it kills nothing this step (isolate the win check).
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party)) e.Stats[StatKey.Atk] = 0;
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.Equal(CombatStatus.Running, s.Status);
            Assert.Contains(s.Entities, e => e.Team == Team.Enemy && e.Alive);

            // Now kill every remaining enemy and step: all-dead ⇒ Won.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy)) e.Hp = 0;
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.Equal(CombatStatus.Won, s.Status);
        }

        // ---------------- Determinism ----------------

        [Fact]
        public void IdenticalRunsProduceIdenticalPositions()
        {
            List<(string, double, double)> Run()
            {
                var d = Gen(5, rooms: 14);
                var s = StrongDungeon(d, rngSeed: 4);
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

            var s = new CombatState { Stage = 1, Kind = EncounterKind.Dungeon, Tactic = PartyTactic.Solo, Dungeon = a };
            AddHero(s, "P0", 0, hp: 1000, atk: 10, range: 2.0, moveSpd: 0.0001); // barely moves

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

            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            Assert.NotEqual("M", hero.TargetId); // never targets the walled-off monster

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
            var s = StrongDungeon(d, rngSeed: 2);
            int startEnemies = s.Entities.Count(e => e.Team == Team.Enemy);
            Assert.True(startEnemies > 0);

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
