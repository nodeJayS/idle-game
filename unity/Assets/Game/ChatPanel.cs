#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
        private static float TabsTop => HeaderH + 6f;        // tabs sit just under the header
        private static float ContentTop => HeaderH + 6f + (ShowTabs ? TabH + 6f : 0f);
        private static readonly Vector2 MinSize = new(210f, 110f);
        private static readonly Vector2 MaxSize = new(440f, 420f);

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
            if (!_collapsed && _active == SystemTab && _feedText != null) RefreshFeed();
        }

        // ---- window ----

        private void Build()
        {
            ClearCanvas();
            _feedText = null; _feedScroll = null;

            float h = _collapsed ? HeaderH : _size.y;
            var panel = UiKit.Panel(_canvas.transform, new Vector2(_size.x, h), new Color(0.08f, 0.08f, 0.11f, 0.92f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 1f);            // anchor by the top-left corner
            // Display-only heal: _pos (the SAVED rect) stays the source of truth — persisting a
            // clamp taken against a stale or transient canvas size overwrites the user's layout
            // (Play-caught). KeepOnCanvas re-derives the display rect from _pos/_size on first
            // layout and on every canvas resize; only drags persist (MakeDraggable below).
            prt.anchoredPosition = UiKit.ClampToCanvas(_pos, prt, _canvas);
            var keep = panel.gameObject.AddComponent<KeepOnCanvas>();
            keep.Canvas = _canvas;
            keep.DesiredPos = () => _pos;
            keep.DesiredSize = () => new Vector2(_size.x, _collapsed ? HeaderH : _size.y);

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
            hrt.sizeDelta = new Vector2(0f, HeaderH);
            hrt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = _locked ? new Color(0.20f, 0.17f, 0.17f, 0.96f) : new Color(0.16f, 0.18f, 0.24f, 0.96f);
            if (!_locked) UiKit.MakeDraggable(go, panelRt, _canvas, p => { _pos = p; Settings.ChatX = p.x; Settings.ChatY = p.y; });

            // Title pinned to the left edge.
            var title = UiKit.Label(go.transform, "Chat", 15, TextAnchor.MiddleLeft, new Vector2(120f, 22f), Vector2.zero);
            Anchor((RectTransform)title.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f));

            // Lock + minimize pinned to the right edge.
            var min = UiKit.TextButton(go.transform, _collapsed ? "+" : "—", new Vector2(24f, 20f), Vector2.zero, ToggleCollapse, 16);
            Anchor((RectTransform)min.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f));

            var lockBtn = UiKit.TextButton(go.transform, _locked ? "Locked" : "Free", new Vector2(46f, 20f), Vector2.zero, ToggleLock, 12);
            Anchor((RectTransform)lockBtn.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-34f, 0f));
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
            UiKit.MakeResizable(go, panelRt, _canvas, MinSize, MaxSize, s =>
            {
                _size = s;
                Settings.ChatW = s.x; Settings.ChatH = s.y;
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
                var l = UiKit.Label(_body, "Coming soon — chat arrives with the online update.",
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
            text.fontSize = 14;
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
        private void RefreshFeed()
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
            if (_feedScroll != null) _feedScroll.verticalNormalizedPosition = 0f; // scroll to newest
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
            for (int i = _canvas.transform.childCount - 1; i >= 0; i--)
                Destroy(_canvas.transform.GetChild(i).gameObject);
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
        }
    }
}
