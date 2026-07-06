#nullable enable
using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The terraced arena terrain (ROADMAP 8 slice 2 — client render of the slice-1 <see cref="ArenaLayout"/>
    /// data). Where a zone names an arena, this replaces the flat legacy Perlin plane with a faceted,
    /// flat-shaded heightfield: walkable cells lift to their sim tier (world Y = Tier ×
    /// Balance.TerrainTierHeight), disc rims hug the shoreline, terrace steps read as low stairs, and the
    /// void beyond the shore drops to a matte water plane. Pure renderer-side: it only READS the layout
    /// geometry the sim authored — no combat rule reads height (high-ground mechanics are deferred).
    ///
    /// Look rules (match Ground/Scenery): NO textures beyond the shared TunicSurface detail; flat vertex
    /// colours are brightness mottle (so <see cref="Ground.SetZone"/> retints still read on any hue); the
    /// TunicSurface slope blend paints vertical walls _SideColor automatically (a wall's world normal.y ≈ 0
    /// falls below _SlopeLo 0.45 ⇒ the shader lerps to the side/dirt colour). Shared material with the
    /// legacy plane so a zone retint hits both.
    /// </summary>
    public static class ArenaTerrain
    {
        private const float CellSize   = 1.0f;   // heightfield cell (spec: 1.0)
        private const float Margin     = 3f;     // grid extends this far past the shape bounds
        private const float BedY       = -1.2f;  // void walls drop to this (below the water line)
        private const float WaterY     = -0.45f; // flat water plane height
        private const float WaterPad   = 60f;    // water spans the legacy map rect + this margin
        private const float JitterAmp  = 0.12f;  // per-corner facet jitter (±), hash-based (NOT Random)
        private const float ShoreInk   = 0.78f;  // multiply top-face rgb within ~1.5 tiles of the shore
        private const float ShoreBand  = 1.5f;   // shore-ink reach (tiles)

        private static readonly Color WaterColor = new Color(0.16f, 0.30f, 0.38f); // deep blue-green (neutral; per-zone flavor = slice 3)

        private static GameObject? _terrain; // the terraced arena mesh (null when the legacy plane owns the field)
        private static GameObject? _water;    // the matte water plane under the arena

        /// <summary>Rebuild the world floor for a zone. <paramref name="arena"/> null ⇒ tear down any
        /// arena mesh + water and show the legacy Perlin plane (today's open-field look). Non-null ⇒ hide
        /// the plane and build the terraced heightfield + water. Called from the ZoneDress hook on a zone
        /// change (rare, ~every 10 stages), so a full rebuild is fine.</summary>
        public static void Rebuild(GameConfig cfg, ArenaLayout? arena)
        {
            if (_terrain != null) { Object.Destroy(_terrain); _terrain = null; }
            if (_water != null) { Object.Destroy(_water); _water = null; }

            if (arena == null)
            {
                Ground.SetVisible(true); // legacy open plane owns the field
                return;
            }

            Ground.SetVisible(false); // the terraced mesh z-covers the field; hide the flat plane
            BuildWater(cfg);
            BuildTerrain(cfg, arena);
        }

        /// <summary>Sim-authored height at a world point: 0 on the open plane, else the tier under the
        /// point × the tier height. Entities and ground FX ride this so they sit on the terraces.
        /// A point in the void (off the union) reads tier 0 — units never stand there anyway.</summary>
        public static float HeightAt(GameConfig cfg, ArenaLayout? arena, double x, double z)
        {
            if (arena == null) return 0f;
            return arena.TierAt(new Vec2(x, z)) * (float)cfg.Balance.TerrainTierHeight;
        }

        // ---- water ----------------------------------------------------------

        private static void BuildWater(GameConfig cfg)
        {
            float halfX = (float)cfg.Balance.MapHalfWidth + WaterPad;
            float halfZ = (float)cfg.Balance.MapHalfDepth + WaterPad;
            var mesh = new Mesh { name = "ArenaWater" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-halfX, WaterY, -halfZ), new Vector3(-halfX, WaterY, halfZ),
                new Vector3( halfX, WaterY,  halfZ), new Vector3( halfX, WaterY, -halfZ),
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _water = new GameObject("ArenaWater");
            _water.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = _water.AddComponent<MeshRenderer>();
            mr.sharedMaterial = WaterMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // flat water: never casts
        }

        /// <summary>A deep blue-green matte URP/Lit plane (Bootstrap.MakeMatte pattern) — NOT the
        /// TunicSurface material, so it gets no grass detail texture. Neutral colour this slice.</summary>
        private static Material WaterMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = WaterColor };
            Bootstrap.MakeMatte(m);
            return m;
        }

        // ---- terrain heightfield --------------------------------------------

        // Working buffers reused per rebuild (cleared at BuildTerrain entry).
        private static readonly List<Vector3> _verts = new List<Vector3>();
        private static readonly List<Color> _cols = new List<Color>();
        private static readonly List<int> _tris = new List<int>();

        private static void BuildTerrain(GameConfig cfg, ArenaLayout arena)
        {
            _verts.Clear(); _cols.Clear(); _tris.Clear();

            float tierH = (float)cfg.Balance.TerrainTierHeight;

            // Grid over the shape bounds + margin.
            GetBounds(arena, out float minX, out float minZ, out float maxX, out float maxZ);
            minX -= Margin; minZ -= Margin; maxX += Margin; maxZ += Margin;
            _cMinX = minX; _cMinZ = minZ; // stash the grid origin for DirToCell (ramp inset)
            int nx = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / CellSize));
            int nz = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / CellSize));

            // Per grid-corner data (indices [0..nx] × [0..nz]). Computed once so every cell touching a
            // corner shares the SAME pulled position + walkable flag (no cracks between adjacent cells).
            int cw = nx + 1, ch = nz + 1;
            var cornerX = new float[cw * ch];   // world X after shore-pull
            var cornerZ = new float[cw * ch];   // world Z after shore-pull
            var walk = new bool[cw * ch];       // corner inside the walkable union
            var nearShore = new bool[cw * ch];  // corner is on the void/shore boundary (no jitter there)

            for (int j = 0; j < ch; j++)
                for (int i = 0; i < cw; i++)
                {
                    int ci = j * cw + i;
                    float wx = minX + i * CellSize, wz = minZ + j * CellSize;
                    var p = new Vec2(wx, wz);
                    bool inside = arena.Contains(p);
                    walk[ci] = inside;
                    if (inside) { cornerX[ci] = wx; cornerZ[ci] = wz; }
                    else
                    {
                        // Void corner: pull to the nearest boundary point (Clamp) so disc rims curve
                        // instead of stair-stepping. Deterministic function of the corner coordinate.
                        var q = arena.Clamp(p);
                        cornerX[ci] = (float)q.X; cornerZ[ci] = (float)q.Y;
                    }
                }

            // Mark shore corners: a walkable corner touching any void corner (4-neighbourhood), or any
            // void corner itself — these get no jitter so walls meet tops exactly.
            for (int j = 0; j < ch; j++)
                for (int i = 0; i < cw; i++)
                {
                    int ci = j * cw + i;
                    if (!walk[ci]) { nearShore[ci] = true; continue; }
                    bool touchesVoid =
                        (i > 0 && !walk[ci - 1]) || (i < cw - 1 && !walk[ci + 1]) ||
                        (j > 0 && !walk[ci - cw]) || (j < ch - 1 && !walk[ci + cw]);
                    if (touchesVoid) nearShore[ci] = true;
                }

            // Per-cell top height (from the CELL CENTRE's tier) and whether the cell emits a top.
            var cellTop = new float[nx * nz];   // world Y of the cell's top (valid only where cellShow)
            var cellTier = new int[nx * nz];
            var cellShow = new bool[nx * nz];   // any walkable corner ⇒ emit a top

            for (int cz = 0; cz < nz; cz++)
                for (int cx = 0; cx < nx; cx++)
                {
                    int ci = cz * nx + cx;
                    int c00 = cz * cw + cx, c10 = c00 + 1, c01 = c00 + cw, c11 = c01 + 1;
                    bool anyWalk = walk[c00] || walk[c10] || walk[c01] || walk[c11];
                    if (!anyWalk) { cellShow[ci] = false; continue; }  // fully outside ⇒ water shows through

                    float centreX = minX + (cx + 0.5f) * CellSize;
                    float centreZ = minZ + (cz + 0.5f) * CellSize;
                    var centre = new Vec2(centreX, centreZ);
                    bool centreIn = arena.Contains(centre);
                    // Cells whose CENTRE is void but that touch walkable corners: skip — the neighbours'
                    // pulled corners close the rim.
                    if (!centreIn) { cellShow[ci] = false; continue; }
                    int tier = arena.TierAt(centre);
                    cellTier[ci] = tier;
                    cellTop[ci] = tier * tierH;
                    cellShow[ci] = true;
                }

            // Tier-boundary corners get NO jitter too (a corner shared by cells of different tiers): the
            // wall meets the top at the exact tier height, so a jittered top edge would crack away from
            // the sheer wall. A corner is a tier boundary when the up-to-4 cells around it (the 4 cells
            // whose grid corner this is) don't all share one shown tier.
            for (int j = 0; j < ch; j++)
                for (int i = 0; i < cw; i++)
                {
                    int ci = j * cw + i;
                    if (nearShore[ci]) continue; // already no-jitter
                    int tRef = int.MinValue; bool mixed = false;
                    // The 4 cells touching corner (i,j): (i-1,j-1),(i,j-1),(i-1,j),(i,j) in cell space.
                    for (int dz = -1; dz <= 0 && !mixed; dz++)
                        for (int dx = -1; dx <= 0; dx++)
                        {
                            int cxk = i + dx, czk = j + dz;
                            if (cxk < 0 || czk < 0 || cxk >= nx || czk >= nz) continue;
                            int cik = czk * nx + cxk;
                            if (!cellShow[cik]) continue;
                            int t = cellTier[cik];
                            if (tRef == int.MinValue) tRef = t;
                            else if (t != tRef) { mixed = true; break; }
                        }
                    if (mixed) nearShore[ci] = true;
                }

            // Emit tops (with facet jitter + shore ink) and walls between differing neighbours.
            for (int cz = 0; cz < nz; cz++)
                for (int cx = 0; cx < nx; cx++)
                {
                    int ci = cz * nx + cx;
                    if (!cellShow[ci]) continue;
                    int c00 = cz * cw + cx, c10 = c00 + 1, c01 = c00 + cw, c11 = c01 + 1;
                    float top = cellTop[ci];

                    // Shore-ink darkening for tops near the void (a painted shore band).
                    bool inkCell = CellNearShore(cx, cz, nx, nz, cellShow);

                    Vector3 p00 = CornerTop(cornerX[c00], cornerZ[c00], top, nearShore[c00]);
                    Vector3 p10 = CornerTop(cornerX[c10], cornerZ[c10], top, nearShore[c10]);
                    Vector3 p01 = CornerTop(cornerX[c01], cornerZ[c01], top, nearShore[c01]);
                    Vector3 p11 = CornerTop(cornerX[c11], cornerZ[c11], top, nearShore[c11]);
                    // Two upward tris (own verts = flat facets), like Ground.AddTri.
                    AddTop(p00, p01, p11, inkCell);
                    AddTop(p00, p11, p10, inkCell);

                    // Walls to the +X and +Z neighbours (each shared edge handled once). A wall drops
                    // from the higher top to the lower; a void neighbour drops to BedY.
                    EmitWall(cx, cz, cx + 1, cz, nx, nz, ci, cellShow, cellTop, cellTier,
                             cornerX, cornerZ, c10, c11, top, horizontal: false);
                    EmitWall(cx, cz, cx, cz + 1, nx, nz, ci, cellShow, cellTop, cellTier,
                             cornerX, cornerZ, c01, c11, top, horizontal: true);
                }

            var mesh = new Mesh { name = "ArenaTerrain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(_verts);
            mesh.SetColors(_cols);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _terrain = new GameObject("ArenaTerrain");
            _terrain.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = _terrain.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Ground.SharedMaterial(); // shared with the plane so SetZone retints both
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // matches the ground plane (receives only)
        }

        /// <summary>A wall between cell (ax,az) and its (bx,bz) neighbour when their tops differ, wound
        /// outward-facing. Two WALKABLE cells one tier apart become TWO half-steps (a mid ledge inset
        /// half a cell toward the higher side) so a terrace reads as stairs; walkable→void or a ≥2-tier
        /// gap stays one sheer face. Shares the two edge corners (ea, eb) so tops/walls never crack.</summary>
        private static void EmitWall(int ax, int az, int bx, int bz, int nx, int nz, int ai,
                                     bool[] cellShow, float[] cellTop, int[] cellTier,
                                     float[] cornerX, float[] cornerZ, int ea, int eb,
                                     float topA, bool horizontal)
        {
            // Edge geometry (world XZ of the two shared corners, after shore-pull).
            Vector3 e0 = new Vector3(cornerX[ea], 0f, cornerZ[ea]);
            Vector3 e1 = new Vector3(cornerX[eb], 0f, cornerZ[eb]);

            bool bIn = bx < nx && bz < nz && cellShow[bz * nx + bx];
            float topB; int tierA = cellTier[ai], tierB;
            if (bIn) { topB = cellTop[bz * nx + bx]; tierB = cellTier[bz * nx + bx]; }
            else { topB = BedY; tierB = -999; }        // void: drop to the water bed

            if (Mathf.Abs(topA - topB) < 1e-4f) return; // level neighbours: no wall

            float hi = Mathf.Max(topA, topB), lo = Mathf.Min(topA, topB);
            bool aHigher = topA > topB;
            bool ramp = bIn && Mathf.Abs(tierA - tierB) == 1; // stair only between two walkable terraces

            // Outward face normal points from the higher side toward the lower side (across the edge).
            // Wind so the quad faces that way; the shader reads walls as _SideColor regardless, but
            // correct winding keeps them from back-face culling.
            if (ramp)
            {
                // Two half-height steps: the mid ledge PROTRUDES half a cell into the LOWER cell (a
                // stoop attached to the terrace), so every face closes. Insetting under the higher
                // side instead leaves an open slot between the ledge and the higher top's overhang
                // (the higher cell's top still spans to the shared edge) — Play-caught: the water
                // showed through the rim as a blue zigzag.
                float mid = (hi + lo) * 0.5f;
                // Outset direction: perpendicular to the edge (in XZ), aimed at the LOWER cell's centre.
                Vector3 edgeMid = (e0 + e1) * 0.5f;
                Vector3 towardLower = aHigher
                    ? DirToCell(bx, bz, edgeMid)   // A is higher: outset toward B's centre
                    : DirToCell(ax, az, edgeMid);
                // Snap the aim to the cross-edge axis (the edge is axis-aligned in this grid).
                Vector3 perp = horizontal ? new Vector3(0f, 0f, 1f)   // +Z wall: edge along X ⇒ outset along Z
                                          : new Vector3(1f, 0f, 0f);  // +X wall: edge along Z ⇒ outset along X
                if (Vector3.Dot(perp, towardLower) < 0f) perp = -perp;
                Vector3 outset = perp * (CellSize * 0.5f);
                // Extend the tread + its front wall half a cell PAST both edge ends: where an X-edge
                // stair turns into a Z-edge stair the two treads would otherwise leave a quarter-cell
                // corner hole (Play-caught as pinhole water diamonds along the rim). The overlap is
                // coplanar same-colour (invisible); at inner corners the spill hides inside the mound.
                Vector3 along = (e1 - e0).normalized * (CellSize * 0.5f);
                Vector3 x0 = e0 - along, x1 = e1 + along;
                Vector3 le0 = x0 + outset, le1 = x1 + outset;

                // Lower face: from lo up to mid, along the OUTSET line (the stoop's front).
                AddWallQuad(le0, le1, lo, mid);
                // Ledge cap: the horizontal tread at mid from the outset line back to the edge.
                AddLedge(x0, x1, le0, le1, mid);
                // Upper face: from mid up to hi, along the ORIGINAL edge — meets the higher top exactly.
                AddWallQuad(e0, e1, mid, hi);
            }
            else
            {
                AddWallQuad(e0, e1, lo, hi);
            }
        }

        // Direction (XZ) from an edge point toward a cell's centre — used to aim the ramp inset.
        private static float _cMinX, _cMinZ; // grid origin, stashed for DirToCell
        private static Vector3 DirToCell(int cx, int cz, Vector3 from)
        {
            float centreX = _cMinX + (cx + 0.5f) * CellSize;
            float centreZ = _cMinZ + (cz + 0.5f) * CellSize;
            var d = new Vector3(centreX - from.x, 0f, centreZ - from.z);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>A vertical wall quad spanning edge (e0→e1) from yLo up to yHi. Emitted DOUBLE-SIDED
        /// (both windings) so it's visible regardless of which side the camera sees — a wall is a thin
        /// vertical face and the exact outward normal is fiddly to derive per edge; the cost is a
        /// handful of extra tris only along tier boundaries. Flat-shaded; the shader's slope blend
        /// paints it _SideColor (a vertical face's world normal.y ≈ 0 falls below _SlopeLo 0.45).</summary>
        private static void AddWallQuad(Vector3 e0, Vector3 e1, float yLo, float yHi)
        {
            Vector3 a = new Vector3(e0.x, yLo, e0.z);
            Vector3 b = new Vector3(e1.x, yLo, e1.z);
            Vector3 c = new Vector3(e1.x, yHi, e1.z);
            Vector3 d = new Vector3(e0.x, yHi, e0.z);
            AddWallTri(a, b, c); AddWallTri(a, c, d); // front
            AddWallTri(a, c, b); AddWallTri(a, d, c); // back (double-sided)
        }

        /// <summary>The small horizontal ledge quad at the mid step height (ramp read). Emitted
        /// double-sided like the walls: the tread's winding depends on which side of the edge the
        /// outset landed, and a few extra tris only along stair edges beats deriving it per case.</summary>
        private static void AddLedge(Vector3 e0, Vector3 e1, Vector3 le0, Vector3 le1, float y)
        {
            Vector3 a = new Vector3(e0.x, y, e0.z);
            Vector3 b = new Vector3(e1.x, y, e1.z);
            Vector3 c = new Vector3(le1.x, y, le1.z);
            Vector3 d = new Vector3(le0.x, y, le0.z);
            AddTop(a, b, c, false); AddTop(a, c, d, false);
            AddTop(a, c, b, false); AddTop(a, d, c, false); // reverse winding (double-sided)
        }

        // Shore-pull + jitter for a corner used as a top vertex. Shore/tier-boundary corners get no
        // jitter so walls meet tops exactly; interior corners get a small hash-based bump for facets.
        private static Vector3 CornerTop(float x, float z, float top, bool noJitter)
        {
            float y = top;
            if (!noJitter) y += (Hash01(x, z) * 2f - 1f) * JitterAmp;
            return new Vector3(x, y, z);
        }

        // Wall triangles carry white vertex colour (the shader paints them _SideColor by slope).
        private static void AddWallTri(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            _cols.Add(Color.white); _cols.Add(Color.white); _cols.Add(Color.white);
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
        }

        // Top triangles carry the grass mottle (× shore ink when near the void), matching Ground.
        private static void AddTop(Vector3 a, Vector3 b, Vector3 c, bool ink)
        {
            int i = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            _cols.Add(TopTint(a.x, a.z, ink));
            _cols.Add(TopTint(b.x, b.z, ink));
            _cols.Add(TopTint(c.x, c.z, ink));
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
        }

        /// <summary>Same brightness/hue mottle as <see cref="Ground"/> (so SetZone retints read on any
        /// hue), darkened by the shore-ink multiplier for the painted shore band.</summary>
        private static Color TopTint(float x, float z, bool ink)
        {
            float p = Mathf.PerlinNoise(x * 0.05f + 30f, z * 0.05f + 30f);
            float q = Mathf.PerlinNoise(x * 0.17f + 70f, z * 0.17f + 70f);
            float br = Mathf.Lerp(0.93f, 1.03f, p * 0.7f + q * 0.3f);
            float hue = Mathf.PerlinNoise(x * 0.03f + 90f, z * 0.03f + 90f);
            var c = new Color(br * Mathf.Lerp(0.92f, 1.0f, hue), br, br * Mathf.Lerp(1.04f, 0.96f, hue));
            if (ink) { c.r *= ShoreInk; c.g *= ShoreInk; c.b *= ShoreInk; }
            return c;
        }

        // A shown cell is inked when it lies within ShoreBand tiles of a void/edge cell.
        private static bool CellNearShore(int cx, int cz, int nx, int nz, bool[] cellShow)
        {
            int r = Mathf.CeilToInt(ShoreBand);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int tx = cx + dx, tz = cz + dz;
                    if (tx < 0 || tz < 0 || tx >= nx || tz >= nz) return true; // grid edge = void beyond
                    int ti = tz * nx + tx;
                    if (!cellShow[ti]) return true; // a nearby void/skipped cell = shore
                }
            return false;
        }

        // Deterministic hash of a world XZ point to [0,1) — a fixed-seed value noise (NOT Random),
        // so facet jitter is stable across runs/screenshots (matches the deterministic art rule).
        private static float Hash01(float x, float z)
        {
            // Integer-lattice value noise: hash the four surrounding lattice points, bilerp. Cheap and
            // seam-free enough for ±0.12u facet bumps.
            int xi = Mathf.FloorToInt(x), zi = Mathf.FloorToInt(z);
            float fx = x - xi, fz = z - zi;
            float h00 = HashLattice(xi, zi), h10 = HashLattice(xi + 1, zi);
            float h01 = HashLattice(xi, zi + 1), h11 = HashLattice(xi + 1, zi + 1);
            float a = Mathf.Lerp(h00, h10, fx), b = Mathf.Lerp(h01, h11, fx);
            return Mathf.Lerp(a, b, fz);
        }

        private static float HashLattice(int x, int z)
        {
            uint h = (uint)(x * 374761393 + z * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000; // [0,1)
        }

        private static void GetBounds(ArenaLayout arena, out float minX, out float minZ,
                                      out float maxX, out float maxZ)
        {
            minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
            foreach (var sh in arena.Shapes)
            {
                float ex = (float)sh.HalfW;                                        // rect half-W or disc radius
                float ez = (float)(sh.Kind == ArenaShapeKind.Disc ? sh.HalfW : sh.HalfD);
                minX = Mathf.Min(minX, (float)sh.X - ex); maxX = Mathf.Max(maxX, (float)sh.X + ex);
                minZ = Mathf.Min(minZ, (float)sh.Y - ez); maxZ = Mathf.Max(maxZ, (float)sh.Y + ez);
            }
            if (minX > maxX) { minX = minZ = -1f; maxX = maxZ = 1f; } // empty guard
        }
    }
}
