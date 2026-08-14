#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// The kit's motion layer: windows and modals ease IN instead of snapping into existence.
    /// Lives beside <see cref="PanelKit"/> rather than inside it because the interesting problem
    /// isn't the tween — it's knowing WHICH builds deserve one.
    ///
    /// Every panel in this game is destroy-and-rebuild: picking an item in the bag runs
    /// <c>Close(); Open();</c> and the whole canvas is thrown away and re-made. So "a canvas was
    /// built" is NOT the same event as "the player opened something", and animating every build
    /// would make the window pulse on every click. <see cref="IsRebuild"/> is the discriminator,
    /// and PanelKit gates both the entrance AND the popup chime on it — which is also why the
    /// chime no longer re-fires on every redraw (a long-standing wart, fixed by the same rule).
    ///
    /// All timing is UNSCALED: windows open at the same speed whether the sim is hit-stopped,
    /// running at 2x, or paused behind a modal.
    /// </summary>
    public static class UiMotion
    {
        /// <summary>Entrance duration. Long enough to read as a movement, short enough that a player
        /// who is spamming the bag button never waits on it — the 120-150ms band UI motion lives in.</summary>
        public const float PanelInMs = 130f;

        /// <summary>Entrance scale. Deliberately shallow: the panel grows the last 3.5% into place, so
        /// it reads as "settling" rather than "zooming". A deeper punch fights the drop shadow, which
        /// tracks the panel's SIZE (not its scale) and would visibly slide out from under it.</summary>
        private const float PanelInScale = 0.965f;

        /// <summary>Frame a panel canvas of this name was last torn down in. Second, weaker signal for
        /// <see cref="IsRebuild"/>: Unity defers <c>Destroy</c> to end-of-frame, so on the usual
        /// close-then-open rebuild the old canvas is still a child when the new one is built (the
        /// primary signal catches it). This one only fires for immediate destruction, and can only
        /// ever be true within the SAME frame — where a same-name rebuild is the only explanation.</summary>
        private static readonly Dictionary<string, int> _closedFrame = new();

        /// <summary>Is a canvas named <paramref name="canvasName"/> about to be REBUILT rather than
        /// opened? True when one is already parented under <paramref name="parent"/> (Unity keeps the
        /// old object alive until end-of-frame, so the outgoing canvas is still there when the
        /// replacement is built), or when one was destroyed earlier in this very frame. Call it
        /// BEFORE creating the new canvas, or it will find that.</summary>
        public static bool IsRebuild(Transform? parent, string canvasName)
        {
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                    if (parent.GetChild(i).name == canvasName) return true;
            }
            return _closedFrame.TryGetValue(canvasName, out int frame) && frame == Time.frameCount;
        }

        /// <summary>Attach the motion bookkeeping to a freshly built panel canvas. Registration happens
        /// for EVERY panel (that's what keeps <see cref="_closedFrame"/> honest); the entrance itself
        /// only plays when <paramref name="animate"/> — a rebuild registers silently and stays put.
        /// Under Reduced Motion the panel appears instantly: no scale, no fade.</summary>
        public static void Register(GameObject canvasGo, string canvasName, RectTransform scaleTarget, bool animate)
        {
            var motion = canvasGo.AddComponent<PanelMotion>();
            motion.CanvasName = canvasName;
            if (!animate || Settings.ReducedMotion) return;

            var group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // Raycasts stay LIVE through the entrance. A window that ignores clicks for its first
            // 130ms feels broken in exactly the way this slice is meant to fix, and it would also
            // put a race under any probe that clicks a button the frame after opening it.
            motion.Begin(group, scaleTarget);
        }

        internal static void NoteClosed(string canvasName)
        {
            if (!string.IsNullOrEmpty(canvasName)) _closedFrame[canvasName] = Time.frameCount;
        }

        /// <summary>Ease-out cubic — fast off the line, settling at the end. The standard curve for
        /// something ARRIVING; anything symmetric reads sluggish at these durations.</summary>
        internal static float EaseOut(float t)
        {
            float inv = 1f - Mathf.Clamp01(t);
            return 1f - inv * inv * inv;
        }

        /// <summary>Drives one panel's entrance and stamps its teardown frame. Self-disables the moment
        /// the tween lands, so an open window costs nothing per frame.</summary>
        public sealed class PanelMotion : MonoBehaviour
        {
            public string CanvasName = "";

            private CanvasGroup? _group;
            private RectTransform? _scale;
            private float _elapsedMs;
            private bool _running;

            internal void Begin(CanvasGroup group, RectTransform scaleTarget)
            {
                _group = group;
                _scale = scaleTarget;
                _elapsedMs = 0f;
                _running = true;
                Apply(0f);
            }

            private void Update()
            {
                if (!_running) return;
                _elapsedMs += Time.unscaledDeltaTime * 1000f;
                float t = Mathf.Clamp01(_elapsedMs / PanelInMs);
                Apply(t);
                if (t < 1f) return;

                _running = false;
                // Drop the CanvasGroup once it has done its job: it is pure overhead on a settled
                // window, and leaving one behind would silently change how a later slice's
                // interactable/blocksRaycasts work reads.
                if (_group != null) Destroy(_group);
                _group = null;
                _scale = null;
            }

            private void Apply(float t)
            {
                float e = EaseOut(t);
                if (_group != null) _group.alpha = e;
                if (_scale != null) _scale.localScale = Vector3.one * Mathf.Lerp(PanelInScale, 1f, e);
            }

            private void OnDestroy()
            {
                NoteClosed(CanvasName);
                // A canvas torn down mid-entrance must not leave its scale behind: SafeRoot is
                // get-or-created per canvas, so it dies with this object — but a future caller that
                // reuses a scale target would inherit 0.965 forever. Cheap insurance.
                if (_scale != null) _scale.localScale = Vector3.one;
            }
        }
    }
}
