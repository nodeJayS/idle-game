#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Procedural skill-FX kit (ROADMAP 10.11f — the user picked procedural over
    /// ripped MS2 assets). Faceted crystal/shard meshes + soft additive halos,
    /// all generated in code so every element is a palette + proportions choice,
    /// no textures shipped. Meshes and the halo texture are cached; materials are
    /// cached per color so projectile spam doesn't leak.
    /// </summary>
    public static class FxKit
    {
        // ---- shard roots ---------------------------------------------------------

        /// <summary>An ice-crystal projectile: hex-bipyramid shard (long axis +Z,
        /// ready for LookRotation toward the target), two trailing chips, a soft
        /// billboard halo, and a slow roll around the flight axis.</summary>
        public static GameObject IceShard(float scale = 1f)
        {
            var root = new GameObject("IceShard");

            // sized against the 0.55u sphere it replaced: shard total length ~0.62u at scale 1
            // (the first in-game pass at 1.4u read as a javelin, not a bolt); emission kept low —
            // the game's bloom + split-tone grade blows hot emissives to white.
            // deep-blue base + faint blue emission, tuned LIVE in Play against the warm sun,
            // split-tone grade and bloom (paler values wash to white under the stack)
            var shard = Part(root, Crystal(0.07f, 0.24f, 0.14f),
                             CrystalMat(new Color(0.16f, 0.42f, 0.85f), new Color(0.05f, 0.2f, 0.7f) * 0.5f));
            shard.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // mesh +Y -> root +Z

            for (int i = 0; i < 2; i++)
            {
                var chip = Part(root, Crystal(0.025f, 0.06f, 0.04f), shard.GetComponent<Renderer>().sharedMaterial);
                chip.transform.localPosition = new Vector3(i == 0 ? 0.07f : -0.06f, 0.03f - 0.06f * i, -0.14f - 0.06f * i);
                chip.transform.localRotation = Quaternion.Euler(70f + 30f * i, 40f, 0f);
            }

            Halo(root, 0.38f, new Color(0.25f, 0.5f, 1f, 0.2f));
            root.AddComponent<FxSpin>().Configure(Vector3.forward, 220f);
            root.transform.localScale = Vector3.one * scale;
            return root;
        }

        /// <summary>A small crystal planted in the ground (blizzard ring beats):
        /// pops in and melts away via TransientFx.</summary>
        public static void GroundCrystal(Vector3 at, float life, float scale)
        {
            var go = new GameObject("GroundCrystal");
            var part = Part(go, Crystal(0.11f, 0.3f, 0.18f),
                            CrystalMat(new Color(0.25f, 0.52f, 0.9f), new Color(0.05f, 0.2f, 0.7f) * 0.5f));
            part.transform.localPosition = Vector3.up * 0.18f;
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(Random.Range(-14f, 14f), Random.Range(0f, 360f), Random.Range(-14f, 14f));
            go.AddComponent<TransientFx>().Configure(life, Vector3.one * scale, Vector3.one * 0.05f);
        }

        // ---- building blocks -----------------------------------------------------

        private static GameObject Part(GameObject root, Mesh mesh, Material mat)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Soft additive glow quad, billboarded to the camera.</summary>
        public static GameObject Halo(GameObject root, float size, Color tint)
        {
            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = halo.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            halo.name = "Halo";
            halo.transform.SetParent(root.transform, false);
            halo.transform.localScale = Vector3.one * size;
            halo.GetComponent<Renderer>().sharedMaterial = AdditiveMat(tint);
            halo.AddComponent<FxBillboard>();
            return halo;
        }

        private static readonly Dictionary<(float, float, float), Mesh> _crystals = new();

        /// <summary>Elongated hexagonal bipyramid along +Y — the classic shard
        /// silhouette, split verts per face for hard Tunic-style facets.</summary>
        public static Mesh Crystal(float radius, float tipLen, float bodyLen)
        {
            var key = (radius, tipLen, bodyLen);
            if (_crystals.TryGetValue(key, out var cached)) return cached;

            var ring = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.PI * 2f * i / 6f;
                ring[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            Vector3 tipF = new(0f, tipLen + bodyLen * 0.5f, 0f), tipB = new(0f, -(tipLen + bodyLen * 0.5f), 0f);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int n = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris.Add(n); tris.Add(n + 1); tris.Add(n + 2);
            }
            for (int i = 0; i < 6; i++)
            {
                var up = Vector3.up * bodyLen * 0.5f;
                Vector3 a = ring[i] + up, b = ring[(i + 1) % 6] + up;
                Vector3 c = ring[i] - up, d = ring[(i + 1) % 6] - up;
                Tri(tipF, b, a);
                Tri(a, b, d); Tri(a, d, c);
                Tri(tipB, c, d);
            }
            var mesh = new Mesh { name = "FxCrystal" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _crystals[key] = mesh;
            return mesh;
        }

        private static readonly Dictionary<(Color, Color), Material> _crystalMats = new();

        public static Material CrystalMat(Color baseColor, Color emission)
        {
            var key = (baseColor, emission);
            if (_crystalMats.TryGetValue(key, out var cached)) return cached;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = baseColor };
            Bootstrap.MakeMatte(m);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f); // icy sheen, not plastic
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            _crystalMats[key] = m;
            return m;
        }

        private static readonly Dictionary<Color, Material> _additiveMats = new();

        public static Material AdditiveMat(Color tint)
        {
            if (_additiveMats.TryGetValue(tint, out var cached)) return cached;
            var m = new Material(Shader.Find("IdleGame/FxAdditive"));
            m.SetTexture("_BaseMap", SoftDot());
            m.SetColor("_BaseColor", tint);
            _additiveMats[tint] = m;
            return m;
        }

        private static Texture2D? _softDot;

        /// <summary>64px radial falloff — the one glow texture everything shares.</summary>
        public static Texture2D SoftDot()
        {
            if (_softDot != null) return _softDot;
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            _softDot = t;
            return t;
        }
    }

    /// <summary>Constant local-axis spin (projectile roll, pickup twirl).</summary>
    public sealed class FxSpin : MonoBehaviour
    {
        private Vector3 _axis = Vector3.up;
        private float _degPerSec = 180f;

        public void Configure(Vector3 axis, float degPerSec)
        {
            _axis = axis;
            _degPerSec = degPerSec;
        }

        private void Update() => transform.Rotate(_axis, _degPerSec * Time.deltaTime, Space.Self);
    }

    /// <summary>Faces the main camera each frame (glow quads).</summary>
    public sealed class FxBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
