#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IdleGame.Game;
using IdleGame.GameCore;

namespace IdleGame.EditorTools
{
    /// <summary>
    /// Tools > Dungeon Preview (roguelite slice 2 control panel). Generates a <see cref="Dungeon"/>
    /// from seed + knobs, renders it via <see cref="DungeonRenderer"/> under a "DungeonPreview" root in
    /// the OPEN scene (edit mode, no Play), and STAGES the reference framing itself: an orthographic
    /// diorama camera, near-black fog, hemisphere ambient, one directional key, and a local post Volume
    /// (Bloom / Vignette / ColorAdjustments / Gaussian DoF). Clear tears everything down and restores
    /// the scene's prior fog + ambient (saved on Build).
    ///
    /// This owns the whole look pipeline so the exact reproduction path is scriptable:
    ///   Build(theme="molten", roomCount=80, seed=880239) → the reference image's params.
    /// </summary>
    public sealed class DungeonPreviewWindow : EditorWindow
    {
        // --- controls ---
        private int _seed = 880239;
        private int _roomCount = 42;
        private float _loopChance = 0.15f;
        private float _decorDensity = 0.6f;
        private int _themeIndex = 0; // index into DungeonTheme.Keys
        private bool _linear = false; // chain layout (the game's roguelite mode); off = the branching showpiece
        private bool _graphOverlay = false;
        private bool _heatmap = false;
        private bool _spawnMarkers = true;

        // --- state ---
        private Dungeon? _dungeon;
        private GameObject? _root;         // the DungeonPreview root (all render + staging under here)
        private GameObject? _renderRoot;   // DungeonRenderer output (child of _root)

        // Saved scene lighting to restore on Clear.
        private bool _savedFog;
        private FogMode _savedFogMode;
        private Color _savedFogColor;
        private float _savedFogDensity;
        private AmbientMode _savedAmbientMode;
        private Color _savedAmbSky, _savedAmbEq, _savedAmbGround;
        private bool _hasSavedEnv;

        [MenuItem("Tools/Dungeon Preview")]
        public static void Open()
        {
            var win = GetWindow<DungeonPreviewWindow>("Dungeon Preview");
            win.minSize = new Vector2(320, 560);
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("DUNGEON FORGE", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Seed + dice.
            using (new EditorGUILayout.HorizontalScope())
            {
                _seed = EditorGUILayout.IntField("Seed", _seed);
                if (GUILayout.Button("⚄", GUILayout.Width(30))) // die face
                    _seed = Random.Range(1, 9_999_999);
            }

            _roomCount = EditorGUILayout.IntSlider("Room Count", _roomCount, 10, 80);
            _loopChance = EditorGUILayout.Slider("Loop Chance", _loopChance, 0f, 0.5f);
            _decorDensity = EditorGUILayout.Slider("Decor Density", _decorDensity, 0f, 1f);
            _themeIndex = EditorGUILayout.Popup("Theme", _themeIndex, DungeonTheme.Keys);
            _linear = EditorGUILayout.Toggle(new GUIContent("Linear (roguelite)",
                "Single self-avoiding chain — what the in-game crypt uses. Off = the branching showpiece."), _linear);

            EditorGUILayout.Space(4);
            _graphOverlay = EditorGUILayout.Toggle("Graph overlay", _graphOverlay);
            _heatmap = EditorGUILayout.Toggle("Difficulty heatmap", _heatmap);
            _spawnMarkers = EditorGUILayout.Toggle("Spawn markers", _spawnMarkers);

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build", GUILayout.Height(28))) Build();
                if (GUILayout.Button("Clear", GUILayout.Height(28))) Clear();
            }

            EditorGUILayout.Space(8);
            DrawStats();
        }

        private void DrawStats()
        {
            if (_dungeon == null)
            {
                EditorGUILayout.HelpBox("No dungeon built. Set params and press Build.", MessageType.Info);
                return;
            }
            var d = _dungeon;
            var big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, wordWrap = true };
            EditorGUILayout.LabelField(d.Name, big);
            EditorGUILayout.LabelField($"seed {d.Params.Seed} · {d.Params.Theme}");
            EditorGUILayout.Space(4);

            var s = d.Stats;
            EditorGUILayout.LabelField($"Rooms: {s.Rooms}    Edges: {s.Edges}    Loops: {s.Loops}");
            EditorGUILayout.LabelField($"Crit path: {s.CriticalLength} rm    Floor tiles: {s.FloorTiles}");
            EditorGUILayout.LabelField($"Props: {s.Props}    Spawns: {d.Spawns.Count}");
            EditorGUILayout.LabelField($"Gen: {s.GenMs:F1} ms    Checksum: {d.Checksum:X8}");
        }

        // ---- build / clear --------------------------------------------------

        /// <summary>Generate + render + stage. Public so a script (reflection) can drive the exact
        /// reproduction path without the GUI.</summary>
        public void Build()
        {
            Clear();

            var p = new DungeonParams
            {
                Seed = _seed,
                RoomCount = _roomCount,
                LoopChance = _loopChance,
                DecorDensity = _decorDensity,
                Theme = DungeonTheme.Keys[Mathf.Clamp(_themeIndex, 0, DungeonTheme.Keys.Length - 1)],
                Linear = _linear,
            };
            try { _dungeon = DungeonGen.Generate(p); }
            catch (System.Exception e)
            {
                Debug.LogError($"[DungeonPreview] Generate failed: {e.Message}");
                _dungeon = null;
                return;
            }

            string themeKey = p.Theme;
            var theme = DungeonTheme.Get(themeKey);

            _root = new GameObject("DungeonPreview");
            _renderRoot = DungeonRenderer.Build(_dungeon, themeKey, _root.transform, _spawnMarkers);

            if (_heatmap) ApplyHeatmap(_dungeon, _renderRoot);
            if (_graphOverlay) BuildGraphOverlay(_dungeon, _root.transform);

            SaveEnv();
            StageEnvironment(_dungeon, theme);

            Selection.activeGameObject = _root;
            SceneView.RepaintAll();
        }

        /// <summary>Tear down the render + staging and restore the saved scene lighting.</summary>
        public void Clear()
        {
            if (_root != null) { DestroyImmediate(_root); _root = null; _renderRoot = null; }
            RestoreEnv();
        }

        private void OnDestroy() => Clear();

        // ---- staging (camera + fog + ambient + dir light + volume) ----------

        private void SaveEnv()
        {
            if (_hasSavedEnv) return; // Build() calls Clear() first, which restores; guard double-save
            _savedFog = RenderSettings.fog;
            _savedFogMode = RenderSettings.fogMode;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;
            _savedAmbientMode = RenderSettings.ambientMode;
            _savedAmbSky = RenderSettings.ambientSkyColor;
            _savedAmbEq = RenderSettings.ambientEquatorColor;
            _savedAmbGround = RenderSettings.ambientGroundColor;
            _hasSavedEnv = true;
        }

        private void RestoreEnv()
        {
            if (!_hasSavedEnv) return;
            RenderSettings.fog = _savedFog;
            RenderSettings.fogMode = _savedFogMode;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogDensity = _savedFogDensity;
            RenderSettings.ambientMode = _savedAmbientMode;
            RenderSettings.ambientSkyColor = _savedAmbSky;
            RenderSettings.ambientEquatorColor = _savedAmbEq;
            RenderSettings.ambientGroundColor = _savedAmbGround;
            _hasSavedEnv = false;
        }

        private void StageEnvironment(Dungeon d, DungeonTheme theme)
        {
            if (_root == null) return;

            // Scene-wide fog + ambient (Exp2 near-black haze; hemisphere trilight ambient).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;   // Exp2 in Unity terms
            RenderSettings.fogColor = theme.Fog;
            RenderSettings.fogDensity = theme.FogDensity;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Urp*Scale: three.js gamma-space intensities read dim in linear URP — see DungeonTheme.
            float amb = theme.HemiIntensity * DungeonTheme.UrpAmbientScale;
            RenderSettings.ambientSkyColor = theme.HemiSky * amb;
            RenderSettings.ambientEquatorColor = Color.Lerp(theme.HemiGround, theme.HemiSky, 0.5f) * amb;
            RenderSettings.ambientGroundColor = theme.HemiGround * amb;

            // Directional key light.
            var dirGo = new GameObject("PreviewSun");
            dirGo.transform.SetParent(_root.transform, false);
            dirGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            var dir = dirGo.AddComponent<Light>();
            dir.type = LightType.Directional;
            dir.color = theme.DirColor;
            dir.intensity = theme.DirIntensity * DungeonTheme.UrpSunScale;
            dir.shadows = LightShadows.None;

            // Orthographic diorama camera framing the dungeon bounds (yaw 45°, pitch ~36°).
            StageCamera(d, theme);

            // Local post Volume (high priority so it wins over the game's, which only exists in Play).
            StageVolume(theme);
        }

        private void StageCamera(Dungeon d, DungeonTheme theme)
        {
            if (_root == null) return;

            // Dungeon centre + radius in world units (grid cell = 1 unit).
            Bounds b = FloorBounds(d);
            Vector3 centre = b.center;
            float radius = Mathf.Max(b.extents.x, b.extents.z, 4f);

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            // Reference framing crops the dungeon's edges slightly (preview.jpg fills the frame);
            // 1.15 showed the whole footprint postage-stamp small. Screenshot-tuned.
            cam.orthographicSize = Mathf.Max(30f, radius * 0.52f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = theme.Bg;

            // Yaw 45°, pitch 36°, backed off ALONG -forward (a rot*Vector3.back sign flip here put
            // the camera underground on the far side — the first capture was pure void). The fixed
            // ~220u distance matches the reference camera, which the theme fog densities were
            // authored against (FogExp2 attenuates by eye-distance even for an ortho camera).
            Quaternion rot = Quaternion.Euler(36f, 45f, 0f);
            const float dist = 220f;
            camGo.transform.rotation = rot;
            camGo.transform.position = centre - rot * Vector3.forward * dist;
            cam.nearClipPlane = 10f;
            cam.farClipPlane = dist + radius * 2f + 100f;

            // URP per-camera: opt post-processing in (built in code, no inspector). Depth texture for
            // DoF. AA stays OFF: SMAA smeared the 1px wall shading + tile checker into mush at this
            // zoom (screenshot-compared); the reference's crispness needs raw pixels.
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.requiresDepthTexture = true;
            camData.antialiasing = AntialiasingMode.None;

            // Aim a SceneView at the same framing so the preview shimmers in the editor without a
            // Game view; the staged Camera is what a screenshot / Game view captures.
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                sv.LookAt(centre, rot, radius * 2.2f);
                sv.Repaint();
            }
        }

        private void StageVolume(DungeonTheme theme)
        {
            if (_root == null) return;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.7f);

            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.5f);

            var color = profile.Add<ColorAdjustments>();
            color.contrast.Override(6f);
            color.saturation.Override(6f);
            color.colorFilter.Override(new Color(1f, 0.98f, 0.95f));

            // NO DepthOfField: the reference's tilt-shift is NOT reproducible with URP DoF on an
            // ORTHOGRAPHIC camera — its CoC math reconstructs perspective depth, which degenerates
            // to a uniform full-frame blur under ortho (screenshot-caught: every "band" setting
            // smeared the whole dungeon; all the crisp captures had DoF off). A custom screen-space
            // band-blur pass could bring it back later; crisp matches the reference better than
            // wrong-blur.

            var volGo = new GameObject("PreviewPostFx");
            volGo.transform.SetParent(_root.transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f; // above the game's PostFx (priority 10)
            vol.sharedProfile = profile;
        }

        // ---- heatmap + graph overlay ---------------------------------------

        // Recolour every floor chunk's verts along heatA→heatB by the cell's normalised BFS depth.
        private void ApplyHeatmap(Dungeon d, GameObject renderRoot)
        {
            int maxBfs = 1;
            for (int i = 0; i < d.Bfs.Length; i++) if (d.Bfs[i] > maxBfs) maxBfs = d.Bfs[i];

            var floor = renderRoot.transform.Find("Floor");
            if (floor == null) return;
            int w = d.W;
            foreach (Transform chunk in floor)
            {
                var mf = chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var cols = mesh.colors;
                if (cols == null || cols.Length != verts.Length) cols = new Color[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    // Verts are world-space tile corners; sample the cell they sit in.
                    int cx = Mathf.Clamp(Mathf.FloorToInt(verts[i].x), 0, d.W - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(verts[i].z), 0, d.H - 1);
                    short bfs = d.Bfs[cy * w + cx];
                    float t = bfs < 0 ? 0f : bfs / (float)maxBfs;
                    // AO fold matching the doc's heatmap scale.
                    int walls8 = Walls8(d, cx, cy);
                    float scale = 0.55f + 0.45f * (1f - 0.09f * Mathf.Min(walls8, 4));
                    cols[i] = Color.Lerp(DungeonTheme.HeatA, DungeonTheme.HeatB, t) * scale;
                    cols[i].a = 1f;
                }
                mesh.colors = cols;
            }
        }

        private int Walls8(Dungeon d, int x, int y)
        {
            int n = 0, w = d.W, h = d.H;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (d.Grid[ny * w + nx] == DungeonCell.Wall) n++;
                }
            return n;
        }

        // MST white / loops cyan / critical red room-to-room lines, drawn slightly above the floor.
        private void BuildGraphOverlay(Dungeon d, Transform parent)
        {
            var go = new GameObject("GraphOverlay");
            go.transform.SetParent(parent, false);

            var idToRoom = new Dictionary<int, DungeonRoom>();
            foreach (var r in d.Rooms) idToRoom[r.Id] = r;

            foreach (var e in d.Edges)
            {
                if (!idToRoom.TryGetValue(e.A, out var ra) || !idToRoom.TryGetValue(e.B, out var rb)) continue;
                Color c = e.IsCritical ? DungeonTheme.OverlayCrit
                        : e.IsLoop ? DungeonTheme.OverlayLoop
                        : DungeonTheme.OverlayMst;
                var lineGo = new GameObject($"edge_{e.A}_{e.B}");
                lineGo.transform.SetParent(go.transform, false);
                var lr = lineGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.widthMultiplier = e.IsCritical ? 0.5f : 0.3f;
                lr.numCapVertices = 2;
                lr.SetPosition(0, new Vector3(ra.Cx + 0.5f, 2.6f, ra.Cy + 0.5f));
                lr.SetPosition(1, new Vector3(rb.Cx + 0.5f, 2.6f, rb.Cy + 0.5f));
                var m = DungeonRenderer.EmissiveMaterial(c, 1.2f);
                lr.sharedMaterial = m;
                lr.startColor = lr.endColor = c;
            }
        }

        // World-space bounds of the floor footprint (grid cell = 1 unit).
        private static Bounds FloorBounds(Dungeon d)
        {
            int w = d.W, h = d.H;
            int minX = w, minY = h, maxX = 0, maxY = 0;
            bool any = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (d.Grid[y * w + x] != DungeonCell.Void)
                    {
                        any = true;
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            if (!any) return new Bounds(Vector3.zero, new Vector3(10, 4, 10));
            var b = new Bounds();
            b.SetMinMax(new Vector3(minX, 0f, minY), new Vector3(maxX + 1f, 3f, maxY + 1f));
            return b;
        }
    }
}
