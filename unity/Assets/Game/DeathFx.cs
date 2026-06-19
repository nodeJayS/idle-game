#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Death crumple for a slain monster: over a short life it knocks back in the hit
    /// direction, shrinks toward nothing, and sinks slightly — the rough inverse of the
    /// spawn-in pop, so kills read as a death rather than an instant vanish. The view is
    /// detached from CombatView's live set first, so the gradual shrink also keeps the
    /// corpse on-screen long enough for a still-flying projectile to land on it.
    /// Self-destructs when done.
    /// </summary>
    public sealed class DeathFx : MonoBehaviour
    {
        private float _t, _life = 0.45f;
        private Vector3 _start, _baseScale, _knock;
        private float _sink;

        public DeathFx Configure(float life, Vector3 baseScale, Vector3 knock, float sink)
        {
            _life = Mathf.Max(0.05f, life);
            _start = transform.position;
            _baseScale = baseScale;
            _knock = knock;
            _sink = sink;
            return this;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float a = Mathf.Clamp01(_t / _life);
            float ease = 1f - (1f - a) * (1f - a); // ease-out: most of the knockback up front

            transform.localScale = Vector3.Lerp(_baseScale, _baseScale * 0.03f, ease);
            transform.position = _start + _knock * ease + Vector3.down * (_sink * a);

            if (a >= 1f) Destroy(gameObject);
        }
    }
}
