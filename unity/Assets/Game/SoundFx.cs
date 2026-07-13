#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// One-shot sound effects from the extracted MS2 banks
    /// (Resources/Sound/*.mp3, pulled by art/tools/fsb_extract.py).
    /// Play("Swing_Sword") picks a random numbered variant; per-set rate
    /// limiting keeps a mob pack from turning hits into white noise.
    /// No BGM — MS2 soundtracks are too recognizable (user call, 2026-07-02).
    /// </summary>
    public static class SoundFx
    {
        private static AudioSource? _fx;
        private static Dictionary<string, AudioClip[]>? _sets;
        private static readonly Dictionary<string, float> _lastPlay = new();
        private const float MinRepeatSec = 0.06f;

        private static AudioSource Channel(ref AudioSource? src, string name, bool loop)
        {
            if (src == null)
            {
                var go = new GameObject(name);
                Object.DontDestroyOnLoad(go);
                src = go.AddComponent<AudioSource>();
                src.spatialBlend = 0f;
                src.loop = loop;
            }
            return src;
        }

        /// <summary>All Resources/Sound clips grouped by name minus a _NN suffix.</summary>
        private static Dictionary<string, AudioClip[]> Sets()
        {
            if (_sets != null) return _sets;
            var groups = new Dictionary<string, List<AudioClip>>();
            foreach (var clip in Resources.LoadAll<AudioClip>("Sound"))
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

        // ---- 10.6f duck bus: big moments (boss down, massacre streak) briefly pull every
        // OTHER one-shot down so the beat gets sonic space. No Update loop — the envelope is
        // computed lazily from unscaled timestamps at each Play. A 10.9 stinger will play with
        // duckExempt: true so it rides ON TOP of the hole it just carved.
        private static float _duckStart = -999f, _duckEnd = -999f, _duckDepth = 1f;

        /// <summary>Duck subsequent one-shots to <paramref name="depth"/> (0..1 gain), recovering
        /// linearly over <paramref name="sec"/>. A deeper duck replaces a shallower in-flight one;
        /// a shallower request never cuts an active deep duck short.</summary>
        public static void Duck(float depth, float sec)
        {
            float now = Time.unscaledTime;
            if (now < _duckEnd && _duckDepth <= depth) return;
            _duckStart = now;
            _duckEnd = now + Mathf.Max(0.05f, sec);
            _duckDepth = Mathf.Clamp01(depth);
        }

        /// <summary>Current duck gain: instant drop to depth, linear recovery to 1.</summary>
        public static float DuckGain()
        {
            float now = Time.unscaledTime;
            if (now >= _duckEnd) return 1f;
            return Mathf.Lerp(_duckDepth, 1f, Mathf.InverseLerp(_duckStart, _duckEnd, now));
        }

        public static void Play(string set, float volume = 0.5f, bool duckExempt = false)
        {
            if (!Sets().TryGetValue(set, out var clips) || clips.Length == 0) return;
            if (_lastPlay.TryGetValue(set, out var t) && Time.unscaledTime - t < MinRepeatSec) return;
            _lastPlay[set] = Time.unscaledTime;
            // SFX slider composes on top of the master (AudioListener) attenuation; the duck
            // bus composes on top of both (exempt = the big moment's own voice).
            Channel(ref _fx, "SoundFx", loop: false)
                .PlayOneShot(clips[Random.Range(0, clips.Length)],
                             volume * Settings.SfxVolume * (duckExempt ? 1f : DuckGain()));
        }
    }
}
