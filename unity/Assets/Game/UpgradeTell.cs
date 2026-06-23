#nullable enable
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Client-side presentation of the pure <see cref="Upgrades"/> verdicts (Lever 2): the glyph,
    /// color, and short text that make a drop's worth legible at a glance — in the bag, the loot
    /// feed, and the equipment compare. All game rules stay in GameCore; this only chooses pixels.
    /// </summary>
    public static class UpgradeTell
    {
        public static readonly Color Up   = new Color(0.45f, 0.90f, 0.50f);
        public static readonly Color Down = new Color(0.95f, 0.45f, 0.45f);
        public static readonly Color Side = new Color(0.70f, 0.72f, 0.78f);

        public static Color Color(Upgrades.Verdict v) => v switch
        {
            Upgrades.Verdict.Upgrade => Up,
            Upgrades.Verdict.Downgrade => Down,
            _ => Side,
        };

        public static string Glyph(Upgrades.Verdict v) => v switch
        {
            Upgrades.Verdict.Upgrade => "▲",
            Upgrades.Verdict.Downgrade => "▼",
            _ => "=",
        };

        /// <summary>A signed percent like "+12%", "−5%", "±0%" (Unicode minus for the down case).</summary>
        public static string Pct(double delta)
        {
            int p = Mathf.RoundToInt((float)(delta * 100.0));
            string sign = p > 0 ? "+" : p < 0 ? "−" : "±";
            return sign + Mathf.Abs(p) + "%";
        }

        /// <summary>Pin a small green ▲ to a bag tile's top-right corner when the item is a genuine
        /// upgrade for someone. Only upgrades are badged — most bag items are worse than what's
        /// worn, so flagging them all would just be noise; the green ▲ stays meaningful.</summary>
        public static void BadgeTile(GameObject tile, Upgrades.Verdict v)
        {
            if (v != Upgrades.Verdict.Upgrade) return;
            var lbl = UiKit.Label(tile.transform, "▲", 15, TextAnchor.UpperRight, new Vector2(22, 16), Vector2.zero);
            var rt = (RectTransform)lbl.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-2f, -1f);
            lbl.color = Up;
            lbl.raycastTarget = false;
        }
    }
}
