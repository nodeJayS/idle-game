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

        public void Bind(CombatView view, Action onMainMenu) => _onMainMenu = onMainMenu;

        public void Open()
        {
            var canvas = UiKit.CreateCanvas("TopBarCanvas", transform, sortOrder: 85);

            var circle = UiKit.Circle(canvas.transform, 56f, AvatarColor(Account.Name), Vector2.zero);
            Anchor(circle.rectTransform, new Vector2(16, -16));
            var initials = UiKit.Label(circle.transform, Initials(Account.Name), 24, TextAnchor.MiddleCenter,
                                       new Vector2(56, 56), Vector2.zero);
            initials.color = new Color(0.1f, 0.1f, 0.12f);

            _nameLabel = UiKit.Label(canvas.transform, Account.Display, 20, TextAnchor.MiddleLeft,
                                     new Vector2(300, 30), Vector2.zero);
            Anchor((RectTransform)_nameLabel.transform, new Vector2(84, -30));

            var gear = UiKit.TextButton(canvas.transform, "Settings", new Vector2(104, 34), Vector2.zero, ToggleSettings, 18);
            Anchor((RectTransform)gear.transform, new Vector2(16, -80));
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

            float y = 180f;
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
