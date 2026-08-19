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

        /// <summary>The player-facing close (toggle, the header X, or entering a floor): the window
        /// eases out and then destroys itself. Tower has no redraw path — it rebuilds per open.</summary>
        public void Close()
        {
            if (_canvas != null) UiMotion.Dismiss(_canvas.gameObject, animate: true);
            _canvas = null;
        }

        private void Build()
        {
            // PanelKit.Window: standard header (title + Close) + a layout-driven body. Close routes
            // through the same Close() the toggle/Modes callers use, so the open/close contract holds.
            var winGo = PanelKit.Window(transform, "Tower of Ascension", Close, out var body,
                                        "TowerCanvas", sortOrder: 90, max: new Vector2(560f, 420f));
            _canvas = winGo.GetComponent<Canvas>();
            var save = _view.CurrentSave;

            int highest = Tower.HighestFloor(save);
            int max = Tower.MaxFloor(_cfg);
            int next = Tower.NextFloor(save);
            bool complete = Tower.IsComplete(save, _cfg);
            double buffPct = Tower.AccountBuffPct(save, _cfg) * 100;

            PanelKit.Stack(body);

            // Climbed line (rich text) then the two-line ascension-buff line.
            PanelKit.Label(body, $"Highest floor cleared: <b>{highest}</b> / {max}", 16,
                new Color(0.85f, 0.89f, 0.95f), TextAnchor.UpperLeft);

            string buffLine = buffPct > 0
                ? $"Ascension buff: <color=#ffd766>+{buffPct:0}%</color> Hp / Atk / Def (account-wide)"
                : "Ascension buff: none yet";
            int nextMilestone = (Tower.MilestonesCleared(save, _cfg) + 1) * _cfg.Balance.TowerMilestoneEvery;
            PanelKit.Label(body,
                $"{buffLine}\nNext milestone at floor {Mathf.Min(nextMilestone, max)} (+{_cfg.Balance.TowerMilestoneStatPct * 100:0}% more)",
                14, new Color(0.80f, 0.84f, 0.90f), TextAnchor.UpperLeft);

            if (complete)
            {
                PanelKit.Flex(body);
                PanelKit.Label(body, "Tower conquered — every floor cleared!", 17,
                    new Color(0.6f, 0.95f, 0.65f), TextAnchor.MiddleCenter);
                PanelKit.Flex(body);
                return;
            }

            // Next-floor preview: difficulty, modifier, and whether it's a milestone (guardian) floor.
            bool milestone = next % _cfg.Balance.TowerMilestoneEvery == 0;
            string? modId = _cfg.TowerModifierForFloor(next);
            string modName = modId != null && _cfg.Modifiers.TryGetValue(modId, out var md) ? md.Name : "none";
            string preview = $"<b>Floor {next}</b>   ·   modifier: {modName}"
                           + (milestone ? "   ·   <color=#ffd766>guardian + buff</color>" : "");
            PanelKit.Label(body, preview, 15, new Color(0.86f, 0.80f, 0.72f), TextAnchor.UpperLeft);

            // Next-floor reward preview, one muted line: the flat gem drip, the one-time gold bundle
            // (Tower.GoldBundle — the same formula RecordClear banks, at the floor's difficulty-equivalent
            // stage), a boss loot bundle (MAJOR on guardian/milestone floors), plus the milestone account
            // buff and any rare-mod pair unlock when they apply. String logic kept verbatim (shipped slice 3).
            string reward = $"Clear: +{_cfg.Balance.TowerGemsPerFloor} gems · ~{Num.CompactFloor(Tower.GoldBundle(next, _cfg))} gold · "
                          + (milestone ? "major boss bundle" : "boss loot bundle");
            if (milestone) reward += " · account buff";
            bool unlocksMod = false;
            foreach (var kv in _cfg.Modifiers)
                if (kv.Value.TowerUnlockFloor == next) { unlocksMod = true; break; }
            if (unlocksMod) reward += " · unlocks rare modifier pair";
            PanelKit.Label(body, reward, 13, new Color(0.72f, 0.82f, 0.66f), TextAnchor.UpperLeft);

            PanelKit.Label(body,
                "One attempt per floor — no farm income here. Beat it to keep the floor; fail and train up to retry.",
                12, new Color(0.66f, 0.70f, 0.78f), TextAnchor.UpperLeft);

            PanelKit.Flex(body); // push Enter to the panel bottom

            int floor = next;
            var enterRow = PanelKit.Row(body, 56f); // 56 tall, over the 48 primary-verb floor
            var enter = PanelKit.ButtonCell(enterRow, $"Enter Floor {floor}",
                () => { _view.EnterTowerFloor(floor); Close(); }, width: 260f, fontSize: 18);
            var img = enter.GetComponent<Image>();
            if (img != null) img.color = new Color(0.26f, 0.42f, 0.62f);
        }
    }
}
