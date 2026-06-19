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
