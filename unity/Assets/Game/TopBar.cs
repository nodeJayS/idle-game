#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace IdleGame.Game
{
    /// <summary>
    /// Top-left HUD: the account chip (circle avatar + Name#1234) and a Settings button
    /// that opens an in-game panel with per-effect toggles, a rename field, and
    /// Main Menu / Exit. Read-only over game state; it only flips Settings/Account
    /// prefs and invokes the supplied callbacks.
    /// </summary>
    public sealed class TopBar : MonoBehaviour
    {
        private Action _onMainMenu = () => { };
        private GameObject? _settings;
        private Text _nameLabel = null!;
        private CombatView? _view;
        private Text? _recordLabel;
        private int _recordShown = -1;

        public void Bind(CombatView view, Action onMainMenu) { _view = view; _onMainMenu = onMainMenu; }

        public void Open()
        {
            var canvas = UiKit.CreateCanvas("TopBarCanvas", transform, sortOrder: 85);

            // Screen-edge insets come from Theme.HudPad; the name label's (84,-30) and the
            // Settings button's -80 are INTERNAL offsets (beside/below the 56px avatar), kept
            // literal — deriving them from the pad would only obscure the chip's layout.
            var circle = UiKit.Circle(canvas.transform, 56f, AvatarColor(Account.Name), Vector2.zero);
            Anchor(circle.rectTransform, new Vector2(Theme.HudPad, -Theme.HudPad));
            var initials = UiKit.Label(circle.transform, Initials(Account.Name), 24, TextAnchor.MiddleCenter,
                                       new Vector2(56, 56), Vector2.zero);
            initials.color = new Color(0.1f, 0.1f, 0.12f);

            _nameLabel = UiKit.Label(canvas.transform, Account.Display, 20, TextAnchor.MiddleLeft,
                                     new Vector2(300, 30), Vector2.zero);
            Anchor((RectTransform)_nameLabel.transform, new Vector2(84, -30));

            // 10.8e: the account chip carries the endless depth record (the Phase-C leaderboard
            // seam). Hidden (empty) until the first endless clear; Update() polls change-only.
            _recordLabel = UiKit.Label(canvas.transform, "", 15, TextAnchor.MiddleLeft,
                                       new Vector2(300, 22), Vector2.zero);
            _recordLabel.color = new Color(1f, 0.82f, 0.32f, 0.95f);
            Anchor((RectTransform)_recordLabel.transform, new Vector2(84, -54));

            var gear = UiKit.TextButton(canvas.transform, "Settings", new Vector2(104, 34), Vector2.zero, ToggleSettings, 18);
            Anchor((RectTransform)gear.transform, new Vector2(Theme.HudPad, -80));
        }

        // Change-only poll: the record label re-interpolates its string ONLY when EndlessBest
        // moves (the _stageLabel pattern — the steady-state HUD must allocate nothing).
        private void Update()
        {
            if (_recordLabel == null || _view == null) return;
            int r = _view.EndlessRecord;
            if (r == _recordShown) return;
            _recordShown = r;
            _recordLabel.text = r > 0 ? $"Endless depth {r}" : "";
        }

        private static void Anchor(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); // top-left
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
        }

        private void ToggleSettings()
        {
            if (_settings != null) { Destroy(_settings); _settings = null; return; }
            OpenSettings();
        }

        private void OpenSettings()
        {
            var canvas = UiKit.CreateCanvas("SettingsCanvas", transform, sortOrder: 120);
            _settings = canvas.gameObject;
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(560, 720), new Color(0.10f, 0.10f, 0.14f, 1f));
            UiKit.Label(panel.transform, "Settings", 30, TextAnchor.MiddleCenter, new Vector2(520, 42), new Vector2(0, 310));

            UiKit.Label(panel.transform, "Name", 22, TextAnchor.MiddleLeft, new Vector2(120, 44), new Vector2(-200, 248));
            UiKit.TextInput(panel.transform, Account.Name, new Vector2(280, 56), new Vector2(70, 248),
                s => { Account.Name = s; _nameLabel.text = Account.Display; });

            float y = 184f;
            SliderRow(panel.transform, "Master Volume", () => Settings.MasterVolume,
                v => { Settings.MasterVolume = v; AudioListener.volume = v; }, ref y);
            SliderRow(panel.transform, "SFX Volume", () => Settings.SfxVolume,
                v => Settings.SfxVolume = v, ref y);

            y -= 8f; // small gap before the effect toggles
            ToggleRow(panel.transform, "Damage Numbers", () => Settings.DamageNumbers, v => Settings.DamageNumbers = v, ref y);
            ToggleRow(panel.transform, "Screen Shake", () => Settings.ScreenShake, v => Settings.ScreenShake = v, ref y);
            ToggleRow(panel.transform, "Loot Feed", () => Settings.LootFeed, v => Settings.LootFeed = v, ref y);
            ToggleRow(panel.transform, "Projectiles", () => Settings.Projectiles, v => Settings.Projectiles = v, ref y);
            ToggleRow(panel.transform, "Spawn Animations", () => Settings.SpawnAnimations, v => Settings.SpawnAnimations = v, ref y);

            UiKit.TextButton(panel.transform, "Main Menu", new Vector2(240, 60), new Vector2(-140, -210),
                () => { Destroy(_settings); _settings = null; _onMainMenu(); });
            UiKit.TextButton(panel.transform, "Exit Game", new Vector2(240, 60), new Vector2(140, -210), Quit);
            UiKit.TextButton(panel.transform, "Close", new Vector2(220, 56), new Vector2(0, -290), ToggleSettings);
        }

        private void ToggleRow(Transform parent, string label, Func<bool> get, Action<bool> set, ref float y)
        {
            UiKit.Label(parent, label, 22, TextAnchor.MiddleLeft, new Vector2(320, 44), new Vector2(-90, y));
            Text? t = null;
            var btn = UiKit.TextButton(parent, get() ? "On" : "Off", new Vector2(120, 44), new Vector2(180, y),
                () => { set(!get()); if (t != null) t.text = get() ? "On" : "Off"; });
            t = btn.GetComponentInChildren<Text>();
            y -= 52f;
        }

        /// <summary>A labelled 0..1 volume slider row, laid out like <see cref="ToggleRow"/>: label
        /// on the left, the control (here a slider + live % readout) on the right. Applies live via
        /// <paramref name="set"/> on every drag and persists through the Settings property.</summary>
        private void SliderRow(Transform parent, string label, Func<float> get, Action<float> set, ref float y)
        {
            UiKit.Label(parent, label, 22, TextAnchor.MiddleLeft, new Vector2(320, 44), new Vector2(-90, y));

            float start = Mathf.Clamp01(get());
            Text? pct = null;

            // Track (background)
            var trackGo = new GameObject("Slider", typeof(RectTransform));
            trackGo.transform.SetParent(parent, false);
            var trackImg = trackGo.AddComponent<Image>();
            trackImg.color = new Color(0.18f, 0.20f, 0.26f);
            var trackRt = (RectTransform)trackGo.transform;
            trackRt.anchorMin = trackRt.anchorMax = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta = new Vector2(160, 16);
            trackRt.anchoredPosition = new Vector2(150, y);

            var slider = trackGo.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            // Half the handle width — the Fill Area and Handle Slide Area inset by this so the
            // round handle stays inside the track ends and the fill doesn't overshoot (mirrors
            // Unity's default slider prefab layout; without it stray fill/handle slivers render).
            const float pad = 13f;

            // Fill
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(trackGo.transform, false);
            var fillAreaRt = (RectTransform)fillArea.transform;
            fillAreaRt.anchorMin = new Vector2(0f, 0f);
            fillAreaRt.anchorMax = new Vector2(1f, 1f);
            fillAreaRt.offsetMin = new Vector2(pad, 0f);
            fillAreaRt.offsetMax = new Vector2(-pad, 0f);
            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = new Color(0.35f, 0.55f, 0.85f);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            fillRt.sizeDelta = new Vector2(0f, 0f);

            // Handle
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(trackGo.transform, false);
            var handleAreaRt = (RectTransform)handleArea.transform;
            handleAreaRt.anchorMin = new Vector2(0f, 0f);
            handleAreaRt.anchorMax = new Vector2(1f, 1f);
            handleAreaRt.offsetMin = new Vector2(pad, 0f);
            handleAreaRt.offsetMax = new Vector2(-pad, 0f);
            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(handleArea.transform, false);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.sprite = UiKit.CircleSprite();
            handleImg.color = new Color(0.85f, 0.88f, 0.95f);
            var handleRt = (RectTransform)handleGo.transform;
            handleRt.sizeDelta = new Vector2(26, 26);

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = start;

            pct = UiKit.Label(parent, Mathf.RoundToInt(start * 100f) + "%", 20,
                TextAnchor.MiddleLeft, new Vector2(70, 44), new Vector2(244, y));

            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                if (pct != null) pct.text = Mathf.RoundToInt(v * 100f) + "%";
            });

            y -= 52f;
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static Color AvatarColor(string name)
        {
            var rnd = new System.Random(name.GetHashCode());
            return Color.HSVToRGB((float)rnd.NextDouble(), 0.55f, 0.85f);
        }

        private static string Initials(string name)
        {
            name = name.Trim();
            return name.Length == 0 ? "?" : name.Substring(0, 1).ToUpperInvariant();
        }
    }
}
