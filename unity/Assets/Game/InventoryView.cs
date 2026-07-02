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
        private string? _confirmSalvageId; // two-step confirm guard for Unique+ salvage
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
            UiKit.Label(panel.transform, $"Scrap: {Num.CompactFloor(scrap)}", 15, TextAnchor.MiddleLeft,
                        new Vector2(130, 30), new Vector2(-215, 274)).color = new Color(0.75f, 0.78f, 0.85f);

            BuildAutoEquip(panel.transform);
            // persisted order: the inventory list IS the display order, so Sort survives
            // reopen. Bottom row between the rarity legend and "Salvage all" (the top row
            // is fully occupied by auto-salvage / auto-equip / Close).
            UiKit.TextButton(panel.transform, "Sort", new Vector2(85, 40), new Vector2(135, -300),
                             () => { _view.SortInventory(); Rebuild(); }, 15);
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
                // Imprinted gear (mechanical-mod stamp) gets a violet ✦ — exclusive, farm-only loot.
                if (Loot.IsImprinted(it, _cfg)) UpgradeTell.ImprintBadgeTile(tile);
                var btn = tile.AddComponent<Button>();
                btn.onClick.AddListener(() => ShowDetail(save, it));
                UiKit.Hover(tile, () => ShowDetail(save, it));
            }

            // rarity color reference (since names no longer carry the rarity word — color is the cue)
            BuildRarityLegend(panel.transform);
            BuildMassSalvage(panel.transform);

            // right: item details
            var box = UiKit.Panel(panel.transform, new Vector2(300, 520), new Color(0.07f, 0.07f, 0.10f, 1f), new Vector2(310, -20));
            _detail = box.rectTransform;
            ShowDetail(save, null); // initial prompt

            // auto-salvage threshold: drops at/below this rarity convert to scrap on pickup.
            // Built last so its expanded dropdown renders (and raycasts) on top of the grid.
            BuildAutoSalvage(panel.transform);
        }

        // A compact color→rarity key along the bottom of the bag: now that item names drop the rarity
        // word, the tile/border color is the cue, so a quick reference keeps it legible at a glance.
        private void BuildRarityLegend(Transform parent)
        {
            var order = new IdleGame.GameCore.Rarity[] {
                IdleGame.GameCore.Rarity.Normal, IdleGame.GameCore.Rarity.Rare, IdleGame.GameCore.Rarity.Unique,
                IdleGame.GameCore.Rarity.Legendary, IdleGame.GameCore.Rarity.Mythic };
            float x = -430f, y = -300f;
            for (int i = 0; i < order.Length; i++)
            {
                UiKit.Panel(parent, new Vector2(13, 13), Palette.Rarity(order[i]), new Vector2(x + 6, y));
                UiKit.Label(parent, StatDisplay.RarityName(order[i]), 12, TextAnchor.MiddleLeft,
                            new Vector2(80, 16), new Vector2(x + 56, y)).color = Palette.Rarity(order[i]);
                x += 104f;
            }
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
            var nameLbl = UiKit.Label(_detail, StatDisplay.ItemName(item, _cfg), 18, TextAnchor.MiddleLeft,
                        new Vector2(280, 26), new Vector2(0, y));
            nameLbl.color = Palette.Rarity(item.Rarity);
            // Titled imprints ("Volatile … of Leeching") can run long — auto-shrink to fit one line.
            nameLbl.resizeTextForBestFit = true; nameLbl.resizeTextMaxSize = 18; nameLbl.resizeTextMinSize = 11;
            y -= 24f;
            UiKit.Label(_detail, StatDisplay.RarityName(item.Rarity), 13, TextAnchor.MiddleLeft,
                        new Vector2(270, 20), new Vector2(0, y)).color = Palette.Rarity(item.Rarity); // rarity as a status line
            y -= 22f;
            UiKit.Label(_detail, $"{SlotOf(item)} · item level {item.ItemLevel}", 13, TextAnchor.MiddleLeft,
                        new Vector2(270, 22), new Vector2(0, y));
            y -= 22f;
            if (item.Enhance > 0)
            {
                UiKit.Label(_detail, $"Enhanced +{item.Enhance} · +{item.Enhance * _cfg.Balance.EnhanceBasePctPerLevel * 100:0}% base stats",
                            13, TextAnchor.MiddleLeft, new Vector2(270, 20), new Vector2(0, y))
                     .color = new Color(0.55f, 0.9f, 0.55f);
                y -= 22f;
            }
            y -= 4f;
            var affixes = new List<Affix>(item.Affixes);
            affixes.Sort((x, z) => StatDisplay.Rank(x.Stat).CompareTo(StatDisplay.Rank(z.Stat)));
            foreach (var a in affixes)
            {
                if (Loot.IsImprintStat(a.Stat, _cfg)) // mechanical-mod signature: flavor, not a raw number
                    UiKit.Label(_detail, $"✦ Imprinted — {StatDisplay.ImprintBlurb(a.Stat)}", 13, TextAnchor.MiddleLeft,
                                new Vector2(270, 20), new Vector2(0, y)).color = new Color(0.85f, 0.6f, 1f);
                else
                    UiKit.Label(_detail, $"+{StatDisplay.Value(a.Stat, a.Value)} {StatDisplay.Label(a.Stat)}", 13, TextAnchor.MiddleLeft,
                                new Vector2(270, 20), new Vector2(0, y));
                y -= 22f;
            }

            // Enhance (THE scrap sink): +5% of the item BASE's stats per level. Safe to +5,
            // can fail +6..+9 (scrap only), can DROP a level from +10 up. Any item, worn or loose.
            if (item.Enhance < _cfg.Balance.EnhanceMax)
            {
                long ec = _cfg.Balance.EnhanceCost(item);
                int nextLvl = item.Enhance + 1;
                double chance = _cfg.Balance.EnhanceSuccess[nextLvl - 1];
                bool canEn = Inventory.CanEnhance(save, item.Id, _cfg);
                string odds = chance >= 1.0 ? "" :
                    nextLvl >= _cfg.Balance.EnhanceDropFrom ? $"  {chance * 100:0}% ⚠" : $"  {chance * 100:0}%";
                var en = UiKit.TextButton(_detail, $"Enhance → +{nextLvl}  {Num.CompactCeil(ec)}s{odds}", new Vector2(260, 40),
                    new Vector2(0, -104), canEn
                        ? () => { _view.EnhanceItem(item.Id); ShowDetail(_view.CurrentSave, _view.CurrentSave.Inventory.Find(i => i.Id == item.Id)); }
                        : (System.Action)(() => { }), 15);
                en.interactable = canEn;
                var enImg = en.GetComponent<Image>();
                if (enImg != null) enImg.color = canEn ? new Color(0.26f, 0.42f, 0.32f) : new Color(0.24f, 0.24f, 0.28f);
            }

            // Reforge (item shop): gamble the normal affix values with gold+scrap. Works on any item
            // (including equipped gear); disabled if it has no normal affixes or you can't afford it.
            if (item.Affixes.Exists(a => !Loot.IsImprintStat(a.Stat, _cfg)))
            {
                var (rg, rs) = Inventory.ReforgeCost(item, _cfg);
                bool canRf = Inventory.CanReforge(save, item.Id, _cfg);
                var rf = UiKit.TextButton(_detail, $"Reforge  {Num.CompactCeil(rg)}g + {Num.CompactCeil(rs)}s", new Vector2(260, 40),
                    new Vector2(0, -150), canRf
                        ? () => { _view.ReforgeItem(item.Id); ShowDetail(_view.CurrentSave, _view.CurrentSave.Inventory.Find(i => i.Id == item.Id)); }
                        : (System.Action)(() => { }), 15);
                rf.interactable = canRf;
                var rfImg = rf.GetComponent<Image>();
                if (rfImg != null) rfImg.color = canRf ? new Color(0.34f, 0.30f, 0.46f) : new Color(0.24f, 0.24f, 0.28f);
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

            // Loose item -> manual salvage. Unique and above take a second click to confirm.
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

        // The selectable thresholds, low→high. Legendary/Mythic are intentionally absent:
        // they're the top boss-only chase tiers, so auto-salvage never touches them.
        // Unique IS offered for the late game where Unique drops become churn.
        // "& below" matches Inventory.AddLoot's `Rarity <= max` semantics.
        private static readonly (Rarity? max, string label)[] AutoSalvageOptions =
        {
            (null, "Off"),
            (Rarity.Normal, "Normal"),
            (Rarity.Rare, "Rare & below"),
            (Rarity.Unique, "Unique & below"),
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

        // ---- mass salvage ----

        /// <summary>One-click bag cleanup: scrap every loose item at or below the auto-salvage
        /// threshold (the panel's rarity selector doubles as the cap). Disabled while the
        /// threshold is Off so the cap is always an explicit player choice.</summary>
        private void BuildMassSalvage(Transform parent)
        {
            var cap = Settings.AutoSalvageMax;
            bool enabled = cap != null;
            var btn = UiKit.TextButton(parent, enabled ? $"Salvage all ≤ {StatDisplay.RarityName(cap!.Value)}" : "Salvage all: set threshold",
                new Vector2(250, 40), new Vector2(320, -300),
                enabled ? () => { _view.SalvageAll(cap.Value); Rebuild(); } : (System.Action)(() => { }), 15);
            btn.interactable = enabled;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = enabled ? new Color(0.42f, 0.26f, 0.26f) : new Color(0.24f, 0.24f, 0.28f);
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null && enabled) lbl.color = AutoSalvageColor(cap);
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
