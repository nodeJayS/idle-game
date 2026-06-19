#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The shared account bag (uGUI). Lists every owned item (rarity-colored) and shows
    /// the selected item's details. Equipping is NOT done here — that's the per-hero
    /// Equipment HUD (<see cref="EquipmentView"/>); this window is purely the bag the whole
    /// roster draws from. Read-only w.r.t. game rules. (Salvage UI lands later.)
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;          // the open inventory canvas (null when closed)
        private string? _selectedItemId;

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
        }

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
            var title = UiKit.Label(panel.transform, $"Inventory   {loose}/{cap}", 30, TextAnchor.MiddleLeft,
                                    new Vector2(420, 40), new Vector2(-220, 270));
            if (loose > cap) title.color = new Color(1f, 0.6f, 0.4f); // overfilled (idle/boss spillover)
            UiKit.TextButton(panel.transform, "Close", new Vector2(200, 64), new Vector2(330, 270), Close);

            // left: item list (the shared bag)
            var content = UiKit.ScrollColumn(panel.transform, new Vector2(440, 500), new Vector2(-230, -20));
            if (save.Inventory.Count == 0)
                UiKit.Label(content, "No items yet — go win some loot!", 18, TextAnchor.MiddleCenter,
                            new Vector2(0, 0), Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            foreach (var item in save.Inventory)
            {
                var it = item; // capture
                ItemRow(content, save, it, () => { _selectedItemId = it.Id; Rebuild(); });
            }

            // right: item details
            BuildDetails(panel.transform, save, new Vector2(240, -20));
        }

        private void BuildDetails(Transform parent, SaveState save, Vector2 pos)
        {
            var box = UiKit.Panel(parent, new Vector2(400, 500), new Color(0.07f, 0.07f, 0.10f, 1f), pos);

            var selected = _selectedItemId != null ? save.Inventory.Find(i => i.Id == _selectedItemId) : null;
            if (selected == null)
            {
                UiKit.Label(box.transform, "Select an item", 16, TextAnchor.MiddleCenter, new Vector2(340, 30), Vector2.zero);
                return;
            }

            float y = 200f;
            UiKit.Label(box.transform, $"{selected.Rarity} {selected.BaseId}", 18, TextAnchor.MiddleLeft,
                        new Vector2(330, 26), new Vector2(0, y)).color = Palette.Rarity(selected.Rarity);
            y -= 28f;
            UiKit.Label(box.transform, $"Item level {selected.ItemLevel}", 13, TextAnchor.MiddleLeft,
                        new Vector2(330, 22), new Vector2(0, y));
            y -= 26f;
            foreach (var a in selected.Affixes)
            {
                UiKit.Label(box.transform, $"+{StatVal(a.Stat, a.Value)} {a.Stat}", 13, TextAnchor.MiddleLeft,
                            new Vector2(330, 20), new Vector2(0, y));
                y -= 22f;
            }

            var owner = EquippedByWhom(save, selected.Id);
            if (owner != null)
            {
                y -= 8f;
                UiKit.Label(box.transform, $"Equipped by {HeroName(save, owner)}", 14, TextAnchor.MiddleLeft,
                            new Vector2(330, 22), new Vector2(0, y)).color = new Color(0.6f, 0.8f, 1f);
            }
        }

        // ---- rows + helpers ----

        private void ItemRow(Transform parent, SaveState save, Item it, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = it.Id == _selectedItemId ? new Color(0.22f, 0.26f, 0.34f) : new Color(0.15f, 0.16f, 0.20f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            go.AddComponent<LayoutElement>().preferredHeight = 56;

            bool equipped = EquippedByWhom(save, it.Id) != null;
            var label = UiKit.Label(go.transform, $"{it.Rarity} {it.BaseId} (i{it.ItemLevel}){(equipped ? "  [E]" : "")}",
                                    20, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16, 0); lrt.offsetMax = new Vector2(-16, 0);
            label.color = Palette.Rarity(it.Rarity);
        }

        private static string StatVal(StatKey k, double v)
        {
            // rate/chance stats are fractional; size stats read as integers
            bool fractional = k == StatKey.Spd || k == StatKey.CritChance || k == StatKey.CritDmg;
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
