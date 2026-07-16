#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The unified "Heroes" screen (uGUI) — the one hub for managing the roster. Left rail:
    /// the party slots (who's fielded) over a list of every owned hero; click to select.
    /// Right pane: the selected hero's header (name/level + Field/Bench, a live farm-only
    /// swap) and three sub-tabs — <b>Equipment</b> (a body-mapped 9-slot doll + the shared
    /// account bag, hover-compare, one-click equip), <b>Skills</b> (read-only until the
    /// Skills milestone), and <b>Stats</b>. Opened from the control bar (no hero ⇒ first
    /// party hero) or a Party-HUD chip (that hero). All mutations go through the pure
    /// <see cref="Inventory"/> / <see cref="Party"/> reducers via <see cref="CombatView"/>.
    ///
    /// Built on <see cref="PanelKit"/> + <see cref="Theme"/> (10.3): the whole tree is
    /// anchor/layout-group driven — no anchoredPosition, no y-cursors, every color/size
    /// comes from Theme. Rarity/verdict colors stay data-driven (Palette / UpgradeTell).
    /// </summary>
    public sealed class EquipmentView : MonoBehaviour
    {
        // Doll laid out like the body — 5 slots in a cross (2026-07-02 trim): helm on the
        // head, chest at the core, weapon/gloves at the hands, boots at the feet.
        private static readonly (EquipSlot slot, int col, int row)[] Cells =
        {
                                      (EquipSlot.Helm,  1, 0),
            (EquipSlot.Weapon, 0, 1), (EquipSlot.Chest, 1, 1), (EquipSlot.Gloves, 2, 1),
                                      (EquipSlot.Boots, 1, 2),
        };

        private const float DollCell = 84f;   // doll tile edge (the 3×3 cross)
        private const float BagTile = 70f;     // shared-bag tile edge
        private const float RailW = 220f;      // fixed left-rail width
        private const float SideColW = 284f;   // fixed doll / detail column width (grid is 268 wide)
        private const float ActionW = 140f;    // header action-button width

        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;
        private string? _heroId;
        private RectTransform? _detail;   // Equipment-tab compare/detail pane (updated on hover)

        private enum SubTab { Equipment, Skills, Stats }
        private SubTab _subTab = SubTab.Equipment;

        public bool IsOpen => _panel != null;

        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }

        /// <summary>Open straight to a specific hero (Party-HUD chip); toggle shut if already on it.</summary>
        public void Toggle(string heroId)
        {
            if (_panel != null && _heroId == heroId) { Close(); return; }
            _heroId = heroId;
            _subTab = SubTab.Equipment;
            Rebuild();
        }

        /// <summary>Open/close the hub on the current/first hero (the control-bar "Heroes" button).</summary>
        public void ToggleDefault()
        {
            if (_panel != null) { Close(); return; }
            Rebuild(); // Build() resolves _heroId to a valid hero
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
            if (_heroId == null || !save.Heroes.Exists(h => h.Id == _heroId))
                _heroId = FirstPartyHeroId(save);
            if (_heroId == null) return;

            // Fully opaque backdrop: this is a full-screen management view, so it covers the HUD
            // chrome (TopBar, chat) behind it instead of letting it bleed through a dim.
            _panel = PanelKit.Window(transform, "Heroes", Close, out var body, "HeroesCanvas", sortOrder: 96);

            var cols = PanelKit.Columns(body, (0f, RailW), (1f, 0f));
            BuildSelector(cols[0], save);
            BuildRightPane(cols[1], save);
        }

        // ---- left rail: party slots + all owned heroes (+ swap stack when full) ----

        private void BuildSelector(RectTransform rail, SaveState save)
        {
            int fielded = PartyFieldedCount(save);
            string? leaderId = Party.EffectiveLeader(save, _cfg); // ★ marks who leads the formation

            var party = PanelKit.Section(rail, $"Party  ·  {fielded}/{save.Party.Length}");
            // 10.13c: the rail's ListRows would default to the new 44 touch floor, but this rail is a
            // NON-scrolling fixed column (party + full roster + optional swap stack). At 44 an 8-hero
            // roster overflows the ~528-unit rail and the window's RectMask2D crops the bottom rows
            // (Part 2 starvation trap). Pinned to 40 as the sanctioned anti-starvation trade — a hair
            // under the floor but still comfortably tappable; the real fix (a roster scroll) is a
            // future slice. (Flagged: even 40 clips at >= ~8 heroes; the live roster fits.)
            const float RailRowH = 40f;
            for (int i = 0; i < save.Party.Length; i++)
            {
                string? id = save.Party[i];
                bool filled = id != null;
                string label = filled
                    ? $"{i + 1}.  {(id == leaderId ? "★ " : "")}{HeroName(save, id)}"
                    : $"{i + 1}.  — empty —";
                PanelKit.ListRow(party, label, () => { if (filled) SelectHero(id!); },
                    selected: filled && id == _heroId, muted: !filled, height: RailRowH);
            }

            var roster = PanelKit.Section(rail, "All heroes");
            foreach (var hero in save.Heroes)
            {
                var id = hero.Id;
                bool isFielded = Array.IndexOf(save.Party, id) >= 0;
                PanelKit.ListRow(roster, isFielded ? HeroName(save, id) : HeroName(save, id) + "  (B)",
                    () => SelectHero(id), selected: id == _heroId, muted: !isFielded, fontSize: Theme.FsBody,
                    height: RailRowH);
            }

            // Party FULL (§3): a benched, selected hero swaps in for a fielded one. One "↔ <Name>"
            // row per fielded hero, gated on CanEditParty (farm-only) — the reducer takes that hero's
            // exact slot. Only shown when there's no empty slot to plain-Field into.
            int slot = Array.IndexOf(save.Party, _heroId);
            bool selectedBenched = slot < 0;
            bool full = Array.IndexOf(save.Party, (string?)null) < 0;
            if (selectedBenched && full)
            {
                var swap = PanelKit.Section(rail, "Swap in for:");
                foreach (var fid in save.Party)
                {
                    if (fid == null) continue;
                    string outgoing = fid;
                    PanelKit.ListRow(swap, $"↔  {HeroName(save, outgoing)}",
                        () => { _view.ApplyPartyEdit(Party.SwapHero(save, _heroId!, outgoing)); Rebuild(); },
                        enabled: _view.CanEditParty, fontSize: Theme.FsLabel, height: RailRowH);
                }
            }
        }

        private void SelectHero(string heroId) { _heroId = heroId; Rebuild(); }

        // ---- right pane: hero header + field/bench + sub-tabs ----

        private void BuildRightPane(RectTransform right, SaveState save)
        {
            BuildHeroHeader(right, save);

            string[] tabs = { "Equipment", "Skills", "Stats" };
            PanelKit.TabBar(right, tabs, (int)_subTab, i => { _subTab = (SubTab)i; Rebuild(); });

            // Tab content stacks directly into the right column (a VerticalLayoutGroup): the fill
            // element in each pane (Columns host / scroll list / stat Section) absorbs the slack.
            switch (_subTab)
            {
                case SubTab.Skills: BuildSkillsPane(right, save); break;
                case SubTab.Stats:  BuildStatsPane(right, save);  break;
                default:            BuildEquipment(right, save);  break;
            }
        }

        private void BuildHeroHeader(RectTransform right, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId);
            if (hero == null) return;

            int slot = Array.IndexOf(save.Party, _heroId);
            bool fielded = slot >= 0;
            int fieldedCount = PartyFieldedCount(save);
            bool canEdit = _view.CanEditParty;
            string? effectiveLeader = Party.EffectiveLeader(save, _cfg);

            // Row A: name + the primary Field/Bench action (right).
            var rowA = PanelKit.Row(right, Theme.BtnHs);
            PanelKit.TextCell(rowA, HeroName(save, _heroId), Theme.FsH1, Theme.TextBright, TextAnchor.MiddleLeft, flex: 1f);
            if (fielded)
            {
                // Last-hero guard (§4): benching the only fielded hero is a no-op (the reducer refuses
                // to strand the party). Surface that — grey + relabel the button, inert.
                bool onlyHero = fieldedCount == 1;
                PanelKit.ButtonCell(rowA, onlyHero ? "Last hero" : "Bench",
                    () => { _view.ApplyPartyEdit(Party.SetPartySlot(save, slot, null)); Rebuild(); },
                    width: ActionW, fontSize: Theme.FsH2, enabled: canEdit && !onlyHero);
            }
            else
            {
                int firstEmpty = Array.IndexOf(save.Party, (string?)null);
                if (firstEmpty >= 0)
                    PanelKit.ButtonCell(rowA, "Field",
                        () => { _view.ApplyPartyEdit(Party.FieldHero(save, firstEmpty, _heroId!)); Rebuild(); },
                        width: ActionW, fontSize: Theme.FsH2, enabled: canEdit);
                else
                    // Party full: the swap stack lives in the left rail; a muted hint points at it.
                    PanelKit.TextCell(rowA, "Party full", Theme.FsSmall, Theme.TextDim, TextAnchor.MiddleCenter, width: ActionW);
            }

            // Row B: level/fielded status + the leader toggle (right, fielded only).
            var rowB = PanelKit.Row(right, Theme.BtnHs);
            PanelKit.TextCell(rowB, fielded ? $"Level {hero.Level} · fielded (slot {slot + 1})" : $"Level {hero.Level} · benched",
                Theme.FsSmall, Theme.TextMuted, TextAnchor.MiddleLeft, flex: 1f);
            if (fielded)
            {
                // Leader toggle: the chosen hero leads the formation; the rest fall in behind. Safe to
                // change any time (it only re-points who's followed), so not farm-gated.
                bool isLeader = _heroId == effectiveLeader;
                PanelKit.ButtonCell(rowB, isLeader ? "★ Leader" : "Make Leader",
                    () => { _view.SetLeader(_heroId); Rebuild(); },
                    width: ActionW, fontSize: Theme.FsH2, enabled: !isLeader);
            }

            // Row C — leader badge (§2): who actually leads and whether it was chosen or auto-derived.
            // Bright gold for the explicit pick, muted gold for the computed default. Only when this
            // hero is the effective leader.
            if (_heroId == effectiveLeader)
            {
                bool explicitPick = save.LeaderHeroId == _heroId;
                var rowC = PanelKit.Row(right, 18f);
                PanelKit.TextCell(rowC, explicitPick ? "★ Leader" : "★ Leader (auto)", Theme.FsSmall,
                    explicitPick ? Theme.AccentGold : Theme.LeaderAuto, TextAnchor.MiddleLeft, flex: 1f);
            }

            BuildAscensionRow(right, save, hero);
        }

        // ---- ascension (10.17, MM5): star display + Star Up + the two shard wallets ----

        /// <summary>Star row for the selected hero: a non-interactive "★★☆☆☆" (gold filled, dim empty) beside
        /// a Star Up button priced off <see cref="Ascension.NextStarCost"/> and gated on
        /// <see cref="Ascension.CanStarUp"/> (own-wallet + universal ≥ cost). A maxed hero swaps the button for
        /// a dim "MAX ★N" cell (the "Party full" placeholder idiom). Under it, a muted wallet line splits the
        /// two shard pools. The click runs the <see cref="CombatView.NavStarUp"/> seam and Rebuilds off the
        /// moved stars/cost/wallets — matching how loadout/enhance verbs refresh the pane.</summary>
        private void BuildAscensionRow(RectTransform right, SaveState save, HeroInstance hero)
        {
            int max = _cfg.Balance.AscensionMaxStars;
            int stars = Mathf.Clamp(hero.Stars, 0, max);
            long cost = Ascension.NextStarCost(hero.Stars, _cfg);
            bool maxed = cost < 0;
            long heroShards = Ascension.ShardsFor(save, hero.DefId);
            long universal = Ascension.UniversalShards(save);

            // Rich-text stars so one label carries both colors (gold filled + dim empty).
            string gold = ColorUtility.ToHtmlStringRGB(Theme.GachaGold);
            string dim = ColorUtility.ToHtmlStringRGB(Theme.TextDim);
            string starStr = $"<color=#{gold}>{new string('★', stars)}</color><color=#{dim}>{new string('☆', max - stars)}</color>";

            var row = PanelKit.Row(right, Theme.TouchMin); // Star Up button: pin the 44 touch floor (10.13c)
            PanelKit.TextCell(row, starStr, Theme.FsH1, Theme.GachaGold, TextAnchor.MiddleLeft, flex: 1f);
            if (maxed)
                PanelKit.TextCell(row, $"MAX ★{max}", Theme.FsSmall, Theme.TextDim, TextAnchor.MiddleCenter, width: ActionW);
            else
                PanelKit.ButtonCell(row, $"Star Up  ({Num.CompactFloor(cost)} shards)",
                    () => { if (_view.NavStarUp(_heroId!).ok) Rebuild(); },
                    width: ActionW, fontSize: Theme.FsSmall, enabled: Ascension.CanStarUp(save, _heroId!, _cfg));

            // Wallet split: this hero's dupe wallet (spent first) + the universal pool (the flex remainder).
            PanelKit.TextCell(PanelKit.Row(right, 16f),
                $"{Num.CompactFloor(heroShards)} hero + {Num.CompactFloor(universal)} universal shards",
                Theme.FsSmall, Theme.TextMuted, TextAnchor.MiddleLeft, flex: 1f);
        }

        // ---- Equipment sub-tab: doll + shared bag + compare pane ----

        private void BuildEquipment(RectTransform right, SaveState save)
        {
            var host = PanelKit.Flex(right); // fills the space under the tab bar; Columns lays it out
            var cols = PanelKit.Columns(host, (0f, SideColW), (1f, 0f), (0f, SideColW));
            BuildDoll(cols[0], save);
            BuildBag(cols[1], save);

            var detail = PanelKit.Section(cols[2], null);
            detail.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f; // fill the column
            _detail = detail;
            ShowHeroStats(save); // default pane = the hero's stat sheet
        }

        private void BuildDoll(RectTransform col, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId)!;

            var section = PanelKit.Section(col, "Equipped");

            // A 3×3 GridLayoutGroup holds the body cross: 5 real slots + 4 invisible placeholders
            // (added in row-major order so each lands in its cell). No manual coordinates.
            var gridGo = new GameObject("Doll", typeof(RectTransform));
            gridGo.transform.SetParent(section, false);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(DollCell, DollCell);
            grid.spacing = new Vector2(Theme.GapS, Theme.GapS);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.MiddleCenter;
            float gridWH = DollCell * 3f + Theme.GapS * 2f;
            var gle = gridGo.AddComponent<LayoutElement>();
            gle.preferredWidth = gridWH; gle.preferredHeight = gridWH; gle.flexibleWidth = 1f;

            for (int row = 0; row < 3; row++)
                for (int colIdx = 0; colIdx < 3; colIdx++)
                {
                    var cell = FindCell(colIdx, row);
                    if (cell == null)
                    {
                        // Invisible placeholder so the cross keeps its shape in the grid.
                        var ph = new GameObject("gap", typeof(RectTransform));
                        ph.transform.SetParent(gridGo.transform, false);
                        continue;
                    }
                    var slot = cell.Value;
                    hero.Equipped.TryGetValue(slot, out var itemId);
                    var item = itemId != null ? save.Inventory.Find(i => i.Id == itemId) : null;
                    var tile = UiKit.ItemTile(gridGo.transform, new Vector2(DollCell, DollCell), Vector2.zero,
                        item?.Rarity, UiKit.SlotAbbrev(slot), raycast: true);
                    var captured = item;
                    tile.AddComponent<Button>().onClick.AddListener(
                        () => { SoundFx.Play("System_Shift_Click", 0.3f); ShowDetail(save, captured, slotIfEmpty: slot); }); // 10.9d tile tick
                    UiKit.Hover(tile, () => ShowDetail(save, captured, slotIfEmpty: slot), () => ShowHeroStats(save));
                }

            // Loadout snapshot verbs (10.5e): Save remembers this outfit; Wear re-equips every
            // surviving piece (never strips a slot — stale pieces skip, see Loadouts.Apply).
            var heroId = _heroId!;
            var lRow = PanelKit.Row(section, Theme.TouchMin); // Button-bearing (Save/Wear): pin the 44 touch floor (10.13c)
            PanelKit.ButtonCell(lRow, "Save loadout",
                () => { _view.SaveLoadout(heroId); Rebuild(); }, fontSize: Theme.FsSmall);
            PanelKit.ButtonCell(lRow, "Wear loadout",
                () => { _view.ApplyLoadout(heroId); Rebuild(); }, fontSize: Theme.FsSmall,
                enabled: hero.Loadout != null && hero.Loadout.Count > 0);
        }

        private static EquipSlot? FindCell(int col, int row)
        {
            foreach (var (slot, c, r) in Cells) if (c == col && r == row) return slot;
            return null;
        }

        private void BuildBag(RectTransform col, SaveState save)
        {
            int loose = Inventory.LooseCount(save);
            var section = PanelKit.Section(col, $"Bag (shared)  ·  {loose}/{_cfg.Balance.InventoryCap}");
            section.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var grid = UiKit.ScrollGridFill(section, new Vector2(BagTile, BagTile));

            bool any = false;
            var equipped = EquippedIds(save);
            foreach (var item in save.Inventory)
            {
                if (equipped.Contains(item.Id)) continue; // only free items can be equipped
                any = true;
                var it = item;
                var tile = UiKit.ItemTile(grid, new Vector2(BagTile, BagTile), Vector2.zero, it.Rarity,
                    UiKit.SlotAbbrev(SlotOf(it)), raycast: true);
                tile.AddComponent<Button>().onClick.AddListener(
                    () => { SoundFx.Play("System_Shift_Click", 0.3f); EquipFromBag(save, it); }); // one click to equip (10.9d tick)
                UiKit.Hover(tile, () => ShowDetail(save, it), () => ShowHeroStats(save));        // hover to compare
            }
            if (!any)
                PanelKit.Label(section, "No free items in the bag.", Theme.FsLabel, Theme.TextMuted, TextAnchor.MiddleCenter);
        }

        // ---- detail / compare pane (Equipment tab, right column) ----

        private void ShowDetail(SaveState save, Item? item, EquipSlot? slotIfEmpty = null)
        {
            if (_detail == null) return;
            ClearPane(_detail);

            if (item == null)
            {
                string msg = slotIfEmpty != null ? $"{slotIfEmpty} — empty\nHover a bag item to compare." : "Hover or click an item.";
                PanelKit.Flex(_detail);
                PanelKit.Label(_detail, msg, Theme.FsBody, Theme.TextMuted, TextAnchor.MiddleCenter);
                PanelKit.Flex(_detail);
                return;
            }

            var nameLbl = PanelKit.Label(_detail, StatDisplay.ItemName(item, _cfg), Theme.FsH2, Palette.Rarity(item.Rarity), TextAnchor.MiddleLeft);
            // Titled imprints ("Volatile … of Leeching") can run long — auto-shrink to fit one line.
            nameLbl.resizeTextForBestFit = true; nameLbl.resizeTextMaxSize = Theme.FsH2; nameLbl.resizeTextMinSize = 11;

            PanelKit.Label(_detail, StatDisplay.RarityTag(item.Rarity), Theme.FsSmall, Palette.Rarity(item.Rarity), TextAnchor.MiddleLeft);
            PanelKit.Label(_detail, $"{SlotOf(item)} · item level {item.ItemLevel}", Theme.FsSmall, Theme.TextBright, TextAnchor.MiddleLeft);

            var affixes = new List<Affix>(item.Affixes);
            affixes.Sort((x, z) => StatDisplay.Rank(x.Stat).CompareTo(StatDisplay.Rank(z.Stat)));
            foreach (var a in affixes)
            {
                if (Loot.IsImprintStat(a.Stat, _cfg)) // mechanical-mod signature: flavor, not a raw number
                    PanelKit.Label(_detail, $"✦ Imprinted — {StatDisplay.ImprintBlurb(a.Stat)}", Theme.FsSmall, Theme.Imprint, TextAnchor.MiddleLeft);
                else
                    PanelKit.Label(_detail, $"+{StatDisplay.Value(a.Stat, a.Value)} {StatDisplay.Label(a.Stat)}", Theme.FsSmall, Theme.TextBright, TextAnchor.MiddleLeft);
            }

            // §6.2 set tells vs the selected hero (n/4 counts the pieces they'd wear with this on).
            CompareCard.SetLines(_detail, save, item, _cfg, _heroId);

            bool equippedHere = EquippedByWhom(save, item.Id) == _heroId;
            if (equippedHere)
            {
                PanelKit.Label(_detail, "Equipped", Theme.FsSmall, Theme.Info, TextAnchor.MiddleLeft);
                PanelKit.Flex(_detail);
                var row = PanelKit.Row(_detail, Theme.BtnH);
                PanelKit.ButtonCell(row, "Unequip",
                    () => { _view.ReplaceSave(Inventory.UnequipItem(save, _heroId!, SlotOf(item))); Rebuild(); });
            }
            else
            {
                PanelKit.Label(_detail, $"vs {HeroName(save, _heroId)}'s {SlotOf(item)}:", Theme.FsSmall, Theme.TextMuted, TextAnchor.MiddleLeft);

                var (before, after) = Inventory.ComparePairForHero(save, _heroId!, item, _cfg);

                // One-line power verdict (Lever 2), then the derived deltas (B2) behind it.
                int stage = save.Progress.CurrentStage;
                var eval = Upgrades.EvaluateForHero(save, _heroId!, item, _cfg, stage);
                PanelKit.Label(_detail, $"{UpgradeTell.Glyph(eval.Verdict)} {eval.Verdict}  {UpgradeTell.Pct(eval.DeltaPercent)} power",
                    Theme.FsBody, UpgradeTell.Color(eval.Verdict), TextAnchor.MiddleLeft);

                DerivedDeltaRow(_detail, "DPS", DerivedStats.Dps(after) - DerivedStats.Dps(before));
                DerivedDeltaRow(_detail, "Eff. Life",
                    DerivedStats.EffectiveHp(after, _cfg, stage) - DerivedStats.EffectiveHp(before, _cfg, stage));

                // Raw stat deltas
                bool anyDelta = false;
                foreach (var k in StatDisplay.Order)
                {
                    double d = after.Get(k) - before.Get(k);
                    if (d == 0) continue;
                    anyDelta = true;
                    PanelKit.Label(_detail, $"{(d > 0 ? "▲" : "▼")} {StatDisplay.Label(k)}  {StatDisplay.Delta(k, d)}",
                        Theme.FsSmall, d > 0 ? Theme.Good : Theme.Bad, TextAnchor.MiddleLeft);
                }
                if (!anyDelta)
                    PanelKit.Label(_detail, "No stat change", Theme.FsSmall, Theme.TextBright, TextAnchor.MiddleLeft);

                // Cross-hero surfacing (10.5b): if a DIFFERENT fielded hero would get a genuine
                // upgrade from this item, point at them — so a bag item that's wasted on the selected
                // hero but great for a partymate doesn't stay invisible until you switch heroes.
                var party = new List<string>();
                foreach (var pid in save.Party) if (pid != null) party.Add(pid);
                var bestOther = Upgrades.BestForItem(save, item, _cfg, stage, party.Count > 0 ? party : null);
                if (bestOther != null && bestOther.HeroId != _heroId && bestOther.Verdict == Upgrades.Verdict.Upgrade)
                    PanelKit.Label(_detail, $"{UpgradeTell.Glyph(bestOther.Verdict)} {UpgradeTell.Pct(bestOther.DeltaPercent)} for {HeroName(save, bestOther.HeroId)} instead",
                        Theme.FsSmall, UpgradeTell.Color(bestOther.Verdict), TextAnchor.MiddleLeft);

                PanelKit.Flex(_detail); // push the Equip button to the bottom of the pane
                var row = PanelKit.Row(_detail, Theme.BtnH);
                PanelKit.ButtonCell(row, "Equip", () => EquipFromBag(save, item));
            }
        }

        private void EquipFromBag(SaveState save, Item item)
        {
            _view.ReplaceSave(Inventory.EquipItem(save, _heroId!, item.Id, _cfg));
            Rebuild();
        }

        /// <summary>A headline derived-stat delta row (DPS / Effective Life) in the compare pane.
        /// Rounds to a whole number; an effectively-zero change reads as a dim "±0".</summary>
        private static void DerivedDeltaRow(RectTransform into, string label, double delta)
        {
            long r = (long)Math.Round(delta);
            string arrow = r > 0 ? "▲" : r < 0 ? "▼" : "·";
            string val = (r > 0 ? "+" : r < 0 ? "-" : "±") + Math.Abs(r).ToString("N0");
            var color = r > 0 ? Theme.GoodBright : r < 0 ? Theme.BadBright : Theme.TextDim;
            PanelKit.Label(into, $"{arrow} {label}  {val}", Theme.FsLabel, color, TextAnchor.MiddleLeft);
        }

        // ---- Stats sub-tab (and the Equipment tab's default pane) ----

        private void ShowHeroStats(SaveState save)
        {
            if (_detail == null) return;
            ClearPane(_detail);
            RenderStatSheet(_detail, save);
        }

        private void BuildStatsPane(RectTransform right, SaveState save)
        {
            var section = PanelKit.Section(right, null);
            section.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            RenderStatSheet(section, save);
        }

        private void RenderStatSheet(RectTransform into, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId);
            if (hero == null) return;
            var stats = Stats.ComputeHeroStats(hero, _cfg, Stats.ResolveEquipped(save, hero));

            // Min-pinned so a height-starved pane crushes the stat rows' slack, not the headline.
            PanelKit.Fixed(PanelKit.Label(into, HeroName(save, _heroId), Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft).gameObject, height: 24f);
            PanelKit.Fixed(PanelKit.Label(into, $"Level {hero.Level}", Theme.FsTiny, Theme.TextBright, TextAnchor.MiddleLeft).gameObject, height: 18f);

            // Headline derived stats (A2): DPS and Effective Life — the at-a-glance "how strong /
            // how tanky" numbers. EHP is read against the current stage's boss hit (the gate).
            int stage = save.Progress.CurrentStage;
            AccentRow(into, "DPS", DerivedStats.Dps(stats).ToString("N0"));
            AccentRow(into, "Effective Life", DerivedStats.EffectiveHp(stats, _cfg, stage).ToString("N0"));
            PanelKit.Label(into, $"vs stage {stage} boss hit", Theme.FsMicro, Theme.TextDim, TextAnchor.MiddleRight);

            foreach (var k in StatDisplay.Order)
                PanelKit.KeyValueRow(into, StatDisplay.Label(k), StatDisplay.Value(k, stats.Get(k)));
        }

        /// <summary>A highlighted derived-stat row (DPS / Effective Life) in the stat sheet.</summary>
        private static void AccentRow(RectTransform into, string label, string value)
        {
            var row = PanelKit.Row(into, 22f);
            PanelKit.TextCell(row, label, Theme.FsLabel, Theme.AccentGoldWarm, TextAnchor.MiddleLeft, flex: 1f);
            PanelKit.TextCell(row, value, Theme.FsLabel, Theme.AccentGoldWarm, TextAnchor.MiddleRight, flex: 1f);
        }

        // ---- Skills sub-tab: the 2+2 kit (§7.2) — no loadout, invest points + mastery ----

        private void BuildSkillsPane(RectTransform right, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId);
            if (hero == null) return;

            var actives = Skills.KnownActive(hero, _cfg);
            var passives = Skills.KnownPassive(hero, _cfg);
            int points = Skills.UnspentPoints(hero, _cfg);

            // Header row: title + the "+1 pt / N levels" note.
            var titleRow = PanelKit.Row(right, 26f);
            PanelKit.TextCell(titleRow, $"{HeroName(save, _heroId)} — Skills", Theme.FsH2, Theme.TextBright, TextAnchor.MiddleLeft, flex: 1f);
            PanelKit.TextCell(titleRow, $"+1 pt / {_cfg.Balance.SkillPointsEveryLevels} levels", Theme.FsSmall, Theme.TextDim2, TextAnchor.MiddleRight, flex: 1f);

            // Skill points + respec row: spend a point to rank a skill up (rank 5 = mastery).
            var pointsRow = PanelKit.Row(right, Theme.TouchMin); // Respec button: pin the 44 touch floor (10.13c)
            PanelKit.TextCell(pointsRow, $"Skill Points: {points}", Theme.FsBody,
                points > 0 ? Theme.AccentGold : Theme.TextDim2, TextAnchor.MiddleLeft, flex: 1f);
            PanelKit.ButtonCell(pointsRow, "Respec",
                () => { _view.ReplaceSave(Skills.RespecHero(save, _heroId!, _cfg)); Rebuild(); },
                width: 110f, fontSize: Theme.FsLabel, enabled: Skills.PointsSpent(hero) > 0);

            // Both kits share one scroll so a full 2+2 with long effect text can't overflow.
            var list = UiKit.ScrollColumnFill(right, spacing: Theme.GapXs);

            PanelKit.Label(list, "ACTIVE — auto-cast once unlocked", Theme.FsTiny, Theme.ActiveSkill, TextAnchor.MiddleLeft);
            foreach (var id in actives)
            {
                if (!_cfg.Skills.TryGetValue(id, out var sk)) continue;
                int rank = Skills.RankOf(hero, id);
                string cd = $"{sk.CooldownMs / 1000.0:0.#}s cd";
                string meta = $"{sk.Effect} · {cd}";

                // Reveal gate: a locked kit skill neither casts nor accepts points — show when it opens.
                bool unlocked = Skills.IsUnlocked(hero, id, _cfg);
                string sub = unlocked ? meta : $"unlocks at Lv {sk.UnlockLevel}";
                var subColor = unlocked ? Theme.TextMuted : Theme.Warn;
                var nameColor = unlocked ? Theme.TextBright : Theme.TextDisabled;
                // Tint the rows the hero actually casts.
                SkillRow(list, save, id, sk, rank, sk.Name, sub, nameColor, subColor,
                    tint: unlocked ? Theme.ActiveRowTint : (Color?)null);
            }

            PanelKit.Label(list, "PASSIVE — always on, rank for stats", Theme.FsTiny, Theme.PassiveSkill, TextAnchor.MiddleLeft);
            foreach (var id in passives)
            {
                if (!_cfg.Skills.TryGetValue(id, out var sk)) continue;
                int rank = Skills.RankOf(hero, id);
                bool unlocked = Skills.IsUnlocked(hero, id, _cfg);
                double total = sk.StatPerRank * Skills.EffectiveRank(rank, sk, _cfg); // mastery-aware
                string effect = !unlocked ? $"unlocks at Lv {sk.UnlockLevel}"
                    : rank > 0 ? $"+{sk.StatPerRank:0.##} {sk.PassiveStat}/rank  (now +{total:0.##})"
                    : $"+{sk.StatPerRank:0.##} {sk.PassiveStat}/rank";
                var nameColor = !unlocked ? Theme.TextDisabled
                    : rank > 0 ? Theme.PassiveBright : Theme.TextBody;
                var subColor = !unlocked ? Theme.Warn : Theme.PassiveDim;
                SkillRow(list, save, id, sk, rank, sk.Name, effect, nameColor, subColor, tint: null);
            }
        }

        /// <summary>One skill entry: [name + sub-line stack | rank badge | ＋ invest]. An optional
        /// <paramref name="tint"/> shades the row background (unlocked actives the hero casts).</summary>
        private void SkillRow(RectTransform list, SaveState save, string id, SkillDef sk, int rank,
                              string name, string sub, Color nameColor, Color subColor, Color? tint)
        {
            var row = PanelKit.Row(list, 44f);
            if (tint != null) row.gameObject.AddComponent<Image>().color = tint.Value;

            var stack = PanelKit.VStack(row, 0f);
            stack.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            PanelKit.Label(stack, name, Theme.FsBody, nameColor, TextAnchor.LowerLeft);
            PanelKit.Label(stack, sub, Theme.FsTiny, subColor, TextAnchor.UpperLeft);

            // Rank readout: grey at 0, gold once invested, red bold "MASTERY" at MaxRank (§7.2 — the
            // capped skill counts extra ranks via Skills.EffectiveRank).
            bool mastered = rank >= sk.MaxRank;
            var badge = PanelKit.TextCell(row, mastered ? "MASTERY" : $"Rk {rank}/{sk.MaxRank}", Theme.FsSmall,
                mastered ? Theme.Mastery : rank > 0 ? Theme.AccentGoldWarm : Theme.TextDim,
                TextAnchor.MiddleCenter, width: 90f);
            if (mastered) badge.fontStyle = FontStyle.Bold;

            string capturedId = id;
            PanelKit.ButtonCell(row, "＋",
                () => { _view.ReplaceSave(Skills.InvestSkill(save, _heroId!, capturedId, _cfg)); Rebuild(); },
                width: 44f, fontSize: Theme.FsH2, enabled: Skills.CanInvest(save, _heroId!, id, _cfg));
        }

        // ---- helpers ----

        private static void ClearPane(RectTransform pane)
        {
            for (int i = pane.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(pane.GetChild(i).gameObject);
        }

        private EquipSlot SlotOf(Item item) => _cfg.ItemBases[item.BaseId].Slot;

        private string HeroName(SaveState save, string? heroId)
        {
            if (heroId == null) return "";
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero != null && _cfg.Heroes.TryGetValue(hero.DefId, out var def) && !string.IsNullOrEmpty(def.Name))
                return def.Name;
            return heroId;
        }

        private static int PartyFieldedCount(SaveState save)
        {
            int n = 0;
            foreach (var id in save.Party) if (id != null) n++;
            return n;
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
