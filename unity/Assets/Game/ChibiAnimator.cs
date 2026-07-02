#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Procedural animation for the chibi hero puppets — code-built (ChibiHero) or
    /// Blender-imported (ModelHero); both expose the same named joints resting at identity.
    /// CombatView only calls <see cref="SetMoving"/> and <see cref="TriggerAttack"/>, and
    /// rotates the whole root to face movement/target itself.
    ///
    /// Fluidity comes from layering, not clips: a damped locomotion blend, footfall-timed
    /// bob with a touch of squash &amp; stretch, body lean/twist/roll, a lagged head so it
    /// reads loose on the neck, arm splay, and a three-phase attack — anticipation wind-up,
    /// whip-fast strike, eased follow-through — with the body twisting into the hit.
    /// </summary>
    public sealed class ChibiAnimator : MonoBehaviour, IHeroAnim
    {
        // Joints assigned by ChibiHero/ModelHero at build time. ArmR holds the weapon.
        public Transform? Body, Head, ArmL, ArmR, LegL, LegR;

        private const float AttackLen = 0.5f;
        private const float WalkHz = 9f;

        private float _moveT, _moveVel;  // 0 idle .. 1 walk (SmoothDamp state)
        private float _attackLeft;       // seconds left in the current swing
        private float _phase;            // per-hero desync so they don't move in unison
        private float _t;
        private Vector3 _bodyBasePos, _bodyBaseScale;

        /// <summary>Capture rest pose + randomise the cycle phase. Call after the parts are wired.</summary>
        public void Setup()
        {
            _phase = Random.Range(0f, 100f);
            if (Body != null) { _bodyBasePos = Body.localPosition; _bodyBaseScale = Body.localScale; }
        }

        public void SetMoving(bool moving)
        {
            if (moving && _attackLeft > 0f) _attackLeft = 0f; // never slide mid-swing
            _moveT = Mathf.SmoothDamp(_moveT, moving ? 1f : 0f, ref _moveVel, 0.10f);
        }

        public void TriggerAttack() => _attackLeft = AttackLen;

        public void SetMoveSpeed(float unitsPerSec) { } // procedural cycle stays fixed

        private static float Ease(float p) { p = Mathf.Clamp01(p); return p * p * (3f - 2f * p); }

        private void Update()
        {
            _t += Time.deltaTime;
            float t = _t + _phase;
            float m = _moveT;

            // One walk cycle = one full stride; the bob dips at each footfall (|sin|).
            float cyc = Mathf.Sin(t * WalkHz);
            float cycC = Mathf.Cos(t * WalkHz);
            float breath = Mathf.Sin(t * 2.2f);

            // Attack envelopes: wind up (0..0.28), strike (0.28..0.55), settle (0.55..1).
            float wind = 0f, strike = 0f;
            if (_attackLeft > 0f)
            {
                _attackLeft -= Time.deltaTime;
                float p = 1f - Mathf.Clamp01(_attackLeft / AttackLen);
                if (p < 0.28f) wind = Ease(p / 0.28f);
                else if (p < 0.55f) { float s = Ease((p - 0.28f) / 0.27f); wind = 1f - s; strike = s; }
                else strike = 1f - Ease((p - 0.55f) / 0.45f);
            }

            // --- body: bob, lean into the run, hips counter the legs, squash on footfall ---
            if (Body != null)
            {
                float bobWalk = Mathf.Abs(cyc);
                float bob = Mathf.Lerp(breath * 0.02f, bobWalk * bobWalk * 0.07f, m); // squared = snappy landing
                Body.localPosition = _bodyBasePos + new Vector3(0f, bob, 0f);

                float lean = m * 6f + strike * 7f - wind * 4f;
                float twist = cyc * 5f * m - strike * 14f + wind * 10f; // shoulders wind then whip
                float roll = cycC * 2.5f * m;
                Body.localRotation = Quaternion.Euler(lean, twist, roll);

                float squash = Mathf.Lerp(breath * 0.012f, (bobWalk - 0.5f) * 0.05f, m);
                Body.localScale = new Vector3(_bodyBaseScale.x * (1f - squash * 0.5f),
                                              _bodyBaseScale.y * (1f + squash),
                                              _bodyBaseScale.z * (1f - squash * 0.5f));
            }

            // --- head: lagged counter-motion (loose on the neck) + idle glances ---
            if (Head != null)
            {
                float lag = Mathf.Sin(t * WalkHz - 0.9f);
                float nod = Mathf.Lerp(breath * 2.5f, lag * 4f, m) - strike * 8f + wind * 5f;
                float look = Mathf.Sin(t * 0.6f) * (1f - m) * 5f;
                Head.localRotation = Quaternion.Euler(nod, look, -cycC * 2f * m);
            }

            // --- legs: hip swing with a slight splay so strides read wide ---
            float legA = cyc * 34f * m;
            if (LegL != null) LegL.localRotation = Quaternion.Euler(legA, 0f, -1.5f * m);
            if (LegR != null) LegR.localRotation = Quaternion.Euler(-legA, 0f, 1.5f * m);

            // --- arms: counter-swing + splay; the weapon arm layers the attack on top ---
            float idleArm = breath * 4f;
            if (ArmL != null)
                ArmL.localRotation = Quaternion.Euler(Mathf.Lerp(idleArm, -cyc * 26f, m), 0f, -4f * m - 2f);

            float armR = Mathf.Lerp(-idleArm, cyc * 26f, m);
            armR += wind * 55f;                      // raise the blade behind
            armR = Mathf.Lerp(armR, -125f, strike);  // whip forward (negative = toward target)
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(armR, 0f, 4f * m + 2f);
        }
    }
}
