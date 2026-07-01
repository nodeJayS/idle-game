#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Top-right quest board: the rolling short-term goals, each with a progress bar. Draggable,
    /// lockable, minimizable, resizable — mirrors <see cref="ChatPanel"/> so the two panels look
    /// and behave the same (shared <see cref="UiKit"/> font + window chrome). Position/size/state
    /// persist via <see cref="Settings"/>. Read-only: CombatView pushes the live board in through
    /// <see cref="UpdateBoard"/> each frame; the panel just reflects it.
    /// </summary>
    public sealed class QuestPanel : MonoBehaviour
    {
        private const float HeaderH = 28f;
        private const float RowH = 40f;
        private const float RowTop = HeaderH + 8f; // body inset below the header
        private const int MaxRows = 6;
        private static readonly Vector2 MinSize = new(220f, 110f);
        private static readonly Vector2 MaxSize = new(380f, 360f);

        private Canvas _canvas = null!;
        private bool _collapsed;
        private bool _locked;
        private Vector2 _pos;
        private Vector2 _size;
        private RectTransform _body = null!;

        // One reusable set of row widgets (the board is small + fixed-ish); UpdateBoard fills them.
        private readonly List<GameObject> _rows = new();
        private readonly List<Text> _names = new();
        private readonly List<Text> _counts = new();
        private readonly List<RectTransform> _fills = new();

        public void Open()
        {
            _canvas = UiKit.CreateCanvas("QuestCanvas", transform, sortOrder: 83);
            _pos = new Vector2(Settings.QuestX, Settings.QuestY);
            _size = new Vector2(Settings.QuestW, Settings.QuestH);
            _size.x = Mathf.Clamp(_size.x, MinSize.x, MaxSize.x);
            _size.y = Mathf.Clamp(_size.y, MinSize.y, MaxSize.y);
            _collapsed = Settings.QuestCollapsed;
            _locked = Settings.QuestLocked;
            Build();
        }

        /// <summary>Push the live board in: refresh each row's label, count, and bar fill. Cheap —
        /// just text + an anchor tweak, safe to call every frame.</summary>
        public void UpdateBoard(QuestBoard board, GameConfig cfg)
        {
            if (board == null || _collapsed || _names.Count == 0) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                bool used = i < board.Active.Count;
                _rows[i].SetActive(used);
                if (!used) continue;
                var q = board.Active[i];
                _names[i].text = QuestLabel(q);
                // progress floors, the goal ceils (game-design §7 — never show a goal as met early)
                _counts[i].text = $"{Num.CompactFloor(q.Progress)} / {Num.CompactCeil(q.Target)}";
                float frac = q.Target > 0 ? Mathf.Clamp01((float)((double)q.Progress / q.Target)) : 0f;
                _fills[i].anchorMax = new Vector2(frac, 1f); // left-anchored fill: width = frac of the bar
            }
        }

        public static string QuestLabel(Quest q) => q.Kind switch
        {
            // goal amounts ceil (game-design §7): never understate what's required
            QuestKind.KillMonsters => $"Slay {Num.CompactCeil(q.Target)} monsters",
            QuestKind.SalvageItems => $"Salvage {Num.CompactCeil(q.Target)} items",
            QuestKind.EarnGold     => $"Earn {Num.CompactCeil(q.Target)} gold",
            QuestKind.ClearStages  => $"Clear {Num.CompactCeil(q.Target)} stages",
            QuestKind.FindRarePlus => $"Find {Num.CompactCeil(q.Target)} Rare+ items",
            _ => "Goal",
        };

        // ---- window (mirrors ChatPanel) ----

        private void Build()
        {
            ClearCanvas();
            _rows.Clear(); _names.Clear(); _counts.Clear(); _fills.Clear();

            float h = _collapsed ? HeaderH : _size.y;
            var panel = UiKit.Panel(_canvas.transform, new Vector2(_size.x, h), new Color(0.08f, 0.08f, 0.11f, 0.92f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 1f);            // anchor by the top-left corner (same as chat)
            prt.anchoredPosition = _pos;
            prt.anchoredPosition = _pos = UiKit.ClampToCanvas(_pos, prt, _canvas);
            Settings.QuestX = _pos.x; Settings.QuestY = _pos.y; // heal an off-screen saved position

            BuildHeader(panel.transform, prt);

            if (!_collapsed)
            {
                BuildBody(panel.transform);
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
            if (!_locked) UiKit.MakeDraggable(go, panelRt, _canvas, p => { _pos = p; Settings.QuestX = p.x; Settings.QuestY = p.y; });

            var title = UiKit.Label(go.transform, "Quests", 15, TextAnchor.MiddleLeft, new Vector2(140f, 22f), Vector2.zero);
            title.color = new Color(0.95f, 0.86f, 0.45f);
            Anchor((RectTransform)title.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f));

            var min = UiKit.TextButton(go.transform, _collapsed ? "+" : "—", new Vector2(24f, 20f), Vector2.zero, ToggleCollapse, 16);
            Anchor((RectTransform)min.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f));

            var lockBtn = UiKit.TextButton(go.transform, _locked ? "Locked" : "Free", new Vector2(46f, 20f), Vector2.zero, ToggleLock, 12);
            Anchor((RectTransform)lockBtn.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-34f, 0f));
            var li = lockBtn.GetComponent<Image>();
            if (li != null) li.color = _locked ? new Color(0.55f, 0.32f, 0.30f) : new Color(0.22f, 0.30f, 0.45f);
        }

        private void BuildBody(Transform panel)
        {
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(panel, false);
            _body = (RectTransform)bodyGo.transform;
            _body.anchorMin = new Vector2(0f, 0f);
            _body.anchorMax = new Vector2(1f, 1f);
            _body.offsetMin = new Vector2(8f, 8f);
            _body.offsetMax = new Vector2(-8f, -RowTop);

            for (int i = 0; i < MaxRows; i++) BuildRow(i);
        }

        private void BuildRow(int i)
        {
            var rowGo = new GameObject("Row" + i, typeof(RectTransform));
            rowGo.transform.SetParent(_body, false);
            var rrt = (RectTransform)rowGo.transform;
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(0f, RowH - 6f);
            rrt.anchoredPosition = new Vector2(0f, -i * RowH);

            var name = UiKit.Label(rowGo.transform, "", 15, TextAnchor.LowerLeft, Vector2.zero, Vector2.zero);
            name.horizontalOverflow = HorizontalWrapMode.Overflow; // single line; bar/edge clips overruns
            name.color = new Color(0.88f, 0.91f, 0.97f);
            var nrt = (RectTransform)name.transform;
            nrt.anchorMin = new Vector2(0f, 0.42f); nrt.anchorMax = new Vector2(0.72f, 1f);
            nrt.offsetMin = Vector2.zero; nrt.offsetMax = Vector2.zero;

            var count = UiKit.Label(rowGo.transform, "", 12, TextAnchor.LowerRight, Vector2.zero, Vector2.zero);
            count.horizontalOverflow = HorizontalWrapMode.Overflow;
            count.color = new Color(0.70f, 0.75f, 0.82f);
            var crt = (RectTransform)count.transform;
            crt.anchorMin = new Vector2(0.5f, 0.42f); crt.anchorMax = new Vector2(1f, 1f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;

            // Progress bar: a dark track with a left-anchored green fill (width set via anchorMax.x).
            var barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(rowGo.transform, false);
            var brt = (RectTransform)barGo.transform;
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(0f, 7f);
            brt.anchoredPosition = new Vector2(0f, 1f);
            barGo.AddComponent<Image>().color = new Color(0.15f, 0.16f, 0.20f);

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(barGo.transform, false);
            var frt = (RectTransform)fillGo.transform;
            frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 1f); // anchorMax.x grows the fill
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            fillGo.AddComponent<Image>().color = new Color(0.40f, 0.72f, 0.46f);

            rowGo.SetActive(false);
            _rows.Add(rowGo); _names.Add(name); _counts.Add(count); _fills.Add(frt);
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
            go.AddComponent<Image>().color = new Color(0.5f, 0.55f, 0.65f, 0.55f);
            UiKit.MakeResizable(go, panelRt, _canvas, MinSize, MaxSize, s =>
            {
                _size = s;
                Settings.QuestW = s.x; Settings.QuestH = s.y;
            });
        }

        private void ToggleCollapse()
        {
            _collapsed = !_collapsed;
            Settings.QuestCollapsed = _collapsed;
            Build();
        }

        private void ToggleLock()
        {
            _locked = !_locked;
            Settings.QuestLocked = _locked;
            Build();
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
