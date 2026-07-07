#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IdleGame.FxSpike
{
    /// <summary>
    /// ROADMAP 10.11e spike: icebolt three ways, side by side, in a throwaway scene —
    /// (1) today's glowing sphere, (2) ripped MS2 IceStrike shard cluster
    /// (Assets/FxSpike/icebolt_ripped.fbx baked by art/tools/fx_bake.py),
    /// (3) procedural crystal bolt (code mesh, no assets). Screenshot the Game view,
    /// the user picks a direction (ROADMAP "Your calls" #9), then this folder either
    /// graduates into the real projectile registry or gets deleted.
    /// </summary>
    public static class FxSpike
    {
        [MenuItem("Tools/FX Spike/Icebolt Comparison")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Cam") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0f, 1.1f, -4.2f);
            camGo.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.19f, 0.26f); // dusk arena-ish, so glows read

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.6f, 0f);
            Tint(ground, new Color(0.23f, 0.3f, 0.28f));

            BuildCurrentSphere(new Vector3(-2.2f, 0.6f, 0f));
            BuildRipped(new Vector3(0f, 0.6f, 0f));
            BuildProcedural(new Vector3(2.2f, 0.6f, 0f));

            foreach (var (label, x) in new[] { ("A  current sphere", -2.2f), ("B  ripped MS2", 0f), ("C  procedural", 2.2f) })
            {
                var t = new GameObject("Label").AddComponent<TextMesh>();
                t.text = label;
                t.fontSize = 48;
                t.characterSize = 0.045f;
                t.anchor = TextAnchor.MiddleCenter;
                t.color = new Color(0.9f, 0.93f, 1f);
                t.transform.position = new Vector3(x, -0.35f, 0f);
            }
            Debug.Log("[FxSpike] Icebolt comparison scene built (throwaway, don't save).");
        }

        // ---- (1) faithful copy of today's icebolt look (CombatView Paint+Glow recipe) ----
        private static void BuildCurrentSphere(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "A_CurrentSphere";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.55f;
            var c = new Color(0.55f, 0.85f, 1f);
            var m = Lit(c);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 2.5f);
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        // ---- (2) ripped MS2 IceStrike ball: shard meshes lit+clip, aura/flare additive ----
        private static void BuildRipped(Vector3 pos)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FxSpike/icebolt_ripped.fbx");
            if (fbx == null) { Debug.LogError("[FxSpike] icebolt_ripped.fbx not found"); return; }
            var root = Object.Instantiate(fbx, pos, Quaternion.identity);
            root.name = "B_RippedIceStrike";
            root.transform.localScale = Vector3.one * 2.2f; // FX authored small; read at sphere size

            var shardTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/FxSpike/21000202_m_leopardfairywizardice_idle01.dds");
            var auraTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/FxSpike/aura_00352.dds");
            var flareTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/FxSpike/flare_1.dds");
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                bool flare = r.name.StartsWith("Plane");
                bool aura = r.name is "ice01" or "ice05";
                r.sharedMaterial = flare ? Additive(flareTex, new Color(0.6f, 0.85f, 1f, 1f))
                                 : aura ? Additive(auraTex, new Color(0.55f, 0.8f, 1f, 0.8f))
                                 : Clip(shardTex);
                if (flare) r.transform.localScale *= 0.45f; // flare quads authored huge
            }
            root.AddComponent<Spin>();
        }

        // ---- (3) procedural crystal bolt: hex bipyramid + additive halo, zero assets ----
        private static void BuildProcedural(Vector3 pos)
        {
            var root = new GameObject("C_ProceduralCrystal");
            root.transform.position = pos;

            var shard = new GameObject("Shard");
            shard.transform.SetParent(root.transform, false);
            // shown upright here; in flight the projectile aligns the long axis to velocity
            var mf = shard.AddComponent<MeshFilter>();
            mf.sharedMesh = CrystalMesh(0.16f, 0.55f, 0.30f);
            shard.transform.localRotation = Quaternion.Euler(18f, 30f, -12f); // catch facet light
            var mr = shard.AddComponent<MeshRenderer>();
            var m = Lit(new Color(0.35f, 0.68f, 0.95f));
            m.SetFloat("_Smoothness", 0.45f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.1f, 0.35f, 0.8f) * 0.9f);
            mr.sharedMaterial = m;

            // two small trailing chunks, like calved ice
            for (int i = 0; i < 2; i++)
            {
                var chip = new GameObject("Chip" + i);
                chip.transform.SetParent(root.transform, false);
                chip.transform.localPosition = new Vector3(0.24f + 0.10f * i, -0.25f - 0.18f * i, 0f);
                chip.transform.localRotation = Quaternion.Euler(30f * i, 40f, 25f);
                var cmf = chip.AddComponent<MeshFilter>();
                cmf.sharedMesh = CrystalMesh(0.05f, 0.13f, 0.08f);
                chip.AddComponent<MeshRenderer>().sharedMaterial = m;
            }

            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            halo.name = "Halo";
            Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.transform.SetParent(root.transform, false);
            halo.transform.localScale = Vector3.one * 1.4f;
            halo.GetComponent<Renderer>().sharedMaterial = Additive(SoftDot(), new Color(0.35f, 0.65f, 1f, 0.55f));
            halo.AddComponent<FaceCamera>();
            root.AddComponent<Spin>();
        }

        /// <summary>Elongated hexagonal bipyramid — the classic ice-shard silhouette,
        /// flat-shaded (split verts per face) to match the game's faceted look.</summary>
        private static Mesh CrystalMesh(float radius, float tipLen, float bodyLen)
        {
            var ring = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.PI * 2f * i / 6f;
                ring[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            Vector3 tipF = new(0f, tipLen + bodyLen * 0.5f, 0f), tipB = new(0f, -(tipLen + bodyLen * 0.5f), 0f);
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int n = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris.Add(n); tris.Add(n + 1); tris.Add(n + 2);
            }
            for (int i = 0; i < 6; i++)
            {
                var a = ring[i] + Vector3.up * bodyLen * 0.5f;
                var b = ring[(i + 1) % 6] + Vector3.up * bodyLen * 0.5f;
                var c = ring[i] - Vector3.up * bodyLen * 0.5f;
                var d = ring[(i + 1) % 6] - Vector3.up * bodyLen * 0.5f;
                Tri(tipF, b, a);          // front pyramid
                Tri(a, b, d); Tri(a, d, c); // prism wall
                Tri(tipB, c, d);          // back pyramid
            }
            var mesh = new Mesh { name = "IceCrystal" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); // split verts -> hard facets
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D? _softDot;
        private static Texture2D SoftDot()
        {
            if (_softDot != null) return _softDot;
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
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

        private static Material Lit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            return m;
        }

        private static Material Clip(Texture2D? tex)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.35f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.3f, 0.6f, 1f) * 0.8f);
            m.SetTexture("_EmissionMap", tex);
            return m;
        }

        private static Material Additive(Texture2D? tex, Color tint)
        {
            // URP's stock Unlit can't be reliably flipped to additive from C#
            // (surface/blend floats only take effect through its ShaderGUI), so the
            // spike ships a 20-line additive shader instead.
            var m = new Material(Shader.Find("FxSpike/Additive"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            return m;
        }

        private static void Tint(GameObject go, Color c)
        {
            go.GetComponent<Renderer>().sharedMaterial = Lit(c);
        }

        /// <summary>Editor-tick spin so the shard cluster reads in stills AND scrubs.</summary>
        [ExecuteAlways]
        private sealed class Spin : MonoBehaviour
        {
            private void Update() => transform.Rotate(0f, 90f * Time.deltaTime + 0.4f, 0f);
        }

        [ExecuteAlways]
        private sealed class FaceCamera : MonoBehaviour
        {
            private void Update()
            {
                var cam = Camera.main;
                if (cam != null) transform.rotation = cam.transform.rotation;
            }
        }
    }
}
