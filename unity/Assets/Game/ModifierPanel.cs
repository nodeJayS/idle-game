#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Monster-modifier management screen (Lever 1 — the risk/reward knob). Lists the modifier
    /// types you've banked from bosses; each row shows what it does to monsters + the thematic
    /// reward it grants, with an ON/OFF toggle. Toggling applies to farm trash on the next spawns
    /// (CombatView.SetModifierActive). A toggled overlay (build on open, destroy on close) like
    /// the Heroes/Bag screens — opened from the control bar. Read-only over the save except the
    /// toggle, which routes through the GameCore reducer via the view.
    /// </summary>
    public sealed class ModifierPanel : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;
        private Canvas? _canvas;

        // Two-click arm/confirm for the free "reset tuning" action (mirrors InventoryPanel's SalvageAll
        // idiom): a reset forfeits gambled tuning, so the first click ARMS ("sure?") and the second
        // executes. Only one row can be armed at a time; any Rebuild disarms.
        private string? _confirmResetId;
        private Coroutine? _resetTimer;

        // The live imprint-preview mock card (a hover tooltip on a rare row's imprint text). At most one
        // exists; pointer-exit / any Rebuild destroys it so it can't leak across redraws.
        private GameObject? _imprintPreview;

        public bool IsOpen => _canvas != null;

        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }

        public void Toggle() { if (IsOpen) Close(); else Build(); }

        /// <summary>The player-facing close (toggle or the header X): the window eases out and then
        /// destroys itself. RebuildKeepConfirm below deliberately does NOT route through here — a
        /// redraw needs the instant teardown.</summary>
        public void Close()
        {
            if (_resetTimer != null) { StopCoroutine(_resetTimer); _resetTimer = null; }
            DestroyImprintPreview();
            if (_canvas != null) UiMotion.Dismiss(_canvas.gameObject, animate: true);
            _canvas = null;
        }

        /// <summary>Redraw on the current save. Disarms any pending reset (a rebuild cancels the arm,
        /// matching the bag's "any mutation cancels the confirm" behavior).</summary>
        private void Rebuild() { _confirmResetId = null; RebuildKeepConfirm(); }

        /// <summary>Redraw WITHOUT disarming the reset confirm (used by the arm click itself).</summary>
        private void RebuildKeepConfirm()
        {
            if (_resetTimer != null) { StopCoroutine(_resetTimer); _resetTimer = null; }
            DestroyImprintPreview();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            Build();
        }

        // ---- build ----

        // RowH gained a few px over the old 60: the sub line now carries gold ▲-delta chips, and rare
        // rows render the full imprint payload inline, so rows are a touch taller for breathing room.
        private const float RowH = 64f;
        private const float SectionHdrH = 22f; // the pool-header label row inside the scroll list
        private const float PanelW = 620f; // widened from 580 to fit the right-side Tune/Reset + imprint payload

        private void Build()
        {
            var save = _view.CurrentSave;
            var mods = save.Modifiers;

            // What actually APPLIES right now (rare slots need ≥2 active) — drives the net line + "inert" tags.
            var applied = new HashSet<string>();
            foreach (var mi in Modifiers.ResolveActive(save, _cfg)) applied.Add(mi.Def.Id);

            // Group owned mods into the three pools (normal / rare-prefix / rare-suffix), each in stable order.
            var normals = new List<(ModifierDef def, int strength)>();
            var prefixes = new List<(ModifierDef def, int strength)>();
            var suffixes = new List<(ModifierDef def, int strength)>();
            foreach (var (def, strength) in OwnedDefs(mods))
            {
                if (!def.Mechanical) normals.Add((def, strength));
                else if (def.ImprintSlot == ImprintSlot.Prefix) prefixes.Add((def, strength));
                else suffixes.Add((def, strength));
            }
            int rowCount = normals.Count + prefixes.Count + suffixes.Count;

            // PanelKit.Window: standard header (title + Close) + a layout body. Clamp to the original
            // 620 width so the panel keeps its shape (Window's default 1160 is far too wide here). The
            // mod list always scrolls, so an owner with many mods overflows the 680-max cleanly.
            var winGo = PanelKit.Window(transform, "Monster Modifiers", Close, out var body,
                                        "ModifierCanvas", sortOrder: 90, max: new Vector2(PanelW, 680f));
            _canvas = winGo.GetComponent<Canvas>();
            PanelKit.Stack(body);

            // Header explainer: the applied-net summary (rich text) + the plain-language tuning help.
            PanelKit.Label(body, NetSummary(applied), 14, new Color(0.80f, 0.84f, 0.90f), TextAnchor.UpperLeft);
            PanelKit.Label(body,
                "Tune gambles gold + scrap to raise a mod's danger and reward together · rare mods can't be tuned.",
                12, new Color(0.58f, 0.62f, 0.70f), TextAnchor.UpperLeft);

            if (rowCount == 0)
            {
                int firstAt = _cfg.Balance.ModifierNewEveryStages;
                PanelKit.Flex(body);
                PanelKit.Label(body, $"Push deeper to unlock modifiers — your first unlocks at stage {firstAt}.",
                    15, new Color(0.70f, 0.74f, 0.80f), TextAnchor.MiddleCenter);
                PanelKit.Flex(body);
                return;
            }

            // The mod list scrolls (fills the remaining body height); section headers + rows flow inside.
            var list = UiKit.ScrollColumnFill(body, spacing: Theme.GapXs);
            Section(list, "Normal", normals, applied, _cfg.Balance.MaxActiveModifiers);
            Section(list, "Rare — Prefix", prefixes, applied, _cfg.Balance.MaxActiveRarePerSlot);
            Section(list, "Rare — Suffix", suffixes, applied, _cfg.Balance.MaxActiveRarePerSlot);
        }

        /// <summary>Draw a pool's header (name + n/cap, plus a "needs N to apply" hint for a half-filled
        /// rare slot) then its rows into the scroll list. No-op for an empty pool.</summary>
        private void Section(RectTransform list, string title, List<(ModifierDef def, int strength)> rows,
                             HashSet<string> applied, int cap)
        {
            if (rows.Count == 0) return;
            var mods = _view.CurrentSave.Modifiers;
            int activeN = 0;
            foreach (var (def, _) in rows) if (mods.Active.Contains(def.Id)) activeN++;

            string hint = "";
            bool rare = rows[0].def.Mechanical;
            if (rare && activeN > 0 && activeN < _cfg.Balance.MinActiveRarePerSlot)
                hint = $"   <color=#e0a070>· needs {_cfg.Balance.MinActiveRarePerSlot} to apply</color>";

            var hdr = PanelKit.Label(list, $"{title}   <color=#8a8f99>{activeN}/{cap}</color>{hint}", 13,
                                     new Color(0.62f, 0.66f, 0.74f), TextAnchor.LowerLeft);
            PanelKit.Fixed(hdr.gameObject, height: SectionHdrH);

            foreach (var (def, strength) in rows)
                BuildRow(list, def, strength, mods.Active.Contains(def.Id), applied.Contains(def.Id));
        }

        private void BuildRow(RectTransform list, ModifierDef def, int strength, bool active, bool applies)
        {
            var save = _view.CurrentSave;
            double tuning = Modifiers.TuningOf(save, def.Id);
            double eff = strength * tuning; // shop tuning scales BOTH danger and reward
            bool tuned = tuning > 1.0001;   // any tuning bought over base?

            var row = PanelKit.Row(list, RowH);

            // LEFT: name + effect sublines stacked in a non-expanding VStack cell (the 10.3 lesson —
            // two-line text in a force-expand Row lives in a VStack slot, not two ballooning children).
            // The cell takes the flexible width; the fixed control cells on the right keep their size.
            var col = PanelKit.VStack(row, Theme.GapXs);
            col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Title: name + banked strength. The "+x% tuned" jargon chip is gone — tuning reads as the
            // per-number gold ▲ deltas on the summary line below (you see exactly what it bought).
            PanelKit.Label(col, $"{def.Name}   ·   str {strength}", 16,
                new Color((float)def.TintR, (float)def.TintG, (float)def.TintB) * 1.15f, TextAnchor.UpperLeft);

            // Summary shows EFFECTIVE numbers (strength × tuning); when tuned, each number carries a
            // compact gold ▲ delta = effective − strength-only, so the tuning gain is legible per-stat.
            string inert = (active && !applies) ? "  ·  <color=#e0a070>inert (needs pair)</color>" : "";
            PanelKit.Label(col,
                $"{MonsterSummary(def, strength, eff)}  ·  <color=#9fe0a0>{RewardSummary(def, strength, eff)}</color>{inert}",
                12, new Color(0.78f, 0.82f, 0.88f), TextAnchor.UpperLeft);

            // MIDDLE: rare rows show the imprint payload (hoverable); normal rows show Tune (+ Reset). Added
            // before ON so the fixed cells read left→right: [reset] [Tune] [ON]  /  [imprint] [ON].
            if (def.Mechanical)
                BuildImprintPayload(row, def, strength);
            else
                BuildTuneControls(row, def, tuned);

            // ON/OFF (rightmost). Lock OFF rows only when THIS mod's own pool is full (per-pool cap).
            bool full = !active && Modifiers.PoolFull(save, _cfg, def);
            var btn = PanelKit.ButtonCell(row, active ? "ON" : (full ? "FULL" : "OFF"),
                () => { _view.SetModifierActive(def.Id, !active); Rebuild(); },
                width: 60f, fontSize: Theme.FsLabel, enabled: !full);
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active ? new Color(0.30f, 0.55f, 0.33f)
                                       : full ? new Color(0.22f, 0.24f, 0.28f) : new Color(0.30f, 0.32f, 0.38f);
        }

        /// <summary>Right-side controls for a NORMAL mod: an optional "↺" reset (only when tuned above
        /// base; arms/confirms before forfeiting the gamble) then the Tune gamble button. Added left→right.</summary>
        private void BuildTuneControls(RectTransform row, ModifierDef def, bool tuned)
        {
            var save = _view.CurrentSave;

            // Reset (free, but forfeits gambled tuning → two-click arm/confirm, mirroring the bag's
            // "Salvage all". First click arms ("sure?"), auto-disarms after a few seconds; second resets.
            if (tuned)
            {
                bool armed = _confirmResetId == def.Id;
                var reset = PanelKit.ButtonCell(row, armed ? "sure?" : "↺", () => OnResetClick(def.Id),
                    width: 58f, fontSize: Theme.FsLabel);
                var rImg = reset.GetComponent<Image>();
                if (rImg != null) rImg.color = armed ? new Color(0.62f, 0.22f, 0.22f) : new Color(0.30f, 0.28f, 0.34f);
            }

            // Tune (shop): gamble tuning with gold+scrap; cost rises as the mod climbs. CanUpgrade gates
            // affordability (ButtonCell disables + greys, and swallows the click); the label spells the cost.
            var (g, s) = Modifiers.UpgradeCost(save, _cfg, def.Id);
            bool canUp = Modifiers.CanUpgrade(save, _cfg, def.Id);
            var up = PanelKit.ButtonCell(row, $"Tune ▲  {Num.CompactCeil(g)}g+{Num.CompactCeil(s)}s",
                () => { _view.UpgradeModifier(def.Id); Rebuild(); }, width: 150f, fontSize: Theme.FsTiny, enabled: canUp);
            var upImg = up.GetComponent<Image>();
            if (upImg != null) upImg.color = canUp ? new Color(0.34f, 0.30f, 0.46f) : new Color(0.22f, 0.22f, 0.26f);
        }

        /// <summary>Reset arm/confirm (free action, forfeits gambled tuning). First click arms this row and
        /// starts the auto-disarm; second click (within the window) resets its tuning to base.</summary>
        private void OnResetClick(string id)
        {
            if (_confirmResetId != id)
            {
                // Arm this row and redraw (RebuildKeepConfirm keeps the arm but resets the timer), THEN
                // start the auto-disarm — starting it after the rebuild, since RebuildKeepConfirm stops
                // any running timer as part of its teardown.
                _confirmResetId = id;
                RebuildKeepConfirm();
                if (gameObject.activeInHierarchy) _resetTimer = StartCoroutine(DisarmResetAfter());
                return;
            }
            _confirmResetId = null;
            _view.ResetModifierTuning(id);
            Rebuild();
        }

        private const float ResetConfirmSeconds = 3f;

        private System.Collections.IEnumerator DisarmResetAfter()
        {
            yield return new WaitForSecondsRealtime(ResetConfirmSeconds);
            _resetTimer = null;
            if (_canvas != null && _confirmResetId != null) { _confirmResetId = null; RebuildKeepConfirm(); }
        }

        /// <summary>Rare (mechanical) row middle cell: the actual imprint payload spelled out — chance,
        /// value, stat, and slot — in the pool's purple, hoverable to preview the resulting gear.</summary>
        private void BuildImprintPayload(RectTransform row, ModifierDef def, int strength)
        {
            var lbl = PanelKit.TextCell(row, ImprintText(def, strength), 12, new Color(0.85f, 0.61f, 1f),
                                        TextAnchor.MiddleRight, width: 230f); // #d99bff purple, the rare pool
            lbl.raycastTarget = true; // hover target for the gear preview

            // Hover → mock item card beside the hovered row; exit destroys it. The row's RectTransform
            // drives the tooltip anchor (world-space), so it tracks wherever the scroll placed the row.
            UiKit.Hover(lbl.gameObject, () => ShowImprintPreview(def, strength, row), DestroyImprintPreview);
        }

        /// <summary>Inline imprint payload text: "✦ {chance}% of drops: +{value} {stat} ({prefix|suffix})".
        /// The value is formatted exactly as gear affixes render this StatKey in the bag.</summary>
        private string ImprintText(ModifierDef def, int strength)
        {
            double value = def.ImprintPerStrength * strength;
            string slot = def.ImprintSlot == ImprintSlot.Prefix ? "prefix" : "suffix";
            return $"✦ {def.ImprintChance * 100:0}% of drops: +{StatDisplay.Value(def.ImprintStat, value)} {StatDisplay.Label(def.ImprintStat)} ({slot})";
        }

        // ---- imprint gear preview (hover a rare row's imprint text) ----

        private const float PreviewW = 230f, PreviewH = 120f;

        /// <summary>Build the mock item-card tooltip beside a rare row: a generic drop ("Any {slot} drop")
        /// with two muted normal affix lines, then the imprint bonus line in purple with a "✦ imprinted"
        /// tag — communicating "your normal drop, PLUS this bonus line". Parented to the CANVAS (not the
        /// panel — the Window panel's RectMask2D would clip a tooltip that sits outside it) and pinned by
        /// the hovered row's world position so it tracks the row wherever the scroll placed it.</summary>
        private void ShowImprintPreview(ModifierDef def, int strength, RectTransform row)
        {
            DestroyImprintPreview();
            if (_canvas == null) return;

            // Parent to the canvas so the panel mask can't clip the card; the card's own top-left origin
            // still drives its internal AnchorTL labels below.
            var card = UiKit.Panel(_canvas.transform, new Vector2(PreviewW, PreviewH), Theme.BgHudCard);
            _imprintPreview = card.gameObject;
            var crt = card.rectTransform;
            crt.pivot = new Vector2(1f, 1f); // top-right corner is the anchor point — the card grows down-left

            // Pin the card's top-right corner just left of the hovered row's top-left (world-space, so it
            // follows the row inside the scroll). Overlay canvas world coords == screen pixels, so setting
            // .position places the card correctly regardless of layout.
            var corners = new Vector3[4];
            row.GetWorldCorners(corners); // [1] = top-left
            crt.position = corners[1] + new Vector3(-8f, 0f, 0f); // -8 = small gap left of the row

            // "Any gear drop" — the prefix/suffix word describes the AFFIX position (shown in the row
            // text), not which items can drop; any gear slot can carry the imprint.
            var title = UiKit.Label(card.transform, "Any gear drop", 14, TextAnchor.UpperLeft, new Vector2(210f, 20f), Vector2.zero);
            title.color = new Color(0.82f, 0.85f, 0.90f);
            AnchorTL(title, new Vector2(12f, -10f));

            // Two generic normal affix lines (grey) — the "your normal drop" part of the message.
            var a1 = UiKit.Label(card.transform, "+… Atk", 12, TextAnchor.UpperLeft, new Vector2(210f, 16f), Vector2.zero);
            a1.color = new Color(0.55f, 0.58f, 0.63f);
            AnchorTL(a1, new Vector2(12f, -36f));
            var a2 = UiKit.Label(card.transform, "+… Hp", 12, TextAnchor.UpperLeft, new Vector2(210f, 16f), Vector2.zero);
            a2.color = new Color(0.55f, 0.58f, 0.63f);
            AnchorTL(a2, new Vector2(12f, -54f));

            // The imprint bonus line (purple) + the "✦ imprinted" tag — the extra line tuning grants.
            double value = def.ImprintPerStrength * strength;
            var imp = UiKit.Label(card.transform, $"+{StatDisplay.Value(def.ImprintStat, value)} {StatDisplay.Label(def.ImprintStat)}", 12, TextAnchor.UpperLeft, new Vector2(210f, 16f), Vector2.zero);
            imp.color = new Color(0.85f, 0.61f, 1f);
            AnchorTL(imp, new Vector2(12f, -78f));
            var tag = UiKit.Label(card.transform, "✦ imprinted", 11, TextAnchor.UpperLeft, new Vector2(210f, 16f), Vector2.zero);
            tag.color = new Color(0.72f, 0.52f, 0.92f);
            AnchorTL(tag, new Vector2(12f, -98f));
        }

        private void DestroyImprintPreview()
        {
            if (_imprintPreview != null) { Destroy(_imprintPreview); _imprintPreview = null; }
        }

        // ---- summaries ----

        private List<(ModifierDef def, int strength)> OwnedDefs(MonsterModifiers mods)
        {
            var list = new List<(ModifierDef, int)>();
            foreach (var id in _cfg.ModifierUnlockOrder) // farm-depth mods, in stable unlock order (boring → spicy)
                if (mods.Owned.TryGetValue(id, out var strength) && _cfg.Modifiers.TryGetValue(id, out var def))
                    list.Add((def, strength));

            // Tower-gated rare mods aren't in the farm unlock order — append them, ordered by unlock floor.
            var tower = new List<(string id, ModifierDef def, int strength)>();
            foreach (var kv in mods.Owned)
                if (!_cfg.ModifierUnlockOrder.Contains(kv.Key) && _cfg.Modifiers.TryGetValue(kv.Key, out var def))
                    tower.Add((kv.Key, def, kv.Value));
            tower.Sort((a, b) => a.def.TowerUnlockFloor.CompareTo(b.def.TowerUnlockFloor));
            foreach (var (id, def, strength) in tower) list.Add((def, strength));
            return list;
        }

        /// <summary>A compact gold ▲ chip = effective% − base% for one number, e.g. " <color=#ffd27f>▲2%</color>".
        /// Empty when untuned or when the rounded delta is 0% (so a 0.4% gain doesn't show a fake "▲0%").
        /// <paramref name="pctPerStr"/> is the per-strength percentage coefficient; base = ×strength,
        /// effective = ×eff.</summary>
        private static string Delta(double pctPerStr, int strength, double eff)
        {
            int baseP = Mathf.RoundToInt((float)(pctPerStr * strength));
            int effP = Mathf.RoundToInt((float)(pctPerStr * eff));
            int d = effP - baseP;
            // NBSP so a line wrap can never orphan the ▲ chip from its number ("+25% XP / ▲1%" —
            // caught on the Chaining row's capture).
            return d > 0 ? $" <color=#ffd27f>▲{d}%</color>" : "";
        }

        // Effective monster-stat/behavior summary. When tuned (eff > strength), each % number gets a gold
        // ▲ delta showing what tuning added over the strength-only base.
        private string MonsterSummary(ModifierDef def, int strength, double eff)
        {
            var parts = new List<string>();
            foreach (var kv in def.StatPerStrength)
                parts.Add($"+{kv.Value * eff * 100:0}% {StatName(kv.Key)}{Delta(kv.Value * 100, strength, eff)}");
            double frac = Mathf.Min((float)_cfg.Balance.ModifierBehaviorCap, (float)(def.BehaviorPerStrength * eff)) * 100;
            // Behavior fractions are capped, so a naive per-strength delta could overstate the gain near
            // the cap — compute base as the capped strength-only fraction and subtract.
            double baseFrac = Mathf.Min((float)_cfg.Balance.ModifierBehaviorCap, (float)(def.BehaviorPerStrength * strength)) * 100;
            string bDelta = Mathf.RoundToInt((float)(frac - baseFrac)) > 0
                ? $" <color=#ffd27f>▲{Mathf.RoundToInt((float)(frac - baseFrac))}%</color>" : "";
            if (def.Behavior == ModifierBehavior.Vampiric) parts.Add($"lifesteal {frac:0}%{bDelta}");
            else if (def.Behavior == ModifierBehavior.Thorns) parts.Add($"reflect {frac:0}%{bDelta}");
            else if (def.Behavior == ModifierBehavior.Splash) parts.Add("attacks splash the party");
            else if (def.Behavior == ModifierBehavior.Chain) parts.Add("attacks chain");
            return string.Join(", ", parts);
        }

        private static string ChannelName(ModifierReward c) => c switch
        {
            ModifierReward.Gold => "gold",
            ModifierReward.Xp => "XP",
            ModifierReward.DropRate => "drop rate",
            _ => "reward",
        };

        private static string RewardSummary(ModifierDef def, int strength, double eff)
        {
            var parts = new List<string>();
            foreach (var p in def.Rewards) // hybrid mods list several channels
                parts.Add($"+{p.PerStrength * eff * 100:0}% {ChannelName(p.Channel)}{Delta(p.PerStrength * 100, strength, eff)}");
            return string.Join(", ", parts);
        }

        private string NetSummary(HashSet<string> applied)
        {
            var mods = _view.CurrentSave.Modifiers;
            double gold = 0, xp = 0, drop = 0;
            foreach (var id in applied)
                if (_cfg.Modifiers.TryGetValue(id, out var def) && mods.Owned.TryGetValue(id, out var strength))
                {
                    double eff = strength * Modifiers.TuningOf(_view.CurrentSave, id);
                    foreach (var p in def.Rewards)
                    {
                        double r = p.PerStrength * eff * 100;
                        switch (p.Channel)
                        {
                            case ModifierReward.Gold: gold += r; break;
                            case ModifierReward.Xp: xp += r; break;
                            case ModifierReward.DropRate: drop += r; break;
                        }
                    }
                }
            if (applied.Count == 0) return "Nothing applied yet — slot modifiers for harder mobs and bigger rewards.";
            var parts = new List<string>();
            if (gold > 0) parts.Add($"+{gold:0}% gold");
            if (xp > 0) parts.Add($"+{xp:0}% XP");
            if (drop > 0) parts.Add($"+{drop:0}% drop rate");
            return $"<b>{applied.Count} applying</b>   ·   {string.Join("   ", parts)}";
        }

        private static string StatName(StatKey k) => k switch
        {
            StatKey.Hp => "HP",
            StatKey.Atk => "Atk",
            StatKey.Def => "Def",
            StatKey.MoveSpd => "move spd",
            StatKey.AtkSpd => "atk spd",
            _ => k.ToString(),
        };

        /// <summary>Top-left anchor helper for the imprint tooltip's own fixed-size internals (the card is
        /// a self-contained 230×120 mock, not one of the migrated windows — its labels stay hand-anchored).</summary>
        private static void AnchorTL(Component c, Vector2 pos)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
        }
    }
}
