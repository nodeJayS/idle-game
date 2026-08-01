#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The end-of-encounter card (was CombatView.DrawOutcome, IMGUI). A WIN is a bordered green
    /// panel — title + sub + OK — that auto-advances or fast-forwards on OK. A LOSS is a bare,
    /// raycast-transparent banner centered over the world (with an optional run summary): every
    /// graphic is non-interactive so clicks fall through, exactly as the old IMGUI banner let them.
    /// Each builder returns the canvas GameObject (destroy it to dismiss).
    /// </summary>
    public static class OutcomeModal
    {
        public static GameObject ShowWin(Transform parent, string title, string sub, Action onOk)
        {
            var canvas = PanelKit.Modal(parent, "OutcomeCanvas", 88, new Vector2(420f, 180f),
                                        out var body, backdrop: null, border: Theme.OutcomeWinBorder,
                                        panelBg: Theme.OutcomeWinBg);

            var t = PanelKit.Label(body, title, Theme.FsOutcomeTitle, Theme.OutcomeWinTitle, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;

            PanelKit.Label(body, sub, Theme.FsBody, Theme.OutcomeWinSub, TextAnchor.MiddleCenter);

            PanelKit.Flex(body); // slack pushes OK to the card bottom

            var row = PanelKit.Row(body, Theme.BtnHs);
            PanelKit.FlexSpacer(row);
            PanelKit.ButtonCell(row, Loc.T("common.ok"), onOk, width: 160f, fontSize: Theme.FsH1);
            PanelKit.FlexSpacer(row);

            return canvas;
        }

        public static GameObject ShowLoss(Transform parent, string banner, string? runSummary)
        {
            // No panel, no backdrop — a centered full-screen stack over the world. match 0.5 keeps
            // the banner readable on ultrawide (same rationale as PanelKit windows).
            var canvas = UiKit.CreateCanvas("OutcomeCanvas", parent, sortOrder: 88, match: 0.5f);

            var stackGo = new GameObject("OutcomeLoss", typeof(RectTransform));
            stackGo.transform.SetParent(canvas.transform, false);
            var srt = (RectTransform)stackGo.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = srt.offsetMax = Vector2.zero;
            var vlg = stackGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter; // center the banner block on the screen
            vlg.spacing = Theme.GapS;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // EVERY graphic here is non-raycast so the banner never eats a click (parity with IMGUI).
            var b = PanelKit.Label(srt, banner, Theme.FsOutcomeBanner, Color.white, TextAnchor.MiddleCenter);
            b.fontStyle = FontStyle.Bold;
            b.raycastTarget = false;
            if (!string.IsNullOrEmpty(runSummary))
            {
                var rs = PanelKit.Label(srt, runSummary!, Theme.FsBody, Theme.OutcomeLossSummary, TextAnchor.MiddleCenter);
                rs.raycastTarget = false;
            }
            return canvas.gameObject;
        }
    }
}
