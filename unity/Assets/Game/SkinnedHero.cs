#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Seam CombatView drives hero animation through — implemented by the
    /// procedural <see cref="ChibiAnimator"/> and the clip-based
    /// <see cref="SkinnedHeroAnim"/>.
    /// </summary>
    public interface IHeroAnim
    {
        void SetMoving(bool moving);
        void SetMoveSpeed(float unitsPerSec);
        void TriggerAttack();
        void TriggerSkill(string skillId);
        void TriggerHit();
        void SetDowned(bool downed);

        /// <summary>Sound set for the basic attack. Default = the sword swing;
        /// skinned heroes override it from their manifest (a ranged caster
        /// shouldn't clang like a sword).</summary>
        string AttackSound => "Swing_Sword";

        /// <summary>Seconds from the last <see cref="TriggerAttack"/> until the swing's
        /// contact moment (the sword lands / the shot is loosed). CombatView delays the
        /// damage number + impact sound by this so they land ON the visible hit rather
        /// than the instant the swing starts. Default fits the procedural chibi swing.</summary>
        float AttackContactSec => 0.2f;

        /// <summary>Seconds from the last <see cref="TriggerAttack"/> until the swing/cast
        /// anim COMPLETES; 0 when the trigger was refused (fire-in-transit: TriggerAttack
        /// refuses while moving). CombatView delays a ranged projectile's LAUNCH by this so
        /// the shot emerges as the cast finishes rather than mid-anim.</summary>
        float AttackFinishSec => 0.45f;

        /// <summary>Same as <see cref="AttackFinishSec"/> for the skill cast last passed to
        /// <see cref="TriggerSkill"/>; 0 when refused.</summary>
        float SkillFinishSec => 0.45f;
    }

    /// <summary>
    /// Loader for skinned hero FBX models (Resources/Models/&lt;defId&gt;_skinned.fbx,
    /// exported by art/skinned_body.py with idle/run/attack takes decoded from the
    /// MS2 .kf files). Returns null when no skinned model ships for the def —
    /// SpawnView falls back to ModelHero, then the code-built chibi.
    /// </summary>
    public static class SkinnedHero
    {
        public static (GameObject root, IHeroAnim anim)? Build(string defId)
        {
            var prefab = Resources.Load<GameObject>("Models/" + defId + "_skinned");
            if (prefab == null) return null;

            var root = Object.Instantiate(prefab);
            root.name = defId;
            var tints = LoadTints(defId);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials) SetupMaterial(m, tints);
            var anim = root.AddComponent<SkinnedHeroAnim>();
            anim.Init(defId, LoadSkills(defId));
            return (root, anim);
        }

        /// <summary>Skill presentation bindings from the hero manifest, exported by
        /// art/skinned_body.py as "skillId slot soundSet" lines next to the FBX.
        /// Our GameCore skill ids -> which Skill state plays + which MS2 sound.</summary>
        private static System.Collections.Generic.Dictionary<string, (int slot, string sound)> LoadSkills(string defId)
        {
            var map = new System.Collections.Generic.Dictionary<string, (int, string)>();
            var txt = Resources.Load<TextAsset>("Models/" + defId + "_skinned_skills");
            if (txt == null) return map;
            foreach (var line in txt.text.Split('\n'))
            {
                var f = line.Trim().Split(' ');
                if (f.Length != 3) continue;
                map[f[0]] = (int.Parse(f[1]), f[2]);
            }
            return map;
        }

        /// <summary>MS2 customization tints (OverrideColor0 per material — skin
        /// tone, hair colour), exported by art/skinned_body.py as
        /// "name r g b" lines next to the FBX.</summary>
        private static System.Collections.Generic.Dictionary<string, Color> LoadTints(string defId)
        {
            var tints = new System.Collections.Generic.Dictionary<string, Color>();
            var txt = Resources.Load<TextAsset>("Models/" + defId + "_skinned_tints");
            if (txt == null) return tints;
            foreach (var line in txt.text.Split('\n'))
            {
                var f = line.Trim().Split(' ');
                if (f.Length != 4) continue;
                tints[f[0]] = new Color(float.Parse(f[1]), float.Parse(f[2]), float.Parse(f[3]));
            }
            return tints;
        }

        /// <summary>Blender's FBX export strips texture paths, so wire the DDS
        /// textures (shipped next to the FBX, named after the material) back up
        /// at runtime; the alpha-textured face shell needs URP alpha-clip.</summary>
        private static void SetupMaterial(Material m,
            System.Collections.Generic.Dictionary<string, Color> tints)
        {
            string fullName = m.name.Replace(" (Instance)", "").ToLowerInvariant();
            // Two parts sharing a texture but differing in tint duplicate the Blender
            // material as "<name>.001" — the texture resolves by the STRIPPED name,
            // while the tints sidecar keys the full (possibly suffixed) name.
            string texName = System.Text.RegularExpressions.Regex.Replace(fullName, @"\.\d+$", "");
            if (m.mainTexture == null)
            {
                var tex = Resources.Load<Texture2D>("Models/" + texName);
                if (tex != null) m.mainTexture = tex;
            }
            if (texName.Contains("face"))
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
            }
            if (tints.TryGetValue(fullName, out var tint) || tints.TryGetValue(texName, out tint))
                m.color = tint;
            Bootstrap.MakeMatte(m);
        }
    }

    /// <summary>
    /// Drives the Animator baked into the skinned FBX: idle&lt;-&gt;run crossfade on
    /// Moving, attack trigger (movement cancels — the controller transitions
    /// Attack-&gt;Run on Moving without exit time). Controller asset:
    /// Resources/Models/HeroAnimator.controller (built once in-editor).
    /// </summary>
    public sealed class SkinnedHeroAnim : MonoBehaviour, IHeroAnim
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int Attack2Id = Animator.StringToHash("Attack2");
        private static readonly int Skill1Id = Animator.StringToHash("Skill1");
        private static readonly int Skill2Id = Animator.StringToHash("Skill2");
        private static readonly int BoreId = Animator.StringToHash("Bore");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int DeadId = Animator.StringToHash("Dead");

        /// <summary>GameCore skill id -> (Skill state slot, MS2 sound set). Loaded
        /// from the manifest-exported bindings by SkinnedHero.Build. Reserved ids:
        /// "_attack" = basic-attack sound override, "_run" = native run speed.</summary>
        public System.Collections.Generic.Dictionary<string, (int slot, string sound)>? SkillBindings;

        public string AttackSound =>
            SkillBindings != null && SkillBindings.TryGetValue("_attack", out var b)
                ? b.sound : "Swing_Sword";

        // Contact tuning: the swing should LAND about this long after TriggerAttack, and the
        // clip is time-scaled so the contact moment sits at ~this fraction of its length. Both
        // attack takes (they differ in length) get normalised to the same felt timing, so the
        // number/sound delay CombatView applies is a single stable value across swings/heroes.
        private const float TargetContactSec = 0.22f;
        private const float ContactFraction = 0.45f; // sword lands slightly before the clip's midpoint
        private float _attackClipLen = 0.5f;         // length of the take last triggered
        public float AttackContactSec => TargetContactSec;

        // Finish times CombatView delays a ranged projectile's launch by: the real playback
        // length of the swing/cast last triggered, 0 when the trigger was refused.
        private float _attackFinish, _skillFinish;
        public float AttackFinishSec => _attackFinish;
        public float SkillFinishSec => _skillFinish;

        /// <summary>Ground speed the hero's MS2 run cycle was authored for
        /// (units/s). Playback scales by actual/native so feet match the ground
        /// instead of gliding. 2.5 fits the warrior's 0.6s cycle; a manifest
        /// "run_speed" (reserved "_run" binding) overrides it per hero.</summary>
        private float _nativeRunSpeed = 2.5f;

        private Animator? _animator;
        private bool _moving;
        private bool _downed;
        private float _speedScale = 1f;
        private float _attackSpeed = 1f;   // playback scale for the CURRENT attack take (contact-normalise)
        private float _idleT;
        private float _nextBore = 8f;

        // Clip lengths (seconds) of the two basic-attack takes, read from the override
        // controller at Init so TriggerAttack can time-scale each to the same contact moment.
        private float _attackLen = 0.467f, _attack2Len = 0.467f;

        // Skill-cast take lengths (seconds); skill states play at authored speed (Update only
        // rescales Run/Attack), so these ARE their playback lengths — the launch-delay values.
        private float _skill1Len = 0.6f, _skill2Len = 0.6f;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = gameObject.AddComponent<Animator>();
            // safe default; Init swaps in the hero's own override controller
            _animator.runtimeAnimatorController =
                Resources.Load<RuntimeAnimatorController>("Models/HeroAnimator");
            _animator.applyRootMotion = false;
        }

        /// <summary>Called by SkinnedHero.Build right after AddComponent. The
        /// shared controller's states reference ONE hero's clips, so each hero
        /// ships an AnimatorOverrideController remapping the states onto its own
        /// FBX takes (built by Tools > Build Hero Animators).</summary>
        public void Init(string defId,
            System.Collections.Generic.Dictionary<string, (int slot, string sound)> bindings)
        {
            SkillBindings = bindings;
            if (bindings.TryGetValue("_run", out var r) &&
                float.TryParse(r.sound, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var speed))
                _nativeRunSpeed = speed;
            var ctrl = Resources.Load<RuntimeAnimatorController>(
                "Models/" + defId + "Animator");
            if (ctrl != null && _animator != null)
                _animator.runtimeAnimatorController = ctrl;

            // Cache the two attack takes' lengths (names end "…attack" / "…attack2") so each
            // swing can be time-scaled to a common contact moment.
            var src = ctrl ?? _animator?.runtimeAnimatorController;
            if (src != null)
                foreach (var c in src.animationClips)
                {
                    if (c == null || c.length <= 0f) continue;
                    var n = c.name.ToLowerInvariant();
                    if (n.EndsWith("attack2")) _attack2Len = c.length;
                    else if (n.EndsWith("attack")) _attackLen = c.length;
                    else if (n.EndsWith("skill1")) _skill1Len = c.length;
                    else if (n.EndsWith("skill2")) _skill2Len = c.length;
                }
        }

        public void SetMoving(bool moving)
        {
            if (_animator == null || _moving == moving) return;
            _moving = moving;
            _animator.SetBool(MovingId, moving);
        }

        public void SetMoveSpeed(float unitsPerSec) =>
            _speedScale = Mathf.Clamp(unitsPerSec / _nativeRunSpeed, 0.4f, 2.2f);

        private void Update()
        {
            if (_animator == null) return;
            // Run cycle scales to ground speed; the basic-attack takes scale so their contact
            // moment lands at the same felt time regardless of clip length (contact-normalise);
            // everything else plays as authored.
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName("Run")) _animator.speed = _speedScale;
            else if (st.IsName("Attack") || st.IsName("Attack2")) _animator.speed = _attackSpeed;
            else _animator.speed = 1f;

            // long idle -> a bored fidget now and then (MS2's bore clip)
            if (!_moving && !_downed)
            {
                _idleT += Time.deltaTime;
                if (_idleT >= _nextBore)
                {
                    _animator.SetTrigger(BoreId);
                    _idleT = 0f;
                    _nextBore = Random.Range(7f, 14f);
                }
            }
            else _idleT = 0f;
        }

        public void TriggerAttack()
        {
            if (_animator == null || _moving || _downed) { _attackFinish = 0f; return; } // never swing mid-slide
            bool two = Random.value < 0.5f;
            float len = two ? _attack2Len : _attackLen;
            _attackClipLen = len;
            // Time-scale the take so its contact frame (ContactFraction of the clip) lands at
            // TargetContactSec — both takes then feel identical and match the number/sound delay.
            float wantContact = len * ContactFraction;
            _attackSpeed = wantContact > 0.001f
                ? Mathf.Clamp(wantContact / TargetContactSec, 0.5f, 3f) : 1f;
            _animator.SetTrigger(two ? Attack2Id : AttackId);
            _attackFinish = _attackClipLen / _attackSpeed; // real playback length of THIS take
        }

        public void TriggerSkill(string skillId)
        {
            if (_animator == null || _downed) { _skillFinish = 0f; return; }
            if (SkillBindings != null && SkillBindings.TryGetValue(skillId, out var b))
            {
                _animator.SetTrigger(b.slot == 2 ? Skill2Id : Skill1Id);
                SoundFx.Play(b.sound, 0.5f);
                // Skill states play at authored speed (Update rescales only Run/Attack), so the
                // take length IS the launch delay.
                _skillFinish = b.slot == 2 ? _skill2Len : _skill1Len;
            }
            else { TriggerAttack(); _skillFinish = _attackFinish; } // unbound skill: fall back to a basic swing
        }

        public void TriggerHit()
        {
            if (_animator == null || _downed) return;
            _animator.SetTrigger(HitId);
        }

        public void SetDowned(bool downed)
        {
            if (_animator == null || _downed == downed) return;
            _downed = downed;
            _animator.SetBool(DeadId, downed);
        }
    }
}
