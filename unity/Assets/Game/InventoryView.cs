#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The shared account bag (uGUI). A grid of rarity-bordered item tiles; hover or click
    /// a tile to see its details on the right. Equipping is NOT done here — that's the
    /// per-hero Equipment HUD (<see cref="EquipmentView"/>); this window is purely the bag
    /// the whole roster draws from. Manual salvage (detail pane) + the auto-salvage
    /// threshold (header) run through the pure <see cref="Inventory"/> reducers and
    /// <see cref="Settings.AutoSalvageMax"/>; game rules stay in GameCore.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;        // the open inventory canvas (null when closed)
        private RectTransform? _detail;    // fixed detail pane, updated on hover/click
        private string? _confirmSalvageId; // two-step confirm guard for Unique/Legendary salvage

        /// <summary>True while the inventory panel is open (the HUD reads this).</summary>
        public bool IsOpen => _panel != null;

        public void Bind(CombatView view, GameConfig cfg)
        {
            _view = view;
            _cfg = cfg;
        }

        /// <summary>Open/close the inventory (driven by the combat HUD's Inventory button).</summary>
        public void Toggle()
        {
            if (_panel != null) { Close(); return; }
            Open();
        }

        private void Close()
        {
            if (_panel != null) Destroy(_panel);
            _panel = null;
            _detail = null;
            _confirmSalvageId = null;
        }

        /// <summary>Reopen on the current save (after a salvage mutates it).</summary>
        private void Rebuild() { Close(); Open(); }

        private void Open()
        {
            var save = _view.CurrentSave;

            var canvas = UiKit.CreateCanvas("InventoryCanvas", transform, sortOrder: 95);
            _panel = canvas.gameObject;
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(920, 640), new Color(0.10f, 0.10f, 0.14f, 1f));

            int loose = Inventory.LooseCount(save);
            int cap = _cfg.Balance.InventoryCap;
            var title = UiKit.Label(panel.transform, $"Inventory  {loose}/{cap}", 24, TextAnchor.MiddleLeft,
                                    new Vector2(200, 40), new Vector2(-330, 274));
            if (loose > cap) title.color = new Color(1f, 0.6f, 0.4f); // overfilled (idle/boss spillover)

            long scrap = save.Currencies.TryGetValue("scrap", out var sc) ? sc : 0;
            UiKit.Label(panel.transform, $"Scrap: {Num.Compact(scrap)}", 16, TextAnchor.MiddleLeft,
                        new Vector2(170, 30), new Vector2(-125, 274)).color = new Color(0.75f, 0.78f, 0.85f);

            // auto-salvage threshold: drops at/below this rarity convert to scrap on pickup.
            var asBtn = UiKit.TextButton(panel.transform, AutoSalvageLabel(Settings.AutoSalvageMax),
                                         new Vector2(300, 46), new Vector2(130, 274), CycleAutoSalvage, 17);
            var asLbl = asBtn.GetComponentInChildren<Text>();
            if (asLbl != null)
                asLbl.color = Settings.AutoSalvageMax == null ? new Color(0.7f, 0.72f, 0.78f)
                                                              : Palette.Rarity(Settings.AutoSalvageMax.Value);

            UiKit.TextButton(panel.transform, "Close", new Vector2(150, 50), new Vector2(375, 274), Close, 22);

            // left: grid of item tiles (the shared bag)
            var grid = UiKit.ScrollGrid(panel.transform, new Vector2(520, 520), new Vector2(-170, -20), new Vector2(76, 76));
            foreach (var item in save.Inventory)
            {
                var it = item; // capture
                var tile = UiKit.ItemTile(grid, new Vector2(76, 76), Vector2.zero, it.Rarity, UiKit.SlotAbbrev(SlotOf(it)), raycast: true);
                var btn = tile.AddComponent<Button>();
                btn.onClick.AddListener(() => ShowDetail(save, it));
                UiKit.Hover(tile, () => ShowDetail(save, it));
            }

            // right: item details
            var box = UiKit.Panel(panel.transform, new Vector2(300, 520), new Color(0.07f, 0.07f, 0.10f, 1f), new Vector2(310, -20));
            _detail = box.rectTransform;
            ShowDetail(save, null); // initial prompt
        }

        private void ShowDetail(SaveState save, Item? item)
        {
            if (_detail == null) return;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);

            // Navigating away from the item awaiting confirmation cancels its confirm.
            if (item?.Id != _confirmSalvageId) _confirmSalvageId = null;

            if (item == null)
            {
                UiKit.Label(_detail, "Hover or click an item.", 15, TextAnchor.MiddleCenter, new Vector2(260, 60), Vector2.zero);
                return;
            }

            float y = 210f;
            UiKit.Label(_detail, $"{item.Rarity} {item.BaseId}", 18, TextAnchor.MiddleLeft,
                        new Vector2(270, 26), new Vector2(0, y)).color = Palette.Rarity(item.Rarity);
            y -= 28f;
            UiKit.Label(_detail, $"{SlotOf(item)} · item level {item.ItemLevel}", 13, TextAnchor.MiddleLeft,
                        new Vector2(270, 22), new Vector2(0, y));
            y -= 26f;
            var affixes = new List<Affix>(item.Affixes);
            affixes.Sort((x, z) => StatDisplay.Rank(x.Stat).CompareTo(StatDisplay.Rank(z.Stat)));
            foreach (var a in affixes)
            {
                UiKit.Label(_detail, $"+{StatDisplay.Value(a.Stat, a.Value)} {StatDisplay.Label(a.Stat)}", 13, TextAnchor.MiddleLeft,
                            new Vector2(270, 20), new Vector2(0, y));
                y -= 22f;
            }

            var owner = EquippedByWhom(save, item.Id);
            if (owner != null)
            {
                // Equipped gear can't be salvaged (the reducer throws) — show the owner instead.
                y -= 8f;
                UiKit.Label(_detail, $"Equipped by {HeroName(save, owner)}", 14, TextAnchor.MiddleLeft,
                            new Vector2(270, 22), new Vector2(0, y)).color = new Color(0.6f, 0.8f, 1f);
                return;
            }

            // Loose item -> manual salvage. Unique/Legendary take a second click to confirm.
            long worth = _cfg.Balance.ScrapValue(item.Rarity, item.ItemLevel);
            if (_confirmSalvageId == item.Id)
            {
                var confirm = UiKit.TextButton(_detail, $"Confirm salvage  +{worth}", new Vector2(260, 46),
                                               new Vector2(0, -186), () => DoSalvage(save, item), 18);
                confirm.GetComponent<Image>().color = new Color(0.62f, 0.22f, 0.22f);
                UiKit.TextButton(_detail, "Cancel", new Vector2(140, 40), new Vector2(0, -234),
                                 () => { _confirmSalvageId = null; ShowDetail(save, item); }, 16);
            }
            else
            {
                UiKit.TextButton(_detail, $"Salvage  +{worth} scrap", new Vector2(260, 46), new Vector2(0, -196),
                    () =>
                    {
                        if (item.Rarity >= Rarity.Unique) { _confirmSalvageId = item.Id; ShowDetail(save, item); }
                        else DoSalvage(save, item);
                    }, 18);
            }
        }

        private void DoSalvage(SaveState save, Item item)
        {
            _confirmSalvageId = null;
            _view.ReplaceSave(Inventory.SalvageItem(save, item.Id, _cfg));
            Rebuild();
        }

        // ---- auto-salvage threshold control ----

        private static string AutoSalvageLabel(Rarity? max) =>
            max == null ? "Auto-salvage: Off" : $"Auto-salvage: ≤ {max.Value}";

        /// <summary>Cycle Off → Normal → Magic → Rare → Off. Unique/Legendary are boss-only
        /// and never auto-salvaged (trash is capped at Rare anyway).</summary>
        private void CycleAutoSalvage()
        {
            Settings.AutoSalvageMax = Settings.AutoSalvageMax switch
            {
                null => Rarity.Normal,
                Rarity.Normal => Rarity.Magic,
                Rarity.Magic => Rarity.Rare,
                _ => (Rarity?)null,
            };
            Rebuild();
        }

        // ---- helpers ----

        private EquipSlot SlotOf(Item item) => _cfg.ItemBases[item.BaseId].Slot;

        private string HeroName(SaveState save, string heroId)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero != null && _cfg.Heroes.TryGetValue(hero.DefId, out var def) && !string.IsNullOrEmpty(def.Name))
                return def.Name;
            return heroId;
        }

        private static string? EquippedByWhom(SaveState save, string itemId)
        {
            foreach (var h in save.Heroes)
                if (h.Equipped.ContainsValue(itemId)) return h.Id;
            return null;
        }
    }
}
