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
        private bool _autoSalvageOpen;     // auto-salvage dropdown expanded? (kept across Rebuild)

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
            _autoSalvageOpen = false; // fresh open starts collapsed
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
            var title = UiKit.Label(panel.transform, $"Inventory  {loose}/{cap}", 22, TextAnchor.MiddleLeft,
                                    new Vector2(180, 40), new Vector2(-355, 274));
            if (loose > cap) title.color = new Color(1f, 0.6f, 0.4f); // overfilled (idle/boss spillover)

            long scrap = save.Currencies.TryGetValue("scrap", out var sc) ? sc : 0;
            UiKit.Label(panel.transform, $"Scrap: {Num.Compact(scrap)}", 15, TextAnchor.MiddleLeft,
                        new Vector2(130, 30), new Vector2(-215, 274)).color = new Color(0.75f, 0.78f, 0.85f);

            BuildAutoEquip(panel.transform);
            UiKit.TextButton(panel.transform, "Close", new Vector2(110, 50), new Vector2(400, 274), Close, 22);

            // left: grid of item tiles (the shared bag)
            var grid = UiKit.ScrollGrid(panel.transform, new Vector2(520, 520), new Vector2(-170, -20), new Vector2(76, 76));
            int stage = save.Progress.CurrentStage;
            foreach (var item in save.Inventory)
            {
                var it = item; // capture
                var tile = UiKit.ItemTile(grid, new Vector2(76, 76), Vector2.zero, it.Rarity, UiKit.SlotAbbrev(SlotOf(it)), raycast: true);
                // Loose items that are an upgrade for someone get a green ▲ (Lever 2 legibility).
                if (EquippedByWhom(save, it.Id) == null)
                {
                    var best = Upgrades.BestForItem(save, it, _cfg, stage);
                    if (best != null) UpgradeTell.BadgeTile(tile, best.Verdict);
                }
                var btn = tile.AddComponent<Button>();
                btn.onClick.AddListener(() => ShowDetail(save, it));
                UiKit.Hover(tile, () => ShowDetail(save, it));
            }

            // right: item details
            var box = UiKit.Panel(panel.transform, new Vector2(300, 520), new Color(0.07f, 0.07f, 0.10f, 1f), new Vector2(310, -20));
            _detail = box.rectTransform;
            ShowDetail(save, null); // initial prompt

            // auto-salvage threshold: drops at/below this rarity convert to scrap on pickup.
            // Built last so its expanded dropdown renders (and raycasts) on top of the grid.
            BuildAutoSalvage(panel.transform);
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

            // Best-fit upgrade verdict (Lever 2): who would this help, and by how much?
            y -= 8f;
            var bestFit = Upgrades.BestForItem(save, item, _cfg, save.Progress.CurrentStage);
            if (bestFit != null && bestFit.Verdict == Upgrades.Verdict.Upgrade)
                UiKit.Label(_detail, $"{UpgradeTell.Glyph(bestFit.Verdict)} {UpgradeTell.Pct(bestFit.DeltaPercent)} power for {HeroName(save, bestFit.HeroId)}",
                            14, TextAnchor.MiddleLeft, new Vector2(270, 22), new Vector2(0, y)).color = UpgradeTell.Color(bestFit.Verdict);
            else
                UiKit.Label(_detail, "No upgrade for any hero", 13, TextAnchor.MiddleLeft,
                            new Vector2(270, 22), new Vector2(0, y)).color = UpgradeTell.Side;
            y -= 26f;

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

        // The selectable thresholds, low→high. Unique/Legendary are intentionally absent:
        // they're boss-only chase items and trash is capped at Rare, so auto-salvage never
        // touches them. "& below" matches Inventory.AddLoot's `Rarity <= max` semantics.
        private static readonly (Rarity? max, string label)[] AutoSalvageOptions =
        {
            (null, "Off"),
            (Rarity.Normal, "Normal"),
            (Rarity.Magic, "Magic & below"),
            (Rarity.Rare, "Rare & below"),
        };

        private static string AutoSalvageLabel(Rarity? max)
        {
            foreach (var o in AutoSalvageOptions) if (o.max == max) return o.label;
            return "Off";
        }

        private static Color AutoSalvageColor(Rarity? max) =>
            max == null ? new Color(0.7f, 0.72f, 0.78f) : Palette.Rarity(max.Value);

        /// <summary>Header button + an explicit dropdown list of thresholds (replaces the old
        /// cycling button). Click the header to expand; click an option to set it and collapse.</summary>
        private void BuildAutoSalvage(Transform parent)
        {
            var cur = Settings.AutoSalvageMax;
            var btn = UiKit.TextButton(parent, $"Auto-salvage: {AutoSalvageLabel(cur)}  {(_autoSalvageOpen ? "▴" : "▾")}",
                                       new Vector2(230, 46), new Vector2(-15, 274),
                                       () => { _autoSalvageOpen = !_autoSalvageOpen; Rebuild(); }, 15);
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.color = AutoSalvageColor(cur);

            if (!_autoSalvageOpen) return;

            const float rowH = 40f, firstY = 229f; // first row just under the header button
            int n = AutoSalvageOptions.Length;
            // backdrop behind the option rows (added before them, so the buttons sit on top)
            float panelH = rowH * n + 8f;
            float panelCY = firstY + rowH / 2f - panelH / 2f;
            UiKit.Panel(parent, new Vector2(238, panelH), new Color(0.05f, 0.05f, 0.08f, 1f), new Vector2(-15, panelCY));

            for (int i = 0; i < n; i++)
            {
                var (max, label) = AutoSalvageOptions[i];
                bool selected = max == cur;
                var ob = UiKit.TextButton(parent, (selected ? "● " : "") + label, new Vector2(222, rowH - 4f),
                                          new Vector2(-15, firstY - rowH * i), () => SelectAutoSalvage(max), 16);
                if (selected) ob.GetComponent<Image>().color = new Color(0.24f, 0.34f, 0.5f);
                var ol = ob.GetComponentInChildren<Text>();
                if (ol != null) ol.color = AutoSalvageColor(max);
            }
        }

        private void SelectAutoSalvage(Rarity? max)
        {
            Settings.AutoSalvageMax = max;
            _autoSalvageOpen = false; // collapse after choosing
            Rebuild();
        }

        // ---- auto-equip-if-better toggle ----

        /// <summary>A simple on/off toggle (Lever 2): when on, a banked drop that's a genuine power
        /// upgrade for a fielded hero auto-equips. Sits in the header beside auto-salvage — both
        /// govern what happens to drops automatically.</summary>
        private void BuildAutoEquip(Transform parent)
        {
            bool on = Settings.AutoEquipUpgrades;
            var btn = UiKit.TextButton(parent, $"Auto-equip: {(on ? "On" : "Off")}", new Vector2(150, 46),
                                       new Vector2(230, 274), () => { Settings.AutoEquipUpgrades = !on; Rebuild(); }, 15);
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.color = on ? UpgradeTell.Up : new Color(0.7f, 0.72f, 0.78f);
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
