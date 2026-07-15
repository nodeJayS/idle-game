#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Insets a full-stretch child RectTransform to the device SAFE AREA (notch / rounded-corner /
    /// home-indicator margins) by writing FRACTIONAL anchors each frame. Attach to a child of a
    /// screen-space-overlay canvas whose rect fills the canvas; corner-anchored HUD surfaces built
    /// under it then sit inside the safe rect. On desktop Screen.safeArea == the full screen, so the
    /// component is a provable no-op (anchors 0/1, offsets 0). Cheap: it caches the last applied
    /// safeArea rect and early-outs when nothing moved (safeArea is in pixels, so any resolution
    /// change moves it too — the rect alone is a sufficient key).
    /// </summary>
    public sealed class SafeArea : MonoBehaviour
    {
        /// <summary>Editor-probe seam: when non-null this rect (in PIXELS, exactly like
        /// Screen.safeArea) is used INSTEAD of Screen.safeArea. Desktop safeArea is always the full
        /// screen, so Play verification of a notch inset has no other way to fake one; set it to a
        /// sub-screen rect to preview the inset, null to restore the real device value. Ships null.</summary>
        public static Rect? DebugInset;

        // Sentinel so the first Apply always runs (no real safeArea has a negative origin+size).
        private Rect _applied = new(-1f, -1f, -1f, -1f);

        private void OnEnable() => _applied = new Rect(-1f, -1f, -1f, -1f); // re-apply after a re-enable
        private void Update() => Apply();

        private void Apply()
        {
            var safe = DebugInset ?? Screen.safeArea;
            if (safe == _applied) return;

            float w = Screen.width, h = Screen.height;
            // Mid-resolution-switch can report a 0-size screen for a frame (a known real thing here);
            // dividing by it would spew NaN anchors — skip and retry next frame while _applied is stale.
            if (w <= 0f || h <= 0f) return;
            _applied = safe;

            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(safe.x / w, safe.y / h);
            rt.anchorMax = new Vector2((safe.x + safe.width) / w, (safe.y + safe.height) / h);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
