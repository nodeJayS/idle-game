#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Tiny code-built uGUI helpers so screens (main menu, claim modal) can be built
    /// without manual editor wiring — consistent with the rest of the in-code scene
    /// setup. Pure presentation; no game rules here.
    /// </summary>
    public static class UiKit
    {
        private static Font? _font;
        /// <summary>The shared UI font used by every Label/Button. Drop a .ttf at
        /// Assets/Resources/Fonts/UIFont.ttf to restyle the whole UI (e.g. Nunito/Fredoka);
        /// falls back to Unity's built-in font if that asset isn't present.</summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.Load<Font>("Fonts/UIFont");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>A screen-space overlay canvas (creates the shared EventSystem if missing).
        /// <paramref name="match"/> is the CanvasScaler width↔height blend: HUD canvases keep the
        /// historical 0 (match width); full-screen windows pass 0.5 so an ultrawide screen doesn't
        /// scale the UI so tall that the window loses its vertical room.</summary>
        public static Canvas CreateCanvas(string name, Transform? parent, int sortOrder = 0, float match = 0f)
        {
            EnsureEventSystem();

            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = match;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            // This project ships the Input System package; uGUI clicks route through it.
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        public static Image Panel(Transform parent, Vector2 size, Color color, Vector2 pos = default)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return img;
        }

        /// <summary>A full-screen stretched image (e.g. a dim backdrop).</summary>
        public static Image FullScreen(Transform parent, Color color)
        {
            var go = new GameObject("FullScreen", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        public static Text Label(Transform parent, string text, int fontSize, TextAnchor anchor,
                                 Vector2 size, Vector2 pos)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = Color.white;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return t;
        }

        /// <summary>
        /// A vertical scrolling list. Returns the content RectTransform — add rows (each
        /// with a LayoutElement for height) as children; it auto-sizes and scrolls.
        /// </summary>
        public static RectTransform ScrollColumn(Transform parent, Vector2 size, Vector2 pos, float spacing = 4f)
        {
            var go = new GameObject("Scroll", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            go.AddComponent<RectMask2D>();

            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 18f;
            scroll.viewport = rt;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(go.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = spacing;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = crt;
            return crt;
        }

        /// <summary>A vertical scroll whose content is a wrapping grid of fixed cells. Add
        /// children (e.g. <see cref="ItemTile"/>); the grid positions them. Returns content.</summary>
        public static RectTransform ScrollGrid(Transform parent, Vector2 size, Vector2 pos, Vector2 cell, float spacing = 8f)
        {
            var go = new GameObject("ScrollGrid", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            go.AddComponent<RectMask2D>();

            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 24f;
            scroll.viewport = rt;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(go.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            // Pin the content to exactly fill the viewport width (no horizontal inset/offset) so
            // the grid can't drift sideways out from under the mask — was clipping the first column.
            crt.offsetMin = new Vector2(0f, crt.offsetMin.y);
            crt.offsetMax = new Vector2(0f, crt.offsetMax.y);

            const int pad = 8;
            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = new Vector2(spacing, spacing);
            grid.padding = new RectOffset(pad, pad, pad, pad);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, Mathf.FloorToInt((size.x - pad * 2 + spacing) / (cell.x + spacing)));
            // Center the cell block within the (wider) content so leftover slack splits evenly
            // instead of all landing on one side — keeps every column fully inside the viewport.
            grid.childAlignment = TextAnchor.UpperCenter;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = crt;
            return crt;
        }

        /// <summary>A square item tile: rarity-colored border (none-ish for Normal) around a
        /// dark cell with a centered label (placeholder until real item icons exist). The
        /// returned object carries the border Image as its raycast target when
        /// <paramref name="raycast"/> is true (add a Button/Hover to it).</summary>
        public static GameObject ItemTile(Transform parent, Vector2 size, Vector2 pos, Rarity? rarity, string text, bool raycast)
        {
            var go = new GameObject("Tile", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;

            var border = go.AddComponent<Image>();
            bool hasBorder = rarity != null && !Palette.Borderless(rarity.Value);
            border.color = rarity == null ? new Color(0.24f, 0.26f, 0.31f)
                         : hasBorder ? Palette.Rarity(rarity.Value)
                                     : new Color(0.30f, 0.32f, 0.37f);
            border.raycastTarget = raycast;

            var inner = new GameObject("bg", typeof(RectTransform));
            inner.transform.SetParent(go.transform, false);
            var irt = (RectTransform)inner.transform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            float b = hasBorder ? 4f : 2f;
            irt.offsetMin = new Vector2(b, b); irt.offsetMax = new Vector2(-b, -b);
            var ibg = inner.AddComponent<Image>();
            ibg.color = new Color(0.12f, 0.13f, 0.16f);
            ibg.raycastTarget = false;

            int fs = size.x >= 64 ? 13 : 11;
            var lbl = Label(inner.transform, text, fs, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            var lrt = (RectTransform)lbl.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(2, 2); lrt.offsetMax = new Vector2(-2, -2);
            lbl.color = rarity != null ? Palette.Rarity(rarity.Value) : new Color(0.55f, 0.58f, 0.63f);
            lbl.raycastTarget = false;
            return go;
        }

        /// <summary>Short tile label for an equip slot (placeholder until real item icons).</summary>
        public static string SlotAbbrev(EquipSlot s) => s switch
        {
            EquipSlot.Weapon => "Wpn", EquipSlot.Helm => "Helm", EquipSlot.Chest => "Body",
            EquipSlot.Gloves => "Glov", EquipSlot.Boots => "Boot", _ => "?",
        };

        private static Sprite? _circle;

        /// <summary>A soft-edged white circle sprite (tint via Image.color). Cached.</summary>
        public static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;
            const int d = 64;
            var tex = new Texture2D(d, d, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var center = new Vector2(d / 2f, d / 2f);
            float r = d / 2f - 1f;
            var px = new Color[d * d];
            for (int y = 0; y < d; y++)
                for (int x = 0; x < d; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    px[y * d + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - dist + 0.5f));
                }
            tex.SetPixels(px);
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, d, d), new Vector2(0.5f, 0.5f));
            return _circle;
        }

        public static Image Circle(Transform parent, float diameter, Color color, Vector2 pos)
        {
            var img = Panel(parent, new Vector2(diameter, diameter), color, pos);
            img.sprite = CircleSprite();
            img.type = Image.Type.Simple;
            return img;
        }

        public static InputField TextInput(Transform parent, string value, Vector2 size, Vector2 pos, Action<string> onEndEdit)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.17f, 0.22f);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var field = go.AddComponent<InputField>();
            var text = Label(go.transform, value, 22, TextAnchor.MiddleLeft, size, Vector2.zero);
            var trt = (RectTransform)text.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12, 0); trt.offsetMax = new Vector2(-12, 0);

            field.textComponent = text;
            field.text = value;
            field.onEndEdit.AddListener(s => onEndEdit(s));
            return field;
        }

        /// <summary>Fire callbacks when the pointer enters/leaves <paramref name="go"/> (e.g. to
        /// drive a hover compare/tooltip). Requires a raycast-target Graphic on the object.</summary>
        public static void Hover(GameObject go, Action onEnter, Action? onExit = null)
        {
            var h = go.AddComponent<HoverProxy>();
            h.OnEnter = onEnter;
            h.OnExit = onExit;
        }

        /// <summary>Makes <paramref name="handle"/> drag <paramref name="target"/> around its
        /// canvas (clamped on-screen). <paramref name="onMoved"/> fires with the new anchored
        /// position so callers can persist it across rebuilds. Pure presentation.</summary>
        public static void MakeDraggable(GameObject handle, RectTransform target, Canvas canvas, Action<Vector2>? onMoved = null)
        {
            var d = handle.AddComponent<DraggableHandle>();
            d.Target = target;
            d.Canvas = canvas;
            d.OnMoved = onMoved;
        }

        /// <summary>Makes <paramref name="grip"/> resize <paramref name="target"/> (top-left fixed,
        /// bottom-right follows the cursor), clamped to [min,max]. <paramref name="onResized"/>
        /// fires with the new size so callers can persist it.</summary>
        public static void MakeResizable(GameObject grip, RectTransform target, Canvas canvas,
                                         Vector2 min, Vector2 max, Action<Vector2>? onResized = null)
        {
            var r = grip.AddComponent<ResizeHandle>();
            r.Target = target;
            r.Canvas = canvas;
            r.Min = min;
            r.Max = max;
            r.OnResized = onResized;
        }

        /// <summary>Clamps a point-anchored RectTransform's anchored position so the whole rect
        /// stays within its canvas. Works for any pivot.</summary>
        public static Vector2 ClampToCanvas(Vector2 pos, RectTransform target, Canvas canvas)
        {
            var canvasRect = ((RectTransform)canvas.transform).rect;
            if (canvasRect.width <= 0f || canvasRect.height <= 0f) return pos; // canvas not laid out yet
            const float m = 8f; // keep a small margin so a panel never sits flush against (or off) an edge
            var size = target.rect.size;
            float minX = m + size.x * target.pivot.x;
            float maxX = canvasRect.width - m - size.x * (1f - target.pivot.x);
            float halfH = canvasRect.height / 2f;
            float maxY = halfH - m - size.y * (1f - target.pivot.y);
            float minY = -halfH + m + size.y * target.pivot.y;
            pos.x = Mathf.Clamp(pos.x, minX, Mathf.Max(minX, maxX));
            pos.y = Mathf.Clamp(pos.y, minY, Mathf.Max(minY, maxY));
            return pos;
        }

        /// <summary>A vertical scroll that fills its layout cell (flexible LayoutElement) instead
        /// of anchoring at a fixed size — for scrolls that live inside a PanelKit column. Returns
        /// the content RectTransform (a VerticalLayoutGroup); add rows with a LayoutElement height.</summary>
        public static RectTransform ScrollColumnFill(Transform parent, float spacing = 4f)
        {
            var go = new GameObject("Scroll", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            go.AddComponent<RectMask2D>();
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.flexibleHeight = 1f;

            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 18f;
            scroll.viewport = rt;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(go.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(0f, crt.offsetMin.y);
            crt.offsetMax = new Vector2(0f, crt.offsetMax.y);
            crt.anchoredPosition = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = spacing;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = crt;
            return crt;
        }

        /// <summary>A wrapping-grid scroll that fills its layout cell (flexible LayoutElement). The
        /// grid uses a Flexible constraint so it re-wraps to whatever width the layout cell gives —
        /// for grids inside a PanelKit column. Returns the content RectTransform.</summary>
        public static RectTransform ScrollGridFill(Transform parent, Vector2 cell, float spacing = 8f)
        {
            var go = new GameObject("ScrollGrid", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            go.AddComponent<RectMask2D>();
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.flexibleHeight = 1f;

            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 24f;
            scroll.viewport = rt;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(go.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(0f, crt.offsetMin.y);
            crt.offsetMax = new Vector2(0f, crt.offsetMax.y);
            crt.anchoredPosition = Vector2.zero;

            const int pad = 8;
            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = new Vector2(spacing, spacing);
            grid.padding = new RectOffset(pad, pad, pad, pad);
            // Flexible constraint auto-wraps to the cell width the layout group hands us — no
            // fixed column count to recompute (the anchored ScrollGrid needs a known width; this
            // one adapts).
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            grid.childAlignment = TextAnchor.UpperCenter;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = crt;
            return crt;
        }

        public static Button TextButton(Transform parent, string label, Vector2 size, Vector2 pos, Action onClick, int fontSize = 26)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.30f, 0.45f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            Label(go.transform, label, fontSize, TextAnchor.MiddleCenter, size, Vector2.zero);
            return btn;
        }
    }

    /// <summary>Pointer enter/exit relay, added via <see cref="UiKit.Hover"/>.</summary>
    public sealed class HoverProxy : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action? OnEnter;
        public Action? OnExit;
        public void OnPointerEnter(PointerEventData e) => OnEnter?.Invoke();
        public void OnPointerExit(PointerEventData e) => OnExit?.Invoke();
    }

    /// <summary>Drag handler that moves a target RectTransform by the pointer delta, clamped so
    /// the window stays on the canvas. Added via <see cref="UiKit.MakeDraggable"/>.</summary>
    public sealed class DraggableHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform Target = null!;
        public Canvas Canvas = null!;
        public Action<Vector2>? OnMoved;

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (Target == null || Canvas == null) return;
            float scale = Canvas.scaleFactor <= 0f ? 1f : Canvas.scaleFactor;
            var pos = UiKit.ClampToCanvas(Target.anchoredPosition + e.delta / scale, Target, Canvas);
            Target.anchoredPosition = pos;
            OnMoved?.Invoke(pos);
        }
    }

    /// <summary>Drag handler that resizes a target RectTransform from its bottom-right grip
    /// (top-left stays anchored). Added via <see cref="UiKit.MakeResizable"/>.</summary>
    public sealed class ResizeHandle : MonoBehaviour, IDragHandler
    {
        public RectTransform Target = null!;
        public Canvas Canvas = null!;
        public Vector2 Min = new(260f, 160f);
        public Vector2 Max = new(640f, 640f);
        public Action<Vector2>? OnResized;

        public void OnDrag(PointerEventData e)
        {
            if (Target == null || Canvas == null) return;
            float scale = Canvas.scaleFactor <= 0f ? 1f : Canvas.scaleFactor;
            var size = Target.sizeDelta;
            // Pointer y is up-positive in screen space; the window grows downward as the grip drops.
            size.x = Mathf.Clamp(size.x + e.delta.x / scale, Min.x, Max.x);
            size.y = Mathf.Clamp(size.y - e.delta.y / scale, Min.y, Max.y);
            Target.sizeDelta = size;
            OnResized?.Invoke(size);
        }
    }
}
