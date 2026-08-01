#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The Manage confirm card (10.14b — the 30-second session, mobile arc MM2): a returning player's
    /// mid-session "do the obvious chores" verb. Opened by the <see cref="NavBar"/> Manage button, it
    /// reads a FRESH <see cref="Session.Preview"/> at open and shows one honest line per non-empty
    /// bucket (claim / equip / salvage), then Confirm applies the whole sweep atomically through
    /// <see cref="CombatView.NavManageApply"/> (feed lines land there) and closes.
    ///
    /// Read-only over the save except that one Confirm call. Built on <see cref="PanelKit.Modal"/> — its
    /// body ALREADY carries the vertical layout group, so rows are added directly (never Stack it — the
    /// Window-vs-Modal foot-gun). The dimming backdrop covers the nav, so it also blocks a second Manage
    /// tap while open (no double-open guard needed). The salvage line is NUCLEAR (Session.cs: it eats
    /// every loose, unlocked item regardless of rarity) — the copy states the count/scrap plainly rather
    /// than softening it, because that honesty IS the point of the confirm step.
    /// </summary>
    public sealed class ManageModal : MonoBehaviour
    {
        private CombatView? _view; // set while shown, so OnDestroy releases the HUD gate

        public void Show(CombatView view, GameConfig cfg)
        {
            _view = view;
            view.PushLaunchModal(); // hide the IMGUI HUD (HP-bar dashes) that draws above uGUI

            long now = view.NowMillis;
            var preview = Session.Preview(view.CurrentSave, cfg, now);

            PanelKit.Modal(transform, "ManageCanvas", 90, new Vector2(440f, 320f),
                           out var body, backdrop: Theme.BackdropDim);

            PanelKit.Label(body, Loc.T("manage.title"), Theme.FsH1, Theme.TextBright, TextAnchor.MiddleCenter);

            if (preview.IsEmpty)
            {
                PanelKit.Label(body, Loc.T("manage.nothing"), Theme.FsH2, Theme.TextDim, TextAnchor.MiddleLeft);
                PanelKit.Flex(body);
                var closeRow = PanelKit.Row(body, Theme.BtnH);
                PanelKit.FlexSpacer(closeRow);
                PanelKit.ButtonCell(closeRow, Loc.T("common.close"), () => Destroy(gameObject), width: 200f, fontSize: Theme.FsH1);
                PanelKit.FlexSpacer(closeRow);
                return;
            }

            // One line per non-empty bucket, sourced straight off the preview so the numbers ARE what
            // Confirm applies (Preview predicts Apply exactly — locked by a GameCore test).
            if (preview.Claims.Count > 0)
            {
                long gems = 0; foreach (var c in preview.Claims) gems += c.Gems;
                PanelKit.Label(body, Loc.F("manage.claim", preview.Claims.Count, Plural(preview.Claims.Count), Num.CompactFloor(gems)),
                               Theme.FsH2, Theme.AccentGold, TextAnchor.MiddleLeft);
            }
            if (preview.EquipCount > 0)
                PanelKit.Label(body, Loc.F("manage.equip", preview.EquipCount, Plural(preview.EquipCount)),
                               Theme.FsH2, UpgradeTell.Up, TextAnchor.MiddleLeft);
            if (preview.SalvageCount > 0)
                PanelKit.Label(body, Loc.F("manage.salvage", preview.SalvageCount, Plural(preview.SalvageCount), Num.CompactFloor(preview.SalvageScrap)),
                               Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft);

            PanelKit.Flex(body); // slack pushes the buttons to the panel bottom

            var row = PanelKit.Row(body, Theme.BtnH);
            PanelKit.ButtonCell(row, Loc.T("common.cancel"), () => Destroy(gameObject), fontSize: Theme.FsH1);
            PanelKit.ButtonCell(row, Loc.T("common.confirm"), () => { _view!.NavManageApply(now); Destroy(gameObject); }, fontSize: Theme.FsH1);
        }

        private static string Plural(int n) => n == 1 ? "" : "s";

        private void OnDestroy() => _view?.PopLaunchModal();
    }
}
