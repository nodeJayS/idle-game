#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Procedural animation for a code-built chibi hero (no rig/clips) — a drop-in for the old
    /// Mixamo <c>HeroAnimator</c>: CombatView only calls <see cref="SetMoving"/> and
    /// <see cref="TriggerAttack"/>, and rotates the whole root to face movement/target itself.
    /// We just swing the puppet's joints: a gentle idle bob, a walk cycle (legs + counter-arms),
    /// and a one-shot weapon-arm swing on attack. Movement cancels an in-progress swing so the
    /// hero never slides while attack-posed (same rule as the old animator).
    /// </summary>
    public sealed class ChibiAnimator : MonoBehaviour
    {
        // Joints assigned by ChibiHero at build time. ArmR holds the weapon (it does the swing).
        public Transform? Body, Head, ArmL, ArmR, LegL, LegR;

        private const float AttackLen = 0.5f;
        private float _moveT;        // 0 idle .. 1 walk (smoothed)
        private float _attackLeft;   // seconds left in the current swing
        private float _phase;        // per-hero desync so they don't move in unison
        private float _t;
        private Vector3 _bodyBase;

        /// <summary>True while the weapon-arm swing is playing (parity with HeroAnimator; unused by CombatView).</summary>
        public bool Attacking => _attackLeft > 0f;

        /// <summary>Capture rest pose + randomise the cycle phase. Call after the parts are wired.</summary>
        public void Setup()
        {
            _phase = Random.Range(0f, 100f);
            if (Body != null) _bodyBase = Body.localPosition;
        }

        public void SetMoving(bool moving)
        {
            if (moving && _attackLeft > 0f) _attackLeft = 0f; // never slide mid-swing
            _moveT = Mathf.MoveTowards(_moveT, moving ? 1f : 0f, Time.deltaTime * 8f);
        }

        public void TriggerAttack() => _attackLeft = AttackLen;

        private void Update()
        {
            _t += Time.deltaTime;
            float t = _t + _phase;
            float m = _moveT;

            // Vertical bob: a slow breathe at idle, a quicker double-bounce at walk.
            float idleBob = Mathf.Sin(t * 2.2f) * 0.02f;
            float walkBob = Mathf.Abs(Mathf.Sin(t * 9f)) * 0.06f;
            if (Body != null) Body.localPosition = _bodyBase + new Vector3(0f, Mathf.Lerp(idleBob, walkBob, m), 0f);
            if (Head != null) Head.localRotation = Quaternion.Euler(Mathf.Lerp(Mathf.Sin(t * 2.2f) * 2.5f, 0f, m), 0f, 0f);

            // Walk cycle: legs swing at the hips, arms counter-swing. Idle ~ still, tiny arm sway.
            float cycle = Mathf.Sin(t * 9f);
            float legA = Mathf.Lerp(0f, cycle * 32f, m);
            if (LegL != null) LegL.localRotation = Quaternion.Euler(legA, 0f, 0f);
            if (LegR != null) LegR.localRotation = Quaternion.Euler(-legA, 0f, 0f);

            float idleArm = Mathf.Sin(t * 2.2f) * 4f;
            if (ArmL != null) ArmL.localRotation = Quaternion.Euler(Mathf.Lerp(idleArm, -cycle * 26f, m), 0f, 0f);

            // Weapon arm: walk counter-swing, overridden by a forward swing while attacking.
            float armRA = Mathf.Lerp(-idleArm, cycle * 26f, m);
            if (_attackLeft > 0f)
            {
                _attackLeft -= Time.deltaTime;
                float p = 1f - Mathf.Clamp01(_attackLeft / AttackLen); // 0 -> 1
                float swing = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);  // 0 -> 1 -> 0
                armRA = Mathf.Lerp(armRA, -110f, swing);               // negative = forward (toward target)
            }
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(armRA, 0f, 0f);
        }
    }
}
