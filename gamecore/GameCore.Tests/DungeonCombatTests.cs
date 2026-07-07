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
            // Corridor sight now ALSO requires a walkable line (anti-wallhack for parallel maze
            // halls), so the visible pair must be two genuinely adjacent corridor cells — a real
            // "down the hall" sightline, not just any point within range.
            var d = Gen(7);
            var a = new DungeonArena(d);
            Vec2? pOpt = null, nearOpt = null;
            foreach (var c1 in d.CorridorCells)
            {
                foreach (var c2 in d.CorridorCells)
                {
                    if (Math.Abs(c1.X - c2.X) + Math.Abs(c1.Y - c2.Y) != 1) continue; // 4-adjacent pair
                    var w1 = new Vec2(c1.X + 0.5, c1.Y + 0.5);
                    var w2 = new Vec2(c2.X + 0.5, c2.Y + 0.5);
                    if (a.Contains(w1) && a.Contains(w2)) { pOpt = w1; nearOpt = w2; break; }
                }
                if (pOpt != null) break;
            }
            Assert.True(pOpt.HasValue, "any generated dungeon has adjacent corridor cells");
            var p = pOpt.Value; var near = nearOpt!.Value;
            Assert.Equal(-1, a.RoomAt(p));
            Assert.True(a.GateTargets(p, near), "adjacent corridor pair with a clear line is visible");
            var far = new Vec2(p.X + 20.0, p.Y);
            Assert.False(a.GateTargets(p, far), "corridor sight is bounded");
        }

        [Fact]
        public void GateTargetsRoomToHallwayFalseEvenAcrossAnOpenDoorway()
        {
            // Room-scoped sight (user rule 2026-07-06): a unit in the hallway and a unit in the room
            // NEVER see each other, even standing on either side of the open doorway — sight ends at
            // the room's walls (hall units see down the hall only; room units see their room only).
            var d = Gen(7);
            var a = new DungeonArena(d);
            foreach (var door in d.Doorways)
            {
                var hall = new Vec2(door.X + 0.5, door.Y + 0.5);
                if (a.RoomAt(hall) != -1) continue; // doorways are corridor cells, but be safe
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var room = new Vec2(door.X + dx + 0.5, door.Y + dy + 0.5);
                    if (a.RoomAt(room) < 0) continue;
                    Assert.False(a.GateTargets(hall, room), "hallway unit saw into the room");
                    Assert.False(a.GateTargets(room, hall), "room unit saw into the hallway");
                    return;
                }
            }
            Assert.Fail("no doorway with an adjacent room cell (unexpected)");
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

        [Fact]
        public void FarFollowersRerouteThroughTheMazeToRejoinTheLeader()
        {
            // Straight-line slot homing wedges on maze wall corners once a follower falls a doorway
            // behind (Play-caught: both followers stranded at the entrance while the leader soloed
            // the crypt). The rejoin fix flow-routes a follower whose straight segment to its slot
            // is wall-blocked. Setup: only an unkillable boss alive, so the leader marches the whole
            // chain; followers start stranded at the entrance and must arrive at the far end too.
            var d = DungeonGen.Generate(new DungeonParams { Seed = 5, RoomCount = 14, Linear = true });
            var s = StrongDungeon(d, rngSeed: 11);
            var a = s.Dungeon!;
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy && !e.IsBoss)) e.Hp = 0;
            var boss = s.Entities.First(e => e.Team == Team.Enemy && e.IsBoss);
            boss.MaxHp = 1e12; boss.Hp = 1e12; // outlives the probe: the run must stay Running
            var party = s.Entities.Where(e => e.Team == Team.Party).OrderBy(e => e.Slot).ToList();
            var entrance = a.EntranceCentre();
            party[1].Pos = a.Clamp(new Vec2(entrance.X - 0.7, entrance.Y));
            party[2].Pos = a.Clamp(new Vec2(entrance.X + 0.7, entrance.Y));

            for (int i = 0; i < 20000 && s.Status == CombatStatus.Running; i++)
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(11));

            Assert.Equal(CombatStatus.Running, s.Status);
            foreach (var f in party.Skip(1))
                Assert.True(Vec2.Distance(party[0].Pos, f.Pos) < 12.0,
                    $"follower {f.Id} stranded {Vec2.Distance(party[0].Pos, f.Pos):F1} tiles from the leader");
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

        // ---------------- §7.3 sealed doors, key bearer, boss gate (10.7b) ----------------

        private static Dungeon GenGrammarFloor(int seed) => DungeonGen.Generate(new DungeonParams
        {
            Seed = seed, RoomCount = 12, Linear = true,
            Encounter = new DungeonEncounterSpec
            { CombatWaves = 1, MobsPerWave = 4, EliteCount = 1, EliteEscort = 2, BossAdds = 2 },
        });

        [Fact]
        public void RunSealsRoomsInChainOrderContainsTheFightAndClearsThemAll()
        {
            var d = GenGrammarFloor(3);
            var s = StrongDungeon(d, rngSeed: 7);
            var a = s.Dungeon!;
            Assert.True(s.DungeonKeyRequired);
            Assert.True(s.DungeonBossRoomId >= 0);

            var sealedSeq = new List<int>();
            var clearedSeq = new List<int>();
            int steps = 0;
            const int cap = 80000;
            while (s.Status == CombatStatus.Running && steps < cap)
            {
                var events = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(7));
                foreach (var ev in events)
                {
                    if (ev.Type == CombatEventType.RoomSealed) sealedSeq.Add(ev.RoomId);
                    if (ev.Type == CombatEventType.RoomCleared) clearedSeq.Add(ev.RoomId);
                }
                // Post-step containment invariant: while a room is sealed, the party AND that
                // room's mobs are inside it.
                if (s.DungeonSealedRoomId >= 0)
                {
                    int room = s.DungeonSealedRoomId;
                    foreach (var e in s.Entities)
                    {
                        if (!e.Alive) continue;
                        if (e.Team == Team.Party)
                            Assert.True(a.RoomAt(e.Pos) == room, $"hero {e.Id} outside sealed room at step {steps}");
                        else if (e.DungeonRoomId == room)
                            Assert.True(a.RoomAt(e.Pos) == room, $"mob {e.Id} escaped its sealed room at step {steps}");
                    }
                }
                steps++;
            }

            Assert.Equal(CombatStatus.Won, s.Status);
            // Every mob room got a seal + a matching clear, in strictly ascending chain order
            // (linear room ids ARE the chain order).
            var mobRooms = d.Spawns.Select(sp => sp.RoomId).Distinct().OrderBy(id => id).ToList();
            Assert.Equal(mobRooms, sealedSeq);
            Assert.Equal(sealedSeq, clearedSeq);
            Assert.True(mobRooms.All(s.DungeonClearedRooms.Contains));
            Assert.Equal(-1, s.DungeonSealedRoomId);
        }

        [Fact]
        public void KeyBearerDeathDropsTheBossKeyBeforeTheBossRoomOpens()
        {
            var d = GenGrammarFloor(4);
            var s = StrongDungeon(d, rngSeed: 9);
            var a = s.Dungeon!;
            var bearer = s.Entities.Single(e => e.DungeonKeyBearer);
            Assert.Equal(RoomOfType(d, RoomType.Key).Id, bearer.DungeonRoomId);
            Assert.Equal(MonsterRank.Elite, bearer.Rank); // the "marked" tell rides the rank glow

            bool keyDropSeen = false;
            int steps = 0;
            while (s.Status == CombatStatus.Running && steps < 80000)
            {
                var events = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(9));
                keyDropSeen |= events.Any(ev => ev.Type == CombatEventType.BossKeyDrop);
                // The locked boss door holds until the bearer dies (§7.3).
                if (!s.DungeonBossKeyHeld)
                    foreach (var e in s.Entities.Where(e => e.Team == Team.Party && e.Alive))
                        Assert.NotEqual(s.DungeonBossRoomId, a.RoomAt(e.Pos));
                steps++;
            }

            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.True(keyDropSeen, "the bearer's death never fired BossKeyDrop");
            Assert.True(s.DungeonBossKeyHeld);
        }

        // ---------------- §7.3 wave phases + room-clear beats (10.7c) ----------------

        private static Dungeon GenWaveFloor(int seed) => DungeonGen.Generate(new DungeonParams
        {
            Seed = seed, RoomCount = 12, Linear = true,
            Encounter = new DungeonEncounterSpec
            { CombatWaves = 2, MobsPerWave = 3, EliteCount = 1, EliteEscort = 2, BossAdds = 2 },
        });

        [Fact]
        public void LaterWavesWaitPendingAndTheBearerArrivesInTheFinalWave()
        {
            var d = GenWaveFloor(6);
            var s = StrongDungeon(d, rngSeed: 5);

            // Init materialises ONLY wave 0; wave 1 waits fully built.
            Assert.All(s.Entities.Where(e => e.Team == Team.Enemy), e => Assert.Equal(0, e.DungeonWave));
            Assert.NotEmpty(s.DungeonPendingWaves);
            Assert.All(s.DungeonPendingWaves, p => Assert.Equal(1, p.DungeonWave));
            // The key room's bearer leads the FINAL wave — so it is pending, not live.
            Assert.DoesNotContain(s.Entities, e => e.DungeonKeyBearer);
            Assert.Single(s.DungeonPendingWaves, p => p.DungeonKeyBearer);

            // Full run: every wave releases exactly once per multi-wave room, always while sealed,
            // before that room's clear; the bearer still gates the boss; the run still wins.
            var waveRooms = new List<int>();
            int steps = 0;
            while (s.Status == CombatStatus.Running && steps < 120000)
            {
                var events = Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(5));
                foreach (var ev in events)
                    if (ev.Type == CombatEventType.RoomWave)
                    {
                        Assert.Equal(ev.RoomId, s.DungeonSealedRoomId); // waves only rise under seal
                        Assert.Equal(1, (int)ev.Amount);
                        waveRooms.Add(ev.RoomId);
                    }
                steps++;
            }
            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.Empty(s.DungeonPendingWaves);
            Assert.True(s.DungeonBossKeyHeld);
            // One second wave per combat room + the key room (elite/boss author a single wave).
            var multiWaveRooms = d.Spawns.Where(sp => sp.Wave > 0).Select(sp => sp.RoomId)
                .Distinct().OrderBy(id => id).ToList();
            Assert.Equal(multiWaveRooms, waveRooms.OrderBy(id => id).ToList());
            Assert.Equal(waveRooms.Count, waveRooms.Distinct().Count());
        }

        [Fact]
        public void RoomClearPaysTheStageScaledGoldBurst()
        {
            var d = GenGrammarFloor(8);
            var s = StrongDungeon(d, rngSeed: 6);

            // The burst is precomputed at init: avg roster gold × KillRewardMult × MobEquiv, floored.
            double avg = (Cfg.Monsters[Roster[0]].GoldReward + Cfg.Monsters[Roster[1]].GoldReward) / 2.0;
            long expected = (long)System.Math.Floor(
                avg * Cfg.Balance.KillRewardMult(1) * Cfg.Balance.DungeonRoomClearMobEquiv);
            Assert.True(expected > 0);
            Assert.Equal(expected, s.DungeonRoomClearGold);

            // Every RoomCleared event carries the burst, and the gold really lands in PendingGold.
            long goldBefore = s.PendingGold;
            int clears = 0, steps = 0;
            while (s.Status == CombatStatus.Running && steps < 120000)
            {
                foreach (var ev in Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(6)))
                    if (ev.Type == CombatEventType.RoomCleared)
                    { Assert.Equal(expected, (long)ev.Amount); clears++; }
                steps++;
            }
            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.True(clears >= 5, $"expected a room-by-room crawl, saw {clears} clears");
            Assert.True(s.PendingGold >= goldBefore + clears * expected,
                "the clear bursts never landed in PendingGold");
        }

        [Fact]
        public void PendingWavesBlockTheWinUntilReleasedAndKilled()
        {
            var d = GenWaveFloor(9);
            var s = StrongDungeon(d, rngSeed: 8);
            Assert.NotEmpty(s.DungeonPendingWaves);

            // Massacre every LIVE enemy directly: with waves still waiting the run must NOT be Won.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy)) e.Hp = 0;
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(8));
            Assert.Equal(CombatStatus.Running, s.Status);

            // The sweep then revisits the pending rooms, releases the waves under seal, and wins.
            int steps = 0;
            while (s.Status == CombatStatus.Running && steps < 120000)
            { Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(8)); steps++; }
            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.Empty(s.DungeonPendingWaves);
        }

        // ---------------- §7.3 chests, mimics, reward room (10.7d) ----------------

        private static Dungeon GenChestFloor(int seed, double mimicChance, bool rewardRoom = false)
            => DungeonGen.Generate(new DungeonParams
            {
                Seed = seed, RoomCount = 12, Linear = true, RewardRoom = rewardRoom,
                Encounter = new DungeonEncounterSpec
                {
                    CombatWaves = 1, MobsPerWave = 3, EliteCount = 1, EliteEscort = 2, BossAdds = 2,
                    ChestCount = 2, ChestWeightWooden = 0, ChestWeightIron = 100, ChestWeightGolden = 0,
                    MimicChance = mimicChance,
                },
            });

        [Fact]
        public void ChestsPopOnApproachPayDustAndGateTheWin()
        {
            var d = GenChestFloor(3, mimicChance: 0, rewardRoom: true);
            var s = StrongDungeon(d, rngSeed: 4);
            Assert.True(d.Chests.Count >= 5); // 2 iron (chest room) + 3 vault
            long expectedGold = s.DungeonRoomClearGold; // iron mult 2× applies per chest

            // Win guard: massacre everything — with lids still shut the run must stay Running.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy)) e.Hp = 0;
            s.DungeonBossKeyHeld = true; // the direct massacre bypassed HandleDeath's key drop
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(4));
            Assert.Equal(CombatStatus.Running, s.Status);

            // The sweep now walks lid to lid: every chest pops, dust lands, and the run wins.
            var opened = new List<int>();
            int steps = 0;
            while (s.Status == CombatStatus.Running && steps < 120000)
            {
                foreach (var ev in Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(4)))
                    if (ev.Type == CombatEventType.ChestOpen)
                    {
                        opened.Add(ev.ChestIndex);
                        Assert.True((long)ev.Amount > 0, "chest paid no gold");
                    }
                steps++;
            }
            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.Equal(d.Chests.Count, opened.Count);
            Assert.Equal(d.Chests.Count, opened.Distinct().Count());
            Assert.True(s.PendingDust > 0, "iron/golden chests must drop grave dust");
            Assert.True(expectedGold > 0);
        }

        [Fact]
        public void MimicChestsAmbushSealTheRoomAndPayOnDeath()
        {
            var d = GenChestFloor(7, mimicChance: 1.0);
            var s = StrongDungeon(d, rngSeed: 9);
            var chestRoom = RoomOfType(d, RoomType.Treasure);
            int mimicChests = d.Chests.Count(c => c.Mimic);
            Assert.Equal(d.Chests.Count, mimicChests); // forced odds: every chest bites
            Assert.Equal(mimicChests, s.DungeonMimics.Count); // pre-built ambushers wait dormant

            var reveals = new List<int>();
            var opens = new List<int>();
            int steps = 0;
            while (s.Status == CombatStatus.Running && steps < 120000)
            {
                foreach (var ev in Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(9)))
                {
                    if (ev.Type == CombatEventType.MimicReveal)
                    {
                        reveals.Add(ev.ChestIndex);
                        Assert.Equal(chestRoom.Id, ev.RoomId);
                    }
                    if (ev.Type == CombatEventType.ChestOpen) opens.Add(ev.ChestIndex);
                }
                steps++;
            }
            Assert.Equal(CombatStatus.Won, s.Status);
            Assert.Equal(mimicChests, reveals.Count);
            // Every revealed mimic died and paid its chest through HandleDeath.
            Assert.Equal(reveals.OrderBy(i => i).ToList(), opens.OrderBy(i => i).ToList());
            Assert.Empty(s.DungeonMimics);
            Assert.DoesNotContain(s.Entities, e => e.DungeonChestIndex >= 0 && e.Alive);
        }

        [Fact]
        public void BossDoorClampsAnUnkeyedPartyOutAndAdmitsAKeyedOne()
        {
            var d = GenGrammarFloor(5);
            var s = StrongDungeon(d, rngSeed: 3);
            var a = s.Dungeon!;
            var boss = RoomOfType(d, RoomType.Boss);
            var inside = new Vec2(boss.Cx + 0.5, boss.Cy + 0.5);
            // Neuter the party's damage so the gate is observed in isolation (no mid-step boss kill).
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party)) e.Stats[StatKey.Atk] = 0;

            // Drop the party INSIDE the locked boss room (the strongest possible violation — any
            // legal crossing is milder) and step: the gate must place every hero back outside.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party)) e.Pos = inside;
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(3));
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party && e.Alive))
                Assert.NotEqual(boss.Id, a.RoomAt(e.Pos));

            // With the key in hand the same entry stands — and the boss room seals around the fight.
            s.DungeonBossKeyHeld = true;
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party)) e.Pos = inside;
            Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(3));
            Assert.Equal(boss.Id, s.DungeonSealedRoomId);
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party && e.Alive))
                Assert.Equal(boss.Id, a.RoomAt(e.Pos));
        }
    }
}
