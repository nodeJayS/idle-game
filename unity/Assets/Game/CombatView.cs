#nullable enable
using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Drives and VISUALIZES the deterministic sim (M8 loop). Two modes, both read-only
    /// over <see cref="CombatState"/>:
    ///   • Farm — endless trash you grind as long as you like (rewards committed to the
    ///     save periodically); a wipe just resets the zone.
    ///   • Boss challenge — a 60s timed miniboss/major boss; a win advances the stage,
    ///     a loss/timeout drops you back to farming.
    /// All rules live in <see cref="Combat"/> / <see cref="Progression"/>.
    /// </summary>
    public sealed class CombatView : MonoBehaviour
    {
        private const float OutcomeDelaySec = 1.5f;
        private const int MaxStepsPerFrame = 8;
        private const float MoveSmoothing = 12f;

        private sealed class View
        {
            public GameObject Go = null!;
            public float Height;
            public Color BaseColor;
            public Vector3 BaseScale;   // full size; spawn anim grows toward this
            public bool Spawning;
            public float SpawnT;        // seconds since the view was created
            public float SpawnDelay;    // per-mob stagger so a wave doesn't pop in unison
            public System.Action<View, float>? SpawnFx; // per-frame spawn-in visual (progress 0..1)
        }

        private const float SpawnAnimSec = 0.35f;

        // MonsterDef.SpawnStyle -> spawn-in animation. ADD-ON POINT: register new styles
        // (spider, ghost, shark, …) here and set the monster's SpawnStyle in GameConfig.
        private readonly Dictionary<string, System.Action<View, float>> _spawnEffects = new();

        private void BuildSpawnEffects()
        {
            // grow with a little overshoot pop
            _spawnEffects["pop"] = (v, a) => v.Go.transform.localScale = v.BaseScale * EaseOutBack(a);

            // emerge upward out of the ground
            _spawnEffects["rise"] = (v, a) =>
            {
                v.Go.transform.localScale = v.BaseScale * Mathf.Clamp01(a);
                var p = v.Go.transform.position;
                p.y = Mathf.Lerp(-v.Height, v.Height, a);
                v.Go.transform.position = p;
            };
        }

        // HeroDef/MonsterDef.AttackFx -> cosmetic projectile. ADD-ON POINT: register new
        // ones (arrow, meteor, arrow_rain via Launch's arc) here. "melee" (no entry) =
        // instant damage number, no projectile.
        private readonly Dictionary<string, System.Action<Vector3, Vector3, float, bool>> _projectileFx = new();

        private void BuildProjectileEffects()
        {
            _projectileFx["fireball"] = (from, to, amount, crit) =>
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.name = "Fireball";
                go.transform.localScale = Vector3.one * (crit ? 0.8f : 0.6f);
                Paint(go, new Color(1f, 0.55f, 0.15f));
                Glow(go, new Color(1f, 0.5f, 0.1f) * 2.5f); // make it read against the ground
                go.AddComponent<Projectile>().Launch(from, to, 14f, () => PlayImpact(to, amount, crit));
            };
        }

        /// <summary>Impact feedback (damage number + crit shake/flash), each per its toggle.</summary>
        private void PlayImpact(Vector3 at, double amount, bool crit)
        {
            if (_juice == null) return;
            if (Settings.DamageNumbers) _juice.DamageNumber(at, amount, crit);
            if (crit && Settings.ScreenShake) _juice.Shake(0.15f);
        }

        /// <summary>Resolve an attacker's basic-attack visual hint (hero or monster def).</summary>
        private string AttackFxFor(string? sourceId)
        {
            if (sourceId == null) return "melee";
            var e = _combat.Entities.Find(x => x.Id == sourceId);
            if (e == null) return "melee";
            if (e.Team == Team.Party)
            {
                var hero = _save.Heroes.Find(h => h.Id == e.RefId);
                if (hero != null && _cfg.Heroes.TryGetValue(hero.DefId, out var hd)) return hd.AttackFx;
            }
            else if (_cfg.Monsters.TryGetValue(e.RefId, out var md)) return md.AttackFx;
            return "melee";
        }

        private static readonly Color DownedColor = new Color(0.30f, 0.30f, 0.34f);

        private GameConfig _cfg = null!;
        private SaveState _save = null!;
        private CombatState _combat = null!;
        private Rng _rng = null!;
        private CombatJuice? _juice;
        private InventoryView? _inventory;
        private readonly Dictionary<string, View> _views = new Dictionary<string, View>();

        private double _accMs;
        private float _outcomeTimer;
        private bool _resolved;
        private uint _runCount;

        private Texture2D _white = null!;

        public SaveState CurrentSave => _save;
            public void ReplaceSave(SaveState save)
        {
            _save = save;
            if (_combat != null) Combat.RefreshPartyStats(_combat, _save, _cfg); // equip applies live
        }
        public void BindInventory(InventoryView inv) => _inventory = inv;

        public void Init(SaveState save, GameConfig cfg)
        {
            _save = save;
            _cfg = cfg;
            BuildSpawnEffects();
            BuildProjectileEffects();
            if (Camera.main != null)
            {
                var jgo = new GameObject("CombatJuice");
                jgo.transform.SetParent(transform, false);
                _juice = jgo.AddComponent<CombatJuice>();
                _juice.Init(Camera.main);
            }
            StartFarm();
        }

        // ---- run lifecycle ----

        private List<HeroInstance> BuildParty()
        {
            var party = new List<HeroInstance>();
            foreach (var id in _save.Party)
                if (id != null)
                {
                    var hero = _save.Heroes.Find(h => h.Id == id);
                    if (hero != null) party.Add(hero);
                }
            return party;
        }

        private void StartFarm() => Begin(Combat.InitFarm(BuildParty(), _save.Progress.CurrentStage, _cfg, NewRng()));
        private void StartBoss() => Begin(Combat.InitBossChallenge(BuildParty(), _save.Progress.CurrentStage, _cfg, NewRng()));

        private Rng NewRng() => _rng = new Rng((uint)(_save.RngSeed + _runCount));

        private void Begin(CombatState combat)
        {
            ClearViews();
            _combat = combat;
            ReconcileViews();
            _accMs = 0;
            _outcomeTimer = 0;
            _resolved = false;
            var heroDefs = new List<string>();
            foreach (var e in _combat.Entities)
                if (e.Team == Team.Party)
                    heroDefs.Add(_save.Heroes.Find(h => h.Id == e.RefId)?.DefId + ":" + AttackFxFor(e.Id));
            Debug.Log($"[CombatView] {_combat.Kind} start: stage {_combat.Stage}; party = [{string.Join(", ", heroDefs)}].");
        }

        private void Update()
        {
            if (_combat == null) return;

            if (_combat.Status == CombatStatus.Running)
            {
                _accMs += Time.deltaTime * 1000.0;
                int steps = 0;
                while (_accMs >= Combat.DefaultStepMs && _combat.Status == CombatStatus.Running && steps < MaxStepsPerFrame)
                {
                    HandleEvents(Combat.StepCombat(_combat, Combat.DefaultStepMs, _cfg, _rng));
                    _accMs -= Combat.DefaultStepMs;
                    steps++;
                }
                if (steps == MaxStepsPerFrame) _accMs = 0;

                // Bank progress as it's earned; a level-up recomputes party stats live.
                if (CommitPending()) Combat.RefreshPartyStats(_combat, _save, _cfg);
            }
            else
            {
                if (!_resolved) { _resolved = true; ResolveOutcome(); }
                _outcomeTimer += Time.deltaTime;
                if (_outcomeTimer >= OutcomeDelaySec) { _runCount++; StartFarm(); return; }
            }

            ReconcileViews();
            SyncViews();
        }

        /// <summary>Bank pending loot/XP/gold into the save. Returns true if XP was
        /// granted (so the caller can refresh live party stats).</summary>
        private bool CommitPending()
        {
            bool xp = false;
            if (_combat.PendingLoot.Count > 0)
            {
                _save = Inventory.AddItems(_save, _combat.PendingLoot);
                _combat.PendingLoot.Clear();
            }
            if (_combat.PendingXp > 0)
            {
                _save = Progression.GrantPartyXp(_save, _combat.PendingXp, _cfg);
                _combat.PendingXp = 0;
                xp = true;
            }
            if (_combat.PendingGold > 0)
            {
                _save = Progression.GrantGold(_save, _combat.PendingGold);
                _combat.PendingGold = 0;
            }
            return xp;
        }

        private void ResolveOutcome()
        {
            CommitPending(); // you keep whatever you earned, win or lose

            if (_combat.Kind == EncounterKind.BossChallenge && _combat.Status == CombatStatus.Won)
            {
                _save = Progression.OnStageCleared(_save, _combat.Stage);
                Debug.Log($"[CombatView] Boss cleared — advanced to stage {_save.Progress.CurrentStage} " +
                          $"(highest {_save.Progress.HighestStage}).");
            }
        }

        // ---- player controls (called from the IMGUI bar) ----

        private void GoToStage(int stage)
        {
            try { _save = Progression.SetStage(_save, stage, _cfg); }
            catch (System.ArgumentOutOfRangeException) { return; }
            CommitPending(); _runCount++; StartFarm();
        }

        private void ChallengeBoss() { CommitPending(); _runCount++; StartBoss(); }
        private void FleeToFarm() { CommitPending(); _runCount++; StartFarm(); }

        // ---- views ----

        private void SpawnView(CombatEntity e)
        {
            bool isHero = e.Team == Team.Party;
            var type = (!isHero && e.IsBoss) ? PrimitiveType.Cube : PrimitiveType.Capsule;
            var go = GameObject.CreatePrimitive(type);
            go.name = e.Id;

            float scale = e.IsBoss ? 1.6f : 1f;
            float height = (type == PrimitiveType.Capsule ? 1f : 0.5f) * scale;
            go.transform.position = new Vector3((float)e.Pos.X, height, (float)e.Pos.Y);
            var baseScale = new Vector3(0.7f * scale, 0.9f * scale, 0.7f * scale);

            Color color;
            if (isHero)
            {
                var hero = _save.Heroes.Find(h => h.Id == e.RefId);
                bool ranged = hero != null && _cfg.Heroes.TryGetValue(hero.DefId, out var hd) && hd.Role == "ranged";
                color = ranged ? new Color(0.62f, 0.45f, 0.92f)   // magician = violet
                               : new Color(0.36f, 0.55f, 0.85f);  // melee = blue
            }
            else color = e.IsBoss ? new Color(0.85f, 0.40f, 0.25f) : new Color(0.45f, 0.80f, 0.50f);
            Paint(go, color);

            var view = new View { Go = go, Height = height, BaseColor = color, BaseScale = baseScale };

            // Enemies (trash + boss) animate in per their monster's SpawnStyle (if the
            // toggle is on); heroes are placed instantly at run start.
            if (e.Team == Team.Enemy && Settings.SpawnAnimations)
            {
                string style = _cfg.Monsters.TryGetValue(e.RefId, out var md) ? md.SpawnStyle : "pop";
                view.SpawnFx = _spawnEffects.TryGetValue(style, out var fx) ? fx : _spawnEffects["pop"];
                view.Spawning = true;
                view.SpawnDelay = Random.Range(0f, 0.45f);
                go.transform.localScale = Vector3.zero;
            }
            else
            {
                go.transform.localScale = baseScale;
            }
            _views[e.Id] = view;
        }

        /// <summary>Add views for new (spawned) entities and drop views for pruned/removed ones.</summary>
        private void ReconcileViews()
        {
            var present = new HashSet<string>();
            foreach (var e in _combat.Entities)
            {
                present.Add(e.Id);
                if (!_views.ContainsKey(e.Id)) SpawnView(e);
            }

            List<string>? stale = null;
            foreach (var kv in _views)
                if (!present.Contains(kv.Key)) (stale ??= new List<string>()).Add(kv.Key);
            if (stale != null)
                foreach (var id in stale)
                {
                    if (_views[id].Go != null) Destroy(_views[id].Go);
                    _views.Remove(id);
                }
        }

        private void ClearViews()
        {
            foreach (var v in _views.Values) if (v.Go != null) Destroy(v.Go);
            _views.Clear();
        }

        private void HandleEvents(List<CombatEvent> events)
        {
            foreach (var ev in events)
            {
                switch (ev.Type)
                {
                    case CombatEventType.Hit:
                    {
                        if (_juice == null || ev.TargetId == null) break;
                        if (!_views.TryGetValue(ev.TargetId, out var hv) || hv.Go == null || !hv.Go.activeSelf) break;
                        var head = hv.Go.transform.position + Vector3.up * (hv.Height + 0.6f);

                        // Ranged attackers launch a projectile (impact pops the number);
                        // melee/projectiles-off pops it instantly.
                        string fx = AttackFxFor(ev.SourceId);
                        bool hasFx = _projectileFx.TryGetValue(fx, out var launch);
                        if (Settings.Projectiles && hasFx && ev.SourceId != null &&
                            _views.TryGetValue(ev.SourceId, out var sv) && sv.Go != null)
                        {
                            var muzzle = sv.Go.transform.position + Vector3.up * (sv.Height + 0.4f);
                            launch!(muzzle, head, (float)ev.Amount, ev.Crit);
                        }
                        else
                        {
                            PlayImpact(head, ev.Amount, ev.Crit);
                        }
                        break;
                    }
                    case CombatEventType.Death:
                        if (ev.EntityId != null && _views.TryGetValue(ev.EntityId, out var v) && v.Go != null)
                        {
                            var ent = _combat.Entities.Find(x => x.Id == ev.EntityId);
                            if (ent != null && ent.Team == Team.Party) Paint(v.Go, DownedColor);
                            else v.Go.SetActive(false);
                        }
                        break;
                    case CombatEventType.Respawn:
                        if (ev.EntityId != null && _views.TryGetValue(ev.EntityId, out var rv) && rv.Go != null)
                        {
                            rv.Go.SetActive(true);
                            Paint(rv.Go, rv.BaseColor);
                        }
                        break;
                    case CombatEventType.BossDefeated:
                        if (_juice != null && Settings.ScreenShake) _juice.Shake(0.4f);
                        break;
                    case CombatEventType.LootDrop:
                        if (ev.Item != null && Settings.LootToasts)
                            _juice?.Toast($"{ev.Item.Rarity} {ev.Item.BaseId}", Palette.Rarity(ev.Item.Rarity));
                        break;
                }
            }
        }

        private void SyncViews()
        {
            float t = 1f - Mathf.Exp(-MoveSmoothing * Time.deltaTime);
            foreach (var e in _combat.Entities)
            {
                if (!_views.TryGetValue(e.Id, out var v) || v.Go == null || !v.Go.activeSelf) continue;
                var target = new Vector3((float)e.Pos.X, v.Height, (float)e.Pos.Y);
                v.Go.transform.position = Vector3.Lerp(v.Go.transform.position, target, t);
                if (v.Spawning) AnimateSpawn(v);
            }
        }

        /// <summary>Grow a freshly-spawned view from zero to full size with a little pop.</summary>
        private static void AnimateSpawn(View v)
        {
            v.SpawnT += Time.deltaTime;
            float a = (v.SpawnT - v.SpawnDelay) / SpawnAnimSec;
            if (a <= 0f) { v.Go.transform.localScale = Vector3.zero; return; }
            if (a >= 1f) { v.Go.transform.localScale = v.BaseScale; v.Spawning = false; return; }
            v.SpawnFx?.Invoke(v, a);
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        // ---- IMGUI HUD + control bar (always-on-top; full juice/UI polish later) ----

        private void OnGUI()
        {
            if (_combat == null) return;
            EnsureTextures();

            // Scale the immediate-mode UI by device DPI so the HUD/buttons stay a usable
            // physical size on phones (uGUI panels already scale via CanvasScaler). All
            // draw code below works in this scaled "logical" space.
            float s = UiScale();
            var prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

            DrawHealthBars(s);
            DrawHud(s);
            DrawControlBar();

            GUI.matrix = prevMatrix;
        }

        private static float UiScale()
        {
            float dpi = Screen.dpi;
            return dpi > 0f ? Mathf.Clamp(dpi / 96f, 1f, 3f) : 1f; // 96dpi desktop => 1x
        }

        private void DrawHealthBars(float s)
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var e in _combat.Entities)
            {
                if (!_views.TryGetValue(e.Id, out var v) || v.Go == null || !v.Go.activeSelf) continue;
                var sp = cam.WorldToScreenPoint(v.Go.transform.position + Vector3.up * (v.Height + 0.6f));
                if (sp.z <= 0) continue;

                // WorldToScreenPoint is in real pixels; convert into the scaled GUI space.
                float cx = sp.x / s, cy = (Screen.height - sp.y) / s;

                if (e.Downed)
                {
                    var dl = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
                    dl.normal.textColor = new Color(1f, 0.85f, 0.4f);
                    GUI.Label(new Rect(cx - 30, cy - 4, 60, 16),
                              $"↻ {Mathf.CeilToInt((float)e.RespawnMs / 1000f)}s", dl);
                    continue;
                }
                if (!e.Alive) continue;

                float w = e.IsBoss ? 56f : 34f, h = 5f;
                float x = cx - w / 2f, y = cy;
                float frac = e.MaxHp > 0 ? Mathf.Clamp01((float)(e.Hp / e.MaxHp)) : 0f;
                DrawRect(x - 1, y - 1, w + 2, h + 2, new Color(0f, 0f, 0f, 0.7f));
                DrawRect(x, y, w, h, new Color(0.25f, 0.05f, 0.05f, 0.9f));
                DrawRect(x, y, w * frac, h, e.Team == Team.Party ? new Color(0.35f, 0.75f, 1f) : new Color(0.9f, 0.35f, 0.3f));
            }
        }

        private void DrawHud(float s)
        {
            float sw = Screen.width / s, sh = Screen.height / s;
            // Centered at the top so it clears the account chip / Settings button (top-left).
            var style = new GUIStyle(GUI.skin.label)
            { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            bool major = _cfg.Stages.Find(st => st.Stage == _combat.Stage)?.IsMajorBoss == true;
            long gold = _save.Currencies.TryGetValue("gold", out var g) ? g : 0;
            string mode = _combat.Kind == EncounterKind.Farm ? "Farming" : (major ? "★ MAJOR BOSS" : "Miniboss");
            GUI.Label(new Rect(0, 8, sw, 28),
                      $"Stage {_combat.Stage} · {mode}  ·  highest {_save.Progress.HighestStage}  ·  {Num.Compact(gold)} gold", style);

            if (_combat.Kind == EncounterKind.BossChallenge)
            {
                float remain = Mathf.Max(0f, (float)(_cfg.Balance.BossChallengeSeconds - _combat.TimeMs / 1000.0));
                var timer = new GUIStyle(GUI.skin.label)
                { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                timer.normal.textColor = remain <= 10 ? new Color(1f, 0.4f, 0.35f) : Color.white;
                GUI.Label(new Rect(sw / 2f - 100, 40, 200, 40), $"{remain:0.0}s", timer);
            }

            if (_combat.Status != CombatStatus.Running)
            {
                string banner = _combat.Kind == EncounterKind.BossChallenge
                    ? (_combat.Status == CombatStatus.Won ? "STAGE CLEARED!" : "BOSS FAILED")
                    : "PARTY WIPED";
                var bs = new GUIStyle(GUI.skin.label)
                { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, sh / 2f - 60, sw, 44), banner, bs);
            }
        }

        private void DrawControlBar()
        {
            const float h = 80f, pad = 16f, gap = 12f;
            float sh = Screen.height / UiScale();
            float y = sh - h - pad;
            float x = pad;
            bool invOpen = _inventory != null && _inventory.IsOpen;

            if (Button(x, y, 260, h, invOpen ? "Close Bag" : "Inventory")) _inventory?.Toggle();
            if (invOpen) return; // keep the bar uncluttered while the bag is open
            x += 260 + gap * 2;

            bool running = _combat.Status == CombatStatus.Running;
            if (running && _combat.Kind == EncounterKind.Farm)
            {
                int cur = _save.Progress.CurrentStage;
                int maxStage = Mathf.Min(_save.Progress.HighestStage + 1, _cfg.Stages.Count);

                if (cur > 1 && Button(x, y, h, h, "◀")) GoToStage(cur - 1);
                x += h + gap;
                if (cur < maxStage && Button(x, y, h, h, "▶")) GoToStage(cur + 1);
                x += h + gap * 2;
                if (Button(x, y, 420, h, "Challenge Miniboss")) ChallengeBoss();
            }
            else if (running && _combat.Kind == EncounterKind.BossChallenge)
            {
                if (Button(x, y, 260, h, "Flee")) FleeToFarm();
            }
        }

        private GUIStyle? _btnStyle;
        private GUIStyle BtnStyle => _btnStyle ??= new GUIStyle(GUI.skin.button)
        { fontSize = 28, fontStyle = FontStyle.Bold };

        private bool Button(float x, float y, float w, float h, string label) =>
            GUI.Button(new Rect(x, y, w, h), label, BtnStyle);

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

        /// <summary>Make a painted object emit light (projectiles read against the ground).</summary>
        private static void Glow(GameObject go, Color emission)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var mat = renderer.sharedMaterial;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
        }
    }
}
