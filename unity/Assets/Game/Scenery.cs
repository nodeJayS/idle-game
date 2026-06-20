using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Procedural low-poly set dressing (Phase 2 art pass): scatters faceted rocks, pine
    /// trees, and bushes across the field so the world reads as an inhabited diorama
    /// instead of an empty plane. Pure renderer-side (no GameCore touch) and deterministic
    /// via a fixed seed, so the layout is stable across runs/screenshots.
    ///
    /// Meshes are generated once and FLAT-shaded for the stylised faceted look; instances
    /// share a handful of materials (instancing on) so hundreds of props stay cheap. This
    /// is the code-built stand-in until/if we bring in Blender or pack assets.
    /// </summary>
    public static class Scenery
    {
        // --- tunables (scatter density + clear zone around the party's start) ---
        private const int TreeCount = 110;
        private const int RockCount = 180;
        private const int BushCount = 240;
        private const float ClearRadius = 10f; // keep props off the party's centre spawn
        private const int Seed = 1337;

        private static Material _trunkMat = null!;
        private static Material[] _foliageMats = null!;
        private static Material[] _rockMats = null!;
        private static Material[] _bushMats = null!;

        public static void Build(GameConfig cfg)
        {
            var root = new GameObject("Scenery").transform;
            float halfW = (float)cfg.Balance.MapHalfWidth - 2f;
            float halfD = (float)cfg.Balance.MapHalfDepth - 2f;
            var rng = new System.Random(Seed);

            EnsureMaterials();

            // Cache shared meshes — instances only vary by transform + material. LOW segment
            // counts + flat shading give crisp faceted Tunic silhouettes (not soft round blobs).
            var trunkMesh = BuildFlatMesh(FrustumData(5, 0.16f, 0.12f, 1f));  // 5-sided angular trunk
            var canopyMesh = BuildFlatMesh(FrustumData(6, 1f, 0f, 1.5f));     // 6-sided faceted cone
            var bushMeshes = new Mesh[6];
            for (int i = 0; i < bushMeshes.Length; i++) bushMeshes[i] = BuildBush(rng);
            var rockMeshes = new Mesh[6];
            for (int i = 0; i < rockMeshes.Length; i++) rockMeshes[i] = BuildRock(rng);

            for (int i = 0; i < TreeCount; i++) PlaceTree(root, rng, halfW, halfD, trunkMesh, canopyMesh);
            for (int i = 0; i < RockCount; i++) PlaceRock(root, rng, halfW, halfD, rockMeshes[rng.Next(rockMeshes.Length)]);
            for (int i = 0; i < BushCount; i++) PlaceBush(root, rng, halfW, halfD, bushMeshes[rng.Next(bushMeshes.Length)]);
        }

        // --- placement ---------------------------------------------------------

        private static Vector3 RandPos(System.Random rng, float halfW, float halfD)
        {
            for (int t = 0; t < 8; t++)
            {
                float x = (float)((rng.NextDouble() * 2 - 1) * halfW);
                float z = (float)((rng.NextDouble() * 2 - 1) * halfD);
                if (x * x + z * z >= ClearRadius * ClearRadius) return new Vector3(x, 0f, z);
            }
            return new Vector3(halfW * 0.5f, 0f, halfD * 0.5f); // fallback, off the spawn
        }

        private static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        private static void PlaceTree(Transform root, System.Random rng, float halfW, float halfD, Mesh trunk, Mesh canopy)
        {
            var go = new GameObject("tree");
            go.transform.SetParent(root, false);
            go.transform.localPosition = RandPos(rng, halfW, halfD);
            go.transform.localRotation = Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);

            float s = Range(rng, 0.8f, 2.0f);           // overall size variety
            float th = Range(rng, 1.1f, 1.5f) * s;       // trunk height
            var foliage = _foliageMats[rng.Next(_foliageMats.Length)];

            // trunk on the parent, two stacked cone canopies = a chunky pine
            Spawn(go.transform, trunk, _trunkMat, Vector3.zero, new Vector3(s, th, s));
            Spawn(go.transform, canopy, foliage, new Vector3(0f, th * 0.9f, 0f), new Vector3(1.5f * s, 1.7f * s, 1.5f * s));
            Spawn(go.transform, canopy, foliage, new Vector3(0f, th * 0.9f + 1.0f * s, 0f), new Vector3(1.1f * s, 1.4f * s, 1.1f * s));
        }

        private static void PlaceRock(Transform root, System.Random rng, float halfW, float halfD, Mesh rock)
        {
            float s = Range(rng, 0.5f, 1.6f);
            // Squashed + sunk slightly into the ground so it reads as embedded, not a ball.
            Spawn(root, rock, _rockMats[rng.Next(_rockMats.Length)],
                  RandPos(rng, halfW, halfD) + new Vector3(0f, -0.2f * s, 0f),
                  new Vector3(s, s * Range(rng, 0.5f, 0.8f), s), Range(rng, 0f, 360f));
        }

        private static void PlaceBush(Transform root, System.Random rng, float halfW, float halfD, Mesh bush)
        {
            float s = Range(rng, 0.5f, 1.1f);
            // Centre lifted so the squashed sphere sits on the ground rather than half-buried.
            Spawn(root, bush, _bushMats[rng.Next(_bushMats.Length)],
                  RandPos(rng, halfW, halfD) + new Vector3(0f, s * 0.45f, 0f),
                  new Vector3(s, s * 0.7f, s), Range(rng, 0f, 360f));
        }

        private static void Spawn(Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3 localScale, float yRot = 0f)
        {
            var go = new GameObject("prop");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // --- materials ---------------------------------------------------------

        private static void EnsureMaterials()
        {
            if (_trunkMat != null) return;
            // TunicSurface paints by facet normal: top colour on up-facing facets, side colour on
            // the slopes/sides. Rocks get MOSS on top + stone sides; foliage a lit top fading to a
            // dark underside; trunks read as bark. (slopeLo/Hi set where the top colour gives way.)
            _trunkMat = Tun(new Color(0.34f, 0.30f, 0.18f), new Color(0.27f, 0.20f, 0.13f), 0.55f, 0.90f, 0.30f, 0f);
            // Foliage/bush: lit top fading only on the UNDERSIDE (slopeLo negative so the sides
            // still catch top-green) — keeps leafy blobs bright, not dark.
            _foliageMats = new[]
            {
                Tun(new Color(0.44f, 0.62f, 0.30f), new Color(0.19f, 0.33f, 0.18f), -0.3f, 0.5f, 0.30f, 0.06f),
                Tun(new Color(0.48f, 0.66f, 0.32f), new Color(0.22f, 0.37f, 0.20f), -0.3f, 0.5f, 0.30f, 0.06f),
                Tun(new Color(0.40f, 0.56f, 0.30f), new Color(0.17f, 0.30f, 0.18f), -0.3f, 0.5f, 0.30f, 0.06f),
            };
            // Rocks: moss only on the near-flat tops (high slopeLo), stone on the sides.
            _rockMats = new[]
            {
                Tun(new Color(0.40f, 0.50f, 0.28f), new Color(0.56f, 0.57f, 0.58f), 0.55f, 0.85f, 0.35f, 0f),
                Tun(new Color(0.36f, 0.46f, 0.26f), new Color(0.50f, 0.51f, 0.52f), 0.55f, 0.85f, 0.35f, 0f),
            };
            _bushMats = new[]
            {
                Tun(new Color(0.42f, 0.60f, 0.30f), new Color(0.18f, 0.32f, 0.18f), -0.3f, 0.5f, 0.30f, 0.05f),
                Tun(new Color(0.38f, 0.54f, 0.28f), new Color(0.16f, 0.28f, 0.17f), -0.3f, 0.5f, 0.30f, 0.05f),
            };
        }

        /// <summary>Build a TunicSurface material (normal-driven top/side colour + inked facet
        /// edges + crisp light). Falls back to a matte URP/Lit if the shader is missing.</summary>
        private static Material Tun(Color top, Color side, float slopeLo, float slopeHi, float edgeDark, float wind)
        {
            var sh = Shader.Find("IdleGame/TunicSurface");
            if (sh == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var fb = new Material(lit) { color = top };
                Bootstrap.MakeMatte(fb);
                fb.enableInstancing = true;
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
            return m;
        }

        // --- procedural meshes (flat-shaded) -----------------------------------

        /// <summary>A leafy bush: a cluster of 3–5 overlapping rounded spheres merged into one
        /// flat-shaded mesh, so it reads like foliage clumps rather than a single blob.
        /// Interior faces are hidden by the overlap, so they cost nothing visually.</summary>
        private static Mesh BuildBush(System.Random rng)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int lumps = 3 + rng.Next(2); // 3..4 chunky lumps
            for (int k = 0; k < lumps; k++)
            {
                float lr = Range(rng, 0.5f, 0.9f);
                var c = new Vector3(Range(rng, -0.5f, 0.5f), Range(rng, 0f, 0.55f), Range(rng, -0.5f, 0.5f));
                var (sv, st) = SphereData(2, 5, lr); // low-poly = angular leafy clump
                int off = verts.Count;
                for (int i = 0; i < sv.Length; i++) verts.Add(sv[i] + c);
                for (int i = 0; i < st.Length; i++) tris.Add(st[i] + off);
            }
            return BuildFlatMesh((verts.ToArray(), tris.ToArray()));
        }

        /// <summary>A low-poly rock: a coarse sphere displaced by smooth noise. Displacement
        /// keys off the vertex direction (not an index) so coincident pole verts move together
        /// and the shell stays closed; a per-rock noise offset gives each a distinct silhouette.</summary>
        private static Mesh BuildRock(System.Random rng)
        {
            var (verts, tris) = SphereData(3, 6, 1f); // coarse = angular faceted boulder
            float ox = (float)rng.NextDouble() * 100f, oz = (float)rng.NextDouble() * 100f;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                float f = 0.55f + 0.8f * Mathf.PerlinNoise(v.x * 1.6f + ox, v.z * 1.6f + oz); // stronger jitter
                verts[i] = v * f;
            }
            return BuildFlatMesh((verts, tris));
        }

        /// <summary>Flat-shade: give every triangle its own 3 vertices (so RecalculateNormals
        /// produces hard per-face normals = facets), and orient each face outward relative to
        /// the mesh centroid so winding never matters (these shapes are star-convex).</summary>
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
            // TunicSurface multiplies albedo by vertex colour; an absent colour channel can read
            // as black on some GPUs, so stamp white (tint = no-op) on every generated prop mesh.
            var cols = new Color[fv.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
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

        /// <summary>Frustum/cone data: bottom + top rings (rTop 0 => cone) with end caps.
        /// Degenerate apex tris are harmless; winding is fixed up in BuildFlatMesh.</summary>
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
                t.Add(b0); t.Add(b1); t.Add(t1);   // side
                t.Add(b0); t.Add(t1); t.Add(t0);
                t.Add(bc); t.Add(b0); t.Add(b1);   // bottom cap
                t.Add(tc); t.Add(t1); t.Add(t0);   // top cap
            }
            return (v.ToArray(), t.ToArray());
        }
    }
}
