#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Left-side chat/activity panel. Only the "System" tab ships pre-release — the live
    /// loot/XP/event log fed by CombatView. The social tabs (Global, Friends, Guild) and
    /// per-person Whispers (DMs) are intentionally hidden until the online service lands
    /// (Phase C), so players aren't shown dead features; re-add them to <see cref="Tabs"/>
    /// to bring the bar back. Drag by the title bar, resize from the bottom-right grip,
    /// minimize, or lock. Position/size/state persist via <see cref="Settings"/>. Read-only.
    /// </summary>
    public sealed class ChatPanel : MonoBehaviour
    {
        private const string SystemTab = "System";
        // Pre-release: System only. When the server ships, add the social tabs back here —
        // "Global", "Friends", "Guild" — plus per-person Whispers (DM threads, opened on demand).
        private static readonly string[] Tabs = { SystemTab };
        private const int MaxFeed = 60;

        private const float HeaderH = 28f;
        private const float TabH = 26f;
        private static bool ShowTabs => Tabs.Length > 1;     // single tab pre-release -> hide the bar
        // a11y text scale (10.20a): chrome metrics ride the scale so labels never outgrow their
        // rects (uGUI Text TRUNCATES vertically — an unscaled header vanishes its title at 130%).
        // Stamped once per Build: the window's labels get their fontSize at build time too, so
        // metrics and text always agree; a mid-session change lands on the next rebuild.
        private float _ts = 1f;
        private float TabsTop => HeaderH * _ts + 6f;         // tabs sit just under the header
        private float ContentTop => HeaderH * _ts + 6f + (ShowTabs ? TabH * _ts + 6f : 0f);
        private static readonly Vector2 MinSize = new(210f, 110f);
        private static readonly Vector2 MaxSize = new(440f, 420f);

        /// <summary>Feed slide duration (10.23). Sits just above the kit's 130ms panel entrance:
        /// a line arrives while the player is looking somewhere else entirely — at the fight — so
        /// it needs to be catchable in peripheral vision without ever becoming a thing to wait on.</summary>
        private const float FeedSlideMs = 150f;

        private float _slideFrom;   // normalized scroll position the slide starts from (0 = pinned bottom)
        private float _slideMs;
        private bool _sliding;

        private readonly List<(string text, Color color)> _feed = new();
        private readonly Dictionary<string, Button> _tabButtons = new();

        private Canvas _canvas = null!;
        private string _active = SystemTab;
        private bool _collapsed;
        private bool _locked;
        private Vector2 _pos;   // top-left anchored position (canvas left edge, vertical-centre origin)
        private Vector2 _size;
        private RectTransform _body = null!;
        // The feed is ONE rich-text label (colors via <color> tags) that is the scroll content,
        // stretch-anchored to the viewport width. No per-row layout group — that was mis-sizing
        // rows and pushing their left edge off the panel, clipping the start of every line.
        private Text? _feedText;
        private ScrollRect? _feedScroll;

        public void Open()
        {
            _canvas = UiKit.CreateCanvas("ChatCanvas", transform, sortOrder: 84);
            _pos = new Vector2(Settings.ChatX, Settings.ChatY);
            _size = new Vector2(Settings.ChatW, Settings.ChatH);
            // Clamp any persisted size to the (now smaller) bounds so an old large window shrinks.
            _size.x = Mathf.Clamp(_size.x, MinSize.x, MaxSize.x);
            _size.y = Mathf.Clamp(_size.y, MinSize.y, MaxSize.y);
            _collapsed = Settings.ChatCollapsed;
            _locked = Settings.ChatLocked;
            Build();
        }

        /// <summary>Append a line to the activity feed (newest at the bottom).</summary>
        public void AddFeed(string text, Color color)
        {
            _feed.Add((text, color));
            if (_feed.Count > MaxFeed) _feed.RemoveAt(0);
            // slide: true only HERE. A rebuild or a tab switch re-renders the same feed and must
            // land settled — animating those would replay the arrival of a line the player already
            // read (the same build-is-not-an-open rule PanelKit learned in 10.23 P3).
            if (!_collapsed && _active == SystemTab && _feedText != null) RefreshFeed(slide: true);
        }

        // ---- window ----

        private void Build()
        {
            ClearCanvas();
            _feedText = null; _feedScroll = null;

            _ts = Settings.TextScale;
            // Display rect = persisted 100%-space size × the text scale (display-only; mirrors
            // QuestPanel — see its Build for the invariant note).
            float h = (_collapsed ? HeaderH : _size.y) * _ts;
            // Corner-anchored HUD: build under the canvas's SafeRoot so it insets from device notches.
            // On desktop SafeRoot == the canvas rect, so position/drag/clamp behave byte-identically.
            var panel = UiKit.Panel(UiKit.SafeRoot(_canvas), new Vector2(_size.x * _ts, h), Theme.BgHudPanel);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 1f);            // anchor by the top-left corner
            // Display-only heal: _pos (the SAVED rect) stays the source of truth — persisting a
            // clamp taken against a stale or transient canvas size overwrites the user's layout
            // (Play-caught). KeepOnCanvas re-derives the display rect from _pos/_size on first
            // layout and on every canvas resize; only drags persist (MakeDraggable below).
            // The NavBar owns the bottom HudBarH band on its own canvas, which this clamp cannot
            // see: left to itself Chat clamps flush to the canvas floor and disappears under the
            // bar on a short canvas (2340x1080 leaves ~591 units of height, not 720).
            prt.anchoredPosition = UiKit.ClampToCanvas(_pos, prt, _canvas, Theme.HudBarH);
            var keep = panel.gameObject.AddComponent<KeepOnCanvas>();
            keep.Canvas = _canvas;
            keep.BottomInset = Theme.HudBarH;
            keep.DesiredPos = () => _pos;
            keep.DesiredSize = () => new Vector2(_size.x * _ts, (_collapsed ? HeaderH : _size.y) * _ts);

            BuildHeader(panel.transform, prt);

            if (!_collapsed)
            {
                if (ShowTabs) BuildTabs(panel.transform);
                BuildBodyContainer(panel.transform);
                BuildBody();
                RefreshTabHighlight();
                if (!_locked) BuildResizeGrip(panel.transform, prt);
            }
        }

        private void BuildHeader(Transform panel, RectTransform panelRt)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var hrt = (RectTransform)go.transform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(0f, HeaderH * _ts);
            hrt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = _locked ? Theme.BgHudHeaderLocked : Theme.BgHudHeader;
            if (!_locked) UiKit.MakeDraggable(go, panelRt, _canvas, p => { _pos = p; Settings.ChatX = p.x; Settings.ChatY = p.y; }, bottomInset: Theme.HudBarH);

            // Title pinned to the left edge.
            var title = UiKit.Label(go.transform, Loc.T("chat.title"), 15, TextAnchor.MiddleLeft, new Vector2(120f, 22f * _ts), Vector2.zero);
            Anchor((RectTransform)title.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f));

            // Lock + minimize pinned to the right edge.
            // Chip rects + the lock chip's offset ride the scale (a fixed 46-wide chip truncates
            // "Locked" to "Lock" at 130% — Play-caught 10.20a).
            var min = UiKit.TextButton(go.transform, _collapsed ? "+" : "—", new Vector2(24f * _ts, 20f * _ts), Vector2.zero, ToggleCollapse, 16);
            Anchor((RectTransform)min.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f));

            var lockBtn = UiKit.TextButton(go.transform, _locked ? Loc.T("common.locked") : Loc.T("common.free"), new Vector2(46f * _ts, 20f * _ts), Vector2.zero, ToggleLock, 12);
            Anchor((RectTransform)lockBtn.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(10f + 24f * _ts), 0f));
            var li = lockBtn.GetComponent<Image>();
            if (li != null) li.color = _locked ? new Color(0.55f, 0.32f, 0.30f) : new Color(0.22f, 0.30f, 0.45f);
        }

        private void BuildResizeGrip(Transform panel, RectTransform panelRt)
        {
            var go = new GameObject("ResizeGrip", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(22f, 22f);
            rt.anchoredPosition = new Vector2(-2f, 2f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.5f, 0.55f, 0.65f, 0.55f);
            // Drag operates on the DISPLAY rect (100%-space × _ts) — divide the scale back out so
            // prefs stay in 100%-space and a later scale change can't compound into the layout.
            UiKit.MakeResizable(go, panelRt, _canvas, MinSize * _ts, MaxSize * _ts, s =>
            {
                _size = s / _ts;
                Settings.ChatW = _size.x; Settings.ChatH = _size.y;
                LayoutBody();
            });
        }

        private void ToggleCollapse()
        {
            _collapsed = !_collapsed;
            Settings.ChatCollapsed = _collapsed;
            Build();
        }

        private void ToggleLock()
        {
            _locked = !_locked;
            Settings.ChatLocked = _locked;
            Build();
        }

        // ---- tabs / body ----

        private void BuildTabs(Transform panel)
        {
            _tabButtons.Clear();
            float x = 10f;
            foreach (var t in Tabs)
            {
                var tab = t;
                var b = UiKit.TextButton(panel, t, new Vector2(72f, TabH), Vector2.zero, () => SwitchTab(tab), 13);
                Anchor((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -TabsTop));
                _tabButtons[t] = b;
                x += 82f;
            }
        }

        private void BuildBodyContainer(Transform panel)
        {
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(panel, false);
            _body = (RectTransform)bodyGo.transform;
            _body.anchorMin = new Vector2(0f, 0f);
            _body.anchorMax = new Vector2(1f, 1f);
            _body.offsetMin = new Vector2(6f, 8f);
            _body.offsetMax = new Vector2(-6f, -ContentTop);
        }

        /// <summary>Re-stretch the body to the current panel size (called live while resizing).</summary>
        private void LayoutBody()
        {
            // Body is anchored stretch, so its rect follows the panel automatically; nothing to
            // recompute. Kept as a seam in case fixed-size children need re-flowing later.
        }

        private void SwitchTab(string tab)
        {
            _active = tab;
            BuildBody();
            RefreshTabHighlight();
        }

        private void BuildBody()
        {
            for (int i = _body.childCount - 1; i >= 0; i--) Destroy(_body.GetChild(i).gameObject);
            _feedText = null; _feedScroll = null;

            if (_active == SystemTab)
            {
                BuildFeedScroll(_body);
                RefreshFeed();
            }
            else
            {
                var l = UiKit.Label(_body, Loc.T("chat.coming-soon"),
                                    13, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
                var lrt = (RectTransform)l.transform;
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            }
        }

        /// <summary>A vertical scroll whose content is a single wrapping rich-text label (the
        /// whole feed). Because the label IS the content — stretch-anchored to the viewport width,
        /// not a layout-group child — its left edge is pinned to the column and never clips.</summary>
        private void BuildFeedScroll(Transform parent)
        {
            var go = new GameObject("Feed", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var vp = (RectTransform)go.transform;
            vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one;
            vp.offsetMin = vp.offsetMax = Vector2.zero;
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            go.AddComponent<RectMask2D>();

            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 18f;
            scroll.viewport = vp;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(go.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); // stretch wide, top-pinned
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(8f, 0f); crt.offsetMax = new Vector2(-8f, 0f); // side padding
            crt.anchoredPosition = Vector2.zero;

            var text = content.AddComponent<Text>();
            text.font = UiKit.Font;
            text.fontSize = UiKit.Scaled(14); // a11y text scale — the feed body is a hand-built Text, not a UiKit.Label
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.color = Color.white;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = crt;
            _feedScroll = scroll;
            _feedText = text;
        }

        /// <summary>Rebuild the feed label from the line buffer (one rich-text string) and pin the
        /// view to the newest line at the bottom.</summary>
        private void RefreshFeed(bool slide = false)
        {
            if (_feedText == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var (text, color) in _feed)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append('>').Append(text).Append("</color>");
            }
            _feedText.text = sb.ToString();

            Canvas.ForceUpdateCanvases();
            if (_feedScroll == null) return;
            _feedScroll.verticalNormalizedPosition = 0f; // scroll to newest
            _sliding = false;

            if (!slide || Settings.ReducedMotion) return;

            // The feed is ONE rich-text label, so there is no per-row transform to animate — the
            // line that just arrived is characters in the middle of a string. The scroll POSITION
            // is the handle that exists: start one line back and ease to the pinned bottom, and the
            // new line rises in from below while the older ones slide up. That is the motion a chat
            // log actually has, and it costs no restructuring of a label whose single-content layout
            // is load-bearing (per-row children clipped their own left edge, see BuildFeedScroll).
            var viewport = (RectTransform)_feedScroll.viewport;
            float range = _feedScroll.content.rect.height - viewport.rect.height;
            // Nothing to scroll yet: until the feed outgrows its viewport the content is pinned and
            // normalized position has no range to travel. That is also while the feed is nearly
            // empty, where a line appearing into open space barely reads as a pop at all.
            if (range <= 1f) return;

            float lineH = _feedText.fontSize * 1.25f;   // nominal, not measured: this is motion, not layout
            _slideFrom = Mathf.Clamp01(lineH / range);
            _slideMs = 0f;
            _sliding = true;
            _feedScroll.velocity = Vector2.zero;         // a live flick would fight the tween for its whole length
            _feedScroll.verticalNormalizedPosition = _slideFrom;
        }

        /// <summary>Drives the feed's slide and nothing else — a settled panel returns on the first
        /// line. UNSCALED like the rest of the kit's motion: the feed fills up fastest during combat,
        /// which is exactly when hit-stop and the 2× alt-mode speed are messing with Time.deltaTime.</summary>
        private void Update()
        {
            if (!_sliding) return;
            if (_feedScroll == null) { _sliding = false; return; }

            _slideMs += Time.unscaledDeltaTime * 1000f;
            float t = Mathf.Clamp01(_slideMs / FeedSlideMs);
            _feedScroll.verticalNormalizedPosition = Mathf.Lerp(_slideFrom, 0f, UiMotion.EaseOut(t));
            if (t >= 1f) _sliding = false;
        }

        private void RefreshTabHighlight()
        {
            foreach (var kv in _tabButtons)
            {
                var img = kv.Value.GetComponent<Image>();
                if (img != null) img.color = kv.Key == _active ? new Color(0.30f, 0.45f, 0.65f) : new Color(0.22f, 0.30f, 0.45f);
            }
        }

        private void ClearCanvas()
        {
            // Clear the SafeRoot's children (the window), NOT the canvas's — the SafeRoot must survive
            // a rebuild. Destroy is deferred to end-of-frame, so nuking the canvas here would mark the
            // SafeRoot for destruction and the very next SafeRoot() would re-find that doomed object,
            // parenting the fresh window under it only to die with it. SafeRoot() re-creates it lazily.
            var root = UiKit.SafeRoot(_canvas);
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
        }
    }
}
