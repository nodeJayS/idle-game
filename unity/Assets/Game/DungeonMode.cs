#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Client coordinator for a playable dungeon RUN (roguelite slice 3b). Mirrors ZoneDress's role:
    /// it owns the WORLD SWAP that turns the overworld into the crypt dungeon and back — nothing here
    /// decides rules, it only drives Unity presentation and calls the existing sim constructors
    /// (<see cref="Combat.InitDungeon"/>) so a MonoBehaviour never authors game state.
    ///
    /// Enter: seed a dungeon deterministically from save progress, generate it (slice 1), build a
    /// FRESH dungeon CombatState via <see cref="Combat.InitDungeon"/> (slice 3a — full mode isolation,
    /// the campaign state is never mutated), deactivate the overworld roots, build the dungeon visual
    /// (slice 2 renderer) under "DungeonWorld", and STASH → restage the scene lighting/fog/ambient/
    /// camera-clear for the crypt look. Exit: tear the visual down and restore everything exactly;
    /// the caller then rebuilds the campaign state from scratch (StartFarm). This is a static
    /// singleton because there is at most one live run at a time.
    /// </summary>
    public static class DungeonMode
    {
        public static bool Active { get; private set; }

        // Per-SESSION entry counter: the FIRST entry at a given stage is reproducible (seed derives
        // purely from stage), but re-entering within a session advances this so the layout re-rolls —
        // a dev convenience while iterating. Not persisted, so a fresh launch replays the first layout.
        private static int _entryCounter;

        // Root of the built dungeon visual (DungeonRenderer output), destroyed on Exit.
        private static GameObject? _worldRoot;

        // Overworld roots we deactivated on Enter, remembered so Exit only re-activates what WAS active
        // (a zone with no arena has no ArenaTerrain/ArenaWater, so we must not blindly turn them on).
        private static readonly System.Collections.Generic.List<GameObject> _hidden =
            new System.Collections.Generic.List<GameObject>();

        // ---- stashed scene state (restored verbatim on Exit) ----
        private static bool _stashed;
        private static bool _fog;
        private static FogMode _fogMode;
        private static Color _fogColor;
        private static float _fogDensity, _fogStart, _fogEnd;
        private static AmbientMode _ambMode;
        private static Color _ambSky, _ambEquator, _ambGround;
        private static Color _sunColor;
        private static float _sunIntensity;
        private static Color _camBg;

        // The overworld root names we swap out. Null-safe: any that don't exist this zone are skipped.
        private static readonly string[] OverworldRoots = { "Ground", "ArenaTerrain", "ArenaWater", "Scenery" };

        // Gloom Hollow cast — the crypt test floor's roster (ids confirmed present in GameConfig; the
        // trash roster falls back inside Combat.InitDungeon if a config drops one).
        private static readonly string[] TrashRoster = { "cave_bat", "gloom_shade" };
        private const string BossId = "nightmare_maw";

        /// <summary>
        /// Generate the run's dungeon (pure data; the caller shows its seeded name on the loading
        /// screen BEFORE any world change happens). Deterministic seed from farm depth, salted, then
        /// advanced by the session entry counter so the FIRST dungeon at a given stage is reproducible
        /// while re-entries re-roll the layout.
        /// </summary>
        public static Dungeon Generate(SaveState save, string themeKey = "crypt")
        {
            int stage = save.Progress.CurrentStage;
            int seed = unchecked((stage * (int)2654435761u ^ 0x5EED) + _entryCounter);
            _entryCounter++;

            return DungeonGen.Generate(new DungeonParams
            {
                Seed = seed,
                RoomCount = 26,       // a test floor — walkable in minutes, not the 80-room showpiece
                LoopChance = 0.15,
                DecorDensity = 0.6,
                Theme = themeKey,
            });
        }

        /// <summary>
        /// Swap the world to <paramref name="dungeon"/> and return a BRAND-NEW dungeon CombatState —
        /// the campaign state is never touched. Fresh-state isolation is the fix for the mode-leak the
        /// user hit live: the old in-place transition left dungeon grid positions behind, so returning
        /// clamped the party (and thus every spawn ring) onto the campaign arena's rim. Now each mode's
        /// state is built from scratch on entry and dropped wholesale on exit — nothing CAN leak.
        /// Runs AT FULL BLACK behind the LoadingScreen (the caller orchestrates), so neither the
        /// teardown nor the camera snap is ever on screen.
        /// <paramref name="rng"/> is threaded to the sim for the spawn stagger (never UnityEngine.Random).
        /// </summary>
        public static CombatState Enter(GameConfig cfg, System.Collections.Generic.IReadOnlyList<HeroInstance> party,
                                        SaveState save, Rng rng, Dungeon dungeon, string themeKey = "crypt")
        {
            // Fresh sim state (party at the entrance, authored spawns materialised). Guard the boss id
            // against a config that dropped it (the sim tolerates "" and just spawns no boss).
            string boss = cfg.Monsters.ContainsKey(BossId) ? BossId : "";
            var state = Combat.InitDungeon(party, save.Progress.CurrentStage, dungeon, TrashRoster, boss, cfg, rng);

            SwapWorldIn(dungeon, themeKey);
            Active = true;
            return state;
        }

        /// <summary>Tear down the dungeon visual, restore the stashed scene state, and reactivate the
        /// overworld roots that were active on Enter. Safe (no-op) when not <see cref="Active"/>.</summary>
        public static void Exit()
        {
            if (!Active) return;

            if (_worldRoot != null) Object.Destroy(_worldRoot);
            _worldRoot = null;

            RestoreStash();

            foreach (var go in _hidden) if (go != null) go.SetActive(true);
            _hidden.Clear();

            Active = false;
        }

        // ---- world swap (private) ------------------------------------------

        private static void SwapWorldIn(Dungeon d, string themeKey)
        {
            var theme = DungeonTheme.Get(themeKey);

            // Deactivate the overworld roots (remember which were active so Exit is symmetric).
            _hidden.Clear();
            foreach (var name in OverworldRoots)
            {
                var go = GameObject.Find(name);
                if (go != null && go.activeSelf) { go.SetActive(false); _hidden.Add(go); }
            }

            // Build the dungeon geometry/props/lights/flicker under a fresh root.
            _worldRoot = DungeonRenderer.Build(d, themeKey, null, showSpawns: false);
            _worldRoot.name = "DungeonWorld";

            StashAndStage(theme);
        }

        // Stash the current fog/ambient/sun/camera-clear, then restage them for the crypt look. We do
        // NOT touch the game's post Volume (its bloom/vignette already suit the dungeon).
        private static void StashAndStage(DungeonTheme th)
        {
            if (!_stashed)
            {
                _fog = RenderSettings.fog;
                _fogMode = RenderSettings.fogMode;
                _fogColor = RenderSettings.fogColor;
                _fogDensity = RenderSettings.fogDensity;
                _fogStart = RenderSettings.fogStartDistance;
                _fogEnd = RenderSettings.fogEndDistance;
                _ambMode = RenderSettings.ambientMode;
                _ambSky = RenderSettings.ambientSkyColor;
                _ambEquator = RenderSettings.ambientEquatorColor;
                _ambGround = RenderSettings.ambientGroundColor;
                _stashed = true;
            }

            // Exponential-squared fog in the theme haze.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = th.Fog;
            RenderSettings.fogDensity = th.FogDensity;

            // Trilight ambient from the theme hemisphere (sky/ground poles; equator = their midpoint),
            // lifted by the theme's authored intensity and the URP ambient scale.
            float ambBoost = th.HemiIntensity * DungeonTheme.UrpAmbientScale;
            Color sky = th.HemiSky * ambBoost;
            Color ground = th.HemiGround * ambBoost;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, ground, 0.5f);

            // The game's "Sun" directional light → faint theme directional (stash colour + intensity).
            // GameplaySunDim: the preview window's DirIntensity × UrpSunScale is calibrated against
            // its own neutral staging; under the GAME's split-tone grade the same value washed the
            // crypt out to near-white (Play screenshot-tuned — 0.35 keeps heroes readable while the
            // torch pools own the mood).
            const float GameplaySunDim = 0.35f;
            var sun = FindSun();
            if (sun != null)
            {
                _sunColor = sun.color;
                _sunIntensity = sun.intensity;
                sun.color = th.DirColor;
                sun.intensity = th.DirIntensity * DungeonTheme.UrpSunScale * GameplaySunDim;
            }

            // Camera clear colour → theme background (near-black), so the void reads dark past the fog.
            var cam = Camera.main;
            if (cam != null)
            {
                _camBg = cam.backgroundColor;
                cam.backgroundColor = th.Bg;
            }
        }

        private static void RestoreStash()
        {
            if (_stashed)
            {
                RenderSettings.fog = _fog;
                RenderSettings.fogMode = _fogMode;
                RenderSettings.fogColor = _fogColor;
                RenderSettings.fogDensity = _fogDensity;
                RenderSettings.fogStartDistance = _fogStart;
                RenderSettings.fogEndDistance = _fogEnd;
                RenderSettings.ambientMode = _ambMode;
                RenderSettings.ambientSkyColor = _ambSky;
                RenderSettings.ambientEquatorColor = _ambEquator;
                RenderSettings.ambientGroundColor = _ambGround;
                _stashed = false;
            }

            var sun = FindSun();
            if (sun != null) { sun.color = _sunColor; sun.intensity = _sunIntensity; }

            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = _camBg;
        }

        // The scene's directional "Sun" (Bootstrap names it so; fall back to any directional light).
        private static Light? FindSun()
        {
            var go = GameObject.Find("Sun");
            if (go != null) { var l = go.GetComponent<Light>(); if (l != null) return l; }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return l;
            return null;
        }
    }
}
