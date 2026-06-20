using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The faceted, flat-shaded, vertex-coloured ground (Tunic art pivot, Phase 3). Replaces the
    /// old textured plane: NO texture — colour is flat vertex colours, and the look comes from
    /// the TunicSurface shader (grass on up-faces) + a drifting light cookie + per-vertex grass
    /// mottle. Gentle height noise (±~0.3u) breaks the surface into facets that catch the light
    /// differently without lifting it enough to clip the units (the sim plants everything at y=0).
    /// Tunic's flat fields are mostly uniform grass; dirt/stone shows on the props & cliffs.
    /// </summary>
    public static class Ground
    {
        private const float MarginPad = 60f;   // ground extends this far past the roam region
        private const float CellSize  = 6f;    // facet size — smaller = finer facets, more verts
        private const float Amplitude = 0.3f;  // max height deviation (keep small: units sit at y=0)

        private static Material _mat = null!;

        public static void Build(GameConfig cfg)
        {
            float halfX = (float)cfg.Balance.MapHalfWidth + MarginPad;
            float halfZ = (float)cfg.Balance.MapHalfDepth + MarginPad;
            int nx = Mathf.Max(1, Mathf.RoundToInt(2f * halfX / CellSize));
            int nz = Mathf.Max(1, Mathf.RoundToInt(2f * halfZ / CellSize));
            float dx = 2f * halfX / nx, dz = 2f * halfZ / nz;

            var verts = new List<Vector3>(nx * nz * 6);
            var cols = new List<Color>(nx * nz * 6);
            var tris = new List<int>(nx * nz * 6);

            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float x0 = -halfX + ix * dx, x1 = x0 + dx;
                    float z0 = -halfZ + iz * dz, z1 = z0 + dz;
                    var p00 = new Vector3(x0, H(x0, z0), z0);
                    var p10 = new Vector3(x1, H(x1, z0), z0);
                    var p01 = new Vector3(x0, H(x0, z1), z1);
                    var p11 = new Vector3(x1, H(x1, z1), z1);
                    // Both tris wound for an upward (+Y) face; flat-shaded (own verts) = facets.
                    AddTri(verts, cols, tris, p00, p01, p11);
                    AddTri(verts, cols, tris, p00, p11, p10);
                }

            var mesh = new Mesh { name = "TunicGround", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Ground");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = EnsureMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // ground only receives
        }

        private static void AddTri(List<Vector3> verts, List<Color> cols, List<int> tris,
                                   Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            cols.Add(GrassTint(a.x, a.z)); cols.Add(GrassTint(b.x, b.z)); cols.Add(GrassTint(c.x, c.z));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>Gentle rolling height (±~Amplitude), two octaves of Perlin so it isn't a grid.</summary>
        private static float H(float x, float z)
        {
            float a = Mathf.PerlinNoise(x * 0.025f + 11f, z * 0.025f + 11f);
            float b = Mathf.PerlinNoise(x * 0.10f + 5f, z * 0.10f + 5f);
            return (a * 0.7f + b * 0.3f - 0.5f) * (Amplitude * 2f);
        }

        /// <summary>Flat vertex-colour grass mottle (replaces the old texture): a multiplier near
        /// 1.0 varying brightness + a faint yellow-green↔deep-green hue shift across the field.</summary>
        private static Color GrassTint(float x, float z)
        {
            float p = Mathf.PerlinNoise(x * 0.05f + 30f, z * 0.05f + 30f);  // broad patches
            float q = Mathf.PerlinNoise(x * 0.17f + 70f, z * 0.17f + 70f);  // fine break-up
            float br = Mathf.Lerp(0.84f, 1.04f, p * 0.7f + q * 0.3f);       // tighter, no over-bright
            float hue = Mathf.PerlinNoise(x * 0.03f + 90f, z * 0.03f + 90f);
            // Lean the hue toward green (cooler), never yellow, so it stays rich not "cheap".
            return new Color(br * Mathf.Lerp(0.92f, 1.0f, hue), br, br * Mathf.Lerp(1.04f, 0.96f, hue));
        }

        private static Material EnsureMaterial()
        {
            if (_mat != null) return _mat;
            var sh = Shader.Find("IdleGame/TunicSurface");
            if (sh == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _mat = new Material(lit) { color = new Color(0.40f, 0.56f, 0.30f) };
                Bootstrap.MakeMatte(_mat);
                return _mat;
            }
            _mat = new Material(sh);
            // Deeper, slightly blue-green grass (matches the old texture's richer tone — the
            // brighter/yellower first pass read "cheap"). Mottle in GrassTint keeps it organic.
            _mat.SetColor("_TopColor", new Color(0.33f, 0.50f, 0.35f));  // grass
            _mat.SetColor("_SideColor", new Color(0.42f, 0.31f, 0.21f)); // dirt (rare on a flat field)
            _mat.SetFloat("_SlopeLo", 0.45f);
            _mat.SetFloat("_SlopeHi", 0.80f);
            _mat.SetFloat("_EdgeDark", 0.12f);   // low: the ground has many facet edges, don't grid it
            _mat.SetFloat("_EdgeSharp", 6f);
            _mat.SetFloat("_ShadowImpact", 0.7f);
            return _mat;
        }
    }
}
