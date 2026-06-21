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
        private RectTransform? _feedContent;

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
            if (!_collapsed && _active == SystemTab && _feedContent != null) AppendFeedRow(text, color);
        }

        // ---- window ----

        private void Build()
        {
            ClearCanvas();
            _feedContent = null;

            float h = _collapsed ? HeaderH : _size.y;
            var panel = UiKit.Panel(_canvas.transform, new Vector2(_size.x, h), new Color(0.08f, 0.08f, 0.11f, 0.92f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 1f);            // anchor by the top-left corner
            prt.anchoredPosition = _pos;
            prt.anchoredPosition = _pos = UiKit.ClampToCanvas(_pos, prt, _canvas);
            Settings.ChatX = _pos.x; Settings.ChatY = _pos.y; // heal a previously off-screen saved position

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
            _feedContent = null;

            if (_active == SystemTab)
            {
                _feedContent = UiKit.ScrollColumn(_body, Vector2.zero, Vector2.zero);
                var scrollRoot = (RectTransform)_feedContent.parent;
                scrollRoot.anchorMin = Vector2.zero;
                scrollRoot.anchorMax = Vector2.one;
                scrollRoot.offsetMin = scrollRoot.offsetMax = Vector2.zero;
                foreach (var (text, color) in _feed) AppendFeedRow(text, color);
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

        private void AppendFeedRow(string text, Color color)
        {
            if (_feedContent == null) return;
            var label = UiKit.Label(_feedContent, text, 14, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            label.color = color;
            // Single line, left-aligned: the RectMask2D clips any overrun on the RIGHT, instead of
            // wrapping (which, with a fixed row height, dropped lines and looked left-misaligned).
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 20;
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
