#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Zone/dungeon ambience beds (ROADMAP 10.9c): one soft looping environmental bed per zone
    /// theme (wind, waves, lava, cave air…), crossfaded on travel. Clips are curated MS2 AMB-bank
    /// extracts under Resources/Sound/Amb — ENVIRONMENTAL SFX ONLY, never the BGM banks (the
    /// hard no-MS2-music rule; the AMB bank is MS2's own "not music" taxonomy, and the curation
    /// picked pure texture: wind/water/fire/air). Client-only; GameCore knows nothing of it.
    ///
    /// Keyed and IDEMPOTENT: <see cref="SetZone"/>/<see cref="SetDungeon"/> may be called every
    /// farm start / floor entry — a repeated key is a no-op, so callers never track "did the
    /// theme actually change". The two-AudioSource crossfade host doubles as the 10.9(b)
    /// AudioDirector scaffold (music beds will ride the same shape when they exist).
    /// Volume = AmbienceVolume slider, live every frame; deliberately NOT on the SFX duck bus
    /// (a bed is the floor under the mix, not a voice in it).
    /// </summary>
    public static class Ambience
    {
        // Zone theme (ZoneDef.Theme) / dungeon theme (DungeonTheme key) -> clip-set base name
        // (variants AMB_X_01/02 group under AMB_X and rotate randomly per travel).
        private static readonly Dictionary<string, string> Beds = new()
        {
            // overworld zones
            { "forest",  "AMB_Forest" },
            { "ruins",   "AMB_AbandonedPark" },
            { "swamp",   "AMB_Rain" },
            { "desert",  "AMB_Desert" },
            { "tundra",  "AMB_Snow" },
            { "volcano", "Amb_Lava" },
            { "cavern",  "AMB_Cave" },
            { "coast",   "AMB_Beach" },
            { "astral",  "AMB_Space" },
            { "summit",  "PIG_MAP_AMB_Wind_Heavy" },
            // crypt depth tiers (DungeonTheme keys)
            { "crypt",   "AMB_Dark_Dungeon" },
            { "molten",  "AMB_LavaCave" },
            { "frost",   "AMB_Cold_Wind" },
        };

        private const float CrossfadeSec = 1.6f;
        private const float BedVolume = 0.35f; // base gain under the slider — a floor, not a voice

        private static Dictionary<string, AudioClip[]>? _sets;
        private static AmbienceHost? _host;
        private static string? _key; // current theme key (the idempotence guard)

        /// <summary>Overworld travel beat: swap to <paramref name="zoneTheme"/>'s bed (no-op when
        /// unchanged). Safe to call on every farm start/resume, exactly like ZoneDress.Sync.</summary>
        public static void SetZone(string? zoneTheme) => SetBed(zoneTheme);

        /// <summary>Dungeon floor entry: the depth tier's bed (crypt/molten/frost).</summary>
        public static void SetDungeon(string? dungeonTheme) => SetBed(dungeonTheme);

        private static void SetBed(string? themeKey)
        {
            if (string.IsNullOrEmpty(themeKey) || themeKey == _key) return;
            if (!Beds.TryGetValue(themeKey!, out var setName)) return; // unmapped theme: keep the old bed
            _key = themeKey;

            var sets = Sets();
            if (!sets.TryGetValue(setName, out var clips) || clips.Length == 0) return;
            Host().Crossfade(clips[Random.Range(0, clips.Length)], CrossfadeSec);
        }

        /// <summary>Resources/Sound/Amb clips grouped by name minus a _NN suffix (SoundFx's rule).</summary>
        private static Dictionary<string, AudioClip[]> Sets()
        {
            if (_sets != null) return _sets;
            var groups = new Dictionary<string, List<AudioClip>>();
            foreach (var clip in Resources.LoadAll<AudioClip>("Sound/Amb"))
            {
                string key = clip.name;
                int us = key.LastIndexOf('_');
                if (us > 0 && int.TryParse(key.Substring(us + 1), out _))
                    key = key.Substring(0, us);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<AudioClip>();
                list.Add(clip);
            }
            _sets = new Dictionary<string, AudioClip[]>();
            foreach (var kv in groups) _sets[kv.Key] = kv.Value.ToArray();
            return _sets;
        }

        private static AmbienceHost Host()
        {
            if (_host == null)
            {
                var go = new GameObject("Ambience");
                Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<AmbienceHost>();
            }
            return _host;
        }

        /// <summary>Two looping AudioSources crossfaded on UNSCALED time (a hit-stop dip or the
        /// 2× alt-mode clock must never warp a fade), target volume re-read every frame so the
        /// Settings slider is live. The 10.9(b) music director will reuse this exact shape.</summary>
        public sealed class AmbienceHost : MonoBehaviour
        {
            private AudioSource _a = null!, _b = null!;
            private AudioSource? _active;   // the source fading IN (or holding steady)
            private float _fadeT, _fadeDur; // countdown of the current crossfade

            private void Awake()
            {
                _a = NewSource();
                _b = NewSource();
            }

            private AudioSource NewSource()
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.loop = true;
                src.spatialBlend = 0f;
                src.playOnAwake = false;
                src.volume = 0f;
                return src;
            }

            public void Crossfade(AudioClip clip, float sec)
            {
                var next = _active == _a ? _b : _a;
                next.clip = clip;
                next.time = 0f;
                next.Play();
                _active = next;
                _fadeDur = Mathf.Max(0.05f, sec);
                _fadeT = _fadeDur;
            }

            private void Update()
            {
                float target = BedVolume * Settings.AmbienceVolume; // live slider
                if (_fadeT > 0f) _fadeT = Mathf.Max(0f, _fadeT - Time.unscaledDeltaTime);
                float mix = _fadeDur > 0f ? 1f - _fadeT / _fadeDur : 1f; // 0 -> 1 over the fade

                var fadingOut = _active == _a ? _b : _a;
                if (_active != null) _active.volume = target * mix;
                fadingOut.volume = target * (1f - mix);
                if (mix >= 1f && fadingOut.isPlaying) fadingOut.Stop(); // fully faded: release the voice
            }
        }
    }
}
