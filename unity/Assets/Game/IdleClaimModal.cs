#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The "while you were away" claim modal (design §7 — the offline-return moment),
    /// built in uGUI. Read-only: it just displays an <see cref="IdleReport"/> already
    /// produced by <see cref="Idle.Claim"/> in game-core. The totals count up for a
    /// beat of juice; Collect dismisses it.
    /// </summary>
    public sealed class IdleClaimModal : MonoBehaviour
    {
        private const float CountUpSeconds = 0.9f;

        private long _gold, _xp;
        private int _items;
        private Text _goldText = null!, _xpText = null!, _itemsText = null!;
        private float _elapsed;
        private bool _animating;

        public void Show(IdleReport report)
        {
            _gold = report.Gold;
            _xp = report.Xp;
            _items = report.Items.Count;

            var canvas = UiKit.CreateCanvas("IdleClaimCanvas", transform, sortOrder: 90);
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(420, 300), new Color(0.10f, 0.10f, 0.14f, 1f));
            UiKit.Label(panel.transform, "While you were away", 24, TextAnchor.MiddleCenter,
                        new Vector2(380, 36), new Vector2(0, 110));

            var ts = System.TimeSpan.FromMilliseconds(report.ElapsedMs);
            string away = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";
            string capped = report.Capped ? "  (capped)" : "";

            Line(panel.transform, $"Away for: {away}{capped}", 50);
            _goldText = Line(panel.transform, "", 14);
            _xpText = Line(panel.transform, "", -22);
            _itemsText = Line(panel.transform, "", -58);
            Render(0f); // start the count-up at zero

            _animating = true;

            UiKit.TextButton(panel.transform, "Collect", new Vector2(160, 48), new Vector2(0, -110),
                () => Destroy(gameObject));
        }

        private void Update()
        {
            if (!_animating) return;
            _elapsed += Time.deltaTime;
            float t = CountUpSeconds > 0 ? Mathf.Clamp01(_elapsed / CountUpSeconds) : 1f;
            Render(EaseOutCubic(t));
            if (t >= 1f) _animating = false;
        }

        private void Render(float p)
        {
            _goldText.text = $"Gold:  {Num.Compact((long)(_gold * p))}";
            _xpText.text = $"XP:    {Num.Compact((long)(_xp * p))}";
            _itemsText.text = $"Items: {(int)(_items * p)}";
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private static Text Line(Transform parent, string text, float y) =>
            UiKit.Label(parent, text, 17, TextAnchor.MiddleLeft, new Vector2(320, 26), new Vector2(0, y));
    }
}
