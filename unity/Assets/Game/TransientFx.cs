#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// A short-lived cosmetic primitive that scales (and optionally rises and/or
    /// follows a transform) over its lifetime, fades its emission to black, then
    /// destroys itself. One component covers skill flourishes: expanding ground
    /// rings (cleave/quake), rising heal sparkles (mend), and caster auras (war cry).
    /// Purely visual — the sim already applied the effect; this draws no numbers.
    /// </summary>
    public sealed class TransientFx : MonoBehaviour
    {
        private float _t, _life = 0.4f;
        private Vector3 _scaleFrom, _scaleTo;
        private float _rise;
        private Transform? _follow;
        private Vector3 _followOffset;
        private Renderer? _renderer;
        private Color _emitFrom;
        private bool _glow;

        public TransientFx Configure(float life, Vector3 scaleFrom, Vector3 scaleTo,
                                     float rise = 0f, Transform? follow = null)
        {
            _life = Mathf.Max(0.01f, life);
            _scaleFrom = scaleFrom;
            _scaleTo = scaleTo;
            _rise = rise;
            _follow = follow;
            if (follow != null) _followOffset = transform.position - follow.position;
            transform.localScale = scaleFrom;

            _renderer = GetComponent<Renderer>();
            var mat = _renderer != null ? _renderer.sharedMaterial : null;
            if (mat != null && mat.IsKeywordEnabled("_EMISSION"))
            {
                _glow = true;
                _emitFrom = mat.GetColor("_EmissionColor");
            }
            return this;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float a = Mathf.Clamp01(_t / _life);

            transform.localScale = Vector3.Lerp(_scaleFrom, _scaleTo, EaseOut(a));

            // A destroyed follow target (e.g. caster despawned) just stops the tracking.
            var p = transform.position;
            if (_follow != null) p = _follow.position + _followOffset;
            p.y += _rise * Time.deltaTime;
            transform.position = p;

            if (_glow && _renderer != null)
                _renderer.sharedMaterial.SetColor("_EmissionColor", Color.Lerp(_emitFrom, Color.black, a));

            if (a >= 1f) Destroy(gameObject);
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
    }
}
