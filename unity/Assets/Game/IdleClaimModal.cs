using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The "while you were away" claim modal (design §7 — the offline-return moment).
    /// Read-only: it just displays an <see cref="IdleReport"/> already produced by
    /// <see cref="Idle.Claim"/> in game-core. Full count-up animation/juice is M7;
    /// this is the functional placeholder. Dismiss with OK to clear it.
    /// </summary>
    public sealed class IdleClaimModal : MonoBehaviour
    {
        private IdleReport _report = null!;
        private Texture2D _dim = null!;

        public void Show(IdleReport report) => _report = report;

        private void OnGUI()
        {
            if (_report == null) return;
            if (_dim == null)
            {
                _dim = new Texture2D(1, 1);
                _dim.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                _dim.Apply();
            }

            // dim the screen behind the panel
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);

            const float w = 360f, h = 230f;
            float x = (Screen.width - w) / 2f;
            float y = (Screen.height - h) / 2f;

            GUI.Box(new Rect(x, y, w, h), GUIContent.none);

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            var line = new GUIStyle(GUI.skin.label) { fontSize = 15 };

            GUI.Label(new Rect(x, y + 14, w, 28), "While you were away", title);

            var ts = System.TimeSpan.FromMilliseconds(_report.ElapsedMs);
            string elapsed = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";
            string capped = _report.Capped ? "  (capped)" : "";

            float ly = y + 56;
            GUI.Label(new Rect(x + 28, ly, w - 40, 24), $"Away for: {elapsed}{capped}", line); ly += 30;
            GUI.Label(new Rect(x + 28, ly, w - 40, 24), $"Gold:  {_report.Gold:N0}", line); ly += 28;
            GUI.Label(new Rect(x + 28, ly, w - 40, 24), $"XP:    {_report.Xp:N0}", line); ly += 28;
            GUI.Label(new Rect(x + 28, ly, w - 40, 24), $"Items: {_report.Items.Count:N0}", line);

            if (GUI.Button(new Rect(x + w / 2f - 60f, y + h - 46f, 120f, 32f), "Collect"))
                Destroy(gameObject);
        }
    }
}
