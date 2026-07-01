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

        // Tunic dappled-lighting: a procedural drifting light cookie on the sun. Flip off if it
        // reads wrong — the rest of the lighting cleanup stands on its own.
        private const bool EnableDappleCookie = true;
        private const float CookieWorldSize = 50f; // world units the cookie tile spans (tune by eye)

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
            save = Modifiers.SyncToStage(save, cfg); // align owned modifiers to farm depth (covers pre-stage-model saves)
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

            var modifiers = director.AddComponent<ModifierPanel>();
            modifiers.Bind(view, cfg);
            view.BindModifiers(modifiers);

            var tower = director.AddComponent<TowerView>();
            tower.Bind(view, cfg);
            view.BindTower(tower);

            var achievements = director.AddComponent<AchievementsPanel>();
            achievements.Bind(view, cfg);
            view.BindAchievements(achievements);

            var topbar = new GameObject("TopBar").AddComponent<TopBar>();
            topbar.transform.SetParent(session.transform);
            topbar.Bind(view, () => QuitToMenu(cfg, session, view));
            topbar.Open();

            var chat = new GameObject("ChatPanel").AddComponent<ChatPanel>();
            chat.transform.SetParent(session.transform);
            chat.Open();
            view.BindChat(chat);

            var quests = new GameObject("QuestPanel").AddComponent<QuestPanel>();
            quests.transform.SetParent(session.transform);
            quests.Open();
            view.BindQuests(quests);

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
            light.intensity = 1.35f;                    // brighter, clean Tunic key (was 1.1)
            light.color = new Color(1f, 0.97f, 0.90f);  // warm sun, less yellow than before
            // Crisp grounded shadows read as Tunic, not the old soft murky haze. Still < 1 so
            // shaded sides stay readable (the TunicSurface shader also caps shadow impact).
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;                 // firmer contact (was 0.55)

            // Dappled "sun through canopy" light cookie (reimplemented in code from the Tunic
            // dappled-lighting reference — procedural, no shipped textures). Drifts via
            // LightCookieScroll so soft light pools slide across the world.
            if (EnableDappleCookie)
            {
                light.cookie = DappleCookie();
                var ald = light.GetUniversalAdditionalLightData();
                ald.lightCookieSize = new Vector2(CookieWorldSize, CookieWorldSize);
                ald.lightCookieOffset = Vector2.zero;
                if (light.GetComponent<LightCookieScroll>() == null)
                    light.gameObject.AddComponent<LightCookieScroll>();
            }

            // The diorama framing puts the camera ~40u back at full zoom-out; bump the URP
            // asset's 50u shadow distance a little so the whole framed view gets shadows.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = 90f;

            // Clean, brighter ambient: de-greened sky fill + a warm ground bounce so shaded
            // faces pick up light colour instead of the old murky green (trilight = sky/eq/ground).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.82f);     // clean cool sky
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.60f, 0.56f); // near-neutral
            RenderSettings.ambientGroundColor = new Color(0.42f, 0.38f, 0.32f);  // warm earth bounce

            // Distance fog only veils the far horizon now — pushed well past the action so the
            // play area stays crisp and bright (the old 65u start was the main source of haze).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyColor;
            RenderSettings.fogStartDistance = 130f;   // was 65 — keep the near field clear
            RenderSettings.fogEndDistance = 300f;     // dissolves the ground edge (±250) into sky

            BuildPostFx();

            // --- ground ---
            // Faceted, flat-shaded, vertex-coloured grass on the TunicSurface shader (no texture).
            // Covers the roam region (Balance.MapHalfWidth/Depth) plus margin so the party never
            // walks off onto the void; the far edge dissolves into fog.
            Ground.Build(cfg);

            // Scatter procedural low-poly props (rocks/trees/bushes) over the field.
            Scenery.Build(cfg);
        }

        /// <summary>The flat-sky / fog colour shared by the camera clear and distance fog.
        /// A clean, bright, slightly-warm sky-blue (was a murky green-teal) — the Tunic re-pass.</summary>
        private static readonly Color SkyColor = new Color(0.60f, 0.76f, 0.88f);

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
            color.postExposure.Override(0.0f);                    // brightness now comes from the key
            color.contrast.Override(8f);                          // a touch more pop for crisp facets
            color.saturation.Override(10f);                       // punchy but not the old over-cozy green
            color.colorFilter.Override(new Color(1f, 0.99f, 0.96f)); // very faint warmth

            var wb = profile.Add<UnityEngine.Rendering.Universal.WhiteBalance>();
            wb.temperature.Override(4f);                          // gentle warmth (was a heavy +8)

            var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>();
            bloom.intensity.Override(0.5f);
            bloom.threshold.Override(1.05f);                      // only true highlights (fire orbs) bloom
            bloom.scatter.Override(0.6f);

            var vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>();
            vignette.intensity.Override(0.15f);                   // lighter — less murk at the edges
            vignette.smoothness.Override(0.5f);

            var fxGo = new GameObject("PostFx");
            var vol = fxGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
        }

        private static Texture2D _dappleTex;

        /// <summary>A seamless, tiling "dappled sunlight" cookie generated in code (no shipped
        /// photo). Built from a sum of integer-frequency sinusoids — exactly periodic over the
        /// tile, so it repeats with no seam — shaped into soft light pools. Values stay in
        /// [floor, 1] so the cookie only ever dims the sun in patches, never blackens it.</summary>
        private static Texture2D DappleCookie()
        {
            if (_dappleTex != null) return _dappleTex;
            const int n = 256;
            const float floor = 0.55f; // darkest dapple keeps 55% of the sun
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };

            // (freqX, freqY, phase, amplitude) — integer freqs => tileable; amps sum to 1.
            var waves = new[]
            {
                (1, 2, 0.0f, 0.30f), (2, 3, 1.7f, 0.25f), (3, 1, 0.5f, 0.20f),
                (4, 2, 2.3f, 0.15f), (2, 5, 4.1f, 0.10f),
            };
            const float TAU = 6.2831853f;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float s = 0f;
                    foreach (var (fx, fy, ph, amp) in waves)
                        s += amp * Mathf.Sin(TAU * fx * u + ph) * Mathf.Sin(TAU * fy * v + ph * 0.7f);
                    float t = s * 0.5f + 0.5f;                    // [-1,1] -> [0,1]
                    float light = Mathf.SmoothStep(0.25f, 0.75f, t); // soft pools
                    float val = Mathf.Lerp(floor, 1f, light);
                    px[y * n + x] = new Color(val, val * 0.99f, val * 0.96f); // faint warmth in the light
                }
            tex.SetPixels(px);
            tex.Apply();
            _dappleTex = tex;
            return tex;
        }

    }
}
