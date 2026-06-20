using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IdleGame.Game
{
    /// <summary>
    /// Gently drifts the sun's directional light-cookie so the dappled "sun through canopy"
    /// pools slide across the world — the Tunic dappled-lighting effect, reimplemented in code
    /// from the reference asset (which animated a CustomRenderTexture's UVs). Attached to the
    /// Sun by <see cref="Bootstrap"/>. Offset is in the same world units as lightCookieSize.
    /// </summary>
    public sealed class LightCookieScroll : MonoBehaviour
    {
        // World-units/sec base drift, with a slow sway so it isn't a dead-straight slide.
        public Vector2 Speed = new Vector2(0.8f, 0.5f);
        public float Wobble = 1.5f;

        private UniversalAdditionalLightData _data;

        private void Awake()
        {
            var light = GetComponent<Light>();
            if (light != null) _data = light.GetUniversalAdditionalLightData();
        }

        private void Update()
        {
            if (_data == null) return;
            float t = Time.time;
            var o = new Vector2(Speed.x * t, Speed.y * t);
            o.x += Mathf.Sin(t * 0.30f) * Wobble;
            o.y += Mathf.Cos(t * 0.23f) * Wobble;
            _data.lightCookieOffset = o;
        }
    }
}
