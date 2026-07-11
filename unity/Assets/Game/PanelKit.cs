#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace IdleGame.Game
{
    /// <summary>
    /// Layout-group builders that LAYER on <see cref="UiKit"/> — every rect they create is
    /// positioned by anchors + Unity layout groups, never by anchoredPosition (the only
    /// exception is the window itself, centered by <see cref="WindowSizer"/>). Screens build
    /// their tree from Window → Columns → Section/Row/Cell instead of hand-placing pixels,
    /// so a layout change reflows automatically. Colors/sizes come from <see cref="Theme"/>.
    /// </summary>
    public static class PanelKit
    {
        // ==== Window ==================================================================

        /// <summary>
        /// A full-screen management view: opaque backdrop + a centered panel that sizes
        /// relative to the canvas (with margins) but never exceeds <paramref name="max"/>.
        /// The panel is a vertical stack of a header row (flexible title + fixed Close) and
        /// <paramref name="body"/> — the remaining space, which the caller fills. Returns the
        /// canvas GameObject (destroy it to close).
        /// </summary>
        public static GameObject Window(Transform parent, string title, Action onClose,
                                        out RectTransform body, string canvasName, int sortOrder,
                                        Vector2? max = null)
        {
            // match 0.5: scale by both axes so ultrawide screens keep enough canvas HEIGHT for the
            // window — match-width alone leaves ~540 units at 21:9 and the rows crush to nothing.
            var canvas = UiKit.CreateCanvas(canvasName, parent, sortOrder, match: 0.5f);
            UiKit.FullScreen(canvas.transform, Theme.Backdrop);

            // The panel is the one centered rect in the kit — WindowSizer keeps it centred and
            // clamps it to max as the canvas resizes (everything inside is layout-driven).
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(canvas.transform, false);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = Theme.BgPanel;
            // Clip children at the panel edge: on a height-starved canvas (extreme aspect) any
            // residual overflow reads as an intentional crop, never bleeds into the world.
            panelGo.AddComponent<RectMask2D>();
            var prt = (RectTransform)panelGo.transform;
            var sizer = panelGo.AddComponent<WindowSizer>();
            // Slim vertical margin: at 720-unit reference height this yields the pre-kit 680px
            // window; at ultrawide (match-0.5 canvas ≈ 624 units) it leaves enough room that the
            // Equipment tab's minimum heights still fit inside the panel instead of overflowing.
            sizer.Margin = new Vector2(Theme.Gap * 5f, Theme.PadL);
            sizer.Max = max ?? new Vector2(1160f, 680f);

            var vlg = panelGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)Theme.PadL, (int)Theme.PadL, (int)Theme.PadL, (int)Theme.PadL);
            vlg.spacing = Theme.Gap;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Header: flexible title, fixed Close on the right.
            var header = Row(prt, Theme.BtnH);
            TextCell(header, title, Theme.FsTitle, Theme.TextBright, TextAnchor.MiddleLeft, flex: 1f);
            ButtonCell(header, "Close", onClose, width: Theme.CloseW, fontSize: Theme.FsH1);

            body = Flex(prt);
            return canvas.gameObject;
        }

        // ==== Structure ================================================================

        /// <summary>Splits <paramref name="parent"/> horizontally. Each column: weight 0 +
        /// minWidth&gt;0 = fixed width; weight&gt;0 = flexible (share = weight). Returns a
        /// VerticalLayoutGroup container per column.</summary>
        public static RectTransform[] Columns(RectTransform parent, params (float weight, float minWidth)[] cols)
        {
            var hlg = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.Gap;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            var result = new RectTransform[cols.Length];
            for (int i = 0; i < cols.Length; i++)
            {
                var col = VStack(parent, Theme.Gap);
                var le = col.gameObject.AddComponent<LayoutElement>();
                if (cols[i].weight <= 0f) { le.preferredWidth = cols[i].minWidth; le.flexibleWidth = 0f; }
                else { le.flexibleWidth = cols[i].weight; le.minWidth = cols[i].minWidth; }
                result[i] = col;
            }
            return result;
        }

        /// <summary>An inset box (Theme.BgInset) with an optional muted header label. Returns the
        /// content container (VerticalLayoutGroup) — the caller adds rows. To make it fill the
        /// remaining column height, set <c>LayoutElement.flexibleHeight = 1</c> on the return.</summary>
        public static RectTransform Section(RectTransform parent, string? header)
        {
            var go = new GameObject("Section", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = Theme.BgInset;

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)Theme.GapS, (int)Theme.GapS, (int)Theme.GapS, (int)Theme.GapS);
            vlg.spacing = Theme.GapXs;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var rt = (RectTransform)go.transform;
            if (!string.IsNullOrEmpty(header))
            {
                var lbl = Label(rt, header!, Theme.FsLabel, Theme.TextMuted, TextAnchor.MiddleLeft);
                Fixed(lbl.gameObject, height: 20f);
            }
            return rt;
        }

        /// <summary>A horizontal row (HorizontalLayoutGroup) of a fixed height. Fill it with the
        /// Cell helpers.</summary>
        public static RectTransform Row(RectTransform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.GapS;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var rt = (RectTransform)go.transform;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height; // under pressure the pane's scroll/fill shrinks, never the rows
            le.flexibleWidth = 1f;
            // Explicit 0 required: with childForceExpandHeight on, the HLG itself REPORTS
            // flexibleHeight=1 to the parent stack, so every row would balloon and steal the
            // slack meant for the pane's fill element. LayoutElement's 0 overrides that.
            le.flexibleHeight = 0f;
            return rt;
        }

        /// <summary>A plain VerticalLayoutGroup container for free-form stacks.</summary>
        public static RectTransform VStack(RectTransform parent, float spacing, RectOffset? padding = null)
        {
            var go = new GameObject("VStack", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.padding = padding ?? new RectOffset(0, 0, 0, 0);
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        /// <summary>An empty stretch container that fills its layout cell (flexible both axes).
        /// The caller adds a layout group (Columns/VStack) or children to it.</summary>
        public static RectTransform Flex(RectTransform parent, float w = 1f, float h = 1f)
        {
            var go = new GameObject("Flex", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = w;
            le.flexibleHeight = h;
            return (RectTransform)go.transform;
        }

        // ==== Cells ====================================================================

        /// <summary>A text cell. <paramref name="width"/>&gt;0 = fixed width; otherwise flexible
        /// (share = <paramref name="flex"/>).</summary>
        public static Text TextCell(RectTransform row, string text, int fontSize, Color color,
                                    TextAnchor anchor = TextAnchor.MiddleLeft, float flex = 1f, float width = 0f)
        {
            var lbl = Label(row, text, fontSize, color, anchor);
            var le = lbl.gameObject.AddComponent<LayoutElement>();
            if (width > 0f) { le.preferredWidth = width; le.flexibleWidth = 0f; }
            else le.flexibleWidth = flex;
            return lbl;
        }

        /// <summary>A button cell. Disabled = interactable off + Theme.BtnDisabled (the old
        /// ActionButton idiom). <paramref name="width"/>&gt;0 = fixed; else flexible.</summary>
        public static Button ButtonCell(RectTransform row, string label, Action onClick, float width = 0f,
                                        int fontSize = Theme.FsBody, bool enabled = true)
        {
            var btn = UiKit.TextButton(row, label, Vector2.zero, Vector2.zero, enabled ? onClick : () => { }, fontSize);
            StretchLabel(btn, TextAnchor.MiddleCenter);
            var img = btn.GetComponent<Image>();
            if (!enabled) { btn.interactable = false; img.color = Theme.BtnDisabled; }
            var le = btn.gameObject.AddComponent<LayoutElement>();
            if (width > 0f) { le.preferredWidth = width; le.flexibleWidth = 0f; }
            else le.flexibleWidth = 1f;
            return btn;
        }

        /// <summary>An expanding gap that pushes later cells to the far edge of a row.</summary>
        public static RectTransform FlexSpacer(RectTransform row)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(row, false);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return (RectTransform)go.transform;
        }

        // ==== Composite rows ===========================================================

        /// <summary>A full-width selector button (party slot / roster entry). Selected =
        /// Theme.BtnSelected, muted = Theme.RowMuted, otherwise Theme.BtnPrimary.</summary>
        public static Button ListRow(RectTransform parent, string label, Action onClick,
                                     bool selected = false, bool muted = false, int fontSize = Theme.FsLabel,
                                     float height = Theme.RowH, bool enabled = true)
        {
            var btn = UiKit.TextButton(parent, label, Vector2.zero, Vector2.zero, enabled ? onClick : () => { }, fontSize);
            // Left-align the label like the roster rows read (TextButton centers by default).
            StretchLabel(btn, TextAnchor.MiddleLeft);

            var img = btn.GetComponent<Image>();
            img.color = selected ? Theme.BtnSelected : muted ? Theme.RowMuted : Theme.BtnPrimary;
            if (!enabled) { btn.interactable = false; img.color = Theme.BtnDisabled; }

            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
            return btn;
        }

        /// <summary>A row of equal-width tab buttons; the active one is selected-colored.</summary>
        public static RectTransform TabBar(RectTransform parent, string[] labels, int active, Action<int> onPick)
        {
            var row = Row(parent, Theme.BtnHs);
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var btn = UiKit.TextButton(row, labels[i], Vector2.zero, Vector2.zero, () => onPick(idx), Theme.FsSubTab);
                StretchLabel(btn, TextAnchor.MiddleCenter);
                if (i == active) btn.GetComponent<Image>().color = Theme.BtnSelected;
                btn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
            return row;
        }

        /// <summary>A stat-sheet row: label left, value right. <paramref name="color"/> tints both.</summary>
        public static RectTransform KeyValueRow(RectTransform parent, string label, string value, Color? color = null)
        {
            var row = Row(parent, 22f);
            var c = color ?? Theme.TextMuted;
            TextCell(row, label, Theme.FsSmall, c, TextAnchor.MiddleLeft, flex: 1f);
            TextCell(row, value, Theme.FsSmall, c, TextAnchor.MiddleRight, flex: 1f);
            return row;
        }

        // ==== Low-level ================================================================

        /// <summary>A layout-friendly Label: created at zero size (the layout group sizes it),
        /// colored, with alignment. Wrapping stays on so cell text can't overflow its box.</summary>
        public static Text Label(RectTransform parent, string text, int fontSize, Color color, TextAnchor anchor)
        {
            var lbl = UiKit.Label(parent, text, fontSize, anchor, Vector2.zero, Vector2.zero);
            lbl.color = color;
            return lbl;
        }

        /// <summary>Stretch a <see cref="UiKit.TextButton"/>'s inner label to fill the button (the
        /// kit builds buttons at zero size and lets layout size them, so the label — anchored to
        /// its own fixed size by TextButton — must be re-anchored to follow).</summary>
        private static void StretchLabel(Button btn, TextAnchor anchor)
        {
            var txt = btn.GetComponentInChildren<Text>();
            if (txt == null) return;
            txt.alignment = anchor;
            var rt = (RectTransform)txt.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 2f);
            rt.offsetMax = new Vector2(-6f, -2f);
        }

        /// <summary>Pin a fixed size on a layout child (0 = leave that axis flexible). Sets min as
        /// well as preferred so a height-starved pane crushes its flexible fills, not its labels.</summary>
        public static LayoutElement Fixed(GameObject go, float width = 0f, float height = 0f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (width > 0f) { le.preferredWidth = width; le.minWidth = width; }
            if (height > 0f) { le.preferredHeight = height; le.minHeight = height; }
            return le;
        }
    }

    /// <summary>Keeps a centered window sized relative to its canvas but clamped to a max —
    /// the one place the kit uses anchoredPosition (center). Re-runs when the canvas resizes.</summary>
    public sealed class WindowSizer : MonoBehaviour
    {
        public Vector2 Margin = new(60f, 36f);
        public Vector2 Max = new(1160f, 680f);
        private Vector2 _lastParent = new(-1f, -1f);

        private void OnEnable() => Apply();
        private void Update() => Apply();

        private void Apply()
        {
            var rt = (RectTransform)transform;
            if (rt.parent is not RectTransform parent) return;
            var ps = parent.rect.size;
            if (ps.x <= 0f || ps.y <= 0f || ps == _lastParent) return;
            _lastParent = ps;

            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.Min(ps - Margin * 2f, Max);
        }
    }
}
