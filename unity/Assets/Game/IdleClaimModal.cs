#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The "while you were away" claim modal (design §7 — the offline-return moment),
    /// built in uGUI. Read-only: it just displays an <see cref="IdleReport"/> already
    /// produced by <see cref="Idle.Claim"/> in game-core. Count-up animation/juice is
    /// M7; this is the functional version. Collect dismisses it.
    /// </summary>
    public sealed class IdleClaimModal : MonoBehaviour
    {
        public void Show(IdleReport report)
        {
            var canvas = UiKit.CreateCanvas("IdleClaimCanvas", transform, sortOrder: 90);
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(420, 300), new Color(0.10f, 0.10f, 0.14f, 1f));
            UiKit.Label(panel.transform, "While you were away", 24, TextAnchor.MiddleCenter,
                        new Vector2(380, 36), new Vector2(0, 110));

            var ts = System.TimeSpan.FromMilliseconds(report.ElapsedMs);
            string away = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";
            string capped = report.Capped ? "  (capped)" : "";

            Line(panel.transform, $"Away for: {away}{capped}", 50);
            Line(panel.transform, $"Gold:  {report.Gold:N0}", 14);
            Line(panel.transform, $"XP:    {report.Xp:N0}", -22);
            Line(panel.transform, $"Items: {report.Items.Count:N0}", -58);

            UiKit.TextButton(panel.transform, "Collect", new Vector2(160, 48), new Vector2(0, -110),
                () => Destroy(gameObject));
        }

        private static void Line(Transform parent, string text, float y) =>
            UiKit.Label(parent, text, 17, TextAnchor.MiddleLeft, new Vector2(320, 26), new Vector2(0, y));
    }
}
