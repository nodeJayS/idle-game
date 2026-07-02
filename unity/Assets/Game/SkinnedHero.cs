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
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials) SetupMaterial(m);
            var anim = root.AddComponent<SkinnedHeroAnim>();
            return (root, anim);
        }

        /// <summary>Blender's FBX export strips texture paths, so wire the DDS
        /// textures (shipped next to the FBX, named after the material) back up
        /// at runtime; the alpha-textured face shell needs URP alpha-clip.</summary>
        private static void SetupMaterial(Material m)
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
            // MS2 tints its grayscale skin texture per character (OverrideColor0
            // in the NIF; f_body default below). Hero manifests own this later.
            if (texName.Contains("skin"))
                m.color = new Color(0.78f, 0.549f, 0.408f);
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

        private Animator? _animator;
        private bool _moving;

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

        public void TriggerAttack()
        {
            if (_animator == null || _moving) return; // never swing mid-slide
            _animator.SetTrigger(AttackId);
        }
    }
}
