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

            // Fixed-timestep render interpolation: the previous and current sim positions
            // (no lunge offset). Each frame the renderer draws Lerp(PrevPos, CurPos, alpha),
            // so motion is locked to real time and immune to the sim's 30Hz step cadence.
            // SmoothPos holds that interpolated result — read by the camera so it tracks the
            // on-screen centroid, not the raw stepping sim position.
            public Vector3 PrevPos;
            public Vector3 CurPos;
            public Vector3 SmoothPos;

            // Attack/cast tell (M11): a quick punch toward the target (or upward for a
            // cast) on each action. Duration scales inversely with AtkSpd, so faster
            // actors snap; LungeDir is a world vector, LungeMag its reach.
            public float LungeT, LungeDur, LungeMag;
            public Vector3 LungeDir;

            // Hit reaction: a brief recoil away from the attacker (same decaying-offset
            // mechanism as the lunge). LastHitDir is remembered so the death crumple can
            // knock the corpse back in the direction of the killing blow.
            public float KnockT, KnockDur, KnockMag;
            public Vector3 KnockDir;
            public Vector3 LastHitDir;
        }

        private const float BaseLungeSec = 0.18f;

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

            // Magician firebolt: a fat, hot meteor lobbed at the target. Routed through the
            // projectile path (not the cast-time skill FX) so the damage number pops on
            // IMPACT, in sync with the meteor landing — like the basic fireball does.
            _projectileFx["firebolt"] = (from, to, amount, crit) =>
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.name = "Meteor";
                go.transform.localScale = Vector3.one * 1.1f;
                Paint(go, new Color(1f, 0.45f, 0.1f));
                Glow(go, new Color(1f, 0.4f, 0.05f) * 3.5f);
                go.AddComponent<Projectile>().Launch(from, to, 16f,
                    () => { PlayImpact(to, amount, crit); Burst(to, 1.0f, new Color(1f, 0.5f, 0.15f)); }, arc: 2.5f);
            };
        }

        // SkillDef.Sprite -> cast flourish (purely cosmetic; the sim already applied the
        // effect and per-victim damage numbers ride the Hit events, so these draw no
        // numbers). Keyed by sprite hint so several skills can share a look. ADD-ON
        // POINT: register a sprite here and set it on the SkillDef in GameConfig.
        // Args: (caster view, primary-target view) — target == caster for self casts.
        private readonly Dictionary<string, System.Action<View, View>> _skillFx = new();

        private void BuildSkillEffects()
        {
            // (Firebolt's meteor lives in _projectileFx so its number pops on impact;
            // the entries here are instant/area flourishes drawn at cast time.)

            // Warrior cleave: an expanding orange shockwave on the ground at the target.
            _skillFx["cleave"] = (src, tgt) => GroundRing(GroundAt(tgt), 1.8f, new Color(1f, 0.65f, 0.2f), 0.35f);

            // Boss quake: a big red ground wave at the boss + a shake.
            _skillFx["quake"] = (src, tgt) =>
            {
                GroundRing(GroundAt(src), 3.2f, new Color(1f, 0.3f, 0.2f), 0.5f);
                if (Settings.ScreenShake) _rig?.Shake(0.25f);
            };

            // Mend: a green sparkle that rises off the healed ally (the +N number rides
            // the separate Heal event).
            _skillFx["mend"] = (src, tgt) =>
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.name = "HealSparkle";
                go.transform.position = HeadOf(tgt);
                Paint(go, new Color(0.5f, 1f, 0.55f));
                Glow(go, new Color(0.4f, 1f, 0.45f) * 2.5f);
                go.AddComponent<TransientFx>().Configure(0.7f, Vector3.one * 0.5f, Vector3.one * 0.1f, rise: 1.6f);
            };

            // War cry: a gold aura that flares around the caster and tracks it briefly.
            _skillFx["warcry"] = (src, tgt) =>
            {
                if (src.Go == null) return;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.name = "WarCryAura";
                go.transform.position = src.Go.transform.position;
                Paint(go, new Color(1f, 0.85f, 0.35f));
                Glow(go, new Color(1f, 0.8f, 0.3f) * 2.5f);
                go.AddComponent<TransientFx>()
                  .Configure(0.6f, Vector3.one * 0.4f, Vector3.one * (src.BaseScale.x * 2.6f), follow: src.Go.transform);
            };
        }

        // ---- skill-FX geometry helpers (positions read from views) ----

        private static Vector3 HeadOf(View v) => v.Go.transform.position + Vector3.up * (v.Height + 0.6f);
        private static Vector3 GroundAt(View v) { var p = v.Go.transform.position; p.y = 0.06f; return p; }

        /// <summary>A flat disc that expands outward and fades — a shockwave on the ground.</summary>
        private void GroundRing(Vector3 at, float radius, Color color, float life)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.name = "GroundRing";
            go.transform.position = at;
            Paint(go, color);
            Glow(go, color * 2.2f);
            // Cylinder is 2 units tall at scale 1 and 1 unit wide; flatten it to a disc.
            var from = new Vector3(0.4f, 0.02f, 0.4f);
            var to = new Vector3(radius * 2f, 0.02f, radius * 2f);
            go.AddComponent<TransientFx>().Configure(life, from, to);
        }

        /// <summary>A quick bright pop at a point (skill impact flash).</summary>
        private void Burst(Vector3 at, float size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.name = "Burst";
            go.transform.position = at;
            Paint(go, color);
            Glow(go, color * 3f);
            go.AddComponent<TransientFx>().Configure(0.3f, Vector3.one * (size * 0.4f), Vector3.one * (size * 1.6f));
        }

        /// <summary>Impact feedback (damage number + crit shake/flash), each per its toggle.</summary>
        private void PlayImpact(Vector3 at, double amount, bool crit)
        {
            if (_juice == null) return;
            if (Settings.DamageNumbers) _juice.DamageNumber(at, amount, crit);
            if (crit && Settings.ScreenShake) _rig?.Shake(0.15f);
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
        private CameraRig? _rig;
        private InventoryView? _inventory;
        private EquipmentView? _equipment;
        private ChatPanel? _chat;
        private readonly Dictionary<string, View> _views = new Dictionary<string, View>();

        private double _accMs;
        private bool _steppedThisFrame; // did the sim advance this frame? (rolls render snapshots)
        private float _renderAlpha;     // 0..1 fraction into the current fixed step, for interpolation
        private float _outcomeTimer;
        private bool _resolved;
        private uint _runCount;
        private bool _bagFullWarned; // throttles the "bag full" feed line

        private Texture2D _white = null!;

        public SaveState CurrentSave => _save;
            public void ReplaceSave(SaveState save)
        {
            _save = save;
            if (_combat != null) Combat.RefreshPartyStats(_combat, _save, _cfg); // equip applies live
        }

        /// <summary>Party swaps are only allowed mid-run while farming — not during a boss
        /// challenge or after a wipe. The roster reads this to gate its Field/Bench controls.</summary>
        public bool CanEditParty => _combat != null
            && _combat.Kind == EncounterKind.Farm && _combat.Status == CombatStatus.Running;

        /// <summary>Apply a roster field/bench to the LIVE farm without restarting: persist the
        /// new save and hot-swap the party's combat entities in place so the run continues
        /// uninterrupted. Outside a running farm it only persists (the roster disables swaps
        /// there anyway). Safe to call from a UI click handler.</summary>
        public void ApplyPartyEdit(SaveState save)
        {
            _save = save;
            if (CanEditParty)
            {
                Combat.ReconcileParty(_combat, _save, _cfg);
                ReconcileViews();
            }
        }
        public void BindInventory(InventoryView inv) => _inventory = inv;
        public void BindEquipment(EquipmentView eq) => _equipment = eq;
        public void BindChat(ChatPanel chat) => _chat = chat;

        private bool AnyPanelOpen => (_inventory != null && _inventory.IsOpen)
                                  || (_equipment != null && _equipment.IsOpen);

        public void Init(SaveState save, GameConfig cfg)
        {
            _save = save;
            _cfg = cfg;
            BuildSpawnEffects();
            BuildProjectileEffects();
            BuildSkillEffects();
            if (Camera.main != null)
            {
                var jgo = new GameObject("CombatJuice");
                jgo.transform.SetParent(transform, false);
                _juice = jgo.AddComponent<CombatJuice>();
                _juice.Init(Camera.main);

                // the camera persists across Quit-to-Menu, so reuse its rig rather than stacking
                _rig = Camera.main.GetComponent<CameraRig>() ?? Camera.main.gameObject.AddComponent<CameraRig>();
                _rig.Init(Camera.main);
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
            _combat.Tactic = PartyTactic.Group; // the party always moves/fights as a cohesive group
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
            _steppedThisFrame = false;

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

                // Render-interpolation bookkeeping: whether the sim advanced (so SyncViews
                // rolls its position snapshots) and how far into the current step we are, so
                // units draw Lerp(prev,cur,alpha) locked to real time — no 30Hz step beat.
                _steppedThisFrame = steps > 0;
                _renderAlpha = Mathf.Clamp01((float)(_accMs / Combat.DefaultStepMs));

                // Bank progress as it's earned; a level-up recomputes party stats live.
                if (CommitPending()) Combat.RefreshPartyStats(_combat, _save, _cfg);
            }
            else
            {
                if (!_resolved) { _resolved = true; ResolveOutcome(); }
                _outcomeTimer += Time.deltaTime;
                // A boss win shows a success popup that auto-advances after ~1s (or OK,
                // which fast-forwards the timer); losses use the longer banner delay.
                bool bossWin = _combat.Kind == EncounterKind.BossChallenge && _combat.Status == CombatStatus.Won;
                float delay = bossWin ? 1.0f : OutcomeDelaySec;
                if (_outcomeTimer >= delay) { _runCount++; StartFarm(); return; }
            }

            ReconcileViews();
            SyncViews();

            // camera follows the party's centre (the group stays cohesive)
            if (_rig != null && TryPartyCentroid(out var focus)) _rig.SetFocus(focus);
        }

        /// <summary>World-space centre of the living party (camera focus); false if all down.
        /// Uses the SMOOTHED render positions (view.SmoothPos), not the raw 30Hz sim positions,
        /// so the camera tracks what's actually on screen and doesn't chop on each sim step
        /// (or on the per-step body-collision nudges).</summary>
        private bool TryPartyCentroid(out Vector3 centroid)
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var e in _combat.Entities)
            {
                if (e.Team != Team.Party || !e.Alive) continue;
                if (_views.TryGetValue(e.Id, out var v) && v.Go != null) { sum += v.SmoothPos; n++; }
            }
            if (n == 0) { centroid = default; return false; }
            centroid = sum / n;
            centroid.y = 0f;
            return true;
        }

        /// <summary>Bank pending loot/XP/gold into the save. Returns true if XP was
        /// granted (so the caller can refresh live party stats).</summary>
        private bool CommitPending()
        {
            bool xp = false;
            if (_combat.PendingLoot.Count > 0)
            {
                // Live farm trash is capped; boss/special-stage clears may overfill the bag.
                bool allowOverflow = _combat.Kind != EncounterKind.Farm;
                var loot = Inventory.AddLoot(_save, _combat.PendingLoot, _cfg, Settings.AutoSalvageMax, allowOverflow);
                _save = loot.Save;
                _combat.PendingLoot.Clear();

                // Warn once when the bag overflows; clear the latch when it has room again.
                if (loot.BagFull)
                {
                    if (!_bagFullWarned)
                    {
                        _chat?.AddFeed("Bag full — new loot left behind. Salvage or enable auto-salvage.",
                                       new Color(1f, 0.55f, 0.4f));
                        _bagFullWarned = true;
                    }
                }
                else if (Inventory.LooseCount(_save) < _cfg.Balance.InventoryCap)
                {
                    _bagFullWarned = false;
                }
            }
            if (_combat.PendingXp > 0)
            {
                int before = PartyLevelSum();
                _save = Progression.GrantPartyXp(_save, _combat.PendingXp, _cfg);
                _combat.PendingXp = 0;
                xp = true;
                if (PartyLevelSum() > before) _chat?.AddFeed("Level up!", new Color(0.5f, 0.85f, 1f));
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
                int cleared = _combat.Stage;
                _save = Progression.OnStageCleared(_save, cleared, _cfg);
                _chat?.AddFeed($"Stage {cleared} cleared!", new Color(0.55f, 0.9f, 0.55f));
            }
        }

        private int PartyLevelSum()
        {
            int sum = 0;
            foreach (var id in _save.Party)
                if (id != null)
                {
                    var h = _save.Heroes.Find(x => x.Id == id);
                    if (h != null) sum += h.Level;
                }
            return sum;
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

        // Per-class low-poly models (in Resources/Characters/, built by tools/blender).
        // Falls back to the coloured capsule if a model is missing.
        private static readonly System.Collections.Generic.Dictionary<string, string> HeroModels = new()
        {
            { "warrior_basic", "Warrior" },
            { "magician_basic", "Mage" },
        };
        private const float HeroModelScale = 1f;    // tune if the model imports too big/small
        private const float HeroModelHeight = 1.7f; // approx model height -> floating health-bar anchor

        private GameObject? TryLoadHeroModel(CombatEntity e)
        {
            var hero = _save.Heroes.Find(h => h.Id == e.RefId);
            if (hero == null || !HeroModels.TryGetValue(hero.DefId, out var name)) return null;
            var prefab = Resources.Load<GameObject>("Characters/" + name);
            if (prefab == null) return null;
            var go = Instantiate(prefab);
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col); // no physics needed
            return go;
        }

        private void SpawnView(CombatEntity e)
        {
            bool isHero = e.Team == Team.Party;
            GameObject go;
            float height;
            Vector3 baseScale;
            Color color = Color.white;

            var model = isHero ? TryLoadHeroModel(e) : null;
            if (model != null)
            {
                go = model;
                go.name = e.Id;
                baseScale = new Vector3(HeroModelScale, HeroModelScale, HeroModelScale);
                go.transform.localScale = baseScale;
                height = HeroModelHeight * HeroModelScale;
                go.transform.position = new Vector3((float)e.Pos.X, 0f, (float)e.Pos.Y); // model origin at the feet
            }
            else
            {
                var type = (!isHero && e.IsBoss) ? PrimitiveType.Cube : PrimitiveType.Capsule;
                go = GameObject.CreatePrimitive(type);
                go.name = e.Id;

                float scale = e.IsBoss ? 1.6f : 1f;
                height = (type == PrimitiveType.Capsule ? 1f : 0.5f) * scale;
                go.transform.position = new Vector3((float)e.Pos.X, height, (float)e.Pos.Y);
                baseScale = new Vector3(0.7f * scale, 0.9f * scale, 0.7f * scale);

                if (isHero)
                {
                    var hero = _save.Heroes.Find(h => h.Id == e.RefId);
                    bool ranged = hero != null && _cfg.Heroes.TryGetValue(hero.DefId, out var hd) && hd.Role == "ranged";
                    color = ranged ? new Color(0.62f, 0.45f, 0.92f)   // magician = violet
                                   : new Color(0.36f, 0.55f, 0.85f);  // melee = blue
                }
                else color = e.IsBoss ? new Color(0.85f, 0.40f, 0.25f) : new Color(0.45f, 0.80f, 0.50f);
                Paint(go, color);
            }

            var view = new View { Go = go, Height = height, BaseColor = color, BaseScale = baseScale,
                                  PrevPos = go.transform.position, CurPos = go.transform.position, SmoothPos = go.transform.position };

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
                if (!_views.ContainsKey(e.Id) && e.Alive) SpawnView(e); // don't re-spawn a view for a corpse mid-death-fx
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
            // Sources that cast a damage skill this step, mapped to the projectile FX their
            // hits should use (a key into _projectileFx) or null for instant/area skills.
            // Their Hit events skip the basic-attack projectile; a single-target projectile
            // skill (firebolt) launches its meteor so the number pops on impact, while area/
            // melee skills pop immediately. A SkillCast precedes its Hits in the list.
            Dictionary<string, string?>? skillHitFx = null;

            foreach (var ev in events)
            {
                switch (ev.Type)
                {
                    case CombatEventType.Hit:
                    {
                        if (_juice == null || ev.TargetId == null) break;
                        if (!_views.TryGetValue(ev.TargetId, out var hv) || hv.Go == null || !hv.Go.activeSelf) break;
                        var head = hv.Go.transform.position + Vector3.up * (hv.Height + 0.6f);

                        ApplyHitReaction(ev.SourceId, hv); // recoil + remember hit direction (for the death crumple)

                        // A skill's damage tick (SkillCast already lunged). A single-target
                        // projectile skill launches its meteor here so the number pops on
                        // impact; area/melee skills (or projectiles off) pop the number now.
                        if (ev.SourceId != null && skillHitFx != null && skillHitFx.TryGetValue(ev.SourceId, out var skKey))
                        {
                            if (skKey != null && Settings.Projectiles && _projectileFx.TryGetValue(skKey, out var skLaunch)
                                && _views.TryGetValue(ev.SourceId, out var ssv) && ssv.Go != null)
                            {
                                var muzzle = ssv.Go.transform.position + Vector3.up * (ssv.Height + 0.4f);
                                skLaunch(muzzle, head, (float)ev.Amount, ev.Crit);
                            }
                            else
                            {
                                PlayImpact(head, ev.Amount, ev.Crit);
                            }
                            break;
                        }

                        TriggerLunge(ev.SourceId, ev.TargetId, towardTarget: true);

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
                    case CombatEventType.SkillCast:
                    {
                        bool isDamage = false;
                        if (ev.SkillId != null && _cfg.Skills.TryGetValue(ev.SkillId, out var sk))
                        {
                            isDamage = sk.Effect == SkillEffectKind.Damage;
                            string key = !string.IsNullOrEmpty(sk.Sprite) ? sk.Sprite! : ev.SkillId;

                            // Single-target damage skill with a projectile FX -> defer the visual
                            // (+ number) to the Hit handler so they land together on impact.
                            bool isProjectile = isDamage && sk.AoeRadius <= 0 && sk.Targeting != "aoe"
                                                && _projectileFx.ContainsKey(key);

                            if (isDamage && ev.SourceId != null)
                                (skillHitFx ??= new Dictionary<string, string?>())[ev.SourceId] = isProjectile ? key : null;

                            // Instant/area flourish drawn now (projectile skills draw on impact).
                            if (!isProjectile && ev.SourceId != null
                                && _skillFx.TryGetValue(key, out var play)
                                && _views.TryGetValue(ev.SourceId, out var csv) && csv.Go != null)
                            {
                                string tgtId = ev.TargetId ?? ev.SourceId;
                                if (_views.TryGetValue(tgtId, out var ctv) && ctv.Go != null)
                                    play(csv, ctv);
                            }
                        }
                        // lunge toward the foe for offensive skills, a small upward cast-pop otherwise
                        TriggerLunge(ev.SourceId, ev.TargetId, towardTarget: isDamage);
                        break;
                    }
                    case CombatEventType.Heal:
                    {
                        if (_juice == null || ev.TargetId == null || !Settings.DamageNumbers) break;
                        if (!_views.TryGetValue(ev.TargetId, out var hev) || hev.Go == null || !hev.Go.activeSelf) break;
                        _juice.HealNumber(hev.Go.transform.position + Vector3.up * (hev.Height + 0.6f), ev.Amount);
                        break;
                    }
                    case CombatEventType.Death:
                        if (ev.EntityId != null && _views.TryGetValue(ev.EntityId, out var v) && v.Go != null)
                        {
                            var ent = _combat.Entities.Find(x => x.Id == ev.EntityId);
                            if (ent != null && ent.Team == Team.Party)
                            {
                                Paint(v.Go, DownedColor); // downed, not dead — keep the view
                            }
                            else
                            {
                                // Enemy died: detach the view and play a knockback + crumple
                                // despawn so it doesn't vanish instantly (and lingers long
                                // enough for an in-flight projectile to land on it).
                                _views.Remove(ev.EntityId);
                                v.Go.AddComponent<DeathFx>()
                                    .Configure(0.45f, v.Go.transform.localScale, v.LastHitDir * 0.6f, sink: 0.4f);
                            }
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
                        if (Settings.ScreenShake) _rig?.Shake(0.4f);
                        break;
                    case CombatEventType.LootDrop:
                        if (ev.Item != null && Settings.LootFeed)
                            _chat?.AddFeed($"{ev.Item.Rarity} {ev.Item.BaseId} (i{ev.Item.ItemLevel})", Palette.Rarity(ev.Item.Rarity));
                        break;
                }
            }
        }

        private void SyncViews()
        {
            foreach (var e in _combat.Entities)
            {
                if (!_views.TryGetValue(e.Id, out var v) || v.Go == null || !v.Go.activeSelf) continue;
                // On a sim step, roll the snapshot forward; between steps hold prev/cur and
                // just advance alpha. Drawing Lerp(prev,cur,alpha) is smooth at any framerate
                // and immune to the 30Hz step beat that made the old exponential ease pulse.
                if (_steppedThisFrame)
                {
                    v.PrevPos = v.CurPos;
                    v.CurPos = new Vector3((float)e.Pos.X, v.Height, (float)e.Pos.Y);
                }
                v.SmoothPos = Vector3.Lerp(v.PrevPos, v.CurPos, _renderAlpha);
                v.Go.transform.position = v.SmoothPos + LungeOffset(v) + KnockOffset(v);
                if (v.Spawning) AnimateSpawn(v);
            }
        }

        /// <summary>Advance a view's attack/cast lunge and return its current offset (a
        /// sin-eased punch out and back). Decays in real time so it scales with AtkSpd.</summary>
        private static Vector3 LungeOffset(View v)
        {
            if (v.LungeT <= 0f) return Vector3.zero;
            v.LungeT = Mathf.Max(0f, v.LungeT - Time.deltaTime);
            float p = 1f - (v.LungeDur > 0f ? v.LungeT / v.LungeDur : 1f); // 0 -> 1
            return v.LungeDir * (v.LungeMag * Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI));
        }

        /// <summary>Advance a view's hit recoil and return its current offset (a quick knock
        /// away from the attacker, then settle).</summary>
        private static Vector3 KnockOffset(View v)
        {
            if (v.KnockT <= 0f) return Vector3.zero;
            v.KnockT = Mathf.Max(0f, v.KnockT - Time.deltaTime);
            float p = 1f - (v.KnockDur > 0f ? v.KnockT / v.KnockDur : 1f); // 0 -> 1
            return v.KnockDir * (v.KnockMag * Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI));
        }

        /// <summary>On a hit, recoil the victim away from the attacker and remember the
        /// direction (so a killing blow's death crumple knocks the corpse the same way).</summary>
        private void ApplyHitReaction(string? sourceId, View hv)
        {
            Vector3 dir = Vector3.back;
            if (sourceId != null && _views.TryGetValue(sourceId, out var sv) && sv.Go != null)
            {
                var d = hv.Go.transform.position - sv.Go.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.0001f) dir = d.normalized;
            }
            hv.LastHitDir = dir;
            hv.KnockDir = dir;
            hv.KnockMag = 0.18f;
            hv.KnockDur = 0.14f;
            hv.KnockT = hv.KnockDur;
        }

        /// <summary>Kick off a lunge on the source view toward the target (or upward for a
        /// self/heal cast). Duration scales inversely with AtkSpd so faster actors snap.</summary>
        private void TriggerLunge(string? sourceId, string? targetId, bool towardTarget)
        {
            if (sourceId == null || !_views.TryGetValue(sourceId, out var sv) || sv.Go == null) return;

            Vector3 dir = Vector3.up;
            float mag = 0.28f;
            if (towardTarget && targetId != null && targetId != sourceId
                && _views.TryGetValue(targetId, out var tv) && tv.Go != null)
            {
                var d = tv.Go.transform.position - sv.Go.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.0001f) { dir = d.normalized; mag = 0.38f; }
            }

            double atkSpd = 1.0;
            var e = _combat.Entities.Find(x => x.Id == sourceId);
            if (e != null) atkSpd = e.EffectiveStat(StatKey.AtkSpd);
            if (atkSpd <= 0.2) atkSpd = 0.2;

            sv.LungeDur = BaseLungeSec / (float)atkSpd;
            sv.LungeT = sv.LungeDur;
            sv.LungeDir = dir;
            sv.LungeMag = mag;
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

            // Route all IMGUI HUD text through the shared UI font. The HUD's GUIStyles leave
            // .font null, so they fall back to GUI.skin.font at draw time — setting it here
            // makes the control bar / stage nav / party HUD match the uGUI font (UiKit.Font).
            GUI.skin.font = UiKit.Font;

            // Scale the immediate-mode UI by device DPI so the HUD/buttons stay a usable
            // physical size on phones (uGUI panels already scale via CanvasScaler). All
            // draw code below works in this scaled "logical" space.
            float s = UiScale();
            var prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

            DrawHealthBars(s);
            DrawHud(s);
            DrawTopControls(s);
            DrawOutcome(s);
            DrawPartyHud(s);
            DrawControlBar();

            GUI.matrix = prevMatrix;
        }

        private static float UiScale()
        {
            // Compact desktop sizing — about half the previous scale (the HUD was way too
            // big). Scales gently with DPI (phones) / screen height; uGUI panels (chat, top
            // bar) scale separately via CanvasScaler, and this now reads in line with them.
            float byDpi = Screen.dpi > 0f ? Screen.dpi / 96f : 1f;
            float byRes = Screen.height / 720f;
            return Mathf.Clamp(Mathf.Max(byDpi, byRes) * 0.62f, 0.58f, 1.8f);
        }

        private void DrawHealthBars(float s)
        {
            if (AnyPanelOpen) return; // these IMGUI bars draw over uGUI panels — hide them while one's open
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
            if (AnyPanelOpen) return; // a full-screen panel (Heroes/Inventory) owns the view
            float sw = Screen.width / s;
            // Top-centre context line (clears the account chip / Settings button at top-left).
            var style = new GUIStyle(GUI.skin.label)
            { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            bool major = _cfg.Stages.Find(st => st.Stage == _combat.Stage)?.IsMajorBoss == true;
            long gold = _save.Currencies.TryGetValue("gold", out var g) ? g : 0;
            // Farm: just the gold readout (the current stage shows in DrawTopControls below).
            // Boss challenge: the boss context (which names the stage) plus gold.
            string ctx = _combat.Kind == EncounterKind.Farm
                ? $"{Num.Compact(gold)} gold"
                : (major ? $"★ MAJOR BOSS — Stage {_combat.Stage}" : $"Miniboss — Stage {_combat.Stage}")
                    + $"  ·  {Num.Compact(gold)} gold";
            GUI.Label(new Rect(0, 8, sw, 28), ctx, style);

            if (_combat.Kind == EncounterKind.BossChallenge)
            {
                float remain = Mathf.Max(0f, (float)(_cfg.Balance.BossChallengeSeconds - _combat.TimeMs / 1000.0));
                var timer = new GUIStyle(GUI.skin.label)
                { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                timer.normal.textColor = remain <= 10 ? new Color(1f, 0.4f, 0.35f) : Color.white;
                GUI.Label(new Rect(sw / 2f - 100, 40, 200, 40), $"{remain:0.0}s", timer);
            }
        }

        /// <summary>Top-centre stage nav + boss challenge/flee, under the context line.</summary>
        private void DrawTopControls(float s)
        {
            if (AnyPanelOpen || _combat.Status != CombatStatus.Running) return;
            float cx = Screen.width / s / 2f;

            if (_combat.Kind == EncounterKind.Farm)
            {
                int cur = _save.Progress.CurrentStage;
                int maxStage = Mathf.Min(_save.Progress.HighestStage + 1, _cfg.Stages.Count);

                var st = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(cx - 90, 44, 180, 40), $"Stage {cur}", st);
                if (cur > 1 && Button(cx - 162, 46, 46, 38, "◀")) GoToStage(cur - 1);
                if (cur < maxStage && Button(cx + 116, 46, 46, 38, "▶")) GoToStage(cur + 1);

                bool major = _cfg.Stages.Find(x => x.Stage == cur)?.IsMajorBoss == true;
                if (Button(cx - 185, 90, 370, 46, major ? "Challenge ★ Major Boss" : "Challenge Miniboss", BtnStyleSm)) ChallengeBoss();
            }
            else if (_combat.Kind == EncounterKind.BossChallenge)
            {
                if (Button(cx - 90, 90, 180, 44, "Flee")) FleeToFarm();
            }
        }

        /// <summary>Outcome overlay: a success popup on a boss win (auto-advances ~1s or OK),
        /// a plain banner on a loss/wipe.</summary>
        private void DrawOutcome(float s)
        {
            if (AnyPanelOpen || _combat.Status == CombatStatus.Running) return;
            float sw = Screen.width / s, sh = Screen.height / s;

            bool bossWin = _combat.Kind == EncounterKind.BossChallenge && _combat.Status == CombatStatus.Won;
            if (bossWin)
            {
                float w = 420f, h = 180f, x = sw / 2f - w / 2f, y = sh / 2f - h / 2f;
                DrawRect(x - 2, y - 2, w + 4, h + 4, new Color(0.40f, 0.70f, 0.45f, 0.95f));
                DrawRect(x, y, w, h, new Color(0.10f, 0.13f, 0.11f, 0.98f));

                var t = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                t.normal.textColor = new Color(0.6f, 0.95f, 0.6f);
                GUI.Label(new Rect(x, y + 24, w, 40), $"Stage {_combat.Stage} cleared!", t);
                var sub = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
                sub.normal.textColor = new Color(0.8f, 0.85f, 0.8f);
                GUI.Label(new Rect(x, y + 70, w, 24), "Advancing to the next stage…", sub);

                if (Button(x + w / 2f - 80, y + h - 60, 160, 44, "OK")) _outcomeTimer = 9999f; // fast-forward
            }
            else
            {
                string banner = _combat.Kind == EncounterKind.BossChallenge ? "BOSS FAILED" : "PARTY WIPED";
                var bs = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, sh / 2f - 60, sw, 44), banner, bs);
            }
        }

        /// <summary>
        /// Party status panel, bottom-right: an HP + mana bar per party hero, read live
        /// from the combat entities. Each chip is clickable and opens that hero's
        /// equipment doll (the only way in, per design). Hidden while a panel is open.
        /// </summary>
        private void DrawPartyHud(float s)
        {
            if (AnyPanelOpen) return;

            const float w = 340f, rowH = 92f, gap = 10f, pad = 18f;
            const float ipad = 16f;       // inner horizontal padding
            float sw = Screen.width / s, sh = Screen.height / s;

            var ids = _save.Party;
            int n = ids.Length;
            float totalH = n * rowH + (n - 1) * gap;
            float x = sw - w - pad;
            float y0 = sh - totalH - pad;
            float bx = x + ipad, bw = w - ipad * 2f;

            for (int i = 0; i < n; i++)
            {
                float y = y0 + i * (rowH + gap);
                string? heroId = ids[i];

                DrawRect(x, y, w, rowH, new Color(0.08f, 0.09f, 0.12f, 0.92f));
                if (heroId == null)
                {
                    GUI.Label(new Rect(x, y, w, rowH), "— empty —", PartyEmptyStyle);
                    continue;
                }

                var e = FindHeroEntity(heroId);
                double hp = e?.Hp ?? 0, maxHp = e?.MaxHp ?? 1;
                double mana = e?.Mana ?? 0, maxMana = e?.MaxMana ?? 0;

                GUI.Label(new Rect(bx, y + 10, bw, 26), HeroDisplayName(heroId), PartyNameStyle);

                // Skill-ready cue: a pulsing gold dot at the top-right of the chip when a
                // skill is off-cooldown + affordable.
                if (e != null && e.Alive && !e.Downed && AnySkillReady(e))
                {
                    float pulse = 0.55f + 0.45f * Mathf.PingPong(Time.time * 2f, 1f);
                    float d = 11f, dx = x + w - d - 12f, dy = y + 12f;
                    DrawRect(dx - 1.5f, dy - 1.5f, d + 3f, d + 3f, new Color(0f, 0f, 0f, 0.5f * pulse));
                    DrawRect(dx, dy, d, d, new Color(1f, 0.85f, 0.35f, pulse));
                }

                // HP bar (with value text)
                DrawBar(bx, y + 46, bw, 16, maxHp > 0 ? Mathf.Clamp01((float)(hp / maxHp)) : 0f,
                        new Color(0.22f, 0.05f, 0.05f, 0.95f), new Color(0.35f, 0.75f, 1f));
                GUI.Label(new Rect(bx, y + 45, bw, 18), $"{Mathf.CeilToInt((float)hp)}/{Mathf.CeilToInt((float)maxHp)}", PartyBarTextStyle);
                // Mana bar
                DrawBar(bx, y + 68, bw, 13, maxMana > 0 ? Mathf.Clamp01((float)(mana / maxMana)) : 0f,
                        new Color(0.05f, 0.06f, 0.14f, 0.95f), new Color(0.45f, 0.55f, 1f));

                if (e != null && e.Downed)
                    GUI.Label(new Rect(bx, y + 10, bw, 26),
                              $"↻ {Mathf.CeilToInt((float)e.RespawnMs / 1000f)}s", PartyDownedStyle);

                // whole chip is a click target -> opens this hero's equipment
                if (GUI.Button(new Rect(x, y, w, rowH), GUIContent.none, GUIStyle.none))
                    _equipment?.Toggle(heroId);
            }
        }

        private void DrawBar(float x, float y, float w, float h, float frac, Color bg, Color fill)
        {
            DrawRect(x, y, w, h, bg);
            DrawRect(x, y, w * frac, h, fill);
        }

        private CombatEntity? FindHeroEntity(string heroId) =>
            _combat.Entities.Find(e => e.RefKind == "hero" && e.RefId == heroId);

        /// <summary>True if any of the entity's skills is off-cooldown and affordable —
        /// drives the Party HUD ready cue. Read-only over the live combat entity.</summary>
        private bool AnySkillReady(CombatEntity e)
        {
            foreach (var id in e.Skills)
            {
                if (!_cfg.Skills.TryGetValue(id, out var sk)) continue;
                if (e.SkillCdMs.TryGetValue(id, out var cd) && cd > 0) continue;
                if (e.Mana < sk.ManaCost) continue;
                return true;
            }
            return false;
        }

        private string HeroDisplayName(string heroId)
        {
            var hero = _save.Heroes.Find(h => h.Id == heroId);
            if (hero != null && _cfg.Heroes.TryGetValue(hero.DefId, out var def) && !string.IsNullOrEmpty(def.Name))
                return def.Name;
            return heroId;
        }

        private GUIStyle? _partyNameStyle, _partyEmptyStyle, _partyDownedStyle, _partyBarTextStyle;
        private GUIStyle PartyNameStyle => _partyNameStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        private GUIStyle PartyEmptyStyle => _partyEmptyStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        private GUIStyle PartyBarTextStyle
        {
            get
            {
                if (_partyBarTextStyle == null)
                {
                    _partyBarTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                    _partyBarTextStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
                }
                return _partyBarTextStyle;
            }
        }
        private GUIStyle PartyDownedStyle
        {
            get
            {
                if (_partyDownedStyle == null)
                {
                    _partyDownedStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
                    _partyDownedStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
                }
                return _partyDownedStyle;
            }
        }

        private void DrawControlBar()
        {
            // the Heroes panel owns the screen (its own Close button dismisses it)
            if (_equipment != null && _equipment.IsOpen) return;
            const float h = 80f, pad = 16f, gap = 12f;
            float sh = Screen.height / UiScale();
            float y = sh - h - pad;
            float x = pad;
            bool invOpen = _inventory != null && _inventory.IsOpen;

            if (Button(x, y, 260, h, invOpen ? "Close Bag" : "Inventory")) _inventory?.Toggle();
            if (invOpen) return; // keep the bar uncluttered while the bag is open
            x += 260 + gap;

            if (Button(x, y, 170, h, "Heroes")) _equipment?.ToggleDefault();
            // (The party always moves as a group now; stage nav + Challenge live in the
            // top-centre HUD — see DrawTopControls.)
        }

        private GUIStyle? _btnStyle;
        private GUIStyle BtnStyle => _btnStyle ??= new GUIStyle(GUI.skin.button)
        { fontSize = 28, fontStyle = FontStyle.Bold };

        private GUIStyle? _btnStyleSm;
        private GUIStyle BtnStyleSm => _btnStyleSm ??= new GUIStyle(GUI.skin.button)
        { fontSize = 20, fontStyle = FontStyle.Bold };

        private bool Button(float x, float y, float w, float h, string label) =>
            GUI.Button(new Rect(x, y, w, h), label, BtnStyle);

        private bool Button(float x, float y, float w, float h, string label, GUIStyle style) =>
            GUI.Button(new Rect(x, y, w, h), label, style);

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
            Bootstrap.MakeMatte(mat); // flat, non-plastic shading to match the stylised look
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
