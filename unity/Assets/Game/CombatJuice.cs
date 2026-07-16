#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Renderer-only combat feedback: floating damage/heal numbers. Driven by
    /// <see cref="CombatEvent"/>s that CombatView already receives — no sim changes,
    /// determinism untouched. Owns its own constant-pixel overlay canvas (1:1 screen
    /// mapping) sorted below the menus/modals. (Camera follow/zoom/shake lives in
    /// <see cref="CameraRig"/>; loot goes to the chat feed.)
    /// </summary>
    public sealed class CombatJuice : MonoBehaviour
    {
        private Camera _cam = null!;
        private Canvas _canvas = null!;

        public void Init(Camera cam)
        {
            _cam = cam;

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
            // Crit reads from color (gold) + bigger font/pop, not punctuation.
            var label = NewText(Num.Compact(amount), crit ? 30 : 20, color);
            var jitter = new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
            label.gameObject.AddComponent<FloatingText>()
                 .Configure(label, _cam, worldHead + jitter, crit ? 1.8f : 1.3f, crit ? 1.0f : 0.8f, color);
        }

        /// <summary>A rarity-colored item name that pops at a world point when a keeper drops —
        /// the standout moment in the loot rain (commons just go to the feed / auto-salvage).</summary>
        public void LootPop(Vector3 worldPos, string text, Rarity rarity)
        {
            var color = Palette.Rarity(rarity);
            var label = NewText(text, rarity >= Rarity.Unique ? 26 : 22, color);
            label.gameObject.AddComponent<FloatingText>()
                 .Configure(label, _cam, worldPos, 2.0f, 1.5f, color); // rises higher, lingers
        }

        /// <summary>A "LEVEL UP!" burst above a hero when the party gains a level.</summary>
        public void LevelUpBurst(Vector3 worldPos)
        {
            var color = new Color(0.55f, 0.85f, 1f);
            var label = NewText("LEVEL UP!", 26, color);
            label.gameObject.AddComponent<FloatingText>()
                 .Configure(label, _cam, worldPos, 1.6f, 1.2f, color);
        }

        /// <summary>Green "+N" floating above a healed ally (M11 mend skill).</summary>
        public void HealNumber(Vector3 worldHead, double amount)
        {
            var color = new Color(0.45f, 1f, 0.5f);
            var label = NewText($"+{Num.Compact(amount)}", 20, color);
            var jitter = new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f));
            label.gameObject.AddComponent<FloatingText>()
                 .Configure(label, _cam, worldHead + jitter, 1.4f, 0.9f, color);
        }

        // ---- factory helpers ----

        private Text NewText(string s, int fontSize, Color color)
        {
            var go = new GameObject("FloatText", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.text = s;
            t.fontSize = UiKit.Scaled(fontSize); // a11y text scale — this factory bypasses UiKit.Label
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
