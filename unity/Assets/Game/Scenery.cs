#nullable enable
using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Procedural low-poly set dressing, now PER ZONE (roadmap 4, prop-shape swaps):
    /// each ZoneDef.PropSet scatters its own faceted prop family — pines/rocks/bushes
    /// in the forest, broken columns and gravestones in the ruins, cacti in the dunes,
    /// ice shards on the tundra, glow-shrooms in the hollow... All meshes are generated
    /// in code and FLAT-shaded (the Tunic look); materials bake the zone palette at
    /// build time, so a zone change simply rebuilds the scatter (rare — every ~10
    /// stages — and cheap). Deterministic per prop set, so layouts are stable across
    /// runs/screenshots. Pure renderer-side; no GameCore touch.
    /// </summary>
    public static class Scenery
    {
        private const float ClearRadius = 10f; // keep props off the party's centre spawn

        private static GameConfig _cfg = null!;
        private static GameObject? _root;
        private static string _builtSet = "";
        private static ArenaLayout? _arena; // current zone's layout; scatter is clamped inside it (null = open field)

        public static void Build(GameConfig cfg)
        {
            _cfg = cfg;
            _builtSet = "";
            _arena = null;
            Rebuild("forest", null, null); // pre-session default; ZoneDress re-syncs on play
        }

        /// <summary>Zone reskin: rebuild the scatter when the zone's prop family OR its arena layout
        /// changes. Props only land where <paramref name="arena"/> contains them (so nothing floats over
        /// the shore/void) and are planted at that spot's terrace height. (Called by ZoneDress on stage/
        /// floor change — a no-op within the same zone AND arena.)</summary>
        public static void SetZone(ZoneDef? zone, ArenaLayout? arena = null)
        {
            if (_cfg == null) return;
            string set = string.IsNullOrEmpty(zone?.PropSet) ? "forest" : zone!.PropSet;
            string arenaId = arena?.Id ?? "";
            if (set == _builtSet && arenaId == (_arena?.Id ?? "")) return;
            _arena = arena;
            Rebuild(set, zone, arena);
        }

        // --- scatter -------------------------------------------------------------

        private static void Rebuild(string set, ZoneDef? zone, ArenaLayout? arena)
        {
            if (_root != null) Object.Destroy(_root);
            _root = new GameObject("Scenery");
            _builtSet = set;
            _arena = arena;

            float halfW = (float)_cfg.Balance.MapHalfWidth - 2f;
            float halfD = (float)_cfg.Balance.MapHalfDepth - 2f;
            int seed = 1337;
            foreach (char c in set) seed = seed * 31 + c; // stable per-set layout
            var rng = new System.Random(seed);

            var t = _root.transform;
            var accent = zone != null
                ? new Color((float)zone.AccentR, (float)zone.AccentG, (float)zone.AccentB)
                : new Color(0.44f, 0.62f, 0.30f);

            switch (set)
            {
                case "ruins":
                    Scatter(rng, 70, halfW, halfD, p => PlaceColumn(t, rng, p, Stone(), StoneDark()));
                    Scatter(rng, 140, halfW, halfD, p => PlaceGravestone(t, rng, p, Stone()));
                    Scatter(rng, 200, halfW, halfD, p => PlaceRock(t, rng, p, StoneDark(), 0.4f, 1.0f));
                    break;
                case "swamp":
                    Scatter(rng, 90, halfW, halfD, p => PlaceDeadTree(t, rng, p, Bark()));
                    Scatter(rng, 150, halfW, halfD, p => PlaceRock(t, rng, p, MossyRock(accent), 0.5f, 1.4f));
                    Scatter(rng, 260, halfW, halfD, p => PlaceTuft(t, rng, p, Foliage(accent, 0.10f), 0.5f, 1.0f));
                    break;
                case "desert":
                    Scatter(rng, 80, halfW, halfD, p => PlaceCactus(t, rng, p, Cactus()));
                    Scatter(rng, 120, halfW, halfD, p => PlaceShard(t, rng, p, Sandstone(), 0.8f, 1.8f, 0.35f));
                    Scatter(rng, 160, halfW, halfD, p => PlaceBushProp(t, rng, p, DeadShrub(), 0.4f, 0.8f));
                    break;
                case "tundra":
                    Scatter(rng, 100, halfW, halfD, p => PlaceTree(t, rng, p, Bark(), Snowy()));
                    Scatter(rng, 140, halfW, halfD, p => PlaceShard(t, rng, p, Ice(), 0.6f, 1.6f, 0.15f));
                    Scatter(rng, 160, halfW, halfD, p => PlaceRock(t, rng, p, IceRock(), 0.5f, 1.4f));
                    break;
                case "volcano":
                    Scatter(rng, 110, halfW, halfD, p => PlaceShardCluster(t, rng, p, Basalt(), 0.9f, 2.0f));
                    Scatter(rng, 180, halfW, halfD, p => PlaceRock(t, rng, p, Basalt(), 0.5f, 1.5f));
                    Scatter(rng, 220, halfW, halfD, p => PlaceRock(t, rng, p, Cinder(), 0.3f, 0.7f));
                    break;
                case "cavern":
                    Scatter(rng, 120, halfW, halfD, p => PlaceShard(t, rng, p, StoneDark(), 0.8f, 2.2f, 0.3f));
                    Scatter(rng, 160, halfW, halfD, p => PlaceRock(t, rng, p, StoneDark(), 0.5f, 1.4f));
                    Scatter(rng, 200, halfW, halfD, p => PlaceShroom(t, rng, p, Bark(), GlowCap()));
                    break;
                case "coast":
                    Scatter(rng, 80, halfW, halfD, p => PlaceDriftwood(t, rng, p, Bark()));
                    Scatter(rng, 160, halfW, halfD, p => PlaceRock(t, rng, p, Stone(), 0.5f, 1.5f));
                    Scatter(rng, 240, halfW, halfD, p => PlaceTuft(t, rng, p, BeachGrass(), 0.4f, 0.9f));
                    break;
                case "astral":
                    Scatter(rng, 70, halfW, halfD, p => PlaceObelisk(t, rng, p, Runestone(), AstralGlow()));
                    Scatter(rng, 130, halfW, halfD, p => PlaceShard(t, rng, p, AstralGlow(), 0.6f, 1.5f, 0.2f));
                    Scatter(rng, 160, halfW, halfD, p => PlaceRock(t, rng, p, Runestone(), 0.5f, 1.3f));
                    break;
                case "summit":
                    Scatter(rng, 80, halfW, halfD, p => PlaceColumn(t, rng, p, Marble(), Gold()));
                    Scatter(rng, 120, halfW, halfD, p => PlaceShard(t, rng, p, Gold(), 0.5f, 1.3f, 0.2f));
                    Scatter(rng, 180, halfW, halfD, p => PlaceBushProp(t, rng, p, Foliage(accent, 0.05f), 0.5f, 1.0f));
                    break;
                default: // forest — the original pine/rock/bush field
                    Scatter(rng, 110, halfW, halfD, p => PlaceTree(t, rng, p, Bark(), Foliage(accent, 0.06f)));
                    Scatter(rng, 180, halfW, halfD, p => PlaceRock(t, rng, p, MossyRock(accent), 0.5f, 1.6f));
                    Scatter(rng, 240, halfW, halfD, p => PlaceBushProp(t, rng, p, Foliage(accent, 0.05f), 0.5f, 1.1f));
                    break;
            }

            // 10.12c draw-call collapse: fold the whole scatter into a few static batches —
            // props never move (wind sway is a SHADER offset; the mask rides vertex alpha, so
            // it survives the world-transform bake — see BuildFlatMesh). Safe to run once per
            // rebuild: a zone change destroys this root wholesale, so a rebuilt root is always
            // fresh GameObjects, never an already-combined set. Materials are shared per look
            // (the cache above) — without that, combine had nothing to merge.
            StaticBatchingUtility.Combine(_root);
        }

        private static void Scatter(System.Random rng, int count, float halfW, float halfD,
                                    System.Action<Vector3> place)
        {
            // With an arena, scatter INSIDE its bounds (the layout is a compact place, ~±46u) and reject
            // points off the walkable union — a swamp prop over the water bay would break the diorama.
            // Props plant at their spot's terrace height so a knoll's rocks sit on the knoll, not in it.
            float rangeW = halfW, rangeZ = halfD;
            float cx = 0f, cz = 0f;
            if (_arena != null)
            {
                ArenaBounds(_arena, out float bx0, out float bz0, out float bx1, out float bz1);
                cx = (bx0 + bx1) * 0.5f; cz = (bz0 + bz1) * 0.5f;
                rangeW = (bx1 - bx0) * 0.5f; rangeZ = (bz1 - bz0) * 0.5f;
            }
            for (int i = 0; i < count; i++)
            {
                Vector3 p = default; bool ok = false;
                for (int t = 0; t < 12; t++)
                {
                    float x = cx + (float)((rng.NextDouble() * 2 - 1) * rangeW);
                    float z = cz + (float)((rng.NextDouble() * 2 - 1) * rangeZ);
                    if (x * x + z * z < ClearRadius * ClearRadius) continue;            // off the spawn bubble
                    if (_arena != null && !_arena.Contains(new Vec2(x, z))) continue;   // off the walkable union
                    float y = ArenaTerrain.HeightAt(_cfg, _arena, x, z);
                    p = new Vector3(x, y, z); ok = true; break;
                }
                if (!ok) continue; // couldn't find a valid spot in this budget — skip rather than float one
                place(p);
            }
        }

        /// <summary>XZ bounds of an arena's shapes (disc uses radius on both axes) — the scatter box.</summary>
        private static void ArenaBounds(ArenaLayout a, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
            foreach (var sh in a.Shapes)
            {
                float ex = (float)sh.HalfW;                                        // rect half-W or disc radius
                float ez = (float)(sh.Kind == ArenaShapeKind.Disc ? sh.HalfW : sh.HalfD);
                minX = Mathf.Min(minX, (float)sh.X - ex); maxX = Mathf.Max(maxX, (float)sh.X + ex);
                minZ = Mathf.Min(minZ, (float)sh.Y - ez); maxZ = Mathf.Max(maxZ, (float)sh.Y + ez);
            }
            if (minX > maxX) { minX = minZ = -1f; maxX = maxZ = 1f; }
        }

        // --- prop placers ----------------------------------------------------------

        private static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        private static void PlaceTree(Transform root, System.Random rng, Vector3 pos, Material trunk, Material foliage)
        {
            var go = Group(root, pos, Range(rng, 0f, 360f));
            float s = Range(rng, 0.8f, 2.0f);
            float th = Range(rng, 1.1f, 1.5f) * s;
            Spawn(go.transform, TrunkMesh(), trunk, Vector3.zero, new Vector3(s, th, s));
            Spawn(go.transform, CanopyMesh(), foliage, new Vector3(0f, th * 0.9f, 0f), new Vector3(1.5f * s, 1.7f * s, 1.5f * s));
            Spawn(go.transform, CanopyMesh(), foliage, new Vector3(0f, th * 0.9f + 1.0f * s, 0f), new Vector3(1.1f * s, 1.4f * s, 1.1f * s));
        }

        private static void PlaceRock(Transform root, System.Random rng, Vector3 pos, Material mat, float sMin, float sMax)
        {
            float s = Range(rng, sMin, sMax);
            Spawn(root, RockMesh(rng), mat, pos + new Vector3(0f, -0.2f * s, 0f),
                  new Vector3(s, s * Range(rng, 0.5f, 0.8f), s), Range(rng, 0f, 360f));
        }

        private static void PlaceBushProp(Transform root, System.Random rng, Vector3 pos, Material mat, float sMin, float sMax)
        {
            float s = Range(rng, sMin, sMax);
            Spawn(root, BushMesh(rng), mat, pos + new Vector3(0f, s * 0.45f, 0f),
                  new Vector3(s, s * 0.7f, s), Range(rng, 0f, 360f));
        }

        private static void PlaceShard(Transform root, System.Random rng, Vector3 pos, Material mat,
                                       float sMin, float sMax, float lean)
        {
            float s = Range(rng, sMin, sMax);
            var go = Group(root, pos, Range(rng, 0f, 360f));
            go.transform.localRotation *= Quaternion.Euler(Range(rng, -lean, lean) * 30f, 0f, Range(rng, -lean, lean) * 30f);
            Spawn(go.transform, ShardMesh(rng), mat, Vector3.zero, new Vector3(0.5f * s, 1.6f * s, 0.5f * s));
        }

        private static void PlaceShardCluster(Transform root, System.Random rng, Vector3 pos, Material mat, float sMin, float sMax)
        {
            var go = Group(root, pos, Range(rng, 0f, 360f));
            int n = rng.Next(2, 4);
            for (int i = 0; i < n; i++)
            {
                float s = Range(rng, sMin, sMax) * (i == 0 ? 1f : Range(rng, 0.4f, 0.7f));
                var off = new Vector3(Range(rng, -0.5f, 0.5f), 0f, Range(rng, -0.5f, 0.5f));
                Spawn(go.transform, ShardMesh(rng), mat, off, new Vector3(0.6f * s, 1.2f * s, 0.6f * s), Range(rng, 0f, 360f));
            }
        }

        private static void PlaceColumn(Transform root, System.Random rng, Vector3 pos, Material stone, Material trim)
        {
            var go = Group(root, pos, Range(rng, 0f, 360f));
            float s = Range(rng, 0.8f, 1.4f);
            int drums = rng.Next(2, 5); // broken at a random height
            float y = 0f;
            Spawn(go.transform, DrumMesh(), trim, Vector3.zero, new Vector3(0.62f * s, 0.14f * s, 0.62f * s)); // plinth
            y += 0.14f * s;
            for (int i = 0; i < drums; i++)
            {
                float h = Range(rng, 0.28f, 0.42f) * s;
                var off = new Vector3(Range(rng, -0.05f, 0.05f) * s, y, Range(rng, -0.05f, 0.05f) * s);
                Spawn(go.transform, DrumMesh(), stone, off, new Vector3(0.42f * s, h, 0.42f * s), Range(rng, 0f, 360f));
                y += h;
            }
        }

        private static void PlaceGravestone(Transform root, System.Random rng, Vector3 pos, Material mat)
        {
            float s = Range(rng, 0.6f, 1.1f);
            var go = Group(root, pos, Range(rng, 0f, 360f));
            go.transform.localRotation *= Quaternion.Euler(Range(rng, -9f, 9f), 0f, Range(rng, -9f, 9f)); // settled tilt
            Spawn(go.transform, SlabMesh(0.55f, 0.16f, 0.8f, 0.85f), mat, Vector3.zero, Vector3.one * s);
        }

        private static void PlaceDeadTree(Transform root, System.Random rng, Vector3 pos, Material bark)
        {
            var go = Group(root, pos, Range(rng, 0f, 360f));
            float s = Range(rng, 0.9f, 1.8f);
            float th = Range(rng, 1.4f, 2.0f) * s;
            Spawn(go.transform, TrunkMesh(), bark, Vector3.zero, new Vector3(s * 0.9f, th, s * 0.9f));
            int branches = rng.Next(2, 4); // bare crooked branches
            for (int i = 0; i < branches; i++)
            {
                float by = th * Range(rng, 0.55f, 0.95f);
                var b = Spawn(go.transform, SlabMesh(0.07f, 0.07f, 0.7f, 0.4f), bark,
                              new Vector3(0f, by, 0f), Vector3.one * s);
                b.transform.localRotation = Quaternion.Euler(Range(rng, 40f, 80f), Range(rng, 0f, 360f), 0f);
            }
        }

        private static void PlaceCactus(Transform root, System.Random rng, Vector3 pos, Material mat)
        {
            var go = Group(root, pos, Range(rng, 0f, 360f));
            float s = Range(rng, 0.7f, 1.5f);
            float h = Range(rng, 1.0f, 1.8f) * s;
            Spawn(go.transform, DrumMesh(), mat, Vector3.zero, new Vector3(0.22f * s, h, 0.22f * s));
            int arms = rng.Next(1, 3);
            for (int i = 0; i < arms; i++)
            {
                int side = i == 0 ? 1 : -1;
                float ay = h * Range(rng, 0.35f, 0.6f);
                Spawn(go.transform, SlabMesh(0.13f, 0.13f, 0.32f, 1f), mat,
                      new Vector3(side * 0.28f * s, ay, 0f), Vector3.one * s); // elbow out
                Spawn(go.transform, DrumMesh(), mat,
                      new Vector3(side * 0.38f * s, ay, 0f), new Vector3(0.15f * s, 0.5f * s, 0.15f * s)); // arm up
            }
        }

        private static void PlaceShroom(Transform root, System.Random rng, Vector3 pos, Material stem, Material cap)
        {
            var go = Group(root, pos, Range(rng, 0f, 360f));
            float s = Range(rng, 0.35f, 0.9f);
            float h = Range(rng, 0.4f, 0.7f) * s;
            Spawn(go.transform, DrumMesh(), stem, Vector3.zero, new Vector3(0.10f * s, h, 0.10f * s));
            Spawn(go.transform, RockMesh(rng), cap, new Vector3(0f, h + 0.05f * s, 0f),
                  new Vector3(0.42f * s, 0.22f * s, 0.42f * s));
        }

        private static void PlaceDriftwood(Transform root, System.Random rng, Vector3 pos, Material bark)
        {
            var go = Group(root, pos, Range(rng, 0f, 360f));
            float s = Range(rng, 0.9f, 1.8f);
            var log = Spawn(go.transform, SlabMesh(0.16f, 0.16f, 1.3f, 0.7f), bark,
                            new Vector3(0f, 0.10f * s, 0f), Vector3.one * s);
            log.transform.localRotation = Quaternion.Euler(Range(rng, 80f, 100f), Range(rng, 0f, 360f), 0f); // fallen
        }

        private static void PlaceObelisk(Transform root, System.Random rng, Vector3 pos, Material stone, Material glow)
        {
            var go = Group(root, pos, Range(rng, 0f, 360f));
            float s = Range(rng, 0.8f, 1.5f);
            go.transform.localRotation *= Quaternion.Euler(Range(rng, -6f, 6f), 0f, Range(rng, -6f, 6f));
            Spawn(go.transform, SlabMesh(0.34f, 0.34f, 1.5f, 0.55f), stone, Vector3.zero, Vector3.one * s);
            Spawn(go.transform, SlabMesh(0.10f, 0.06f, 0.5f, 0.8f), glow,
                  new Vector3(0f, 0.45f * s, -0.16f * s), Vector3.one * s); // rune strip on a face
        }

        private static void PlaceTuft(Transform root, System.Random rng, Vector3 pos, Material mat, float sMin, float sMax)
        {
            float s = Range(rng, sMin, sMax);
            Spawn(root, TuftMesh(rng), mat, pos, Vector3.one * s, Range(rng, 0f, 360f));
        }

        // --- materials (CACHED per look, 10.12c) ---------------------------------------
        // The placer lambdas call these helpers per PROP, which used to mint a fresh Material
        // per renderer (~1,500 unique instances per zone build): nothing could batch — every
        // draw was its own material — and each rebuild leaked the previous set. One shared
        // instance per parameter tuple means a whole prop family batches (and static-combines)
        // into a handful of draws. Zone palettes still bake per zone: the accent is part of
        // the key, so a different zone's foliage gets its own cached material.

        private static Material Foliage(Color accent, float wind)
            => Tun(accent, accent * 0.5f, -0.3f, 0.5f, 0.30f, wind);
        private static Material Snowy()
            => Tun(new Color(0.90f, 0.93f, 0.97f), new Color(0.55f, 0.65f, 0.78f), -0.3f, 0.5f, 0.30f, 0.04f);
        private static Material DeadShrub()
            => Tun(new Color(0.52f, 0.42f, 0.26f), new Color(0.32f, 0.25f, 0.16f), -0.3f, 0.5f, 0.30f, 0.05f);
        private static Material BeachGrass()
            => Tun(new Color(0.72f, 0.72f, 0.50f), new Color(0.48f, 0.48f, 0.32f), -0.3f, 0.5f, 0.30f, 0.10f);
        private static Material Bark()
            => Tun(new Color(0.34f, 0.30f, 0.18f), new Color(0.27f, 0.20f, 0.13f), 0.55f, 0.90f, 0.30f, 0f);
        private static Material MossyRock(Color accent)
            => Tun(Color.Lerp(new Color(0.40f, 0.50f, 0.28f), accent, 0.4f), new Color(0.55f, 0.56f, 0.57f), 0.55f, 0.85f, 0.35f, 0f);
        private static Material Stone()
            => Tun(new Color(0.60f, 0.59f, 0.56f), new Color(0.48f, 0.47f, 0.45f), 0.3f, 0.8f, 0.35f, 0f);
        private static Material StoneDark()
            => Tun(new Color(0.42f, 0.42f, 0.44f), new Color(0.32f, 0.32f, 0.34f), 0.3f, 0.8f, 0.35f, 0f);
        private static Material Sandstone()
            => Tun(new Color(0.78f, 0.64f, 0.40f), new Color(0.60f, 0.47f, 0.28f), 0.3f, 0.8f, 0.35f, 0f);
        private static Material Cactus()
            => Tun(new Color(0.32f, 0.55f, 0.30f), new Color(0.22f, 0.40f, 0.22f), -0.3f, 0.5f, 0.30f, 0f);
        private static Material Ice()
            => Tun(new Color(0.80f, 0.90f, 0.98f), new Color(0.55f, 0.72f, 0.90f), -0.3f, 0.6f, 0.30f, 0f);
        private static Material IceRock()
            => Tun(new Color(0.86f, 0.90f, 0.95f), new Color(0.58f, 0.66f, 0.78f), 0.55f, 0.85f, 0.35f, 0f);
        private static Material Basalt()
            => Tun(new Color(0.26f, 0.23f, 0.22f), new Color(0.17f, 0.15f, 0.14f), 0.3f, 0.8f, 0.4f, 0f);
        private static Material Cinder()
            => Tun(new Color(0.50f, 0.30f, 0.20f), new Color(0.30f, 0.18f, 0.12f), 0.3f, 0.8f, 0.35f, 0f);
        private static Material GlowCap()
            => Tun(new Color(0.70f, 0.50f, 0.98f), new Color(0.40f, 0.28f, 0.60f), -0.3f, 0.5f, 0.25f, 0f);
        private static Material Runestone()
            => Tun(new Color(0.48f, 0.44f, 0.60f), new Color(0.34f, 0.31f, 0.44f), 0.3f, 0.8f, 0.35f, 0f);
        private static Material AstralGlow()
            => Tun(new Color(0.75f, 0.62f, 1.00f), new Color(0.50f, 0.42f, 0.72f), -0.3f, 0.6f, 0.25f, 0f);
        private static Material Marble()
            => Tun(new Color(0.88f, 0.86f, 0.80f), new Color(0.68f, 0.66f, 0.60f), 0.3f, 0.8f, 0.30f, 0f);
        private static Material Gold()
            => Tun(new Color(0.90f, 0.76f, 0.35f), new Color(0.66f, 0.52f, 0.22f), 0.3f, 0.8f, 0.30f, 0f);

        private static readonly Dictionary<(Color, Color, float, float, float, float), Material> _mats
            = new Dictionary<(Color, Color, float, float, float, float), Material>();

        /// <summary>Build (or fetch the cached) TunicSurface material for a look (normal-driven
        /// top/side colour + inked facet edges + crisp light). Falls back to a matte URP/Lit if
        /// the shader is missing. Cached per parameter tuple — see the section note above.</summary>
        private static Material Tun(Color top, Color side, float slopeLo, float slopeHi, float edgeDark, float wind)
        {
            var key = (top, side, slopeLo, slopeHi, edgeDark, wind);
            if (_mats.TryGetValue(key, out var cached) && cached != null) return cached; // Unity-null check: survives teardown

            var sh = Shader.Find("IdleGame/TunicSurface");
            if (sh == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var fb = new Material(lit) { color = top };
                Bootstrap.MakeMatte(fb);
                fb.enableInstancing = true;
                _mats[key] = fb;
                return fb;
            }
            var m = new Material(sh);
            m.SetColor("_TopColor", top);
            m.SetColor("_SideColor", side);
            m.SetFloat("_SlopeLo", slopeLo);
            m.SetFloat("_SlopeHi", slopeHi);
            m.SetFloat("_EdgeDark", edgeDark);
            m.SetFloat("_EdgeSharp", 6f);
            m.SetFloat("_ShadowImpact", 0.7f);
            m.SetFloat("_WindStrength", wind);
            m.enableInstancing = true; // identical mesh+material -> one batch
            _mats[key] = m;
            return m;
        }

        // --- shared meshes (cached per rebuild) --------------------------------------

        private static Mesh? _trunk, _canopy, _drum;
        private static readonly List<Mesh> _rocks = new List<Mesh>();
        private static readonly List<Mesh> _bushes = new List<Mesh>();
        private static readonly List<Mesh> _shards = new List<Mesh>();
        private static readonly List<Mesh> _tufts = new List<Mesh>();
        private static readonly Dictionary<(float, float, float, float), Mesh> _slabs
            = new Dictionary<(float, float, float, float), Mesh>();

        private static Mesh TrunkMesh() => _trunk ??= BuildFlatMesh(FrustumData(5, 0.16f, 0.12f, 1f));
        private static Mesh CanopyMesh() => _canopy ??= BuildFlatMesh(FrustumData(6, 1f, 0f, 1.5f));
        private static Mesh DrumMesh() => _drum ??= BuildFlatMesh(FrustumData(7, 1f, 0.92f, 1f));

        private static Mesh RockMesh(System.Random rng)
        {
            if (_rocks.Count < 6) _rocks.Add(BuildRock(rng));
            return _rocks[rng.Next(_rocks.Count)];
        }

        private static Mesh BushMesh(System.Random rng)
        {
            if (_bushes.Count < 6) _bushes.Add(BuildBush(rng));
            return _bushes[rng.Next(_bushes.Count)];
        }

        private static Mesh ShardMesh(System.Random rng)
        {
            if (_shards.Count < 6)
            {
                // a jittered crystal spike: cone with irregular base ring
                var (verts, tris) = FrustumData(rng.Next(4, 6), 1f, 0.04f, 1f);
                for (int i = 0; i < verts.Length; i++)
                {
                    var v = verts[i];
                    float f = 0.75f + 0.5f * Mathf.PerlinNoise(v.x * 2.1f + _shards.Count * 9f, v.z * 2.1f);
                    verts[i] = new Vector3(v.x * f, v.y, v.z * f);
                }
                _shards.Add(BuildFlatMesh((verts, tris)));
            }
            return _shards[rng.Next(_shards.Count)];
        }

        private static Mesh TuftMesh(System.Random rng)
        {
            if (_tufts.Count < 4)
            {
                // 4-6 thin leaning blades merged into one flat-shaded clump
                var verts = new List<Vector3>();
                var tris = new List<int>();
                int blades = 4 + rng.Next(3);
                for (int b = 0; b < blades; b++)
                {
                    var (bv, bt) = FrustumData(3, 0.05f, 0.005f, Range(rng, 0.35f, 0.7f));
                    var off = new Vector3(Range(rng, -0.12f, 0.12f), 0f, Range(rng, -0.12f, 0.12f));
                    var tilt = Quaternion.Euler(Range(rng, -18f, 18f), Range(rng, 0f, 360f), Range(rng, -18f, 18f));
                    int baseIdx = verts.Count;
                    foreach (var v in bv) verts.Add(tilt * v + off);
                    foreach (var i in bt) tris.Add(baseIdx + i);
                }
                _tufts.Add(BuildFlatMesh((verts.ToArray(), tris.ToArray())));
            }
            return _tufts[rng.Next(_tufts.Count)];
        }

        private static Mesh SlabMesh(float w, float d, float h, float taperTop)
        {
            var key = (w, d, h, taperTop);
            if (_slabs.TryGetValue(key, out var cached)) return cached;
            var verts = new Vector3[8];
            int i = 0;
            for (int zi = 0; zi <= 1; zi++)
                for (int yi = 0; yi <= 1; yi++)
                    for (int xi = 0; xi <= 1; xi++)
                    {
                        float t = zi == 1 ? taperTop : 1f;
                        // z is height here (built lying, spawned upright via rotation-free y-up below)
                        verts[i++] = new Vector3((xi - 0.5f) * w * t, zi * h, (yi - 0.5f) * d * t);
                    }
            int[] tris =
            {
                0,1,2, 1,3,2,  4,6,5, 5,6,7,  // bottom, top
                0,4,1, 1,4,5,  2,3,6, 3,7,6,  // front, back
                0,2,4, 2,6,4,  1,5,3, 3,5,7,  // left, right
            };
            var m = BuildFlatMesh((verts, tris));
            _slabs[key] = m;
            return m;
        }

        private static GameObject Group(Transform root, Vector3 pos, float yRot)
        {
            var go = new GameObject("prop");
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            return go;
        }

        // Detail props shorter than this (world units) cast no shadow: a grass tuft or pebble
        // contributes nothing readable to the shadow map, but every caster re-draws once per
        // cascade — at ~1,500 scenery renderers the shadow pass dwarfed the camera pass (10.12c).
        private const float ShadowMinHeight = 0.6f;

        private static GameObject Spawn(Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3 localScale, float yRot = 0f)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            // Per-PART policy (this is the one spawn choke point): a tree keeps its shadow via
            // its tall trunk/canopy parts even if a small accent part opts out. Height comes from
            // MESH bounds × scale, NOT renderer.bounds — renderer bounds are zero until first
            // render, so a boot-time build would gate every prop off (Play-caught).
            if (mesh.bounds.size.y * localScale.y < ShadowMinHeight)
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // --- procedural meshes (flat-shaded) -----------------------------------

        /// <summary>A leafy bush: a cluster of 3–5 overlapping rounded spheres merged into one
        /// flat-shaded mesh, so it reads like foliage clumps rather than a single blob.</summary>
        private static Mesh BuildBush(System.Random rng)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int lumps = 3 + rng.Next(2);
            for (int k = 0; k < lumps; k++)
            {
                float lr = Range(rng, 0.5f, 0.9f);
                var c = new Vector3(Range(rng, -0.5f, 0.5f), Range(rng, 0f, 0.55f), Range(rng, -0.5f, 0.5f));
                var (sv, st) = SphereData(2, 5, lr);
                int off = verts.Count;
                for (int i = 0; i < sv.Length; i++) verts.Add(sv[i] + c);
                for (int i = 0; i < st.Length; i++) tris.Add(st[i] + off);
            }
            return BuildFlatMesh((verts.ToArray(), tris.ToArray()));
        }

        /// <summary>A low-poly rock: a coarse sphere displaced by smooth noise.</summary>
        private static Mesh BuildRock(System.Random rng)
        {
            var (verts, tris) = SphereData(3, 6, 1f);
            float ox = (float)rng.NextDouble() * 100f, oz = (float)rng.NextDouble() * 100f;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                float f = 0.55f + 0.8f * Mathf.PerlinNoise(v.x * 1.6f + ox, v.z * 1.6f + oz);
                verts[i] = v * f;
            }
            return BuildFlatMesh((verts, tris));
        }

        /// <summary>Flat-shade: give every triangle its own 3 vertices (hard per-face normals),
        /// oriented outward from the centroid so winding never matters (star-convex shapes).</summary>
        private static Mesh BuildFlatMesh((Vector3[] verts, int[] tris) data)
        {
            var (verts, tris) = data;
            Vector3 c = Vector3.zero;
            for (int i = 0; i < verts.Length; i++) c += verts[i];
            c /= Mathf.Max(1, verts.Length);

            var fv = new Vector3[tris.Length];
            var ft = new int[tris.Length];
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], d = verts[tris[i + 2]];
                Vector3 n = Vector3.Cross(b - a, d - a);
                bool outward = Vector3.Dot(n, (a + b + d) / 3f - c) >= 0f;
                fv[i] = a;
                fv[i + 1] = outward ? b : d;
                fv[i + 2] = outward ? d : b;
                ft[i] = i; ft[i + 1] = i + 1; ft[i + 2] = i + 2;
            }
            var m = new Mesh { vertices = fv, triangles = ft };
            m.RecalculateNormals();
            m.RecalculateBounds();
            // TunicSurface multiplies albedo by vertex colour; stamp white (no-op tint). The
            // ALPHA carries the wind-sway mask — saturate(mesh-space height), exactly what the
            // shader used to derive from posOS.y at runtime. Baked into the vertex so it
            // survives StaticBatchingUtility folding world transforms into a combined mesh
            // (post-combine, posOS.y is combined-root space and would un-plant every base).
            var cols = new Color[fv.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(fv[i].y));
            m.colors = cols;
            return m;
        }

        /// <summary>UV sphere data (shared verts; winding fixed up in BuildFlatMesh).</summary>
        private static (Vector3[], int[]) SphereData(int lat, int lon, float r)
        {
            var v = new List<Vector3>();
            for (int i = 0; i <= lat; i++)
            {
                float theta = i / (float)lat * Mathf.PI;
                float y = Mathf.Cos(theta) * r, rr = Mathf.Sin(theta) * r;
                for (int j = 0; j <= lon; j++)
                {
                    float phi = j / (float)lon * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(phi) * rr, y, Mathf.Sin(phi) * rr));
                }
            }
            var t = new List<int>();
            int stride = lon + 1;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * stride + j;
                    t.Add(a); t.Add(a + stride); t.Add(a + 1);
                    t.Add(a + 1); t.Add(a + stride); t.Add(a + stride + 1);
                }
            return (v.ToArray(), t.ToArray());
        }

        /// <summary>Frustum/cone data: bottom + top rings (rTop 0 => cone) with end caps.</summary>
        private static (Vector3[], int[]) FrustumData(int segs, float rBottom, float rTop, float h)
        {
            var v = new List<Vector3>();
            for (int i = 0; i < segs; i++)
            {
                float a = i / (float)segs * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * rBottom, 0f, Mathf.Sin(a) * rBottom));
            }
            for (int i = 0; i < segs; i++)
            {
                float a = i / (float)segs * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * rTop, h, Mathf.Sin(a) * rTop));
            }
            int bc = v.Count; v.Add(new Vector3(0f, 0f, 0f));
            int tc = v.Count; v.Add(new Vector3(0f, h, 0f));

            var t = new List<int>();
            for (int i = 0; i < segs; i++)
            {
                int n = (i + 1) % segs, b0 = i, b1 = n, t0 = segs + i, t1 = segs + n;
                t.Add(b0); t.Add(b1); t.Add(t1);
                t.Add(b0); t.Add(t1); t.Add(t0);
                t.Add(bc); t.Add(b0); t.Add(b1);
                t.Add(tc); t.Add(t1); t.Add(t0);
            }
            return (v.ToArray(), t.ToArray());
        }
    }
}
