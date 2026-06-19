#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Renderer-only combat feedback: floating damage numbers, loot toasts, and camera
    /// shake. Driven entirely by <see cref="CombatEvent"/>s that CombatView already
    /// receives — no sim changes, determinism untouched. Owns its own constant-pixel
    /// overlay canvas (1:1 screen mapping) sorted below the menus/modals.
    /// </summary>
    public sealed class CombatJuice : MonoBehaviour
    {
        private Camera _cam = null!;
        private Canvas _canvas = null!;
        private Vector3 _camBase;

        private float _shake;

        private readonly List<(Text text, float born, float life)> _toasts = new();

        public void Init(Camera cam)
        {
            _cam = cam;
            _camBase = cam.transform.position;

            var go = new GameObject("CombatJuiceCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50; // above combat, below claim(90)/menu(100)
            go.AddComponent<CanvasScaler>(); // default constant-pixel: screen px == anchored px
        }

        // ---- public feedback API (called from CombatView.HandleEvents) ----

        public void DamageNumber(Vector3 worldHead, double amount, bool crit)
        {
            var color = crit ? new Color(1f, 0.78f, 0.25f) : new Color(1f, 0.95f, 0.9f);
            var label = NewText(crit ? $"{Num.Compact(amount)}!" : Num.Compact(amount),
                                crit ? 30 : 20, color);
            var jitter = new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
            label.gameObject.AddComponent<FloatingText>()
                 .Configure(label, _cam, worldHead + jitter, crit ? 1.8f : 1.3f, crit ? 1.0f : 0.8f, color);
        }

        public void Toast(string message, Color color)
        {
            var t = NewText(message, 18, color);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            t.alignment = TextAnchor.MiddleRight;
            _toasts.Add((t, Time.time, 2.0f));
        }

        public void Shake(float magnitude) => _shake = Mathf.Max(_shake, magnitude);

        // ---- per-frame upkeep ----

        private void Update()
        {
            // toast stack: newest at top-right, slide down + fade, cull expired
            float y = -24f;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var (text, born, life) = _toasts[i];
                float age = Time.time - born;
                if (text == null || age >= life) { if (text != null) Destroy(text.gameObject); _toasts.RemoveAt(i); continue; }

                var rt = (RectTransform)text.transform;
                rt.anchoredPosition = new Vector2(-16f, y);
                y -= 26f;
                var c = text.color; c.a = 1f - Mathf.Clamp01(age / life); text.color = c;
            }
        }

        private void LateUpdate()
        {
            if (_shake > 0f)
            {
                _shake = Mathf.Max(0f, _shake - Time.deltaTime * 2.5f);
                var off = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * _shake;
                _cam.transform.position = _camBase + off;
            }
            else if (_cam.transform.position != _camBase)
            {
                _cam.transform.position = _camBase;
            }
        }

        // ---- factory helpers ----

        private Text NewText(string s, int fontSize, Color color)
        {
            var go = new GameObject("FloatText", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.text = s;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = color;
            t.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero; // bottom-left: screen px == anchoredPosition
            rt.sizeDelta = new Vector2(200, 40);
            return t;
        }
    }
}
