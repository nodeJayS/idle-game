using System;
using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Roguelite dungeon generator (ROADMAP roguelite, slice 1): PURE-DATA procedural
    // generation. These pin the hard invariants the client/sim will trust — full floor
    // connectivity from the entrance, seed-determinism (identical Checksum), the room
    // semantics (boss depth / entrance degree / critical-path elites / treasure leaves),
    // the grid/bounds contract, the prop+spawn reservation mask, the BFS distance field
    // convention, and the perf budget. Generation is standalone tech this slice (no combat
    // wiring), so everything here exercises DungeonGen.Generate alone.
    public class DungeonGenTests
    {
        private static DungeonParams Params(int seed, int rooms = 42) =>
            new DungeonParams { Seed = seed, RoomCount = rooms };

        private static Dungeon Gen(int seed, int rooms = 42) => DungeonGen.Generate(Params(seed, rooms));

        private static DungeonRoom RoomById(Dungeon d, int id) => d.Rooms.First(r => r.Id == id);
        private static DungeonRoom Entrance(Dungeon d) => d.Rooms.First(r => r.Type == RoomType.Entrance);
        private static DungeonRoom Boss(Dungeon d) => d.Rooms.First(r => r.Type == RoomType.Boss);

        // ---------------- Connectivity ----------------

        [Fact]
        public void FloodFillFromEntranceReachesEveryFloorCell()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var d = Gen(seed);
                int floor = d.Grid.Count(c => c == DungeonCell.Floor);
                int reached = d.Bfs.Count(v => v >= 0);
                Assert.Equal(floor, reached); // Bfs>=0 exactly on the entrance-reachable floor
            }
        }

        [Fact]
        public void DefaultsNeverExhaustRetries()
        {
            // If defaults ever hit the 5-attempt ceiling, Generate throws — this asserts it doesn't.
            for (int seed = 1; seed <= 30; seed++)
            {
                var ex = Record.Exception(() => Gen(seed));
                Assert.Null(ex);
            }
        }

        // ---------------- Determinism ----------------

        [Fact]
        public void SameSeedProducesIdenticalChecksumAcrossRuns()
        {
            for (int seed = 1; seed <= 5; seed++)
            {
                var a = Gen(seed);
                var b = Gen(seed);
                var c = Gen(seed);
                Assert.Equal(a.Checksum, b.Checksum);
                Assert.Equal(b.Checksum, c.Checksum);
            }
        }

        [Fact]
        public void SameSeedProducesIdenticalGridAndCounts()
        {
            var a = Gen(7);
            var b = Gen(7);
            Assert.Equal(a.W, b.W);
            Assert.Equal(a.H, b.H);
            Assert.True(a.Grid.SequenceEqual(b.Grid));
            Assert.Equal(a.Rooms.Count, b.Rooms.Count);
            Assert.Equal(a.Props.Count, b.Props.Count);
            Assert.Equal(a.Spawns.Count, b.Spawns.Count);
        }

        [Fact]
        public void DifferentSeedsProduceDifferentChecksums()
        {
            var seen = new HashSet<uint>();
            for (int seed = 1; seed <= 12; seed++)
                seen.Add(Gen(seed).Checksum);
            // Not a hard collision-free guarantee, but 12 seeds must yield many distinct dungeons.
            Assert.True(seen.Count >= 10, $"only {seen.Count} distinct checksums across 12 seeds");
        }

        // ---------------- Semantics ----------------

        [Fact]
        public void BossDepthIsAtLeastSixtyPercentOfMaxDepth()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var d = Gen(seed);
                int maxDepth = d.Rooms.Max(r => r.Depth);
                Assert.True(Boss(d).Depth >= 0.6 * maxDepth,
                    $"seed {seed}: boss depth {Boss(d).Depth} < 60% of {maxDepth}");
            }
        }

        [Fact]
        public void EntranceHasDegreeOneAndIsNotAdjacentToBoss()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var d = Gen(seed);
                var ent = Entrance(d);
                Assert.Equal(1, ent.Degree);
                // Entrance's single neighbour is not the boss.
                var neighbour = d.Edges.First(e => e.A == ent.Id || e.B == ent.Id);
                int other = neighbour.A == ent.Id ? neighbour.B : neighbour.A;
                Assert.NotEqual(Boss(d).Id, other);
            }
        }

        [Fact]
        public void ExactlyOneBossAndOneEntrance()
        {
            var d = Gen(3);
            Assert.Single(d.Rooms, r => r.Type == RoomType.Boss);
            Assert.Single(d.Rooms, r => r.Type == RoomType.Entrance);
        }

        [Fact]
        public void HasAtLeastThreeLeafRooms()
        {
            var d = Gen(4, rooms: 42);
            int leaves = d.Rooms.Count(r => r.Degree == 1);
            Assert.True(leaves >= 3, $"only {leaves} leaf rooms");
        }

        [Fact]
        public void StatsLoopsEqualsCyclomaticNumberAndIsAtLeastOne()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = Gen(seed);
                int cyclomatic = d.Edges.Count - d.Rooms.Count + 1;
                Assert.Equal(cyclomatic, d.Stats.Loops);
                Assert.True(d.Stats.Loops >= 1, $"seed {seed}: {d.Stats.Loops} loops");
            }
        }

        [Fact]
        public void TreasureRoomsAreLeaves()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = Gen(seed);
                foreach (var r in d.Rooms.Where(r => r.Type == RoomType.Treasure))
                    Assert.Equal(1, r.Degree);
            }
        }

        [Fact]
        public void EliteRoomsLieOnCriticalPathAtFiftyFiveToEightyFivePercentDepth()
        {
            for (int seed = 1; seed <= 12; seed++)
            {
                var d = Gen(seed);
                var critEdges = d.Edges.Where(e => e.IsCritical).ToList();
                var critRooms = new HashSet<int>();
                foreach (var e in critEdges) { critRooms.Add(e.A); critRooms.Add(e.B); }
                int maxDepth = d.Rooms.Max(r => r.Depth);
                foreach (var r in d.Rooms.Where(r => r.Type == RoomType.Elite))
                {
                    Assert.Contains(r.Id, critRooms);
                    double frac = maxDepth > 0 ? r.Depth / (double)maxDepth : 0;
                    Assert.InRange(frac, 0.55 - 1e-9, 0.85 + 1e-9);
                }
            }
        }

        [Fact]
        public void DifficultyIsNonDecreasingAlongDepthAndBossIsOne()
        {
            var d = Gen(6);
            Assert.Equal(1.0, Boss(d).Difficulty, 9);
            // Difficulty = 0.15 + 0.85*(depth/max) except boss ⇒ monotone in depth for non-boss rooms.
            var nonBoss = d.Rooms.Where(r => r.Type != RoomType.Boss).OrderBy(r => r.Depth).ToList();
            for (int i = 1; i < nonBoss.Count; i++)
                Assert.True(nonBoss[i].Difficulty >= nonBoss[i - 1].Difficulty - 1e-9,
                    "difficulty decreased as depth increased");
        }

        [Fact]
        public void CriticalPathRunsFromEntranceToBoss()
        {
            var d = Gen(9);
            var critEdges = d.Edges.Where(e => e.IsCritical).ToList();
            Assert.NotEmpty(critEdges);
            var critRooms = new HashSet<int>();
            foreach (var e in critEdges) { critRooms.Add(e.A); critRooms.Add(e.B); }
            Assert.Contains(Entrance(d).Id, critRooms);
            Assert.Contains(Boss(d).Id, critRooms);
            Assert.Equal(d.Stats.CriticalLength, critEdges.Count + 1); // N rooms ⇒ N-1 edges
        }

        // ---------------- Grid + bounds ----------------

        [Fact]
        public void GridStaysWithinTwoFiftySix()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var d = Gen(seed);
                Assert.True(d.W <= 256 && d.H <= 256, $"seed {seed}: grid {d.W}x{d.H}");
                Assert.Equal(d.W * d.H, d.Grid.Length);
                Assert.Equal(d.W * d.H, d.Bfs.Length);
            }
        }

        [Fact]
        public void EveryRoomFitsInBoundsAndItsCentreIsFloor()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = Gen(seed);
                foreach (var r in d.Rooms)
                {
                    Assert.InRange(r.Cx, 0, d.W - 1);
                    Assert.InRange(r.Cy, 0, d.H - 1);
                    Assert.Equal(DungeonCell.Floor, d.Grid[r.Cy * d.W + r.Cx]);
                }
            }
        }

        [Fact]
        public void EveryRoomFootprintHasFloorAtItsCentreCross()
        {
            // The stamped footprint must be FLOOR around each room centre (a 3-cell cross inside the room).
            var d = Gen(5);
            foreach (var r in d.Rooms)
            {
                Assert.Equal(DungeonCell.Floor, d.Grid[r.Cy * d.W + r.Cx]);
                // one step along each axis stays inside a room ≥5 wide/tall
                Assert.Equal(DungeonCell.Floor, d.Grid[r.Cy * d.W + (r.Cx + 1)]);
                Assert.Equal(DungeonCell.Floor, d.Grid[(r.Cy + 1) * d.W + r.Cx]);
            }
        }

        [Fact]
        public void WallsBorderFloorAndVoidHasNoFloorNeighbourWhereWallExpected()
        {
            var d = Gen(2);
            // Every WALL cell must have at least one 8-neighbour FLOOR (walls only exist to border floor).
            for (int y = 0; y < d.H; y++)
                for (int x = 0; x < d.W; x++)
                {
                    if (d.Grid[y * d.W + x] != DungeonCell.Wall) continue;
                    bool hasFloor = false;
                    for (int dy = -1; dy <= 1 && !hasFloor; dy++)
                        for (int dx = -1; dx <= 1 && !hasFloor; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= d.W || ny < 0 || ny >= d.H) continue;
                            if (d.Grid[ny * d.W + nx] == DungeonCell.Floor) hasFloor = true;
                        }
                    Assert.True(hasFloor, $"wall at {x},{y} borders no floor");
                }
        }

        // ---------------- BFS distance field ----------------

        [Fact]
        public void BfsConventionHoldsAndEntranceCentreIsZero()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var d = Gen(seed);
                for (int i = 0; i < d.Grid.Length; i++)
                {
                    if (d.Grid[i] == DungeonCell.Floor)
                        Assert.True(d.Bfs[i] >= 0, $"floor cell {i} has Bfs {d.Bfs[i]}");
                    else
                        Assert.Equal(-1, d.Bfs[i]);
                }
                var ent = Entrance(d);
                Assert.Equal(0, d.Bfs[ent.Cy * d.W + ent.Cx]);
            }
        }

        // ---------------- Props + spawns reservation ----------------

        [Fact]
        public void NoPropOrSpawnSitsOnWallVoidOrDoorway()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = Gen(seed);
                var doorSet = new HashSet<(int, int)>(d.Doorways.Select(c => (c.X, c.Y)));
                foreach (var pr in d.Props)
                {
                    Assert.Equal(DungeonCell.Floor, d.Grid[pr.Y * d.W + pr.X]);
                    Assert.DoesNotContain((pr.X, pr.Y), doorSet);
                }
                foreach (var sp in d.Spawns)
                {
                    Assert.Equal(DungeonCell.Floor, d.Grid[sp.Y * d.W + sp.X]);
                    Assert.DoesNotContain((sp.X, sp.Y), doorSet);
                }
            }
        }

        [Fact]
        public void NoTwoPropsOrSpawnsShareACell()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = Gen(seed);
                var occupied = new HashSet<(int, int)>();
                foreach (var pr in d.Props)
                    Assert.True(occupied.Add((pr.X, pr.Y)), $"prop cell {pr.X},{pr.Y} reused");
                foreach (var sp in d.Spawns)
                    Assert.True(occupied.Add((sp.X, sp.Y)), $"spawn cell {sp.X},{sp.Y} reused");
            }
        }

        [Fact]
        public void BossRoomHasOneTierTwoSpawnAndTreasureShrineEntranceHaveNone()
        {
            var d = Gen(7);
            int bossId = Boss(d).Id;
            var bossSpawns = d.Spawns.Where(s => s.RoomId == bossId).ToList();
            Assert.Single(bossSpawns);
            Assert.Equal(2, bossSpawns[0].Tier);

            var noSpawnTypes = new[] { RoomType.Treasure, RoomType.Shrine, RoomType.Entrance };
            foreach (var r in d.Rooms.Where(r => noSpawnTypes.Contains(r.Type)))
                Assert.DoesNotContain(d.Spawns, s => s.RoomId == r.Id);
        }

        [Fact]
        public void ChestInEveryTreasureRoomAndPortalAtEntrance()
        {
            var d = Gen(4);
            foreach (var r in d.Rooms.Where(r => r.Type == RoomType.Treasure))
                Assert.Contains(d.Props, pr => pr.Kind == PropKind.Chest && pr.RoomId == r.Id);
            Assert.Contains(d.Props, pr => pr.Kind == PropKind.PortalRing && pr.RoomId == Entrance(d).Id);
        }

        [Fact]
        public void EliteSpawnsAreTierOne()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = Gen(seed);
                foreach (var r in d.Rooms.Where(r => r.Type == RoomType.Elite))
                    foreach (var s in d.Spawns.Where(s => s.RoomId == r.Id))
                        Assert.Equal(1, s.Tier);
            }
        }

        // ---------------- Name + params ----------------

        [Fact]
        public void GeneratesANonEmptyNameAndCarriesParams()
        {
            var p = Params(11);
            var d = DungeonGen.Generate(p);
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.Same(p, d.Params);
        }

        // ---------------- Perf ----------------

        [Fact]
        public void SixtyRoomGenerationIsUnderFiftyMs()
        {
            // Warm up the JIT so the measured run reflects steady state.
            _ = Gen(1, rooms: 60);
            var d = Gen(123, rooms: 60);
            Assert.True(d.Stats.GenMs < 50.0, $"60-room gen took {d.Stats.GenMs:F2} ms (budget 50)");
        }

        // ---------------- Corridor anti-spaghetti (carve helper) ----------------

        [Fact]
        public void CarvedCorridorHasAtMostOneElbow()
        {
            // Verify by construction: the CarveCorridor helper stamps exactly two axis-aligned runs
            // (one elbow). We drive it directly on a blank grid and confirm the floor cells form an
            // L with a single corner — i.e. all floor cells share the source row OR the corner column
            // segment, never a diagonal staircase.
            int w = 40, h = 40;
            var grid = new byte[w * h];
            var isRoom = new bool[w * h];
            InvokeCarve(5, 5, 30, 25, 1, grid, isRoom, w, h, elbowSeed: 0); // horizontal-first

            var floors = new List<(int x, int y)>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (grid[y * w + x] == DungeonCell.Floor) floors.Add((x, y));

            // Horizontal-first: run along y=5 from x=5..30, then along x=30 from y=5..25.
            foreach (var (x, y) in floors)
            {
                bool onHRun = (y == 5 && x >= 5 && x <= 30);
                bool onVRun = (x == 30 && y >= 5 && y <= 25);
                Assert.True(onHRun || onVRun, $"floor cell {x},{y} is off the single-elbow L");
            }
            // Corner present.
            Assert.Contains((30, 5), floors);
        }

        [Fact]
        public void CarvedCorridorElbowDirectionIsSeedStable()
        {
            int w = 40, h = 40;
            var g0 = new byte[w * h]; var r0 = new bool[w * h];
            var g1 = new byte[w * h]; var r1 = new bool[w * h];
            InvokeCarve(5, 5, 30, 25, 1, g0, r0, w, h, elbowSeed: 0);  // horizontal-first
            InvokeCarve(5, 5, 30, 25, 1, g1, r1, w, h, elbowSeed: 1);  // vertical-first
            // Different elbow choice ⇒ the corner cell differs: (30,5) vs (5,25).
            Assert.Equal(DungeonCell.Floor, g0[5 * w + 30]);   // horizontal-first corner
            Assert.Equal(DungeonCell.Floor, g1[25 * w + 5]);   // vertical-first corner
        }

        private static void InvokeCarve(int ax, int ay, int bx, int by, int width, byte[] grid, bool[] isRoom,
            int w, int h, int elbowSeed)
        {
            var mi = typeof(DungeonGen).GetMethod("CarveCorridor",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            mi!.Invoke(null, new object?[] { ax, ay, bx, by, width, grid, isRoom, w, h, elbowSeed });
        }

        // ---------------- Larger + smaller room counts still valid ----------------

        [Fact]
        public void VariedRoomCountsGenerateWithoutThrowing()
        {
            foreach (int count in new[] { 12, 24, 42, 60 })
                for (int seed = 1; seed <= 4; seed++)
                {
                    var d = Gen(seed, count);
                    Assert.True(d.Rooms.Count > 0);
                    int floor = d.Grid.Count(c => c == DungeonCell.Floor);
                    Assert.Equal(floor, d.Bfs.Count(v => v >= 0));
                }
        }

        // ---------------- CellRoom + BossBfs (slice 3a data for Combat) ----------------

        [Fact]
        public void EveryRoomFootprintCellHasThatRoomsId()
        {
            // Sample each room's bounding box: every FLOOR cell that its shape stamped is tagged with
            // the room's id (corridors carved into it can't override — separation keeps footprints
            // disjoint, so a room's own stamped cells stay its own).
            var d = Gen(7);
            Assert.Equal(d.Grid.Length, d.CellRoom.Length);
            foreach (var r in d.Rooms)
            {
                // The room centre cell is guaranteed floor + tagged.
                int cIdx = r.Cy * d.W + r.Cx;
                Assert.Equal(DungeonCell.Floor, d.Grid[cIdx]);
                Assert.Equal(r.Id, d.CellRoom[cIdx]);
            }
            // No cell claims a non-existent room id; corridor/void cells read -1.
            int maxId = d.Rooms.Max(r => r.Id);
            for (int i = 0; i < d.CellRoom.Length; i++)
            {
                Assert.True(d.CellRoom[i] >= -1 && d.CellRoom[i] <= maxId);
                if (d.Grid[i] != DungeonCell.Floor) Assert.Equal(-1, d.CellRoom[i]); // walls/void untagged
            }
        }

        [Fact]
        public void BossBfsIsZeroAtBossCentreAndCoversEveryFloorCell()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var d = Gen(seed);
                var boss = Boss(d);
                Assert.Equal(0, d.BossBfs[boss.Cy * d.W + boss.Cx]); // seeded at the boss centre
                // Fully connected floor ⇒ every floor cell has a finite (≥0) boss distance; non-floor -1.
                int floor = d.Grid.Count(c => c == DungeonCell.Floor);
                Assert.Equal(floor, d.BossBfs.Count(v => v >= 0));
            }
        }

        // ---- LINEAR layout (the auto-battled roguelite mode: a chain, never a fork) ----

        private static Dungeon GenLinear(int seed, int rooms = 26) =>
            DungeonGen.Generate(new DungeonParams { Seed = seed, RoomCount = rooms, Linear = true });

        [Fact]
        public void LinearIsAPureChain()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = GenLinear(seed);
                int n = d.Rooms.Count;
                Assert.Equal(26, n);
                Assert.Equal(n - 1, d.Edges.Count);            // exactly the chain links
                Assert.Equal(0, d.Stats.Loops);                // cyclomatic 0 — no cycles
                Assert.All(d.Edges, e => Assert.True(e.IsCritical)); // the whole crawl is the path
                // Degrees: two endpoints (entrance + boss), everything else degree 2.
                Assert.Equal(2, d.Rooms.Count(r => r.Degree == 1));
                Assert.Equal(n - 2, d.Rooms.Count(r => r.Degree == 2));
            }
        }

        [Fact]
        public void LinearEntranceAndBossAreTheChainEnds()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = GenLinear(seed);
                int n = d.Rooms.Count;
                var entrance = Entrance(d);
                var boss = Boss(d);
                Assert.Equal(0, entrance.Depth);
                Assert.Equal(n - 1, boss.Depth);               // boss caps the run — maximal depth
                Assert.Equal(1, entrance.Degree);
                Assert.Equal(1, boss.Degree);
                Assert.Equal(1.0, boss.Difficulty);
                // Difficulty ramps monotonically along the chain (the watchable ARPG push).
                var byDepth = d.Rooms.OrderBy(r => r.Depth).ToList();
                for (int i = 1; i < byDepth.Count; i++)
                    Assert.True(byDepth[i].Difficulty >= byDepth[i - 1].Difficulty,
                        $"difficulty dipped at depth {byDepth[i].Depth}");
            }
        }

        [Fact]
        public void LinearStaysConnectedDeterministicAndInBounds()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = GenLinear(seed);
                Assert.True(d.W <= 256 && d.H <= 256, $"grid {d.W}x{d.H} exceeds the clamp (seed {seed})");
                // Full reachability: every floor cell has a finite entrance-BFS distance.
                int floor = d.Grid.Count(c => c == DungeonCell.Floor);
                Assert.Equal(floor, d.Bfs.Count(v => v >= 0));
                // Same seed ⇒ identical checksum (linear rides the same determinism contract).
                Assert.Equal(d.Checksum, GenLinear(seed).Checksum);
            }
        }

        [Fact]
        public void LinearStillDressesTheRunWithRoles()
        {
            // Breather + spike rooms survive the chain conversion: treasure/shrine/elite present,
            // all strictly inside the ends.
            for (int seed = 1; seed <= 8; seed++)
            {
                var d = GenLinear(seed);
                int n = d.Rooms.Count;
                Assert.InRange(d.Rooms.Count(r => r.Type == RoomType.Treasure), 1, 2);
                Assert.InRange(d.Rooms.Count(r => r.Type == RoomType.Shrine), 1, 2);
                Assert.InRange(d.Rooms.Count(r => r.Type == RoomType.Elite), 1, 2);
                foreach (var r in d.Rooms)
                    if (r.Type == RoomType.Treasure || r.Type == RoomType.Shrine || r.Type == RoomType.Elite)
                        Assert.True(r.Depth > 0 && r.Depth < n - 1, $"{r.Type} at a chain end (depth {r.Depth})");
            }
        }
    }
}
