#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IdleGame.Game
{
    /// <summary>
    /// The kit's motion layer: windows and modals ease IN and OUT instead of snapping into and out of
    /// existence. Lives beside <see cref="PanelKit"/> rather than inside it because the interesting
    /// problem isn't the tween — it's knowing WHICH builds and teardowns deserve one.
    ///
    /// Every panel in this game is destroy-and-rebuild: picking an item in the bag runs
    /// <c>Teardown(); Build();</c> and the whole canvas is thrown away and re-made. So "a canvas was
    /// built" is NOT the same event as "the player opened something", and "a canvas was destroyed" is
    /// not "the player closed something". <see cref="IsRebuild"/> is the discriminator on the way in;
    /// on the way out the panels discriminate structurally instead — their public <c>Close()</c> is
    /// the real-close verb (animated), and only the private rebuild paths ask for an instant teardown.
    /// PanelKit gates the entrance AND the popup chime on IsRebuild — which is also why the chime no
    /// longer re-fires on every redraw (a long-standing wart, fixed by the same rule).
    ///
    /// All timing is UNSCALED: windows move at the same speed whether the sim is hit-stopped,
    /// running at 2x, or paused behind a modal.
    /// </summary>
    public static class UiMotion
    {
        /// <summary>Entrance duration. Long enough to read as a movement, short enough that a player
        /// who is spamming the bag button never waits on it — the 120-150ms band UI motion lives in.</summary>
        public const float PanelInMs = 130f;

        /// <summary>Exit duration. Shorter than the entrance on purpose: an arrival wants to be seen,
        /// a departure only has to not be abrupt. Any longer and closing a window feels like asking
        /// permission to close it.</summary>
        public const float PanelOutMs = 90f;

        /// <summary>Entrance scale. Deliberately shallow: the panel grows the last 3.5% into place, so
        /// it reads as "settling" rather than "zooming". A deeper punch fights the drop shadow, which
        /// tracks the panel's SIZE (not its scale) and would visibly slide out from under it.</summary>
        private const float PanelInScale = 0.965f;

        /// <summary>Exit scale — the panel shrinks back toward where it grew from, so open and close
        /// read as one gesture and its reverse rather than two unrelated effects.</summary>
        private const float PanelOutScale = 0.975f;

        /// <summary>Suffix stamped onto a canvas that is playing its exit. A closing canvas is still
        /// parented for <see cref="PanelOutMs"/>, and <see cref="IsRebuild"/> matches on NAME — without
        /// the rename, closing a window and immediately reopening it would look like a rebuild and the
        /// new window would skip its entrance.</summary>
        private const string ClosingSuffix = " (closing)";

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
        /// for EVERY panel (that's what keeps <see cref="_closedFrame"/> honest, and it is what lets a
        /// panel that was BUILT as a rebuild still animate when it is finally closed); the entrance
        /// itself only plays when <paramref name="animate"/> — a rebuild registers silently and stays
        /// put. Under Reduced Motion the panel appears instantly: no scale, no fade.</summary>
        public static void Register(GameObject canvasGo, string canvasName, RectTransform scaleTarget, bool animate)
        {
            var motion = canvasGo.AddComponent<PanelMotion>();
            motion.CanvasName = canvasName;
            motion.Target = scaleTarget;
            if (!animate || Settings.ReducedMotion) return;

            var group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // Raycasts stay LIVE through the entrance. A window that ignores clicks for its first
            // 130ms feels broken in exactly the way this slice is meant to fix, and it would also
            // put a race under any probe that clicks a button the frame after opening it.
            motion.Begin(group);
        }

        /// <summary>Take a panel canvas away. <paramref name="animate"/> is the whole point: TRUE is a
        /// player-facing close (the panel eases out, then destroys itself), FALSE is a rebuild tearing
        /// its own canvas down to re-make it this frame — which must stay instant, or the outgoing
        /// canvas would linger on top of its own replacement. Falls back to an immediate destroy
        /// whenever the tween can't run (Reduced Motion, or a canvas that never registered).</summary>
        public static void Dismiss(GameObject? canvasGo, bool animate)
        {
            if (canvasGo == null) return;
            if (!animate || Settings.ReducedMotion) { Object.Destroy(canvasGo); return; }

            var motion = canvasGo.GetComponent<PanelMotion>();
            if (motion == null) { Object.Destroy(canvasGo); return; }
            motion.BeginExit();
        }

        /// <summary>Press durations. Down is the fastest motion in the kit on purpose — a press must
        /// feel like it happened AT the finger, not after it. The release is slower so the button
        /// reads as springing back rather than snapping.</summary>
        public const float PressDownMs = 55f;
        public const float PressUpMs = 90f;

        /// <summary>How far a pressed control sinks. 4% is under the threshold where anyone would call
        /// it an animation and over the one where the hand stops believing the tap registered.</summary>
        private const float PressScale = 0.96f;

        /// <summary>Give a control the kit's press squash. Rides <see cref="UiKit.ApplyButtonStates"/>,
        /// so the tint and the squash are one contract and no button can get half of it. Scale is
        /// layout-independent (uGUI lays out by rect, not by scale), which is exactly why the squash
        /// scales the control instead of resizing it: a shrinking button inside a layout group would
        /// shove its neighbours around on every tap.</summary>
        public static void AttachPress(Selectable s)
        {
            if (Settings.ReducedMotion) return;
            if (s.GetComponent<PressSquash>() != null) return;
            s.gameObject.AddComponent<PressSquash>().Bind(s);
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

        /// <summary>Ease-in quadratic — hesitates, then leaves. The mirror of <see cref="EaseOut"/> and
        /// the standard curve for something DEPARTING: the panel stays legible for the first frames, so
        /// the click that dismissed it reads as connected to it, and then it goes quickly.</summary>
        internal static float EaseIn(float t)
        {
            float c = Mathf.Clamp01(t);
            return c * c;
        }

        /// <summary>Drives one panel's entrance and exit and stamps its teardown frame. Self-disables the
        /// moment an entrance lands, so an open window costs nothing per frame.</summary>
        public sealed class PanelMotion : MonoBehaviour
        {
            public string CanvasName = "";

            /// <summary>What the tweens scale — SafeRoot, not the panel (see PanelKit). Kept for the
            /// life of the canvas rather than just the entrance: a panel built silently as a rebuild
            /// still needs this when the player eventually closes it.</summary>
            public RectTransform? Target;

            private CanvasGroup? _group;
            private float _elapsedMs;
            private bool _running;
            private bool _exiting;
            private float _fromAlpha = 1f;
            private float _fromScale = 1f;

            internal void Begin(CanvasGroup group)
            {
                _group = group;
                _elapsedMs = 0f;
                _running = true;
                _exiting = false;
                Apply(0f);
            }

            /// <summary>Start the exit. Idempotent — a second close request on an already-closing panel
            /// must not restart the tween, or double-clicking the X would keep the window alive.</summary>
            internal void BeginExit()
            {
                if (_exiting) return;
                _exiting = true;
                _running = true;
                _elapsedMs = 0f;

                // Stop being FINDABLE: IsRebuild scans children by name, and this canvas outlives the
                // close by PanelOutMs. Rename before anything else can look.
                gameObject.name = CanvasName + ClosingSuffix;
                // And stop stamping the same-frame close signal: with a deferred destroy that stamp
                // would land in whatever frame the tween happens to end, which says nothing true.
                CanvasName = "";

                // A closing panel must not eat the click that comes after it. It is a ghost from the
                // moment the player dismisses it, even though it is still on screen.
                if (_group == null) _group = gameObject.GetComponent<CanvasGroup>();
                if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;

                // Start from wherever the panel actually IS — it may be mid-entrance (opened and shut
                // inside 130ms), where an exit from full opacity would visibly pop brighter first.
                _fromAlpha = _group.alpha;
                _fromScale = Target != null ? Target.localScale.x : 1f;
            }

            private void Update()
            {
                if (!_running) return;
                _elapsedMs += Time.unscaledDeltaTime * 1000f;
                float t = Mathf.Clamp01(_elapsedMs / (_exiting ? PanelOutMs : PanelInMs));
                Apply(t);
                if (t < 1f) return;

                _running = false;
                if (_exiting) { Destroy(gameObject); return; }

                // Drop the CanvasGroup once the entrance has done its job: it is pure overhead on a
                // settled window, and leaving one behind would silently change how a later slice's
                // interactable/blocksRaycasts work reads. The exit re-adds one if it needs it.
                if (_group != null) Destroy(_group);
                _group = null;
            }

            private void Apply(float t)
            {
                if (_exiting)
                {
                    float x = EaseIn(t);
                    if (_group != null) _group.alpha = Mathf.Lerp(_fromAlpha, 0f, x);
                    if (Target != null) Target.localScale = Vector3.one * Mathf.Lerp(_fromScale, PanelOutScale, x);
                    return;
                }

                float e = EaseOut(t);
                if (_group != null) _group.alpha = e;
                if (Target != null) Target.localScale = Vector3.one * Mathf.Lerp(PanelInScale, 1f, e);
            }

            private void OnDestroy()
            {
                NoteClosed(CanvasName);
                // A canvas torn down mid-tween must not leave its scale behind: SafeRoot is
                // get-or-created per canvas, so it dies with this object — but a future caller that
                // reuses a scale target would inherit 0.965 forever. Cheap insurance.
                if (Target != null) Target.localScale = Vector3.one;
            }
        }

        /// <summary>Sinks a control under the finger and springs it back. Rides alongside the Button
        /// rather than inside it: uGUI delivers a pointer event to EVERY handler on the object, so the
        /// squash and the Button's own click/tint stay independent — nothing here can swallow a click.
        /// </summary>
        public sealed class PressSquash : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            private Selectable? _sel;
            private RectTransform? _rt;
            private float _from = 1f;
            private float _to = 1f;
            private float _elapsedMs;
            private float _durMs = 1f;
            private bool _running;

            internal void Bind(Selectable s)
            {
                _sel = s;
                _rt = s.transform as RectTransform;
            }

            /// <summary>A DEAD control must not squash: the raycast still lands on its graphic (that is
            /// what makes disabled buttons feel solid rather than absent), so without this check a
            /// greyed-out button would answer the finger and promise something it won't do.</summary>
            public void OnPointerDown(PointerEventData e)
            {
                if (_sel != null && !_sel.IsInteractable()) return;
                To(PressScale, PressDownMs);
            }

            /// <summary>No interactable check here: whatever happened to the control mid-press, a
            /// button that stayed squashed would read as stuck.</summary>
            public void OnPointerUp(PointerEventData e) => To(1f, PressUpMs);

            private void To(float target, float ms)
            {
                if (_rt == null) return;
                _from = _rt.localScale.x;
                _to = target;
                _elapsedMs = 0f;
                _durMs = Mathf.Max(1f, ms);
                _running = true;
            }

            private void Update()
            {
                if (!_running || _rt == null) return;
                _elapsedMs += Time.unscaledDeltaTime * 1000f;
                float t = Mathf.Clamp01(_elapsedMs / _durMs);
                _rt.localScale = Vector3.one * Mathf.Lerp(_from, _to, EaseOut(t));
                if (t >= 1f) _running = false;
            }

            /// <summary>A control switched off mid-press (a rebuild, a tab swap) comes back at full
            /// size. Scale is the one piece of state here that would otherwise survive.</summary>
            private void OnDisable()
            {
                _running = false;
                if (_rt != null) _rt.localScale = Vector3.one;
            }
        }
    }
}
