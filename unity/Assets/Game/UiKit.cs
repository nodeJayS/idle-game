#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
        public static Font Font => _font != null
            ? _font
            : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>A screen-space overlay canvas (creates the shared EventSystem if missing).</summary>
        public static Canvas CreateCanvas(string name, Transform? parent, int sortOrder = 0)
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

        public static Button TextButton(Transform parent, string label, Vector2 size, Vector2 pos, Action onClick)
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

            Label(go.transform, label, 26, TextAnchor.MiddleCenter, size, Vector2.zero);
            return btn;
        }
    }
}
