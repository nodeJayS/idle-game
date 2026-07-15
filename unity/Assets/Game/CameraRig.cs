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
        // Per PIXEL of two-finger pinch separation change. Derivation mirrors ZoomSensitivity's
        // per-notch scaling: a ~360px pinch (half the 720p reference height) should sweep roughly the
        // FULL zoom range MaxDistance-MinDistance = 40-13 = 27u, so 27/360 ≈ 0.075. Touch-only path;
        // the mouse-wheel block is untouched.
        public float PinchSensitivity = 0.075f;
        // Dead-zone follow (anti-dizziness): the camera does NOT track the party continuously —
        // at the magnified ortho framing every centroid wiggle became a full-screen slide with
        // no parallax anchor. Instead the rig holds a focus F and a target T (party centroid +
        // look-ahead); while T stays within DeadRadius of F the camera is rock-still, and when
        // T escapes, F glides just far enough to put T back on the zone edge — a calm, speed-
        // capped SmoothDamp glide (frame-rate independent), never a snap.
        public float DeadRadius = 2.5f;       // world units T may drift from F before the camera moves
        public float GlideSmoothTime = 0.5f;  // SmoothDamp time for the catch-up glide
        public float MaxGlideSpeed = 8f;      // u/s cap so pack-to-pack retargets stay calm

        // Look-ahead toward the action: the target leans from the party centroid toward the
        // midpoint between party and action point (their targets / the next pack), with the
        // offset clamped so heroes always stay well inside the magnified frame. The lean is
        // part of T, so it routes through the same dead-zone/glide — no direct snap path.
        public float LookAheadMax = 4.5f;     // world units, max target offset from the party

        // Orthographic framing: the camera is ortho now (Tunic diorama), so zoom drives
        // orthographicSize instead of pushing the camera in/out for a perspective change.
        // Distance still moves the camera (keeps shadow/clip framing sane), but the visible
        // extent is orthographicSize = distance * this factor. 0.18 => size 7.2 at the
        // MaxDistance-40 framing (original 0.225 → size 9): a 1.25x magnification at every
        // zoom level — user-tuned middle ground (0.15/1.5x read too zoomed-in). Zoom still
        // scans the same MinDistance..MaxDistance range; only the framing tightness changed.
        private const float SizePerDistance = 0.18f;

        private Camera _cam = null!;
        private Vector3 _dir;   // normalized view direction, from the iso rotation
        private float _distance;
        private Vector3 _focus;       // F — where the camera actually looks (held while parked)
        private Vector3 _targetFocus; // T — party centroid + clamped look-ahead
        private Vector3 _focusVel;    // SmoothDamp state for the glide
        private bool _hasFocus;
        private float _shake;

        public void Init(Camera cam)
        {
            _cam = cam;
            _cam.orthographic = true;                       // Tunic diorama — zoom drives orthographicSize
            _dir = cam.transform.forward.normalized;
            _distance = MaxDistance;                       // start zoomed out (the familiar framing)
            _cam.orthographicSize = _distance * SizePerDistance;
            _focus = _targetFocus = cam.transform.position + _dir * _distance;
        }

        /// <summary>Focus target without look-ahead: T = the given point. Same dead-zone rules.</summary>
        public void SetFocus(Vector3 worldFocus) => SetFocus(worldFocus, worldFocus);

        /// <summary>Feed the camera target: T = <paramref name="partyCentroid"/> plus an offset
        /// toward the midpoint between party and <paramref name="actionPoint"/> (their attack
        /// targets, or the next pack), clamped to <see cref="LookAheadMax"/> so heroes never
        /// leave the frame. T only sets the target — all camera MOTION goes through the
        /// dead-zone/glide in LateUpdate, so an action-point jump can never snap the view.</summary>
        public void SetFocus(Vector3 partyCentroid, Vector3 actionPoint)
        {
            var offset = (actionPoint - partyCentroid) * 0.5f; // midpoint(party, action) - party
            offset.y = 0f;
            var t = partyCentroid + Vector3.ClampMagnitude(offset, LookAheadMax);

            if (!_hasFocus) { _focus = _targetFocus = t; _hasFocus = true; return; }
            _targetFocus = t;
        }

        public void Shake(float magnitude) => _shake = Mathf.Max(_shake, magnitude);

        // 10.6e kill-streak pulse: a quick zoom punch (fraction of orthographicSize) with a sin
        // spring-back — a "felt beat" distinct from Shake's positional rattle.
        private float _pulseT;    // 1 -> 0 over PulseSec
        private float _pulseMag;  // peak fractional zoom-in
        private const float PulseSec = 0.25f;

        /// <summary>Punch the zoom in by <paramref name="magnitude"/> (0.03 ≈ subtle) and spring
        /// back over ~0.25s. A stronger pulse replaces a weaker in-flight one, never stacks.</summary>
        public void Pulse(float magnitude)
        {
            _pulseMag = Mathf.Max(_pulseT > 0f ? _pulseMag : 0f, magnitude);
            _pulseT = 1f;
        }

        /// <summary>Teleport the view: park focus AND target on <paramref name="worldFocus"/> with no
        /// glide (velocity cleared). For LOAD boundaries only (mode transitions behind the loading
        /// screen) — a cross-map SetFocus would smooth-damp the camera diagonally across the world,
        /// which read as "the same map sliding over" (user-caught). Gameplay following stays on the
        /// dead-zone glide.</summary>
        public void SnapTo(Vector3 worldFocus)
        {
            _focus = _targetFocus = worldFocus;
            _focusVel = Vector3.zero;
            _hasFocus = true;
            if (_cam != null) _cam.transform.position = _focus - _dir * _distance;
        }

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

            // Touch pinch: spreading fingers (positive delta) zoom IN, i.e. shrink _distance — the same
            // sign convention as the wheel. TouchGestures owns per-frame state, so this LateUpdate is
            // its sole once-per-frame caller. The >0.5px gate drops sub-pixel jitter; a pinch that began
            // over an open uGUI window returns 0 (gated at pinch start), so windows don't zoom the world.
            float pinch = TouchGestures.PinchDelta();
            if (Mathf.Abs(pinch) > 0.5f)
                _distance = Mathf.Clamp(_distance - pinch * PinchSensitivity, MinDistance, MaxDistance);

            // Ortho zoom: the visible extent is orthographicSize, tied to _distance so the zoom
            // range (Min/MaxDistance) maps to the same framing as the old perspective push-in.
            // The 10.6e pulse composes on top: a brief punch-IN and spring back (sin envelope),
            // decayed on UNSCALED time so it plays through a hit-stop dip.
            float pulse = 1f;
            if (_pulseT > 0f)
            {
                _pulseT = Mathf.Max(0f, _pulseT - Time.unscaledDeltaTime / PulseSec);
                pulse = 1f - _pulseMag * Mathf.Sin(Mathf.PI * (1f - _pulseT));
            }
            _cam.orthographicSize = _distance * SizePerDistance * pulse;

            // Dead-zone follow: while T sits within DeadRadius of F the camera does not move
            // at all (rock-still during combat shuffle). When T escapes, glide F just far
            // enough to put T back on the zone edge — SmoothDamp (deltaTime-based, so frame-
            // rate independent) with a hard speed cap keeps retargets calm, never a snap.
            if (_hasFocus)
            {
                var delta = _targetFocus - _focus;
                float dist = delta.magnitude;
                if (dist > DeadRadius)
                {
                    var goal = _targetFocus - delta / dist * DeadRadius; // stop at the zone edge
                    _focus = Vector3.SmoothDamp(_focus, goal, ref _focusVel, GlideSmoothTime, MaxGlideSpeed);
                }
                else
                {
                    _focusVel = Vector3.zero; // parked — kill residual velocity, hold perfectly still
                }
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
