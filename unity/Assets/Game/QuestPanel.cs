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
        // 10.12b: last-pushed values per row. UpdateBoard runs every frame and the .text setter
        // early-outs on an EQUAL string — but the interpolation itself still allocated one; skip
        // the string work entirely unless the quest actually moved. Seeded with sentinels in Build.
        private readonly List<QuestKind> _rowKind = new();
        private readonly List<long> _rowTarget = new();
        private readonly List<long> _rowProgress = new();

        // FTUE guided-intro strip (§7.4): a warm "Getting started" header + one row per beat, above the
        // rolling board. Pre-built hidden and driven by CombatView.UpdateIntro; _introOffset is the body
        // space the active strip reserves, which pushes the rolling rows down (0 when it's inactive/retired).
        private const float IntroRowH = 22f;
        private const float IntroHeaderH = 22f;
        private GameObject? _introHeaderGo;
        private readonly List<GameObject> _introRows = new();
        private readonly List<Text> _introLabels = new();
        private readonly List<int> _introDone = new(); // 10.12b: -1 unseeded / 0 pending / 1 claimed
        private float _introOffset;

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

        /// <summary>Push the live guided-intro strip in (§7.4): show/label the beats above the rolling
        /// board and set the offset that pushes those rows down. <paramref name="active"/> false (or null
        /// rows) hides the whole strip — the state a fresh save reaches once all five beats are claimed,
        /// and the state every unarmed save is always in (so it renders nothing). Safe every frame.</summary>
        public void UpdateIntro(List<IntroQuestStatus>? rows, bool active)
        {
            if (_collapsed || _introHeaderGo == null) { _introOffset = 0f; return; }
            bool show = active && rows != null && rows.Count > 0;
            _introHeaderGo.SetActive(show);
            int shown = 0;
            for (int i = 0; i < _introRows.Count; i++)
            {
                bool use = show && rows != null && i < rows.Count;
                _introRows[i].SetActive(use);
                if (!use) continue;
                var r = rows![i];
                int done = r.Claimed ? 1 : 0;
                if (_introDone[i] != done) // only the claimed flag can change; title is fixed per beat
                {
                    _introDone[i] = done;
                    _introLabels[i].text = (done == 1 ? "✔ " : "• ") + r.Quest.Title;
                    _introLabels[i].color = done == 1 ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.82f, 0.86f, 0.92f);
                }
                shown++;
            }
            _introOffset = show ? IntroHeaderH + shown * IntroRowH + 6f : 0f;
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
                // Slide the rolling rows below the guided-intro strip (offset 0 when it's inactive).
                // Position/anchor writes are struct setters — alloc-free, so they stay unconditional.
                ((RectTransform)_rows[i].transform).anchoredPosition = new Vector2(0f, -_introOffset - i * RowH);
                if (_rowKind[i] != q.Kind || _rowTarget[i] != q.Target || _rowProgress[i] != q.Progress)
                {
                    _rowKind[i] = q.Kind; _rowTarget[i] = q.Target; _rowProgress[i] = q.Progress;
                    _names[i].text = QuestLabel(q);
                    // progress floors, the goal ceils (game-design §7 — never show a goal as met early)
                    _counts[i].text = $"{Num.CompactFloor(q.Progress)} / {Num.CompactCeil(q.Target)}";
                }
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
            _rowKind.Clear(); _rowTarget.Clear(); _rowProgress.Clear();
            _introRows.Clear(); _introLabels.Clear(); _introDone.Clear(); _introHeaderGo = null; _introOffset = 0f;

            float h = _collapsed ? HeaderH : _size.y;
            // Corner-anchored HUD: build under the canvas's SafeRoot so it insets from device notches.
            // On desktop SafeRoot == the canvas rect, so position/drag/clamp behave byte-identically.
            var panel = UiKit.Panel(UiKit.SafeRoot(_canvas), new Vector2(_size.x, h), new Color(0.08f, 0.08f, 0.11f, 0.92f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 1f);            // anchor by the top-left corner (same as chat)
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

            BuildIntro();                                    // guided-intro strip (hidden until pushed)
            for (int i = 0; i < MaxRows; i++) BuildRow(i);
        }

        // Guided-intro widgets (§7.4): a header + one label per beat, anchored from the top of the body
        // (above the rolling rows). Built hidden; UpdateIntro shows/labels them and sets _introOffset.
        private void BuildIntro()
        {
            var hgo = new GameObject("IntroHeader", typeof(RectTransform));
            hgo.transform.SetParent(_body, false);
            var hrt = (RectTransform)hgo.transform;
            hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(0f, IntroHeaderH);
            hrt.anchoredPosition = Vector2.zero;
            var htext = UiKit.Label(hgo.transform, "Getting started", 14, TextAnchor.LowerLeft, Vector2.zero, Vector2.zero);
            htext.color = new Color(0.98f, 0.80f, 0.42f);
            var hlrt = (RectTransform)htext.transform;
            hlrt.anchorMin = new Vector2(0f, 0f); hlrt.anchorMax = new Vector2(1f, 1f);
            hlrt.offsetMin = Vector2.zero; hlrt.offsetMax = Vector2.zero;
            hgo.SetActive(false);
            _introHeaderGo = hgo;

            for (int i = 0; i < IntroQuests.All.Count; i++)
            {
                var rgo = new GameObject("IntroRow" + i, typeof(RectTransform));
                rgo.transform.SetParent(_body, false);
                var rrt = (RectTransform)rgo.transform;
                rrt.anchorMin = new Vector2(0f, 1f); rrt.anchorMax = new Vector2(1f, 1f);
                rrt.pivot = new Vector2(0.5f, 1f);
                rrt.sizeDelta = new Vector2(0f, IntroRowH - 2f);
                rrt.anchoredPosition = new Vector2(0f, -(IntroHeaderH + i * IntroRowH));

                var lbl = UiKit.Label(rgo.transform, "", 13, TextAnchor.LowerLeft, Vector2.zero, Vector2.zero);
                lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                lbl.color = new Color(0.82f, 0.86f, 0.92f);
                var lrt = (RectTransform)lbl.transform;
                lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 1f);
                lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

                rgo.SetActive(false);
                _introRows.Add(rgo); _introLabels.Add(lbl); _introDone.Add(-1);
            }
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
            _rowKind.Add((QuestKind)(-1)); _rowTarget.Add(-1); _rowProgress.Add(-1); // sentinel: first update always fills
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
