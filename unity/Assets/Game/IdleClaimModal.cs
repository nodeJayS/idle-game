#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The "while you were away" claim modal (design §7 — the offline-return moment),
    /// built on <see cref="PanelKit"/>. Read-only: it just displays an <see cref="IdleReport"/>
    /// already produced by <see cref="Idle.Claim"/> in game-core. The totals count up for a
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
        private CombatView? _view; // set while shown, so OnDestroy releases the HUD gate

        public void Show(CombatView view, IdleReport report)
        {
            _view = view;
            view.PushLaunchModal(); // hide the IMGUI HUD (HP-bar dashes) that draws above uGUI

            _gold = report.Gold;
            _xp = report.Xp;
            _items = report.Items.Count;

            PanelKit.Modal(transform, "IdleClaimCanvas", 90, new Vector2(420f, 300f),
                           out var body, backdrop: Theme.BackdropDim);

            PanelKit.Label(body, "Idle Rewards", Theme.FsH1, Theme.TextBright, TextAnchor.MiddleCenter);

            var ts = System.TimeSpan.FromMilliseconds(report.ElapsedMs);
            string away = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";
            string capped = report.Capped ? "  (capped)" : "";
            PanelKit.Label(body, $"Time: {away}{capped}", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);

            _goldText = PanelKit.Label(body, "", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);
            _xpText = PanelKit.Label(body, "", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);
            _itemsText = PanelKit.Label(body, "", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);
            Render(0f); // start the count-up at zero
            _animating = true;

            PanelKit.Flex(body); // slack pushes Collect to the panel bottom

            var row = PanelKit.Row(body, Theme.BtnH);
            PanelKit.FlexSpacer(row);
            PanelKit.ButtonCell(row, "Collect", () => Destroy(gameObject), width: 240f, fontSize: Theme.FsH1);
            PanelKit.FlexSpacer(row);
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
            // granted amounts floor (game-design §7): never advertise more than is banked
            _goldText.text = $"Gold:  {Num.CompactFloor((long)(_gold * p))}";
            _xpText.text = $"XP:    {Num.CompactFloor((long)(_xp * p))}";
            _itemsText.text = $"Items: {(int)(_items * p)}";
        }

        private void OnDestroy() => _view?.PopLaunchModal();

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    }
}
