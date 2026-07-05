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
            public float Height;   // head height — floating-bar anchor, muzzle points
            public float YOffset;  // transform Y: capsule center for primitives, 0 for
                                   // hero models (their pivot is at the FEET — grounded)
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

            // Procedural chibi animation (set for code-built hero puppets). Idle/Walk blend by
            // Moving; the swing fires on a Hit/SkillCast. Null for capsules/enemies.
            public IHeroAnim? Anim;
            public bool Moving;

            // Procedural monster life (set for faceted MODEL enemies AND for SDF-blob enemies; null
            // for the primitive capsule/cube fallback and for heroes). Gait/telegraph/hit/death; the
            // root stays owned by SyncViews. Typed as the shared IMonsterAnim so both the rigid
            // MonsterAnimator (body pivot) and the SdfBlobAnimator (prim nodes) feed through one shape.
            public IMonsterAnim? MonsterAnim;

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
                SoundFx.Play("Skill_Wizard_FireBall_Ball", 0.4f);
                go.AddComponent<Projectile>().Launch(from, to, 14f, () => PlayImpact(to, amount, crit));
            };

            // Priest basic attack: a bright holy bolt (warm white-gold, so it reads as
            // light against both the ground and the fire mage's oranges).
            _projectileFx["holybolt"] = (from, to, amount, crit) =>
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.name = "HolyBolt";
                go.transform.localScale = Vector3.one * (crit ? 0.75f : 0.55f);
                Paint(go, new Color(1f, 0.97f, 0.8f));
                Glow(go, new Color(1f, 0.95f, 0.6f) * 2.5f);
                go.AddComponent<Projectile>().Launch(from, to, 15f, () => PlayImpact(to, amount, crit));
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

            // Ice Mage basic attack: a small pale-ice bolt fired straight (no arc), the frost
            // sibling of the priest's holy bolt. Cold sphere, gentle glow; the number pops on
            // impact. No launch sound — the manifest attack_sound (IceStrike cast) already rides
            // the swing; the extracted IceStrike SPLASH lands on the visible hit instead. (An
            // "IceStrike_Ball" clip does NOT exist in the extract — only Cast + Splash_01..04.)
            _projectileFx["icebolt"] = (from, to, amount, crit) =>
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.name = "IceBolt";
                go.transform.localScale = Vector3.one * (crit ? 0.75f : 0.55f);
                Paint(go, new Color(0.55f, 0.85f, 1f));
                Glow(go, new Color(0.55f, 0.85f, 1f) * 2.5f);
                go.AddComponent<Projectile>().Launch(from, to, 15f,
                    () => { PlayImpact(to, amount, crit); SoundFx.Play("Skill_Wizard_IceStrike_Splash", 0.3f); });
            };

            // Ice Mage Frostbolt skill: a fat icy orb lobbed at the target — the frost twin of
            // the fire mage's meteor. Routed through the projectile path so its number pops on
            // IMPACT; lands with a white-blue frost burst.
            _projectileFx["frostbolt"] = (from, to, amount, crit) =>
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.name = "FrostOrb";
                go.transform.localScale = Vector3.one * 1.0f;
                Paint(go, new Color(0.65f, 0.88f, 1f));
                Glow(go, new Color(0.65f, 0.88f, 1f) * 3f);
                go.AddComponent<Projectile>().Launch(from, to, 16f,
                    () => { PlayImpact(to, amount, crit); Burst(to, 0.9f, new Color(0.75f, 0.92f, 1f)); }, arc: 2.5f);
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

            // Shield Charge: a launch ring at the takeoff spot + an arrival ring on the
            // target — reads as the dash streak without a bespoke particle.
            _skillFx["charge"] = (src, tgt) =>
            {
                GroundRing(GroundAt(src), 0.9f, new Color(0.75f, 0.85f, 1f), 0.25f);
                GroundRing(GroundAt(tgt), 1.2f, new Color(0.75f, 0.85f, 1f), 0.35f);
            };

            // Ice Mage Blizzard: a pale-blue AoE flourish AT THE TARGET — a wide expanding
            // ground ring sized to the skill's AoeRadius, plus a smaller brighter inner ring
            // for a frosty double-pulse. No screen shake (that stays the boss quake's identity).
            _skillFx["blizzard"] = (src, tgt) =>
            {
                GroundRing(GroundAt(tgt), 2.6f, new Color(0.6f, 0.85f, 1f), 0.45f);
                GroundRing(GroundAt(tgt), 1.4f, new Color(0.8f, 0.93f, 1f), 0.35f);
            };

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

        /// <summary>Impact feedback (damage number + crit shake/flash + one impact clang), each
        /// per its toggle. <paramref name="secondary"/> = a splash/chain/thorns hit riding the
        /// same swing: the number still pops, but the clang is quieter (~0.5×) so a swing into a
        /// pack reads as one hit with echoes, not a wall of full-volume clangs.</summary>
        private void PlayImpact(Vector3 at, double amount, bool crit, bool secondary = false)
        {
            if (_juice == null) return;
            if (Settings.DamageNumbers) _juice.DamageNumber(at, amount, crit);
            if (crit && Settings.ScreenShake && !secondary) _rig?.Shake(0.15f);
            float vol = crit ? 0.6f : 0.45f;
            if (secondary) vol *= 0.5f;
            SoundFx.Play("Hit_SwordDefault", vol);
        }

        /// <summary>Present a melee hit's number + impact sound after <paramref name="delaySec"/>
        /// so they land on the VISIBLE sword contact (mid-swing), not the instant the swing starts.
        /// Presentation-only — the sim already applied the damage. Caches the world point at
        /// schedule time (the victim may die/move before contact). Falls through to an immediate
        /// pop when there's no meaningful delay (0 = projectile/skill paths already time it).</summary>
        private void ScheduleImpact(Vector3 at, double amount, bool crit, float delaySec, bool secondary)
        {
            if (delaySec <= 0.001f) { PlayImpact(at, amount, crit, secondary); return; }
            StartCoroutine(ImpactAfter(at, amount, crit, delaySec, secondary));
        }

        private System.Collections.IEnumerator ImpactAfter(Vector3 at, double amount, bool crit, float delaySec, bool secondary)
        {
            yield return new WaitForSeconds(delaySec);
            PlayImpact(at, amount, crit, secondary);
        }

        /// <summary>Time-to-contact of the source hero's current swing (0 for non-hero/ranged
        /// sources, whose numbers ride a projectile impact instead). Used to delay the melee
        /// number/sound onto the visible hit. Generic across heroes via IHeroAnim.</summary>
        private float ContactDelayFor(string? sourceId)
        {
            if (sourceId != null && _views.TryGetValue(sourceId, out var sv) && sv.Anim != null)
                return sv.Anim.AttackContactSec;
            return 0f;
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
        private QuestPanel? _questPanel;
        private ModifierPanel? _modifierPanel;
        private TowerView? _towerView;
        private GachaPanel? _gachaPanel;
        private AchievementsPanel? _achievements;
        private readonly Dictionary<string, View> _views = new Dictionary<string, View>();

        // Persistent in-world leader marker (party-feel batch): a thin flat gold disc that sits on
        // the ground under whoever Party.EffectiveLeader resolves to, following that hero's view
        // each frame. Created lazily; re-targeted when the effective leader (or its entity) changes;
        // hidden while the leader's view is missing (dead/respawning) and re-shown when it's back.
        private GameObject? _leaderMarker;
        private string? _leaderMarkerEntityId; // the entity id the marker currently follows

        private double _accMs;
        private bool _steppedThisFrame; // did the sim advance this frame? (rolls render snapshots)
        private float _renderAlpha;     // 0..1 fraction into the current fixed step, for interpolation
        private float _outcomeTimer;
        private bool _resolved;
        // Auto-advance (push): while on, the party auto-challenges each stage's boss and chains
        // clears with no input, until a boss run FAILS (timeout or wipe) — which clears the flag.
        // A manual flee also clears it. Transient (a push session), not a persisted preference.
        //
        // SHELVED for now (off): each stage should feel meaningful, so we don't want a one-tap
        // skip past the boss walls. The toggle button and the auto-challenge loop are gated on
        // this flag — flip it true to bring the feature back wholesale.
        private const bool AutoAdvanceEnabled = false;
        private bool _autoAdvance;
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
        /// <summary>Set (or clear, via null) the party's formation leader from the roster UI:
        /// persists the choice and applies it to the live fight immediately — it only changes
        /// who the others fall in behind, so no entity reconcile is needed.</summary>
        public void SetLeader(string? heroId)
        {
            _save = Party.SetLeader(_save, heroId);
            if (_combat != null) _combat.LeaderRefId = _save.LeaderHeroId;
        }

        public void BindQuests(QuestPanel quests) => _questPanel = quests;
        public void BindInventory(InventoryView inv) => _inventory = inv;
        public void BindEquipment(EquipmentView eq) => _equipment = eq;
        public void BindChat(ChatPanel chat) => _chat = chat;
        public void BindModifiers(ModifierPanel panel) => _modifierPanel = panel;
        public void BindTower(TowerView panel) => _towerView = panel;
        public void BindGacha(GachaPanel panel) => _gachaPanel = panel;
        public void BindAchievements(AchievementsPanel panel) => _achievements = panel;

        /// <summary>The player's premium-currency (gem) balance — read-only surface the GachaPanel reads to
        /// show affordability. The gems SINK (a roll) still routes through <see cref="RollGacha"/>.</summary>
        public long Gems => _save.Currencies.TryGetValue(_cfg.Balance.PremiumCurrency, out var g) ? g : 0;

        /// <summary>The configured gacha banners (read-only), so the GachaPanel can enumerate them and the
        /// control bar can decide whether to show the Summon button. Empty until slice 3 seeds a banner.</summary>
        public IReadOnlyDictionary<string, GachaBannerDef> Banners => _cfg.Banners;

        /// <summary>Current pity count on a banner (rolls made without drawing its featured hero) — the
        /// GachaPanel reads it to draw the "X / PityCount" progress. Delegates to the GameCore rule.</summary>
        public int GachaPityOf(string bannerId) => Gacha.PityOf(_save, bannerId);

        /// <summary>True if the player can afford one roll of a banner (with a non-empty pool) — the
        /// GachaPanel greys the Roll button off this. Delegates to the GameCore gate.</summary>
        public bool CanRollGacha(string bannerId) => Gacha.CanRoll(_save, _cfg, bannerId);

        /// <summary>Enter a Tower-of-Ascension floor from the TowerView: convert the live encounter
        /// into the bounded tower fight IN PLACE (no scene reset), mirroring the boss challenge. The
        /// outcome is banked in <see cref="ResolveOutcome"/> (win → Tower.RecordClear + any milestone
        /// buff; either way it returns to farming on the same map after the result shows).</summary>
        public void EnterTowerFloor(int floor)
        {
            if (_combat == null || !Tower.CanAttempt(_save, floor, _cfg)) return;
            CommitPending();
            Combat.EnterTower(_combat, floor, _cfg, NewRng());
            DressZone(floor); // tower floors travel the same zones (floor = zone key)
            _accMs = 0; _outcomeTimer = 0; _resolved = false;
            ReconcileViews();
        }

        /// <summary>Hero gacha (roadmap 3): spend gems on one roll of a banner from the GachaPanel. Persists
        /// the returned save + reports the outcome in the feed (roll result with the hero name + NEW/dupe; a
        /// pity trigger and a new-hero join get their own prominent lines, matching the OnStageCleared voice).
        /// Refreshes live party stats when the roll granted XP (a dupe can level a fielded hero) or minted a
        /// new hero — mirrors how EnhanceItem/ApplyPartyEdit reconcile _save. Returns the result so the panel
        /// can play its reveal beat; a no-op result (can't afford / unknown banner) plays nothing.</summary>
        public Gacha.RollResult RollGacha(string bannerId)
        {
            var r = Gacha.Roll(_save, _cfg, bannerId);
            if (!r.Rolled) return r; // no-op (unaffordable / unknown banner) — the panel skips its reveal
            _save = r.Save;

            string name = HeroDefDisplayName(r.HeroDefId);
            if (r.IsNew)
            {
                // A fresh hero joining the roster is a headline beat — same prominent voice as a stage unlock.
                _chat?.AddFeed($"{name} joins the roster!", new Color(1f, 0.82f, 0.32f));
            }
            else
            {
                string bonus = r.DupeScrap != 0 ? $"  (+{Num.CompactFloor(r.DupeXp)} XP, +{Num.CompactFloor(r.DupeScrap)} scrap)"
                                                : $"  (+{Num.CompactFloor(r.DupeXp)} XP)";
                _chat?.AddFeed($"Summon: {name} (dupe){bonus}", new Color(0.72f, 0.80f, 0.95f));
            }
            if (r.PityTriggered)
                _chat?.AddFeed($"Pity! {name} is guaranteed.", new Color(1f, 0.85f, 0.4f));

            // A new hero (AcquireHero) or a dupe's XP (can level a FIELDED hero) both change live party stats.
            if (_combat != null) Combat.RefreshPartyStats(_combat, _save, _cfg);

            // Premium currency was spent (and possibly a hero minted) — flush now so a quit before the
            // 30s autosave can't refund the roll (the same rule as ClaimDailyLogin's gem credit).
            SaveStore.Save(Save.Touch(_save, System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            return r;
        }

        /// <summary>Display name for a hero DEF (not an owned instance) — the GachaPanel/feed need it for the
        /// featured hero and a just-rolled result before it may exist in the roster. Falls back to the def id.</summary>
        public string HeroDefDisplayName(string defId) =>
            _cfg.Heroes.TryGetValue(defId, out var def) && !string.IsNullOrEmpty(def.Name) ? def.Name : defId;

        /// <summary>Toggle a monster modifier on/off (Lever 1) from the ModifierPanel: persist via
        /// the GameCore reducer and re-resolve the live farm's active set so the next spawned packs
        /// gain/lose it (mobs already on the field keep what they spawned with).</summary>
        public void SetModifierActive(string typeId, bool on)
        {
            _save = Modifiers.SetActive(_save, typeId, on, _cfg);
            if (_combat != null) _combat.ActiveModifiers = Modifiers.ResolveActive(_save, _cfg);
        }

        /// <summary>Modifier shop: gamble a mod's tuning with gold+scrap. Persists via the reducer,
        /// re-resolves the live farm's active set (so the tuned potency hits the next spawns), and
        /// reports the roll in the feed. No-op with a feed note if unaffordable.</summary>
        public void UpgradeModifier(string typeId)
        {
            if (!Modifiers.CanUpgrade(_save, _cfg, typeId))
            {
                _chat?.AddFeed("Not enough gold + scrap to upgrade that modifier.", new Color(0.9f, 0.6f, 0.5f));
                return;
            }
            double before = Modifiers.TuningOf(_save, typeId);
            _save = Modifiers.UpgradeModifier(_save, typeId, _cfg);
            double after = Modifiers.TuningOf(_save, typeId);
            if (_combat != null) _combat.ActiveModifiers = Modifiers.ResolveActive(_save, _cfg);

            string nm = _cfg.Modifiers.TryGetValue(typeId, out var d) ? d.Name : typeId;
            double delta = (after - before) * 100.0;
            string sign = delta >= 0 ? "+" : "";
            _chat?.AddFeed($"{nm} tuning rolled {sign}{delta:0.#}% → now +{(after - 1) * 100:0}%",
                           delta >= 0 ? new Color(0.55f, 0.9f, 0.6f) : new Color(0.95f, 0.6f, 0.45f));
        }

        /// <summary>Modifier shop: reset a mod's tuning back to base (free, no refund — the gamble spend is
        /// sunk). Mirrors <see cref="UpgradeModifier"/>'s persist + live re-resolve exactly, routing the
        /// GameCore reducer. The ModifierPanel calls this by name (the contract). No-op with a feed note if
        /// there's nothing to reset.</summary>
        public void ResetModifierTuning(string typeId)
        {
            double before = Modifiers.TuningOf(_save, typeId);
            _save = Modifiers.ResetTuning(_save, typeId);
            if (_combat != null) _combat.ActiveModifiers = Modifiers.ResolveActive(_save, _cfg);

            string nm = _cfg.Modifiers.TryGetValue(typeId, out var d) ? d.Name : typeId;
            if (before <= 1.0)
                _chat?.AddFeed($"{nm} is already at base tuning.", new Color(0.9f, 0.6f, 0.5f));
            else
                _chat?.AddFeed($"{nm} tuning reset to base (+0%).", new Color(0.7f, 0.8f, 0.95f));
        }

        /// <summary>Reforge (item shop): gamble an item's normal affix values with gold+scrap. Persists
        /// via the reducer + reports in the feed. No-op with a note if it can't be reforged/afforded.</summary>
        public void ReforgeItem(string itemId)
        {
            if (!Inventory.CanReforge(_save, itemId, _cfg))
            {
                _chat?.AddFeed("Not enough gold + scrap to reforge that item.", new Color(0.9f, 0.6f, 0.5f));
                return;
            }
            var item = _save.Inventory.Find(i => i.Id == itemId);
            _save = Inventory.Reforge(_save, itemId, _cfg);
            if (_combat != null) Combat.RefreshPartyStats(_combat, _save, _cfg); // reforged worn gear applies at once
            string nm = item != null ? StatDisplay.ItemName(item, _cfg) : "item";
            _chat?.AddFeed($"Reforged {nm} — its affixes re-rolled.", new Color(0.7f, 0.8f, 1f));
        }

        /// <summary>InventoryView's "Sort" button: persist the bag in reading order
        /// (rarity desc, then item level) via the pure reducer.</summary>
        public void SortInventory() => _save = Inventory.Sort(_save, _cfg);

        /// <summary>InventoryView's "Enhance" button: one +1 attempt via the pure
        /// reducer (scrap cost, rng-cursor gamble); report the outcome in the feed.</summary>
        public void EnhanceItem(string itemId)
        {
            var r = Inventory.Enhance(_save, itemId, _cfg);
            if (r == null) return;
            _save = r.Save;
            var item = _save.Inventory.Find(i => i.Id == itemId);
            string name = item != null ? StatDisplay.ItemName(item, _cfg) : "item";
            if (r.Success)
                _chat?.AddFeed($"⚒ Enhanced: {name}", new Color(0.55f, 0.9f, 0.55f));
            else if (r.Dropped)
                _chat?.AddFeed($"⚒ Enhance failed — dropped to +{r.Level}", new Color(1f, 0.55f, 0.4f));
            else
                _chat?.AddFeed($"⚒ Enhance failed (+{r.Level} kept)", new Color(0.85f, 0.75f, 0.5f));
        }

        /// <summary>Mass-salvage (InventoryView's "Salvage all" button): scrap EVERY loose, unlocked item
        /// regardless of rarity via the pure reducer + report the haul in the feed. Equipped gear and
        /// locked items survive. The client arms a two-click confirm because this now destroys rares+.</summary>
        public void SalvageAll()
        {
            var (next, count, scrap) = Inventory.SalvageAll(_save, _cfg);
            if (count == 0)
            {
                _chat?.AddFeed("No loose, unlocked items to salvage.", new Color(0.72f, 0.76f, 0.82f));
                return;
            }
            _save = next;
            _chat?.AddFeed($"Salvaged {count} items  (+{Num.CompactFloor(scrap)} scrap)",
                           new Color(0.7f, 0.8f, 1f));
        }

        /// <summary>InventoryView's per-item Lock toggle: flip whether an item can ever be salvaged,
        /// via the pure reducer. Works on bag AND equipped gear.</summary>
        public void ToggleItemLock(string itemId) => _save = Inventory.ToggleLock(_save, itemId);

        private bool AnyPanelOpen => _launchModals > 0
                                  || (_inventory != null && _inventory.IsOpen)
                                  || (_equipment != null && _equipment.IsOpen)
                                  || (_modifierPanel != null && _modifierPanel.IsOpen)
                                  || (_towerView != null && _towerView.IsOpen)
                                  || (_gachaPanel != null && _gachaPanel.IsOpen)
                                  || (_achievements != null && _achievements.IsOpen);

        // Launch modals (idle claim / daily login) are transient GameObjects, not bound panels, so
        // they register here — the IMGUI HUD (floating HP bars etc.) draws above every uGUI canvas
        // and would otherwise strike through the modal text (AnyPanelOpen gates those draws).
        private int _launchModals;
        public void PushLaunchModal() => _launchModals++;
        public void PopLaunchModal() => _launchModals = Mathf.Max(0, _launchModals - 1);

        public void Init(SaveState save, GameConfig cfg)
        {
            _save = save;
            _cfg = cfg;
            _save = Quests.EnsureBoard(_save, _cfg); // backfill the goal board (new field on older saves)
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

        private void StartFarm() => Begin(Combat.InitFarm(BuildParty(), _save.Progress.CurrentStage, _cfg, NewRng(),
                                                          Modifiers.ResolveActive(_save, _cfg))); // active modifiers (Lever 1)

        private Rng NewRng() => _rng = new Rng((uint)(_save.RngSeed + _runCount));

        private void Begin(CombatState combat)
        {
            ClearViews();
            _combat = combat;
            _combat.Tactic = PartyTactic.Solo;        // formation travel: the leader heads for
            _combat.LeaderRefId = _save.LeaderHeroId; // the pack, the rest hold a triangle behind
                                                      // it (chosen leader, else lowest slot)
            DressZone(_combat.Stage);                 // reskin the world for the stage's zone
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
            _questPanel?.UpdateBoard(_save.Quests, _cfg); // reflect live goal progress

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

                // Auto-advance: while pushing, the moment we're back to farming (run start, or
                // after a win's resume) launch the next boss challenge — chaining clears with no
                // input. A fail clears _autoAdvance (see ResolveOutcome), ending the loop here.
                if (AutoAdvanceEnabled && _autoAdvance && _combat.Kind == EncounterKind.Farm && _combat.Status == CombatStatus.Running)
                    ChallengeBoss();
            }
            else
            {
                if (!_resolved) { _resolved = true; ResolveOutcome(); }
                _outcomeTimer += Time.deltaTime;
                // A boss win shows a success popup that auto-advances after ~1s (or OK,
                // which fast-forwards the timer); losses use the longer banner delay.
                bool bossWin = _combat.Kind == EncounterKind.BossChallenge && _combat.Status == CombatStatus.Won;
                bool towerDone = _combat.Kind == EncounterKind.Tower; // win or lose: brief result, then back to farm
                float delay = (bossWin || towerDone) ? 1.0f : OutcomeDelaySec;
                if (_outcomeTimer >= delay)
                {
                    // Back to farming on the SAME map (no rebuild). A win farms the next stage at
                    // normal cadence; a fail/wipe re-farms the current stage after the anti-spam
                    // cooldown before trash returns.
                    double spawnDelay = bossWin ? _cfg.Balance.SpawnIntervalMs : _cfg.Balance.BossFleeCooldownMs;
                    ResumeFarmInPlace(_save.Progress.CurrentStage, spawnDelay);
                    return;
                }
            }

            ReconcileViews();
            SyncViews();

            // camera follows the party's centre, leaning toward the action (their targets /
            // the next pack) so the approach phase isn't blind at the tighter 1.5x framing;
            // the rig clamps and smooths the lean so heroes always stay well in frame
            if (_rig != null && TryPartyCentroid(out var focus)) _rig.SetFocus(focus, ActionPoint(focus));
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

        /// <summary>Where the fight is, for the camera look-ahead: the centroid of the party's
        /// current attack targets when engaged; otherwise the nearest alive enemy pack's centroid
        /// (alive enemies within 8u of the nearest one); the party centroid itself when nothing
        /// is alive (no lean). Read-only over sim state — display logic, not rules.</summary>
        private Vector3 ActionPoint(Vector3 partyCentroid)
        {
            // Engaged: average the on-screen (smoothed) positions of the party's targets.
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var e in _combat.Entities)
            {
                if (e.Team != Team.Party || !e.Alive || e.TargetId == null) continue;
                if (_views.TryGetValue(e.TargetId, out var tv) && tv.Go != null && tv.Go.activeSelf)
                {
                    sum += tv.SmoothPos; n++;
                }
            }
            if (n > 0) { var c = sum / n; c.y = 0f; return c; }

            // Roaming: nearest alive enemy, then its pack's centroid.
            Vector3 nearest = default; float best = float.MaxValue; bool found = false;
            foreach (var e in _combat.Entities)
            {
                if (e.Team != Team.Enemy || !e.Alive) continue;
                var p = new Vector3((float)e.Pos.X, 0f, (float)e.Pos.Y);
                float d = (p - partyCentroid).sqrMagnitude;
                if (d < best) { best = d; nearest = p; found = true; }
            }
            if (!found) return partyCentroid;

            const float packRadiusSq = 8f * 8f;
            sum = Vector3.zero; n = 0;
            foreach (var e in _combat.Entities)
            {
                if (e.Team != Team.Enemy || !e.Alive) continue;
                var p = new Vector3((float)e.Pos.X, 0f, (float)e.Pos.Y);
                if ((p - nearest).sqrMagnitude <= packRadiusSq) { sum += p; n++; }
            }
            var pack = n > 0 ? sum / n : nearest;
            pack.y = 0f;
            return pack;
        }

        /// <summary>Record progress toward the goal board and surface any completions. Goals can
        /// grant party XP, so a completion refreshes live party stats. Safe to call with 0/none.</summary>
        private void AdvanceQuest(QuestKind kind, long amount)
        {
            if (amount <= 0) return;
            var (next, completed) = Quests.Advance(_save, kind, amount, _cfg);
            _save = next;
            if (completed.Count == 0) return;

            foreach (var q in completed)
                _chat?.AddFeed($"Goal complete: {QuestPanel.QuestLabel(q)}  (+{Num.CompactFloor(q.RewardGold)} gold)",
                               new Color(1f, 0.85f, 0.35f));
            if (_combat != null) Combat.RefreshPartyStats(_combat, _save, _cfg); // quest XP may have leveled a hero
        }

        /// <summary>Record lifetime progress toward the achievement ladder (Lever 4) and announce any
        /// tiers that just completed. Milestone rewards can grant party XP, so a completion refreshes
        /// live party stats. Safe to call with 0/none (the reducer no-ops). Fed from the same game
        /// events as the goal board, plus the state-max milestones (stage/floor/level).</summary>
        private void Award(AchievementMetric metric, long amount)
        {
            var (next, done) = Achievements.Record(_save, metric, amount, _cfg);
            _save = next;
            if (done.Count == 0) return;

            foreach (var u in done)
            {
                var t = u.Tier;
                var bits = new List<string>();
                if (t.RewardGold > 0) bits.Add($"{Num.CompactFloor(t.RewardGold)} gold");
                if (t.RewardScrap > 0) bits.Add($"{Num.CompactFloor(t.RewardScrap)} scrap");
                if (t.RewardXp > 0) bits.Add($"{Num.CompactFloor(t.RewardXp)} XP");
                string reward = bits.Count > 0 ? $"  (+{string.Join(", ", bits)})" : "";
                _chat?.AddFeed($"★ Achievement: {u.Name} {u.TierIndex + 1}!{reward}", new Color(1f, 0.82f, 0.32f));
            }
            if (_combat != null) Combat.RefreshPartyStats(_combat, _save, _cfg); // milestone XP may have leveled a hero
        }

        /// <summary>Highest level across all owned heroes (fielded or benched) — the source for the
        /// HeroLevel achievement (a MAX metric, so feeding it redundantly is a harmless no-op).</summary>
        private int MaxHeroLevel()
        {
            int m = 0;
            foreach (var h in _save.Heroes) if (h.Level > m) m = h.Level;
            return m;
        }

        /// <summary>Claim today's daily-login reward (Lever 4 — premium currency): advance the streak
        /// and credit gems via the GameCore reducer, announced in the feed. No-op if already claimed
        /// today. Called by <see cref="DailyLoginModal"/> on Collect; <c>now</c> is epoch ms.</summary>
        public void ClaimDailyLogin(long now)
        {
            var (next, gems, streak, claimed) = DailyLogin.Claim(_save, _cfg, now);
            if (!claimed) return;
            _save = next;
            // Premium currency must never be lost to a quit before the 30s autosave — flush now.
            SaveStore.Save(Save.Touch(_save, now));
            _chat?.AddFeed($"Daily reward — day {streak} streak!  +{Num.CompactFloor(gems)} gems",
                           new Color(0.6f, 0.85f, 1f));
        }

        /// <summary>Bank pending loot/XP/gold into the save. Returns true if XP was
        /// granted (so the caller can refresh live party stats).</summary>
        private bool CommitPending()
        {
            bool xp = false;
            if (_combat.PendingLoot.Count > 0)
            {
                // Count Rare+ drops before the bag swallows them (goal: find Rare+ items).
                int rarePlus = 0;
                foreach (var it in _combat.PendingLoot) if (it.Rarity >= Rarity.Rare) rarePlus++;

                // Live farm trash is capped; boss/special-stage clears may overfill the bag.
                bool allowOverflow = _combat.Kind != EncounterKind.Farm;
                var loot = Inventory.AddLoot(_save, _combat.PendingLoot, _cfg, Settings.AutoSalvageMax, allowOverflow);
                _save = loot.Save;
                _combat.PendingLoot.Clear();

                AdvanceQuest(QuestKind.FindRarePlus, rarePlus);
                AdvanceQuest(QuestKind.SalvageItems, loot.Salvaged.Count); // auto-salvaged this batch
                Award(AchievementMetric.RarePlusFound, rarePlus);          // lifetime ladder (Lever 4)
                Award(AchievementMetric.ItemsSalvaged, loot.Salvaged.Count);

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

                // Auto-equip-if-better (Lever 2, opt-in): equip stored drops that are a genuine
                // upgrade for a fielded hero. Runs on the running _save so each equip informs the
                // next eval; one live stat refresh at the end (mirrors ReplaceSave).
                if (Settings.AutoEquipUpgrades && loot.Stored.Count > 0)
                {
                    var fielded = new List<string>();
                    foreach (var id in _save.Party) if (id != null) fielded.Add(id);
                    bool equippedAny = false;
                    if (fielded.Count > 0)
                        foreach (var stored in loot.Stored)
                        {
                            var (next, equipped) = Upgrades.AutoEquipIfBetter(_save, stored, _cfg, _save.Progress.CurrentStage, fielded);
                            if (equipped == null) continue;
                            _save = next;
                            equippedAny = true;
                            _chat?.AddFeed($"Auto-equipped {StatDisplay.ItemName(stored, _cfg)} → {HeroDisplayName(equipped.HeroId)} ({UpgradeTell.Pct(equipped.DeltaPercent)})",
                                           UpgradeTell.Up);
                        }
                    if (equippedAny && _combat != null) Combat.RefreshPartyStats(_combat, _save, _cfg);
                }
            }
            if (_combat.PendingXp > 0)
            {
                int before = PartyLevelSum();
                _save = Progression.GrantPartyXp(_save, _combat.PendingXp, _cfg);
                _combat.PendingXp = 0;
                xp = true;
                if (PartyLevelSum() > before)
                {
                    _chat?.AddFeed("Level up!", new Color(0.5f, 0.85f, 1f));
                    SoundFx.Play("CH_Levelup", 0.55f);
                    if (_juice != null)
                        foreach (var e in _combat.Entities)
                            if (e.Team == Team.Party && e.Alive && _views.TryGetValue(e.Id, out var lv) && lv.Go != null)
                                _juice.LevelUpBurst(lv.Go.transform.position + Vector3.up * (lv.Height + 0.9f));
                }
            }
            if (_combat.PendingGold > 0)
            {
                long earned = _combat.PendingGold;
                _save = Progression.GrantGold(_save, earned);
                _combat.PendingGold = 0;
                AdvanceQuest(QuestKind.EarnGold, earned);
                Award(AchievementMetric.GoldEarned, earned); // lifetime ladder (Lever 4)
            }
            if (xp) Award(AchievementMetric.HeroLevel, MaxHeroLevel()); // a level-up may complete a milestone
            return xp;
        }

        private void ResolveOutcome()
        {
            CommitPending(); // you keep whatever you earned, win or lose

            if (_combat.Kind == EncounterKind.BossChallenge && _combat.Status == CombatStatus.Won)
            {
                int cleared = _combat.Stage;

                // Snapshot modifiers before the clear so we can feed any stage-driven unlock/upgrade.
                var ownedBefore = new HashSet<string>(_save.Modifiers.Owned.Keys);
                int strBefore = MaxModifierStrength(_save);

                _save = Progression.OnStageCleared(_save, cleared, _cfg); // also syncs modifiers to depth
                _chat?.AddFeed($"Stage {cleared} cleared!", new Color(0.55f, 0.9f, 0.55f));
                Award(AchievementMetric.BossesKilled, 1);                          // stage boss down (Lever 4)
                Award(AchievementMetric.HighestStage, _save.Progress.HighestStage); // deepest-stage milestone

                // Modifiers now unlock/upgrade by farm depth (Lever 1) — surface the beat in the feed.
                var unlock = new Color(0.85f, 0.6f, 1f);
                foreach (var kv in _save.Modifiers.Owned)
                    if (!ownedBefore.Contains(kv.Key))
                    {
                        string name = _cfg.Modifiers.TryGetValue(kv.Key, out var md) ? md.Name : kv.Key;
                        _chat?.AddFeed($"Modifier unlocked: {name} (str {kv.Value})", unlock);
                    }
                int strAfter = MaxModifierStrength(_save);
                if (ownedBefore.Count > 0 && strAfter > strBefore)
                    _chat?.AddFeed($"Modifiers upgraded → strength {strAfter}", unlock);
            }
            // A failed boss run (timeout or wipe) ends an auto-push: drop back to manual farming.
            else if (_autoAdvance && _combat.Kind == EncounterKind.BossChallenge && _combat.Status == CombatStatus.Lost)
            {
                _autoAdvance = false;
                _chat?.AddFeed($"Auto-advance stopped — failed Stage {_combat.Stage}'s boss.",
                               new Color(1f, 0.6f, 0.4f));
            }
            // Tower of Ascension: bank a floor clear (advances the track + any milestone buff), or
            // report the fail. Either way the Update loop resumes farming on the same map next.
            else if (_combat.Kind == EncounterKind.Tower)
            {
                int floor = _combat.TowerFloor;
                if (_combat.Status == CombatStatus.Won)
                {
                    int before = Tower.MilestonesCleared(_save, _cfg);
                    _save = Tower.RecordClear(_save, floor, _cfg);
                    // A Tower clear can unlock a tower-gated modifier (e.g. Volatile at floor 10) on top
                    // of the milestone account buff — resync owned mods from the new floor and announce it.
                    var ownedBefore = new HashSet<string>(_save.Modifiers.Owned.Keys);
                    _save = Modifiers.SyncToStage(_save, _cfg);
                    Combat.RefreshPartyStats(_combat, _save, _cfg); // a new milestone buff applies at once
                    _chat?.AddFeed($"Tower floor {floor} cleared!", new Color(0.6f, 0.85f, 1f));
                    Award(AchievementMetric.HighestTowerFloor, floor); // highest-floor milestone (Lever 4)
                    if (Tower.MilestonesCleared(_save, _cfg) > before)
                        _chat?.AddFeed($"Ascension buff! +{Tower.AccountBuffPct(_save, _cfg) * 100:0}% account power (Hp/Atk/Def).",
                                       new Color(1f, 0.85f, 0.4f));
                    foreach (var kv in _save.Modifiers.Owned)
                        if (!ownedBefore.Contains(kv.Key) && _cfg.Modifiers.TryGetValue(kv.Key, out var nm))
                            _chat?.AddFeed($"New modifier unlocked: {nm.Name}! Slot it in the Modifiers panel.",
                                           new Color(0.85f, 0.6f, 1f));
                }
                else
                {
                    _chat?.AddFeed($"Tower floor {floor} failed — train up and try again.", new Color(1f, 0.6f, 0.4f));
                }
            }
        }

        // All owned modifiers share the same stage-derived strength; read the max (0 if none owned).
        private static int MaxModifierStrength(SaveState save)
        {
            int m = 0;
            foreach (var v in save.Modifiers.Owned.Values) if (v > m) m = v;
            return m;
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

        /// <summary>Zone reskin (roadmap 4): retint ground/props when the stage (or tower
        /// floor) belongs to a different zone, and announce the travel beat in the feed.
        /// ZoneDress no-ops when the zone hasn't changed, so this is safe to call on every
        /// farm start/resume.</summary>
        private void DressZone(int stageOrFloor)
        {
            var zone = ZoneDress.Sync(_cfg, stageOrFloor);
            if (zone == null) return;
            var accent = new Color((float)zone.AccentR, (float)zone.AccentG, (float)zone.AccentB);
            _chat?.AddFeed($"Now entering {zone.Name}.", Color.Lerp(accent, Color.white, 0.45f));
        }

        // ---- player controls (called from the IMGUI bar) ----

        private void GoToStage(int stage)
        {
            try { _save = Progression.SetStage(_save, stage, _cfg); }
            catch (System.ArgumentOutOfRangeException) { return; }
            CommitPending(); _runCount++; StartFarm();
        }

        // C1: the boss fight happens IN PLACE on the current farm map — trash despawns and the
        // boss appears — rather than swapping to a fresh arena.
        private void ChallengeBoss()
        {
            CommitPending();
            Combat.EnterBossChallenge(_combat, _cfg);
            _accMs = 0; _outcomeTimer = 0; _resolved = false;
            ReconcileViews();
        }

        private void FleeToFarm()
        {
            _autoAdvance = false; // a manual flee is an explicit stop to any auto-push
            CommitPending();
            ResumeFarmInPlace(_save.Progress.CurrentStage, _cfg.Balance.BossFleeCooldownMs);
        }

        /// <summary>Flip the auto-push toggle (top-centre HUD). On = chain boss challenges until a
        /// fail; off = back to manual. The Update loop does the actual challenging.</summary>
        private void ToggleAutoAdvance()
        {
            _autoAdvance = !_autoAdvance;
            if (_autoAdvance)
                _chat?.AddFeed("Auto-advance on — pushing stages until a boss run fails.",
                               new Color(0.6f, 0.85f, 1f));
            else
                _chat?.AddFeed("Auto-advance off.", new Color(0.72f, 0.76f, 0.82f));
        }

        /// <summary>Resume farming on the same map (no scene reset), gating the next trash pack by
        /// <paramref name="spawnDelayMs"/>. Used by flee, boss-fail, and the post-win advance.</summary>
        private void ResumeFarmInPlace(int stage, double spawnDelayMs)
        {
            Combat.ResumeFarm(_combat, stage, _cfg, spawnDelayMs);
            DressZone(stage); // post-win advance / tower exit can land in another zone
            // A boss clear can field a newly-unlocked hero (OnStageCleared), so sync the live
            // party to the save here too: ResumeFarm/RestoreParty only heal EXISTING entities,
            // so without this the new hero has no combat entity and reads as 0 HP on the HUD
            // until a manual bench/unbench. ReconcileParty is idempotent for flee/fail resumes.
            Combat.ReconcileParty(_combat, _save, _cfg);
            _combat.ActiveModifiers = Modifiers.ResolveActive(_save, _cfg); // re-apply toggles to the resumed farm
            _accMs = 0; _outcomeTimer = 0; _resolved = false;
            ReconcileViews();
            // Heroes who died in the boss fight are healed by ResumeFarm directly (no
            // Respawn EVENT fires), so resync their views out of the death pose too —
            // otherwise they walk the farm stuck in the death clip + downed paint.
            foreach (var e in _combat.Entities)
                if (e.Team == Team.Party && e.Alive &&
                    _views.TryGetValue(e.Id, out var v) && v.Go != null)
                {
                    v.Go.SetActive(true);
                    Paint(v.Go, v.BaseColor);
                    v.Anim?.SetDowned(false);
                }
        }

        // ---- views ----

        private const float ChibiHeight = 1.35f; // chibi head height -> floating health-bar anchor

        private void SpawnView(CombatEntity e)
        {
            bool isHero = e.Team == Team.Party;
            GameObject go;
            float height;
            Vector3 baseScale;
            Color color = Color.white;
            IHeroAnim? heroAnim = null;

            GameObject? model = null;
            float modelHeight = ChibiHeight;
            // SDF blob-shell critters (ROADMAP 4 slice 3): built here so they slot into the model
            // path (feet-at-ground, rank scale, spawn FX) exactly like a faceted FBX. The blob's own
            // animator replaces the MonsterAnimator below; the rig carries the rank/mod tell.
            SdfBlobRig? blobRig = null;
            SdfBlobAnimator? blobAnim = null;
            if (isHero)
            {
                var hero = _save.Heroes.Find(h => h.Id == e.RefId);
                bool ranged = hero != null && _cfg.Heroes.TryGetValue(hero.DefId, out var hd0) && hd0.Role == "ranged";
                // Skinned MS2-pipeline model first, then the rigid Blender model,
                // else the code-built chibi.
                if (hero != null)
                {
                    var skinned = SkinnedHero.Build(hero.DefId);
                    if (skinned != null) { model = skinned.Value.root; heroAnim = skinned.Value.anim; }
                    else
                    {
                        var built = ModelHero.Build(hero.DefId) ?? ChibiHero.Build(hero.DefId, ranged);
                        if (built != null) { model = built.Value.root; heroAnim = built.Value.anim; }
                    }
                }
            }
            else if (SdfBlobDefs.Has(e.RefId))
            {
                // SDF blend-shell blob (slime/shambler/spirit family): a GameObject with a rig +
                // animator, mirroring SdfGaitTest.ApplyMaterial's setup (fresh material off the SDF
                // shader, made matte; the MPB carries the per-renderer prim arrays + rank/mod tell).
                var def = SdfBlobDefs.TryGet(e.RefId)!;
                var blob = new GameObject(e.Id);
                blobRig = blob.AddComponent<SdfBlobRig>();
                blobRig.subdivisions = 9;
                blobRig.boundsPadding = def.BoundsPadding;
                blobRig.prims = def.ClonePrims(); // per-instance copy — shared defs would stomp pd.node
                blobRig.BuildMesh();
                var shader = Shader.Find("IdleGame/SdfBlendShell");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    Bootstrap.MakeMatte(mat);
                    blob.GetComponent<MeshRenderer>().sharedMaterial = mat;
                }
                blobRig.SetPrimitivesDirty();
                blobAnim = blob.AddComponent<SdfBlobAnimator>();
                blobAnim.Init(def.Family, e.Id.GetHashCode());
                model = blob;
                modelHeight = def.Height;
            }
            else
            {
                // Scripted-Blender LOW-POLY monster model (faceted Tunic style — the MS2
                // pipeline is heroes-only). Null → the painted primitive below.
                var mm = MonsterModel.Build(e.RefId);
                if (mm != null) { model = mm.Value.root; modelHeight = mm.Value.height; }
            }
            if (model != null)
            {
                go = model;
                go.name = e.Id;
                float mScale = 1f;
                if (!isHero && !e.IsBoss) // rank tell: chunkier body, like the primitives
                    mScale = e.Rank == MonsterRank.Rare ? 1.7f : e.Rank == MonsterRank.Elite ? 1.35f : 1f;
                baseScale = Vector3.one * mScale;
                go.transform.localScale = baseScale;
                height = modelHeight * mScale;
                go.transform.position = new Vector3((float)e.Pos.X, 0f, (float)e.Pos.Y); // feet at the ground

                if (!isHero)
                {
                    // Rank + modifier tells on authored-palette models: a gentle lean +
                    // faint glow (the primitives' flat repaint would erase the palette —
                    // verified 2026-07-02: 0.4 lean washed everything to the mod colour).
                    if (!e.IsBoss && e.Rank != MonsterRank.Normal)
                    {
                        var rc = e.Rank == MonsterRank.Rare ? new Color(0.96f, 0.76f, 0.22f)
                                                            : new Color(0.35f, 0.70f, 0.96f);
                        // Blobs carry the tell on the rig (albedo lean + gentle emission through the
                        // MPB); faceted models lean every material. Same lean (0.25) + gentle glow.
                        if (blobRig != null)
                        {
                            blobRig.SetTint(rc, 0.25f);
                            blobRig.SetEmission(rc * (e.Rank == MonsterRank.Rare ? 0.55f : 0.35f));
                        }
                        else
                            MonsterModel.Tint(go, rc, 0.25f, rc * (e.Rank == MonsterRank.Rare ? 0.55f : 0.35f));
                    }
                    if (e.ModTypes.Count > 0 && _cfg.Modifiers.TryGetValue(e.ModTypes[0], out var mmd))
                    {
                        // glow-only, and FAINT: albedo lean or strong emission yellows the
                        // whole field when an income mod is active (every farm mob carries
                        // it) and buries the authored palette (verified by screenshot)
                        var mt = new Color((float)mmd.TintR, (float)mmd.TintG, (float)mmd.TintB);
                        if (blobRig != null)
                            blobRig.SetEmission(mt * 0.12f); // glow-only (no albedo lean), same faintness
                        else
                            MonsterModel.Tint(go, mt, 0f, mt * 0.12f);
                    }
                }
            }
            else
            {
                var type = (!isHero && e.IsBoss) ? PrimitiveType.Cube : PrimitiveType.Capsule;
                go = GameObject.CreatePrimitive(type);
                go.name = e.Id;

                float scale = e.IsBoss ? 1.6f : 1f;
                // Elites/rares are chunkier — a size tell that matches their fattened sim body.
                if (!isHero && !e.IsBoss)
                    scale *= e.Rank == MonsterRank.Rare ? 1.7f : e.Rank == MonsterRank.Elite ? 1.35f : 1f;
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
                else
                {
                    // Trash green; bosses orange; ranks get the PoE blue (elite) / gold (rare) tells.
                    color = e.IsBoss                    ? new Color(0.85f, 0.40f, 0.25f)
                          : e.Rank == MonsterRank.Rare  ? new Color(0.96f, 0.76f, 0.22f)
                          : e.Rank == MonsterRank.Elite ? new Color(0.35f, 0.70f, 0.96f)
                          : new Color(0.45f, 0.80f, 0.50f);
                }
                Paint(go, color);
                // Make a rank mob glow so it stands out in a pack at a glance.
                if (!isHero && !e.IsBoss && e.Rank != MonsterRank.Normal)
                    Glow(go, color * (e.Rank == MonsterRank.Rare ? 2.0f : 1.5f));

                // Monster-modifier aura (Lever 1): tint + glow toward the (first) modifier's colour
                // — a clear "this mob is modified" tell (also marks the boss exhibiting its type).
                if (!isHero && e.ModTypes.Count > 0 && _cfg.Modifiers.TryGetValue(e.ModTypes[0], out var amd))
                {
                    var tint = new Color((float)amd.TintR, (float)amd.TintG, (float)amd.TintB);
                    Paint(go, Color.Lerp(color, tint, 0.6f));
                    Glow(go, tint * 1.7f);
                }
            }

            // Procedural life for faceted monster MODELS (not the primitive fallback, not heroes):
            // a body-pivot animator that gaits/telegraphs/flinches/dies per family. Seed its phase
            // off the entity id so a pack of the same monster doesn't bob in unison. Must run AFTER
            // the tint above so its material cache sees the rank/mod emission it has to restore.
            IMonsterAnim? monsterAnim = null;
            if (blobAnim != null)
            {
                // SDF blob: its own animator (built above) already ran Init; feed it through the
                // shared IMonsterAnim so the five SyncViews/Trigger/Die sites drive it unchanged.
                monsterAnim = blobAnim;
            }
            else if (!isHero && model != null)
            {
                var ma = go.AddComponent<MonsterAnimator>();
                ma.Init(e.RefId, e.Id.GetHashCode());
                monsterAnim = ma;
            }

            var view = new View { Go = go, Height = height, YOffset = model != null ? 0f : height,
                                  BaseColor = color, BaseScale = baseScale,
                                  PrevPos = go.transform.position, CurPos = go.transform.position, SmoothPos = go.transform.position,
                                  Anim = heroAnim, MonsterAnim = monsterAnim };

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
            // Where each enemy died this step, so a keeper's loot pop appears at the drop site
            // (the corpse's view is detached on Death, before the LootDrop event is handled).
            Dictionary<string, Vector3>? deathPos = null;
            // Sources that have already landed their PRIMARY basic-attack hit this step: the one
            // swing is triggered once, and its splash/chain siblings pop quieter (secondary). A
            // pack-clearing swing then reads as one hit + echoes, not N full-volume clangs/swings.
            HashSet<string>? swung = null;
            int enemyKills = 0; // batched into the goal board after the loop

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
                                // An AoE skill's per-victim ticks: first at full volume, the rest
                                // quieter so a big cast reads as one boom + echoes, not N clangs.
                                bool skPrimary = (swung ??= new HashSet<string>()).Add("sk:" + ev.SourceId);
                                PlayImpact(head, ev.Amount, ev.Crit, secondary: !skPrimary);
                            }
                            break;
                        }

                        // One swing per source per step: the primary hit triggers the animation
                        // + swing sound; splash/chain siblings ride that same swing (secondary),
                        // so they never re-trigger the clip or stack a full-volume swing sound.
                        bool primary = ev.SourceId == null || (swung ??= new HashSet<string>()).Add(ev.SourceId);
                        if (primary) TriggerLunge(ev.SourceId, ev.TargetId, towardTarget: true);

                        // Ranged attackers launch a projectile (impact pops the number);
                        // melee/projectiles-off pops it. For melee, delay the number + clang to the
                        // swing's contact moment (mid-clip) so it lands ON the visible hit, not the
                        // instant the swing starts. Secondary hits pop quieter.
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
                            ScheduleImpact(head, ev.Amount, ev.Crit, ContactDelayFor(ev.SourceId), secondary: !primary);
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
                        TriggerLunge(ev.SourceId, ev.TargetId, towardTarget: isDamage, skillId: ev.SkillId);
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
                                v.Anim?.SetDowned(true);  // skeletal heroes collapse (death clip)
                            }
                            else
                            {
                                // Enemy died: detach the view and play a knockback + crumple
                                // despawn so it doesn't vanish instantly (and lingers long
                                // enough for an in-flight projectile to land on it).
                                (deathPos ??= new Dictionary<string, Vector3>())[ev.EntityId] = v.Go.transform.position;
                                _views.Remove(ev.EntityId);
                                enemyKills++;
                                SoundFx.Play("BadWood_Dead", 0.4f);
                                // Faceted monster models die per-family (topple / poof) on their body
                                // animator, which now owns the whole detached object and self-destructs.
                                // The primitive fallback keeps the generic DeathFx crumple. Either way
                                // the corpse lingers ~0.5s so an in-flight projectile can still land.
                                if (v.MonsterAnim != null)
                                    v.MonsterAnim.Die(v.LastHitDir);
                                else
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
                            rv.Anim?.SetDowned(false); // back on their feet
                        }
                        break;
                    case CombatEventType.BossDefeated:
                        if (Settings.ScreenShake) _rig?.Shake(0.4f);
                        break;
                    case CombatEventType.LootDrop:
                        if (ev.Item != null && Settings.LootFeed)
                        {
                            // Tag the loot-rain line when the drop is a real upgrade (Lever 2), so a
                            // kill visibly matters in the stream you're watching. Skip items the
                            // auto-salvage threshold will scrap anyway (no point, and saves the eval).
                            string line = $"{StatDisplay.ItemName(ev.Item, _cfg)} (i{ev.Item.ItemLevel})";
                            bool keep = Settings.AutoSalvageMax == null || ev.Item.Rarity > Settings.AutoSalvageMax.Value;
                            var up = keep ? Upgrades.BestForItem(_save, ev.Item, _cfg, _save.Progress.CurrentStage) : null;
                            if (up != null && up.Verdict == Upgrades.Verdict.Upgrade)
                                line += $"  ▲ {UpgradeTell.Pct(up.DeltaPercent)} {HeroDisplayName(up.HeroId)}";
                            _chat?.AddFeed(line, Palette.Rarity(ev.Item.Rarity));
                            // Imprinted drops (mechanical-mod loot stamp) get their own louder beat —
                            // a build-defining affix you can't get any other way is the dopamine spike.
                            if (Loot.IsImprinted(ev.Item, _cfg))
                                foreach (var a in ev.Item.Affixes)
                                    if (Loot.IsImprintStat(a.Stat, _cfg))
                                    {
                                        _chat?.AddFeed($"✦ Imprinted! {ev.Item.BaseId} rolled {StatDisplay.ImprintBlurb(a.Stat)} — equip it to cleave harder.",
                                                       new Color(0.85f, 0.6f, 1f));
                                        break;
                                    }
                            // Keepers (Rare+) also pop in the world at the drop site — the
                            // standout beat in the loot rain; commons stay feed-only.
                            if (_juice != null && ev.Item.Rarity >= Rarity.Rare && ev.EntityId != null
                                && deathPos != null && deathPos.TryGetValue(ev.EntityId, out var dp))
                                _juice.LootPop(dp + Vector3.up * 0.8f, StatDisplay.ItemName(ev.Item, _cfg), ev.Item.Rarity);
                        }
                        break;
                }
            }
            if (enemyKills > 0)
            {
                AdvanceQuest(QuestKind.KillMonsters, enemyKills);
                Award(AchievementMetric.MonstersKilled, enemyKills); // lifetime ladder (Lever 4)
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
                    v.CurPos = new Vector3((float)e.Pos.X, v.YOffset, (float)e.Pos.Y);
                }
                v.SmoothPos = Vector3.Lerp(v.PrevPos, v.CurPos, _renderAlpha);
                v.Go.transform.position = v.SmoothPos + LungeOffset(v) + KnockOffset(v);
                if (v.Spawning) AnimateSpawn(v);

                // Movement drives both the skeletal hero clips and the monster body gaits, off the
                // same math and the SAME feed shape: Moving every frame, ground speed (units/sec)
                // only on sim steps while moving — so the speed stays sticky on the 60fps render
                // frames between 30Hz steps (feeding 0 there whipsaws hop/stride cadence).
                if (v.Anim != null || v.MonsterAnim != null)
                {
                    if (_steppedThisFrame)
                    {
                        v.Moving = (v.CurPos - v.PrevPos).sqrMagnitude > 0.0004f;
                        if (v.Moving)
                        {
                            float groundSpeed = (v.CurPos - v.PrevPos).magnitude /
                                                (float)(Combat.DefaultStepMs / 1000.0);
                            v.Anim?.SetMoveSpeed(groundSpeed);        // clip playback matches (no foot-glide)
                            v.MonsterAnim?.SetMoveSpeed(groundSpeed); // gait cadence tracks real pace
                        }
                    }
                    v.Anim?.SetMoving(v.Moving);
                    v.MonsterAnim?.SetMoving(v.Moving);
                }

                // Facing: heroes AND monsters turn toward their movement (or their target when
                // standing). Root rotation is safe here — only root position/scale are owned
                // elsewhere. Skip the primitive fallback (no Anim, no MonsterAnim).
                if (v.Anim != null || v.MonsterAnim != null)
                {
                    Vector3 face = Vector3.zero;
                    if (v.Moving) face = v.CurPos - v.PrevPos;
                    else if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var tv) && tv.Go != null)
                        face = tv.SmoothPos - v.SmoothPos;
                    face.y = 0f;
                    if (face.sqrMagnitude > 0.0001f)
                    {
                        var rot = Quaternion.LookRotation(face.normalized, Vector3.up);
                        v.Go.transform.rotation = Quaternion.RotateTowards(v.Go.transform.rotation, rot, 540f * Time.deltaTime);
                    }
                }
            }

            SyncLeaderMarker();
        }

        // Ground-marker geometry: a flat disc parked just above the ground (the GroundRing FX use
        // y ≈ 0.02..0.06; match that so it reads as painted on, never floating).
        private const float LeaderMarkerY = 0.05f;

        /// <summary>Keep the persistent leader ground marker under the effective leader. Re-targets
        /// when Party.EffectiveLeader (or its combat entity) changes, follows the leader's view root
        /// each frame, and hides while that view is missing (dead/respawning) — re-showing when it
        /// returns. Read-only over sim state; purely a display tell.</summary>
        private void SyncLeaderMarker()
        {
            // Who leads (UI intent view) -> that hero's combat entity -> the entity id keying _views.
            string? leaderHeroId = Party.EffectiveLeader(_save, _cfg);
            var leaderEntity = leaderHeroId != null ? FindHeroEntity(leaderHeroId) : null;
            string? entityId = leaderEntity?.Id;

            // No leader on the field at all: drop the marker.
            if (entityId == null || !_views.TryGetValue(entityId, out var lv) || lv.Go == null || !lv.Go.activeSelf)
            {
                if (_leaderMarker != null) _leaderMarker.SetActive(false);
                return;
            }

            if (_leaderMarker == null) _leaderMarker = BuildLeaderMarker();
            if (_leaderMarkerEntityId != entityId) _leaderMarkerEntityId = entityId; // note the re-target
            _leaderMarker.SetActive(true);
            // Glue it to the leader's feet each frame (view roots sit at the ground; SmoothPos is the
            // interpolated on-screen position the health bars/camera already read).
            var p = lv.SmoothPos; p.y = LeaderMarkerY;
            _leaderMarker.transform.position = p;
        }

        /// <summary>Build the leader disc: a flattened cylinder (scale y ≈ 0.02) at ground level —
        /// warm gold, LOW intensity. The diorama bloom washes bright emission to white, so the tint
        /// stays gentle and the glow faint (mirrors the rank/mod tell rule). No collider (never
        /// raycasts, like the projectile FX) and no shadow casting (like the ground plane).</summary>
        private GameObject BuildLeaderMarker()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col); // must not raycast/collide
            go.name = "LeaderMarker";
            go.transform.SetParent(transform, false);
            // Unity's cylinder is 2u tall / 1u wide at scale 1; flatten to a thin ground disc.
            go.transform.localScale = new Vector3(1.4f, 0.02f, 1.4f);

            var warmGold = new Color(0.95f, 0.78f, 0.35f);
            Paint(go, warmGold);
            Glow(go, warmGold * 0.35f); // faint — bloom blows anything brighter out to white

            var r = go.GetComponent<Renderer>();
            if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // a flat tell, not a caster
            return go;
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
            hv.Anim?.TriggerHit(); // skeletal heroes flinch with the MS2 knock-back clip
            hv.MonsterAnim?.TriggerHit(); // faceted monsters flash + squash on the body pivot
        }

        /// <summary>Kick off a lunge on the source view toward the target (or upward for a
        /// self/heal cast). Duration scales inversely with AtkSpd so faster actors snap.</summary>
        private void TriggerLunge(string? sourceId, string? targetId, bool towardTarget, string? skillId = null)
        {
            if (sourceId == null || !_views.TryGetValue(sourceId, out var sv) || sv.Go == null) return;

            // Skeletal heroes play their swing/cast clip instead of the capsule lunge;
            // named skills route to their bound clip + sound (manifest bindings).
            if (sv.Anim != null)
            {
                if (skillId != null) sv.Anim.TriggerSkill(skillId);
                else
                {
                    sv.Anim.TriggerAttack();
                    SoundFx.Play(sv.Anim.AttackSound, 0.4f);
                }
                return;
            }

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

            // Faceted monsters take this same positional lunge (Anim == null), but ALSO play a
            // body-pivot telegraph synced to it: anticipation crouch, then a punch-out stretch.
            // Body language only — no second positional offset.
            sv.MonsterAnim?.TriggerAttack(sv.LungeDur);
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

                float w = e.IsBoss ? 56f : (e.Team == Team.Enemy && e.Rank != MonsterRank.Normal ? 46f : 34f), h = 5f;
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
            // Boss challenge: a top-centre context line naming the stage + its modifier (the
            // boss exhibits and grants it — Lever 1). Farm needs no centre line: the wallet
            // moved top-left (below) and the stage shows in DrawTopControls.
            if (_combat.Kind == EncounterKind.BossChallenge)
            {
                string bossMod = "";
                var mtype = _cfg.ModifierTypeForStage(_combat.Stage);
                if (mtype != null && _cfg.Modifiers.TryGetValue(mtype, out var bmd)) bossMod = $"  ·  {bmd.Name}";
                string ctx = (major ? $"★ MAJOR BOSS — Stage {_combat.Stage}" : $"Miniboss — Stage {_combat.Stage}") + bossMod;
                GUI.Label(new Rect(0, 8, sw, 28), ctx, style);
            }

            DrawWallet(s);

            if (_combat.Kind == EncounterKind.BossChallenge)
            {
                float remain = Mathf.Max(0f, (float)(_cfg.Balance.BossChallengeSeconds - _combat.TimeMs / 1000.0));
                remain = Mathf.Ceil(remain * 10f) / 10f; // countdown rounds UP (game-design §7)
                var timer = new GUIStyle(GUI.skin.label)
                { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                timer.normal.textColor = remain <= 10 ? new Color(1f, 0.4f, 0.35f) : Color.white;
                GUI.Label(new Rect(sw / 2f - 100, 40, 200, 40), $"{remain:0.0}s", timer);
            }
        }

        /// <summary>Top-left wallet — gold / scrap / gems stacked under the account chip +
        /// Settings button (TopBar.cs, uGUI). TopBar's canvas scales by WIDTH (reference 1280),
        /// this HUD by UiScale(): convert the button's bottom edge into logical space so the
        /// readout stays glued under it at any resolution. Balances round DOWN (game-design §7).</summary>
        private void DrawWallet(float s)
        {
            long gold = _save.Currencies.TryGetValue("gold", out var g) ? g : 0;
            long scrap = _save.Currencies.TryGetValue("scrap", out var sc) ? sc : 0;
            long gems = _save.Currencies.TryGetValue(_cfg.Balance.PremiumCurrency, out var gm) ? gm : 0;

            float topBarBottomPx = 114f * (Screen.width / 1280f); // Settings button y 80 + h 34
            float y = topBarBottomPx / s + 8f;

            if (_walletStyle == null)
                _walletStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };

            DrawWalletLine(16f, ref y, $"Gold   {Num.CompactFloor(gold)}", new Color(1f, 0.84f, 0.35f));
            DrawWalletLine(16f, ref y, $"Scrap  {Num.CompactFloor(scrap)}", new Color(0.75f, 0.78f, 0.85f));
            DrawWalletLine(16f, ref y, $"Gems   {Num.CompactFloor(gems)}", new Color(0.65f, 0.85f, 1f));
        }

        private GUIStyle? _walletStyle;
        private void DrawWalletLine(float x, ref float y, string text, Color color)
        {
            _walletStyle!.normal.textColor = color;
            GUI.Label(new Rect(x, y, 260, 22), text, _walletStyle);
            y += 24f;
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

            // Auto-push toggle — always available while running so it can be armed or cancelled
            // mid-fight. When on it chains boss challenges automatically until one fails.
            // (Shelved: hidden unless AutoAdvanceEnabled — see the field's note.)
            if (AutoAdvanceEnabled)
            {
                var autoStyle = new GUIStyle(BtnStyleSm);
                if (_autoAdvance) autoStyle.normal.textColor = new Color(0.55f, 0.9f, 0.6f);
                if (Button(cx - 130, 144, 260, 36, _autoAdvance ? "■ Stop Auto-Advance" : "▶ Auto-Advance", autoStyle))
                    ToggleAutoAdvance();
            }
        }

        /// <summary>Outcome overlay: a success popup on a boss win (auto-advances ~1s or OK),
        /// a plain banner on a loss/wipe.</summary>
        private void DrawOutcome(float s)
        {
            if (AnyPanelOpen || _combat.Status == CombatStatus.Running) return;
            float sw = Screen.width / s, sh = Screen.height / s;

            bool bossWin = _combat.Kind == EncounterKind.BossChallenge && _combat.Status == CombatStatus.Won;
            bool towerWin = _combat.Kind == EncounterKind.Tower && _combat.Status == CombatStatus.Won;
            if (bossWin || towerWin)
            {
                float w = 420f, h = 180f, x = sw / 2f - w / 2f, y = sh / 2f - h / 2f;
                DrawRect(x - 2, y - 2, w + 4, h + 4, new Color(0.40f, 0.70f, 0.45f, 0.95f));
                DrawRect(x, y, w, h, new Color(0.10f, 0.13f, 0.11f, 0.98f));

                var t = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                t.normal.textColor = new Color(0.6f, 0.95f, 0.6f);
                GUI.Label(new Rect(x, y + 24, w, 40),
                    towerWin ? $"Tower floor {_combat.TowerFloor} cleared!" : $"Stage {_combat.Stage} cleared!", t);
                var sub = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
                sub.normal.textColor = new Color(0.8f, 0.85f, 0.8f);
                GUI.Label(new Rect(x, y + 70, w, 24),
                    towerWin ? "Returning to the field…" : "Advancing to the next stage…", sub);

                if (Button(x + w / 2f - 80, y + h - 60, 160, 44, "OK")) _outcomeTimer = 9999f; // fast-forward
            }
            else
            {
                string banner = _combat.Kind == EncounterKind.BossChallenge ? "BOSS FAILED"
                              : _combat.Kind == EncounterKind.Tower ? $"FLOOR {_combat.TowerFloor} FAILED"
                              : "PARTY WIPED";
                var bs = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, sh / 2f - 60, sw, 44), banner, bs);
            }
        }

        /// <summary>
        /// Party status panel, bottom-right: an HP bar per party hero, read live from the
        /// combat entities (mana was removed — skills are cooldown-only). Each chip is
        /// clickable and opens that hero's equipment doll (the only way in, per design).
        /// Hidden while a panel is open.
        /// </summary>
        private void DrawPartyHud(float s)
        {
            if (AnyPanelOpen) return;

            const float w = 340f, rowH = 72f, gap = 10f, pad = 18f;
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

                var hero = _save.Heroes.Find(h => h.Id == heroId);
                string chipLabel = hero != null ? $"{HeroDisplayName(heroId)}  Lv {hero.Level}" : HeroDisplayName(heroId);
                GUI.Label(new Rect(bx, y + 10, bw, 26), chipLabel, PartyNameStyle);

                // Skill-ready cue: a pulsing gold dot at the top-right of the chip when a
                // skill is off-cooldown + affordable.
                if (e != null && e.Alive && !e.Downed && AnySkillReady(e))
                {
                    float pulse = 0.55f + 0.45f * Mathf.PingPong(Time.time * 2f, 1f);
                    float d = 11f, dx = x + w - d - 12f, dy = y + 12f;
                    DrawRect(dx - 1.5f, dy - 1.5f, d + 3f, d + 3f, new Color(0f, 0f, 0f, 0.5f * pulse));
                    DrawRect(dx, dy, d, d, new Color(1f, 0.85f, 0.35f, pulse));
                }

                // HP bar (red fill — reads as health at a glance) with a shadowed value label
                DrawBar(bx, y + 46, bw, 16, maxHp > 0 ? Mathf.Clamp01((float)(hp / maxHp)) : 0f,
                        new Color(0.16f, 0.04f, 0.04f, 0.95f), new Color(0.85f, 0.24f, 0.20f));
                DrawShadowedLabel(new Rect(bx, y + 45, bw, 18),
                        $"{Mathf.CeilToInt((float)hp)}/{Mathf.CeilToInt((float)maxHp)}", PartyBarTextStyle);

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

        /// <summary>Label with a 1px black drop shadow so bar text stays legible over any fill.</summary>
        private void DrawShadowedLabel(Rect r, string text, GUIStyle style)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            style.normal.textColor = prev;
            GUI.Label(r, text, style);
        }

        private CombatEntity? FindHeroEntity(string heroId) =>
            _combat.Entities.Find(e => e.RefKind == "hero" && e.RefId == heroId);

        /// <summary>True if any of the entity's non-passive skills is off-cooldown —
        /// drives the Party HUD ready cue. Skills are cooldown-only now (mana removed).
        /// Read-only over the live combat entity.</summary>
        private bool AnySkillReady(CombatEntity e)
        {
            foreach (var id in e.Skills)
            {
                if (!_cfg.Skills.TryGetValue(id, out var sk)) continue;
                if (sk.Passive) continue;
                if (e.SkillCdMs.TryGetValue(id, out var cd) && cd > 0) continue;
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
                    // Bold + pure white + the DrawShadowedLabel drop shadow = readable over the red fill.
                    _partyBarTextStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                    _partyBarTextStyle.normal.textColor = Color.white;
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
            x += 170 + gap * 4; // wider gap: everyday pair (bag/heroes) | system panels

            // Monster modifiers (Lever 1): the risk/reward knob. Shows a count when any are active.
            int activeMods = _save.Modifiers.Active.Count;
            string modLabel = activeMods > 0 ? $"Modifiers ({activeMods})" : "Modifiers";
            if (Button(x, y, 230, h, modLabel)) _modifierPanel?.Toggle();
            x += 230 + gap;

            // Tower of Ascension (alt mode): shows the highest floor cleared.
            if (Button(x, y, 190, h, $"Tower (F{Tower.HighestFloor(_save)})")) _towerView?.Toggle();
            x += 190 + gap;

            // Hero gacha (roadmap 3): only surfaces once a banner with a real pool exists. cfg.Banners is
            // empty until slice 3 seeds the Ice Mage banner, so this button HIDES itself until then.
            if (AnyLiveBanner)
            {
                if (Button(x, y, 190, h, "Summon")) _gachaPanel?.Toggle();
                x += 190 + gap;
            }

            // Achievements (Lever 4): the permanent milestone ladder.
            if (Button(x, y, 240, h, "Achievements")) _achievements?.Toggle();
            // (The party always moves as a group now; stage nav + Challenge live in the
            // top-centre HUD — see DrawTopControls.)
        }

        /// <summary>True when at least one configured banner has a non-empty pool — the ONLY condition
        /// under which the control-bar Summon button appears (cfg.Banners is empty until slice 3).</summary>
        private bool AnyLiveBanner
        {
            get
            {
                foreach (var kv in _cfg.Banners) if (kv.Value.Pool.Count > 0) return true;
                return false;
            }
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
