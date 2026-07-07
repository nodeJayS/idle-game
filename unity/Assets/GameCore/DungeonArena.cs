#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Wraps a generated <see cref="Dungeon"/> as an <see cref="IArenaSurface"/> plus the
    /// dungeon-specific queries Combat needs (M8 slice 3a). Pure and deterministic — it only READS
    /// the immutable dungeon data (grid, per-cell room ids, the boss BFS flow field); it never mutates
    /// the dungeon and holds no rng.
    ///
    /// World mapping: grid cell (x,y) spans world [x,x+1)×[y,y+1); its centre is (x+0.5, y+0.5) — the
    /// exact mapping the client renders tiles at, so sim positions and rendered tiles line up.
    ///
    /// The three roles it plays:
    ///  - WALKABLE SURFACE: <see cref="Contains"/> insets walls/corners (WalkInset) so a 0.45-body hero
    ///    never buries into a wall, while width-1 corridor centres still pass; <see cref="Clamp"/> pushes
    ///    an off-surface point back to the nearest walkable cell centre by a deterministic ring search.
    ///  - ROOM GATE (<see cref="GateTargets"/>): room-scoped sight — two units may only SEE each other
    ///    inside the SAME room, or both in the hallway within corridor sight range with a clear line;
    ///    never across a room boundary (not even an open doorway).
    ///  - FLOW FIELD (<see cref="DownhillStep"/>): the leader descends the boss BFS toward the exit,
    ///    routing through corridors without any per-step pathfinding.
    /// </summary>
    public sealed class DungeonArena : IArenaSurface
    {
        /// <summary>Half a body-width of wall inset: a candidate point must clear a wall by this much
        /// on each axis to be walkable. 0.35 &lt; 0.5 so a width-1 corridor CENTRE (0.5 from each wall)
        /// still passes, while a point hugging a wall face fails.</summary>
        public const double WalkInset = 0.35;

        private readonly Dungeon _d;
        private readonly int _w, _h;
        private readonly double _corridorSight;

        // Per-room objective flow fields (sweep routing): roomId -> BFS distance over FLOOR from that
        // room's centre cell, -1 for non-floor. Computed on demand and cached; deterministic (a pure
        // function of the immutable grid), so caching never changes results.
        private readonly Dictionary<int, short[]> _roomFields = new Dictionary<int, short[]>();
        // Per-seed-cell flow fields (sweep travel onto a pack): cell index -> BFS field from that cell.
        // Same deterministic pure-of-grid caching as _roomFields.
        private readonly Dictionary<int, short[]> _cellFields = new Dictionary<int, short[]>();

        /// <summary>Wrap a dungeon. <paramref name="corridorSight"/> is the euclidean range at which two
        /// units in the hallway may see each other down it (Balance.DungeonCorridorSight); defaults to
        /// 6.5 so a hand-built test arena needn't thread config.</summary>
        public DungeonArena(Dungeon d, double corridorSight = 6.5)
        {
            _d = d ?? throw new ArgumentNullException(nameof(d));
            _w = d.W;
            _h = d.H;
            _corridorSight = corridorSight;
        }

        /// <summary>The wrapped dungeon (client rendering / tests read the raw data through this).</summary>
        public Dungeon Dungeon => _d;

        /// <summary>Floor-cell test for a WORLD point: the cell it falls in is FLOOR. Out-of-bounds ⇒ false.</summary>
        private bool CellIsFloor(double wx, double wy)
        {
            int cx = (int)Math.Floor(wx), cy = (int)Math.Floor(wy);
            if (cx < 0 || cx >= _w || cy < 0 || cy >= _h) return false;
            return _d.Grid[cy * _w + cx] == DungeonCell.Floor;
        }

        /// <summary>
        /// True when <paramref name="p"/>'s cell is FLOOR AND the four inset probe points p±(WalkInset,0),
        /// p±(0,WalkInset) also fall in FLOOR cells — this pulls the walkable boundary a body's-half inside
        /// walls/corners so a hero never visually clips into a wall. A width-1 corridor centre still passes
        /// (0.5 ≥ WalkInset on each side).
        /// </summary>
        public bool Contains(Vec2 p)
        {
            if (!CellIsFloor(p.X, p.Y)) return false;
            return CellIsFloor(p.X + WalkInset, p.Y) && CellIsFloor(p.X - WalkInset, p.Y)
                && CellIsFloor(p.X, p.Y + WalkInset) && CellIsFloor(p.X, p.Y - WalkInset);
        }

        /// <summary>Centre of the cell a WORLD point falls in.</summary>
        private static Vec2 CellCentreOf(double wx, double wy)
            => new Vec2(Math.Floor(wx) + 0.5, Math.Floor(wy) + 0.5);

        /// <summary>
        /// <paramref name="p"/> when contained, else a deterministic outward ring search (Chebyshev rings
        /// r=1..8) over the cells around p's cell for the nearest cell whose CENTRE passes <see cref="Contains"/>,
        /// tie-broken by (ring, then a fixed row-major scan order). Returns that cell centre. Fallback when
        /// nothing within 8 rings qualifies: the entrance room centre — a guaranteed-walkable anchor.
        /// </summary>
        public Vec2 Clamp(Vec2 p)
        {
            if (Contains(p)) return p;
            int cx = (int)Math.Floor(p.X), cy = (int)Math.Floor(p.Y);
            for (int ring = 1; ring <= 8; ring++)
            {
                // Fixed scan order: row-major over the ring's bounding square, only the border cells.
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue; // border of this ring only
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || nx >= _w || ny < 0 || ny >= _h) continue;
                        var c = new Vec2(nx + 0.5, ny + 0.5);
                        if (Contains(c)) return c;
                    }
            }
            return EntranceCentre();
        }

        /// <summary>Dungeons are flat this slice — every cell is tier 0.</summary>
        public int TierAt(Vec2 p) => 0;

        /// <summary>The owning room id at <paramref name="p"/>'s cell, or -1 for a corridor/void cell.</summary>
        public int RoomAt(Vec2 p)
        {
            int cx = (int)Math.Floor(p.X), cy = (int)Math.Floor(p.Y);
            if (cx < 0 || cx >= _w || cy < 0 || cy >= _h) return -1;
            return _d.CellRoom[cy * _w + cx];
        }

        /// <summary>
        /// The anti-wallhack gate — sight is ROOM-SCOPED (user rule 2026-07-06): a unit sees exactly as far
        /// as its own area's walls. True only when both points are in the SAME room (equal ids, ≥ 0), or
        /// both are in HALLWAY cells (room id -1) within <see cref="BalanceConstants.DungeonCorridorSight"/>
        /// euclidean range with a walkable straight line between them (sight runs down the hall, never
        /// through a wall). Room ↔ hallway is ALWAYS false — even across an open doorway — so packs never
        /// bleed out of their room at heroes passing in the hall, and heroes never pre-aggro the next room
        /// before stepping through the door. Symmetric in the two arguments.
        /// </summary>
        public bool GateTargets(Vec2 seeker, Vec2 candidate)
        {
            int ra = RoomAt(seeker), rb = RoomAt(candidate);
            if (ra != rb) return false;   // different areas: room vs other room, or room vs hallway
            if (ra >= 0) return true;     // same room: visible up to the walls
            // Both in hallway cells: proximity + a walkable line of sight down the hall.
            return Vec2.Distance(seeker, candidate) <= _corridorSight && SegmentWalkable(seeker, candidate);
        }

        /// <summary>
        /// True when the straight segment a→b stays on the walkable surface: sample points every
        /// ~0.3 tiles (plus the endpoints) all pass <see cref="Contains"/>. This is the shared
        /// "line of walkability" behind corridor sight, dash flight paths, and the string-pulled
        /// travel step. Deterministic pure geometry.
        /// </summary>
        public bool SegmentWalkable(Vec2 a, Vec2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return Contains(a);
            int samples = (int)Math.Ceiling(len / 0.3);
            for (int i = 0; i <= samples; i++)
            {
                double t = (double)i / samples;
                if (!Contains(new Vec2(a.X + dx * t, a.Y + dy * t))) return false;
            }
            return true;
        }

        /// <summary>
        /// The centre of the 4-neighbour FLOOR cell with the LOWEST <see cref="Dungeon.BossBfs"/> that is
        /// STRICTLY lower than the current cell's — the flow-field step toward the boss. Fixed neighbour
        /// scan order (+x, -x, +y, -y) breaks ties. When no neighbour is strictly lower (at or one cell
        /// from the boss, or off the floor), returns <paramref name="from"/>'s own cell centre so the
        /// caller simply holds. Deterministic.
        /// </summary>
        public Vec2 DownhillStep(Vec2 from) => SmoothedStep(from, _d.BossBfs);

        /// <summary>
        /// The flow-field step toward the CENTRE of <paramref name="roomId"/> — the room-sweep router.
        /// Same downhill rule as the boss overload, but against <see cref="FieldFor"/>(roomId) so the
        /// leader is drawn to the current objective room rather than always the boss. Deterministic.
        /// </summary>
        public Vec2 DownhillStep(Vec2 from, int roomId) => SmoothedStep(from, FieldFor(roomId));

        /// <summary>How far ahead the string-pulled step may cut along the downhill chain.</summary>
        private const int LookAheadCells = 8;
        // Scratch for the lookahead chain (the sim is single-threaded; avoids per-step allocation).
        private readonly double[] _chainX = new double[LookAheadCells];
        private readonly double[] _chainY = new double[LookAheadCells];

        /// <summary>
        /// STRING-PULLED flow step: walk the downhill chain up to <see cref="LookAheadCells"/> cells
        /// ahead and return the FARTHEST chain point reachable from <paramref name="from"/> in a
        /// straight walkable line — so travel cuts smooth diagonals through corridors instead of
        /// zigzagging cell-centre to cell-centre into walls (user-caught: "runs into walls a lot").
        /// Falls back to the plain one-cell downhill step when no lookahead point has a clear line.
        /// Deterministic — the chain and the segment test are pure functions of the grid.
        /// </summary>
        private Vec2 SmoothedStep(Vec2 from, short[] field)
        {
            var first = RawDownhillStep(from, field);
            // Collect the downhill chain (cell centres) beyond the first step.
            int count = 0;
            var cur = first;
            _chainX[count] = first.X; _chainY[count] = first.Y; count++;
            while (count < LookAheadCells)
            {
                var next = RawDownhillStep(cur, field);
                if (next.X == cur.X && next.Y == cur.Y) break; // reached the seed
                _chainX[count] = next.X; _chainY[count] = next.Y; count++;
                cur = next;
            }
            // Farthest-first: the longest straight cut wins.
            for (int i = count - 1; i >= 1; i--)
            {
                var p = new Vec2(_chainX[i], _chainY[i]);
                if (SegmentWalkable(from, p)) return p;
            }
            return first;
        }

        /// <summary>
        /// The centre of the 4-neighbour FLOOR cell with the LOWEST <paramref name="field"/> value that is
        /// STRICTLY lower than the current cell's — the raw single-cell flow step (the string-pulled
        /// <see cref="SmoothedStep"/> chains + shortcuts these). Fixed neighbour scan order (+x, -x, +y, -y)
        /// breaks ties. When no neighbour is strictly lower (at or one cell from the seed, or off the
        /// floor), returns <paramref name="from"/>'s own cell centre so the caller simply holds.
        /// </summary>
        private Vec2 RawDownhillStep(Vec2 from, short[] field)
        {
            int cx = (int)Math.Floor(from.X), cy = (int)Math.Floor(from.Y);
            var here = CellCentreOf(from.X, from.Y);
            if (cx < 0 || cx >= _w || cy < 0 || cy >= _h) return here;
            short cur = field[cy * _w + cx];
            if (cur < 0) return here; // not on the flow field

            int[] dxs = { 1, -1, 0, 0 };
            int[] dys = { 0, 0, 1, -1 };
            short bestVal = cur;
            int bx = cx, by = cy;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dxs[k], ny = cy + dys[k];
                if (nx < 0 || nx >= _w || ny < 0 || ny >= _h) continue;
                short v = field[ny * _w + nx];
                if (v < 0) continue;          // non-floor
                if (v < bestVal) { bestVal = v; bx = nx; by = ny; } // strictly lower; fixed order = tie-break
            }
            if (bx == cx && by == cy) return here; // nothing lower — at/near the seed
            return new Vec2(bx + 0.5, by + 0.5);
        }

        /// <summary>
        /// The 4-connected BFS distance field over FLOOR cells seeded from <paramref name="roomId"/>'s
        /// centre cell (-1 for non-floor). Computed on demand and CACHED (a pure function of the immutable
        /// grid, so the cache never affects results). Used by the room-sweep router: the leader descends
        /// the field of the current objective room. An unknown room id, or a room whose centre cell isn't
        /// floor, yields an all-(-1) field (no reachable step — the caller holds).
        /// </summary>
        public short[] FieldFor(int roomId)
        {
            if (_roomFields.TryGetValue(roomId, out var cached)) return cached;

            int cx = -1, cy = -1;
            foreach (var r in _d.Rooms)
                if (r.Id == roomId) { cx = r.Cx; cy = r.Cy; break; }

            var field = BfsFieldFromCell(cx, cy);
            _roomFields[roomId] = field;
            return field;
        }

        /// <summary>
        /// The centre of the 4-neighbour FLOOR cell that steps toward the CELL <paramref name="cellX"/>,
        /// <paramref name="cellY"/> — the flow-field step toward a specific cell (used to route the sweep
        /// leader onto a pack that spawned on corridor-tagged cells, where the room-centre field would
        /// leave the leader stranded at the seed). Cached per cell index; deterministic. Off-floor seed ⇒
        /// hold at <paramref name="from"/>.
        /// </summary>
        public Vec2 DownhillStepToCell(Vec2 from, int cellX, int cellY)
        {
            int key = cellY * _w + cellX;
            if (!_cellFields.TryGetValue(key, out var field))
            {
                field = BfsFieldFromCell(cellX, cellY);
                _cellFields[key] = field;
            }
            return SmoothedStep(from, field);
        }

        /// <summary>4-connected BFS distance field over FLOOR seeded from cell (cx,cy) (-1 for non-floor
        /// or an off-floor/out-of-bounds seed). The shared field builder behind the room + cell caches.</summary>
        private short[] BfsFieldFromCell(int cx, int cy)
        {
            var field = new short[_w * _h];
            for (int i = 0; i < field.Length; i++) field[i] = -1;
            if (cx < 0 || cx >= _w || cy < 0 || cy >= _h || _d.Grid[cy * _w + cx] != DungeonCell.Floor)
                return field;

            var q = new Queue<int>();
            int start = cy * _w + cx;
            field[start] = 0;
            q.Enqueue(start);
            int[] dxs = { 1, -1, 0, 0 };
            int[] dys = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                int idx = q.Dequeue();
                int x = idx % _w, y = idx / _w;
                short nd = (short)(field[idx] + 1);
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dxs[k], ny = y + dys[k];
                    if (nx < 0 || nx >= _w || ny < 0 || ny >= _h) continue;
                    int ni = ny * _w + nx;
                    if (_d.Grid[ni] != DungeonCell.Floor || field[ni] >= 0) continue;
                    field[ni] = nd;
                    q.Enqueue(ni);
                }
            }
            return field;
        }

        /// <summary>The entrance-BFS distance at a room's CENTRE cell — how "deep" the room is from the
        /// entrance along the floor. Drives objective ordering (sweep the shallowest living room first).
        /// A room whose centre isn't floor (never for a valid dungeon) reads short.MaxValue = last.</summary>
        public short RoomEntranceDepth(int roomId)
        {
            foreach (var r in _d.Rooms)
                if (r.Id == roomId)
                {
                    if (r.Cx < 0 || r.Cx >= _w || r.Cy < 0 || r.Cy >= _h) return short.MaxValue;
                    short v = _d.Bfs[r.Cy * _w + r.Cx];
                    return v < 0 ? short.MaxValue : v;
                }
            return short.MaxValue;
        }

        /// <summary>
        /// The nearest walkable cell centre INSIDE room <paramref name="roomId"/> — the sealed-door
        /// containment clamp (§7.3): a party hero (or the room's own mob) that crossed the doorway
        /// mid-fight is placed back just inside it. Same deterministic ring search as
        /// <see cref="Clamp"/> but the candidate must ALSO sit in the room. <paramref name="p"/> is
        /// returned unchanged when it already qualifies. Ring cap 16 (rooms are ≤15 wide + a short
        /// hall); the room's centre cell is the guaranteed fallback.
        /// </summary>
        public Vec2 ClampToRoom(Vec2 p, int roomId)
        {
            if (RoomAt(p) == roomId && Contains(p)) return p;
            var found = RingSearch(p, c => RoomAt(c) == roomId, 16);
            if (found != null) return found.Value;
            foreach (var r in _d.Rooms)
                if (r.Id == roomId) return new Vec2(r.Cx + 0.5, r.Cy + 0.5);
            return Clamp(p);
        }

        /// <summary>
        /// The nearest walkable cell centre OUTSIDE room <paramref name="roomId"/> — the locked
        /// boss-door gate (§7.3): a hero stepping into the boss room without the Boss Key is placed
        /// back at the threshold. <paramref name="p"/> returns unchanged when already outside.
        /// </summary>
        public Vec2 ClampOutsideRoom(Vec2 p, int roomId)
        {
            if (RoomAt(p) != roomId && Contains(p)) return p;
            var found = RingSearch(p, c => RoomAt(c) != roomId, 16);
            return found ?? EntranceCentre();
        }

        /// <summary>Deterministic Chebyshev ring search (the <see cref="Clamp"/> scan) for the first
        /// walkable cell centre that also passes <paramref name="ok"/>; null when none within
        /// <paramref name="maxRing"/> rings. Ring 0 checks p's own cell centre.</summary>
        private Vec2? RingSearch(Vec2 p, Func<Vec2, bool> ok, int maxRing)
        {
            int cx = (int)Math.Floor(p.X), cy = (int)Math.Floor(p.Y);
            for (int ring = 0; ring <= maxRing; ring++)
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || nx >= _w || ny < 0 || ny >= _h) continue;
                        var c = new Vec2(nx + 0.5, ny + 0.5);
                        if (Contains(c) && ok(c)) return c;
                    }
            return null;
        }

        /// <summary>The entrance room's centre-cell world point — the Clamp fallback and party spawn anchor.
        /// Falls back to the grid centre if no entrance room is tagged (never happens for a valid dungeon).</summary>
        public Vec2 EntranceCentre()
        {
            foreach (var r in _d.Rooms)
                if (r.Type == RoomType.Entrance) return new Vec2(r.Cx + 0.5, r.Cy + 0.5);
            return new Vec2(_w / 2 + 0.5, _h / 2 + 0.5);
        }
    }
}
