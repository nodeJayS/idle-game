#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Manual inventory + item-compare screen (uGUI). Reads the live save from
    /// <see cref="CombatView"/>, lists owned items (rarity-colored), and on selection
    /// shows the green▲/red▼ stat deltas from the pure <see cref="Inventory.CompareForHero"/>
    /// against a chosen party hero, with Equip/Unequip via the pure reducers. Equip
    /// changes are written back through <see cref="CombatView.ReplaceSave"/> and take
    /// effect on the next run. Read-only w.r.t. game rules.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;          // the open inventory canvas (null when closed)
        private string? _selectedHeroId;
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
            _selectedHeroId ??= FirstPartyHeroId(save);

            var canvas = UiKit.CreateCanvas("InventoryCanvas", transform, sortOrder: 95);
            _panel = canvas.gameObject;
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(820, 540), new Color(0.10f, 0.10f, 0.14f, 1f));
            UiKit.Label(panel.transform, "Inventory", 26, TextAnchor.MiddleLeft, new Vector2(300, 34), new Vector2(-250, 240));
            UiKit.TextButton(panel.transform, "Close", new Vector2(120, 40), new Vector2(330, 240), Close);

            BuildHeroRow(panel.transform, save, new Vector2(-250, 198));

            // left: item list
            var content = UiKit.ScrollColumn(panel.transform, new Vector2(380, 380), new Vector2(-205, -30));
            if (save.Inventory.Count == 0)
                UiKit.Label(content, "No items yet — go win some loot!", 15, TextAnchor.MiddleCenter,
                            new Vector2(0, 0), Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            foreach (var item in save.Inventory)
            {
                var it = item; // capture
                ItemRow(content, it, IsEquippedBy(save, _selectedHeroId, it.Id),
                        () => { _selectedItemId = it.Id; Rebuild(); });
            }

            // right: details + compare
            BuildDetails(panel.transform, save, new Vector2(205, -30));
        }

        private void BuildHeroRow(Transform parent, SaveState save, Vector2 pos)
        {
            float x = pos.x;
            foreach (var id in save.Party)
            {
                if (id == null) continue;
                var heroId = id;
                bool sel = heroId == _selectedHeroId;
                var btn = UiKit.TextButton(parent, heroId, new Vector2(110, 34), new Vector2(x, pos.y),
                    () => { _selectedHeroId = heroId; _selectedItemId = null; Rebuild(); });
                if (sel) btn.GetComponent<Image>().color = new Color(0.30f, 0.45f, 0.65f);
                x += 120f;
            }
        }

        private void BuildDetails(Transform parent, SaveState save, Vector2 pos)
        {
            var box = UiKit.Panel(parent, new Vector2(360, 380), new Color(0.07f, 0.07f, 0.10f, 1f), pos);

            var selected = _selectedItemId != null ? save.Inventory.Find(i => i.Id == _selectedItemId) : null;
            if (selected == null)
            {
                UiKit.Label(box.transform, "Select an item", 16, TextAnchor.MiddleCenter, new Vector2(340, 30), Vector2.zero);
                return;
            }

            float y = 150f;
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

            // compare vs the selected hero
            y -= 8f;
            UiKit.Label(box.transform, $"If equipped on {_selectedHeroId}:", 14, TextAnchor.MiddleLeft,
                        new Vector2(330, 22), new Vector2(0, y));
            y -= 24f;

            if (_selectedHeroId != null)
            {
                var delta = Inventory.CompareForHero(save, _selectedHeroId, selected, _cfg);
                bool any = false;
                foreach (StatKey k in Enum.GetValues(typeof(StatKey)))
                {
                    double d = delta.Get(k);
                    if (d == 0) continue;
                    any = true;
                    string arrow = d > 0 ? "▲" : "▼";
                    var line = UiKit.Label(box.transform, $"{arrow} {k}  {(d > 0 ? "+" : "")}{StatVal(k, d)}",
                                           14, TextAnchor.MiddleLeft, new Vector2(330, 22), new Vector2(0, y));
                    line.color = d > 0 ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.95f, 0.45f, 0.45f);
                    y -= 22f;
                }
                if (!any)
                    UiKit.Label(box.transform, "No stat change", 13, TextAnchor.MiddleLeft, new Vector2(330, 20), new Vector2(0, y));
            }

            BuildActionButton(box.transform, save, selected, new Vector2(0, -150));
        }

        private void BuildActionButton(Transform parent, SaveState save, Item item, Vector2 pos)
        {
            string? equippedHero = EquippedByWhom(save, item.Id);

            if (equippedHero == _selectedHeroId && equippedHero != null)
            {
                UiKit.TextButton(parent, "Unequip", new Vector2(160, 44), pos, () =>
                {
                    if (!_cfg.ItemBases.TryGetValue(item.BaseId, out var b)) return;
                    _view.ReplaceSave(Inventory.UnequipItem(save, _selectedHeroId!, b.Slot));
                    Rebuild();
                });
            }
            else if (equippedHero != null)
            {
                UiKit.Label(parent, $"Equipped by {equippedHero}", 13, TextAnchor.MiddleCenter,
                            new Vector2(330, 22), pos);
            }
            else if (_selectedHeroId != null)
            {
                UiKit.TextButton(parent, "Equip", new Vector2(160, 44), pos, () =>
                {
                    _view.ReplaceSave(Inventory.EquipItem(save, _selectedHeroId!, item.Id, _cfg));
                    Rebuild();
                });
            }
        }

        // ---- rows + helpers ----

        private void ItemRow(Transform parent, Item it, bool equippedHere, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = it.Id == _selectedItemId ? new Color(0.22f, 0.26f, 0.34f) : new Color(0.15f, 0.16f, 0.20f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            go.AddComponent<LayoutElement>().preferredHeight = 34;

            var label = UiKit.Label(go.transform, $"{it.Rarity} {it.BaseId} (i{it.ItemLevel}){(equippedHere ? "  [E]" : "")}",
                                    15, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10, 0); lrt.offsetMax = new Vector2(-10, 0);
            label.color = Palette.Rarity(it.Rarity);
        }

        private static string StatVal(StatKey k, double v)
        {
            // rate/chance stats are fractional; size stats read as integers
            bool fractional = k == StatKey.Spd || k == StatKey.CritChance || k == StatKey.CritDmg;
            return fractional ? v.ToString("0.##") : Mathf.RoundToInt((float)v).ToString();
        }

        private static string? FirstPartyHeroId(SaveState save)
        {
            foreach (var id in save.Party) if (id != null) return id;
            return null;
        }

        private static bool IsEquippedBy(SaveState save, string? heroId, string itemId)
        {
            if (heroId == null) return false;
            var hero = save.Heroes.Find(h => h.Id == heroId);
            return hero != null && hero.Equipped.ContainsValue(itemId);
        }

        private static string? EquippedByWhom(SaveState save, string itemId)
        {
            foreach (var h in save.Heroes)
                if (h.Equipped.ContainsValue(itemId)) return h.Id;
            return null;
        }
    }
}
