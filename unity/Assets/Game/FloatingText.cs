#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace IdleGame.Game
{
    /// <summary>
    /// A short-lived bit of text anchored to a world point that floats up and fades,
    /// then destroys itself. Lives on a constant-pixel overlay canvas so screen-space
    /// coords map directly to anchoredPosition. Used for damage numbers and toasts.
    /// </summary>
    public sealed class FloatingText : MonoBehaviour
    {
        private Text _text = null!;
        private RectTransform _rt = null!;
        private Camera _cam = null!;
        private Vector3 _worldStart;
        private float _riseWorld;
        private float _life;
        private float _elapsed;
        private Color _color;

        public void Configure(Text text, Camera cam, Vector3 worldStart, float riseWorld, float life, Color color)
        {
            _text = text;
            _rt = (RectTransform)text.transform;
            _cam = cam;
            _worldStart = worldStart;
            _riseWorld = riseWorld;
            _life = life;
            _color = color;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _life > 0 ? Mathf.Clamp01(_elapsed / _life) : 1f;

            var world = _worldStart + Vector3.up * (_riseWorld * t);
            var sp = _cam != null ? _cam.WorldToScreenPoint(world) : Vector3.zero;
            if (sp.z > 0) _rt.anchoredPosition = new Vector2(sp.x, sp.y);

            _color.a = 1f - t;            // fade out
            _text.color = _color;

            if (_elapsed >= _life) Destroy(gameObject);
        }
    }
}
