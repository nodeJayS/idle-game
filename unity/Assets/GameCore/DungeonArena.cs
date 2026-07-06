#nullable enable
using System;

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
    ///  - ROOM GATE (<see cref="GateTargets"/>): the anti-wallhack rule — two units may only SEE each
    ///    other when in the same room, or (if either is in a corridor) within corridor sight range.
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

        /// <summary>Wrap a dungeon. <paramref name="corridorSight"/> is the euclidean range at which a unit
        /// in a corridor may see across a doorway (Balance.DungeonCorridorSight); defaults to 6.5 so a
        /// hand-built test arena needn't thread config.</summary>
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
        /// The anti-wallhack gate: true when <paramref name="seeker"/> and <paramref name="candidate"/> MAY
        /// see each other — they're in the SAME room (equal room ids, both ≥ 0), OR (when either stands in a
        /// corridor, room id -1) they're within <see cref="BalanceConstants.DungeonCorridorSight"/> euclidean
        /// distance. Two units in DIFFERENT rooms can never see each other (a wall between them), which is
        /// what stops targeting through walls. Symmetric in the two arguments.
        /// </summary>
        public bool GateTargets(Vec2 seeker, Vec2 candidate)
        {
            int ra = RoomAt(seeker), rb = RoomAt(candidate);
            if (ra >= 0 && ra == rb) return true;                 // same room: always visible
            if (ra >= 0 && rb >= 0) return false;                 // two different rooms: a wall between
            // At least one is in a corridor — gate on proximity so a mob only "sees" down the hall a bit.
            return Vec2.Distance(seeker, candidate) <= _corridorSight;
        }

        /// <summary>
        /// The centre of the 4-neighbour FLOOR cell with the LOWEST <see cref="Dungeon.BossBfs"/> that is
        /// STRICTLY lower than the current cell's — the flow-field step toward the boss. Fixed neighbour
        /// scan order (+x, -x, +y, -y) breaks ties. When no neighbour is strictly lower (at or one cell
        /// from the boss, or off the floor), returns <paramref name="from"/>'s own cell centre so the
        /// caller simply holds. Deterministic.
        /// </summary>
        public Vec2 DownhillStep(Vec2 from)
        {
            int cx = (int)Math.Floor(from.X), cy = (int)Math.Floor(from.Y);
            var here = CellCentreOf(from.X, from.Y);
            if (cx < 0 || cx >= _w || cy < 0 || cy >= _h) return here;
            short cur = _d.BossBfs[cy * _w + cx];
            if (cur < 0) return here; // not on the flow field

            int[] dxs = { 1, -1, 0, 0 };
            int[] dys = { 0, 0, 1, -1 };
            short bestVal = cur;
            int bx = cx, by = cy;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dxs[k], ny = cy + dys[k];
                if (nx < 0 || nx >= _w || ny < 0 || ny >= _h) continue;
                short v = _d.BossBfs[ny * _w + nx];
                if (v < 0) continue;          // non-floor
                if (v < bestVal) { bestVal = v; bx = nx; by = ny; } // strictly lower; fixed order = tie-break
            }
            if (bx == cx && by == cy) return here; // nothing lower — at/near the boss
            return new Vec2(bx + 0.5, by + 0.5);
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
