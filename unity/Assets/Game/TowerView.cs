#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Tower of Ascension entry screen (alt mode). Shows how high you've climbed, the permanent
    /// account-wide buff earned from milestones, and what the next floor holds (its modifier + whether
    /// it's a guardian/milestone floor), with an "Enter Floor N" button that hands off to
    /// <see cref="CombatView.EnterTowerFloor"/>. A toggled overlay opened from the control bar, like
    /// the Modifiers panel. Read-only over the save except entering a floor (routed through the view).
    /// </summary>
    public sealed class TowerView : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;
        private Canvas? _canvas;

        public bool IsOpen => _canvas != null;

        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }

        public void Toggle() { if (IsOpen) Close(); else Build(); }

        public void Close()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
        }

        private void Build()
        {
            _canvas = UiKit.CreateCanvas("TowerCanvas", transform, sortOrder: 90);
            var save = _view.CurrentSave;

            int highest = Tower.HighestFloor(save);
            int max = Tower.MaxFloor(_cfg);
            int next = Tower.NextFloor(save);
            bool complete = Tower.IsComplete(save, _cfg);
            double buffPct = Tower.AccountBuffPct(save, _cfg) * 100;

            var panel = UiKit.Panel(_canvas.transform, new Vector2(560f, 380f), new Color(0.09f, 0.10f, 0.13f, 0.98f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 50f);

            var title = UiKit.Label(panel.transform, "Tower of Ascension", 22, TextAnchor.UpperLeft, new Vector2(380f, 30f), Vector2.zero);
            title.color = new Color(0.62f, 0.78f, 1f);
            AnchorTL(title, new Vector2(22f, -14f));

            var close = UiKit.TextButton(panel.transform, "Close", new Vector2(84f, 30f), Vector2.zero, Close, 15);
            AnchorTR((RectTransform)close.transform, new Vector2(-14f, -14f));

            var climbed = UiKit.Label(panel.transform, $"Highest floor cleared: <b>{highest}</b> / {max}", 16,
                TextAnchor.UpperLeft, new Vector2(520f, 24f), Vector2.zero);
            climbed.color = new Color(0.85f, 0.89f, 0.95f); climbed.supportRichText = true;
            AnchorTL(climbed, new Vector2(22f, -56f));

            string buffLine = buffPct > 0
                ? $"Ascension buff: <color=#ffd766>+{buffPct:0}%</color> Hp / Atk / Def (account-wide)"
                : "Ascension buff: none yet";
            int nextMilestone = (Tower.MilestonesCleared(save, _cfg) + 1) * _cfg.Balance.TowerMilestoneEvery;
            var buff = UiKit.Label(panel.transform,
                $"{buffLine}\nNext milestone at floor {Mathf.Min(nextMilestone, max)} (+{_cfg.Balance.TowerMilestoneStatPct * 100:0}% more)",
                14, TextAnchor.UpperLeft, new Vector2(520f, 50f), Vector2.zero);
            buff.color = new Color(0.80f, 0.84f, 0.90f); buff.supportRichText = true;
            AnchorTL(buff, new Vector2(22f, -92f));

            if (complete)
            {
                var done = UiKit.Label(panel.transform, "Tower conquered — every floor cleared!", 17,
                    TextAnchor.MiddleCenter, new Vector2(500f, 40f), Vector2.zero);
                done.color = new Color(0.6f, 0.95f, 0.65f);
                AnchorTL(done, new Vector2(30f, -170f));
                return;
            }

            // Next-floor preview: difficulty, modifier, and whether it's a milestone (guardian) floor.
            bool milestone = next % _cfg.Balance.TowerMilestoneEvery == 0;
            string? modId = _cfg.TowerModifierForFloor(next);
            string modName = modId != null && _cfg.Modifiers.TryGetValue(modId, out var md) ? md.Name : "none";
            string preview = $"<b>Floor {next}</b>   ·   modifier: {modName}"
                           + (milestone ? "   ·   <color=#ffd766>guardian + buff</color>" : "");
            var prev = UiKit.Label(panel.transform, preview, 15, TextAnchor.UpperLeft, new Vector2(520f, 24f), Vector2.zero);
            prev.color = new Color(0.86f, 0.80f, 0.72f); prev.supportRichText = true;
            AnchorTL(prev, new Vector2(22f, -160f));

            var warn = UiKit.Label(panel.transform,
                "One attempt per floor — no farm income here. Beat it to keep the floor; fail and train up to retry.",
                12, TextAnchor.UpperLeft, new Vector2(516f, 40f), Vector2.zero);
            warn.color = new Color(0.66f, 0.70f, 0.78f);
            AnchorTL(warn, new Vector2(22f, -192f));

            int floor = next;
            var enter = UiKit.TextButton(panel.transform, $"Enter Floor {floor}", new Vector2(240f, 52f), Vector2.zero,
                () => { _view.EnterTowerFloor(floor); Close(); }, 18);
            var img = enter.GetComponent<Image>();
            if (img != null) img.color = new Color(0.26f, 0.42f, 0.62f);
            AnchorBL((RectTransform)enter.transform, new Vector2(22f, 22f));
        }

        private static void AnchorTL(Component c, Vector2 pos)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
        }

        private static void AnchorTR(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = pos;
        }

        private static void AnchorBL(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = pos;
        }
    }
}
