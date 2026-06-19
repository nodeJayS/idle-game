#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Per-hero equipment screen (uGUI), opened from the Party HUD. A tab per party hero;
    /// a doll of 9 slot tiles (rarity-bordered) on the left; the shared account bag on the
    /// right. Low-friction: hovering a bag item shows its stats vs what's equipped in that
    /// slot; a single click equips it (slot auto-derived). Clicking a doll tile shows the
    /// worn item with an Unequip button. All changes go through the pure
    /// <see cref="Inventory"/> reducers via <see cref="CombatView.ReplaceSave"/>.
    /// </summary>
    public sealed class EquipmentView : MonoBehaviour
    {
        private static readonly (EquipSlot slot, int col, int row)[] Cells =
        {
            (EquipSlot.Helm, 0, 0), (EquipSlot.Amulet, 1, 0), (EquipSlot.Cape, 2, 0),
            (EquipSlot.Weapon, 0, 1), (EquipSlot.Chest, 1, 1), (EquipSlot.Offhand, 2, 1),
            (EquipSlot.Gloves, 0, 2), (EquipSlot.Boots, 1, 2), (EquipSlot.Ring, 2, 2),
        };

        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;
        private string? _heroId;
        private RectTransform? _detail;   // fixed pane, updated on hover/click (no full rebuild)

        public bool IsOpen => _panel != null;

        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }

        public void Toggle(string heroId)
        {
            if (_panel != null && _heroId == heroId) { Close(); return; }
            _heroId = heroId;
            Rebuild();
        }

        public void Close()
        {
            if (_panel != null) Destroy(_panel);
            _panel = null;
            _detail = null;
        }

        private void Rebuild() { Close(); Build(); }

        private void Build()
        {
            var save = _view.CurrentSave;
            // Keep any OWNED hero (the roster can open a benched one to gear it); only fall
            // back to a party hero when the current id is gone/invalid.
            if (_heroId == null || !save.Heroes.Exists(h => h.Id == _heroId))
                _heroId = FirstPartyHeroId(save);
            if (_heroId == null) return;

            var canvas = UiKit.CreateCanvas("EquipmentCanvas", transform, sortOrder: 96);
            _panel = canvas.gameObject;
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(1000, 680), new Color(0.10f, 0.10f, 0.14f, 1f));
            UiKit.Label(panel.transform, "Equipment", 28, TextAnchor.MiddleLeft, new Vector2(300, 38), new Vector2(-340, 300));
            UiKit.TextButton(panel.transform, "Close", new Vector2(150, 52), new Vector2(420, 300), Close);

            BuildTabs(panel.transform, save, new Vector2(-440, 248));
            BuildDoll(panel.transform, save);
            BuildBag(panel.transform, save);

            // detail / compare pane (right)
            var box = UiKit.Panel(panel.transform, new Vector2(280, 480), new Color(0.07f, 0.07f, 0.10f, 1f), new Vector2(355, -40));
            _detail = box.rectTransform;
            ShowHeroStats(save); // default pane = the hero's stat sheet
        }

        private void BuildTabs(Transform parent, SaveState save, Vector2 pos)
        {
            var partyIds = PartyHeroIds(save);
            var partySet = new HashSet<string>(partyIds);
            var ids = new List<string>(partyIds);
            if (_heroId != null && !partySet.Contains(_heroId)) ids.Add(_heroId); // benched hero being geared

            float x = pos.x;
            foreach (var heroId in ids)
            {
                var id = heroId;
                bool sel = id == _heroId;
                bool benched = !partySet.Contains(id);
                var b = UiKit.TextButton(parent, benched ? HeroName(save, id) + " (B)" : HeroName(save, id),
                    new Vector2(150, 50), new Vector2(x, pos.y), () => { _heroId = id; Rebuild(); }, 18);
                if (sel) b.GetComponent<Image>().color = new Color(0.30f, 0.45f, 0.65f);
                else if (benched) b.GetComponent<Image>().color = new Color(0.34f, 0.30f, 0.42f);
                x += 162f;
            }
        }

        private void BuildDoll(Transform parent, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId)!;
            const float cell = 96f, sp = 110f;
            float ox = -300f, oy = 130f; // centre of (col1,row0)

            UiKit.Label(parent, HeroName(save, _heroId), 18, TextAnchor.MiddleCenter, new Vector2(330, 24), new Vector2(-300, 205));

            foreach (var (slot, col, row) in Cells)
            {
                float cx = ox + (col - 1) * sp;
                float cy = oy - row * sp;

                hero.Equipped.TryGetValue(slot, out var itemId);
                var item = itemId != null ? save.Inventory.Find(i => i.Id == itemId) : null;
                Rarity? rarity = item?.Rarity;

                var tile = UiKit.ItemTile(parent, new Vector2(cell, cell), new Vector2(cx, cy), rarity, UiKit.SlotAbbrev(slot), raycast: true);
                var captured = item;
                var btn = tile.AddComponent<Button>();
                btn.onClick.AddListener(() => ShowDetail(save, captured, slotIfEmpty: slot));
                UiKit.Hover(tile, () => ShowDetail(save, captured, slotIfEmpty: slot), () => ShowHeroStats(save));
            }
        }

        private void BuildBag(Transform parent, SaveState save)
        {
            UiKit.Label(parent, "Bag", 18, TextAnchor.MiddleLeft, new Vector2(120, 24), new Vector2(-95, 205));
            var grid = UiKit.ScrollGrid(parent, new Vector2(310, 440), new Vector2(50, -40), new Vector2(72, 72));

            bool any = false;
            var equipped = EquippedIds(save);
            foreach (var item in save.Inventory)
            {
                if (equipped.Contains(item.Id)) continue; // only free items can be equipped
                any = true;
                var it = item;
                var tile = UiKit.ItemTile(grid, new Vector2(72, 72), Vector2.zero, it.Rarity, UiKit.SlotAbbrev(SlotOf(it)), raycast: true);
                tile.AddComponent<Button>().onClick.AddListener(() => EquipFromBag(save, it)); // one click to equip
                UiKit.Hover(tile, () => ShowDetail(save, it), () => ShowHeroStats(save));        // hover to compare
            }
            if (!any)
                UiKit.Label(parent, "No free items in the bag.", 14, TextAnchor.MiddleCenter,
                            new Vector2(280, 40), new Vector2(50, 60));
        }

        // ---- detail / compare pane ----

        private void ShowDetail(SaveState save, Item? item, EquipSlot? slotIfEmpty = null)
        {
            if (_detail == null) return;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);

            if (item == null)
            {
                string msg = slotIfEmpty != null ? $"{slotIfEmpty} — empty\nHover a bag item to compare." : "Hover or click an item.";
                UiKit.Label(_detail, msg, 15, TextAnchor.MiddleCenter, new Vector2(250, 80), new Vector2(0, 0));
                return;
            }

            float y = 200f;
            UiKit.Label(_detail, $"{item.Rarity} {item.BaseId}", 18, TextAnchor.MiddleLeft,
                        new Vector2(250, 24), new Vector2(0, y)).color = Palette.Rarity(item.Rarity);
            y -= 26f;
            UiKit.Label(_detail, $"{SlotOf(item)} · item level {item.ItemLevel}", 13, TextAnchor.MiddleLeft,
                        new Vector2(250, 20), new Vector2(0, y));
            y -= 24f;
            var affixes = new List<Affix>(item.Affixes);
            affixes.Sort((x, z) => StatDisplay.Rank(x.Stat).CompareTo(StatDisplay.Rank(z.Stat)));
            foreach (var a in affixes)
            {
                UiKit.Label(_detail, $"+{StatDisplay.Value(a.Stat, a.Value)} {StatDisplay.Label(a.Stat)}", 13, TextAnchor.MiddleLeft,
                            new Vector2(250, 18), new Vector2(0, y));
                y -= 20f;
            }

            bool equippedHere = EquippedByWhom(save, item.Id) == _heroId;
            if (equippedHere)
            {
                UiKit.Label(_detail, "Equipped", 13, TextAnchor.MiddleLeft, new Vector2(250, 18), new Vector2(0, y - 6)).color =
                    new Color(0.6f, 0.85f, 1f);
                UiKit.TextButton(_detail, "Unequip", new Vector2(200, 50), new Vector2(0, -200),
                    () => { _view.ReplaceSave(Inventory.UnequipItem(save, _heroId!, SlotOf(item))); Rebuild(); });
            }
            else
            {
                y -= 8f;
                UiKit.Label(_detail, $"vs {HeroName(save, _heroId)}'s {SlotOf(item)}:", 13, TextAnchor.MiddleLeft,
                            new Vector2(250, 18), new Vector2(0, y));
                y -= 22f;
                var delta = Inventory.CompareForHero(save, _heroId!, item, _cfg);
                bool anyDelta = false;
                foreach (var k in StatDisplay.Order)
                {
                    double d = delta.Get(k);
                    if (d == 0) continue;
                    anyDelta = true;
                    var l = UiKit.Label(_detail, $"{(d > 0 ? "▲" : "▼")} {StatDisplay.Label(k)}  {StatDisplay.Delta(k, d)}",
                                        13, TextAnchor.MiddleLeft, new Vector2(250, 18), new Vector2(0, y));
                    l.color = d > 0 ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.95f, 0.45f, 0.45f);
                    y -= 20f;
                }
                if (!anyDelta)
                    UiKit.Label(_detail, "No stat change", 13, TextAnchor.MiddleLeft, new Vector2(250, 18), new Vector2(0, y));

                UiKit.TextButton(_detail, "Equip", new Vector2(200, 50), new Vector2(0, -200),
                    () => EquipFromBag(save, item));
            }
        }

        private void EquipFromBag(SaveState save, Item item)
        {
            _view.ReplaceSave(Inventory.EquipItem(save, _heroId!, item.Id, _cfg));
            Rebuild();
        }

        /// <summary>Default pane: the selected hero's full stats in canonical order.</summary>
        private void ShowHeroStats(SaveState save)
        {
            if (_detail == null) return;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);

            var hero = save.Heroes.Find(h => h.Id == _heroId);
            if (hero == null) return;
            var stats = Stats.ComputeHeroStats(hero, _cfg, Stats.ResolveEquipped(save, hero));

            UiKit.Label(_detail, HeroName(save, _heroId), 18, TextAnchor.MiddleLeft, new Vector2(250, 24), new Vector2(0, 210))
                .color = new Color(0.85f, 0.9f, 1f);
            UiKit.Label(_detail, $"Level {hero.Level}", 12, TextAnchor.MiddleLeft, new Vector2(250, 18), new Vector2(0, 188));

            float y = 158f;
            foreach (var k in StatDisplay.Order)
            {
                UiKit.Label(_detail, StatDisplay.Label(k), 13, TextAnchor.MiddleLeft, new Vector2(160, 18), new Vector2(-45, y));
                UiKit.Label(_detail, StatDisplay.Value(k, stats.Get(k)), 13, TextAnchor.MiddleRight, new Vector2(90, 18), new Vector2(95, y));
                y -= 22f;
            }
        }

        // ---- helpers ----

        private EquipSlot SlotOf(Item item) => _cfg.ItemBases[item.BaseId].Slot;

        private string HeroName(SaveState save, string? heroId)
        {
            if (heroId == null) return "";
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

        private static string? EquippedByWhom(SaveState save, string itemId)
        {
            foreach (var h in save.Heroes)
                if (h.Equipped.ContainsValue(itemId)) return h.Id;
            return null;
        }
    }
}
