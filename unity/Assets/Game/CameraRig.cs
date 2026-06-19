#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;

namespace IdleGame.Game
{
    /// <summary>
    /// Sole authority over the camera position: follows the party (a focus point fed each
    /// frame) at the fixed iso angle, with mouse-wheel zoom — the previous static framing
    /// is the max zoom-out, and you can zoom in toward the group — plus additive
    /// screen-shake. The rotation is left as Bootstrap set it (the iso tilt).
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public float MinDistance = 14f;
        public float MaxDistance = 42f;       // ≈ the old fixed framing distance (max zoom-out)
        public float ZoomSensitivity = 0.03f; // per scroll unit (~120/notch)
        public float FollowSmoothing = 6f;
        public float FollowDeadzone = 6f;     // hold still until the party drifts this far (no constant pan)

        private Camera _cam = null!;
        private Vector3 _dir;   // normalized view direction, from the iso rotation
        private float _distance;
        private Vector3 _focus;
        private bool _hasFocus;
        private float _shake;

        public void Init(Camera cam)
        {
            _cam = cam;
            _dir = cam.transform.forward.normalized;
            _distance = MaxDistance;                       // start zoomed out (the familiar framing)
            _focus = cam.transform.position + _dir * _distance;
        }

        /// <summary>Set the world point to keep centred (the party centroid). Smoothed.</summary>
        public void SetFocus(Vector3 worldFocus)
        {
            if (!_hasFocus) { _focus = worldFocus; _hasFocus = true; return; }
            // Deadzone: ignore small drifts so the camera holds steady while the group fights
            // in place, and only eases over when they actually relocate. Kills the constant pan
            // that makes the whole field look like it's sliding/moving in unison.
            if (Vector3.Distance(worldFocus, _focus) <= FollowDeadzone) return;
            _focus = Vector3.Lerp(_focus, worldFocus, 1f - Mathf.Exp(-FollowSmoothing * Time.deltaTime));
        }

        public void Shake(float magnitude) => _shake = Mathf.Max(_shake, magnitude);

        private void LateUpdate()
        {
            if (_cam == null) return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    _distance = Mathf.Clamp(_distance - scroll * ZoomSensitivity, MinDistance, MaxDistance);
            }

            var pos = _focus - _dir * _distance;
            if (_shake > 0f)
            {
                _shake = Mathf.Max(0f, _shake - Time.deltaTime * 2.5f);
                pos += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * _shake;
            }
            _cam.transform.position = pos;
        }
    }
}
