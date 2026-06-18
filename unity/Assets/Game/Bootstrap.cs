using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Scene bootstrap. Builds the isometric environment in code on Play — no
    /// manual editor wiring needed — then hands the party off to <see cref="CombatView"/>
    /// which drives the M1 auto-battle. The renderer only READS game-core state
    /// (Save + CombatState); all logic stays in IdleGame.GameCore. Primitives are
    /// placeholders to be swapped for real art later (pixels or low-poly).
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Boot()
        {
            // --- game-core: a fresh save; CombatView owns the live combat lineup ---
            var cfg = GameConfig.Default();
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var save = Save.NewGame(12345u, cfg, now);

            // DEV (until M6 persistence): no real offline gap exists yet, so simulate
            // one — a few cleared stages + a 3h-old LastClaimAt — to exercise the
            // claim flow. Remove once load/save provides a genuine elapsed time.
            const bool DemoIdle = true;
            if (DemoIdle)
            {
                save.Progress.HighestStage = 5;
                save.Progress.CurrentStage = 6;
                save.LastClaimAt = now - 3L * 3600_000L;
            }

            // --- M5: claim offline accrual before the run starts ---
            var (claimed, idleReport) = Idle.Claim(save, cfg, now);
            save = claimed;

            // --- camera (iso-ish angle) ---
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(9f, 11f, -9f);
            cam.transform.rotation = Quaternion.Euler(38f, -45f, 0f);
            cam.backgroundColor = new Color(0.08f, 0.06f, 0.10f);

            // --- directional light ---
            var light = Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                var lightGo = new GameObject("Sun");
                light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
            }
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1.15f;

            // --- ground plane ---
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
            CombatView.Paint(ground, new Color(0.18f, 0.35f, 0.22f));

            // --- hand off to the M1 auto-combat driver ---
            var director = new GameObject("CombatDirector");
            var view = director.AddComponent<CombatView>();
            view.Init(save, cfg);

            // --- M5: "while you were away" modal, if the claim yielded anything ---
            if (!idleReport.IsEmpty)
            {
                var modalGo = new GameObject("IdleClaimModal");
                modalGo.AddComponent<IdleClaimModal>().Show(idleReport);
                Debug.Log($"[Bootstrap] Idle claim: {idleReport.Gold} gold, {idleReport.Xp} XP, " +
                          $"{idleReport.Items.Count} item(s) over {idleReport.ElapsedMs / 3600_000.0:F1}h.");
            }

            Debug.Log("[Bootstrap] Scene built; CombatView driving M1 auto-combat.");
        }
    }
}
