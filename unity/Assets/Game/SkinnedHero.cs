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
            return (root, anim);
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

        /// <summary>Ground speed the MS2 run cycle was authored for (units/s).
        /// The chibi sprint covers ~1.5 body heights per 0.6s cycle. Playback
        /// scales by actual/native so feet match the ground instead of gliding.</summary>
        private const float NativeRunSpeed = 2.5f;

        private Animator? _animator;
        private bool _moving;
        private float _speedScale = 1f;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = gameObject.AddComponent<Animator>();
            _animator.runtimeAnimatorController =
                Resources.Load<RuntimeAnimatorController>("Models/HeroAnimator");
            _animator.applyRootMotion = false;
        }

        public void SetMoving(bool moving)
        {
            if (_animator == null || _moving == moving) return;
            _moving = moving;
            _animator.SetBool(MovingId, moving);
        }

        public void SetMoveSpeed(float unitsPerSec) =>
            _speedScale = Mathf.Clamp(unitsPerSec / NativeRunSpeed, 0.4f, 2.2f);

        private void Update()
        {
            if (_animator == null) return;
            // scale only the run cycle — idle breathing and attacks play as authored
            bool running = _animator.GetCurrentAnimatorStateInfo(0).IsName("Run");
            _animator.speed = running ? _speedScale : 1f;
        }

        public void TriggerAttack()
        {
            if (_animator == null || _moving) return; // never swing mid-slide
            _animator.SetTrigger(AttackId);
        }
    }
}
