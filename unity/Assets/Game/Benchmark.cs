#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IdleGame.Game
{
    /// <summary>
    /// Scripted performance benchmark (10.12d tooling; user call 2026-07-12 — verify weak-spec
    /// perf without waiting on hardware). Launched only by Bootstrap's `-benchmark` branch:
    /// the session is save-sandboxed and UNCAPPED, and this component tours the heavy scenes —
    /// a stage-25 campaign farm the unleveled party can't clear (the screen fills to MobCap:
    /// worst-case density by design) and a blob-dense crypt floor — sampling unscaled frame
    /// times + GC per phase. Writes benchmark.json to persistentDataPath and quits, so any
    /// machine (the weak laptop included) produces a one-click perf report.
    ///
    /// 2026-08-20 (10.12e prep): the farm scene is now swept across the quality LEVERS in one
    /// run, because a single frame-time number cannot say WHICH cut to spend. The sweep isolates
    /// render scale (1.0 / 0.75 / 0.6), shadows, and post separately, so the report answers
    /// "is this machine fill-bound, and does the tier ladder actually buy anything" instead of
    /// leaving it a hunch. Levers are poked straight at the URP asset here — NEVER through
    /// Settings — so an unattended run cannot rewrite the player's persisted graphics prefs.
    /// boot_to_playable_ms is engine-start to first benchmark frame (Bootstrap's in-code scene
    /// build + Prewarm); it excludes pre-engine process launch, so it is a FLOOR for the 10.22
    /// cold-start-under-5s goal, not the whole story.
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        private CombatView _view = null!;
        private float _bootMs;

        public void Bind(CombatView view)
        {
            _view = view;
            _bootMs = Time.realtimeSinceStartup * 1000f;
            StartCoroutine(Run());
        }

        private sealed class Phase
        {
            public string Name = "";
            public float Scale;
            public bool Shadows;
            public bool Post;
            public readonly List<float> FrameMs = new();
            public long GcDelta;
            public int Collections;
        }

        /// <summary>One quality tier, applied directly to the live pipeline (see class note: not
        /// via Settings). Mirrors GraphicsQuality.Apply so the numbers describe the real levers.</summary>
        private static void Tier(float scale, bool shadows, bool post)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = scale;
                urp.shadowDistance = shadows ? Bootstrap.ShadowDistance : 0f;
            }
            var cam = Camera.main;
            if (cam != null && cam.TryGetComponent(out UniversalAdditionalCameraData data))
                data.renderPostProcessing = post;
        }

        private IEnumerator Run()
        {
            // The boot arrival card (10.14b: idle + daily folded into one IdleClaimModal) greets a fresh
            // save — dismiss it so the tour renders the game, not a dimmed overlay.
            var arrival = GameObject.Find("IdleClaimModal");
            if (arrival != null) Destroy(arrival);

            var results = new List<Phase>();
            // Baseline first, with the long settle: the farm has to fill to MobCap before any
            // number means anything. The sweep phases then ride that same settled scene, so a
            // short re-settle (the pipeline swap reallocates render targets) is enough.
            // READING THE SWEEP: it only discriminates on a machine that is actually fill-bound.
            // On a fast desktop the frame is CPU/driver-bound at ~500fps and the scale rows come
            // back flat or even inverted — that is noise, not a finding. Trust p95/p99 over
            // max_ms in the sweep rows; a one-off swap hitch can still land inside a sample.
            yield return Sample("farm_s25_mobcap", 1.0f, true, true, settleSec: 10f, sampleSec: 20f, results);
            yield return Sample("farm_s25_scale075", 0.75f, true, true, settleSec: 3f, sampleSec: 10f, results);
            yield return Sample("farm_s25_scale060", 0.60f, true, true, settleSec: 3f, sampleSec: 10f, results);
            yield return Sample("farm_s25_noshadow", 1.0f, false, true, settleSec: 3f, sampleSec: 10f, results);
            yield return Sample("farm_s25_nopost", 1.0f, true, false, settleSec: 3f, sampleSec: 10f, results);
            yield return Sample("farm_s25_low_tier", 0.60f, false, false, settleSec: 3f, sampleSec: 10f, results);
            _view.BenchmarkEnterCrypt();
            yield return Sample("crypt_floor1_blobs", 1.0f, true, true, settleSec: 12f, sampleSec: 20f, results);
            Write(results, _bootMs);
            Application.Quit();
        }

        private IEnumerator Sample(string name, float scale, bool shadows, bool post,
                                   float settleSec, float sampleSec, List<Phase> results)
        {
            Tier(scale, shadows, post);
            for (float t = 0; t < settleSec; t += Time.unscaledDeltaTime) yield return null;
            var p = new Phase { Name = name, Scale = scale, Shadows = shadows, Post = post };
            long gc0 = GC.GetTotalMemory(false);
            int col0 = GC.CollectionCount(0);
            for (float t = 0; t < sampleSec; t += Time.unscaledDeltaTime)
            {
                p.FrameMs.Add(Time.unscaledDeltaTime * 1000f);
                yield return null;
            }
            p.GcDelta = GC.GetTotalMemory(false) - gc0;
            p.Collections = GC.CollectionCount(0) - col0;
            results.Add(p);
        }

        private static float Pct(List<float> sorted, float q) =>
            sorted.Count > 0 ? sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * q))] : 0f;

        private static void Write(List<Phase> results, float bootMs)
        {
            // A second pass (fullscreen native) must not overwrite the windowed-720p one: the
            // whole point is comparing them. `-benchmarkTag native` -> benchmark_native.json.
            var args = Environment.GetCommandLineArgs();
            string tag = "";
            int ti = Array.IndexOf(args, "-benchmarkTag");
            if (ti >= 0 && ti + 1 < args.Length) tag = "_" + args[ti + 1];

            var sb = new System.Text.StringBuilder();
            sb.Append("{\n  \"gpu\": \"").Append(SystemInfo.graphicsDeviceName)
              .Append("\",\n  \"cpu\": \"").Append(SystemInfo.processorType)
              .Append("\",\n  \"ram_mb\": ").Append(SystemInfo.systemMemorySize)
              .Append(",\n  \"resolution\": \"").Append(Screen.width).Append('x').Append(Screen.height)
              .Append("\",\n  \"fullscreen\": ").Append(Screen.fullScreen ? "true" : "false")
              .Append(",\n  \"boot_to_playable_ms\": ").Append(bootMs.ToString("0"))
              .Append(",\n  \"phases\": [\n");
            for (int i = 0; i < results.Count; i++)
            {
                var p = results[i];
                p.FrameMs.Sort();
                float avg = 0f;
                foreach (var f in p.FrameMs) avg += f;
                avg /= Mathf.Max(1, p.FrameMs.Count);
                sb.Append("    {\"name\": \"").Append(p.Name)
                  .Append("\", \"scale\": ").Append(p.Scale.ToString("0.00"))
                  .Append(", \"shadows\": ").Append(p.Shadows ? "true" : "false")
                  .Append(", \"post\": ").Append(p.Post ? "true" : "false")
                  .Append(", \"frames\": ").Append(p.FrameMs.Count)
                  .Append(", \"avg_ms\": ").Append(avg.ToString("0.00"))
                  .Append(", \"avg_fps\": ").Append((1000f / Mathf.Max(0.001f, avg)).ToString("0"))
                  .Append(", \"p95_ms\": ").Append(Pct(p.FrameMs, 0.95f).ToString("0.00"))
                  .Append(", \"p99_ms\": ").Append(Pct(p.FrameMs, 0.99f).ToString("0.00"))
                  .Append(", \"max_ms\": ").Append((p.FrameMs.Count > 0 ? p.FrameMs[p.FrameMs.Count - 1] : 0f).ToString("0.00"))
                  .Append(", \"gc_kb_per_frame\": ").Append((p.GcDelta / 1024f / Mathf.Max(1, p.FrameMs.Count)).ToString("0.0"))
                  .Append(", \"gc_gen0_collections\": ").Append(p.Collections)
                  .Append('}').Append(i < results.Count - 1 ? "," : "").Append('\n');
            }
            sb.Append("  ]\n}\n");
            string path = System.IO.Path.Combine(Application.persistentDataPath, "benchmark" + tag + ".json");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log("[Benchmark] wrote " + path);
        }
    }
}
