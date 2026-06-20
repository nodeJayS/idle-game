#nullable enable
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace IdleGame.Game
{
    /// <summary>
    /// Drives a Mixamo Humanoid hero model with a tiny Playables graph — no AnimatorController
    /// asset, because the scene is built in code. Blends Idle &lt;-&gt; Walk by movement, and
    /// plays a one-shot Attack (overriding locomotion) when the sim says the hero swings/casts.
    /// Movement INTERRUPTS an attack, so you never see a swing/cast pose sliding along the
    /// ground. Idle/Walk are looped manually (Playables don't loop on their own).
    /// Mixer inputs: 0 = idle, 1 = walk, 2 = attack.
    /// </summary>
    public sealed class HeroAnimator : MonoBehaviour
    {
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mix;
        private AnimationClipPlayable _idleP, _walkP, _attackP;
        private float _idleLen = 1f, _walkLen = 1f;
        private float _moveT;          // 0 idle .. 1 walk (smoothed)
        private float _attackW;        // attack layer weight 0..1
        private float _attackLeft;     // seconds remaining in the current swing
        private float _attackLen = 0.6f;
        private bool _hasAttack;
        private bool _ready;

        /// <summary>True while the swing/cast clip is playing (used to face the target).</summary>
        public bool Attacking => _attackLeft > 0f;

        public void Init(Animator animator, AnimationClip idle, AnimationClip walk, AnimationClip? attack)
        {
            animator.applyRootMotion = false; // the sim owns position; the clip only animates the body
            _graph = PlayableGraph.Create("HeroAnim_" + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "out", animator);
            _mix = AnimationMixerPlayable.Create(_graph, 3);
            output.SetSourcePlayable(_mix);

            _idleP = Connect(idle, 0); _idleLen = Mathf.Max(0.1f, idle.length);
            _walkP = Connect(walk, 1); _walkLen = Mathf.Max(0.1f, walk.length);
            // Stagger each hero's cycle so they don't breathe/step in unison (cosmetic only).
            _idleP.SetTime(Random.Range(0f, _idleLen));
            _walkP.SetTime(Random.Range(0f, _walkLen));
            if (attack != null)
            {
                _attackP = Connect(attack, 2);
                _attackLen = attack.length > 0.05f ? attack.length : 0.6f;
                _hasAttack = true;
            }

            SetWeights(1f, 0f, 0f);
            _graph.Play();
            _ready = true;
        }

        private AnimationClipPlayable Connect(AnimationClip clip, int port)
        {
            var p = AnimationClipPlayable.Create(_graph, clip);
            _graph.Connect(p, 0, _mix, port);
            return p;
        }

        /// <summary>Steer toward idle (false) or walk (true). Moving cancels an in-progress
        /// swing/cast so the hero never slides while attack-posed.</summary>
        public void SetMoving(bool moving)
        {
            if (moving && _attackLeft > 0f) _attackLeft = 0f;
            _moveT = Mathf.MoveTowards(_moveT, moving ? 1f : 0f, Time.deltaTime * 8f);
        }

        /// <summary>Play the attack clip once from the start (a swing / cast).</summary>
        public void TriggerAttack()
        {
            if (!_ready || !_hasAttack) return;
            _attackP.SetTime(0);
            _attackLeft = _attackLen;
            _attackW = 1f;
        }

        private void Update()
        {
            if (!_ready) return;

            WrapLoop(_idleP, _idleLen);
            WrapLoop(_walkP, _walkLen);

            if (_attackLeft > 0f)
            {
                _attackLeft -= Time.deltaTime;
                _attackW = Mathf.MoveTowards(_attackW, _attackLeft > 0.12f ? 1f : 0f, Time.deltaTime * 10f);
            }
            else
            {
                _attackW = Mathf.MoveTowards(_attackW, 0f, Time.deltaTime * 12f);
            }

            float loco = 1f - _attackW;
            SetWeights(loco * (1f - _moveT), loco * _moveT, _attackW);
        }

        private static void WrapLoop(AnimationClipPlayable p, float len)
        {
            if (!p.IsValid()) return;
            double t = p.GetTime();
            if (t >= len) p.SetTime(t % len);
        }

        private void SetWeights(float idle, float walk, float attack)
        {
            _mix.SetInputWeight(0, idle);
            _mix.SetInputWeight(1, walk);
            if (_hasAttack) _mix.SetInputWeight(2, attack);
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
