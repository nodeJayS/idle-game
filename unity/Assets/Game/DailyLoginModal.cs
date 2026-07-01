#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Daily-login reward modal (Lever 4 — the premium-currency hook), built in uGUI like
    /// <see cref="IdleClaimModal"/>. Shown on launch when a claim is available: it previews today's
    /// streak day and gem reward via <see cref="DailyLogin.Preview"/> — the reducer's own dry-run,
    /// so the shown amount is exactly what Collect grants (no separate preview math, no count-up
    /// that can rest on a wrong number). Collect routes the actual claim through
    /// <see cref="CombatView.ClaimDailyLogin"/> (the sim mutation) before dismissing. Read-only over
    /// the save except that one call.
    /// </summary>
    public sealed class DailyLoginModal : MonoBehaviour
    {
        private CombatView? _view; // set once actually shown, so OnDestroy releases the HUD gate

        public void Show(CombatView view, GameConfig cfg, long now)
        {
            // The reducer's dry-run is the single source of truth for the preview. If no claim is
            // actually available (already claimed this UTC day / rolled-back clock), never render a
            // reward Collect wouldn't grant — even if a caller forgot to gate on CanClaim.
            var (gems, streak, canClaim) = DailyLogin.Preview(view.CurrentSave, cfg, now);
            if (!canClaim)
            {
                Debug.LogWarning("[DailyLoginModal] Shown with no claim available — dismissing.");
                Destroy(gameObject);
                return;
            }
            _view = view;
            view.PushLaunchModal(); // hide the IMGUI HUD (HP-bar dashes) that draws above uGUI
            bool milestone = cfg.Balance.DailyLoginMilestoneEvery > 0 && streak % cfg.Balance.DailyLoginMilestoneEvery == 0;

            var canvas = UiKit.CreateCanvas("DailyLoginCanvas", transform, sortOrder: 92); // above the idle-claim modal
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(430, 300), new Color(0.10f, 0.11f, 0.16f, 1f));

            var title = UiKit.Label(panel.transform, "Daily Reward", 24, TextAnchor.MiddleCenter,
                                    new Vector2(390, 36), new Vector2(0, 112));
            title.color = new Color(0.62f, 0.85f, 1f);

            var streakLine = UiKit.Label(panel.transform, $"Day {streak} streak", 18, TextAnchor.MiddleCenter,
                                         new Vector2(390, 28), new Vector2(0, 62));
            streakLine.color = new Color(0.88f, 0.91f, 0.97f);

            // The exact grant, rendered up-front: a count-up here once rested on "+0 gems" whenever
            // frames stalled (paused editor, launch hitch), showing a number Collect doesn't grant.
            var gemsText = UiKit.Label(panel.transform, $"+{Num.CompactFloor(gems)} gems", 30,
                                       TextAnchor.MiddleCenter, new Vector2(390, 40), new Vector2(0, 6));
            gemsText.color = new Color(0.55f, 0.85f, 1f);
            gemsText.fontStyle = FontStyle.Bold;

            if (milestone)
            {
                var m = UiKit.Label(panel.transform, "★ Streak milestone bonus!", 14, TextAnchor.MiddleCenter,
                                    new Vector2(390, 22), new Vector2(0, -34));
                m.color = new Color(1f, 0.85f, 0.4f);
            }

            UiKit.TextButton(panel.transform, "Collect", new Vector2(240, 66), new Vector2(0, -108),
                () => { view.ClaimDailyLogin(now); Destroy(gameObject); });
        }

        private void OnDestroy() => _view?.PopLaunchModal();
    }
}
