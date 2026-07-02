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
            string texName = m.name.Replace(" (Instance)", "").ToLowerInvariant();
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
            if (tints.TryGetValue(texName, out var tint))
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

        /// <summary>Ground speed the hero's MS2 run cycle was authored for
        /// (units/s). Playback scales by actual/native so feet match the ground
        /// instead of gliding. 2.5 fits the warrior's 0.6s cycle; a manifest
        /// "run_speed" (reserved "_run" binding) overrides it per hero.</summary>
        private float _nativeRunSpeed = 2.5f;

        private Animator? _animator;
        private bool _moving;
        private bool _downed;
        private float _speedScale = 1f;
        private float _idleT;
        private float _nextBore = 8f;

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
            // scale only the run cycle — everything else plays as authored
            bool running = _animator.GetCurrentAnimatorStateInfo(0).IsName("Run");
            _animator.speed = running ? _speedScale : 1f;

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
            if (_animator == null || _moving || _downed) return; // never swing mid-slide
            _animator.SetTrigger(Random.value < 0.5f ? AttackId : Attack2Id);
        }

        public void TriggerSkill(string skillId)
        {
            if (_animator == null || _downed) return;
            if (SkillBindings != null && SkillBindings.TryGetValue(skillId, out var b))
            {
                _animator.SetTrigger(b.slot == 2 ? Skill2Id : Skill1Id);
                SoundFx.Play(b.sound, 0.5f);
            }
            else TriggerAttack(); // unbound skill: fall back to a basic swing
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
