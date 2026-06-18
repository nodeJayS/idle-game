using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// M1 — drives and VISUALIZES the deterministic auto-battle. This is the
    /// renderer: it only READS <see cref="CombatState"/>; all combat rules live in
    /// <see cref="Combat"/> (IdleGame.GameCore). Per fixed step it calls
    /// <see cref="Combat.StepCombat"/>, then interpolates placeholder primitives
    /// toward the sim's entity positions for smooth motion. Deaths hide the view;
    /// a win/loss restarts the run so the loop is visibly continuous.
    /// </summary>
    public sealed class CombatView : MonoBehaviour
    {
        private const float RestartDelaySec = 2.0f;
        private const int MaxStepsPerFrame = 8;       // anti-spiral on a slow frame
        private const float MoveSmoothing = 12f;       // exp smoothing toward sim pos

        private sealed class View
        {
            public GameObject Go = null!;
            public float Height;                       // world Y (half the primitive height)
        }

        private GameConfig _cfg = null!;
        private SaveState _save = null!;
        private CombatState _combat = null!;
        private Rng _rng = null!;
        private readonly Dictionary<string, View> _views = new Dictionary<string, View>();

        private double _accMs;
        private float _endTimer;
        private uint _runCount;
        private bool _committed;   // has this run's loot been added to the save yet?

        // --- GUI textures (created lazily) ---
        private Texture2D _white = null!;

        public void Init(SaveState save, GameConfig cfg)
        {
            _save = save;
            _cfg = cfg;
            StartRun();
        }

        private void StartRun()
        {
            // clear any previous run's views
            foreach (var v in _views.Values)
                if (v.Go != null) Destroy(v.Go);
            _views.Clear();

            // one RNG threaded through the whole fight (determinism). Offset by the
            // run counter so the auto-restart loop shows varied fights, not a replay.
            _rng = new Rng((uint)(_save.RngSeed + _runCount));

            var party = new List<HeroInstance>();
            foreach (var id in _save.Party)
                if (id != null)
                {
                    var hero = _save.Heroes.Find(h => h.Id == id);
                    if (hero != null) party.Add(hero);
                }

            _combat = Combat.InitCombat(party, _save.Progress.CurrentStage, _cfg, _rng);
            SpawnViews();

            _accMs = 0;
            _endTimer = 0;
            _committed = false;
            Debug.Log($"[CombatView] Run #{_runCount} start: {_combat.Entities.Count} entities, stage {_combat.Stage}.");
        }

        private void SpawnViews()
        {
            // combat Pos is a 2D plane (X, Y) -> world (X, Z); world Y is up.
            foreach (var e in _combat.Entities)
            {
                bool isHero = e.Team == Team.Party;
                var type = (!isHero && e.IsBoss) ? PrimitiveType.Cube : PrimitiveType.Capsule;
                var go = GameObject.CreatePrimitive(type);
                go.name = e.Id;

                float scale = e.IsBoss ? 1.6f : 1f;
                float height = (type == PrimitiveType.Capsule ? 1f : 0.5f) * scale;
                go.transform.position = new Vector3((float)e.Pos.X, height, (float)e.Pos.Y);
                go.transform.localScale = new Vector3(0.7f * scale, 0.9f * scale, 0.7f * scale);

                Color color = isHero
                    ? new Color(0.36f, 0.55f, 0.85f)               // party = blue
                    : (e.IsBoss ? new Color(0.85f, 0.40f, 0.25f)   // boss = orange
                                : new Color(0.45f, 0.80f, 0.50f));  // mobs = green
                Paint(go, color);

                _views[e.Id] = new View { Go = go, Height = height };
            }
        }

        private void Update()
        {
            if (_combat == null) return;

            if (_combat.Status == CombatStatus.Running)
            {
                _accMs += Time.deltaTime * 1000.0;
                int steps = 0;
                while (_accMs >= Combat.DefaultStepMs &&
                       _combat.Status == CombatStatus.Running &&
                       steps < MaxStepsPerFrame)
                {
                    var events = Combat.StepCombat(_combat, Combat.DefaultStepMs, _cfg, _rng);
                    _accMs -= Combat.DefaultStepMs;
                    steps++;
                    HandleEvents(events);
                }
                if (steps == MaxStepsPerFrame) _accMs = 0; // drop backlog after a stall
            }
            else
            {
                // Commit this run's loot to the save once, on the first ended frame.
                if (!_committed)
                {
                    _committed = true;
                    if (_combat.Status == CombatStatus.Won && _combat.PendingLoot.Count > 0)
                    {
                        _save = Inventory.AddItems(_save, _combat.PendingLoot);
                        Debug.Log($"[CombatView] Won — claimed {_combat.PendingLoot.Count} item(s). " +
                                  $"Inventory now {_save.Inventory.Count}.");
                    }
                }

                _endTimer += Time.deltaTime;
                if (_endTimer >= RestartDelaySec)
                {
                    _runCount++;
                    StartRun();
                    return;
                }
            }

            SyncViews();
        }

        private void HandleEvents(List<CombatEvent> events)
        {
            foreach (var ev in events)
            {
                switch (ev.Type)
                {
                    case CombatEventType.Death:
                        if (ev.EntityId != null && _views.TryGetValue(ev.EntityId, out var v) && v.Go != null)
                            v.Go.SetActive(false);
                        break;
                    case CombatEventType.BossDefeated:
                        Debug.Log($"[CombatView] Boss defeated at stage {ev.Stage}.");
                        break;
                    case CombatEventType.LootDrop:
                        if (ev.Item != null)
                            Debug.Log($"[CombatView] Drop: {ev.Item.Rarity} {ev.Item.BaseId} " +
                                      $"(iLvl {ev.Item.ItemLevel}, {ev.Item.Affixes.Count} affixes)");
                        break;
                }
            }
        }

        /// <summary>Interpolate each living view toward its sim position (smooth motion).</summary>
        private void SyncViews()
        {
            float t = 1f - Mathf.Exp(-MoveSmoothing * Time.deltaTime);
            foreach (var e in _combat.Entities)
            {
                if (!_views.TryGetValue(e.Id, out var v) || v.Go == null || !v.Go.activeSelf) continue;
                var target = new Vector3((float)e.Pos.X, v.Height, (float)e.Pos.Y);
                v.Go.transform.position = Vector3.Lerp(v.Go.transform.position, target, t);
            }
        }

        // --- lightweight status + HP-bar overlay (full juice is M6) ---
        private void OnGUI()
        {
            if (_combat == null) return;
            EnsureTextures();

            var cam = Camera.main;
            if (cam != null)
            {
                foreach (var e in _combat.Entities)
                {
                    if (!e.Alive) continue;
                    if (!_views.TryGetValue(e.Id, out var v) || v.Go == null || !v.Go.activeSelf) continue;

                    var head = v.Go.transform.position + Vector3.up * (v.Height + 0.6f);
                    var sp = cam.WorldToScreenPoint(head);
                    if (sp.z <= 0) continue; // behind camera

                    float w = e.IsBoss ? 56f : 34f;
                    float h = 5f;
                    float x = sp.x - w / 2f;
                    float y = Screen.height - sp.y;
                    float frac = e.MaxHp > 0 ? Mathf.Clamp01((float)(e.Hp / e.MaxHp)) : 0f;

                    DrawRect(x - 1, y - 1, w + 2, h + 2, new Color(0f, 0f, 0f, 0.7f));
                    DrawRect(x, y, w, h, new Color(0.25f, 0.05f, 0.05f, 0.9f));
                    Color fill = e.Team == Team.Party
                        ? new Color(0.35f, 0.75f, 1f)
                        : new Color(0.9f, 0.35f, 0.3f);
                    DrawRect(x, y, w * frac, h, fill);
                }
            }

            string status = _combat.Status switch
            {
                CombatStatus.Won  => "VICTORY — restarting…",
                CombatStatus.Lost => "DEFEAT — restarting…",
                _                 => "Auto-combat running",
            };
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(12, 8, 600, 28), $"Stage {_combat.Stage} · {status}", style);
        }

        private void DrawRect(float x, float y, float w, float h, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), _white);
            GUI.color = prev;
        }

        private void EnsureTextures()
        {
            if (_white != null) return;
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }

        /// <summary>Apply a flat URP/Standard-lit material of one color. Shared with Bootstrap.</summary>
        public static void Paint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            renderer.sharedMaterial = mat;
        }
    }
}
