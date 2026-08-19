#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The shared account bag (uGUI). A grid of rarity-bordered item tiles; hover or click
    /// a tile to see its details on the right. Equipping is NOT done here — that's the
    /// per-hero Equipment HUD (<see cref="EquipmentView"/>); this window is purely the bag
    /// the whole roster draws from. Manual salvage (detail pane) + the loot filter (header:
    /// per-slot auto-salvage floors + the imprint guard, 10.5a) run through the pure
    /// <see cref="Inventory"/> reducers — the filter lives in the SAVE, not a pref; game
    /// rules stay in GameCore. Bulk-select salvage (10.5c): a footer "Select" mode where
    /// tiles toggle/sweep into a hand-picked set for one no-confirm salvage
    /// (<see cref="Inventory.SalvageMany"/>).
    ///
    /// Built on <see cref="PanelKit"/> + <see cref="Theme"/> (10.3): anchor/layout-group
    /// driven throughout. The two sanctioned positional exceptions: tile-corner badges
    /// (<see cref="LockBadgeTile"/> et al.) and the loot-filter dropdown overlay, whose
    /// anchor is DERIVED from the laid-out header button (see <see cref="DropdownFollow"/>).
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        private const float TileSize = 76f;      // bag tile edge
        private const float DetailW = 300f;      // fixed detail column width
        private const float FooterH = 40f;       // bottom action row / detail action-button height
        private const float SalvageH = 46f;      // the salvage/confirm buttons (slightly taller — the risky verb)
        private const float SwatchPx = 13f;      // rarity-legend swatch edge
        private const float LegendLabelW = 68f;  // rarity-legend name cell
        private const float CountW = 90f;        // header live-count cell
        private const float ScrapW = 130f;       // header scrap cell
        private const float AutoSalvageW = 230f; // loot-filter header button
        private const float SlotLabelW = 74f;    // slot-name cell in the filter dropdown
        private const float GuardBtnW = 64f;     // the imprint-guard On/Off button
        private const float AutoEquipW = 150f;   // auto-equip toggle
        private const float SortW = 85f;
        // 200 (was 250 pre-10.5c) still fits the armed "Confirm salvage all (999)" label — the shave
        // pays for the Select toggle so the footer stays inside the 920-window's width budget.
        private const float SalvageAllW = 200f;
        private const float SelectW = 90f;       // bulk-select mode toggle (10.5c)
        private const float SalvageSelW = 290f;  // "Salvage N selected  (+X scrap)" runs long
        private const float LockW = 96f;
        private const float CancelW = 140f;
        private const float DropRowH = 36f;      // dropdown option row height
        private const int FsMid = 16;            // Cancel + dropdown options (between FsBody and FsSubTab)
        private const int MinNameFs = 11;        // long imprint names auto-shrink to this

        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;        // the open inventory canvas (null when closed)
        private RectTransform? _detail;    // fixed detail pane, updated on hover/click
        private string? _confirmSalvageId; // two-step confirm guard for Unique+ salvage
        private bool _filterOpen;          // loot-filter dropdown expanded? (kept across Rebuild)
        private bool _confirmMassSalvage;  // "Salvage all" armed? (first click arms, second executes)
        private Coroutine? _massSalvageTimer; // disarms the confirm after a few seconds

        // ---- bulk-select salvage (10.5c) ----
        private bool _selectMode;          // tiles toggle into a selection instead of showing detail
        private readonly HashSet<string> _selected = new HashSet<string>(); // selected item ids
        private bool _sweeping;            // a press-drag sweep is in progress (press ... release)
        private Button? _selSalvageBtn;    // footer "Salvage N selected" — updated IN PLACE:
        private Text? _selSalvageLbl;      // rebuilding mid-sweep would destroy the tile under the pointer

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
            _filterOpen = false;         // fresh open starts collapsed
            _confirmMassSalvage = false; // fresh open starts disarmed
            _selectMode = false;         // fresh open starts in browse mode, selection discarded
            _selected.Clear();
            _sweeping = false;
            Open();
        }

        /// <summary>The player-facing close (the bag toggle or the header X): the window eases out and
        /// then destroys itself.</summary>
        private void Close() => Teardown(animate: true);

        /// <summary>Drop the canvas. The redraw paths pass animate:false — picking an item or flipping
        /// the loot filter rebuilds this whole canvas, and an outgoing one that lingered would sit on
        /// top of its own replacement.</summary>
        private void Teardown(bool animate)
        {
            if (_massSalvageTimer != null) { StopCoroutine(_massSalvageTimer); _massSalvageTimer = null; }
            UiMotion.Dismiss(_panel, animate);
            _panel = null;
            _detail = null;
            _confirmSalvageId = null;
            _selSalvageBtn = null; // destroyed with the panel; select-mode STATE survives Rebuild
            _selSalvageLbl = null;
            _sweeping = false;
        }

        /// <summary>Reopen on the current save (after a salvage mutates it). Disarms the mass-salvage
        /// confirm — any bag mutation (single salvage, lock, sort) cancels a pending "Salvage all".</summary>
        private void Rebuild() { _confirmMassSalvage = false; RebuildKeepConfirm(); }

        /// <summary>Redraw without disarming the mass-salvage confirm (used by the arm click itself).</summary>
        private void RebuildKeepConfirm() { Teardown(animate: false); Open(); }

        private void Open()
        {
            var save = _view.CurrentSave;

            _panel = PanelKit.Window(transform, "Inventory", Close, out var body, "InventoryCanvas",
                                     sortOrder: 95, max: new Vector2(920, 640));
            var canvas = _panel.GetComponent<Canvas>();
            var panelRt = (RectTransform)_panel.GetComponentInChildren<WindowSizer>().transform;

            PanelKit.Stack(body); // header row / grid+detail / footer

            // Header row: live bag count, scrap balance, then the two drop-handling automations.
            var header = PanelKit.Row(body, Theme.BtnHs);
            int loose = Inventory.LooseCount(save);
            int cap = _cfg.Balance.InventoryCap;
            PanelKit.TextCell(header, $"{loose}/{cap}", Theme.FsBody,
                loose > cap ? Theme.Overfull : Theme.TextBody, // overfilled (idle/boss spillover)
                TextAnchor.MiddleLeft, width: CountW);
            long scrap = save.Currencies.TryGetValue("scrap", out var sc) ? sc : 0;
            PanelKit.TextCell(header, $"Scrap: {Num.CompactFloor(scrap)}", Theme.FsBody, Theme.TextBody,
                TextAnchor.MiddleLeft, width: ScrapW);
            PanelKit.FlexSpacer(header);
            var lootFilterBtn = BuildLootFilter(header);
            BuildAutoEquip(header);

            // Middle: grid of item tiles (the shared bag) | fixed detail pane.
            var mid = PanelKit.Flex(body);
            var cols = PanelKit.Columns(mid, (1f, 0f), (0f, DetailW));

            var grid = UiKit.ScrollGridFill(cols[0], new Vector2(TileSize, TileSize));
            int stage = save.Progress.CurrentStage;
            foreach (var item in save.Inventory)
            {
                var it = item; // capture
                var tile = UiKit.ItemTile(grid, new Vector2(TileSize, TileSize), Vector2.zero, it.Rarity, UiKit.SlotAbbrev(SlotOf(it)), raycast: true);
                // Loose items that are an upgrade for someone get a green ▲ (Lever 2 legibility).
                if (EquippedByWhom(save, it.Id) == null)
                {
                    var best = Upgrades.BestForItem(save, it, _cfg, stage);
                    if (best != null) UpgradeTell.BadgeTile(tile, best.Verdict);
                }
                // Imprinted gear (mechanical-mod stamp) gets a violet ✦ — exclusive, farm-only loot.
                if (Loot.IsImprinted(it, _cfg)) UpgradeTell.ImprintBadgeTile(tile);
                // Locked items get a padlock in the bottom-left corner (never salvaged). Bottom-left so it
                // never collides with the top-right ▲ upgrade badge or the top-left ✦ imprint badge.
                if (it.Locked) LockBadgeTile(tile);
                if (_selectMode)
                {
                    WireSelectTile(tile, save, it); // toggle/sweep instead of detail
                }
                else
                {
                    var btn = tile.AddComponent<Button>();
                    btn.onClick.AddListener(() => { SoundFx.Play("System_Shift_Click", 0.3f); ShowDetail(save, it); }); // 10.9d tile tick
                    UiKit.ApplyButtonStates(btn); // tiles press like every other control (P3)
                    UiKit.Hover(tile, () => ShowDetail(save, it));
                }
            }

            var detail = PanelKit.Section(cols[1], null);
            detail.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f; // fill the column
            _detail = detail;
            if (_selectMode) ShowSelectHint(); else ShowDetail(save, null); // initial prompt

            // Footer: rarity color reference (since names no longer carry the rarity word —
            // color is the cue), then Sort (persisted order: the inventory list IS the display
            // order, so Sort survives reopen) and the mass-salvage verb.
            // The window's primary action row (Select / Sort / Salvage all) — raised past the 44 touch
            // floor to BtnH=48 (10.13c). The grid Flex above absorbs the extra height, so no starvation.
            var footer = PanelKit.Row(body, Theme.BtnH);
            BuildRarityLegend(footer);
            PanelKit.FlexSpacer(footer);
            BuildSelectToggle(footer);
            if (_selectMode)
            {
                // Select mode swaps the verb stack: the wide "Salvage N selected" takes both the
                // Sort and Salvage-all slots (sorting mid-selection would shuffle tiles under a sweep).
                BuildSalvageSelected(footer);
            }
            else
            {
                PanelKit.ButtonCell(footer, "Sort", () => { _view.SortInventory(); Rebuild(); },
                    width: SortW, fontSize: Theme.FsBody);
                BuildMassSalvage(footer);
            }

            // loot filter: per-slot floors + imprint guard governing what scraps on pickup.
            // Built last so its expanded dropdown renders (and raycasts) on top of the grid.
            BuildLootFilterDropdown(canvas, panelRt, lootFilterBtn);
        }

        // A compact color→rarity key along the bottom of the bag: now that item names drop the rarity
        // word, the tile/border color is the cue, so a quick reference keeps it legible at a glance.
        private static void BuildRarityLegend(RectTransform footer)
        {
            var order = new[] { Rarity.Normal, Rarity.Rare, Rarity.Unique, Rarity.Legendary, Rarity.Mythic };
            foreach (var r in order)
            {
                var sw = new GameObject("Swatch", typeof(RectTransform));
                sw.transform.SetParent(footer, false);
                sw.AddComponent<Image>().color = Palette.Rarity(r);
                var le = sw.AddComponent<LayoutElement>();
                le.preferredWidth = SwatchPx; le.preferredHeight = SwatchPx;
                // Explicit 0s: the Row's force-expand would stretch the chip to full row height
                // otherwise (an explicit LayoutElement value overrides forceExpand).
                le.flexibleWidth = 0f; le.flexibleHeight = 0f;
                PanelKit.TextCell(footer, StatDisplay.RarityTag(r), Theme.FsTiny, Palette.Rarity(r),
                    TextAnchor.MiddleLeft, width: LegendLabelW);
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
                PanelKit.Flex(_detail);
                PanelKit.Label(_detail, "Hover or click an item.", Theme.FsBody, Theme.TextBright, TextAnchor.MiddleCenter);
                PanelKit.Flex(_detail);
                return;
            }

            // The identity block is Fixed-pinned (min = preferred): when the pane is over
            // budget, the flexible fill below crushes first — these lines must never vanish
            // (the height-starvation trap; the set-tell lines pushed a 1080p pane over once).
            // Locked items lead with a gold [L] tag (mirrors the tile badge) so the protection reads at a glance.
            string nameText = (item.Locked ? "[L] " : "") + StatDisplay.ItemName(item, _cfg);
            var nameLbl = PanelKit.Label(_detail, nameText, Theme.FsH2, Palette.Rarity(item.Rarity), TextAnchor.MiddleLeft);
            // Titled imprints ("Volatile … of Leeching") can run long — auto-shrink to fit one line.
            nameLbl.resizeTextForBestFit = true; nameLbl.resizeTextMaxSize = Theme.FsH2; nameLbl.resizeTextMinSize = MinNameFs;
            PanelKit.Fixed(nameLbl.gameObject, height: 24f);

            var rar = PanelKit.Label(_detail, StatDisplay.RarityTag(item.Rarity), Theme.FsSmall,
                Palette.Rarity(item.Rarity), TextAnchor.MiddleLeft); // rarity as a status line (glyph = the colorblind channel)
            PanelKit.Fixed(rar.gameObject, height: 18f);
            var slotLbl = PanelKit.Label(_detail, $"{SlotOf(item)} · item level {item.ItemLevel}", Theme.FsSmall,
                Theme.TextBright, TextAnchor.MiddleLeft);
            PanelKit.Fixed(slotLbl.gameObject, height: 18f);
            if (item.Enhance > 0)
            {
                var enhLbl = PanelKit.Label(_detail, $"Enhanced +{item.Enhance} · +{item.Enhance * _cfg.Balance.EnhanceBasePctPerLevel * 100:0}% base stats",
                    Theme.FsSmall, Theme.Good, TextAnchor.MiddleLeft);
                PanelKit.Fixed(enhLbl.gameObject, height: 18f);
            }

            var affixes = new List<Affix>(item.Affixes);
            affixes.Sort((x, z) => StatDisplay.Rank(x.Stat).CompareTo(StatDisplay.Rank(z.Stat)));
            foreach (var a in affixes)
            {
                var affLbl = Loot.IsImprintStat(a.Stat, _cfg) // mechanical-mod signature: flavor, not a raw number
                    ? PanelKit.Label(_detail, $"✦ Imprinted — {StatDisplay.ImprintBlurb(a.Stat)}", Theme.FsSmall,
                        Theme.Imprint, TextAnchor.MiddleLeft)
                    : PanelKit.Label(_detail, $"+{StatDisplay.Value(a.Stat, a.Value)} {StatDisplay.Label(a.Stat)}",
                        Theme.FsSmall, Theme.TextBright, TextAnchor.MiddleLeft);
                PanelKit.Fixed(affLbl.gameObject, height: 18f);
            }

            PanelKit.Flex(_detail); // pin the action stack to the pane bottom

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
                var enRow = PanelKit.Row(_detail, FooterH);
                var en = PanelKit.ButtonCell(enRow, $"Enhance → +{nextLvl}  {Num.CompactCeil(ec)}s{odds}",
                    () => { _view.EnhanceItem(item.Id); ShowDetail(_view.CurrentSave, _view.CurrentSave.Inventory.Find(i => i.Id == item.Id)); },
                    fontSize: Theme.FsBody, enabled: canEn);
                en.GetComponent<Image>().color = canEn ? Theme.BtnEnhance : Theme.BtnDisabledDark;
            }

            // Reforge (item shop): gamble the normal affix values with gold+scrap. Works on any item
            // (including equipped gear); disabled if it has no normal affixes or you can't afford it.
            if (item.Affixes.Exists(a => !Loot.IsImprintStat(a.Stat, _cfg)))
            {
                var (rg, rs) = Inventory.ReforgeCost(item, _cfg);
                bool canRf = Inventory.CanReforge(save, item.Id, _cfg);
                var rfRow = PanelKit.Row(_detail, FooterH);
                var rf = PanelKit.ButtonCell(rfRow, $"Reforge  {Num.CompactCeil(rg)}g + {Num.CompactCeil(rs)}s",
                    () => { _view.ReforgeItem(item.Id); ShowDetail(_view.CurrentSave, _view.CurrentSave.Inventory.Find(i => i.Id == item.Id)); },
                    fontSize: Theme.FsBody, enabled: canRf);
                rf.GetComponent<Image>().color = canRf ? Theme.BtnReforge : Theme.BtnDisabledDark;
            }

            // Lock toggle (works on BAG and EQUIPPED gear alike): a locked item can never be salvaged.
            // Sits in the action stack above the salvage row; equipped gear gets it too.
            BuildLockToggle(save, item);

            var owner = EquippedByWhom(save, item.Id);
            if (owner != null)
            {
                // Equipped gear can't be salvaged (the reducer throws) — show the owner instead,
                // with the §6.2 set tells counted against them (they're wearing it).
                CompareCard.SetLines(_detail, save, item, _cfg, owner);
                PanelKit.Label(_detail, $"Equipped by {HeroName(save, owner)}", Theme.FsLabel,
                    Theme.Info, TextAnchor.MiddleLeft);
                return;
            }

            // Compare-anywhere (10.5b): the full power delta vs the best fielded hero's slot — not
            // just the one-line "who does this help" headline, so a loose drop's worth is legible
            // in the bag without opening the Heroes screen.
            CompareCard.Build(_detail, save, item, _cfg, _view, heroId: null);

            // Locked loose items can't be salvaged — show a disabled placeholder instead of the salvage button.
            if (item.Locked)
            {
                var pRow = PanelKit.Row(_detail, SalvageH);
                var protd = PanelKit.ButtonCell(pRow, "Locked — unlock to salvage", () => { },
                    fontSize: Theme.FsBody, enabled: false);
                protd.GetComponent<Image>().color = Theme.BtnDisabledDark;
                var plbl = protd.GetComponentInChildren<Text>();
                if (plbl != null) plbl.color = Theme.LockGold;
                return;
            }

            // Imprint-guarded gear can't be salvaged while the guard is on (SalvageItem throws) —
            // the same disabled-placeholder treatment the Locked state gets, in imprint violet.
            if (save.Progress.Loot.NeverSalvageImprinted && Inventory.IsImprinted(item, _cfg))
            {
                var gRow = PanelKit.Row(_detail, SalvageH);
                var guarded = PanelKit.ButtonCell(gRow, "✦ Imprinted — guarded by the loot filter", () => { },
                    fontSize: Theme.FsBody, enabled: false);
                guarded.GetComponent<Image>().color = Theme.BtnDisabledDark;
                var glbl = guarded.GetComponentInChildren<Text>();
                if (glbl != null) glbl.color = Theme.Imprint;
                return;
            }

            // Loose item -> manual salvage. Unique and above take a second click to confirm.
            long worth = _cfg.Balance.ScrapValue(item.Rarity, item.ItemLevel);
            if (_confirmSalvageId == item.Id)
            {
                var cRow = PanelKit.Row(_detail, SalvageH);
                PanelKit.ButtonCell(cRow, $"Confirm salvage  +{worth}", () => DoSalvage(save, item), fontSize: Theme.FsH2)
                    .GetComponent<Image>().color = Theme.BtnDangerArmed;
                var xRow = PanelKit.Row(_detail, FooterH);
                PanelKit.FlexSpacer(xRow);
                PanelKit.ButtonCell(xRow, "Cancel", () => { _confirmSalvageId = null; ShowDetail(save, item); },
                    width: CancelW, fontSize: FsMid);
                PanelKit.FlexSpacer(xRow);
            }
            else
            {
                var sRow = PanelKit.Row(_detail, SalvageH);
                PanelKit.ButtonCell(sRow, $"Salvage  +{worth} scrap",
                    () =>
                    {
                        if (item.Rarity >= Rarity.Unique) { _confirmSalvageId = item.Id; ShowDetail(save, item); }
                        else DoSalvage(save, item);
                    }, fontSize: Theme.FsH2);
            }
        }

        private void DoSalvage(SaveState save, Item item)
        {
            _confirmSalvageId = null;
            _view.ReplaceSave(Inventory.SalvageItem(save, item.Id, _cfg));
            Rebuild();
        }

        /// <summary>The per-item padlock button (right-aligned in the detail action stack): flips
        /// Item.Locked via the pure reducer. Works on bag AND equipped gear. Toggling refreshes the
        /// whole panel so the tile badge + salvage button track the new state.</summary>
        private void BuildLockToggle(SaveState save, Item item)
        {
            if (_detail == null) return;
            var row = PanelKit.Row(_detail, Theme.TouchMin); // Lock/Locked button: pin the 44 touch floor (10.13c)
            PanelKit.FlexSpacer(row);
            var btn = PanelKit.ButtonCell(row, item.Locked ? "Locked" : "Lock", () =>
                {
                    _view.ToggleItemLock(item.Id);
                    Rebuild(); // re-open the panel so tiles/badges refresh, then re-show this item
                    ShowDetail(_view.CurrentSave, _view.CurrentSave.Inventory.Find(i => i.Id == item.Id));
                }, width: LockW, fontSize: Theme.FsLabel);
            btn.GetComponent<Image>().color = item.Locked ? Theme.BtnLockOn : Theme.BtnLockOff;
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.color = item.Locked ? Theme.LockGold : Theme.TextBright;
        }

        // ---- loot filter control (10.5a: per-slot auto-salvage floors + imprint guard) ----

        // The selectable floors, low→high. Legendary/Mythic are intentionally absent:
        // they're the top boss-only chase tiers, so auto-salvage never touches them.
        // Unique IS offered for the late game where Unique drops become churn.
        // "& below" matches Inventory.WouldAutoSalvage's `Rarity <= floor` semantics.
        private static readonly (Rarity? max, string label)[] AutoSalvageOptions =
        {
            (null, "Off"),
            (Rarity.Normal, "Normal"),
            (Rarity.Rare, "Rare & below"),
            (Rarity.Unique, "Unique & below"),
        };

        private static string AutoSalvageLabel(Rarity? max)
        {
            foreach (var o in AutoSalvageOptions) if (o.max == max) return MarkPrefix(o.max) + o.label;
            return "Off";
        }

        /// <summary>"● " / "■ " / … before a floor label — the 10.20b glyph channel on the filter's
        /// rarity-colored rows (and the header summary, which shares the label). Empty for Off/Normal,
        /// so unmarked tiers collapse cleanly instead of carrying a stray space.</summary>
        private static string MarkPrefix(Rarity? r)
        {
            if (r == null) return "";
            string mark = Palette.RarityMark(r.Value);
            return mark.Length > 0 ? mark + " " : "";
        }

        private static Color AutoSalvageColor(Rarity? max) =>
            max == null ? Theme.ToggleOff : Palette.Rarity(max.Value);

        /// <summary>The option after <paramref name="cur"/> in the cycle (a slot's button click).</summary>
        private static Rarity? NextOption(Rarity? cur)
        {
            for (int i = 0; i < AutoSalvageOptions.Length; i++)
                if (AutoSalvageOptions[i].max == cur)
                    return AutoSalvageOptions[(i + 1) % AutoSalvageOptions.Length].max;
            return null;
        }

        /// <summary>The shared floor when every active slot agrees: (uniform, value). No floors at
        /// all reads as uniform Off; a partial or mixed spread is not uniform ("Custom").</summary>
        private static (bool uniform, Rarity? shared) SharedFloor(LootFilterState f)
        {
            if (f.SalvageMaxBySlot.Count == 0) return (true, null);
            if (f.SalvageMaxBySlot.Count != EquipSlots.Active.Length) return (false, null);
            Rarity? first = null;
            foreach (var slot in EquipSlots.Active)
            {
                if (!f.SalvageMaxBySlot.TryGetValue(slot, out var v)) return (false, null);
                if (first == null) first = v;
                else if (v != first) return (false, null);
            }
            return (true, first);
        }

        /// <summary>The loot-filter header button (dropdown header): "Loot filter: &lt;summary&gt;"
        /// where the summary is Off / the shared floor label / Custom. Click to expand/collapse;
        /// the panel itself is the canvas overlay built by <see cref="BuildLootFilterDropdown"/>.</summary>
        private RectTransform BuildLootFilter(RectTransform header)
        {
            var filter = _view.CurrentSave.Progress.Loot;
            var (uniform, shared) = SharedFloor(filter);
            string summary = uniform ? AutoSalvageLabel(shared) : "Custom";
            var btn = PanelKit.ButtonCell(header, $"Loot filter: {summary}  {(_filterOpen ? "▴" : "▾")}",
                () => { _filterOpen = !_filterOpen; Rebuild(); },
                width: AutoSalvageW, fontSize: Theme.FsBody);
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.color = uniform ? AutoSalvageColor(shared) : Theme.TextBright;
            return (RectTransform)btn.transform;
        }

        /// <summary>The expanded loot-filter panel: one cycle row per equip slot, an "All slots"
        /// row of the four floor options, and the imprint-guard toggle. Layout groups can't express
        /// overlays, so this is an anchored overlay parented to the CANVAS (not a layout child):
        /// its anchor is DERIVED from the header button's laid-out rect — the sanctioned exception
        /// (same class as WindowSizer centering), not a hand-placed coordinate.
        /// <see cref="DropdownFollow"/> re-derives it every LateUpdate, so it stays glued even
        /// while the first frame's layout settles or the window resizes. Content inside is still
        /// layout-group driven. Every mutation routes through the view and rebuilds, so the rows
        /// always show live save state (the dropdown stays open across the rebuild).</summary>
        private void BuildLootFilterDropdown(Canvas canvas, RectTransform panelRt, RectTransform headerBtn)
        {
            if (!_filterOpen) return;
            var filter = _view.CurrentSave.Progress.Loot;

            var root = new GameObject("LootFilterDropdown", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false); // last canvas child ⇒ draws/raycasts above the window
            root.AddComponent<Image>().color = Theme.BgDropdown; // backdrop; also blocks hover on tiles beneath

            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)Theme.GapXs, (int)Theme.GapXs, (int)Theme.GapXs, (int)Theme.GapXs);
            vlg.spacing = Theme.GapXs;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // height from the rows; width from DropdownFollow

            var rootRt = (RectTransform)root.transform;

            // One row per slot: label + a cycle button showing the current floor (click = next option;
            // cycling four options beats a nested dropdown-in-a-dropdown).
            foreach (var slot in EquipSlots.Active)
            {
                var s = slot; // capture
                Rarity? cur = filter.SalvageMaxBySlot.TryGetValue(slot, out var v) ? v : (Rarity?)null;
                var row = PanelKit.Row(rootRt, DropRowH);
                PanelKit.TextCell(row, slot.ToString(), FsMid, Theme.TextBody, TextAnchor.MiddleLeft, width: SlotLabelW);
                var cb = PanelKit.ButtonCell(row, AutoSalvageLabel(cur),
                    () => { _view.SetSalvageFloor(s, NextOption(cur)); Rebuild(); }, fontSize: Theme.FsSmall);
                var cl = cb.GetComponentInChildren<Text>();
                if (cl != null) cl.color = AutoSalvageColor(cur);
            }

            // "All slots": the four floors as one-click buttons (compact labels — the per-slot rows
            // spell out "& below"). The active one highlights only when every slot already agrees.
            var (uniform, shared) = SharedFloor(filter);
            var all = PanelKit.Row(rootRt, DropRowH);
            PanelKit.TextCell(all, "All slots", FsMid, Theme.TextMuted, TextAnchor.MiddleLeft, width: SlotLabelW);
            foreach (var (max, _) in AutoSalvageOptions)
            {
                var m = max; // capture
                string shortLbl = max == null ? "Off" : MarkPrefix(max) + max.Value.ToString();
                bool selected = uniform && shared == max;
                var ab = PanelKit.ButtonCell(all, shortLbl,
                    () => { _view.SetSalvageFloorAll(m); Rebuild(); }, fontSize: Theme.FsSmall);
                if (selected) ab.GetComponent<Image>().color = Theme.DropdownSelected;
                var al = ab.GetComponentInChildren<Text>();
                if (al != null) al.color = AutoSalvageColor(max);
            }

            // Imprint guard: On = imprinted gear refuses every salvage path (the Locked precedent).
            bool guard = filter.NeverSalvageImprinted;
            var gRow = PanelKit.Row(rootRt, DropRowH);
            PanelKit.TextCell(gRow, "Keep imprinted", FsMid, guard ? Theme.Imprint : Theme.TextBody,
                TextAnchor.MiddleLeft, flex: 1f);
            var gb = PanelKit.ButtonCell(gRow, guard ? "On" : "Off",
                () => { _view.SetImprintGuard(!guard); Rebuild(); }, width: GuardBtnW, fontSize: FsMid);
            var gl = gb.GetComponentInChildren<Text>();
            if (gl != null) gl.color = guard ? Theme.Imprint : Theme.ToggleOff;

            var follow = root.AddComponent<DropdownFollow>();
            follow.Button = headerBtn;
            follow.CanvasRt = (RectTransform)canvas.transform;
            // Wider than the header: four floor buttons share the All-slots row, and even the
            // compact labels wrap mid-word at the button's own width ("Normal" needs the most).
            follow.ExtraWidth = 150f;

            // One-shot settle so the overlay lands right on its first visible frame; the follow
            // component keeps it correct from then on.
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRt);
            follow.Apply();
        }

        // ---- mass salvage ----

        private const float MassSalvageConfirmSeconds = 3f;

        /// <summary>How many loose, unlocked items "Salvage all" would scrap (matches the reducer's filter).</summary>
        private int SalvageableCount(SaveState save)
        {
            int n = 0;
            foreach (var it in save.Inventory)
                if (!it.Locked && EquippedByWhom(save, it.Id) == null) n++;
            return n;
        }

        /// <summary>One-click bag cleanup: scrap EVERY loose, unlocked item regardless of rarity. Because
        /// this now destroys rares/uniques/legendaries, the first click ARMS a confirm ("Confirm salvage
        /// all (N)") for a few seconds; the second click executes. Locked items are always spared.</summary>
        private void BuildMassSalvage(RectTransform footer)
        {
            var save = _view.CurrentSave;
            int n = SalvageableCount(save);
            bool enabled = n > 0;

            string label = !enabled ? "Salvage all"
                         : _confirmMassSalvage ? $"Confirm salvage all ({n})"
                         : "Salvage all";
            var btn = PanelKit.ButtonCell(footer, label, OnMassSalvageClick,
                width: SalvageAllW, fontSize: Theme.FsBody, enabled: enabled);
            btn.GetComponent<Image>().color = !enabled ? Theme.BtnDisabledDark
                : _confirmMassSalvage ? Theme.BtnDangerArmed // armed = hot red
                : Theme.BtnDanger;
        }

        private void OnMassSalvageClick()
        {
            if (!_confirmMassSalvage)
            {
                // First click arms; auto-disarm after a few seconds so a stray click can't destroy the bag.
                // The timer must start AFTER the rebuild: RebuildKeepConfirm → Close stops any running
                // timer, so starting first left the confirm armed forever (pre-10.3 bug, fixed here).
                _confirmMassSalvage = true;
                RebuildKeepConfirm();
                if (_massSalvageTimer != null) StopCoroutine(_massSalvageTimer);
                if (gameObject.activeInHierarchy) _massSalvageTimer = StartCoroutine(DisarmMassSalvageAfter());
                return;
            }
            // Second click within the window: execute.
            _confirmMassSalvage = false;
            _view.SalvageAll();
            Rebuild();
        }

        private System.Collections.IEnumerator DisarmMassSalvageAfter()
        {
            yield return new WaitForSecondsRealtime(MassSalvageConfirmSeconds);
            _massSalvageTimer = null;
            if (_panel != null && _confirmMassSalvage) { _confirmMassSalvage = false; RebuildKeepConfirm(); }
        }

        // ---- bulk-select salvage (10.5c) ----

        // Selection tint: Theme.BtnSelected ghosted over the tile so it reads as "picked" without
        // fighting the rarity border (which stays fully visible around it).
        private static readonly Color SelectedTint =
            new Color(Theme.BtnSelected.r, Theme.BtnSelected.g, Theme.BtnSelected.b, 0.45f);

        /// <summary>The footer mode toggle. Entering select mode collapses the loot-filter dropdown
        /// (its overlay would eat sweep raycasts over the top tile rows); leaving — by toggle or by
        /// closing the window (see <see cref="Toggle"/>) — discards the selection.</summary>
        private void BuildSelectToggle(RectTransform footer)
        {
            var btn = PanelKit.ButtonCell(footer, "Select", ToggleSelectMode, width: SelectW, fontSize: Theme.FsBody);
            if (_selectMode) btn.GetComponent<Image>().color = Theme.BtnSelected;
        }

        private void ToggleSelectMode()
        {
            _selectMode = !_selectMode;
            _selected.Clear(); // both directions start clean — a stale pick must never carry over
            _sweeping = false;
            if (_selectMode) _filterOpen = false;
            Rebuild(); // also disarms a pending mass-salvage confirm
        }

        /// <summary>Select-mode wiring for one bag tile. Eligible tiles toggle on press and join a
        /// press-drag sweep; a sweep only ever SETS selected — dragging back over a tile never
        /// deselects it (the mobile-inventory idiom). Ineligible tiles (equipped / locked /
        /// imprint-guarded — the same set <see cref="Inventory.SalvageMany"/> refuses) get NO relay:
        /// clicks and sweeps pass over them, so the footer never promises scrap it can't deliver.
        /// They keep their existing tells (equipped shows in detail, [L] and ✦ badges).</summary>
        private void WireSelectTile(GameObject tile, SaveState save, Item it)
        {
            bool guard = save.Progress.Loot.NeverSalvageImprinted;
            bool eligible = !it.Locked
                         && EquippedByWhom(save, it.Id) == null
                         && !(guard && Inventory.IsImprinted(it, _cfg));
            if (!eligible) return;

            // Stretch-fill tint above the tile art; enabled tracks membership in the selection.
            var ovGo = new GameObject("Selected", typeof(RectTransform));
            ovGo.transform.SetParent(tile.transform, false);
            var ovRt = (RectTransform)ovGo.transform;
            ovRt.anchorMin = Vector2.zero; ovRt.anchorMax = Vector2.one;
            ovRt.offsetMin = Vector2.zero; ovRt.offsetMax = Vector2.zero;
            var overlay = ovGo.AddComponent<Image>();
            overlay.color = SelectedTint;
            overlay.raycastTarget = false;
            overlay.enabled = _selected.Contains(it.Id);

            var relay = tile.AddComponent<SelectRelay>();
            relay.OnPress = () =>
            {
                _sweeping = true;
                if (!_selected.Add(it.Id)) _selected.Remove(it.Id); // a plain press toggles
                overlay.enabled = _selected.Contains(it.Id);
                UpdateSelectedFooter();
            };
            relay.OnRelease = () => _sweeping = false;
            relay.OnSweepEnter = () =>
            {
                if (!_sweeping) return;
                _selected.Add(it.Id); // sweep SETS — re-crossing never toggles off
                overlay.enabled = true;
                UpdateSelectedFooter();
            };
        }

        /// <summary>Select mode's static detail pane: the pane is a usage hint, not a hover target.</summary>
        private void ShowSelectHint()
        {
            if (_detail == null) return;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            PanelKit.Flex(_detail);
            PanelKit.Label(_detail, "Tap items to select — drag to sweep.", Theme.FsBody, Theme.TextMuted, TextAnchor.MiddleCenter);
            PanelKit.Flex(_detail);
        }

        /// <summary>The select-mode salvage verb. No arm/confirm step — unlike "Salvage all", the
        /// player hand-picked every item — but it keeps the danger red so it still reads destructive.
        /// Label/enabled refresh IN PLACE via <see cref="UpdateSelectedFooter"/>.</summary>
        private void BuildSalvageSelected(RectTransform footer)
        {
            var btn = PanelKit.ButtonCell(footer, "", DoSalvageSelected, width: SalvageSelW, fontSize: Theme.FsBody);
            _selSalvageBtn = btn;
            _selSalvageLbl = btn.GetComponentInChildren<Text>();
            UpdateSelectedFooter();
        }

        /// <summary>Refresh the select-mode footer button in place — selection changes during a
        /// sweep must NOT rebuild the panel (destroying the tile under a pressed pointer kills the
        /// sweep). Count/scrap are recomputed against the live save, so a stale id drops out of the
        /// promise instead of inflating it (display floors — CLAUDE.md rounding).</summary>
        private void UpdateSelectedFooter()
        {
            if (_selSalvageBtn == null || _selSalvageLbl == null) return;
            var save = _view.CurrentSave;
            int n = 0;
            long scrap = 0;
            foreach (var it in save.Inventory)
                if (_selected.Contains(it.Id)) { n++; scrap += _cfg.Balance.ScrapValue(it.Rarity, it.ItemLevel); }
            _selSalvageLbl.text = $"Salvage {n} selected  (+{Num.CompactFloor(scrap)} scrap)";
            _selSalvageBtn.interactable = n > 0;
            _selSalvageBtn.GetComponent<Image>().color = n > 0 ? Theme.BtnDanger : Theme.BtnDisabledDark;
        }

        private void DoSalvageSelected()
        {
            var ids = new List<string>(_selected);
            _selectMode = false; // done: back to browse mode on the post-salvage rebuild
            _selected.Clear();
            _sweeping = false;
            _view.SalvageSelected(ids); // posts the feed line; stale ids skip inside the reducer
            Rebuild();
        }

        // ---- auto-equip-if-better toggle ----

        /// <summary>A simple on/off toggle (Lever 2): when on, a banked drop that's a genuine power
        /// upgrade for a fielded hero auto-equips. Sits in the header beside auto-salvage — both
        /// govern what happens to drops automatically.</summary>
        private void BuildAutoEquip(RectTransform header)
        {
            bool on = Settings.AutoEquipUpgrades;
            var btn = PanelKit.ButtonCell(header, $"Auto-equip: {(on ? "On" : "Off")}",
                () => { Settings.AutoEquipUpgrades = !on; Rebuild(); },
                width: AutoEquipW, fontSize: Theme.FsBody);
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.color = on ? UpgradeTell.Up : Theme.ToggleOff;
        }

        // ---- helpers ----

        /// <summary>Pin a small "[L]" lock tag to a bag tile's bottom-left corner when the item is locked
        /// (protected from all salvage). Bottom-left keeps it clear of the ▲ / ✦ corner badges. Uses the
        /// [L] tag rather than an emoji so it renders in Unity's default font. (Tile-corner pin — the
        /// badge class of positional exception, same as UpgradeTell's badges.)</summary>
        private static void LockBadgeTile(GameObject tile)
        {
            var lbl = UiKit.Label(tile.transform, "[L]", Theme.FsTiny, TextAnchor.LowerLeft, new Vector2(24, 14), Vector2.zero);
            var rt = (RectTransform)lbl.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(2f, 2f);
            lbl.color = Theme.LockGold;
            lbl.raycastTarget = false;
        }

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

    /// <summary>Keeps the auto-salvage dropdown glued under its header button: every LateUpdate the
    /// overlay's position/width are re-DERIVED from the button's laid-out world rect, converted into
    /// canvas-local units (InverseTransformPoint folds in Canvas.scaleFactor). Deriving each frame —
    /// rather than once at build — makes it robust to the first frame's unsettled layout and to
    /// window resizes. Height stays owned by the overlay's own ContentSizeFitter.</summary>
    public sealed class DropdownFollow : MonoBehaviour
    {
        public RectTransform Button = null!;
        public RectTransform CanvasRt = null!;
        /// <summary>Widens the panel beyond the button (centered) — for dropdowns whose rows
        /// need more room than the header button is wide (the loot filter's All-slots row).</summary>
        public float ExtraWidth;
        private static readonly Vector3[] Corners = new Vector3[4];

        private void LateUpdate() => Apply();

        public void Apply()
        {
            if (Button == null || CanvasRt == null) return;
            Button.GetWorldCorners(Corners);
            Vector3 bl = CanvasRt.InverseTransformPoint(Corners[0]); // bottom-left
            Vector3 br = CanvasRt.InverseTransformPoint(Corners[3]); // bottom-right
            float w = br.x - bl.x;
            if (w <= 0f) return; // button not laid out yet — converge on a later frame

            var rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f); // hang below the button
            rt.sizeDelta = new Vector2(w + Theme.GapS + ExtraWidth, rt.sizeDelta.y); // a hair wider than the button
            rt.anchoredPosition = new Vector2((bl.x + br.x) * 0.5f, bl.y - Theme.GapXs);
        }
    }

    /// <summary>
    /// Pointer relay for bulk-select tiles (the <see cref="HoverProxy"/> class of component): press
    /// toggles, press-drag sweeps via enter events, release ends the sweep (uGUI delivers pointer-up
    /// to the pressed object even after the pointer leaves it, so the sweep can't stick on). The
    /// empty drag handlers are load-bearing: they CLAIM the drag so the bag's ScrollRect doesn't
    /// scroll the grid out from under a sweep (wheel scrolling is untouched; dragging from a
    /// non-selectable tile or the gaps still scrolls).
    /// </summary>
    public sealed class SelectRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                                      IPointerEnterHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action? OnPress;
        public Action? OnRelease;
        public Action? OnSweepEnter;

        public void OnPointerDown(PointerEventData e) => OnPress?.Invoke();
        public void OnPointerUp(PointerEventData e) => OnRelease?.Invoke();
        public void OnPointerEnter(PointerEventData e) => OnSweepEnter?.Invoke();
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
    }
}
