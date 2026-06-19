#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IdleGame.Game
{
    /// <summary>
    /// Left-side chat/activity panel. The "Feed" tab is the live loot/XP log (fed by
    /// CombatView); Global / Friends / Guild are stubs until the online service lands.
    /// Collapsible so it can stay out of the way. Read-only over game state.
    /// </summary>
    public sealed class ChatPanel : MonoBehaviour
    {
        private static readonly string[] Tabs = { "Feed", "Global", "Friends", "Guild" };
        private const int MaxFeed = 60;

        private readonly List<(string text, Color color)> _feed = new();
        private readonly Dictionary<string, Button> _tabButtons = new();

        private Canvas _canvas = null!;
        private string _active = "Feed";
        private RectTransform _body = null!;
        private RectTransform? _feedContent;

        public void Open()
        {
            _canvas = UiKit.CreateCanvas("ChatCanvas", transform, sortOrder: 84);
            BuildExpanded();
        }

        /// <summary>Append a line to the activity feed (newest at the bottom).</summary>
        public void AddFeed(string text, Color color)
        {
            _feed.Add((text, color));
            if (_feed.Count > MaxFeed) _feed.RemoveAt(0);
            if (_active == "Feed" && _feedContent != null) AppendFeedRow(text, color);
        }

        // ---- expanded / collapsed ----

        private void BuildExpanded()
        {
            ClearCanvas();

            var panel = UiKit.Panel(_canvas.transform, new Vector2(360, 480), new Color(0.08f, 0.08f, 0.11f, 0.92f));
            AnchorLeft(panel.rectTransform, new Vector2(190, 0));

            UiKit.Label(panel.transform, "Chat", 22, TextAnchor.MiddleLeft, new Vector2(120, 34), new Vector2(-150, 212));
            UiKit.TextButton(panel.transform, "Hide", new Vector2(90, 40), new Vector2(130, 212), Collapse);

            _tabButtons.Clear();
            float x = -126;
            foreach (var t in Tabs)
            {
                var tab = t;
                var b = UiKit.TextButton(panel.transform, t, new Vector2(82, 44), new Vector2(x, 162), () => SwitchTab(tab));
                _tabButtons[t] = b;
                x += 84;
            }

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(panel.transform, false);
            _body = (RectTransform)bodyGo.transform;
            _body.anchorMin = _body.anchorMax = new Vector2(0.5f, 0.5f);
            _body.sizeDelta = new Vector2(340, 360);
            _body.anchoredPosition = new Vector2(0, -40);

            BuildBody();
            RefreshTabHighlight();
        }

        private void Collapse()
        {
            ClearCanvas();
            var b = UiKit.TextButton(_canvas.transform, "Chat", new Vector2(120, 48), Vector2.zero, Expand);
            AnchorLeft(b.GetComponent<RectTransform>(), new Vector2(70, -200));
        }

        private void Expand() => BuildExpanded();

        // ---- tabs / body ----

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

            if (_active == "Feed")
            {
                _feedContent = UiKit.ScrollColumn(_body, new Vector2(340, 360), Vector2.zero);
                foreach (var (text, color) in _feed) AppendFeedRow(text, color);
            }
            else
            {
                UiKit.Label(_body, "Coming soon — chat arrives with the online update.",
                            17, TextAnchor.MiddleCenter, new Vector2(320, 120), Vector2.zero);
            }
        }

        private void AppendFeedRow(string text, Color color)
        {
            if (_feedContent == null) return;
            var label = UiKit.Label(_feedContent, text, 16, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            label.color = color;
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 26;
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

        private static void AnchorLeft(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
        }
    }
}
