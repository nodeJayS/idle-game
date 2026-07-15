#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace IdleGame.Game
{
    /// <summary>
    /// Two-finger pinch detection on the raw new-Input-System touchscreen (deliberately NOT the
    /// EnhancedTouch layer — no extra dependency). The math is split out PURE
    /// (<see cref="ComputePinchDelta"/>) so it is editor-testable with no device attached; the
    /// device read (<see cref="PinchDelta"/>) owns one frame of static state and so carries a
    /// single-writer-per-frame contract — call it at most once per frame from exactly one caller
    /// (CameraRig.LateUpdate), like the rest of the per-frame HUD polls.
    /// </summary>
    public static class TouchGestures
    {
        // Previous frame's two touch positions (the "previous separation", kept as the point pair so
        // PinchDelta can feed ComputePinchDelta directly). Reset when the pinch ends.
        private static bool _pinching;
        private static Vector2 _prev0, _prev1;
        private static bool _ignore; // this pinch began over a uGUI surface -> don't zoom the world

        /// <summary>True while two touches are down (a pinch is forming). Cheap; CameraRig may gate on it.</summary>
        public static bool PinchActive { get; private set; }

        /// <summary>Pure pinch delta: change in separation between the two touch pairs, in PIXELS
        /// (positive = fingers spreading). distance(cur) - distance(prev). Order-independent (distance
        /// is symmetric), so finger-slot swaps between frames don't matter. No device read — the Play
        /// probe asserts this directly.</summary>
        public static float ComputePinchDelta(Vector2 prev0, Vector2 prev1, Vector2 cur0, Vector2 cur1)
            => Vector2.Distance(cur0, cur1) - Vector2.Distance(prev0, prev1);

        /// <summary>This frame's change in two-finger separation, in PIXELS (positive = spreading =
        /// zoom in). Returns 0 when fewer than two touches are down, on the first frame a pinch forms
        /// (no previous separation to diff), and for the whole life of a pinch that began over uGUI.
        /// Owns static per-frame state: call at most ONCE per frame, from one caller.</summary>
        public static float PinchDelta()
        {
            var ts = Touchscreen.current;
            Vector2 cur0 = default, cur1 = default;
            int id0 = 0, id1 = 0, count = 0;
            if (ts != null)
            {
                var touches = ts.touches;
                for (int i = 0; i < touches.Count && count < 2; i++)
                {
                    var t = touches[i];
                    if (!t.isInProgress) continue;
                    if (count == 0) { cur0 = t.position.ReadValue(); id0 = t.touchId.ReadValue(); }
                    else            { cur1 = t.position.ReadValue(); id1 = t.touchId.ReadValue(); }
                    count++;
                }
            }

            if (count < 2)
            {
                // Pinch ended (or never formed): reset so the next pinch starts clean.
                _pinching = false;
                _ignore = false;
                PinchActive = false;
                return 0f;
            }

            PinchActive = true;

            if (!_pinching)
            {
                // New pinch. Sample the over-UI gate ONCE, here at pinch start: if either finger is
                // over a uGUI raycast target, ignore this whole pinch (until it ends) so a pinch on an
                // open window can't also zoom the world. No previous separation yet -> emit 0.
                _pinching = true;
                _prev0 = cur0; _prev1 = cur1;
                var es = EventSystem.current;
                _ignore = es != null && (es.IsPointerOverGameObject(id0) || es.IsPointerOverGameObject(id1));
                return 0f;
            }

            float delta = _ignore ? 0f : ComputePinchDelta(_prev0, _prev1, cur0, cur1);
            _prev0 = cur0; _prev1 = cur1;
            return delta;
        }
    }
}
