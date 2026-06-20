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
    /// Mixer inputs: 0 = idle, 1 = walk, 2 = attack.
    /// </summary>
    public sealed class HeroAnimator : MonoBehaviour
    {
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mix;
        private AnimationClipPlayable _attack;
        private float _moveT;          // 0 idle .. 1 walk (smoothed)
        private float _attackW;        // attack layer weight 0..1
        private float _attackLeft;     // seconds remaining in the current swing
        private float _attackLen = 0.6f;
        private bool _ready;

        public void Init(Animator animator, AnimationClip idle, AnimationClip walk, AnimationClip? attack)
        {
            animator.applyRootMotion = false; // the sim owns position; the clip only animates the body
            _graph = PlayableGraph.Create("HeroAnim_" + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "out", animator);
            _mix = AnimationMixerPlayable.Create(_graph, 3);
            output.SetSourcePlayable(_mix);

            Loop(idle, 0);
            Loop(walk, 1);
            if (attack != null)
            {
                _attack = AnimationClipPlayable.Create(_graph, attack);
                _graph.Connect(_attack, 0, _mix, 2);
                _attackLen = attack.length > 0.05f ? attack.length : 0.6f;
            }

            SetWeights(1f, 0f, 0f);
            _graph.Play();
            _ready = true;
        }

        private void Loop(AnimationClip clip, int port)
        {
            var p = AnimationClipPlayable.Create(_graph, clip);
            _graph.Connect(p, 0, _mix, port);
        }

        /// <summary>Smoothly steer toward idle (false) or walk (true).</summary>
        public void SetMoving(bool moving)
        {
            _moveT = Mathf.MoveTowards(_moveT, moving ? 1f : 0f, Time.deltaTime * 6f);
        }

        /// <summary>Play the attack clip once from the start (a swing / cast).</summary>
        public void TriggerAttack()
        {
            if (!_ready || !_attack.IsValid()) return;
            _attack.SetTime(0);
            _attackLeft = _attackLen;
            _attackW = 1f;
        }

        private void Update()
        {
            if (!_ready) return;

            if (_attackLeft > 0f)
            {
                _attackLeft -= Time.deltaTime;
                _attackW = Mathf.MoveTowards(_attackW, _attackLeft > 0.12f ? 1f : 0f, Time.deltaTime * 9f);
            }
            else
            {
                _attackW = Mathf.MoveTowards(_attackW, 0f, Time.deltaTime * 9f);
            }

            float loco = 1f - _attackW;
            SetWeights(loco * (1f - _moveT), loco * _moveT, _attackW);
        }

        private void SetWeights(float idle, float walk, float attack)
        {
            _mix.SetInputWeight(0, idle);
            _mix.SetInputWeight(1, walk);
            if (_mix.GetInputCount() > 2) _mix.SetInputWeight(2, attack);
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
