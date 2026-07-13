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

        /// <summary>A holy bolt: one slim bright shard of light (no chips — light is clean),
        /// warm-white with a gold halo, gentle roll. The priest's basic attack.</summary>
        public static GameObject LightShard(float scale = 1f)
        {
            var root = new GameObject("LightShard");
            var shard = Part(root, Crystal(0.045f, 0.26f, 0.10f),
                             CrystalMat(new Color(1f, 0.93f, 0.72f), new Color(1f, 0.85f, 0.45f) * 0.8f));
            shard.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // mesh +Y -> root +Z
            Halo(root, 0.5f, new Color(1f, 0.88f, 0.5f, 0.35f));
            root.AddComponent<FxSpin>().Configure(Vector3.forward, 160f);
            root.transform.localScale = Vector3.one * scale;
            return root;
        }

        /// <summary>A molten rock chunk: stubby dark crystal running hot (emission carries
        /// the heat; the game's bloom does the rest), ember chips, warm halo, slow tumble.
        /// The fire family's projectile body (fireball basic, firebolt meteor).</summary>
        public static GameObject FireChunk(float scale = 1f)
        {
            var root = new GameObject("FireChunk");

            var rock = Part(root, Crystal(0.11f, 0.10f, 0.10f),
                            CrystalMat(new Color(0.30f, 0.14f, 0.07f), new Color(1f, 0.3f, 0.05f) * 1.0f));
            rock.transform.localRotation = Quaternion.Euler(35f, 20f, 0f); // stubby chunk, not a spike

            for (int i = 0; i < 3; i++)
            {
                var ember = Part(root, Crystal(0.03f, 0.04f, 0.03f), rock.GetComponent<Renderer>().sharedMaterial);
                float a = Mathf.PI * 2f * i / 3f;
                ember.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.13f, Mathf.Sin(a) * 0.10f, -0.10f - 0.04f * i);
                ember.transform.localRotation = Quaternion.Euler(50f * i, 70f, 20f);
            }

            Halo(root, 0.55f, new Color(1f, 0.5f, 0.15f, 0.4f));
            root.AddComponent<FxSpin>().Configure(new Vector3(0.5f, 0.7f, 0.5f).normalized, 240f);
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

        // ---- per-element impact language (10.6b) -----------------------------------

        public enum ImpactElement { Physical, Fire, Ice, Holy }

        // Memoized key -> element map: AttackFx keys ("icebolt", "fireball"…) and skill sprite
        // hints ("blizzard", "holysmite"…) are a small closed set, so the substring probe runs
        // once per distinct key ever seen (ToLowerInvariant + Contains allocate; impacts are hot).
        private static readonly Dictionary<string, ImpactElement> _elementMemo = new();

        /// <summary>Element for an AttackFx / skill-sprite key ("frostbolt" → Ice). Null/unknown
        /// keys are Physical — melee stays understated by design.</summary>
        public static ImpactElement ElementOf(string? key)
        {
            if (string.IsNullOrEmpty(key)) return ImpactElement.Physical;
            if (_elementMemo.TryGetValue(key!, out var hit)) return hit;
            string k = key!.ToLowerInvariant();
            ImpactElement el =
                  k.Contains("fire") || k.Contains("meteor") || k.Contains("flame") || k.Contains("ember")
                    ? ImpactElement.Fire
                : k.Contains("ice") || k.Contains("frost") || k.Contains("blizzard")
                    ? ImpactElement.Ice
                : k.Contains("holy") || k.Contains("smite") || k.Contains("sanctify")
                    ? ImpactElement.Holy
                : ImpactElement.Physical;
            _elementMemo[key!] = el;
            return el;
        }

        /// <summary>The ONE impact-burst API (10.6b): a short per-element flourish at the felt
        /// hit point. fire = ember scatter (hot chips, gravity), ice = flat shard ring + a cool
        /// halo flash (the "frost coat" read WITHOUT touching the victim's material — tints are
        /// stateful rank/mod tells, never safe to borrow), holy = a vertical flash column,
        /// physical = three understated dust chips. All parts share cached meshes/materials;
        /// the mover is <see cref="FxScatter"/> on the root (renderer-less, so it can never
        /// trip TransientFx-style shared-material fades). Crit scales the whole burst.</summary>
        public static void ImpactBurst(Vector3 at, ImpactElement el, bool crit = false)
        {
            float s = crit ? 1.35f : 1f;
            switch (el)
            {
                case ImpactElement.Fire:
                {
                    // Ember burst: hot chips thrown in an upward cone, pulled down hard — coals.
                    var root = NewBurstRoot(at, s);
                    var mat = CrystalMat(new Color(0.30f, 0.14f, 0.07f), new Color(1f, 0.35f, 0.08f) * 0.9f);
                    var (parts, vel) = Chips(root, 5, Crystal(0.03f, 0.05f, 0.03f), mat,
                        i => (Quaternion.Euler(0f, i * 72f + Random.Range(-20f, 20f), 0f)
                              * new Vector3(0.6f, 1.3f, 0f)).normalized * Random.Range(2.6f, 3.6f));
                    Halo(root, 0.55f, new Color(1f, 0.45f, 0.12f, 0.30f));
                    root.AddComponent<FxScatter>().Configure(parts, vel, 0.38f, 9f);
                    break;
                }
                case ImpactElement.Ice:
                {
                    // Shard ring: flat crystals bursting outward, each pointing along its flight
                    // (mesh long axis = +Y), plus the cool flash that reads as a brief frost coat.
                    var root = NewBurstRoot(at, s);
                    var mat = CrystalMat(new Color(0.20f, 0.48f, 0.90f), new Color(0.05f, 0.2f, 0.7f) * 0.4f);
                    var (parts, vel) = Chips(root, 5, Crystal(0.035f, 0.09f, 0.05f), mat,
                        i => Quaternion.Euler(0f, i * 72f + Random.Range(-14f, 14f), 0f)
                             * Vector3.forward * Random.Range(2.2f, 2.9f));
                    for (int i = 0; i < parts.Length; i++)
                        parts[i].rotation = Quaternion.FromToRotation(Vector3.up, vel[i].normalized);
                    // 10.6c: deep blue, low alpha — the first pass (0.5,0.75,1 @ 0.28) bloomed to
                    // a white core; the blue must dominate its channels to survive the post stack.
                    Halo(root, 0.55f, new Color(0.25f, 0.5f, 1f, 0.20f));
                    root.AddComponent<FxScatter>().Configure(parts, vel, 0.32f, 0f);
                    break;
                }
                case ImpactElement.Holy:
                {
                    // Flash column: one tall billboarded beam + a ground-level glow; no debris —
                    // light is clean (the LightShard rule).
                    var root = NewBurstRoot(at, s);
                    var beam = Halo(root, 1f, new Color(1f, 0.9f, 0.55f, 0.4f));
                    beam.transform.localScale = new Vector3(0.4f, 2.2f, 1f);
                    beam.transform.localPosition = Vector3.up * 0.7f;
                    Halo(root, 0.7f, new Color(1f, 0.85f, 0.45f, 0.3f));
                    root.AddComponent<FxScatter>().Configure(
                        System.Array.Empty<Transform>(), System.Array.Empty<Vector3>(), 0.30f, 0f);
                    break;
                }
                default:
                {
                    // Physical: three understated dust chips, low and fast — a thud, not a spell.
                    var root = NewBurstRoot(at, s);
                    var mat = CrystalMat(new Color(0.55f, 0.50f, 0.44f), Color.black);
                    var (parts, vel) = Chips(root, 3, Crystal(0.025f, 0.04f, 0.025f), mat,
                        i => (Quaternion.Euler(0f, i * 120f + Random.Range(-30f, 30f), 0f)
                              * new Vector3(0.9f, 0.7f, 0f)).normalized * Random.Range(1.8f, 2.4f));
                    root.AddComponent<FxScatter>().Configure(parts, vel, 0.25f, 10f);
                    break;
                }
            }
        }

        private static GameObject NewBurstRoot(Vector3 at, float scale)
        {
            var root = new GameObject("ImpactBurst");
            root.transform.position = at;
            root.transform.localScale = Vector3.one * scale;
            return root;
        }

        /// <summary>Spawn <paramref name="count"/> chip children sharing one cached mesh/material,
        /// each with a launch velocity from <paramref name="dirFor"/> (LOCAL units/sec).</summary>
        private static (Transform[] parts, Vector3[] vel) Chips(
            GameObject root, int count, Mesh mesh, Material mat, System.Func<int, Vector3> dirFor)
        {
            var parts = new Transform[count];
            var vel = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var chip = Part(root, mesh, mat);
                parts[i] = chip.transform;
                vel[i] = dirFor(i);
            }
            return (parts, vel);
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

    /// <summary>Impact-burst mover (10.6b): flings chip children outward in LOCAL space (so the
    /// root's crit scale multiplies cleanly), pulls them down by gravity, shrinks the whole root
    /// out over the last 40% of life, then destroys it. Lives on a renderer-LESS root on purpose:
    /// it never touches materials, so cached FxKit materials stay pristine (TransientFx fades
    /// sharedMaterial emission — handing it a cached mat would corrupt every user).</summary>
    public sealed class FxScatter : MonoBehaviour
    {
        private Transform[] _parts = System.Array.Empty<Transform>();
        private Vector3[] _vel = System.Array.Empty<Vector3>();
        private float _t, _life = 0.35f, _gravity;
        private Vector3 _baseScale = Vector3.one;

        public void Configure(Transform[] parts, Vector3[] vel, float life, float gravity)
        {
            _parts = parts;
            _vel = vel;
            _life = Mathf.Max(0.01f, life);
            _gravity = gravity;
            _baseScale = transform.localScale;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float a = Mathf.Clamp01(_t / _life);
            for (int i = 0; i < _parts.Length; i++)
            {
                var p = _parts[i];
                if (p == null) continue;
                _vel[i] += Vector3.down * (_gravity * Time.deltaTime);
                p.localPosition += _vel[i] * Time.deltaTime;
            }
            // Shrink out over the last 40% so chips melt away instead of popping off.
            float shrink = a < 0.6f ? 1f : 1f - (a - 0.6f) / 0.4f;
            transform.localScale = _baseScale * Mathf.Max(0.02f, shrink);
            if (a >= 1f) Destroy(gameObject);
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
