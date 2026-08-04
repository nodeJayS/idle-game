#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

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
            // Asymmetric safe-area (10.13c): the dimming backdrop stays FULL-BLEED on the raw canvas so
            // it covers the notch / rounded-corner region — a dim that stopped at the safe edge reads
            // broken. Only the PANEL insets under SafeRoot, so its content clears the notch. WindowSizer
            // then sizes against the SafeRoot rect (its parent) — the wanted inset on a notched device,
            // a provable no-op on desktop where SafeRoot == the canvas rect.
            UiKit.FullScreen(canvas.transform, Theme.Backdrop);
            var safe = UiKit.SafeRoot(canvas);

            // The panel is the one centered rect in the kit — WindowSizer keeps it centred and
            // clamps it to max as the canvas resizes (everything inside is layout-driven).
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(safe, false);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = Theme.BgPanel;
            UiKit.Round(panelImg, Theme.RadiusWindow);
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
            DropShadow(safe, prt, Theme.RadiusWindow);

            var vlg = panelGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)Theme.PadL, (int)Theme.PadL, (int)Theme.PadL, (int)Theme.PadL);
            vlg.spacing = Theme.Gap;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Header: flexible title, fixed Close on the right. 10.9d: the window pops open
            // audibly (built per-open) and the Close verb pops shut — the popup pair of the
            // one UI sound family. Other close paths (control-bar toggles) stay silent.
            SoundFx.Play("System_PopUpOpen_new", 0.35f);
            var header = Row(prt, Theme.BtnH);
            HeaderRule(header);
            TextCell(header, title, Theme.FsTitle, Theme.TextBright, TextAnchor.MiddleLeft, flex: 1f);
            ButtonCell(header, Loc.T("common.close"),
                () => { SoundFx.Play("System_PopUpClose_new", 0.35f); onClose(); },
                width: Theme.CloseW, fontSize: Theme.FsH1);

            body = Flex(prt);
            return canvas.gameObject;
        }

        /// <summary>
        /// A centered dialog — lighter than <see cref="Window"/> (no header/Close row; the caller
        /// owns dismissal). The panel is <paramref name="size"/> on a roomy canvas and shrinks with
        /// margins on a starved one. <paramref name="backdrop"/> null = NO dim at all, so clicks
        /// outside the panel pass through to nothing (an unblocked overlay). <paramref name="border"/>
        /// non-null wraps the panel in a 2px frame of that color. <paramref name="body"/> is the
        /// panel's vertical stack — the caller adds rows. Returns the canvas (destroy it to close).
        /// </summary>
        public static GameObject Modal(Transform parent, string canvasName, int sortOrder,
                                       Vector2 size, out RectTransform body,
                                       Color? backdrop = null, Color? border = null, Color? panelBg = null)
        {
            // match 0.5 like Window: an ultrawide canvas keeps enough HEIGHT that the dialog's rows
            // don't crush to nothing (match-width alone leaves ~540 units at 21:9).
            var canvas = UiKit.CreateCanvas(canvasName, parent, sortOrder, match: 0.5f);
            // Only add a backdrop when asked: a null backdrop leaves clicks outside the panel
            // falling through to the game (no invisible raycast blocker). Asymmetric safe-area (10.13c),
            // mirroring Window: the backdrop dims FULL-BLEED on the raw canvas (a dim that stopped at the
            // notch reads broken), while the panel / border wrapper insets under SafeRoot so its content
            // clears the notch. WindowSizer then sizes against the SafeRoot rect (no-op on desktop).
            if (backdrop != null) UiKit.FullScreen(canvas.transform, backdrop.Value);
            var safe = UiKit.SafeRoot(canvas);

            // Optional border: an outer image carries the sizer; the panel insets 2px inside it so
            // the color reads as a thin frame. Without a border the panel carries the sizer itself.
            Transform panelParent = safe;
            RectTransform? outer = null;
            if (border != null)
            {
                var borderGo = new GameObject("Border", typeof(RectTransform));
                borderGo.transform.SetParent(safe, false);
                var bimg = borderGo.AddComponent<Image>();
                bimg.color = border.Value;
                // +2 radius = the 2px the panel insets, so the frame keeps an even width all the
                // way around the arc instead of pinching at the corners.
                UiKit.Round(bimg, Theme.RadiusWindow + 2);
                var bs = borderGo.AddComponent<WindowSizer>();
                bs.Margin = new Vector2(Theme.Gap * 5f, Theme.PadL);
                bs.Max = size + new Vector2(4f, 4f);
                panelParent = borderGo.transform;
                outer = (RectTransform)borderGo.transform;
            }

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(panelParent, false);
            var pimg = panelGo.AddComponent<Image>();
            pimg.color = panelBg ?? Theme.BgPanel;
            UiKit.Round(pimg, Theme.RadiusWindow);
            panelGo.AddComponent<RectMask2D>(); // crop overflow at the panel edge on a starved canvas
            var prt = (RectTransform)panelGo.transform;

            if (border != null)
            {
                // Stretch to the border, 2px in on every side (the frame shows through).
                prt.anchorMin = Vector2.zero;
                prt.anchorMax = Vector2.one;
                prt.offsetMin = new Vector2(2f, 2f);
                prt.offsetMax = new Vector2(-2f, -2f);
            }
            else
            {
                var sizer = panelGo.AddComponent<WindowSizer>();
                sizer.Margin = new Vector2(Theme.Gap * 5f, Theme.PadL);
                sizer.Max = size;
            }
            // The shadow follows whichever rect the sizer drives — the frame when there is one,
            // else the panel; it must never be the INNER rect or it'd hide under the frame.
            DropShadow(safe, outer ?? prt, Theme.RadiusWindow);

            var vlg = panelGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)Theme.PadL, (int)Theme.PadL, (int)Theme.PadL, (int)Theme.PadL);
            vlg.spacing = Theme.Gap;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            body = prt;
            return canvas.gameObject;
        }

        // ==== Elevation ================================================================

        /// <summary>Lays a soft drop shadow behind <paramref name="target"/>, lifting the window off
        /// the diorama. It is a SIBLING inserted before the target, never a child: the window clips
        /// its children with a RectMask2D (which would eat the fringe) and its layout group would try
        /// to size anything parented inside. Never raycasts, never lays out — hit-testing and reflow
        /// are byte-identical with or without it.</summary>
        private static void DropShadow(Transform parent, RectTransform target, int radius, float grow = 12f)
        {
            var go = new GameObject("Shadow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(target.GetSiblingIndex()); // hierarchy order == draw order
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SoftShadow(radius);
            img.type = Image.Type.Sliced;
            img.color = new Color(0f, 0f, 0f, 0.35f);
            img.raycastTarget = false;
            go.AddComponent<LayoutElement>().ignoreLayout = true; // defensive: SafeRoot has no group today
            var follow = go.AddComponent<ShadowFollow>();
            follow.Target = target;
            follow.Grow = new Vector2(grow, grow);
        }

        /// <summary>A hairline of <see cref="Theme.AccentGold"/> under a window header, parting chrome
        /// from content without spending a whole divider row. Layout-IGNORED and anchored to the row's
        /// bottom edge so it lives in the existing Gap — the header's cell arithmetic (and the 44pt
        /// floor on the Close button) never sees it.</summary>
        private static void HeaderRule(RectTransform header)
        {
            var go = new GameObject("HeaderRule", typeof(RectTransform));
            go.transform.SetParent(header, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = go.AddComponent<Image>();
            img.color = new Color(Theme.AccentGold.r, Theme.AccentGold.g, Theme.AccentGold.b, 0.35f);
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -2f);
            rt.offsetMax = Vector2.zero;
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

        /// <summary>Turn an existing container (e.g. Window's body) into a vertical stack IN PLACE —
        /// the vertical counterpart of <see cref="Columns"/>. Children then flow top-to-bottom;
        /// a <see cref="Flex"/> child absorbs the slack. Returns the same rect.</summary>
        public static RectTransform Stack(RectTransform parent, float spacing = Theme.Gap)
        {
            var vlg = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return parent;
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
            UiKit.Round(img, Theme.RadiusPanel);

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
            // Tighter arc than a standalone button: stacked rows at the button radius read as a pile
            // of lozenges, and the list has to scan as a column.
            UiKit.Round(img, Theme.RadiusTile);

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

        /// <summary>A labelled 0..1 slider row (the Settings volumes): label left, a track+handle filling
        /// the middle, a live % readout right. The row rides the 44 touch floor and the SLIDER HOST fills
        /// that full height as an invisible-but-raycasting drag target — so the grabbable area clears the
        /// floor even though the visible groove stays a slim 16px bar. This is TopBar's hand-built slider
        /// (track/fill/handle with the half-handle inset + CircleSprite handle) converted to layout form:
        /// the widget anchors inside a flexible cell instead of a fixed anchoredPosition. Applies live via
        /// <paramref name="set"/> on every drag and syncs the % label. Returns the Slider.</summary>
        public static Slider SliderRow(RectTransform parent, string label, Func<float> get, Action<float> set)
        {
            var row = Row(parent, Theme.TouchMin);
            TextCell(row, label, Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft, flex: 1f);

            // Slider cell: a plain container the Row sizes (fixed-ish width); the host fills it, so the
            // whole widget reflows with the cell instead of a hand-placed 160px track at a literal offset.
            var cellGo = new GameObject("SliderCell", typeof(RectTransform));
            cellGo.transform.SetParent(row, false);
            var cellLe = cellGo.AddComponent<LayoutElement>();
            cellLe.preferredWidth = 180f; cellLe.minWidth = 140f; cellLe.flexibleWidth = 0f;

            float start = Mathf.Clamp01(get());

            // Host: fills the cell (44 tall), transparent but raycast-on — the Slider lives here, so the
            // DRAG TARGET is the full row height, not the 16px groove. Groove/fill/handle are children.
            var hostGo = new GameObject("Slider", typeof(RectTransform));
            hostGo.transform.SetParent(cellGo.transform, false);
            var hostImg = hostGo.AddComponent<Image>();
            hostImg.color = new Color(0f, 0f, 0f, 0f); // invisible, still raycasts
            var hostRt = (RectTransform)hostGo.transform;
            hostRt.anchorMin = Vector2.zero; hostRt.anchorMax = Vector2.one;
            hostRt.offsetMin = hostRt.offsetMax = Vector2.zero;

            var slider = hostGo.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            // Visible groove: a thin 16px bar centered in the host (the old track look, now decorative).
            var groove = new GameObject("Groove", typeof(RectTransform));
            groove.transform.SetParent(hostGo.transform, false);
            var grooveImg = groove.AddComponent<Image>();
            grooveImg.color = Theme.BarTrack;
            grooveImg.raycastTarget = false;
            // Tile radius, not button: at 16px tall a larger arc's 9-slice borders overrun the
            // groove's own height and the ends pinch.
            UiKit.Round(grooveImg, Theme.RadiusTile);
            var grooveRt = (RectTransform)groove.transform;
            grooveRt.anchorMin = new Vector2(0f, 0.5f); grooveRt.anchorMax = new Vector2(1f, 0.5f);
            grooveRt.offsetMin = new Vector2(0f, -8f); grooveRt.offsetMax = new Vector2(0f, 8f);

            // Half the handle width — the Fill Area and Handle Slide Area inset by this so the round handle
            // stays inside the ends and the fill doesn't overshoot (mirrors Unity's default slider layout).
            const float pad = 13f;

            // Fill: a 16px-tall centered band, padded; the Slider grows its child's anchorMax.x with value.
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(hostGo.transform, false);
            var fillAreaRt = (RectTransform)fillArea.transform;
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f); fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.offsetMin = new Vector2(pad, -8f); fillAreaRt.offsetMax = new Vector2(-pad, 8f);
            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = Theme.BarGold; // the settings sliders are progress fills like any other
            fillImg.raycastTarget = false;
            UiKit.Round(fillImg, Theme.RadiusTile);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = new Vector2(0f, 0f); fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            fillRt.sizeDelta = new Vector2(0f, 0f);

            // Handle: same 16px centered band + pad as the original (the CircleSprite handle overhangs it).
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(hostGo.transform, false);
            var handleAreaRt = (RectTransform)handleArea.transform;
            handleAreaRt.anchorMin = new Vector2(0f, 0.5f); handleAreaRt.anchorMax = new Vector2(1f, 0.5f);
            handleAreaRt.offsetMin = new Vector2(pad, -8f); handleAreaRt.offsetMax = new Vector2(-pad, 8f);
            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(handleArea.transform, false);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.sprite = UiKit.CircleSprite();
            handleImg.color = Theme.TextBright;
            var handleRt = (RectTransform)handleGo.transform;
            handleRt.sizeDelta = new Vector2(26f, 26f);

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f;
            slider.value = start;

            var pct = TextCell(row, Mathf.RoundToInt(start * 100f) + "%", Theme.FsBody, Theme.TextBright,
                               TextAnchor.MiddleLeft, width: 56f);
            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                pct.text = Mathf.RoundToInt(v * 100f) + "%";
            });
            return slider;
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

    /// <summary>Glues a drop-shadow rect to the window it sits behind. A sibling can't inherit the
    /// window's size the way a child would, and the window's own size is only known after
    /// <see cref="WindowSizer"/> runs against the live canvas — so it's copied every frame, grown and
    /// nudged down. LateUpdate, because WindowSizer resizes in Update and a frame of lag would show
    /// as the shadow snapping a beat behind on every canvas resize.</summary>
    public sealed class ShadowFollow : MonoBehaviour
    {
        public RectTransform Target = null!;
        /// <summary>Per-side spread — this much shadow shows beyond the window's edge.</summary>
        public Vector2 Grow = new(12f, 12f);
        /// <summary>Offset from the target; down, so the light reads as coming from above.</summary>
        public Vector2 Offset = new(0f, -3f);

        private void LateUpdate()
        {
            if (Target == null) return;
            var rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Target.rect.size + Grow * 2f;
            rt.anchoredPosition = Target.anchoredPosition + Offset;
        }
    }

    /// <summary>Keeps a centered window sized relative to its canvas but clamped to a max —
    /// the one place the kit uses anchoredPosition (center). Re-runs when the canvas resizes
    /// OR when Margin/Max change.</summary>
    public sealed class WindowSizer : MonoBehaviour
    {
        public Vector2 Margin = new(60f, 36f);
        public Vector2 Max = new(1160f, 680f);
        private Vector2 _lastParent = new(-1f, -1f);
        // Seeded NaN so the first Apply always runs: AddComponent fires OnEnable SYNCHRONOUSLY,
        // before the caller can assign Margin/Max on the next line, so that first pass sizes off
        // the DEFAULTS. Watching only _lastParent then early-outed every later frame (the canvas
        // never resizes on desktop) and the declared size never landed — every window sat at
        // 1160x648 instead of its own max (gacha asked for 560x266 and got the full slab; the
        // Heroes window ran 32px short and clipped its Boot tile + Stats column). NaN != NaN, so
        // these comparisons are false on frame one and the real values apply.
        private Vector2 _lastMargin = new(float.NaN, float.NaN);
        private Vector2 _lastMax = new(float.NaN, float.NaN);

        private void OnEnable() => Apply();
        private void Update() => Apply();

        private void Apply()
        {
            var rt = (RectTransform)transform;
            if (rt.parent is not RectTransform parent) return;
            var ps = parent.rect.size;
            if (ps.x <= 0f || ps.y <= 0f) return;
            // Vector2 == is an approximate compare; with a NaN seed it reports "changed" once.
            if (ps == _lastParent && Margin == _lastMargin && Max == _lastMax) return;
            _lastParent = ps;
            _lastMargin = Margin;
            _lastMax = Max;

            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.Min(ps - Margin * 2f, Max);
        }
    }
}
