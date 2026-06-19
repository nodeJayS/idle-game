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

            var topbar = new GameObject("TopBar").AddComponent<TopBar>();
            topbar.transform.SetParent(session.transform);
            topbar.Bind(view, () => QuitToMenu(cfg, session, view));
            topbar.Open();

            var chat = new GameObject("ChatPanel").AddComponent<ChatPanel>();
            chat.transform.SetParent(session.transform);
            chat.Open();
            view.BindChat(chat);

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
            // Must cover the whole roam region (Balance.MapHalfWidth/Depth, now ±200×140)
            // plus margin so the party never walks off onto the void. A Unity plane is
            // 10x10 units, so scale 50 => 500x500 units (±250).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(50f, 1f, 50f);
            CombatView.Paint(ground, new Color(0.18f, 0.35f, 0.22f));
        }
    }
}
