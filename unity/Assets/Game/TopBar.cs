#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

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

            var gear = UiKit.TextButton(root, Loc.T("settings.title"), new Vector2(104, 34), Vector2.zero, ToggleSettings, 18);
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
            _recordLabel.text = r > 0 ? Loc.F("hud.endless-depth", r) : "";
        }

        private static void Anchor(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); // top-left
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
        }

        private void ToggleSettings()
        {
            // A real close (the gear button or the header X routed here): ease the window out.
            if (_settings != null) { UiMotion.Dismiss(_settings, animate: true); _settings = null; return; }
            OpenSettings();
        }

        private void OpenSettings()
        {
            // PanelKit.Window (sortOrder 120, above everything): header (title + Close) + a scrolling
            // body of rows, with the Main Menu / Exit / Close verbs pinned below the scroll. onClose is
            // ToggleSettings so the header Close obeys the same open/close toggle the gear button drives.
            var winGo = PanelKit.Window(transform, Loc.T("settings.title"), ToggleSettings, out var body,
                                        "SettingsCanvas", sortOrder: 120, max: new Vector2(620f, 680f));
            _settings = winGo; // Window returns the canvas GO — ToggleSettings destroys it to close
            // Settings is a full-screen window but NOT one of CombatView's bound panels, so
            // AnyPanelOpen never went true for it and the IMGUI HUD — wallet, party frames,
            // floating HP-bar dashes, damage numbers — drew straight over the whole screen
            // (IMGUI repaints after canvas compositing; no sortingOrder can beat it). Ride the
            // launch-modal gate instead, popped on destroy so every close path stays balanced.
            HudSuppressor.Attach(winGo, _view);
            PanelKit.Stack(body); // the body is a bare Flex until stacked — without this every row
                                  // collapses to a centered zero-size rect (Play-caught 10.13d)

            // The row count (name + 3 sliders + 5 fx toggles + 3 a11y rows + 3 quality rows) overflows the
            // phone-canvas window height, so the rows scroll; the verb row stays fixed at the panel bottom.
            var list = UiKit.ScrollColumnFill(body, spacing: Theme.GapS);

            // Name: label + a rename field wrapped in a flexible layout cell (a LayoutElement, the way
            // ButtonCell wraps its button). Commit semantics unchanged — on end-edit, set Account.Name
            // and refresh the HUD chip's display label.
            var nameRow = PanelKit.Row(list, Theme.BtnH);
            PanelKit.TextCell(nameRow, Loc.T("settings.name"), Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft, width: 120f);
            var input = UiKit.TextInput(nameRow, Account.Name, new Vector2(280f, 44f), Vector2.zero,
                s => { Account.Name = s; _nameLabel.text = Account.Display; });
            var inLe = input.gameObject.AddComponent<LayoutElement>();
            inLe.flexibleWidth = 1f; inLe.minWidth = 200f;

            PanelKit.SliderRow(list, Loc.T("settings.master-volume"), () => Settings.MasterVolume,
                v => { Settings.MasterVolume = v; AudioListener.volume = v; });
            PanelKit.SliderRow(list, Loc.T("settings.sfx-volume"), () => Settings.SfxVolume, v => Settings.SfxVolume = v);
            PanelKit.SliderRow(list, Loc.T("settings.ambience-volume"), () => Settings.AmbienceVolume,
                v => Settings.AmbienceVolume = v); // 10.9c beds read this live

            ToggleRow(list, Loc.T("settings.damage-numbers"), () => Settings.DamageNumbers, v => Settings.DamageNumbers = v);
            ToggleRow(list, Loc.T("settings.screen-shake"), () => Settings.ScreenShake, v => Settings.ScreenShake = v);
            ToggleRow(list, Loc.T("settings.loot-feed"), () => Settings.LootFeed, v => Settings.LootFeed = v);
            ToggleRow(list, Loc.T("settings.projectiles"), () => Settings.Projectiles, v => Settings.Projectiles = v);
            ToggleRow(list, Loc.T("settings.spawn-animations"), () => Settings.SpawnAnimations, v => Settings.SpawnAnimations = v);

            // Accessibility (10.20a). Text Size cycles the three hand-checked steps; on each advance we
            // close+re-open Settings so THIS window's own labels re-render at the new scale on the spot
            // (uGUI windows read UiKit.Scaled only at build time — see §3 live-refresh notes). Reduced
            // Motion / Haptics are plain toggles over the new prefs.
            CycleRow(list, Loc.T("settings.text-size"), () => Settings.TextSizePct + "%",
                () =>
                {
                    Settings.TextSizePct = Settings.TextSizePct == 100 ? 115
                                         : Settings.TextSizePct == 115 ? 130 : 100;
                    // A REDRAW at the new text size, not a close — instant teardown, no exit motion.
                    UiMotion.Dismiss(_settings, animate: false); _settings = null; OpenSettings();
                });
            ToggleRow(list, Loc.T("settings.reduced-motion"), () => Settings.ReducedMotion, v => Settings.ReducedMotion = v);
            ToggleRow(list, Loc.T("settings.haptics"), () => Settings.Haptics, v => Settings.Haptics = v);

            // Quality tier (10.12d): the weak-hardware levers, applied live via GraphicsQuality.
            CycleRow(list, Loc.T("settings.render-scale"),
                () => Settings.RenderScale >= 0.99f ? "100%" : Settings.RenderScale >= 0.74f ? "75%" : "60%",
                () =>
                {
                    Settings.RenderScale = Settings.RenderScale >= 0.99f ? 0.75f
                                         : Settings.RenderScale >= 0.74f ? 0.6f : 1f;
                    GraphicsQuality.Apply();
                });
            ToggleRow(list, Loc.T("settings.shadows"), () => Settings.Shadows,
                v => { Settings.Shadows = v; GraphicsQuality.Apply(); });
            ToggleRow(list, Loc.T("settings.post-fx"), () => Settings.PostFx,
                v => { Settings.PostFx = v; GraphicsQuality.Apply(); });

            // Credits. The slot icons are CC BY 3.0, which REQUIRES attribution in the shipped
            // product — the README alone would not do it, since nobody playing the game reads that.
            // Settings is the only surface a player can reach from anywhere, so it lives here.
            PanelKit.Label(list, Loc.T("settings.credits"), Theme.FsTiny, Theme.TextMuted,
                           TextAnchor.MiddleLeft);

            // Verb row (fixed, below the scroll): the two LEAVING verbs. Close used to sit here too,
            // competing with the header Close directly above it — two controls, same job, one screen.
            // The header Close is the kit-wide affordance every other window carries, so it wins.
            var verbs = PanelKit.Row(body, Theme.BtnH);
            PanelKit.ButtonCell(verbs, Loc.T("settings.main-menu"),
                () => { UiMotion.Dismiss(_settings, animate: true); _settings = null; _onMainMenu(); },
                fontSize: Theme.FsBody);
            PanelKit.ButtonCell(verbs, Loc.T("common.exit-game"), Quit, fontSize: Theme.FsBody);
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
            var btn = PanelKit.ButtonCell(row, get() ? Loc.T("common.on") : Loc.T("common.off"),
                () => { set(!get()); if (t != null) t.text = get() ? Loc.T("common.on") : Loc.T("common.off"); }, width: 120f, fontSize: Theme.FsBody);
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

    /// <summary>Holds <see cref="CombatView"/>'s launch-modal gate for as long as this GameObject
    /// lives. The IMGUI HUD draws above EVERY uGUI canvas regardless of sortingOrder, so a
    /// full-screen window that isn't one of the bound panels in AnyPanelOpen has to suppress it
    /// explicitly. Pushing on attach and popping in OnDestroy keeps the count balanced no matter
    /// which close path runs (header Close, the gear toggle, Main Menu, or a rebuild).</summary>
    public sealed class HudSuppressor : MonoBehaviour
    {
        private CombatView? _view;

        public static void Attach(GameObject go, CombatView? view)
        {
            if (view == null) return;
            var s = go.AddComponent<HudSuppressor>();
            s._view = view;
            view.PushLaunchModal();
        }

        private void OnDestroy() => _view?.PopLaunchModal();
    }
}
