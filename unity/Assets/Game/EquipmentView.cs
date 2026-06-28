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
    /// </summary>
    public sealed class EquipmentView : MonoBehaviour
    {
        // Doll laid out like the body (MapleStory/PoE-ish): helm top-centre, chest centre,
        // boots at the feet, weapon/offhand at the hands, amulet/cape up top, gloves/ring low.
        private static readonly (EquipSlot slot, int col, int row)[] Cells =
        {
            (EquipSlot.Amulet, 0, 0), (EquipSlot.Helm,  1, 0), (EquipSlot.Cape,   2, 0),
            (EquipSlot.Weapon, 0, 1), (EquipSlot.Chest, 1, 1), (EquipSlot.Offhand, 2, 1),
            (EquipSlot.Gloves, 0, 2), (EquipSlot.Boots, 1, 2), (EquipSlot.Ring,   2, 2),
        };

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

            var canvas = UiKit.CreateCanvas("HeroesCanvas", transform, sortOrder: 96);
            _panel = canvas.gameObject;
            // Fully opaque backdrop: this is a full-screen management view, so it covers the
            // HUD chrome (TopBar, chat) behind it instead of letting them bleed through a dim.
            UiKit.FullScreen(canvas.transform, new Color(0.06f, 0.06f, 0.09f, 1f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(1160, 680), new Color(0.10f, 0.10f, 0.14f, 1f));
            // anchoredPosition is the rect CENTRE; offset by half-width so the left-aligned
            // title sits just inside the panel's left edge instead of hanging off it.
            UiKit.Label(panel.transform, "Heroes", 28, TextAnchor.MiddleLeft, new Vector2(230, 38), new Vector2(-440, 300));
            UiKit.TextButton(panel.transform, "Close", new Vector2(140, 48), new Vector2(495, 300), Close, 22);

            BuildSelector(panel.transform, save);
            BuildHeroHeader(panel.transform, save);
            BuildSubTabs(panel.transform);

            switch (_subTab)
            {
                case SubTab.Skills: BuildSkillsPane(panel.transform, save); break;
                case SubTab.Stats:  BuildStatsPane(panel.transform, save);  break;
                default:            BuildEquipment(panel.transform, save);  break;
            }
        }

        // ---- left rail: party slots + all owned heroes ----

        private void BuildSelector(Transform parent, SaveState save)
        {
            const float xL = -462f;
            int fielded = PartyFieldedCount(save);
            string? leaderId = EffectiveLeaderId(save); // ★ marks who leads the formation

            UiKit.Label(parent, $"Party  ·  {fielded}/{save.Party.Length}", 14, TextAnchor.MiddleLeft,
                        new Vector2(196, 20), new Vector2(xL, 272)).color = new Color(0.7f, 0.74f, 0.8f);

            float y = 242f;
            for (int i = 0; i < save.Party.Length; i++)
            {
                string? id = save.Party[i];
                bool filled = id != null;
                string label = filled
                    ? $"{i + 1}.  {(id == leaderId ? "★ " : "")}{HeroName(save, id)}"
                    : $"{i + 1}.  — empty —";
                var b = UiKit.TextButton(parent, label, new Vector2(196, 32), new Vector2(xL, y),
                    () => { if (filled) SelectHero(id!); }, 14);
                var img = b.GetComponent<Image>();
                if (!filled) img.color = new Color(0.12f, 0.13f, 0.16f);
                else if (id == _heroId) img.color = new Color(0.30f, 0.45f, 0.65f);
                y -= 36f;
            }

            UiKit.Label(parent, "All heroes", 14, TextAnchor.MiddleLeft,
                        new Vector2(196, 20), new Vector2(xL, 98)).color = new Color(0.7f, 0.74f, 0.8f);

            // Fixed stack for now (rosters are small); becomes a scroll list as it grows.
            y = 68f;
            foreach (var hero in save.Heroes)
            {
                var id = hero.Id;
                bool isFielded = Array.IndexOf(save.Party, id) >= 0;
                var b = UiKit.TextButton(parent, isFielded ? HeroName(save, id) : HeroName(save, id) + "  (B)",
                    new Vector2(196, 30), new Vector2(xL, y), () => SelectHero(id), 15);
                var img = b.GetComponent<Image>();
                if (id == _heroId) img.color = new Color(0.30f, 0.45f, 0.65f);
                else if (!isFielded) img.color = new Color(0.18f, 0.18f, 0.23f);
                y -= 34f;
            }
        }

        private void SelectHero(string heroId) { _heroId = heroId; Rebuild(); }

        // ---- right pane: hero header + field/bench ----

        private void BuildHeroHeader(Transform parent, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId);
            if (hero == null) return;

            int slot = Array.IndexOf(save.Party, _heroId);
            bool fielded = slot >= 0;
            int fieldedCount = PartyFieldedCount(save);
            bool canEdit = _view.CanEditParty;

            // Left-aligned in the RIGHT pane: centre = leftEdge(-330) + width/2 so the text
            // starts at -330 and never reaches back over the left rail (ends ~-364).
            UiKit.Label(parent, HeroName(save, _heroId), 22, TextAnchor.MiddleLeft, new Vector2(300, 26), new Vector2(-180, 282))
                .color = new Color(0.85f, 0.9f, 1f);
            UiKit.Label(parent, fielded ? $"Level {hero.Level} · fielded (slot {slot + 1})" : $"Level {hero.Level} · benched",
                        13, TextAnchor.MiddleLeft, new Vector2(340, 18), new Vector2(-160, 258))
                .color = new Color(0.7f, 0.74f, 0.8f);

            // Stacked UNDER Close (top-right) with a gap, not overlapping it.
            if (fielded)
            {
                ActionButton(parent, "Bench", new Vector2(140, 44), new Vector2(495, 248), canEdit && fieldedCount > 1,
                    () => { _view.ApplyPartyEdit(Party.SetPartySlot(save, slot, null)); Rebuild(); });

                // Leader toggle: the chosen hero leads the formation; the rest fall in behind.
                // Safe to change any time (it only re-points who's followed), so not farm-gated.
                bool isLeader = _heroId == EffectiveLeaderId(save);
                ActionButton(parent, isLeader ? "★ Leader" : "Make Leader", new Vector2(140, 40), new Vector2(495, 198), !isLeader,
                    () => { _view.SetLeader(_heroId); Rebuild(); });
            }
            else
            {
                int firstEmpty = Array.IndexOf(save.Party, (string?)null);
                ActionButton(parent, "Field", new Vector2(140, 44), new Vector2(495, 248), canEdit && firstEmpty >= 0,
                    () => { _view.ApplyPartyEdit(Party.FieldHero(save, firstEmpty, _heroId!)); Rebuild(); });
            }
        }

        private void BuildSubTabs(Transform parent)
        {
            var defs = new (SubTab tab, string label, float x)[]
            {
                (SubTab.Equipment, "Equipment", -250f),
                (SubTab.Skills, "Skills", -110f),
                (SubTab.Stats, "Stats", 30f),
            };
            foreach (var d in defs)
            {
                var tab = d.tab;
                var b = UiKit.TextButton(parent, d.label, new Vector2(130, 42), new Vector2(d.x, 214f),
                    () => { _subTab = tab; Rebuild(); }, 17);
                if (_subTab == tab) b.GetComponent<Image>().color = new Color(0.30f, 0.45f, 0.65f);
            }
        }

        // ---- Equipment sub-tab: doll + shared bag + compare pane ----

        private void BuildEquipment(Transform parent, SaveState save)
        {
            BuildDoll(parent, save);
            BuildBag(parent, save);
            var box = UiKit.Panel(parent, new Vector2(270, 440), new Color(0.07f, 0.07f, 0.10f, 1f), new Vector2(445, -30));
            _detail = box.rectTransform;
            ShowHeroStats(save); // default pane = the hero's stat sheet
        }

        private void BuildDoll(Transform parent, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId)!;
            const float cell = 84f, sp = 92f, ox = -210f, oy = 8f;

            UiKit.Label(parent, "Equipped", 14, TextAnchor.MiddleCenter, new Vector2(280, 18), new Vector2(-210, 168))
                .color = new Color(0.7f, 0.74f, 0.8f);

            foreach (var (slot, col, row) in Cells)
            {
                float cx = ox + (col - 1) * sp;
                float cy = oy - (row - 1) * sp;

                hero.Equipped.TryGetValue(slot, out var itemId);
                var item = itemId != null ? save.Inventory.Find(i => i.Id == itemId) : null;
                Rarity? rarity = item?.Rarity;

                var tile = UiKit.ItemTile(parent, new Vector2(cell, cell), new Vector2(cx, cy), rarity, UiKit.SlotAbbrev(slot), raycast: true);
                var captured = item;
                tile.AddComponent<Button>().onClick.AddListener(() => ShowDetail(save, captured, slotIfEmpty: slot));
                UiKit.Hover(tile, () => ShowDetail(save, captured, slotIfEmpty: slot), () => ShowHeroStats(save));
            }
        }

        private void BuildBag(Transform parent, SaveState save)
        {
            int loose = Inventory.LooseCount(save);
            UiKit.Label(parent, $"Bag (shared)  ·  {loose}/{_cfg.Balance.InventoryCap}", 14, TextAnchor.MiddleLeft,
                        new Vector2(300, 20), new Vector2(140, 170)).color = new Color(0.7f, 0.74f, 0.8f);
            var grid = UiKit.ScrollGrid(parent, new Vector2(300, 300), new Vector2(140, -26), new Vector2(70, 70));

            bool any = false;
            var equipped = EquippedIds(save);
            foreach (var item in save.Inventory)
            {
                if (equipped.Contains(item.Id)) continue; // only free items can be equipped
                any = true;
                var it = item;
                var tile = UiKit.ItemTile(grid, new Vector2(70, 70), Vector2.zero, it.Rarity, UiKit.SlotAbbrev(SlotOf(it)), raycast: true);
                tile.AddComponent<Button>().onClick.AddListener(() => EquipFromBag(save, it)); // one click to equip
                UiKit.Hover(tile, () => ShowDetail(save, it), () => ShowHeroStats(save));        // hover to compare
            }
            if (!any)
                UiKit.Label(parent, "No free items in the bag.", 14, TextAnchor.MiddleCenter,
                            new Vector2(280, 40), new Vector2(140, 40));
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

            float y = 196f;
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
                UiKit.TextButton(_detail, "Unequip", new Vector2(200, 50), new Vector2(0, -196),
                    () => { _view.ReplaceSave(Inventory.UnequipItem(save, _heroId!, SlotOf(item))); Rebuild(); });
            }
            else
            {
                y -= 8f;
                UiKit.Label(_detail, $"vs {HeroName(save, _heroId)}'s {SlotOf(item)}:", 13, TextAnchor.MiddleLeft,
                            new Vector2(250, 18), new Vector2(0, y));
                y -= 22f;

                var (before, after) = Inventory.ComparePairForHero(save, _heroId!, item, _cfg);

                // One-line power verdict (Lever 2), then the derived deltas (B2) behind it.
                int stage = save.Progress.CurrentStage;
                var eval = Upgrades.EvaluateForHero(save, _heroId!, item, _cfg, stage);
                UiKit.Label(_detail, $"{UpgradeTell.Glyph(eval.Verdict)} {eval.Verdict}  {UpgradeTell.Pct(eval.DeltaPercent)} power",
                            15, TextAnchor.MiddleLeft, new Vector2(250, 20), new Vector2(0, y)).color = UpgradeTell.Color(eval.Verdict);
                y -= 24f;
                y = DerivedDeltaRow(_detail, "DPS", DerivedStats.Dps(after) - DerivedStats.Dps(before), y);
                y = DerivedDeltaRow(_detail, "Eff. Life",
                        DerivedStats.EffectiveHp(after, _cfg, stage) - DerivedStats.EffectiveHp(before, _cfg, stage), y);
                y -= 4f;

                // Raw stat deltas
                bool anyDelta = false;
                foreach (var k in StatDisplay.Order)
                {
                    double d = after.Get(k) - before.Get(k);
                    if (d == 0) continue;
                    anyDelta = true;
                    var l = UiKit.Label(_detail, $"{(d > 0 ? "▲" : "▼")} {StatDisplay.Label(k)}  {StatDisplay.Delta(k, d)}",
                                        13, TextAnchor.MiddleLeft, new Vector2(250, 18), new Vector2(0, y));
                    l.color = d > 0 ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.95f, 0.45f, 0.45f);
                    y -= 20f;
                }
                if (!anyDelta)
                    UiKit.Label(_detail, "No stat change", 13, TextAnchor.MiddleLeft, new Vector2(250, 18), new Vector2(0, y));

                UiKit.TextButton(_detail, "Equip", new Vector2(200, 50), new Vector2(0, -196),
                    () => EquipFromBag(save, item));
            }
        }

        private void EquipFromBag(SaveState save, Item item)
        {
            _view.ReplaceSave(Inventory.EquipItem(save, _heroId!, item.Id, _cfg));
            Rebuild();
        }

        // ---- Stats sub-tab ----

        private void ShowHeroStats(SaveState save)
        {
            if (_detail == null) return;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            RenderStatSheet(_detail, save);
        }

        private void BuildStatsPane(Transform parent, SaveState save)
        {
            var box = UiKit.Panel(parent, new Vector2(380, 430), new Color(0.07f, 0.07f, 0.10f, 1f), new Vector2(60, -26));
            RenderStatSheet(box.rectTransform, save);
        }

        private void RenderStatSheet(RectTransform into, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId);
            if (hero == null) return;
            var stats = Stats.ComputeHeroStats(hero, _cfg, Stats.ResolveEquipped(save, hero));

            UiKit.Label(into, HeroName(save, _heroId), 18, TextAnchor.MiddleLeft, new Vector2(250, 24), new Vector2(0, 188))
                .color = new Color(0.85f, 0.9f, 1f);
            UiKit.Label(into, $"Level {hero.Level}", 12, TextAnchor.MiddleLeft, new Vector2(250, 18), new Vector2(0, 166));

            // Headline derived stats (A2): DPS and Effective Life — the at-a-glance "how strong /
            // how tanky" numbers. EHP is read against the current stage's boss hit (the gate).
            int stage = save.Progress.CurrentStage;
            var accent = new Color(1f, 0.86f, 0.45f);
            DerivedRow(into, "DPS", DerivedStats.Dps(stats).ToString("N0"), 138f, accent);
            DerivedRow(into, "Effective Life", DerivedStats.EffectiveHp(stats, _cfg, stage).ToString("N0"), 116f, accent);
            UiKit.Label(into, $"vs stage {stage} boss hit", 10, TextAnchor.MiddleRight, new Vector2(205, 14), new Vector2(22, 101))
                .color = new Color(0.6f, 0.62f, 0.68f);

            float y = 78f;
            foreach (var k in StatDisplay.Order)
            {
                UiKit.Label(into, StatDisplay.Label(k), 13, TextAnchor.MiddleLeft, new Vector2(160, 18), new Vector2(-45, y));
                UiKit.Label(into, StatDisplay.Value(k, stats.Get(k)), 13, TextAnchor.MiddleRight, new Vector2(90, 18), new Vector2(95, y));
                y -= 22f;
            }
        }

        /// <summary>A highlighted derived-stat row (DPS / Effective Life) in the stat sheet.</summary>
        private static void DerivedRow(RectTransform into, string label, string value, float y, Color color)
        {
            UiKit.Label(into, label, 14, TextAnchor.MiddleLeft, new Vector2(160, 18), new Vector2(-45, y)).color = color;
            UiKit.Label(into, value, 14, TextAnchor.MiddleRight, new Vector2(130, 18), new Vector2(75, y)).color = color;
        }

        /// <summary>A headline derived-stat delta row (DPS / Effective Life) in the compare pane.
        /// Rounds to a whole number; an effectively-zero change reads as a dim "±0". Returns next y.</summary>
        private static float DerivedDeltaRow(RectTransform into, string label, double delta, float y)
        {
            long r = (long)System.Math.Round(delta);
            string arrow = r > 0 ? "▲" : r < 0 ? "▼" : "·";
            string val = (r > 0 ? "+" : r < 0 ? "-" : "±") + System.Math.Abs(r).ToString("N0");
            var l = UiKit.Label(into, $"{arrow} {label}  {val}", 14, TextAnchor.MiddleLeft, new Vector2(250, 18), new Vector2(0, y));
            l.color = r > 0 ? new Color(0.55f, 1f, 0.6f) : r < 0 ? new Color(1f, 0.5f, 0.5f) : new Color(0.6f, 0.62f, 0.68f);
            return y - 22f;
        }

        // ---- Skills sub-tab: pick the active loadout from the hero's known pool (≤ cap) ----

        private void BuildSkillsPane(Transform parent, SaveState save)
        {
            var hero = save.Heroes.Find(h => h.Id == _heroId);
            if (hero == null) return;

            // Grown taller than the other panes (8 rows: actives + passives) — top still tucks under
            // the subtabs (y=214); extends down toward the panel floor.
            var box = UiKit.Panel(parent, new Vector2(560, 500), new Color(0.07f, 0.07f, 0.10f, 1f), new Vector2(120, -61));

            var actives = Skills.KnownActive(hero, _cfg);
            var passives = Skills.KnownPassive(hero, _cfg);
            int active = hero.SkillLoadout.Count;
            int cap = _cfg.Balance.MaxActiveSkills;
            int points = Skills.UnspentPoints(hero, _cfg);

            UiKit.Label(box.transform, $"{HeroName(save, _heroId)} — Skills", 18, TextAnchor.MiddleLeft,
                        new Vector2(300, 26), new Vector2(-110, 224)).color = new Color(0.85f, 0.9f, 1f);
            UiKit.Label(box.transform, $"Active {active}/{cap}", 14, TextAnchor.MiddleRight,
                        new Vector2(150, 22), new Vector2(190, 224)).color =
                active >= cap ? new Color(1f, 0.82f, 0.4f) : new Color(0.7f, 0.74f, 0.8f);

            // Skill points + respec row (Lever 3): spend a point to rank a skill up.
            UiKit.Label(box.transform, $"Skill Points: {points}", 15, TextAnchor.MiddleLeft,
                        new Vector2(280, 22), new Vector2(-110, 198)).color =
                points > 0 ? new Color(1f, 0.85f, 0.4f) : new Color(0.62f, 0.66f, 0.72f);
            ActionButton(box.transform, "Respec", new Vector2(110, 30), new Vector2(210, 198),
                Skills.PointsSpent(hero) > 0,
                () => { _view.ReplaceSave(Skills.RespecHero(save, _heroId!, _cfg)); Rebuild(); }, 14);

            // ---- Active skills (the auto-cast bar) ----
            UiKit.Label(box.transform, "ACTIVE — pick " + cap + " to auto-cast", 12, TextAnchor.MiddleLeft,
                        new Vector2(400, 18), new Vector2(-110, 170)).color = new Color(0.55f, 0.7f, 0.95f);

            float y = 144f;
            foreach (var id in actives)
            {
                if (!_cfg.Skills.TryGetValue(id, out var sk)) continue;
                bool on = hero.SkillLoadout.Contains(id);
                int rank = Skills.RankOf(hero, id);
                string cd = $"{sk.CooldownMs / 1000.0:0.#}s cd";
                string meta = sk.ManaCost > 0 ? $"{sk.Effect} · {sk.ManaCost} mana · {cd}" : $"{sk.Effect} · {cd}";

                // Tree gate (Lever 3 slice 3): a locked node can't be ranked yet — show why
                // (prereq and/or level) in place of the meta line, and dim the name.
                bool unlocked = Skills.IsUnlocked(hero, id, _cfg);
                string sub = meta;
                var subColor = new Color(0.66f, 0.70f, 0.78f);
                if (!unlocked)
                {
                    var reqs = new System.Collections.Generic.List<string>();
                    if (!string.IsNullOrEmpty(sk.Prereq) && _cfg.Skills.TryGetValue(sk.Prereq, out var pre)
                        && Skills.RankOf(hero, sk.Prereq) < 1) reqs.Add(pre.Name);
                    if (hero.Level < sk.UnlockLevel) reqs.Add("Lv " + sk.UnlockLevel);
                    sub = "needs " + string.Join(" + ", reqs);
                    subColor = new Color(0.95f, 0.62f, 0.55f);
                }

                if (on) // tint the row for slotted skills (drawn first so labels sit on top)
                    UiKit.Panel(box.transform, new Vector2(540, 42), new Color(0.18f, 0.28f, 0.42f, 0.55f), new Vector2(0, y - 8f));

                UiKit.Label(box.transform, sk.Name, 16, TextAnchor.MiddleLeft, new Vector2(250, 22), new Vector2(-115, y))
                    .color = !unlocked ? new Color(0.55f, 0.57f, 0.62f)
                           : on ? new Color(0.85f, 0.92f, 1f) : new Color(0.78f, 0.80f, 0.85f);
                UiKit.Label(box.transform, sub, 12, TextAnchor.MiddleLeft, new Vector2(250, 16), new Vector2(-115, y - 16f))
                    .color = subColor;

                // Rank readout — gold once invested.
                UiKit.Label(box.transform, $"Rk {rank}/{sk.MaxRank}", 13, TextAnchor.MiddleCenter, new Vector2(70, 22), new Vector2(40, y - 8f))
                    .color = rank > 0 ? new Color(1f, 0.85f, 0.45f) : new Color(0.6f, 0.64f, 0.7f);

                string capturedId = id;
                // Active -> click to remove; inactive -> "Slot" (disabled when the bar is full).
                bool clickable = on || active < cap;
                var btn = ActionButton(box.transform, on ? "Active ✓" : (active < cap ? "Slot" : "Full"),
                    new Vector2(96, 34), new Vector2(140, y - 8f), clickable,
                    () => { _view.ReplaceSave(Skills.ToggleSkill(save, _heroId!, capturedId, _cfg)); Rebuild(); }, 14);
                if (on) btn.GetComponent<Image>().color = new Color(0.30f, 0.45f, 0.65f);

                // Invest a point -> +1 rank (disabled when no points or at max rank).
                ActionButton(box.transform, "＋", new Vector2(40, 34), new Vector2(232, y - 8f),
                    Skills.CanInvest(save, _heroId!, id, _cfg),
                    () => { _view.ReplaceSave(Skills.InvestSkill(save, _heroId!, capturedId, _cfg)); Rebuild(); }, 18);

                y -= 44f;
            }

            // ---- Passive nodes (always-on; rank them for stats, never slotted) ----
            y -= 6f;
            UiKit.Label(box.transform, "PASSIVE — always on, rank for stats", 12, TextAnchor.MiddleLeft,
                        new Vector2(400, 18), new Vector2(-110, y)).color = new Color(0.6f, 0.85f, 0.65f);
            y -= 26f;
            foreach (var id in passives)
            {
                if (!_cfg.Skills.TryGetValue(id, out var sk)) continue;
                int rank = Skills.RankOf(hero, id);
                double total = sk.StatPerRank * rank;
                string effect = rank > 0
                    ? $"+{sk.StatPerRank:0.##} {sk.PassiveStat}/rank  (now +{total:0.##})"
                    : $"+{sk.StatPerRank:0.##} {sk.PassiveStat}/rank";

                UiKit.Label(box.transform, sk.Name, 16, TextAnchor.MiddleLeft, new Vector2(250, 22), new Vector2(-115, y))
                    .color = rank > 0 ? new Color(0.82f, 1f, 0.86f) : new Color(0.78f, 0.80f, 0.85f);
                UiKit.Label(box.transform, effect, 12, TextAnchor.MiddleLeft, new Vector2(280, 16), new Vector2(-115, y - 16f))
                    .color = new Color(0.66f, 0.78f, 0.70f);

                UiKit.Label(box.transform, $"Rk {rank}/{sk.MaxRank}", 13, TextAnchor.MiddleCenter, new Vector2(70, 22), new Vector2(40, y - 8f))
                    .color = rank > 0 ? new Color(1f, 0.85f, 0.45f) : new Color(0.6f, 0.64f, 0.7f);

                string capturedId = id;
                ActionButton(box.transform, "＋", new Vector2(40, 34), new Vector2(232, y - 8f),
                    Skills.CanInvest(save, _heroId!, id, _cfg),
                    () => { _view.ReplaceSave(Skills.InvestSkill(save, _heroId!, capturedId, _cfg)); Rebuild(); }, 18);

                y -= 44f;
            }
        }

        // ---- helpers ----

        /// <summary>A TextButton that renders greyed + inert when <paramref name="enabled"/> is false.</summary>
        private static Button ActionButton(Transform parent, string label, Vector2 size, Vector2 pos,
                                           bool enabled, Action onClick, int fontSize = 18)
        {
            var b = UiKit.TextButton(parent, label, size, pos, enabled ? onClick : () => { }, fontSize);
            if (!enabled)
            {
                b.interactable = false;
                b.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.28f);
            }
            return b;
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

        /// <summary>Who actually leads the formation: the chosen leader if it's still fielded,
        /// otherwise the auto fallback (lowest-slot fielded hero) — mirrors the sim's rule.</summary>
        private static string? EffectiveLeaderId(SaveState save)
            => (save.LeaderHeroId != null && Array.IndexOf(save.Party, save.LeaderHeroId) >= 0)
               ? save.LeaderHeroId
               : FirstPartyHeroId(save);

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
