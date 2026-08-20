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
            // Editor test-tool spawns (Tools > SDF Blob/Gait Test) live in the open scene, and a
            // long-lived editor session carries them into every Play — they showed up as idle
            // ghost blobs in a real session (2026-07-05). Play is authoritative: sweep them.
            foreach (var n in new[] { "SdfBlobTest", "SdfGaitTest" })
            {
                var stale = GameObject.Find(n);
                if (stale != null) Object.Destroy(stale);
            }

            var cfg = GameConfig.Default();
            AudioListener.volume = Settings.MasterVolume; // apply persisted master volume at boot
            // Benchmark mode (10.12d tooling, user call 2026-07-12): `-benchmark` runs the scripted
            // perf tour instead of the menu — uncapped (no FrameCap: true frame cost is the point,
            // and the 10fps unfocused cap would poison an unattended run) and save-sandboxed.
            bool benchmark = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark") >= 0;
            if (!benchmark)
                new GameObject("FrameCap").AddComponent<FrameCap>(); // 10.12a: the game must never run uncapped
            TrimShadows(); // 10.12c: the shadow pass re-drew ~1,500 scenery casters x4 cascades
            BuildEnvironment(cfg);
            // 10.12c2: bake the lazy caches (all GroundDetail styles, FxKit sprite) and render
            // each cold shader variant once, all hidden behind the main menu — the session
            // start has no LoadingScreen, so the menu IS the cover. See Prewarm.cs.
            Prewarm.Run();
            GraphicsQuality.Apply(); // 10.12d: persisted quality tier (render scale/shadows/post)
            if (benchmark) { RunBenchmark(cfg); return; }
            ShowMenu(cfg);
        }

        // 10.12c shadow trim. The URP asset ships shadowDistance 90 / 4 cascades; at the fixed
        // orthographic diorama framing the far cascades spend their texels (and a full scenery
        // re-draw each) on ground the camera barely sees. Set at RUNTIME via the asset's public
        // setters so the change lives in reviewable code and the .asset stays pristine on disk
        // (note: in the editor the in-memory asset keeps the values until domain reload — Play
        // again or restart to see the serialized numbers). Distance is a taste call — eyeball
        // shadow reach in Play and bump back toward 90 if distant shadows visibly pop in.
        private const int ShadowCascades = 2;
        public const float ShadowDistance = 60f; // public: GraphicsQuality restores it on shadows-on

        private static void TrimShadows()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowCascadeCount = ShadowCascades;
                urp.shadowDistance = ShadowDistance;
            }
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
            StartSession(cfg, save);
        }

        /// <summary>Scripted perf benchmark (10.12d tooling): sandbox the save store FIRST — the
        /// session writes saves on mode transitions and autosave ticks, and a benchmark must
        /// never touch a real player's file — then run a mid-game session uncapped and hand the
        /// tour to <see cref="Benchmark"/> (campaign full-pack farm → blob-dense crypt floor →
        /// benchmark.json → quit). The party is deliberately UNLEVELED for stage 25: it wipes,
        /// respawns, and the screen fills to MobCap — the worst-case density is the point.</summary>
        private static void RunBenchmark(GameConfig cfg)
        {
            SaveStore.FileNameOverride = "save_benchmark.json";
            SaveStore.Delete(); // a fresh sandbox every run
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            // 2026-08-20: a standalone player THROTTLES ITSELF when it loses focus, and the whole
            // point of the .bat is that the tester walks away. Without this the tour crawls and
            // every frame time in the report is a lie about the machine, not a measurement.
            Application.runInBackground = true;

            var save = Save.NewGame(Seed, cfg, NowMs());
            for (int st = 1; st < 25; st++) save = Progression.OnStageCleared(save, st, cfg);
            var view = StartSession(cfg, save);
            view.gameObject.AddComponent<Benchmark>().Bind(view);
        }

        private static void Continue(GameConfig cfg)
        {
            var loaded = SaveStore.Load();
            if (loaded == null) { NewGame(cfg); return; } // corrupt/missing -> fall back

            // One-time carry-over (10.5a): the auto-salvage threshold moved into the SAVE
            // (per-slot loot filter). If the legacy global pref is still set and this save has
            // no floors yet, seed every slot from it and clear the pref — it was a PlayerPrefs
            // global that leaked into New Game; it now survives only as this migration source.
            // Runs right after Save.Migrate (inside Load) and BEFORE the sync/prune below.
            if (Settings.AutoSalvageMax != null && loaded.Progress.Loot.SalvageMaxBySlot.Count == 0)
            {
                loaded = Inventory.SetSalvageFloorAll(loaded, Settings.AutoSalvageMax);
                Settings.AutoSalvageMax = null;
            }

            // 10.14b: offline idle is NO LONGER claimed at load — the boot arrival card previews it and
            // Session.Arrive banks it on one tap (see StartSession). LastClaimAt stays put until then, so
            // a quit before Collect just re-previews the same accrual next launch (nothing is lost). The
            // sync/prune reducers below don't depend on the idle grant (idle only adds gold/xp/valid loot),
            // so their order relative to the deferred claim is immaterial.
            var save = Modifiers.SyncToStage(loaded, cfg); // align owned modifiers to farm depth (pre-stage-model saves)
            save = Progression.SyncHeroUnlocks(save, cfg); // retro-grant unlocks ≤ HighestStage; drop shelved heroes
            save = Codex.SyncFromInventory(save, cfg);     // retro-stamp set discovery from gear predating the codex (10.15)
            save = Inventory.PruneUnknownGear(save, cfg);  // dissolve gear from deleted slots/bases into scrap
            StartSession(cfg, save);
        }

        // Everything for a play session lives under one "Session" root so Quit-to-Menu
        // can tear it down cleanly (the EventSystem is separate and persists). Returns the
        // CombatView so the benchmark rig can attach; normal boots ignore it.
        private static CombatView StartSession(GameConfig cfg, SaveState save)
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

            var gacha = director.AddComponent<GachaPanel>();
            gacha.Bind(view, cfg);
            view.BindGacha(gacha);

            var goals = director.AddComponent<GoalsPanel>();
            goals.Bind(view, cfg);
            view.BindGoals(goals);

            // The persistent bottom-corner nav (10.13b) — built at Bind, AFTER every panel it toggles
            // exists, so its verbs route to live bindings.
            var nav = director.AddComponent<NavBar>();
            nav.Bind(view, cfg);

            // The mode-select window + the top-centre verb strip (10.13e — the last interactive IMGUI,
            // now uGUI). ModesWindow is a toggled window (bound so the nav's Modes button targets it);
            // TopControls is a persistent HUD strip in the NavBar mold (built at Bind, polls change-only).
            var modesWindow = director.AddComponent<ModesWindow>();
            modesWindow.Bind(view, cfg);
            view.BindModes(modesWindow);

            var topControls = director.AddComponent<TopControls>();
            topControls.Bind(view, cfg);

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

            // 10.14b — the 30-second-session ARRIVAL (mobile arc MM2): ONE card replaces the old idle +
            // daily modals. IdleClaim and DailyLogin reveal TOGETHER at S3 (Progression.FeatureRevealStage),
            // so DailyLogin's reveal gates the whole arrival card. The goals half (daily) is date-driven
            // and NOT FTUE-gated inside GameCore (Goals.Claimables keys off the calendar), so we gate it
            // client-side here — exactly what the old DailyLoginModal boot check did.
            long now = NowMs();
            if (Progression.FeatureUnlocked(Feature.DailyLogin, save))
            {
                // PREVIEW both halves (no grant yet); the card's Collect applies the whole thing atomically
                // via Session.Arrive against this same `now`, so the shown numbers ARE what it banks.
                var idlePreview = Idle.Preview(save, cfg, now);
                var claims = Goals.Claimables(save, cfg, now); // safe: DailyLogin revealed
                if (!idlePreview.IsEmpty || claims.Count > 0)
                {
                    var card = new GameObject("IdleClaimModal");
                    card.transform.SetParent(session.transform);
                    card.AddComponent<IdleClaimModal>().Show(view, idlePreview, claims, now);
                    Debug.Log($"[Bootstrap] Arrival: {idlePreview.Gold} gold, {idlePreview.Xp} XP, " +
                              $"{idlePreview.Items.Count} item(s), {claims.Count} bonus claim(s).");
                }
            }
            else if (!Idle.Preview(save, cfg, now).IsEmpty)
            {
                // Pre-reveal (< S3): the arrival card is suppressed, but offline income must still bank —
                // the old boot claimed idle at load unconditionally. We can't run Session.Arrive here (it
                // would also claim the daily, whose popup S3 gates), so bank idle ALONE and silently. The
                // daily stays unclaimed until the card first greets the player (streaks start then).
                view.BankIdleSilently(now);
            }

            // Live-ops banner (10.16b, mobile arc MM4): announce every event live right now, one feed
            // line each. 10.20c: the name composes CLIENT-side via Loc off the stable id + ZoneIndex
            // (StatDisplay.EventName) — EventInfo.Name is GameCore English and stays compat-only.
            // Countdown display-CEILs the hours from EndMs — the crypt-key countdown rule (ModesWindow).
            foreach (var ev in Events.Active(cfg, now))
            {
                long hrs = (ev.EndMs - now + 3_599_999) / 3_600_000; // ceil to whole hours (never under-promise)
                chat.AddFeed(Loc.F("event.ends-in", StatDisplay.EventName(ev, cfg), hrs), Theme.GachaGold);
            }

            Debug.Log($"[Bootstrap] Session started at stage {save.Progress.CurrentStage}.");
            return view;
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
            cam.transform.rotation = Quaternion.Euler(45f, -45f, 0f); // 45° iso pitch (42° read too top-down under ortho)
            // Diorama compression (Tunic re-pass): go fully ORTHOGRAPHIC so the low-poly world
            // reads like a hand-built model with zero perspective convergence — parallel edges
            // stay parallel, exactly Tunic's flat diorama look. The old FOV 34° is kept only as
            // the frustum half-angle that CameraRig converts to orthographicSize per zoom level
            // (size = distance * tan(FOV/2)), so the framing matches the pre-ortho pass and zoom
            // still works. Field-of-view is inert while orthographic but left for that math.
            cam.orthographic = true;
            cam.fieldOfView = 34f;
            cam.orthographicSize = 40f * 0.225f; // MaxDistance framing (size 9 — CameraRig owns this per zoom)
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
            // Lower sun elevation (38° pitch, same yaw) throws LONGER shadows for the dramatic
            // late-afternoon diorama rake; brighter warm key balances the split-toned cool shade.
            light.transform.rotation = Quaternion.Euler(38f, -30f, 0f);
            light.intensity = 2.6f;                     // strong warm key (variant-matrix winner) — lit areas clearly beat the cool shade
            light.color = new Color(1f, 0.97f, 0.90f);  // warm sun, less yellow than before
            // Crisp grounded shadows read as Tunic, not the old soft murky haze. Still < 1 so
            // shaded sides stay readable (the TunicSurface shader also caps shadow impact).
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.9f;                 // firmer contact (was 0.8)

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
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.60f, 0.56f); // near-neutral (a full-cool fill murked the lit areas)
            RenderSettings.ambientGroundColor = new Color(0.42f, 0.40f, 0.52f);  // only the ground bounce stays cool violet

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

            // Split-toning: warm highlights / cool purple-blue shadows — the core of the Tunic
            // late-afternoon diorama mood. Balance leans slightly toward highlights so the cool
            // tone stays in true shade and never tints the sunlit areas murky.
            var split = profile.Add<UnityEngine.Rendering.Universal.SplitToning>();
            split.shadows.Override(new Color(0.55f, 0.52f, 0.82f));    // cool purple-blue shade
            split.highlights.Override(new Color(1.0f, 0.96f, 0.82f));  // warm sunlit
            split.balance.Override(5f);

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

    /// <summary>
    /// 10.12a frame cap. The game shipped UNCAPPED — a laptop GPU renders an idle scene at
    /// hundreds of fps, which is pure heat (user-reported overheating on a weaker machine).
    /// vSync stays OFF deliberately: vSyncCount &gt; 0 makes Unity IGNORE targetFrameRate and
    /// lock to panel refresh, so a 144 Hz laptop would still run 144 fps hot. Unfocused, an
    /// idle game needs presence, not fluidity — drop hard: the sim steps by accumulated
    /// delta so nothing is lost, and idle accrual covers real absence.
    /// </summary>
    public sealed class FrameCap : MonoBehaviour
    {
        public const int Focused = 60;
        public const int Unfocused = 10;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Focused;
        }

        private void OnApplicationFocus(bool focused)
            => Application.targetFrameRate = focused ? Focused : Unfocused;
    }
}
