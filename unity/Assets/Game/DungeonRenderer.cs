#nullable enable
using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Client RENDER of a pure-data <see cref="Dungeon"/> (roguelite slice 2). Reads the generator's
    /// grid/rooms/props/spawns and bakes the reference look: near-black void, chunky flat-shaded block
    /// walls with a lighter cap trim, tiled floors with wall-base AO + a subtle checkerboard, a sea of
    /// tiny warm emissive flames under bloom, and only ~12 real point lights. Nothing here decides
    /// rules — it only reads the data the sim authored (grid (x,y) → world (x, 0, y), 1 cell = 1 unit).
    ///
    /// Determinism: every per-cell jitter comes from a seeded hash of (x,y,salt), never
    /// UnityEngine.Random — a seed reproduces the same visuals for screenshot regression.
    ///
    /// Geometry is CHUNKED into ~24×24-cell blocks (one floor mesh + one wall mesh per chunk): a single
    /// dungeon-wide mesh would hit URP Forward's per-object additional-light cap (~8), so chunking lets
    /// each block pick up its own nearby torch lights. The reference runs 64 draw calls; an 80-room
    /// molten dungeon (~120×120 cells) chunks to ~5×5 = 25 blocks × 2 kinds = ~50 draws + props/lights.
    /// </summary>
    public static class DungeonRenderer
    {
        private const int Chunk = 24;          // cells per chunk edge
        private const float WallBaseH = 2.0f;  // base wall height (+ jitter)
        private const float CapH = 0.22f;      // cap slab thickness on top of a wall
        private const int LightBudget = 12;    // total point lights (reference budget)

        private static Material? _litMat;

        // ---- public API -----------------------------------------------------

        /// <summary>Build the whole dungeon under a fresh root parented to <paramref name="parent"/>.
        /// Returns the root so a caller (preview window) can <see cref="Clear"/> it wholesale.</summary>
        public static GameObject Build(Dungeon d, string themeKey, Transform? parent, bool showSpawns = true)
        {
            var theme = DungeonTheme.Get(themeKey);
            var root = new GameObject("DungeonRender");
            if (parent != null) root.transform.SetParent(parent, false);

            var mat = LitMaterial();
            BuildGeometry(d, theme, root.transform, mat);
            BuildProps(d, theme, root.transform);
            if (showSpawns) BuildSpawnMarkers(d, theme, root.transform);
            var lights = BuildLights(d, theme, root.transform);

            // Flicker driver: gathers the point lights + flame quads and shimmers them (edit mode too).
            var flicker = root.AddComponent<DungeonFlicker>();
            flicker.Init(lights);

            return root;
        }

        /// <summary>Destroy a root built by <see cref="Build"/> (edit-safe DestroyImmediate).</summary>
        public static void Clear(GameObject? root)
        {
            if (root == null) return;
            if (Application.isPlaying) Object.Destroy(root);
            else Object.DestroyImmediate(root);
        }

        // ---- shared materials ----------------------------------------------

        /// <summary>The shared level-geometry material (IdleGame/DungeonLit, matte). One instance for
        /// every floor/wall chunk so the whole dungeon batches on one material.</summary>
        public static Material LitMaterial()
        {
            if (_litMat != null) return _litMat;
            var sh = Shader.Find("IdleGame/DungeonLit");
            if (sh == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _litMat = new Material(lit) { color = Color.gray };
            }
            else _litMat = new Material(sh);
            Bootstrap.MakeMatte(_litMat);
            return _litMat;
        }

        /// <summary>An UNLIT emissive material (URP Unlit if present, else a Lit with full emission) for
        /// flame quads / glows / crystals — bright enough to bloom, unaffected by scene lighting. Each
        /// call makes a fresh instance so per-prop colours don't share.</summary>
        public static Material EmissiveMaterial(Color color, float intensity = 1f)
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                var m = new Material(unlit);
                var c = color * intensity;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                m.color = c;
                return m;
            }
            // Fallback: Lit with emission so it still glows.
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var lm = new Material(lit) { color = Color.black };
            Bootstrap.MakeMatte(lm);
            if (lm.HasProperty("_EmissionColor"))
            {
                lm.EnableKeyword("_EMISSION");
                lm.SetColor("_EmissionColor", color * intensity);
            }
            return lm;
        }

        // ---- geometry (floor + wall chunks) --------------------------------

        // Per-chunk mesh accumulators (reused across chunks).
        private sealed class MeshBuf
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
            public void Clear() { V.Clear(); C.Clear(); T.Clear(); }
            public bool Empty => V.Count == 0;
        }

        private static void BuildGeometry(Dungeon d, DungeonTheme th, Transform parent, Material mat)
        {
            int w = d.W, h = d.H;
            int chunksX = (w + Chunk - 1) / Chunk;
            int chunksZ = (h + Chunk - 1) / Chunk;

            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(parent, false);
            var wallGo = new GameObject("Walls");
            wallGo.transform.SetParent(parent, false);

            var floorBuf = new MeshBuf();
            var wallBuf = new MeshBuf();

            // Doorway lookup (cheap set) for the ×1.14 highlight in the floor pipeline.
            var doorSet = new HashSet<int>();
            foreach (var dc in d.Doorways) doorSet.Add(dc.Y * w + dc.X);
            var corridorSet = new HashSet<int>();
            foreach (var cc in d.CorridorCells) corridorSet.Add(cc.Y * w + cc.X);

            // Per-cell owning room type (for the non-combat room tint). -1 = none.
            var cellRoomType = RoomTypeGrid(d);

            for (int cz = 0; cz < chunksZ; cz++)
                for (int cx = 0; cx < chunksX; cx++)
                {
                    floorBuf.Clear();
                    wallBuf.Clear();
                    int x0 = cx * Chunk, x1 = Mathf.Min(x0 + Chunk, w);
                    int z0 = cz * Chunk, z1 = Mathf.Min(z0 + Chunk, h);

                    for (int y = z0; y < z1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            byte cell = d.Grid[y * w + x];
                            if (cell == DungeonCell.Floor)
                                EmitFloorTile(floorBuf, d, th, x, y, w, h, doorSet, corridorSet, cellRoomType);
                            else if (cell == DungeonCell.Wall)
                                EmitWall(wallBuf, d, th, x, y, w, h);
                        }

                    if (!floorBuf.Empty) MakeChunk(floorBuf, floorGo.transform, mat, $"FloorChunk_{cx}_{cz}");
                    if (!wallBuf.Empty) MakeChunk(wallBuf, wallGo.transform, mat, $"WallChunk_{cx}_{cz}");
                }
        }

        private static void MakeChunk(MeshBuf buf, Transform parent, Material mat, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(buf.V);
            mesh.SetColors(buf.C);
            mesh.SetTriangles(buf.T, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Floor tile: a 1×1 quad at y≈0, colour per the constants-doc pipeline in EXACT order.
        private static void EmitFloorTile(MeshBuf buf, Dungeon d, DungeonTheme th, int x, int y,
            int w, int h, HashSet<int> doorSet, HashSet<int> corridorSet, sbyte[] cellRoomType)
        {
            int idx = y * w + x;
            bool corridor = corridorSet.Contains(idx);

            // 1. base = corridor ? corridor : floor
            Color c = corridor ? th.Corridor : th.Floor;

            // 2. non-combat room tint (0.17 lerp toward the room-type TINT)
            sbyte rt = cellRoomType[idx];
            if (rt >= 0 && (RoomType)rt != RoomType.Combat)
            {
                if (th.Tint.TryGetValue((RoomType)rt, out var tint))
                    c = Color.Lerp(c, tint, 0.17f);
            }

            // 3. doorway ×1.14
            if (doorSet.Contains(idx)) c *= 1.14f;

            // 4. AO ×(1 − 0.11·min(walls8, 4))
            int walls8 = Walls8(d, x, y, w, h);
            c *= 1f - 0.11f * Mathf.Min(walls8, 4);

            // 5. checkerboard ×0.965 on odd (x+y)
            if (((x + y) & 1) != 0) c *= 0.965f;

            // 6. value noise ×rand(0.94..1.06)
            c *= Mathf.Lerp(0.94f, 1.06f, Hash01(x, y, 17));
            c.a = 1f;

            // 7. tiny per-tile Y offset rand(−0.02..0.008)
            float ty = Mathf.Lerp(-0.02f, 0.008f, Hash01(x, y, 23));

            float fx = x, fz = y;
            Vector3 p00 = new Vector3(fx, ty, fz);
            Vector3 p10 = new Vector3(fx + 1f, ty, fz);
            Vector3 p01 = new Vector3(fx, ty, fz + 1f);
            Vector3 p11 = new Vector3(fx + 1f, ty, fz + 1f);
            AddQuadUp(buf, p00, p01, p11, p10, c);
        }

        // Wall: a box (1×h×1) with a cap slab on top. Colour wall×rand(0.9..1.08), cap×rand(0.92..1.1).
        private static void EmitWall(MeshBuf buf, Dungeon d, DungeonTheme th, int x, int y, int w, int h)
        {
            float bh = WallBaseH + Mathf.Lerp(-0.25f, 0.25f, Hash01(x, y, 31));
            Color wallC = th.Wall * Mathf.Lerp(0.9f, 1.08f, Hash01(x, y, 37)); wallC.a = 1f;
            Color capC = th.Cap * Mathf.Lerp(0.92f, 1.1f, Hash01(x, y, 41)); capC.a = 1f;

            float x0 = x, x1 = x + 1f, z0 = y, z1 = y + 1f;

            // Only emit a side face where the neighbour is NOT a wall (skip wall-wall shared faces to
            // cut overdraw). Top always; bottom never (unseen under the ortho diorama cam).
            bool nx0 = !IsWall(d, x - 1, y, w, h);
            bool nx1 = !IsWall(d, x + 1, y, w, h);
            bool nz0 = !IsWall(d, x, y - 1, w, h);
            bool nz1 = !IsWall(d, x, y + 1, w, h);

            // Wall body (0..bh).
            EmitBox(buf, x0, x1, 0f, bh, z0, z1, wallC, nx0, nx1, nz0, nz1, top: false, bottom: false);
            // Cap slab (bh..bh+CapH) — all four sides + top, its own lighter colour.
            EmitBox(buf, x0, x1, bh, bh + CapH, z0, z1, capC, true, true, true, true, top: true, bottom: false);
        }

        // ---- props ----------------------------------------------------------

        private static void BuildProps(Dungeon d, DungeonTheme th, Transform parent)
        {
            var propsGo = new GameObject("Props");
            propsGo.transform.SetParent(parent, false);
            var pt = propsGo.transform;

            foreach (var p in d.Props)
            {
                switch (p.Kind)
                {
                    case PropKind.Pillar:        BuildPillar(pt, th, p); break;
                    case PropKind.Torch:         BuildTorch(pt, th, p, d); break;
                    case PropKind.Debris:        BuildDebris(pt, th, p); break;
                    case PropKind.Brazier:       BuildBrazier(pt, th, p); break;
                    case PropKind.Chest:         BuildChest(pt, th, p); break;
                    case PropKind.ShrineCrystal: BuildCrystal(pt, th, p); break;
                    case PropKind.PortalRing:    BuildPortal(pt, th, p); break;
                }
            }
        }

        private static Vector3 CellCentre(DungeonProp p, float y) => new Vector3(p.X + 0.5f, y, p.Y + 0.5f);

        // A faceted box mesh GameObject on the shared lit material, flat vertex colour.
        private static GameObject SolidBox(Transform parent, Vector3 centre, Vector3 size, Color col,
            string name, float rotY = 0f)
        {
            var buf = new MeshBuf();
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            EmitBox(buf, -hx, hx, 0f, size.y, -hz, hz, col, true, true, true, true, true, true);
            var go = MakeMeshGo(buf, parent, LitMaterial(), name);
            go.transform.localPosition = centre;
            if (rotY != 0f) go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            return go;
        }

        // An emissive quad (billboard-ish flat card standing up in +Y) at a point.
        private static GameObject EmissiveQuad(Transform parent, Vector3 centre, float size, Color col,
            float intensity, string name)
        {
            var buf = new MeshBuf();
            float hs = size * 0.5f;
            // A vertical card facing -Z and +Z (double-sided), centred at `centre`.
            Vector3 a = new Vector3(-hs, -hs, 0f), b = new Vector3(hs, -hs, 0f);
            Vector3 c = new Vector3(hs, hs, 0f), dd = new Vector3(-hs, hs, 0f);
            AddQuadRaw(buf, a, b, c, dd, Color.white);
            AddQuadRaw(buf, a, dd, c, b, Color.white); // back
            var go = MakeMeshGo(buf, parent, EmissiveMaterial(col, intensity), name);
            go.transform.localPosition = centre;
            return go;
        }

        private static void BuildPillar(Transform parent, DungeonTheme th, DungeonProp p)
        {
            var root = new GameObject("Pillar");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = CellCentre(p, 0f);
            // Tapered box stack: wide base, narrower shaft, wide cap.
            SolidBox(root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.85f, 0.25f, 0.85f), th.Pillar * 0.92f, "base");
            SolidBox(root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 2.1f, 0.6f), th.Pillar, "shaft");
            SolidBox(root.transform, new Vector3(0f, 2.35f, 0f), new Vector3(0.85f, 0.25f, 0.85f), th.Pillar * 1.05f, "cap");
        }

        private static void BuildTorch(Transform parent, DungeonTheme th, DungeonProp p, Dungeon d)
        {
            var root = new GameObject("Torch");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = CellCentre(p, 0f);

            // Facing: point the arm toward the adjacent wall (the torch cell touches a wall by data rule).
            Vector2 face = TorchFacing(d, p.X, p.Y);
            Vector3 arm = new Vector3(face.x, 1.5f, face.y) * 0.35f;
            // Small wall-mounted arm (dark bracket).
            SolidBox(root.transform, new Vector3(face.x, 1.4f, face.y) * 0.3f, new Vector3(0.14f, 0.14f, 0.14f),
                th.Wall * 0.7f, "bracket");

            // Two nested emissive flame quads: outer flame, inner core. Both float above the arm.
            // Sizes screenshot-tuned ×1.8 over the first pass: at the reference zoom (~10px/tile) the
            // original quads read as 1px specks where preview.jpg shows fat glowing embers.
            Vector3 flamePos = new Vector3(face.x, 1.7f, face.y) * 0.3f + new Vector3(0f, 0.25f, 0f);
            var outer = EmissiveQuad(root.transform, flamePos, 0.9f, th.Flame, 1.6f, "flame");
            var inner = EmissiveQuad(root.transform, flamePos + new Vector3(0f, 0.02f, -0.02f), 0.47f, th.FlameCore, 1.9f, "core");
            // Tag them so the flicker driver scale-jitters the flames.
            root.AddComponent<DungeonFlameTag>().Init(outer.transform, inner.transform,
                Hash01(p.X, p.Y, 7) * 6.283f);
        }

        private static void BuildDebris(Transform parent, DungeonTheme th, DungeonProp p)
        {
            var root = new GameObject("Debris");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = CellCentre(p, 0f);
            float rot = (float)(p.Rot * Mathf.Rad2Deg);
            root.transform.localRotation = Quaternion.Euler(0f, rot, 0f);
            float s = (float)p.Scale;
            // A few small rock boxes, alternating the two debris tints.
            SolidBox(root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.4f, 0.28f, 0.35f) * s, th.DebrisA, "rock0",
                Hash01(p.X, p.Y, 3) * 90f);
            SolidBox(root.transform, new Vector3(0.22f * s, 0f, 0.15f * s), new Vector3(0.24f, 0.18f, 0.22f) * s, th.DebrisB, "rock1",
                Hash01(p.X, p.Y, 5) * 90f);
            SolidBox(root.transform, new Vector3(-0.18f * s, 0f, -0.14f * s), new Vector3(0.2f, 0.14f, 0.2f) * s, th.DebrisA * 1.05f, "rock2",
                Hash01(p.X, p.Y, 9) * 90f);
        }

        private static void BuildBrazier(Transform parent, DungeonTheme th, DungeonProp p)
        {
            var root = new GameObject("Brazier");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = CellCentre(p, 0f);
            // Dark bowl on a short stem.
            SolidBox(root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.2f, 0.55f, 0.2f), DungeonTheme.BrazierBody, "stem");
            SolidBox(root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.6f, 0.22f, 0.6f), DungeonTheme.BrazierBody * 1.1f, "bowl");
            // Emissive coals + a flame card.
            var coals = EmissiveQuad(root.transform, new Vector3(0f, 0.72f, 0f), 0.45f, DungeonTheme.BrazierCoal, 1.7f, "coals");
            var flame = EmissiveQuad(root.transform, new Vector3(0f, 0.95f, 0f), 0.55f, th.Flame, 1.6f, "flame");
            root.AddComponent<DungeonFlameTag>().Init(flame.transform, coals.transform, Hash01(p.X, p.Y, 11) * 6.283f);
        }

        private static void BuildChest(Transform parent, DungeonTheme th, DungeonProp p)
        {
            var root = new GameObject("Chest");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = CellCentre(p, 0f);
            SolidBox(root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.7f, 0.45f, 0.5f), DungeonTheme.ChestBody, "body");
            SolidBox(root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.74f, 0.12f, 0.54f), DungeonTheme.ChestTrim, "lid");
            EmissiveQuad(root.transform, new Vector3(0f, 0.5f, 0f), 0.5f, DungeonTheme.ChestGlow, 0.9f, "glow");
        }

        private static void BuildCrystal(Transform parent, DungeonTheme th, DungeonProp p)
        {
            var root = new GameObject("ShrineCrystal");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = CellCentre(p, 0f);
            // Plinth: pillar colour lerped toward white ×1.12 (per constants doc).
            Color plinth = Color.Lerp(th.Pillar, Color.white, 0.12f);
            SolidBox(root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.7f, 0.35f, 0.7f), plinth, "plinth");
            // Emissive octahedron floating above.
            var oct = EmissiveOctahedron(root.transform, new Vector3(0f, 0.95f, 0f), 0.35f, DungeonTheme.CrystalCol, 1.6f, "crystal");
            oct.AddComponent<DungeonSpin>();
        }

        private static void BuildPortal(Transform parent, DungeonTheme th, DungeonProp p)
        {
            var root = new GameObject("PortalRing");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = CellCentre(p, 0.16f); // ring at y≈0.16
            // A flat emissive teal ring lying on the floor.
            var buf = new MeshBuf();
            EmitRing(buf, 0.9f, 1.15f, 24, Color.white);
            var go = MakeMeshGo(buf, root.transform, EmissiveMaterial(DungeonTheme.PortalCol, 1.4f), "ring");
            go.transform.localRotation = Quaternion.identity;
        }

        // ---- spawn markers --------------------------------------------------

        private static void BuildSpawnMarkers(Dungeon d, DungeonTheme th, Transform parent)
        {
            var go = new GameObject("Spawns");
            go.transform.SetParent(parent, false);
            foreach (var s in d.Spawns)
            {
                Color col = s.Tier == 2 ? DungeonTheme.SpawnBoss
                          : s.Tier == 1 ? DungeonTheme.SpawnElite
                          : DungeonTheme.SpawnNormal;
                float intensity = s.Tier == 0 ? 0.6f : 1.0f;
                // A small faint emissive diamond flat on the floor.
                var buf = new MeshBuf();
                float r = s.Tier == 2 ? 0.55f : 0.28f;
                Vector3 n = new Vector3(0f, r, 0.02f), e = new Vector3(r, 0f, 0f);
                Vector3 s2 = new Vector3(0f, -r, 0.02f), wv = new Vector3(-r, 0f, 0f);
                AddQuadRaw(buf, s2, e, n, wv, Color.white);
                AddQuadRaw(buf, s2, wv, n, e, Color.white); // double-sided
                var m = MakeMeshGo(buf, go.transform, EmissiveMaterial(col, intensity), $"spawn_{s.X}_{s.Y}");
                m.transform.localPosition = new Vector3(s.X + 0.5f, 0.05f, s.Y + 0.5f);
                m.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat on the floor
            }
        }

        // ---- lights (budget 12) --------------------------------------------

        private static List<Light> BuildLights(Dungeon d, DungeonTheme th, Transform parent)
        {
            var go = new GameObject("Lights");
            go.transform.SetParent(parent, false);
            var lights = new List<Light>();

            // Key lights on named rooms.
            DungeonRoom? entrance = FindRoom(d, RoomType.Entrance);
            DungeonRoom? boss = FindRoom(d, RoomType.Boss);
            DungeonRoom? shrine = FindRoom(d, RoomType.Shrine);

            if (entrance != null)
                lights.Add(MakePoint(go.transform, entrance.Cx + 0.5f, 1.6f, entrance.Cy + 0.5f,
                    DungeonTheme.KeyEntrance, 1.0f, 13f, "key_entrance"));
            if (boss != null)
                lights.Add(MakePoint(go.transform, boss.Cx + 0.5f, 2.2f, boss.Cy + 0.5f,
                    DungeonTheme.KeyBoss, 1.7f, 17f, "key_boss"));
            if (shrine != null)
                lights.Add(MakePoint(go.transform, shrine.Cx + 0.5f, 1.7f, shrine.Cy + 0.5f,
                    DungeonTheme.KeyShrine, 1.0f, 12f, "key_shrine"));

            // Torch lights: farthest-point-sampled subset filling the remaining budget (min 4).
            var torches = new List<DungeonProp>();
            foreach (var p in d.Props) if (p.Kind == PropKind.Torch) torches.Add(p);
            int torchBudget = Mathf.Max(4, LightBudget - lights.Count);
            var chosen = FarthestPointSample(d, torches, torchBudget);
            foreach (var t in chosen)
            {
                Vector2 face = TorchFacing(d, t.X, t.Y);
                // Torch cell nudged 0.6 toward its facing, y 1.7.
                float wx = t.X + 0.5f + face.x * 0.6f;
                float wz = t.Y + 0.5f + face.y * 0.6f;
                lights.Add(MakePoint(go.transform, wx, 1.7f, wz,
                    th.TorchLight, th.TorchIntensity, th.TorchDistance, $"torch_{t.X}_{t.Y}"));
            }
            return lights;
        }

        private static Light MakePoint(Transform parent, float x, float y, float z, Color col,
            float intensity, float range, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, y, z);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = col;
            // Theme intensities are the reference's gamma-space three.js units; URP linear HDR needs
            // them several times hotter to pool the same way (screenshot-tuned — see DungeonTheme).
            l.intensity = intensity * DungeonTheme.UrpLightScale;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        // Farthest-point sampling: greedily pick torches that maximise the min distance to the picks
        // so far, so the chosen subset spreads across the whole map (deterministic — starts from id 0).
        private static List<DungeonProp> FarthestPointSample(Dungeon d, List<DungeonProp> torches, int k)
        {
            var picks = new List<DungeonProp>();
            if (torches.Count == 0) return picks;
            k = Mathf.Min(k, torches.Count);
            picks.Add(torches[0]); // deterministic seed pick
            var minD = new float[torches.Count];
            for (int i = 0; i < torches.Count; i++) minD[i] = Dist2(torches[i], torches[0]);
            while (picks.Count < k)
            {
                int best = -1; float bestD = -1f;
                for (int i = 0; i < torches.Count; i++)
                {
                    if (minD[i] < 0f) continue; // already picked
                    if (minD[i] > bestD) { bestD = minD[i]; best = i; }
                }
                if (best < 0) break;
                picks.Add(torches[best]);
                minD[best] = -1f; // mark picked
                for (int i = 0; i < torches.Count; i++)
                {
                    if (minD[i] < 0f) continue;
                    float dd = Dist2(torches[i], torches[best]);
                    if (dd < minD[i]) minD[i] = dd;
                }
            }
            return picks;
        }

        private static float Dist2(DungeonProp a, DungeonProp b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        // ---- low-level mesh emit -------------------------------------------

        // A box with selectable side faces (all flat-shaded, own verts). All faces carry `col`.
        private static void EmitBox(MeshBuf buf, float x0, float x1, float y0, float y1, float z0, float z1,
            Color col, bool nx0, bool nx1, bool nz0, bool nz1, bool top, bool bottom)
        {
            // corners
            Vector3 c000 = new Vector3(x0, y0, z0), c100 = new Vector3(x1, y0, z0);
            Vector3 c010 = new Vector3(x0, y1, z0), c110 = new Vector3(x1, y1, z0);
            Vector3 c001 = new Vector3(x0, y0, z1), c101 = new Vector3(x1, y0, z1);
            Vector3 c011 = new Vector3(x0, y1, z1), c111 = new Vector3(x1, y1, z1);

            if (top)    AddQuadRaw(buf, c010, c011, c111, c110, col); // +Y
            if (bottom) AddQuadRaw(buf, c000, c100, c101, c001, col); // -Y
            if (nz0)    AddQuadRaw(buf, c000, c010, c110, c100, col); // -Z
            if (nz1)    AddQuadRaw(buf, c001, c101, c111, c011, col); // +Z
            if (nx0)    AddQuadRaw(buf, c000, c001, c011, c010, col); // -X
            if (nx1)    AddQuadRaw(buf, c100, c110, c111, c101, col); // +X
        }

        // An upward-facing quad (a,b,c,d wound CCW seen from +Y) with a single colour.
        private static void AddQuadUp(MeshBuf buf, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
            => AddQuadRaw(buf, a, b, c, d, col);

        // Add a quad as two tris (a-b-c, a-c-d), flat colour, own verts.
        private static void AddQuadRaw(MeshBuf buf, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            int i = buf.V.Count;
            buf.V.Add(a); buf.V.Add(b); buf.V.Add(c); buf.V.Add(d);
            buf.C.Add(col); buf.C.Add(col); buf.C.Add(col); buf.C.Add(col);
            buf.T.Add(i); buf.T.Add(i + 1); buf.T.Add(i + 2);
            buf.T.Add(i); buf.T.Add(i + 2); buf.T.Add(i + 3);
        }

        // A flat ring (annulus) in the XZ plane, y=0, `seg` segments, inner/outer radius.
        private static void EmitRing(MeshBuf buf, float rIn, float rOut, int seg, Color col)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 i0 = new Vector3(Mathf.Cos(a0) * rIn, 0f, Mathf.Sin(a0) * rIn);
                Vector3 o0 = new Vector3(Mathf.Cos(a0) * rOut, 0f, Mathf.Sin(a0) * rOut);
                Vector3 i1 = new Vector3(Mathf.Cos(a1) * rIn, 0f, Mathf.Sin(a1) * rIn);
                Vector3 o1 = new Vector3(Mathf.Cos(a1) * rOut, 0f, Mathf.Sin(a1) * rOut);
                AddQuadRaw(buf, i0, o0, o1, i1, col);
                AddQuadRaw(buf, i0, i1, o1, o0, col); // double-sided
            }
        }

        private static GameObject EmissiveOctahedron(Transform parent, Vector3 centre, float r, Color col,
            float intensity, string name)
        {
            var buf = new MeshBuf();
            Vector3 top = new Vector3(0f, r, 0f), bot = new Vector3(0f, -r, 0f);
            Vector3 px = new Vector3(r, 0f, 0f), nx = new Vector3(-r, 0f, 0f);
            Vector3 pz = new Vector3(0f, 0f, r), nz = new Vector3(0f, 0f, -r);
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = buf.V.Count; buf.V.Add(a); buf.V.Add(b); buf.V.Add(c); buf.C.Add(Color.white); buf.C.Add(Color.white); buf.C.Add(Color.white); buf.T.Add(i); buf.T.Add(i + 1); buf.T.Add(i + 2); }
            Tri(top, px, pz); Tri(top, pz, nx); Tri(top, nx, nz); Tri(top, nz, px);
            Tri(bot, pz, px); Tri(bot, nx, pz); Tri(bot, nz, nx); Tri(bot, px, nz);
            var go = MakeMeshGo(buf, parent, EmissiveMaterial(col, intensity), name);
            go.transform.localPosition = centre;
            return go;
        }

        private static GameObject MakeMeshGo(MeshBuf buf, Transform parent, Material mat, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(buf.V);
            mesh.SetColors(buf.C);
            mesh.SetTriangles(buf.T, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ---- grid helpers ---------------------------------------------------

        private static bool IsWall(Dungeon d, int x, int y, int w, int h)
            => x >= 0 && x < w && y >= 0 && y < h && d.Grid[y * w + x] == DungeonCell.Wall;

        private static bool IsFloor(Dungeon d, int x, int y, int w, int h)
            => x >= 0 && x < w && y >= 0 && y < h && d.Grid[y * w + x] == DungeonCell.Floor;

        private static int Walls8(Dungeon d, int x, int y, int w, int h)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (IsWall(d, x + dx, y + dy, w, h)) n++;
                }
            return n;
        }

        // Direction (grid XZ) from a torch cell toward its adjacent wall (the wall it's mounted on).
        private static Vector2 TorchFacing(Dungeon d, int x, int y)
        {
            int w = d.W, h = d.H;
            if (IsWall(d, x + 1, y, w, h)) return new Vector2(1f, 0f);
            if (IsWall(d, x - 1, y, w, h)) return new Vector2(-1f, 0f);
            if (IsWall(d, x, y + 1, w, h)) return new Vector2(0f, 1f);
            if (IsWall(d, x, y - 1, w, h)) return new Vector2(0f, -1f);
            return new Vector2(0f, 1f); // fallback
        }

        // Per-cell owning room type (row-major stamp of each room's footprint bbox), -1 = none. Used
        // for the non-combat floor tint. Later rooms overwrite earlier in the bbox — matches "the cell
        // belongs to the room whose footprint covers it"; overlaps are rare (rooms are separated).
        private static sbyte[] RoomTypeGrid(Dungeon d)
        {
            int w = d.W, h = d.H;
            var g = new sbyte[w * h];
            for (int i = 0; i < g.Length; i++) g[i] = -1;
            foreach (var r in d.Rooms)
            {
                int halfW = r.W / 2, halfH = r.H / 2;
                for (int y = r.Cy - halfH; y <= r.Cy + halfH; y++)
                {
                    if (y < 0 || y >= h) continue;
                    for (int x = r.Cx - halfW; x <= r.Cx + halfW; x++)
                    {
                        if (x < 0 || x >= w) continue;
                        if (d.Grid[y * w + x] != DungeonCell.Floor) continue;
                        g[y * w + x] = (sbyte)r.Type;
                    }
                }
            }
            return g;
        }

        private static DungeonRoom? FindRoom(Dungeon d, RoomType type)
        {
            foreach (var r in d.Rooms) if (r.Type == type) return r;
            return null;
        }

        // Deterministic hash of (x,y,salt) → [0,1). Integer bit-mix — NOT UnityEngine.Random, so the
        // visuals reproduce from the dungeon data alone (the art determinism rule).
        private static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint hh = (uint)(x * 374761393 + y * 668265263 + salt * 2246822519);
                hh = (hh ^ (hh >> 13)) * 1274126177u;
                hh ^= hh >> 16;
                return (hh & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }

    /// <summary>
    /// Edit-mode-safe flicker driver on the dungeon root: sine/noise-flickers each point light's
    /// intensity around its base (per-light phase i×1.7), scale-jitters the flame quads tagged by
    /// <see cref="DungeonFlameTag"/>, and — the load-bearing part — PUSHES the light table into the
    /// global shader arrays DungeonLit actually reads (_DungeonLightPos/.w=range, _DungeonLightColor
    /// premultiplied by flickered intensity, _DungeonLightCount). The Unity Light components are kept
    /// as the authoring/source-of-truth, but level geometry is lit from these globals: URP's own
    /// additional-light path proved unreliable across edit-mode render paths (the Forward+ cluster
    /// macros rendered BLACK in offscreen SingleCameraRequest renders while working in the game view).
    /// <see cref="ExecuteAlways"/> so the preview shimmers without Play.
    /// </summary>
    [ExecuteAlways]
    public sealed class DungeonFlicker : MonoBehaviour
    {
        public const int MaxShaderLights = 16; // must match DUNGEON_MAX_LIGHTS in DungeonLit.shader

        private static readonly Vector4[] _posBuf = new Vector4[MaxShaderLights];
        private static readonly Vector4[] _colBuf = new Vector4[MaxShaderLights];
        private static readonly int _idPos = Shader.PropertyToID("_DungeonLightPos");
        private static readonly int _idCol = Shader.PropertyToID("_DungeonLightColor");
        private static readonly int _idCount = Shader.PropertyToID("_DungeonLightCount");

        private Light[] _lights = System.Array.Empty<Light>();
        private float[] _baseIntensity = System.Array.Empty<float>();
        private DungeonFlameTag[] _flames = System.Array.Empty<DungeonFlameTag>();

        public void Init(List<Light> lights)
        {
            _lights = lights.ToArray();
            _baseIntensity = new float[_lights.Length];
            for (int i = 0; i < _lights.Length; i++)
                _baseIntensity[i] = _lights[i] != null ? _lights[i].intensity : 1f;
            _flames = GetComponentsInChildren<DungeonFlameTag>(true);
            PushLightGlobals();
        }

        private void OnEnable()
        {
            // Re-gather after a domain reload (arrays don't survive serialization here).
            if (_flames == null || _flames.Length == 0)
                _flames = GetComponentsInChildren<DungeonFlameTag>(true);
            if (_lights.Length == 0)
            {
                var found = GetComponentsInChildren<Light>(true);
                var pts = new List<Light>();
                foreach (var l in found) if (l.type == LightType.Point) pts.Add(l);
                Init(pts);
            }
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(_idCount, 0f); // no stale lights when the preview is torn down
        }

        private void Update()
        {
            float t = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] == null) continue;
                float ph = i * 1.7f;
                // Noise-ish flicker: two sines beating, ±18% around base.
                float f = 0.82f + 0.18f * (0.6f * Mathf.Sin(t * 6.3f + ph) + 0.4f * Mathf.Sin(t * 11.1f + ph * 2.1f) + 1f) * 0.5f;
                _lights[i].intensity = _baseIntensity[i] * f;
            }
            for (int i = 0; i < _flames.Length; i++)
                if (_flames[i] != null) _flames[i].Flicker(t);
            PushLightGlobals();
        }

        /// <summary>Write the (flickered) light table into the DungeonLit globals.</summary>
        private void PushLightGlobals()
        {
            int n = 0;
            for (int i = 0; i < _lights.Length && n < MaxShaderLights; i++)
            {
                var l = _lights[i];
                if (l == null || !l.enabled) continue;
                var p = l.transform.position;
                _posBuf[n] = new Vector4(p.x, p.y, p.z, l.range);
                var c = l.color * l.intensity;
                _colBuf[n] = new Vector4(c.r, c.g, c.b, 1f);
                n++;
            }
            Shader.SetGlobalVectorArray(_idPos, _posBuf);
            Shader.SetGlobalVectorArray(_idCol, _colBuf);
            Shader.SetGlobalFloat(_idCount, n);
        }
    }

    /// <summary>Marks a torch/brazier's two flame quads so <see cref="DungeonFlicker"/> scale-jitters
    /// them (a per-flame phase keeps them out of lockstep). Stores the quad base scales.</summary>
    public sealed class DungeonFlameTag : MonoBehaviour
    {
        [SerializeField] private Transform? _outer;
        [SerializeField] private Transform? _inner;
        [SerializeField] private float _phase;
        private Vector3 _outerBase = Vector3.one, _innerBase = Vector3.one;

        public void Init(Transform outer, Transform inner, float phase)
        {
            _outer = outer; _inner = inner; _phase = phase;
            _outerBase = outer.localScale; _innerBase = inner.localScale;
        }

        private void OnEnable()
        {
            if (_outer != null) _outerBase = _outer.localScale;
            if (_inner != null) _innerBase = _inner.localScale;
        }

        public void Flicker(float t)
        {
            float sy = 1f + 0.16f * Mathf.Sin(t * 9.2f + _phase);
            float sxz = 1f + 0.08f * Mathf.Sin(t * 7.4f + _phase * 1.6f);
            if (_outer != null) _outer.localScale = Vector3.Scale(_outerBase, new Vector3(sxz, sy, sxz));
            if (_inner != null) _inner.localScale = Vector3.Scale(_innerBase, new Vector3(sxz, sy * 1.1f, sxz));
        }
    }

    /// <summary>Slowly spins a prop about +Y (shrine crystal). Edit-mode-safe.</summary>
    [ExecuteAlways]
    public sealed class DungeonSpin : MonoBehaviour
    {
        public float DegPerSec = 40f;
        private void Update()
        {
            float t = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            transform.localRotation = Quaternion.Euler(0f, t * DegPerSec, 0f);
        }
    }
}
