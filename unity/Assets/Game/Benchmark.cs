#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        private CombatView _view = null!;

        public void Bind(CombatView view)
        {
            _view = view;
            StartCoroutine(Run());
        }

        private sealed class Phase
        {
            public string Name = "";
            public readonly List<float> FrameMs = new();
            public long GcDelta;
            public int Collections;
        }

        private IEnumerator Run()
        {
            // The daily-login modal greets a fresh save — dismiss it so the tour renders the game,
            // not a dimmed overlay.
            var daily = GameObject.Find("DailyLoginModal");
            if (daily != null) Destroy(daily);

            var results = new List<Phase>();
            yield return Sample("campaign_farm_stage25_mobcap", settleSec: 10f, sampleSec: 30f, results);
            _view.BenchmarkEnterCrypt();
            yield return Sample("crypt_floor1_blobs", settleSec: 12f, sampleSec: 30f, results);
            Write(results);
            Application.Quit();
        }

        private IEnumerator Sample(string name, float settleSec, float sampleSec, List<Phase> results)
        {
            for (float t = 0; t < settleSec; t += Time.unscaledDeltaTime) yield return null;
            var p = new Phase { Name = name };
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

        private static void Write(List<Phase> results)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n  \"gpu\": \"").Append(SystemInfo.graphicsDeviceName)
              .Append("\",\n  \"cpu\": \"").Append(SystemInfo.processorType)
              .Append("\",\n  \"ram_mb\": ").Append(SystemInfo.systemMemorySize)
              .Append(",\n  \"resolution\": \"").Append(Screen.width).Append('x').Append(Screen.height)
              .Append("\",\n  \"phases\": [\n");
            for (int i = 0; i < results.Count; i++)
            {
                var p = results[i];
                p.FrameMs.Sort();
                float avg = 0f;
                foreach (var f in p.FrameMs) avg += f;
                avg /= Mathf.Max(1, p.FrameMs.Count);
                float p95 = p.FrameMs.Count > 0 ? p.FrameMs[Mathf.Min(p.FrameMs.Count - 1, (int)(p.FrameMs.Count * 0.95f))] : 0f;
                float max = p.FrameMs.Count > 0 ? p.FrameMs[p.FrameMs.Count - 1] : 0f;
                sb.Append("    {\"name\": \"").Append(p.Name)
                  .Append("\", \"frames\": ").Append(p.FrameMs.Count)
                  .Append(", \"avg_ms\": ").Append(avg.ToString("0.00"))
                  .Append(", \"p95_ms\": ").Append(p95.ToString("0.00"))
                  .Append(", \"max_ms\": ").Append(max.ToString("0.00"))
                  .Append(", \"gc_kb_per_frame\": ").Append((p.GcDelta / 1024f / Mathf.Max(1, p.FrameMs.Count)).ToString("0.0"))
                  .Append(", \"gc_gen0_collections\": ").Append(p.Collections)
                  .Append('}').Append(i < results.Count - 1 ? "," : "").Append('\n');
            }
            sb.Append("  ]\n}\n");
            string path = System.IO.Path.Combine(Application.persistentDataPath, "benchmark.json");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"[Benchmark] wrote {path}");
        }
    }
}
