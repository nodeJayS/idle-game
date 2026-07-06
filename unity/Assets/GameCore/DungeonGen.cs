#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IdleGame.GameCore
{
    // ========================================================================
    // Procedural dungeon PIPELINE (ROADMAP roguelite, slice 1). Pure data in,
    // pure data out: DungeonGen.Generate(params) → Dungeon. Every stage is a
    // discrete private step; all randomness flows through ONE local Rng seeded
    // from Params.Seed, so a seed reproduces a dungeon bit-for-bit on any host.
    //
    // Determinism rules obeyed throughout:
    //  - one local `new Rng(seed)` per attempt; never the save cursor.
    //  - no System.Random, no clock feeding logic (Stopwatch only measures).
    //  - every list is sorted by an explicit, total ordering before any logic
    //    consumes it — no unordered Set/Dictionary iteration drives decisions.
    //  - explicit FNV-1a for the checksum (never GetHashCode, which is salted).
    //
    // Robustness: each generation validates (connected floor, ≥1 loop at
    // defaults, boss deep enough, entrance degree 1); on failure it re-rolls
    // with a derived seed up to 5 attempts, then throws with the failed stage.
    // Tests assert the DEFAULTS never hit that ceiling.
    //
    // Delaunay path: a deterministic k-nearest-neighbour graph (the spec's
    // sanctioned fallback). It is symmetric, connected via Prim's MST over the
    // union of kNN candidate edges, and re-adds spare edges as loops — which
    // gives the required "≥1 loop at defaults" without Bowyer–Watson's
    // degenerate-cocircular fragility. See BuildGraph for the rationale.
    // ========================================================================

    /// <summary>Generates a <see cref="Dungeon"/> from <see cref="DungeonParams"/>. Static, pure, deterministic.</summary>
    public static class DungeonGen
    {
        private const int MaxAttempts = 5;
        private const int MaxGridDim = 256;
        private const int Margin = 2;

        /// <summary>
        /// Build a dungeon. Retries internally with a derived seed on validation failure (up to
        /// <see cref="MaxAttempts"/>), then throws <see cref="InvalidOperationException"/> naming the
        /// failed stage. At default params this never exhausts the retries (tests pin that).
        /// </summary>
        public static Dungeon Generate(DungeonParams p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            var sw = Stopwatch.StartNew();

            int seed = p.Seed;
            string lastFail = "unknown";
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var rng = new Rng(unchecked((uint)seed));
                if (TryGenerate(p, rng, out var d, out lastFail))
                {
                    sw.Stop();
                    d.Stats.GenMs = sw.Elapsed.TotalMilliseconds;
                    d.Checksum = ComputeChecksum(d);
                    return d;
                }
                // Derive a fresh seed and try again (LCG step keeps it deterministic per input seed).
                seed = unchecked(seed * 747796405 + (attempt + 1));
            }
            sw.Stop();
            throw new InvalidOperationException($"DungeonGen failed after {MaxAttempts} attempts at stage: {lastFail}");
        }

        // --------------------------------------------------------------------
        // Stage 1 — RNG helpers over the existing Rng (no Rng.cs changes).
        // --------------------------------------------------------------------

        /// <summary>Double in [a, b).</summary>
        private static double Float(Rng rng, double a, double b) => rng.RandRange(a, b);

        /// <summary>Integer in [a, b] inclusive.</summary>
        private static int Int(Rng rng, int a, int b) => rng.RandInt(a, b);

        /// <summary>Uniform pick from a list (never empty in this pipeline).</summary>
        private static T Pick<T>(Rng rng, IReadOnlyList<T> items) => items[rng.RandInt(0, items.Count - 1)];

        /// <summary>True with probability <paramref name="p"/>.</summary>
        private static bool Chance(Rng rng, double p) => rng.Next() < p;

        /// <summary>Gaussian sample via Box–Muller, consuming EXACTLY two draws for determinism.</summary>
        private static double Gaussian(Rng rng, double mu, double sigma)
        {
            double u1 = rng.Next();
            double u2 = rng.Next();
            if (u1 < 1e-12) u1 = 1e-12; // avoid log(0); keeps the draw count fixed at 2
            double mag = Math.Sqrt(-2.0 * Math.Log(u1));
            return mu + sigma * mag * Math.Cos(2.0 * Math.PI * u2);
        }

        // --------------------------------------------------------------------
        // Working types (mutable, internal to a single generation attempt).
        // --------------------------------------------------------------------

        private sealed class RoomWork
        {
            public int Id;
            public double Cx, Cy;      // float centre during scatter/separation, snapped to int at rasterise
            public int W, H;
            public RoomShape Shape;
            public RoomType Type = RoomType.Combat;
            public int Depth;
            public double Difficulty;
            public int Degree;
            public double Area => W * (double)H;
        }

        private static bool TryGenerate(DungeonParams p, Rng rng, out Dungeon dungeon, out string failStage)
        {
            dungeon = new Dungeon();
            failStage = "";

            // 2 — Scatter candidates.
            var rooms = ScatterRooms(p, rng);

            // 3 — Separate + cull to RoomCount.
            SeparateRooms(rooms);
            rooms = CullToCount(rooms, p.RoomCount);
            if (rooms.Count < 3) { failStage = "scatter"; return false; }

            // 4 — Connectivity graph (kNN → MST → loops).
            var edges = BuildGraph(rooms, p, rng, out int loops);
            if (loops < 1) { failStage = "graph-loops"; return false; }

            // Adjacency + degrees.
            var adj = BuildAdjacency(rooms.Count, edges);
            for (int i = 0; i < rooms.Count; i++) rooms[i].Degree = adj[i].Count;

            // 5 — Semantics (roles, depth, difficulty, critical path).
            if (!AssignSemantics(rooms, edges, adj, out var criticalRooms, out int bossId, out int entranceId))
            { failStage = "semantics"; return false; }

            // 6 — Carve corridors + stamp room footprints into a float-space grid, then
            // 7 — Rasterise into the final W×H byte grid with a margin.
            if (!CarveAndRasterise(rooms, edges, entranceId, bossId, out var grid, out int w, out int h,
                    out var corridorCells, out var doorways, out var bfs, out var cellRoom, out var bossBfs,
                    out int entranceCx, out int entranceCy, out int ox, out int oy, out failStage))
                return false;

            // Validation: entrance degree, boss depth, connectivity already checked inside carve/graph.
            var entrance = rooms[entranceId];
            if (entrance.Degree != 1) { failStage = "entrance-degree"; return false; }

            int maxDepth = 0;
            foreach (var r in rooms) if (r.Depth > maxDepth) maxDepth = r.Depth;
            if (maxDepth > 0 && rooms[bossId].Depth < 0.6 * maxDepth) { failStage = "boss-depth"; return false; }

            // 8 — Decoration (props + spawns) with a reserved mask.
            var props = new List<DungeonProp>();
            var spawns = new List<DungeonSpawn>();
            Decorate(p, rng, rooms, grid, w, h, doorways, bossId, ox, oy, props, spawns);

            // 9 — Metadata assembly (room centres in GRID space, translated by the rasterise offset).
            var outRooms = new List<DungeonRoom>();
            foreach (var r in rooms)
                outRooms.Add(new DungeonRoom
                {
                    Id = r.Id, Cx = (int)Math.Round(r.Cx) - ox, Cy = (int)Math.Round(r.Cy) - oy,
                    W = r.W, H = r.H, Shape = r.Shape, Type = r.Type,
                    Depth = r.Depth, Difficulty = r.Difficulty, Degree = r.Degree,
                });

            int floorTiles = 0, wallTiles = 0;
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i] == DungeonCell.Floor) floorTiles++;
                else if (grid[i] == DungeonCell.Wall) wallTiles++;
            }

            dungeon.Params = p;
            dungeon.Name = MakeName(p, rng);
            dungeon.W = w;
            dungeon.H = h;
            dungeon.Grid = grid;
            dungeon.Bfs = bfs;
            dungeon.CellRoom = cellRoom;
            dungeon.BossBfs = bossBfs;
            dungeon.Rooms = outRooms;
            dungeon.Edges = edges;
            dungeon.Doorways = doorways;
            dungeon.CorridorCells = corridorCells;
            dungeon.Props = props;
            dungeon.Spawns = spawns;
            dungeon.Stats = new DungeonStats
            {
                Rooms = outRooms.Count,
                Edges = edges.Count,
                Loops = loops,
                CriticalLength = criticalRooms.Count,
                FloorTiles = floorTiles,
                WallTiles = wallTiles,
                Props = props.Count,
            };
            return true;
        }

        // --------------------------------------------------------------------
        // Stage 2 — Room scatter.
        // Candidates = RoomCount*1.4, placed in an ellipse whose radius grows
        // with sqrt(RoomCount) (constant areal density). Archetype + shape are
        // drawn from weighted tables; ≥2 large rooms are forced deterministically.
        // --------------------------------------------------------------------

        private static List<RoomWork> ScatterRooms(DungeonParams p, Rng rng)
        {
            int candidateCount = (int)Math.Round(p.RoomCount * 1.4);
            if (candidateCount < 3) candidateCount = 3;

            // Ellipse radius ∝ sqrt(RoomCount): area ∝ RoomCount ⇒ constant density.
            double radius = 6.0 * Math.Sqrt(p.RoomCount);
            double radX = radius * Float(rng, 1.0, 1.25);
            double radY = radius * Float(rng, 1.0, 1.25);

            var rooms = new List<RoomWork>(candidateCount);
            for (int i = 0; i < candidateCount; i++)
            {
                // Uniform-ish disc sample (sqrt for radial uniformity), then scale to the ellipse.
                double ang = Float(rng, 0, Math.PI * 2);
                double rr = Math.Sqrt(rng.Next());
                double cx = Math.Cos(ang) * rr * radX;
                double cy = Math.Sin(ang) * rr * radY;

                var (w, h, shape) = RollArchetype(rng);
                rooms.Add(new RoomWork { Id = i, Cx = cx, Cy = cy, W = w, H = h, Shape = shape });
            }

            // Force ≥2 large rooms: count larges, and if short, re-roll the largest-area candidates'
            // archetype to a guaranteed large band. Sort by area desc (id tiebreak) so the choice is
            // deterministic and independent of scatter order.
            int LargeThreshold = 13;
            int larges = 0;
            foreach (var r in rooms) if (Math.Max(r.W, r.H) >= LargeThreshold) larges++;
            if (larges < 2)
            {
                var byArea = new List<RoomWork>(rooms);
                byArea.Sort((a, b) =>
                {
                    int c = b.Area.CompareTo(a.Area);
                    return c != 0 ? c : a.Id.CompareTo(b.Id);
                });
                int need = 2 - larges;
                for (int k = 0; k < byArea.Count && need > 0; k++)
                {
                    var r = byArea[k];
                    if (Math.Max(r.W, r.H) >= LargeThreshold) continue; // already large
                    r.W = Int(rng, 13, 18);
                    r.H = Int(rng, 13, 18);
                    need--;
                }
            }
            return rooms;
        }

        /// <summary>Weighted archetype (size band) + shape roll. Dims are drawn per-axis inside the band.</summary>
        private static (int w, int h, RoomShape shape) RollArchetype(Rng rng)
        {
            double t = rng.Next();
            int w, h;
            if (t < 0.45) { w = Int(rng, 5, 7); h = Int(rng, 5, 7); }        // small 45%
            else if (t < 0.85) { w = Int(rng, 8, 12); h = Int(rng, 8, 12); } // medium 40%
            else { w = Int(rng, 13, 18); h = Int(rng, 13, 18); }            // large 15%

            double s = rng.Next();
            RoomShape shape = s < 0.60 ? RoomShape.Rectangle
                            : s < 0.82 ? RoomShape.Ellipse
                            : RoomShape.Octagon;
            return (w, h, shape);
        }

        // --------------------------------------------------------------------
        // Stage 3 — Separation (AABB push-apart) + cull.
        // --------------------------------------------------------------------

        private static void SeparateRooms(List<RoomWork> rooms)
        {
            const int Pad = 2;
            const int MaxIter = 300;
            for (int iter = 0; iter < MaxIter; iter++)
            {
                bool moved = false;
                for (int i = 0; i < rooms.Count; i++)
                {
                    for (int j = i + 1; j < rooms.Count; j++)
                    {
                        var a = rooms[i];
                        var b = rooms[j];
                        double halfWa = a.W / 2.0 + Pad, halfHa = a.H / 2.0 + Pad;
                        double halfWb = b.W / 2.0, halfHb = b.H / 2.0;
                        double dx = b.Cx - a.Cx, dy = b.Cy - a.Cy;
                        double overlapX = (halfWa + halfWb) - Math.Abs(dx);
                        double overlapY = (halfHa + halfHb) - Math.Abs(dy);
                        if (overlapX > 0 && overlapY > 0)
                        {
                            moved = true;
                            // Push along the least-overlap axis (stable, deterministic tiebreak).
                            if (overlapX < overlapY)
                            {
                                double push = overlapX / 2.0 + 0.01;
                                double dir = dx == 0 ? (a.Id <= b.Id ? -1 : 1) : Math.Sign(dx);
                                a.Cx -= dir * push; b.Cx += dir * push;
                            }
                            else
                            {
                                double push = overlapY / 2.0 + 0.01;
                                double dir = dy == 0 ? (a.Id <= b.Id ? -1 : 1) : Math.Sign(dy);
                                a.Cy -= dir * push; b.Cy += dir * push;
                            }
                        }
                    }
                }
                if (!moved) break;
            }
            // Snap centres to the integer grid.
            foreach (var r in rooms) { r.Cx = Math.Round(r.Cx); r.Cy = Math.Round(r.Cy); }
        }

        /// <summary>Cull the smallest overflow candidates down to <paramref name="count"/> (deterministic order).</summary>
        private static List<RoomWork> CullToCount(List<RoomWork> rooms, int count)
        {
            if (rooms.Count <= count) return rooms;
            var sorted = new List<RoomWork>(rooms);
            // Keep largest by area; tiebreak by id for determinism.
            sorted.Sort((a, b) =>
            {
                int c = b.Area.CompareTo(a.Area);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            var kept = sorted.GetRange(0, count);
            // Re-index ids 0..count-1 in a STABLE order (original id) so downstream is deterministic.
            kept.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < kept.Count; i++) kept[i].Id = i;
            return kept;
        }

        // --------------------------------------------------------------------
        // Stage 4 — Connectivity graph.
        // kNN candidate edges (k≈4) → Prim MST for guaranteed connectivity →
        // re-add spare candidate edges with probability LoopChance, rejecting
        // any longer than 2.2× the mean MST edge. Loops = cyclomatic number.
        // --------------------------------------------------------------------

        private static List<DungeonEdge> BuildGraph(List<RoomWork> rooms, DungeonParams p, Rng rng, out int loops)
        {
            int n = rooms.Count;
            loops = 0;

            // Candidate edge set: each room's k nearest neighbours (undirected, deduped).
            int k = Math.Min(6, Math.Max(3, n - 1));
            var candSet = new HashSet<long>();
            var candidates = new List<(int a, int b, double len)>();
            for (int i = 0; i < n; i++)
            {
                var order = new List<(int j, double d2)>();
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    double dx = rooms[i].Cx - rooms[j].Cx, dy = rooms[i].Cy - rooms[j].Cy;
                    order.Add((j, dx * dx + dy * dy));
                }
                order.Sort((x, y) =>
                {
                    int c = x.d2.CompareTo(y.d2);
                    return c != 0 ? c : x.j.CompareTo(y.j);
                });
                int take = Math.Min(k, order.Count);
                for (int m = 0; m < take; m++)
                {
                    int j = order[m].j;
                    int lo = Math.Min(i, j), hi = Math.Max(i, j);
                    long key = ((long)lo << 32) | (uint)hi;
                    if (candSet.Add(key))
                        candidates.Add((lo, hi, Math.Sqrt(order[m].d2)));
                }
            }
            // Deterministic candidate order (by length, then endpoints).
            candidates.Sort((x, y) =>
            {
                int c = x.len.CompareTo(y.len);
                if (c != 0) return c;
                c = x.a.CompareTo(y.a);
                return c != 0 ? c : x.b.CompareTo(y.b);
            });

            // Prim MST over the candidate graph (falls back to nearest-unconnected if kNN under-connects).
            var mst = PrimMst(rooms, candidates, out double meanMstLen);

            var edges = new List<DungeonEdge>();
            var used = new HashSet<long>();
            foreach (var (a, b) in mst)
            {
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                used.Add(((long)lo << 32) | (uint)hi);
                edges.Add(new DungeonEdge { A = lo, B = hi, IsLoop = false });
            }

            // Re-add spare candidate edges as loops (deterministic order already applied).
            double maxLoopLen = meanMstLen * 2.2;
            foreach (var c in candidates)
            {
                long key = ((long)c.a << 32) | (uint)c.b;
                if (used.Contains(key)) continue;
                if (c.len > maxLoopLen) continue;
                if (!Chance(rng, p.LoopChance)) continue;
                used.Add(key);
                edges.Add(new DungeonEdge { A = c.a, B = c.b, IsLoop = true });
                loops++;
            }

            // Cyclomatic guarantee: if defaults produced zero loops (small graphs / unlucky rolls),
            // add the shortest eligible spare edge so there is always ≥1 cycle.
            if (loops == 0)
            {
                foreach (var c in candidates)
                {
                    long key = ((long)c.a << 32) | (uint)c.b;
                    if (used.Contains(key)) continue;
                    if (c.len > maxLoopLen) continue;
                    edges.Add(new DungeonEdge { A = c.a, B = c.b, IsLoop = true });
                    loops++;
                    break;
                }
            }

            // Loops as the true cyclomatic number E - V + 1 (connected graph ⇒ components = 1).
            loops = edges.Count - n + 1;

            // Final deterministic edge order.
            edges.Sort((x, y) =>
            {
                int c = x.A.CompareTo(y.A);
                return c != 0 ? c : x.B.CompareTo(y.B);
            });
            return edges;
        }

        /// <summary>Prim's MST over a candidate edge list; if disconnected, stitches components by nearest pair.</summary>
        private static List<(int a, int b)> PrimMst(List<RoomWork> rooms, List<(int a, int b, double len)> candidates, out double meanLen)
        {
            int n = rooms.Count;
            var adj = new List<(int to, double len)>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<(int, double)>();
            foreach (var c in candidates)
            {
                adj[c.a].Add((c.b, c.len));
                adj[c.b].Add((c.a, c.len));
            }

            var inTree = new bool[n];
            var best = new double[n];
            var from = new int[n];
            for (int i = 0; i < n; i++) { best[i] = double.MaxValue; from[i] = -1; }

            var mst = new List<(int a, int b)>();
            double total = 0;
            best[0] = 0;
            for (int it = 0; it < n; it++)
            {
                // Pick the cheapest fringe node (lowest index breaks ties → deterministic).
                int u = -1; double bd = double.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!inTree[i] && best[i] < bd) { bd = best[i]; u = i; }

                if (u == -1)
                {
                    // Disconnected: connect the nearest not-in-tree node to the nearest in-tree node.
                    int bi = -1, bj = -1; double bdist = double.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (inTree[i]) continue;
                        for (int j = 0; j < n; j++)
                        {
                            if (!inTree[j]) continue;
                            double dx = rooms[i].Cx - rooms[j].Cx, dy = rooms[i].Cy - rooms[j].Cy;
                            double d = Math.Sqrt(dx * dx + dy * dy);
                            if (d < bdist || (Math.Abs(d - bdist) < 1e-9 && (i < bi || (i == bi && j < bj))))
                            { bdist = d; bi = i; bj = j; }
                        }
                    }
                    if (bi == -1) break;
                    best[bi] = bdist; from[bi] = bj; u = bi;
                }

                inTree[u] = true;
                if (from[u] != -1) { mst.Add((from[u], u)); total += best[u]; }

                foreach (var (to, len) in adj[u])
                    if (!inTree[to] && len < best[to]) { best[to] = len; from[to] = u; }
            }

            meanLen = mst.Count > 0 ? total / mst.Count : 0;
            return mst;
        }

        private static List<int>[] BuildAdjacency(int n, List<DungeonEdge> edges)
        {
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();
            foreach (var e in edges) { adj[e.A].Add(e.B); adj[e.B].Add(e.A); }
            for (int i = 0; i < n; i++) adj[i].Sort();
            return adj;
        }

        // --------------------------------------------------------------------
        // Stage 5 — Semantics.
        // Boss = largest area. Entrance = degree-1 room maximising graph distance
        // from boss (lowest id tiebreak). Critical path = BFS entrance→boss over
        // the room graph. Leaves → Treasure (cap 4, deepest first). 1–2 mid-depth
        // off-path Shrines. 1–2 Elite on the critical path at 55–85% depth.
        // Difficulty = 0.15 + 0.85*(Depth/maxDepth); boss forced 1.0.
        // --------------------------------------------------------------------

        private static bool AssignSemantics(List<RoomWork> rooms, List<DungeonEdge> edges, List<int>[] adj,
            out List<int> criticalRooms, out int bossId, out int entranceId)
        {
            int n = rooms.Count;
            criticalRooms = new List<int>();

            // Boss = largest area (lowest id tiebreak).
            bossId = 0;
            for (int i = 1; i < n; i++)
            {
                if (rooms[i].Area > rooms[bossId].Area ||
                    (rooms[i].Area == rooms[bossId].Area && i < bossId))
                    bossId = i;
            }

            // Distances from boss over the room graph.
            var distFromBoss = Bfs(adj, bossId);

            // Entrance = degree-1 room farthest from boss (lowest id tiebreak).
            entranceId = -1; int bestDist = -1;
            for (int i = 0; i < n; i++)
            {
                if (adj[i].Count != 1) continue;
                if (i == bossId) continue;
                int d = distFromBoss[i];
                if (d < 0) continue;
                if (d > bestDist) { bestDist = d; entranceId = i; }
            }
            if (entranceId == -1) return false; // no degree-1 room ⇒ reject, re-roll

            // Depth = graph distance from entrance.
            var depth = Bfs(adj, entranceId);
            int maxDepth = 0;
            for (int i = 0; i < n; i++)
            {
                if (depth[i] < 0) return false; // disconnected ⇒ reject
                rooms[i].Depth = depth[i];
                if (depth[i] > maxDepth) maxDepth = depth[i];
            }

            // Critical path entrance→boss (parent-pointer BFS).
            criticalRooms = ShortestPath(adj, entranceId, bossId);
            if (criticalRooms.Count == 0) return false;
            var critSet = new HashSet<int>(criticalRooms);
            // Mark critical edges.
            for (int i = 0; i + 1 < criticalRooms.Count; i++)
            {
                int a = criticalRooms[i], b = criticalRooms[i + 1];
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                foreach (var e in edges)
                    if (e.A == lo && e.B == hi) { e.IsCritical = true; break; }
            }

            // Base roles.
            for (int i = 0; i < n; i++) rooms[i].Type = RoomType.Combat;
            rooms[bossId].Type = RoomType.Boss;
            rooms[entranceId].Type = RoomType.Entrance;

            // Leaves (degree 1, excluding entrance/boss) → Treasure, deepest first, cap 4.
            var leaves = new List<int>();
            for (int i = 0; i < n; i++)
                if (adj[i].Count == 1 && i != entranceId && i != bossId) leaves.Add(i);
            leaves.Sort((a, b) =>
            {
                int c = rooms[b].Depth.CompareTo(rooms[a].Depth);
                return c != 0 ? c : a.CompareTo(b);
            });
            int treasureCap = Math.Min(4, leaves.Count);
            for (int k = 0; k < treasureCap; k++) rooms[leaves[k]].Type = RoomType.Treasure;

            // Shrines: 1–2 mid-depth (40–70%) rooms OFF the critical path, still Combat.
            var shrineCandidates = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (rooms[i].Type != RoomType.Combat) continue;
                if (critSet.Contains(i)) continue;
                double frac = maxDepth > 0 ? rooms[i].Depth / (double)maxDepth : 0;
                if (frac >= 0.40 && frac <= 0.70) shrineCandidates.Add(i);
            }
            shrineCandidates.Sort((a, b) =>
            {
                // Closest to mid (0.55) first, then id.
                double fa = maxDepth > 0 ? rooms[a].Depth / (double)maxDepth : 0;
                double fb = maxDepth > 0 ? rooms[b].Depth / (double)maxDepth : 0;
                int c = Math.Abs(fa - 0.55).CompareTo(Math.Abs(fb - 0.55));
                return c != 0 ? c : a.CompareTo(b);
            });
            int shrineCount = Math.Min(shrineCandidates.Count, shrineCandidates.Count >= 2 ? 2 : shrineCandidates.Count);
            for (int k = 0; k < shrineCount; k++) rooms[shrineCandidates[k]].Type = RoomType.Shrine;

            // Elites: 1–2 ON the critical path at 55–85% depth (still Combat, interior nodes only).
            var eliteCandidates = new List<int>();
            for (int i = 1; i + 1 < criticalRooms.Count; i++) // skip entrance (0) and boss (last)
            {
                int id = criticalRooms[i];
                if (rooms[id].Type != RoomType.Combat) continue;
                double frac = maxDepth > 0 ? rooms[id].Depth / (double)maxDepth : 0;
                if (frac >= 0.55 && frac <= 0.85) eliteCandidates.Add(id);
            }
            int eliteCount = Math.Min(2, eliteCandidates.Count);
            for (int k = 0; k < eliteCount; k++) rooms[eliteCandidates[k]].Type = RoomType.Elite;

            // Difficulty.
            for (int i = 0; i < n; i++)
                rooms[i].Difficulty = maxDepth > 0 ? 0.15 + 0.85 * (rooms[i].Depth / (double)maxDepth) : 0.15;
            rooms[bossId].Difficulty = 1.0;

            return true;
        }

        private static int[] Bfs(List<int>[] adj, int src)
        {
            int n = adj.Length;
            var dist = new int[n];
            for (int i = 0; i < n; i++) dist[i] = -1;
            var q = new Queue<int>();
            dist[src] = 0; q.Enqueue(src);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                foreach (int v in adj[u]) // adj pre-sorted ⇒ deterministic
                    if (dist[v] == -1) { dist[v] = dist[u] + 1; q.Enqueue(v); }
            }
            return dist;
        }

        private static List<int> ShortestPath(List<int>[] adj, int src, int dst)
        {
            int n = adj.Length;
            var prev = new int[n];
            for (int i = 0; i < n; i++) prev[i] = -2;
            var q = new Queue<int>();
            prev[src] = -1; q.Enqueue(src);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                if (u == dst) break;
                foreach (int v in adj[u])
                    if (prev[v] == -2) { prev[v] = u; q.Enqueue(v); }
            }
            var path = new List<int>();
            if (prev[dst] == -2) return path;
            for (int cur = dst; cur != -1; cur = prev[cur]) path.Add(cur);
            path.Reverse();
            return path;
        }

        // --------------------------------------------------------------------
        // Stages 6 + 7 — Carve corridors, stamp footprints, rasterise.
        // We stamp everything into a temporary float-offset grid, then translate
        // to the final W×H (clamped 256×256) with a margin, add walls, doorways,
        // and the entrance BFS distance field.
        // --------------------------------------------------------------------

        private static bool CarveAndRasterise(List<RoomWork> rooms, List<DungeonEdge> edges, int entranceId, int bossId,
            out byte[] grid, out int w, out int h, out List<GridCell> corridorCells, out List<GridCell> doorways,
            out short[] bfs, out short[] cellRoom, out short[] bossBfs,
            out int entranceCx, out int entranceCy, out int ox, out int oy, out string failStage)
        {
            grid = Array.Empty<byte>(); w = 0; h = 0;
            corridorCells = new List<GridCell>(); doorways = new List<GridCell>(); bfs = Array.Empty<short>();
            cellRoom = Array.Empty<short>(); bossBfs = Array.Empty<short>();
            entranceCx = 0; entranceCy = 0; ox = 0; oy = 0;
            failStage = "";

            // Bounds from room footprints + corridor extents.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var r in rooms)
            {
                minX = Math.Min(minX, r.Cx - r.W / 2.0 - 1);
                minY = Math.Min(minY, r.Cy - r.H / 2.0 - 1);
                maxX = Math.Max(maxX, r.Cx + r.W / 2.0 + 1);
                maxY = Math.Max(maxY, r.Cy + r.H / 2.0 + 1);
            }
            ox = (int)Math.Floor(minX) - Margin;
            oy = (int)Math.Floor(minY) - Margin;
            w = (int)Math.Ceiling(maxX) - ox + Margin + 1;
            h = (int)Math.Ceiling(maxY) - oy + Margin + 1;
            if (w > MaxGridDim || h > MaxGridDim) { failStage = "grid-size"; return false; }
            if (w < 3 || h < 3) { failStage = "grid-size"; return false; }

            grid = new byte[w * h];

            // Room grid-space integer centres (translated).
            var rcx = new int[rooms.Count];
            var rcy = new int[rooms.Count];
            for (int i = 0; i < rooms.Count; i++)
            {
                rcx[i] = (int)Math.Round(rooms[i].Cx) - ox;
                rcy[i] = (int)Math.Round(rooms[i].Cy) - oy;
            }

            // Track which cells belong to a room footprint (to separate corridor cells).
            var isRoomCell = new bool[w * h];
            // Per-cell owning room id, -1 for corridor/void. Filled as each footprint is stamped
            // (later rooms can't overlap earlier ones — separation guarantees disjoint footprints —
            // so first-writer-wins is moot, but we keep the stamp order deterministic regardless).
            cellRoom = new short[w * h];
            for (int i = 0; i < cellRoom.Length; i++) cellRoom[i] = -1;

            // Stamp room footprints (shape-aware).
            for (int i = 0; i < rooms.Count; i++)
                StampRoom(rooms[i], rcx[i], rcy[i], grid, isRoomCell, cellRoom, w, h);

            // Carve corridors: one L per edge, width from role.
            foreach (var e in edges)
            {
                int width = e.IsCritical ? 3 : 2;
                // Treasure spur (edge touching a treasure room) → width 1.
                if (rooms[e.A].Type == RoomType.Treasure || rooms[e.B].Type == RoomType.Treasure) width = 1;
                CarveCorridor(rcx[e.A], rcy[e.A], rcx[e.B], rcy[e.B], width, grid, isRoomCell, w, h,
                    e.A * 31 + e.B); // seeded elbow via a stable per-edge value
            }

            // Collect corridor cells (FLOOR cells not inside a room footprint).
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (grid[idx] == DungeonCell.Floor && !isRoomCell[idx])
                        corridorCells.Add(new GridCell(x, y));
                }

            // Walls: any VOID cell with an 8-neighbour FLOOR.
            var walls = new List<int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (grid[idx] != DungeonCell.Void) continue;
                    if (HasFloorNeighbour8(grid, w, h, x, y)) walls.Add(idx);
                }
            foreach (int idx in walls) grid[idx] = DungeonCell.Wall;

            // Doorways: corridor cell 4-adjacent to a room floor cell.
            foreach (var c in corridorCells)
            {
                if (IsRoomAdjacent4(isRoomCell, grid, w, h, c.X, c.Y))
                    doorways.Add(c);
            }

            // Entrance centre in grid space.
            entranceCx = rcx[entranceId];
            entranceCy = rcy[entranceId];
            // Guard: entrance centre must be floor (shape stamping guarantees it, but be safe).
            if (grid[entranceCy * w + entranceCx] != DungeonCell.Floor) { failStage = "entrance-cell"; return false; }

            // BFS distance field from the entrance centre over FLOOR cells (4-connected).
            bfs = FloorBfs(grid, w, h, entranceCx, entranceCy, out int reached, out int totalFloor);
            if (reached != totalFloor) { failStage = "floor-connectivity"; return false; }

            // Same field seeded from the BOSS room centre — Combat's leader-travel flow field descends
            // it toward the boss. The floor is fully connected (checked above), so every floor cell
            // gets a finite distance; the boss centre itself is 0.
            var bossCentre = rcx[bossId] >= 0 && rcx[bossId] < w && rcy[bossId] >= 0 && rcy[bossId] < h
                             && grid[rcy[bossId] * w + rcx[bossId]] == DungeonCell.Floor;
            if (!bossCentre) { failStage = "boss-cell"; return false; }
            bossBfs = FloorBfs(grid, w, h, rcx[bossId], rcy[bossId], out _, out _);

            return true;
        }

        /// <summary>Stamp one room's footprint (rect / ellipse / chamfered octagon) as FLOOR, and
        /// tag each stamped cell with the room's id in <paramref name="cellRoom"/>.</summary>
        private static void StampRoom(RoomWork r, int cx, int cy, byte[] grid, bool[] isRoomCell, short[] cellRoom, int w, int h)
        {
            int halfW = r.W / 2, halfH = r.H / 2;
            for (int dy = -halfH; dy <= halfH; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= h) continue;
                for (int dx = -halfW; dx <= halfW; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= w) continue;
                    bool inside;
                    switch (r.Shape)
                    {
                        case RoomShape.Ellipse:
                            double nx = halfW > 0 ? dx / (double)halfW : 0;
                            double ny = halfH > 0 ? dy / (double)halfH : 0;
                            inside = nx * nx + ny * ny <= 1.0001;
                            break;
                        case RoomShape.Octagon:
                            // Chamfer the corners: cut cells beyond a diagonal ~0.7 of the extent.
                            int chamfer = (int)Math.Round(Math.Min(halfW, halfH) * 0.5);
                            inside = !(Math.Abs(dx) + Math.Abs(dy) > halfW + halfH - chamfer);
                            break;
                        default: // Rectangle
                            inside = true;
                            break;
                    }
                    if (!inside) continue;
                    int idx = y * w + x;
                    grid[idx] = DungeonCell.Floor;
                    isRoomCell[idx] = true;
                    cellRoom[idx] = (short)r.Id;
                }
            }
            // Guarantee the centre is floor even for degenerate shapes.
            if (cx >= 0 && cx < w && cy >= 0 && cy < h)
            {
                int c = cy * w + cx;
                grid[c] = DungeonCell.Floor; isRoomCell[c] = true; cellRoom[c] = (short)r.Id;
            }
        }

        /// <summary>
        /// Carve an L-corridor between two centres, at most ONE elbow (anti-spaghetti). Elbow
        /// direction (H-then-V vs V-then-H) is chosen by the stable per-edge seed; when the spans
        /// overlap enough the run is straight. Stamps FLOOR of the given width.
        /// </summary>
        private static void CarveCorridor(int ax, int ay, int bx, int by, int width, byte[] grid, bool[] isRoomCell,
            int w, int h, int elbowSeed)
        {
            // Seeded elbow choice (deterministic per edge): even → horizontal-first.
            bool horizFirst = (elbowSeed & 1) == 0;
            int cornerX = horizFirst ? bx : ax;
            int cornerY = horizFirst ? ay : by;

            StampThickLine(ax, ay, cornerX, cornerY, width, grid, isRoomCell, w, h);
            StampThickLine(cornerX, cornerY, bx, by, width, grid, isRoomCell, w, h);
        }

        /// <summary>Stamp a horizontal or vertical run of the given width as FLOOR (does not mark room cells).</summary>
        private static void StampThickLine(int x0, int y0, int x1, int y1, int width, byte[] grid, bool[] isRoomCell,
            int w, int h)
        {
            int half = Math.Max(0, (width - 1) / 2);
            int extra = (width - 1) - half; // handle even widths symmetrically-ish
            if (x0 == x1)
            {
                int lo = Math.Min(y0, y1), hi = Math.Max(y0, y1);
                for (int y = lo; y <= hi; y++)
                    for (int t = -half; t <= extra; t++)
                        SetFloor(grid, w, h, x0 + t, y);
            }
            else if (y0 == y1)
            {
                int lo = Math.Min(x0, x1), hi = Math.Max(x0, x1);
                for (int x = lo; x <= hi; x++)
                    for (int t = -half; t <= extra; t++)
                        SetFloor(grid, w, h, x, y0 + t);
            }
            else
            {
                // Diagonal fallback (shouldn't happen for L-corridors): step axis-first.
                StampThickLine(x0, y0, x1, y0, width, grid, isRoomCell, w, h);
                StampThickLine(x1, y0, x1, y1, width, grid, isRoomCell, w, h);
            }
        }

        private static void SetFloor(byte[] grid, int w, int h, int x, int y)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            grid[y * w + x] = DungeonCell.Floor;
        }

        private static bool HasFloorNeighbour8(byte[] grid, int w, int h, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (grid[ny * w + nx] == DungeonCell.Floor) return true;
                }
            return false;
        }

        private static bool IsRoomAdjacent4(bool[] isRoomCell, byte[] grid, int w, int h, int x, int y)
        {
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                int idx = ny * w + nx;
                if (isRoomCell[idx] && grid[idx] == DungeonCell.Floor) return true;
            }
            return false;
        }

        /// <summary>4-connected BFS distance field over FLOOR cells from (sx, sy); -1 for non-floor.</summary>
        private static short[] FloorBfs(byte[] grid, int w, int h, int sx, int sy, out int reached, out int totalFloor)
        {
            var dist = new short[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            totalFloor = 0;
            for (int i = 0; i < grid.Length; i++) if (grid[i] == DungeonCell.Floor) totalFloor++;

            reached = 0;
            var q = new Queue<int>();
            int start = sy * w + sx;
            dist[start] = 0; q.Enqueue(start); reached = 1;
            int[] dxs = { 1, -1, 0, 0 };
            int[] dys = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int cx = cur % w, cy = cur / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + dxs[k], ny = cy + dys[k];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    int nidx = ny * w + nx;
                    if (grid[nidx] != DungeonCell.Floor || dist[nidx] != -1) continue;
                    dist[nidx] = (short)(dist[cur] + 1);
                    q.Enqueue(nidx);
                    reached++;
                }
            }
            return dist;
        }

        // --------------------------------------------------------------------
        // Stage 8 — Decoration (props + spawns), all deterministic, with a
        // reserved mask so no two markers (or a wall/void cell) ever collide.
        // --------------------------------------------------------------------

        private static void Decorate(DungeonParams p, Rng rng, List<RoomWork> rooms, byte[] grid, int w, int h,
            List<GridCell> doorways, int bossId, int ox, int oy, List<DungeonProp> props, List<DungeonSpawn> spawns)
        {
            // Reserved: true where a doorway/prop/spawn sits (never overlap).
            var reserved = new bool[w * h];
            foreach (var d in doorways) reserved[d.Y * w + d.X] = true;

            // Room grid centres (translated by the rasterise offset) + per-room floor cell lists.
            var rcx = new int[rooms.Count];
            var rcy = new int[rooms.Count];
            for (int i = 0; i < rooms.Count; i++)
            {
                rcx[i] = (int)Math.Round(rooms[i].Cx) - ox;
                rcy[i] = (int)Math.Round(rooms[i].Cy) - oy;
            }

            // Doorway distance (Chebyshev) field precomputed for spacing rules.
            var doorCheby = DoorChebyshev(doorways, w, h);

            bool Free(int x, int y)
                => x >= 0 && x < w && y >= 0 && y < h
                   && grid[y * w + x] == DungeonCell.Floor && !reserved[y * w + x];

            void Reserve(int x, int y) => reserved[y * w + x] = true;

            // Guaranteed centre markers go FIRST so nothing else (pillar/torch/debris) can steal the
            // cell: one Chest per treasure room, one ShrineCrystal per shrine, a PortalRing at the
            // entrance, and the boss's single Tier-2 spawn — all on the room centre.
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                switch (r.Type)
                {
                    case RoomType.Treasure when Free(rcx[i], rcy[i]):
                        props.Add(new DungeonProp { Kind = PropKind.Chest, X = rcx[i], Y = rcy[i], Rot = 0, Scale = 1, RoomId = r.Id });
                        Reserve(rcx[i], rcy[i]);
                        break;
                    case RoomType.Shrine when Free(rcx[i], rcy[i]):
                        props.Add(new DungeonProp { Kind = PropKind.ShrineCrystal, X = rcx[i], Y = rcy[i], Rot = 0, Scale = 1, RoomId = r.Id });
                        Reserve(rcx[i], rcy[i]);
                        break;
                    case RoomType.Entrance when Free(rcx[i], rcy[i]):
                        props.Add(new DungeonProp { Kind = PropKind.PortalRing, X = rcx[i], Y = rcy[i], Rot = 0, Scale = 1, RoomId = r.Id });
                        Reserve(rcx[i], rcy[i]);
                        break;
                    case RoomType.Boss when Free(rcx[i], rcy[i]):
                        spawns.Add(new DungeonSpawn { X = rcx[i], Y = rcy[i], Tier = 2, RoomId = r.Id });
                        Reserve(rcx[i], rcy[i]);
                        break;
                }
            }

            // --- Pillars: LARGE rooms only, interior lattice cells ≥2 Chebyshev from doorways. ---
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                if (Math.Max(r.W, r.H) < 13) continue; // large only
                int halfW = r.W / 2, halfH = r.H / 2;
                for (int y = rcy[i] - halfH; y <= rcy[i] + halfH; y++)
                    for (int x = rcx[i] - halfW; x <= rcx[i] + halfW; x++)
                    {
                        if (((x - rcx[i]) % 3 != 0) || ((y - rcy[i]) % 3 != 0)) continue; // 3-cell lattice
                        if (!Free(x, y)) continue;
                        if (!AllNeighbours8Floor(grid, w, h, x, y)) continue;
                        if (doorCheby[y * w + x] < 2) continue;
                        props.Add(new DungeonProp { Kind = PropKind.Pillar, X = x, Y = y, Rot = 0, Scale = 1, RoomId = r.Id });
                        Reserve(x, y);
                    }
            }

            // --- Torches: floor cells adjacent to a wall that faces floor; min Chebyshev spacing 4. ---
            var torchPlaced = new List<GridCell>();
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                int halfW = r.W / 2 + 1, halfH = r.H / 2 + 1;
                for (int y = rcy[i] - halfH; y <= rcy[i] + halfH; y++)
                    for (int x = rcx[i] - halfW; x <= rcx[i] + halfW; x++)
                    {
                        if (!Free(x, y)) continue;
                        if (!Touches4(grid, w, h, x, y, DungeonCell.Wall)) continue;
                        bool tooClose = false;
                        foreach (var t in torchPlaced)
                            if (Math.Max(Math.Abs(t.X - x), Math.Abs(t.Y - y)) < 4) { tooClose = true; break; }
                        if (tooClose) continue;
                        props.Add(new DungeonProp { Kind = PropKind.Torch, X = x, Y = y, Rot = 0, Scale = 1, RoomId = r.Id });
                        Reserve(x, y);
                        torchPlaced.Add(new GridCell(x, y));
                    }
            }

            // --- Braziers: 6–8 ringing the boss arena's inner rim. ---
            {
                var r = rooms[bossId];
                int cx = rcx[bossId], cy = rcy[bossId];
                double rimR = Math.Max(2.0, Math.Min(r.W, r.H) / 2.0 - 1.5);
                int count = Int(rng, 6, 8);
                for (int k = 0; k < count; k++)
                {
                    double ang = k * (Math.PI * 2 / count);
                    int x = (int)Math.Round(cx + Math.Cos(ang) * rimR);
                    int y = (int)Math.Round(cy + Math.Sin(ang) * rimR);
                    if (Free(x, y))
                    {
                        props.Add(new DungeonProp { Kind = PropKind.Brazier, X = x, Y = y, Rot = ang, Scale = 1, RoomId = r.Id });
                        Reserve(x, y);
                    }
                }
            }

            // --- Debris: density ∝ DecorDensity, biased toward LOW-difficulty rooms. ---
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                if (r.Type == RoomType.Boss) continue;
                // Low difficulty ⇒ more debris. Bias factor in [0.3, 1.0].
                double bias = 1.0 - 0.7 * r.Difficulty;
                var cells = RoomFloorCells(r, rcx[i], rcy[i], grid, w, h);
                foreach (var c in cells)
                {
                    if (!Free(c.X, c.Y)) continue;
                    if (!Chance(rng, p.DecorDensity * 0.12 * bias)) continue;
                    props.Add(new DungeonProp { Kind = PropKind.Debris, X = c.X, Y = c.Y, Rot = Float(rng, 0, Math.PI * 2), Scale = Float(rng, 0.7, 1.2), RoomId = r.Id });
                    Reserve(c.X, c.Y);
                }
            }

            // --- Enemy spawns (combat/elite rooms; boss already placed its single Tier-2 above). ---
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                if (r.Type == RoomType.Entrance || r.Type == RoomType.Treasure
                    || r.Type == RoomType.Shrine || r.Type == RoomType.Boss) continue;

                int tier = r.Type == RoomType.Elite ? 1 : 0;
                int count = (int)Math.Round(r.Area / 18.0 * (0.5 + r.Difficulty));
                if (count < 1) count = 1;
                var cells = RoomFloorCells(r, rcx[i], rcy[i], grid, w, h);
                // Deterministic: scan cells in order, place until count reached.
                int placed = 0;
                foreach (var c in cells)
                {
                    if (placed >= count) break;
                    if (!Free(c.X, c.Y)) continue;
                    // Prefer cells away from doorways for spawns (keep them interior).
                    if (doorCheby[c.Y * w + c.X] < 1) continue;
                    spawns.Add(new DungeonSpawn { X = c.X, Y = c.Y, Tier = tier, RoomId = r.Id });
                    Reserve(c.X, c.Y);
                    placed++;
                }
            }
        }

        private static bool AllNeighbours8Floor(byte[] grid, int w, int h, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
                    if (grid[ny * w + nx] != DungeonCell.Floor) return false;
                }
            return true;
        }

        private static bool Touches4(byte[] grid, int w, int h, int x, int y, byte cell)
        {
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (grid[ny * w + nx] == cell) return true;
            }
            return false;
        }

        /// <summary>Floor cells inside a room's bounding box, deterministic row-major order.</summary>
        private static List<GridCell> RoomFloorCells(RoomWork r, int cx, int cy, byte[] grid, int w, int h)
        {
            var list = new List<GridCell>();
            int halfW = r.W / 2, halfH = r.H / 2;
            for (int y = cy - halfH; y <= cy + halfH; y++)
                for (int x = cx - halfW; x <= cx + halfW; x++)
                {
                    if (x < 0 || x >= w || y < 0 || y >= h) continue;
                    if (grid[y * w + x] == DungeonCell.Floor) list.Add(new GridCell(x, y));
                }
            return list;
        }

        /// <summary>Chebyshev distance to the nearest doorway per cell (multi-source BFS), int.MaxValue if none.</summary>
        private static int[] DoorChebyshev(List<GridCell> doorways, int w, int h)
        {
            var dist = new int[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;
            var q = new Queue<int>();
            foreach (var d in doorways) { int idx = d.Y * w + d.X; dist[idx] = 0; q.Enqueue(idx); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int cx = cur % w, cy = cur / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        int nidx = ny * w + nx;
                        if (dist[nidx] > dist[cur] + 1) { dist[nidx] = dist[cur] + 1; q.Enqueue(nidx); }
                    }
            }
            return dist;
        }

        // --------------------------------------------------------------------
        // Stage 9 — Metadata: seeded name + FNV-1a checksum.
        // --------------------------------------------------------------------

        private static readonly string[] NameAdjectives =
            { "Ashen", "Sunken", "Forgotten", "Whispering", "Shattered", "Gilded", "Hollow", "Weeping", "Frostbound", "Bloodstained" };
        private static readonly string[] NameNouns =
            { "Vaults", "Catacombs", "Halls", "Crypt", "Depths", "Sanctum", "Labyrinth", "Warrens", "Reliquary", "Undercroft" };
        private static readonly string[] NameLords =
            { "Vor'gul", "Malakar", "the Hollow King", "Ys", "Asht", "Nyx'ra", "Grimhold", "the Weaver", "Dûl", "Serath" };

        private static string MakeName(DungeonParams p, Rng rng)
        {
            string adj = Pick(rng, NameAdjectives);
            string noun = Pick(rng, NameNouns);
            // 60% get a "of X" tail.
            if (Chance(rng, 0.6))
            {
                string lord = Pick(rng, NameLords);
                return $"The {adj} {noun} of {lord}";
            }
            return $"The {adj} {noun}";
        }

        /// <summary>
        /// FNV-1a 32 over a deterministic byte serialization: the raw grid, then each room's fields,
        /// each prop's fields, each spawn's fields — all in their (already deterministic) list order.
        /// Explicit hash (never GetHashCode) so it is stable across runtimes/platforms.
        /// </summary>
        private static uint ComputeChecksum(Dungeon d)
        {
            uint hash = 2166136261u;
            void Mix(int v)
            {
                unchecked
                {
                    hash ^= (byte)(v & 0xFF); hash *= 16777619u;
                    hash ^= (byte)((v >> 8) & 0xFF); hash *= 16777619u;
                    hash ^= (byte)((v >> 16) & 0xFF); hash *= 16777619u;
                    hash ^= (byte)((v >> 24) & 0xFF); hash *= 16777619u;
                }
            }

            Mix(d.W); Mix(d.H);
            for (int i = 0; i < d.Grid.Length; i++) { unchecked { hash ^= d.Grid[i]; hash *= 16777619u; } }
            foreach (var r in d.Rooms)
            {
                Mix(r.Id); Mix(r.Cx); Mix(r.Cy); Mix(r.W); Mix(r.H);
                Mix((int)r.Shape); Mix((int)r.Type); Mix(r.Depth); Mix(r.Degree);
                Mix((int)Math.Round(r.Difficulty * 1000));
            }
            foreach (var pr in d.Props)
            {
                Mix((int)pr.Kind); Mix(pr.X); Mix(pr.Y); Mix(pr.RoomId);
                Mix((int)Math.Round(pr.Rot * 1000)); Mix((int)Math.Round(pr.Scale * 1000));
            }
            foreach (var sp in d.Spawns) { Mix(sp.X); Mix(sp.Y); Mix(sp.Tier); Mix(sp.RoomId); }
            return hash;
        }
    }
}
