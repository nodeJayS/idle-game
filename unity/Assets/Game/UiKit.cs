#nullable enable
using System;
using System.Collections.Generic;
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

        /// <summary>Get-or-create a full-stretch "SafeRoot" child of <paramref name="canvas"/>
        /// carrying a <see cref="SafeArea"/>, and return its RectTransform. Build corner-anchored HUD
        /// surfaces under it so they inset from device notches / rounded corners; centered windows
        /// don't collide with a LANDSCAPE notch, so only corner surfaces need this now (panel/window
        /// migration is slice 10.13c). Idempotent — finds the existing child by name first. On desktop
        /// the SafeArea is a no-op, so the SafeRoot rect == the canvas rect and nothing shifts.</summary>
        public static RectTransform SafeRoot(Canvas canvas)
        {
            var existing = canvas.transform.Find("SafeRoot");
            if (existing != null) return (RectTransform)existing;

            var go = new GameObject("SafeRoot", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.AddComponent<SafeArea>();
            return rt;
        }

        public static Image Panel(Transform parent, Vector2 size, Color color, Vector2 pos = default)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            Round(img, Theme.RadiusPanel);
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

        /// <summary>The a11y text-size multiplier applied to EVERY font size — one place so uGUI
        /// (via <see cref="Label"/>) and IMGUI (the HUD's cached GUIStyles) render at the same scale.
        /// Rounds and floors at 1 so a tiny base can never vanish. See <see cref="Settings.TextScale"/>.</summary>
        public static int Scaled(int fontSize) => Mathf.Max(1, Mathf.RoundToInt(fontSize * Settings.TextScale));

        public static Text Label(Transform parent, string text, int fontSize, TextAnchor anchor,
                                 Vector2 size, Vector2 pos)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = Scaled(fontSize);
            t.alignment = anchor;
            t.color = Color.white;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return t;
        }

        /// <summary>The recessed ground shared by all four scroll viewports. Neutral black at 25%
        /// rather than a Theme token — it darkens whatever surface it lands on, so it followed the
        /// palette warm for free — rounded like the inset boxes it reads as.</summary>
        private static Image ScrollBg(GameObject go)
        {
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            return Round(bg, Theme.RadiusPanel);
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

            ScrollBg(go);
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

            ScrollBg(go);
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
        public static GameObject ItemTile(Transform parent, Vector2 size, Vector2 pos, Rarity? rarity, string text, bool raycast, EquipSlot? slot = null)
        {
            var go = new GameObject("Tile", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;

            var border = go.AddComponent<Image>();
            bool hasBorder = rarity != null && !Palette.Borderless(rarity.Value);
            border.color = rarity == null ? new Color(0.255f, 0.220f, 0.175f)
                         : hasBorder ? Palette.Rarity(rarity.Value)
                                     : new Color(0.315f, 0.272f, 0.215f);
            border.raycastTarget = raycast;
            Round(border, Theme.RadiusTile);

            var inner = new GameObject("bg", typeof(RectTransform));
            inner.transform.SetParent(go.transform, false);
            var irt = (RectTransform)inner.transform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            float b = hasBorder ? 4f : 2f;
            irt.offsetMin = new Vector2(b, b); irt.offsetMax = new Vector2(-b, -b);
            var ibg = inner.AddComponent<Image>();
            ibg.color = Theme.RowEmpty;
            ibg.raycastTarget = false;
            // Inner arc = outer minus the frame width, so the two curves stay concentric instead of
            // leaving a pinched sliver of border colour in each corner.
            Round(ibg, Mathf.Max(2, Theme.RadiusTile - Mathf.RoundToInt(b)));

            // The tile's centre: the slot's icon when we have one, the old text abbreviation when we
            // don't. Same COLOUR rule either way — the glyph carries rarity, the border carries it
            // again, and an empty doll slot stays dim while still saying what belongs there.
            var glyph = rarity != null ? Palette.Rarity(rarity.Value) : Theme.TextDisabled;
            var icon = slot != null ? SlotIcon(slot.Value) : null;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(inner.transform, false);
                var iconRt = (RectTransform)iconGo.transform;
                iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one;
                // Proportional inset, so one icon set serves the 84px doll cell and the 56px bag
                // tile without a second asset or a per-caller size.
                float pad = Mathf.Round(size.x * 0.17f);
                iconRt.offsetMin = new Vector2(pad, pad); iconRt.offsetMax = new Vector2(-pad, -pad);
                var im = iconGo.AddComponent<Image>();
                im.sprite = icon;
                im.preserveAspect = true;   // the source art is square, but never trust a caller's size
                im.color = glyph;
                im.raycastTarget = false;   // the border Image owns the tile's clicks
            }
            else
            {
                int fs = size.x >= 64 ? 13 : 11;
                var lbl = Label(inner.transform, text, fs, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
                var lrt = (RectTransform)lbl.transform;
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(2, 2); lrt.offsetMax = new Vector2(-2, -2);
                lbl.color = glyph;
                lbl.raycastTarget = false;
            }

            // 10.20b glyph channel: the rarity mark in the tile's BOTTOM-RIGHT corner, inside the
            // inner bg, so the tier reads without color vision. Bottom-right is the one free corner
            // of the badge layout — top-right = upgrade ▲, top-left = imprint ✦, bottom-left = the
            // [L] lock tag. A subtle marker, not a badge — FsTiny, near-white, never a raycast
            // target (the border Image owns the tile's clicks). Built after the centre label so it
            // draws on top; Normal's empty mark keeps baseline tiles clean, mirroring the
            // borderless treatment.
            string mark = rarity != null ? Palette.RarityMark(rarity.Value) : "";
            if (mark.Length > 0)
            {
                var mk = Label(inner.transform, mark, Theme.FsTiny, TextAnchor.LowerRight,
                               new Vector2(16f, 16f), Vector2.zero);
                var mrt = (RectTransform)mk.transform;
                mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(1f, 0f);
                mrt.anchoredPosition = new Vector2(-2f, 2f);
                mk.color = new Color(1f, 1f, 1f, 0.9f);
                mk.raycastTarget = false;
            }
            return go;
        }

        /// <summary>Loaded slot icons, including the MISSES: a null here means "we looked and there
        /// is no sprite", which keeps a failed load from re-hitting Resources on every tile of every
        /// redraw. Sprite names match the enum (weapon/helm/chest/gloves/boots), so adding a slot
        /// needs art and nothing else.</summary>
        private static readonly Dictionary<EquipSlot, Sprite?> _slotIcons = new();

        /// <summary>The icon for an equip slot, or null to fall back to <see cref="SlotAbbrev"/>.
        /// Art: game-icons.net, CC BY 3.0 (credited in the README and in Settings) — baked to
        /// tintable white-on-transparent PNGs by <c>art/icons/build.py</c>.</summary>
        public static Sprite? SlotIcon(EquipSlot s)
        {
            if (_slotIcons.TryGetValue(s, out var cached)) return cached;
            var sprite = Resources.Load<Sprite>("Icons/" + s.ToString().ToLowerInvariant());
            _slotIcons[s] = sprite;
            return sprite;
        }

        /// <summary>Short tile label for an equip slot — the fallback when an icon is missing, and
        /// still the name every non-tile surface uses.</summary>
        public static string SlotAbbrev(EquipSlot s) => s switch
        {
            EquipSlot.Weapon => "Wpn", EquipSlot.Helm => "Helm", EquipSlot.Chest => "Body",
            EquipSlot.Gloves => "Glov", EquipSlot.Boots => "Boot", _ => "?",
        };

        // ==== Procedural surfaces =====================================================
        // Nothing in this project is authored in the editor — no prefabs, no imported UI sprites —
        // so the kit BAKES its own rounded-rect textures and 9-slices them: the corner arc keeps a
        // fixed pixel radius while the middle stretches to whatever the layout hands the Image.
        // Coverage is sampled from the signed distance rather than thresholded, because an aliased
        // curve reads worse than an honest square corner. One texture per shape, cached for the
        // session — a window rebuild (every open) must never bake a new one.
        private static readonly Dictionary<int, Sprite> _rounded = new();
        private static readonly Dictionary<int, Sprite> _shadows = new();

        /// <summary>A white rounded-rect sprite, 9-sliced so it stretches without smearing its
        /// corners — assign it and set <c>Image.type = Sliced</c>; tint via Image.color.
        /// <paramref name="border"/>&gt;0 punches the middle out for a rounded OUTLINE of that
        /// pixel thickness. Cached per (radius, border).</summary>
        public static Sprite RoundedRect(int radius, int border = 0)
        {
            radius = Mathf.Max(1, radius);
            border = Mathf.Clamp(border, 0, 255);
            int key = radius * 256 + border;
            if (_rounded.TryGetValue(key, out var cached) && cached != null) return cached;

            // 2r+3: one arc per side plus a single stretchable pixel of middle, which is exactly
            // what a (r+1) 9-slice border leaves over. Any bigger is wasted memory.
            int n = radius * 2 + 3;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float half = n / 2f, core = half - radius; // half-extent of the square the arc rides on
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - core, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - core, 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - radius; // signed: <0 inside
                    float a = Mathf.Clamp01(0.5f - d);                // 1px coverage ramp across the edge
                    if (border > 0) a -= Mathf.Clamp01(0.5f - (d + border));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            // 100 ppu == the canvas' referencePixelsPerUnit, so one texel renders as one UI unit and
            // the radius constants mean what they say on screen.
            var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect,
                                   new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            _rounded[key] = sp;
            return sp;
        }

        /// <summary>The elevation counterpart of <see cref="RoundedRect"/>: solid in the middle and
        /// ramping to zero over the outer <paramref name="radius"/> pixels, so a black copy behind a
        /// window reads as a soft drop shadow. Same 9-slice, same cache discipline.</summary>
        public static Sprite SoftShadow(int radius)
        {
            radius = Mathf.Max(2, radius);
            if (_shadows.TryGetValue(radius, out var cached) && cached != null) return cached;

            int n = radius * 2 + 3;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float half = n / 2f, core = half - radius;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - core, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - core, 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - radius; // -radius at the centre, 0 at the edge
                    float t = Mathf.Clamp01(-d / radius);
                    float a = t * t * (3f - 2f * t); // smoothstep: a linear ramp bands visibly at low alpha
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect,
                                   new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            _shadows[radius] = sp;
            return sp;
        }

        /// <summary>Give an existing Image a rounded surface. One call site for the sprite+type pair
        /// so nothing can round a rect and forget to slice it (an unsliced sprite squashes its arcs).</summary>
        public static Image Round(Image img, int radius)
        {
            img.sprite = RoundedRect(radius);
            img.type = Image.Type.Sliced;
            return img;
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
            img.color = Theme.RowEmpty; // a field reads as an empty row you can fill, not a panel
            Round(img, Theme.RadiusButton);
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
        public static void MakeDraggable(GameObject handle, RectTransform target, Canvas canvas,
                                         Action<Vector2>? onMoved = null, float bottomInset = 0f)
        {
            var d = handle.AddComponent<DraggableHandle>();
            d.Target = target;
            d.Canvas = canvas;
            d.OnMoved = onMoved;
            d.BottomInset = bottomInset;
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
        /// <paramref name="bottomInset"/> reserves a band along the bottom edge that the panel may
        /// not enter — the NavBar owns that strip on its own canvas, and the clamp cannot see it.
        /// Without it a bottom-anchored HUD panel clamps flush to the canvas floor and slides
        /// straight under the bar on a short canvas (HUD canvases are match-0, so 2340x1080 gives
        /// only ~591 units of height against 720 at 16:9 — that is where Chat went under).
        public static Vector2 ClampToCanvas(Vector2 pos, RectTransform target, Canvas canvas,
                                            float bottomInset = 0f)
        {
            var canvasRect = ((RectTransform)canvas.transform).rect;
            if (canvasRect.width <= 0f || canvasRect.height <= 0f) return pos; // canvas not laid out yet
            const float m = 8f; // keep a small margin so a panel never sits flush against (or off) an edge
            var size = target.rect.size;
            float minX = m + size.x * target.pivot.x;
            float maxX = canvasRect.width - m - size.x * (1f - target.pivot.x);
            float halfH = canvasRect.height / 2f;
            float maxY = halfH - m - size.y * (1f - target.pivot.y);
            float minY = -halfH + m + bottomInset + size.y * target.pivot.y;
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

            ScrollBg(go);
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
            ScrollFade.Attach(scroll); // "more below" tell; self-hides when the content fits
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

            ScrollBg(go);
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
            ScrollFade.Attach(scroll); // "more below" tell; self-hides when the grid fits
            return crt;
        }

        public static Button TextButton(Transform parent, string label, Vector2 size, Vector2 pos, Action onClick, int fontSize = 26)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = Theme.BtnPrimary;
            Round(img, Theme.RadiusButton);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ApplyButtonStates(btn);
            // 10.9d UI sound language: EVERY button clicks through this factory (PanelKit.
            // ButtonCell wraps it), so the one family lands everywhere. Disabled cells never
            // fire (interactable = false), so dead buttons stay silent.
            btn.onClick.AddListener(() => { SoundFx.Play("System_SubButton_Click", 0.3f); onClick(); });

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            Label(go.transform, label, fontSize, TextAnchor.MiddleCenter, size, Vector2.zero);
            return btn;
        }

        /// <summary>The one press-feedback contract for every button the kit builds. ColorTint
        /// MULTIPLIES the Image colour, so every state is a multiplier around white and the
        /// Theme.Btn* token a view assigns still owns the button's identity — hover lifts it, the
        /// press sinks it, and nothing here fights an explicit <c>img.color =</c>. Disabled is
        /// white on purpose: callers already swap in Theme.BtnDisabled, and Unity's default
        /// disabled tint (grey at HALF ALPHA) would ghost those buttons on top of that.</summary>
        public static void ApplyButtonStates(Selectable s)
        {
            var c = s.colors;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            c.pressedColor = new Color(0.90f, 0.90f, 0.90f, 1f);
            c.selectedColor = Color.white; // uGUI keeps focus after a click; a lingering tint reads as stuck
            c.disabledColor = Color.white;
            c.colorMultiplier = 1f;
            c.fadeDuration = 0.08f;
            s.colors = c;
            s.transition = Selectable.Transition.ColorTint;
            // P3 motion: tint and squash are ONE contract. Hooking the squash here (rather than at
            // each factory) is what makes it impossible for a control to get the colour half of the
            // press feedback and not the movement half.
            UiMotion.AttachPress(s);
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
        /// <summary>Bottom band the drag may not enter (the NavBar's strip). Matches the
        /// <see cref="KeepOnCanvas"/> inset so dragging can't put a panel somewhere the
        /// restore clamp would immediately pull it back out of.</summary>
        public float BottomInset;

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (Target == null || Canvas == null) return;
            float scale = Canvas.scaleFactor <= 0f ? 1f : Canvas.scaleFactor;
            var pos = UiKit.ClampToCanvas(Target.anchoredPosition + e.delta / scale, Target, Canvas, BottomInset);
            Target.anchoredPosition = pos;
            OnMoved?.Invoke(pos);
        }
    }

    /// <summary>
    /// Keeps a point-anchored, persisted-rect panel (quest/chat HUD) fully on its canvas —
    /// the restore-time counterpart of <see cref="DraggableHandle"/>'s drag clamp. A rect
    /// saved under a DIFFERENT canvas size (HUD canvases are match-0: at 21:9 the canvas is
    /// only ~540 units tall vs 720 at 16:9) can restore off-screen; this re-clamps on first
    /// layout and again on every canvas resize, mirroring <see cref="WindowSizer"/>'s
    /// cache-last-parent-size shape. NON-persisting by design: the editor/window can report
    /// TRANSIENT canvas sizes mid-resolution-switch, and persisting a clamp taken against one
    /// of those overwrites the user's saved layout (Play-caught). The saved rect stays the
    /// source of truth; every resize re-derives the display rect from the getters, so a
    /// temporary squeeze self-restores on the next valid size.
    /// </summary>
    public sealed class KeepOnCanvas : MonoBehaviour
    {
        public Canvas Canvas = null!;
        /// <summary>The persisted/authored position (the drag path keeps it current).</summary>
        public Func<Vector2> DesiredPos = () => Vector2.zero;
        /// <summary>The intended size incl. collapsed state; null = leave size alone. Size
        /// applies BEFORE position (a rect larger than the canvas would defeat the clamp).</summary>
        public Func<Vector2>? DesiredSize;
        /// <summary>Bottom band this panel may not enter — the NavBar's strip. Both the size clamp
        /// and the position clamp honour it, so a panel too tall for the remaining room shrinks to
        /// fit ABOVE the bar instead of sliding under it.</summary>
        public float BottomInset;
        private Vector2 _lastCanvas = new(-1f, -1f);

        private void OnEnable() => _lastCanvas = new(-1f, -1f); // re-clamp on re-enable
        private void Update() => Apply();

        private void Apply()
        {
            if (Canvas == null) return;
            var cs = ((RectTransform)Canvas.transform).rect.size;
            if (cs.x <= 0f || cs.y <= 0f || cs == _lastCanvas) return; // not laid out yet / unchanged
            _lastCanvas = cs;

            var rt = (RectTransform)transform;
            const float m = 8f; // the margin ClampToCanvas keeps — size honours the same inset
            if (DesiredSize != null)
                rt.sizeDelta = Vector2.Min(DesiredSize(), cs - new Vector2(m * 2f, m * 2f + BottomInset));
            rt.anchoredPosition = UiKit.ClampToCanvas(DesiredPos(), rt, Canvas, BottomInset);
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

    /// <summary>A bottom-edge fade that says "there is more below". It rides inside a scroll's
    /// viewport, never raycasts, and drives ONLY its own alpha — hit-testing and layout are
    /// byte-identical with or without it (the DropShadow contract).
    ///
    /// It earns its keep because a scroll whose content happens to end flush with the viewport
    /// edge is indistinguishable from a list that has ended: the Settings window seated exactly
    /// through Text Size at 720p and hid Reduced Motion, Haptics, Render Scale, Shadows and Post
    /// FX behind a boundary with no tell at all. Alpha tracks scroll position, so it fades out as
    /// you reach the bottom and never shows on a list that fits.</summary>
    public sealed class ScrollFade : MonoBehaviour
    {
        private const float BandH = 26f;    // fade band height in canvas units
        private const float MaxAlpha = 0.7f;

        private ScrollRect _scroll = null!;
        private Image _img = null!;

        /// <summary>Adds the band to <paramref name="scroll"/>'s viewport. Call after the
        /// ScrollRect has its viewport and content wired.</summary>
        public static void Attach(ScrollRect scroll)
        {
            var go = new GameObject("Fade", typeof(RectTransform));
            go.transform.SetParent(scroll.viewport, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);   // pin across the bottom edge
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, BandH);

            var img = go.AddComponent<Image>();
            img.sprite = FadeSprite();
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f); // sprite carries the black ramp; colour drives alpha

            var f = go.AddComponent<ScrollFade>();
            f._scroll = scroll;
            f._img = img;
        }

        private void LateUpdate()
        {
            if (_scroll == null || _scroll.content == null || _scroll.viewport == null) return;
            float overflow = _scroll.content.rect.height - _scroll.viewport.rect.height;
            // verticalNormalizedPosition is 1 at the TOP and 0 at the BOTTOM, so it doubles as
            // "how much is still below" — the band simply fades out as the list runs out.
            float a = overflow > 1f ? Mathf.Clamp01(_scroll.verticalNormalizedPosition) * MaxAlpha : 0f;
            var c = _img.color;
            if (!Mathf.Approximately(c.a, a)) { c.a = a; _img.color = c; }
        }

        private static Sprite? _fade;

        /// <summary>A 1×64 vertical alpha ramp — clear at the top, black at the bottom. Squared so
        /// the falloff is gentle where it meets the content and only firms up at the very edge.
        /// Cached like the kit's other procedural sprites; no asset files.</summary>
        private static Sprite FadeSprite()
        {
            if (_fade != null) return _fade;
            const int h = 64;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < h; y++)
            {
                // Row 0 is the BOTTOM in texture space, which is where the band is most opaque.
                float k = 1f - (float)y / (h - 1);
                tex.SetPixel(0, y, new Color(0f, 0f, 0f, k * k));
            }
            tex.Apply();
            _fade = Sprite.Create(tex, new Rect(0f, 0f, 1f, h), new Vector2(0.5f, 0.5f),
                                  100f, 0, SpriteMeshType.FullRect);
            return _fade;
        }
    }
}
