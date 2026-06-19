#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Per-hero equipment "doll" (uGUI), opened from the Party HUD. A tab per party hero
    /// flips between characters; the 9 slots show what that hero has equipped. Clicking a
    /// slot opens a chooser of valid items from the SHARED account bag (with ▲/▼ compare)
    /// to equip, or unequip the current one. Separate from the inventory bag. Equip
    /// changes go through the pure <see cref="Inventory"/> reducers via
    /// <see cref="CombatView.ReplaceSave"/>. Read-only w.r.t. game rules.
    /// </summary>
    public sealed class EquipmentView : MonoBehaviour
    {
        // Doll layout: 3×3 grid of slots (label, grid column, grid row).
        private static readonly (EquipSlot slot, string name, int col, int row)[] Cells =
        {
            (EquipSlot.Helm,   "Helm",    0, 0), (EquipSlot.Amulet, "Amulet",  1, 0), (EquipSlot.Cape,    "Cape",    2, 0),
            (EquipSlot.Weapon, "Weapon",  0, 1), (EquipSlot.Chest,  "Chest",   1, 1), (EquipSlot.Offhand, "Offhand", 2, 1),
            (EquipSlot.Gloves, "Gloves",  0, 2), (EquipSlot.Boots,  "Boots",   1, 2), (EquipSlot.Ring,    "Ring",    2, 2),
        };

        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;        // open canvas (null when closed)
        private string? _heroId;           // selected hero tab
        private EquipSlot? _chooserSlot;    // non-null while choosing an item for a slot

        public bool IsOpen => _panel != null;

        public void Bind(CombatView view, GameConfig cfg)
        {
            _view = view;
            _cfg = cfg;
        }

        /// <summary>Open this hero's equipment (or close if it's already showing them).</summary>
        public void Toggle(string heroId)
        {
            if (_panel != null && _heroId == heroId) { Close(); return; }
            _heroId = heroId;
            _chooserSlot = null;
            Rebuild();
        }

        public void Close()
        {
            if (_panel != null) Destroy(_panel);
            _panel = null;
        }

        private void Rebuild()
        {
            Close();
            Build();
        }

        private void Build()
        {
            var save = _view.CurrentSave;
            if (_heroId == null || Array.IndexOf(PartyHeroIds(save), _heroId) < 0)
                _heroId = FirstPartyHeroId(save);
            if (_heroId == null) return; // no party

            var canvas = UiKit.CreateCanvas("EquipmentCanvas", transform, sortOrder: 96);
            _panel = canvas.gameObject;
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(760, 600), new Color(0.10f, 0.10f, 0.14f, 1f));
            UiKit.Label(panel.transform, "Equipment", 28, TextAnchor.MiddleLeft, new Vector2(300, 38), new Vector2(-210, 258));
            UiKit.TextButton(panel.transform, "Close", new Vector2(150, 56), new Vector2(290, 258), Close);

            BuildTabs(panel.transform, save, new Vector2(-300, 200));

            if (_chooserSlot == null) BuildDoll(panel.transform, save);
            else BuildChooser(panel.transform, save, _chooserSlot.Value);
        }

        private void BuildTabs(Transform parent, SaveState save, Vector2 pos)
        {
            float x = pos.x;
            foreach (var heroId in PartyHeroIds(save))
            {
                var id = heroId;
                bool sel = id == _heroId;
                var b = UiKit.TextButton(parent, HeroName(save, id), new Vector2(150, 52), new Vector2(x, pos.y),
                    () => { _heroId = id; _chooserSlot = null; Rebuild(); }, 18);
                if (sel) b.GetComponent<Image>().color = new Color(0.30f, 0.45f, 0.65f);
                x += 162f;
            }
        }

        private void BuildDoll(Transform parent, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId);
            if (hero == null) return;

            const float cellW = 200f, cellH = 116f, gapX = 16f, gapY = 14f;
            float originX = -(cellW + gapX);   // column 0 centre
            float originY = 96f;               // row 0 centre

            foreach (var (slot, name, col, row) in Cells)
            {
                float cx = originX + col * (cellW + gapX);
                float cy = originY - row * (cellH + gapY);

                hero.Equipped.TryGetValue(slot, out var itemId);
                var item = itemId != null ? save.Inventory.Find(i => i.Id == itemId) : null;
                string label = item != null ? $"{name}\n{item.Rarity} {item.BaseId}" : $"{name}\n(empty)";

                var captured = slot;
                var btn = UiKit.TextButton(parent, label, new Vector2(cellW, cellH), new Vector2(cx, cy),
                    () => { _chooserSlot = captured; Rebuild(); }, 15);
                var img = btn.GetComponent<Image>();
                if (img != null)
                    img.color = item != null ? Dim(Palette.Rarity(item.Rarity)) : new Color(0.16f, 0.17f, 0.22f);
            }
        }

        private void BuildChooser(Transform parent, SaveState save, EquipSlot slot)
        {
            UiKit.Label(parent, $"Equip into {slot}", 20, TextAnchor.MiddleLeft, new Vector2(360, 30), new Vector2(-200, 150));
            UiKit.TextButton(parent, "Back", new Vector2(120, 48), new Vector2(280, 150), () => { _chooserSlot = null; Rebuild(); });

            var hero = save.Heroes.Find(h => h.Id == _heroId)!;
            bool slotFilled = hero.Equipped.ContainsKey(slot);
            if (slotFilled)
            {
                UiKit.TextButton(parent, "Unequip", new Vector2(200, 52), new Vector2(-200, 100), () =>
                {
                    _view.ReplaceSave(Inventory.UnequipItem(save, _heroId!, slot));
                    _chooserSlot = null;
                    Rebuild();
                });
            }

            var content = UiKit.ScrollColumn(parent, new Vector2(660, 360), new Vector2(0, -60));
            var equipped = EquippedIds(save);

            bool any = false;
            foreach (var item in save.Inventory)
            {
                if (!_cfg.ItemBases.TryGetValue(item.BaseId, out var b) || b.Slot != slot) continue;
                if (equipped.Contains(item.Id)) continue; // only items free to equip
                any = true;
                var captured = item;
                ChooserRow(content, save, captured, () =>
                {
                    _view.ReplaceSave(Inventory.EquipItem(save, _heroId!, captured.Id, _cfg));
                    _chooserSlot = null;
                    Rebuild();
                });
            }
            if (!any)
                UiKit.Label(content, "No items for this slot in the bag.", 16, TextAnchor.MiddleCenter,
                            Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
        }

        private void ChooserRow(Transform parent, SaveState save, Item item, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.16f, 0.20f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            go.AddComponent<LayoutElement>().preferredHeight = 52;

            // name (rarity-colored)
            var name = UiKit.Label(go.transform, $"{item.Rarity} {item.BaseId} (i{item.ItemLevel})",
                                   17, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            var nrt = (RectTransform)name.transform;
            nrt.anchorMin = new Vector2(0f, 0f); nrt.anchorMax = new Vector2(0.5f, 1f);
            nrt.offsetMin = new Vector2(14, 0); nrt.offsetMax = new Vector2(-6, 0);
            name.color = Palette.Rarity(item.Rarity);

            // compare delta vs the selected hero (right half)
            var delta = UiKit.Label(go.transform, CompareSummary(save, item), 14, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            var drt = (RectTransform)delta.transform;
            drt.anchorMin = new Vector2(0.5f, 0f); drt.anchorMax = new Vector2(1f, 1f);
            drt.offsetMin = new Vector2(6, 0); drt.offsetMax = new Vector2(-12, 0);
            delta.color = new Color(0.8f, 0.85f, 0.9f);
        }

        private string CompareSummary(SaveState save, Item candidate)
        {
            var delta = Inventory.CompareForHero(save, _heroId!, candidate, _cfg);
            var parts = new List<string>();
            foreach (StatKey k in Enum.GetValues(typeof(StatKey)))
            {
                double d = delta.Get(k);
                if (d == 0) continue;
                parts.Add($"{(d > 0 ? "▲" : "▼")}{k} {(d > 0 ? "+" : "")}{StatVal(k, d)}");
            }
            return parts.Count == 0 ? "no change" : string.Join("  ", parts);
        }

        // ---- helpers ----

        private static Color Dim(Color c) => new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 1f);

        private static string StatVal(StatKey k, double v)
        {
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

        private static string[] PartyHeroIds(SaveState save)
        {
            var list = new List<string>(4);
            foreach (var id in save.Party) if (id != null) list.Add(id);
            return list.ToArray();
        }

        private static string? FirstPartyHeroId(SaveState save)
        {
            foreach (var id in save.Party) if (id != null) return id;
            return null;
        }

        private static HashSet<string> EquippedIds(SaveState save)
        {
            var set = new HashSet<string>();
            foreach (var h in save.Heroes)
                foreach (var id in h.Equipped.Values) set.Add(id);
            return set;
        }
    }
}
