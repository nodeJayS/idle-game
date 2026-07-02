#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// One-shot sound effects + BGM from the extracted MS2 banks
    /// (Resources/Sound/*.mp3, pulled by art/tools/fsb_extract.py).
    /// Play("Swing_Sword") picks a random numbered variant; per-set rate
    /// limiting keeps a mob pack from turning hits into white noise.
    /// </summary>
    public static class SoundFx
    {
        private static AudioSource? _fx;
        private static AudioSource? _bgm;
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

        public static void Play(string set, float volume = 0.5f)
        {
            if (!Sets().TryGetValue(set, out var clips) || clips.Length == 0) return;
            if (_lastPlay.TryGetValue(set, out var t) && Time.unscaledTime - t < MinRepeatSec) return;
            _lastPlay[set] = Time.unscaledTime;
            Channel(ref _fx, "SoundFx", loop: false)
                .PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
        }

        public static void PlayBgm(string clipName, float volume = 0.28f)
        {
            var bgm = Channel(ref _bgm, "Bgm", loop: true);
            if (bgm.isPlaying && bgm.clip != null && bgm.clip.name == clipName) return;
            var clip = Resources.Load<AudioClip>("Sound/" + clipName);
            if (clip == null) return;
            bgm.clip = clip;
            bgm.volume = volume;
            bgm.Play();
        }
    }
}
