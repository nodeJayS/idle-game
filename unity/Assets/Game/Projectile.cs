#nullable enable
using System;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// A purely cosmetic flying object (fireball, arrow, meteor…). The sim already
    /// applied the hit; this just travels start→end and fires <see cref="_onImpact"/>
    /// on arrival (where the damage number/impact juice plays). Supports an optional
    /// arc so the same component does flat shots and lobbed/from-above ones.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        private Vector3 _from, _to;
        private float _t, _dur, _arc;
        private Action? _onImpact;

        public void Launch(Vector3 from, Vector3 to, float speed, Action onImpact, float arc = 0f)
        {
            _from = from;
            _to = to;
            // floor the flight time so even point-blank shots are visible
            _dur = Mathf.Max(0.18f, Vector3.Distance(from, to) / Mathf.Max(0.01f, speed));
            _onImpact = onImpact;
            _arc = arc;
            transform.position = from;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float a = Mathf.Clamp01(_dur > 0 ? _t / _dur : 1f);

            var p = Vector3.Lerp(_from, _to, a);
            if (_arc > 0f) p.y += Mathf.Sin(a * Mathf.PI) * _arc; // lob (meteors/arrow-rain later)
            transform.position = p;

            if (a >= 1f)
            {
                _onImpact?.Invoke();
                // 10.6d: release any trail before the body dies so the ribbon lingers past the
                // hit instead of vanishing on the same frame. Timed destroy, not autodestruct —
                // deterministic even if the detached trail never gets another movement edge.
                foreach (var tr in GetComponentsInChildren<TrailRenderer>())
                {
                    tr.transform.SetParent(null, true);
                    tr.emitting = false;
                    Destroy(tr.gameObject, tr.time + 0.1f);
                }
                Destroy(gameObject);
            }
        }
    }
}
