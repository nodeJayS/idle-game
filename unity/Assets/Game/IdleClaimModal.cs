#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The ONE boot arrival card (10.14b — the 30-second session, mobile arc MM2): the old idle-claim
    /// modal (design §7 — the offline-return moment) EXTENDED to also carry the daily-login (and any
    /// other <see cref="Goals.Claimables"/>) payoff, so a returning player gets one card and one tap
    /// instead of two sequential modals. Built on <see cref="PanelKit"/>, keeping the 10.3d rebuild's
    /// count-up beat as the emotional core.
    ///
    /// Read-only DISPLAY: it renders an <see cref="IdleReport"/> PREVIEW (<see cref="Idle.Preview"/>)
    /// plus the pending goal claims — nothing is granted until Collect, which routes the WHOLE arrival
    /// through <see cref="CombatView.ArriveClaim"/> (one atomic <see cref="Session.Arrive"/>). Because
    /// idle's LastClaimAt is untouched until then, the previewed numbers ARE what Collect grants (same
    /// save + same boot <c>now</c> ⇒ identical rolls), and a quit before Collect simply re-previews the
    /// same accrual next launch — nothing is lost. The idle totals count up for a beat of juice; the
    /// bonus rows (gems) are static, showing each claim's exact grant.
    /// </summary>
    public sealed class IdleClaimModal : MonoBehaviour
    {
        private const float CountUpSeconds = 0.9f;

        private long _gold, _xp;
        private int _items;
        private Text _goldText = null!, _xpText = null!, _itemsText = null!;
        private float _elapsed;
        private bool _animating;
        private long _now;         // the boot timestamp the card previewed against; Collect applies at it
        private CombatView? _view; // set while shown, so OnDestroy releases the HUD gate

        public void Show(CombatView view, IdleReport idle, List<GoalClaim> claims, long now)
        {
            _view = view;
            _now = now;
            view.PushLaunchModal(); // hide the IMGUI HUD (HP-bar dashes) that draws above uGUI

            bool hasIdle = !idle.IsEmpty; // daily may be claimable with no offline gap (idle-empty card)
            _gold = idle.Gold;
            _xp = idle.Xp;
            _items = idle.Items.Count;

            PanelKit.Modal(transform, "IdleClaimCanvas", 90, new Vector2(420f, 340f),
                           out var body, backdrop: Theme.BackdropDim);

            // Title leans on whichever half is present: the offline-return moment keeps its name; a
            // rare idle-empty arrival (clock crossed midnight with no accrual) reads as the daily.
            PanelKit.Label(body, hasIdle ? "Idle Rewards" : "Daily Reward",
                           Theme.FsH1, Theme.TextBright, TextAnchor.MiddleCenter);

            if (hasIdle)
            {
                var ts = System.TimeSpan.FromMilliseconds(idle.ElapsedMs);
                string away = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";
                string capped = idle.Capped ? "  (capped)" : "";
                PanelKit.Label(body, $"Time: {away}{capped}", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);

                _goldText = PanelKit.Label(body, "", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);
                _xpText = PanelKit.Label(body, "", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);
                _itemsText = PanelKit.Label(body, "", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);
                Render(0f); // start the count-up at zero
                _animating = true;
            }

            // Bonus rows: one clearly-separated line per pending goal claim (daily login etc.). Static —
            // the gem number is the exact grant from the claim's own preview (Goals.Claimables), so it
            // can't diverge from what Collect banks. A muted "Bonus" header separates them from the idle
            // count-up when both halves are present.
            if (claims.Count > 0)
            {
                if (hasIdle) PanelKit.Label(body, "Bonus", Theme.FsLabel, Theme.TextDim, TextAnchor.MiddleLeft);
                foreach (var c in claims)
                    PanelKit.Label(body, $"{c.Label}   +{Num.CompactFloor(c.Gems)} gems",
                                   Theme.FsH2, Theme.AccentGold, TextAnchor.MiddleLeft);
            }

            PanelKit.Flex(body); // slack pushes Collect to the panel bottom

            var row = PanelKit.Row(body, Theme.BtnH);
            PanelKit.FlexSpacer(row);
            // Collect applies the WHOLE arrival (idle + every goal claim) in one atomic Session.Arrive.
            PanelKit.ButtonCell(row, "Collect", () => { _view!.ArriveClaim(_now); Destroy(gameObject); },
                                width: 240f, fontSize: Theme.FsH1);
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
