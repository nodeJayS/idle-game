using UnityEngine;
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
            BuildEnvironment();

            // --- session starters (the menu invokes one of these) ---
            void StartSession(SaveState save, IdleReport idleReport)
            {
                var director = new GameObject("CombatDirector");
                var view = director.AddComponent<CombatView>();
                view.Init(save, cfg);

                // autosave the live save (heartbeat + on pause/quit)
                director.AddComponent<Autosave>().Bind(view);

                // manual inventory + item-compare screen (opened from the combat HUD)
                var inventory = director.AddComponent<InventoryView>();
                inventory.Bind(view, cfg);
                view.BindInventory(inventory);

                // "while you were away" modal, if the claim yielded anything
                if (!idleReport.IsEmpty)
                {
                    new GameObject("IdleClaimModal").AddComponent<IdleClaimModal>().Show(idleReport);
                    Debug.Log($"[Bootstrap] Idle claim: {idleReport.Gold} gold, {idleReport.Xp} XP, " +
                              $"{idleReport.Items.Count} item(s) over {idleReport.ElapsedMs / 3600_000.0:F1}h.");
                }

                Debug.Log($"[Bootstrap] Session started at stage {save.Progress.CurrentStage}.");
            }

            void NewGame()
            {
                var save = Save.NewGame(Seed, cfg, NowMs());
                SaveStore.Save(save); // write immediately so Continue works next launch
                StartSession(save, new IdleReport());
            }

            void Continue()
            {
                var loaded = SaveStore.Load();
                if (loaded == null) { NewGame(); return; } // corrupt/missing -> fall back
                var (save, report) = Idle.Claim(loaded, cfg, NowMs()); // real offline gap
                StartSession(save, report);
            }

            // --- main menu ---
            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            menu.HasSave = SaveStore.Exists();
            menu.OnContinue = Continue;
            menu.OnNewGame = NewGame;
            menu.Open();
        }

        private static void BuildEnvironment()
        {
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
        }
    }
}
