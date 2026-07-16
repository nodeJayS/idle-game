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
            // Corner-anchored HUD: build under the SafeRoot so the chip insets from device notches.
            // On desktop SafeRoot == the canvas rect, so the top-left anchoring is byte-identical.
            var root = UiKit.SafeRoot(canvas);

            // Screen-edge insets come from Theme.HudPad; the name label's (84,-30) and the
            // Settings button's -80 are INTERNAL offsets (beside/below the 56px avatar), kept
            // literal — deriving them from the pad would only obscure the chip's layout.
            var circle = UiKit.Circle(root, 56f, AvatarColor(Account.Name), Vector2.zero);
            Anchor(circle.rectTransform, new Vector2(Theme.HudPad, -Theme.HudPad));
            var initials = UiKit.Label(circle.transform, Initials(Account.Name), 24, TextAnchor.MiddleCenter,
                                       new Vector2(56, 56), Vector2.zero);
            initials.color = new Color(0.1f, 0.1f, 0.12f);

            _nameLabel = UiKit.Label(root, Account.Display, 20, TextAnchor.MiddleLeft,
                                     new Vector2(300, 30), Vector2.zero);
            Anchor((RectTransform)_nameLabel.transform, new Vector2(84, -30));

            // 10.8e: the account chip carries the endless depth record (the Phase-C leaderboard
            // seam). Hidden (empty) until the first endless clear; Update() polls change-only.
            _recordLabel = UiKit.Label(root, "", 15, TextAnchor.MiddleLeft,
                                       new Vector2(300, 22), Vector2.zero);
            _recordLabel.color = new Color(1f, 0.82f, 0.32f, 0.95f);
            Anchor((RectTransform)_recordLabel.transform, new Vector2(84, -54));

            var gear = UiKit.TextButton(root, "Settings", new Vector2(104, 34), Vector2.zero, ToggleSettings, 18);
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
            // PanelKit.Window (sortOrder 120, above everything): header (title + Close) + a scrolling
            // body of rows, with the Main Menu / Exit / Close verbs pinned below the scroll. onClose is
            // ToggleSettings so the header Close obeys the same open/close toggle the gear button drives.
            var winGo = PanelKit.Window(transform, "Settings", ToggleSettings, out var body,
                                        "SettingsCanvas", sortOrder: 120, max: new Vector2(620f, 680f));
            _settings = winGo; // Window returns the canvas GO — ToggleSettings destroys it to close
            PanelKit.Stack(body); // the body is a bare Flex until stacked — without this every row
                                  // collapses to a centered zero-size rect (Play-caught 10.13d)

            // The row count (name + 3 sliders + 5 fx toggles + 3 a11y rows + 3 quality rows) overflows the
            // phone-canvas window height, so the rows scroll; the verb row stays fixed at the panel bottom.
            var list = UiKit.ScrollColumnFill(body, spacing: Theme.GapS);

            // Name: label + a rename field wrapped in a flexible layout cell (a LayoutElement, the way
            // ButtonCell wraps its button). Commit semantics unchanged — on end-edit, set Account.Name
            // and refresh the HUD chip's display label.
            var nameRow = PanelKit.Row(list, Theme.BtnH);
            PanelKit.TextCell(nameRow, "Name", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft, width: 120f);
            var input = UiKit.TextInput(nameRow, Account.Name, new Vector2(280f, 44f), Vector2.zero,
                s => { Account.Name = s; _nameLabel.text = Account.Display; });
            var inLe = input.gameObject.AddComponent<LayoutElement>();
            inLe.flexibleWidth = 1f; inLe.minWidth = 200f;

            PanelKit.SliderRow(list, "Master Volume", () => Settings.MasterVolume,
                v => { Settings.MasterVolume = v; AudioListener.volume = v; });
            PanelKit.SliderRow(list, "SFX Volume", () => Settings.SfxVolume, v => Settings.SfxVolume = v);
            PanelKit.SliderRow(list, "Ambience Volume", () => Settings.AmbienceVolume,
                v => Settings.AmbienceVolume = v); // 10.9c beds read this live

            ToggleRow(list, "Damage Numbers", () => Settings.DamageNumbers, v => Settings.DamageNumbers = v);
            ToggleRow(list, "Screen Shake", () => Settings.ScreenShake, v => Settings.ScreenShake = v);
            ToggleRow(list, "Loot Feed", () => Settings.LootFeed, v => Settings.LootFeed = v);
            ToggleRow(list, "Projectiles", () => Settings.Projectiles, v => Settings.Projectiles = v);
            ToggleRow(list, "Spawn Animations", () => Settings.SpawnAnimations, v => Settings.SpawnAnimations = v);

            // Accessibility (10.20a). Text Size cycles the three hand-checked steps; on each advance we
            // close+re-open Settings so THIS window's own labels re-render at the new scale on the spot
            // (uGUI windows read UiKit.Scaled only at build time — see §3 live-refresh notes). Reduced
            // Motion / Haptics are plain toggles over the new prefs.
            CycleRow(list, "Text Size", () => Settings.TextSizePct + "%",
                () =>
                {
                    Settings.TextSizePct = Settings.TextSizePct == 100 ? 115
                                         : Settings.TextSizePct == 115 ? 130 : 100;
                    Destroy(_settings); _settings = null; OpenSettings();
                });
            ToggleRow(list, "Reduced Motion", () => Settings.ReducedMotion, v => Settings.ReducedMotion = v);
            ToggleRow(list, "Haptics", () => Settings.Haptics, v => Settings.Haptics = v);

            // Quality tier (10.12d): the weak-hardware levers, applied live via GraphicsQuality.
            CycleRow(list, "Render Scale",
                () => Settings.RenderScale >= 0.99f ? "100%" : Settings.RenderScale >= 0.74f ? "75%" : "60%",
                () =>
                {
                    Settings.RenderScale = Settings.RenderScale >= 0.99f ? 0.75f
                                         : Settings.RenderScale >= 0.74f ? 0.6f : 1f;
                    GraphicsQuality.Apply();
                });
            ToggleRow(list, "Shadows", () => Settings.Shadows,
                v => { Settings.Shadows = v; GraphicsQuality.Apply(); });
            ToggleRow(list, "Post FX", () => Settings.PostFx,
                v => { Settings.PostFx = v; GraphicsQuality.Apply(); });

            // Verb row (fixed, below the scroll): three ≥48 buttons.
            var verbs = PanelKit.Row(body, Theme.BtnH);
            PanelKit.ButtonCell(verbs, "Main Menu",
                () => { Destroy(_settings); _settings = null; _onMainMenu(); }, fontSize: Theme.FsBody);
            PanelKit.ButtonCell(verbs, "Exit Game", Quit, fontSize: Theme.FsBody);
            PanelKit.ButtonCell(verbs, "Close", ToggleSettings, fontSize: Theme.FsBody);
        }

        /// <summary>A labelled cycle button (tap advances to the next option), composed from the kit —
        /// for small enumerated settings (render scale steps). Rides the 44 touch floor.</summary>
        private void CycleRow(RectTransform parent, string label, Func<string> current, Action advance)
        {
            var row = PanelKit.Row(parent, Theme.TouchMin);
            PanelKit.TextCell(row, label, Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft, flex: 1f);
            Text? t = null;
            var btn = PanelKit.ButtonCell(row, current(),
                () => { advance(); if (t != null) t.text = current(); }, width: 120f, fontSize: Theme.FsBody);
            t = btn.GetComponentInChildren<Text>();
        }

        /// <summary>A labelled On/Off toggle button, composed from the kit. Rides the 44 touch floor.</summary>
        private void ToggleRow(RectTransform parent, string label, Func<bool> get, Action<bool> set)
        {
            var row = PanelKit.Row(parent, Theme.TouchMin);
            PanelKit.TextCell(row, label, Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft, flex: 1f);
            Text? t = null;
            var btn = PanelKit.ButtonCell(row, get() ? "On" : "Off",
                () => { set(!get()); if (t != null) t.text = get() ? "On" : "Off"; }, width: 120f, fontSize: Theme.FsBody);
            t = btn.GetComponentInChildren<Text>();
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
