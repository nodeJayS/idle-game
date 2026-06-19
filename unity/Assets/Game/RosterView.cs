#nullable enable
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Roster screen (uGUI): field/bench the party and gear ANY owned hero, including
    /// benched ones (the per-hero Equipment doll is otherwise party-only). The 4 party
    /// slots sit up top; below is a scrolling list of every owned hero (built to scale
    /// toward a large roster). All party edits go through the pure <see cref="Party"/>
    /// reducers via <see cref="CombatView.ReplaceSave"/>; "Gear" reuses the shared
    /// <see cref="EquipmentView"/>. Party changes persist and take effect on the next run.
    /// </summary>
    public sealed class RosterView : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;
        private EquipmentView? _equipment;
        private GameObject? _panel;

        /// <summary>True while the roster panel is open (the HUD reads this to hide IMGUI).</summary>
        public bool IsOpen => _panel != null;

        public void Bind(CombatView view, GameConfig cfg, EquipmentView equipment)
        {
            _view = view;
            _cfg = cfg;
            _equipment = equipment;
        }

        public void Toggle() { if (_panel != null) Close(); else Open(); }

        private void Close()
        {
            if (_panel != null) Destroy(_panel);
            _panel = null;
        }

        /// <summary>Reopen on the current save (after a field/bench mutates it).</summary>
        private void Rebuild() { Close(); Open(); }

        private void Open()
        {
            var save = _view.CurrentSave;

            var canvas = UiKit.CreateCanvas("RosterCanvas", transform, sortOrder: 95);
            _panel = canvas.gameObject;
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(920, 640), new Color(0.10f, 0.10f, 0.14f, 1f));

            int fielded = 0;
            foreach (var id in save.Party) if (id != null) fielded++;
            UiKit.Label(panel.transform, $"Roster  ·  party {fielded}/{save.Party.Length}", 28, TextAnchor.MiddleLeft,
                        new Vector2(420, 40), new Vector2(-230, 274));
            UiKit.TextButton(panel.transform, "Close", new Vector2(150, 52), new Vector2(375, 274), Close, 22);

            BuildPartySlots(panel.transform, save);

            // owned-hero list — one wide card per row, scrollable so it scales to many heroes.
            var grid = UiKit.ScrollGrid(panel.transform, new Vector2(860, 420), new Vector2(0, -78), new Vector2(820, 70));
            foreach (var hero in save.Heroes)
                BuildHeroCard(grid, save, hero);
        }

        private void BuildPartySlots(Transform parent, SaveState save)
        {
            float[] xs = { -315f, -105f, 105f, 315f };
            int n = Mathf.Min(save.Party.Length, xs.Length);
            for (int i = 0; i < n; i++)
            {
                int slot = i;
                string? heroId = save.Party[i];
                bool filled = heroId != null;

                var chip = UiKit.Panel(parent, new Vector2(196, 56),
                    filled ? new Color(0.16f, 0.22f, 0.30f) : new Color(0.12f, 0.13f, 0.16f), new Vector2(xs[i], 208));
                UiKit.Label(chip.transform, filled ? $"{i + 1}.  {HeroName(save, heroId!)}" : $"{i + 1}.  — empty —",
                            15, TextAnchor.MiddleCenter, new Vector2(190, 26), new Vector2(0, 9));
                if (filled)
                    UiKit.TextButton(chip.transform, "Bench", new Vector2(92, 24), new Vector2(0, -15),
                        () => { _view.ReplaceSave(Party.SetPartySlot(save, slot, null)); Rebuild(); }, 13);
            }
        }

        private void BuildHeroCard(Transform grid, SaveState save, HeroInstance hero)
        {
            int slot = System.Array.IndexOf(save.Party, hero.Id);
            bool fielded = slot >= 0;

            var card = new GameObject("HeroCard", typeof(RectTransform));
            card.transform.SetParent(grid, false);
            card.AddComponent<Image>().color = fielded ? new Color(0.15f, 0.19f, 0.16f) : new Color(0.16f, 0.16f, 0.20f);

            string cls = _cfg.Heroes.TryGetValue(hero.DefId, out var def) && !string.IsNullOrEmpty(def.Class)
                ? def.Class : hero.DefId;

            UiKit.Label(card.transform, HeroName(save, hero.Id), 18, TextAnchor.MiddleLeft,
                        new Vector2(280, 24), new Vector2(-250, 13));
            UiKit.Label(card.transform, $"{cls}  ·  Lv {hero.Level}", 13, TextAnchor.MiddleLeft,
                        new Vector2(280, 20), new Vector2(-250, -13)).color = new Color(0.70f, 0.74f, 0.80f);

            var status = UiKit.Label(card.transform, fielded ? $"Fielded · slot {slot + 1}" : "Benched", 14,
                                     TextAnchor.MiddleCenter, new Vector2(170, 22), new Vector2(55, 0));
            status.color = fielded ? new Color(0.55f, 0.85f, 0.60f) : new Color(0.70f, 0.70f, 0.75f);

            if (fielded)
            {
                UiKit.TextButton(card.transform, "Bench", new Vector2(120, 46), new Vector2(230, 0),
                    () => { _view.ReplaceSave(Party.SetPartySlot(save, slot, null)); Rebuild(); }, 16);
            }
            else
            {
                int firstEmpty = System.Array.IndexOf(save.Party, (string?)null);
                if (firstEmpty >= 0)
                {
                    UiKit.TextButton(card.transform, "Field", new Vector2(120, 46), new Vector2(230, 0),
                        () => { _view.ReplaceSave(Party.FieldHero(save, firstEmpty, hero.Id)); Rebuild(); }, 16);
                }
                else
                {
                    // party full — show a disabled Field so it's clear why nothing happens
                    var b = UiKit.TextButton(card.transform, "Field", new Vector2(120, 46), new Vector2(230, 0), () => { }, 16);
                    b.interactable = false;
                    b.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.28f);
                }
            }

            UiKit.TextButton(card.transform, "Gear", new Vector2(110, 46), new Vector2(352, 0),
                () => _equipment?.Toggle(hero.Id), 16);
        }

        private string HeroName(SaveState save, string heroId)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero != null && _cfg.Heroes.TryGetValue(hero.DefId, out var def) && !string.IsNullOrEmpty(def.Name))
                return def.Name;
            return heroId;
        }
    }
}
