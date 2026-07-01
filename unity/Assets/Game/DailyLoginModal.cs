#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Daily-login reward modal (Lever 4 — the premium-currency hook), built in uGUI like
    /// <see cref="IdleClaimModal"/>. Shown on launch when a claim is available: it previews today's
    /// streak day and gem reward, and Collect routes the actual claim through
    /// <see cref="CombatView.ClaimDailyLogin"/> (the sim mutation) before dismissing. Read-only over
    /// the save except that one call.
    /// </summary>
    public sealed class DailyLoginModal : MonoBehaviour
    {
        private const float CountUpSeconds = 0.7f;

        private long _gems;
        private Text _gemsText = null!;
        private float _elapsed;
        private bool _animating;

        public void Show(CombatView view, GameConfig cfg, long now)
        {
            var save = view.CurrentSave;
            int streak = DailyLogin.StreakIfClaimed(save, now);
            _gems = DailyLogin.RewardFor(streak, cfg);
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

            _gemsText = UiKit.Label(panel.transform, "", 30, TextAnchor.MiddleCenter, new Vector2(390, 40), new Vector2(0, 6));
            _gemsText.color = new Color(0.55f, 0.85f, 1f);
            _gemsText.fontStyle = FontStyle.Bold;

            if (milestone)
            {
                var m = UiKit.Label(panel.transform, "★ Streak milestone bonus!", 14, TextAnchor.MiddleCenter,
                                    new Vector2(390, 22), new Vector2(0, -34));
                m.color = new Color(1f, 0.85f, 0.4f);
            }

            Render(0f);
            _animating = true;

            UiKit.TextButton(panel.transform, "Collect", new Vector2(240, 66), new Vector2(0, -108),
                () => { view.ClaimDailyLogin(now); Destroy(gameObject); });
        }

        private void Update()
        {
            if (!_animating) return;
            _elapsed += Time.deltaTime;
            float t = CountUpSeconds > 0 ? Mathf.Clamp01(_elapsed / CountUpSeconds) : 1f;
            Render(EaseOutCubic(t));
            if (t >= 1f) _animating = false;
        }

        private void Render(float p) => _gemsText.text = $"+{Num.Compact((long)(_gems * p))} gems";

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    }
}
