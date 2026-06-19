using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Scene bootstrap. Builds the environment in code on Play (no manual editor
    /// wiring), shows the main menu, and on Continue / New Game starts a session that
    /// hands the party to <see cref="CombatView"/>. The renderer only READS game-core
    /// state; all logic stays in IdleGame.GameCore. Primitives are placeholders.
    /// </summary>
    public static class Bootstrap
    {
        private const uint Seed = 12345u;

        private static long NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Boot()
        {
            var cfg = GameConfig.Default();
            BuildEnvironment(cfg);
            ShowMenu(cfg);
        }

        private static void ShowMenu(GameConfig cfg)
        {
            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            menu.HasSave = SaveStore.Exists();
            menu.OnContinue = () => Continue(cfg);
            menu.OnNewGame = () => NewGame(cfg);
            menu.Open();
        }

        private static void NewGame(GameConfig cfg)
        {
            var save = Save.NewGame(Seed, cfg, NowMs());
            SaveStore.Save(save); // write immediately so Continue works next launch
            StartSession(cfg, save, new IdleReport());
        }

        private static void Continue(GameConfig cfg)
        {
            var loaded = SaveStore.Load();
            if (loaded == null) { NewGame(cfg); return; } // corrupt/missing -> fall back
            var (save, report) = Idle.Claim(loaded, cfg, NowMs()); // real offline gap
            StartSession(cfg, save, report);
        }

        // Everything for a play session lives under one "Session" root so Quit-to-Menu
        // can tear it down cleanly (the EventSystem is separate and persists).
        private static void StartSession(GameConfig cfg, SaveState save, IdleReport idleReport)
        {
            var session = new GameObject("Session");

            var director = new GameObject("CombatDirector");
            director.transform.SetParent(session.transform);
            var view = director.AddComponent<CombatView>();
            view.Init(save, cfg);
            director.AddComponent<Autosave>().Bind(view);

            var inventory = director.AddComponent<InventoryView>();
            inventory.Bind(view, cfg);
            view.BindInventory(inventory);

            var equipment = director.AddComponent<EquipmentView>();
            equipment.Bind(view, cfg);
            view.BindEquipment(equipment);

            var topbar = new GameObject("TopBar").AddComponent<TopBar>();
            topbar.transform.SetParent(session.transform);
            topbar.Bind(view, () => QuitToMenu(cfg, session, view));
            topbar.Open();

            var chat = new GameObject("ChatPanel").AddComponent<ChatPanel>();
            chat.transform.SetParent(session.transform);
            chat.Open();
            view.BindChat(chat);

            if (!idleReport.IsEmpty)
            {
                var modal = new GameObject("IdleClaimModal");
                modal.transform.SetParent(session.transform);
                modal.AddComponent<IdleClaimModal>().Show(idleReport);
                Debug.Log($"[Bootstrap] Idle claim: {idleReport.Gold} gold, {idleReport.Xp} XP, " +
                          $"{idleReport.Items.Count} item(s) over {idleReport.ElapsedMs / 3600_000.0:F1}h.");
            }

            Debug.Log($"[Bootstrap] Session started at stage {save.Progress.CurrentStage}.");
        }

        private static void QuitToMenu(GameConfig cfg, GameObject session, CombatView view)
        {
            SaveStore.Save(Save.Touch(view.CurrentSave, NowMs())); // flush progress before leaving
            Object.Destroy(session);
            ShowMenu(cfg);
        }

        private static void BuildEnvironment(GameConfig cfg)
        {
            // --- camera (iso angle) ---
            // Fixed iso tilt + a default framing; the runtime CameraRig (added by CombatView)
            // takes over position to follow the party and handle zoom, treating this framing
            // as the max zoom-out. Decoupled from map size so zoom feel is stable.
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(18f, 30f, -22f);
            cam.transform.rotation = Quaternion.Euler(42f, -45f, 0f);
            // Diorama compression (Tunic-ish): a narrow FOV flattens perspective so the
            // low-poly world reads like a hand-built model. The CameraRig pushes the camera
            // ~2x further back to keep the same framing — see its Min/MaxDistance. Lower this
            // for more compression; raise toward 60 for the wide PoE/Diablo overview.
            cam.fieldOfView = 34f;
            // A soft flat sky (no procedural skybox) — cozy, Tunic-ish, and the fog below
            // fades the ground plane's far edge into it so the world reads as endless.
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyColor;

            // URP per-camera post-processing must be opted in (the scene is built in code, so
            // there's no inspector to tick). SMAA keeps the low-poly edges crisp under bloom.
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

            // --- directional light ---
            var light = Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                var lightGo = new GameObject("Sun");
                light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
            }
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.86f); // warm "sun" key
            // Soft grounded shadows are the single biggest contributor to the cozy diorama
            // look. Not pitch-black (strength < 1) so shaded sides stay readable.
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.55f;

            // The diorama framing puts the camera ~40u back at full zoom-out; bump the URP
            // asset's 50u shadow distance a little so the whole framed view gets shadows.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = 90f;

            // Stylized ambient: a cool sky fill + a slightly warm ground bounce, so shaded
            // faces pick up colour instead of going flat grey (trilight = sky/equator/ground).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.64f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.52f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.34f, 0.30f);

            // Gentle distance fog tinted to the sky, to add depth and dissolve the ground
            // plane's far edge. Kept far/linear so it never muddies the active play area.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyColor;
            // Fog is distance-from-camera; start it beyond the party (~40u back) so the action
            // stays clear, and let it gently veil mobs further out toward the horizon.
            RenderSettings.fogStartDistance = 65f;
            RenderSettings.fogEndDistance = 180f;

            BuildPostFx();

            // --- ground plane ---
            // Must cover the whole roam region (Balance.MapHalfWidth/Depth, now ±200×140)
            // plus margin so the party never walks off onto the void. A Unity plane is
            // 10x10 units, so scale 50 => 500x500 units (±250).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(50f, 1f, 50f);

            // Tiled grid texture so the floor reads as a surface (and gives a motion
            // reference) instead of a flat void. Tinted green by the material colour.
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var groundMat = new Material(shader) { color = new Color(0.30f, 0.52f, 0.40f) }; // soft cozy green
            groundMat.mainTexture = GroundTexture();
            groundMat.mainTextureScale = new Vector2(100f, 100f); // ~5-unit grid cells across the 500u plane
            MakeMatte(groundMat); // no plastic specular; Phase 2 replaces this with stylised terrain
            var gr = ground.GetComponent<Renderer>();
            if (gr != null) gr.sharedMaterial = groundMat;
        }

        /// <summary>The cozy flat-sky / fog colour shared by the camera clear and distance fog.</summary>
        private static readonly Color SkyColor = new Color(0.64f, 0.76f, 0.74f);

        /// <summary>Kill plastic specular so lit surfaces read matte (the stylised look).
        /// Guards each property since the Standard fallback names smoothness differently.</summary>
        public static void MakeMatte(Material mat)
        {
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        }

        /// <summary>
        /// Build the global post-processing stack in code (no scene Volume to wire up):
        /// neutral tonemapping, a touch of warm colour grading + bloom, and a soft vignette.
        /// Deliberately subtle — this sells the mood without crushing readability. All values
        /// are first-pass guesses to tune by screenshot.
        /// </summary>
        private static void BuildPostFx()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var tone = profile.Add<UnityEngine.Rendering.Universal.Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);

            var color = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>();
            color.postExposure.Override(0.05f);
            color.contrast.Override(6f);
            color.saturation.Override(14f);                       // a little punchier, cozier
            color.colorFilter.Override(new Color(1f, 0.98f, 0.94f)); // faint warmth

            var wb = profile.Add<UnityEngine.Rendering.Universal.WhiteBalance>();
            wb.temperature.Override(8f);                          // warm the whole frame slightly

            var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>();
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.6f);

            var vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>();
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            var fxGo = new GameObject("PostFx");
            var vol = fxGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
        }

        private static Texture2D _groundTex;

        /// <summary>A repeating grid cell (light fill + darker edge lines) — multiplied by the
        /// ground's green base colour. Generated once; tiled across the plane.</summary>
        private static Texture2D GroundTexture()
        {
            if (_groundTex != null) return _groundTex;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var fill = new Color(0.95f, 0.95f, 0.95f); // ~base colour after tint
            var line = new Color(0.62f, 0.62f, 0.62f); // darker grid lines
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = (x < 2 || y < 2) ? line : fill; // 2px lines on two edges => grid when tiled
            tex.SetPixels(px);
            tex.Apply();
            _groundTex = tex;
            return tex;
        }
    }
}
