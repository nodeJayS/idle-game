#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The shared account bag (uGUI). A grid of rarity-bordered item tiles; hover or click
    /// a tile to see its details on the right. Equipping is NOT done here — that's the
    /// per-hero Equipment HUD (<see cref="EquipmentView"/>); this window is purely the bag
    /// the whole roster draws from. Read-only w.r.t. game rules. (Salvage UI lands later.)
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;        // the open inventory canvas (null when closed)
        private RectTransform? _detail;    // fixed detail pane, updated on hover/click

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
        }

        private void Open()
        {
            var save = _view.CurrentSave;

            var canvas = UiKit.CreateCanvas("InventoryCanvas", transform, sortOrder: 95);
            _panel = canvas.gameObject;
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(920, 640), new Color(0.10f, 0.10f, 0.14f, 1f));

            int loose = Inventory.LooseCount(save);
            int cap = _cfg.Balance.InventoryCap;
            var title = UiKit.Label(panel.transform, $"Inventory   {loose}/{cap}", 30, TextAnchor.MiddleLeft,
                                    new Vector2(420, 40), new Vector2(-220, 270));
            if (loose > cap) title.color = new Color(1f, 0.6f, 0.4f); // overfilled (idle/boss spillover)
            UiKit.TextButton(panel.transform, "Close", new Vector2(200, 64), new Vector2(330, 270), Close);

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
            foreach (var a in item.Affixes)
            {
                UiKit.Label(_detail, $"+{StatVal(a.Stat, a.Value)} {a.Stat}", 13, TextAnchor.MiddleLeft,
                            new Vector2(270, 20), new Vector2(0, y));
                y -= 22f;
            }

            var owner = EquippedByWhom(save, item.Id);
            if (owner != null)
            {
                y -= 8f;
                UiKit.Label(_detail, $"Equipped by {HeroName(save, owner)}", 14, TextAnchor.MiddleLeft,
                            new Vector2(270, 22), new Vector2(0, y)).color = new Color(0.6f, 0.8f, 1f);
            }
        }

        // ---- helpers ----

        private EquipSlot SlotOf(Item item) => _cfg.ItemBases[item.BaseId].Slot;

        private static string StatVal(StatKey k, double v)
        {
            // rate/chance stats are fractional; size stats read as integers
            bool fractional = k == StatKey.MoveSpd || k == StatKey.AtkSpd || k == StatKey.CritChance || k == StatKey.CritDmg;
            return fractional ? v.ToString("0.##") : Mathf.RoundToInt((float)v).ToString();
        }

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
