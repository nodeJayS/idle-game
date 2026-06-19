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
        // Diorama framing at FOV 34°. Distances are tuned so heroes read clearly: at full
        // zoom-out the camera sits 40u back (~24u of world visible vertically — heroes are
        // ~2x the size of the first diorama pass). Halve/scale these together with
        // Bootstrap's cam.fieldOfView if you change the compression.
        public float MinDistance = 13f;
        public float MaxDistance = 40f;       // max zoom-out (the default framing)
        public float ZoomSensitivity = 0.03f; // per scroll unit (~120/notch); scaled to the range
        // Continuous follow with a small fixed lag: tracks the smoothed party centroid every
        // frame (no deadzone), so during steady roaming the camera moves at constant velocity
        // like the characters — it only eases at the start/stop of motion, never mid-roam.
        // Lower = tighter/more direct; higher = floatier.
        public float FollowSmoothTime = 0.12f;

        private Camera _cam = null!;
        private Vector3 _dir;   // normalized view direction, from the iso rotation
        private float _distance;
        private Vector3 _focus;
        private Vector3 _targetFocus;
        private Vector3 _focusVel; // SmoothDamp state
        private bool _hasFocus;
        private float _shake;

        public void Init(Camera cam)
        {
            _cam = cam;
            _dir = cam.transform.forward.normalized;
            _distance = MaxDistance;                       // start zoomed out (the familiar framing)
            _focus = _targetFocus = cam.transform.position + _dir * _distance;
        }

        /// <summary>Set the world point to keep centred (the smoothed party centroid). Tracked
        /// continuously — no deadzone — so the camera never start/stops mid-roam; the ease
        /// toward it happens every frame in LateUpdate.</summary>
        public void SetFocus(Vector3 worldFocus)
        {
            if (!_hasFocus) { _focus = _targetFocus = worldFocus; _hasFocus = true; return; }
            _targetFocus = worldFocus;
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

            // Critically-damped ease toward the party every frame — smooth and chop-free
            // regardless of how the focus target was fed (no Lerp-vs-deadzone start/stop).
            if (_hasFocus)
                _focus = Vector3.SmoothDamp(_focus, _targetFocus, ref _focusVel, FollowSmoothTime);

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
