#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// "Compare anywhere" (10.5b): renders ONE power-compare section for a candidate item into a
    /// PanelKit stack — a target hero, the verdict headline, the DPS/Effective-Life deltas, and the
    /// raw stat swap. All the judgement is GameCore's (<see cref="Upgrades"/> / <see cref="Inventory"/>
    /// / <see cref="DerivedStats"/>); this only chooses pixels, mirroring the Heroes screen's own
    /// compare pane (<see cref="EquipmentView"/>) so a delta reads the same everywhere.
    ///
    /// Every line is a FIXED-height label (the Fixed/Label idiom): under a height-starved pane the
    /// flexible fills around the card crush first, never these rows, and the card adds no
    /// flexible-height surprises to the 21:9 panes it drops into.
    /// </summary>
    public static class CompareCard
    {
        // Line heights, tuned to each font size — pinned min = preferred so the card holds its
        // shape when the pane is squeezed.
        private const float HeaderH = 18f;   // FsSmall
        private const float HeadlineH = 22f; // FsBody
        private const float DerivedH = 20f;  // FsLabel
        private const float RawH = 18f;      // FsSmall
        private const int MaxRawRows = 6;    // these panes are height-tight at 21:9

        /// <summary>
        /// Build the compare section for <paramref name="item"/> into <paramref name="parent"/>.
        /// <paramref name="heroId"/> pins the target hero (the Heroes screen passes the selected
        /// hero); null picks the best fielded hero via <see cref="Upgrades.BestForItem"/> (falling
        /// back to all heroes only if the party is empty). Renders the muted "no upgrade" line and
        /// stops when there is no candidate hero at all.
        /// </summary>
        public static void Build(RectTransform parent, SaveState save, Item item, GameConfig cfg,
                                 CombatView view, string? heroId = null)
        {
            int stage = save.Progress.CurrentStage;

            // Resolve the target hero + its eval. A pinned hero evaluates directly; otherwise scope
            // BestForItem to the FIELDED party (null scope = every hero, only if the party is empty).
            string? targetId = heroId;
            Upgrades.ItemEval? eval;
            if (targetId != null)
            {
                eval = Upgrades.EvaluateForHero(save, targetId, item, cfg, stage);
            }
            else
            {
                var party = FieldedParty(save);
                eval = Upgrades.BestForItem(save, item, cfg, stage, party.Count > 0 ? party : null);
                targetId = eval?.HeroId;
            }

            if (eval == null || targetId == null)
            {
                Line(parent, Loc.T("compare.no-upgrade"), Theme.FsSmall, UpgradeTell.Side, HeaderH);
                return;
            }

            // Header: who we're comparing against, and what currently fills that slot.
            var slot = cfg.ItemBases[item.BaseId].Slot;
            Line(parent, Loc.F("compare.vs", HeroName(save, cfg, targetId), EquippedName(save, cfg, targetId, slot)),
                Theme.FsSmall, Theme.TextMuted, HeaderH);

            // §6.2 set tells: what set this joins and where the target hero would land. The
            // verdict numbers below already PRICE any 2pc/4pc threshold crossing (sets flow
            // through ComputeHeroStats) — these lines explain why the delta jumped.
            SetLines(parent, save, item, cfg, targetId);

            // Headline verdict — for ALL verdicts (a red ▼ −12% is as actionable as a green ▲ +5%).
            Line(parent, Loc.F("compare.power", UpgradeTell.Glyph(eval.Verdict), UpgradeTell.Pct(eval.DeltaPercent)),
                Theme.FsBody, UpgradeTell.Color(eval.Verdict), HeadlineH);

            // Derived deltas (DPS / Effective-Life), same rounding/glyph convention as the Heroes
            // screen's compare pane (EquipmentView.DerivedDeltaRow).
            var (before, after) = Inventory.ComparePairForHero(save, targetId, item, cfg);
            DerivedRow(parent, Loc.T("compare.dps"), DerivedStats.Dps(after) - DerivedStats.Dps(before));
            DerivedRow(parent, Loc.T("compare.eff-life"),
                DerivedStats.EffectiveHp(after, cfg, stage) - DerivedStats.EffectiveHp(before, cfg, stage));

            // Raw stat swap: the non-zero sheet-stat deltas, in canonical order. Iterating
            // StatDisplay.Order (not the raw block) keeps the under-the-hood stats (range/splash/
            // imprints) off the list, exactly as the stat sheet and the Heroes compare pane do.
            var delta = Inventory.CompareForHero(save, targetId, item, cfg);
            int shown = 0;
            foreach (var k in StatDisplay.Order)
            {
                double d = delta.Get(k);
                if (d == 0) continue;
                Line(parent, Loc.F("compare.stat-row", d > 0 ? "▲" : "▼", StatDisplay.Label(k), StatDisplay.Delta(k, d)),
                    Theme.FsSmall, d > 0 ? Theme.Good : Theme.Bad, RawH);
                if (++shown >= MaxRawRows) break;
            }

            // The bag tile's ▲ badge evaluates ALL heroes, this card the fielded party — without
            // this line a badge could promise an upgrade the card then denies (it's for someone
            // on the bench). Only when unpinned, and only when the bench actually beats the field.
            if (heroId == null && eval.Verdict != Upgrades.Verdict.Upgrade)
            {
                var all = Upgrades.BestForItem(save, item, cfg, stage);
                if (all != null && all.Verdict == Upgrades.Verdict.Upgrade && all.HeroId != targetId)
                    Line(parent, Loc.F("compare.benched", UpgradeTell.Pct(all.DeltaPercent), HeroName(save, cfg, all.HeroId)),
                        Theme.FsSmall, UpgradeTell.Up, HeaderH);
            }
        }

        /// <summary>
        /// §6.2 set tells for one item: "«Set name» — n/4" where n counts the pieces
        /// <paramref name="heroId"/> would wear WITH this item equipped (the candidate's slot
        /// is excluded first, so an already-worn item counts itself exactly once), then the
        /// 2pc/4pc bonus lines — reached tiers in set teal, unreached ones dim. No-op for
        /// setless items or a set trimmed from content. heroId null = name + bonuses only.
        /// </summary>
        public static void SetLines(RectTransform parent, SaveState save, Item item, GameConfig cfg, string? heroId)
        {
            if (item.SetId == null || !cfg.Sets.TryGetValue(item.SetId, out var set)) return;

            int worn = 0;
            string wear = "";
            if (heroId != null)
            {
                var hero = save.Heroes.Find(h => h.Id == heroId);
                if (hero != null)
                {
                    var slot = cfg.ItemBases.TryGetValue(item.BaseId, out var b) ? b.Slot : (EquipSlot?)null;
                    foreach (var kv in hero.Equipped)
                    {
                        if (slot != null && kv.Key == slot.Value) continue; // the candidate replaces this slot
                        var eq = save.Inventory.Find(i => i.Id == kv.Value);
                        if (eq != null && eq.SetId == item.SetId) worn++;
                    }
                    worn++; // the candidate itself
                    wear = Loc.F("compare.set-wear", worn);
                }
            }

            Line(parent, StatDisplay.SetName(item.SetId, set.Name) + wear, Theme.FsSmall, Theme.SetBonus, RawH);
            // Both tiers on ONE rich-text line (the detail panes are height-budgeted — a third
            // set line was enough to starve the identity block at 1080p): reached tier in set
            // teal, unreached in dim.
            var bonus = Line(parent,
                $"{TierText(Loc.T("compare.tier-2pc"), set.Piece2, worn >= 2)}   "
                + TierText(Loc.T("compare.tier-4pc"), set.Piece4, worn >= 4),
                Theme.FsSmall, Color.white, RawH);
            bonus.supportRichText = true;
        }

        private static string TierText(string tag, StatBlock block, bool active)
        {
            var color = active ? Theme.SetBonus : Theme.TextDim;
            string hex = ColorUtility.ToHtmlStringRGB(color);
            var parts = new List<string>();
            foreach (var kv in block)
                parts.Add(Loc.F("compare.set-stat", StatDisplay.Value(kv.Key, kv.Value), StatDisplay.Label(kv.Key)));
            return $"<color=#{hex}>{tag} {string.Join(", ", parts)}</color>";
        }

        /// <summary>A headline derived-stat delta row (DPS / Eff. Life). Mirrors
        /// <c>EquipmentView.DerivedDeltaRow</c>: rounds to a whole number, ±0 reads dim.</summary>
        private static void DerivedRow(RectTransform into, string label, double delta)
        {
            long r = (long)Math.Round(delta);
            if (r == 0) return; // a dim "± 0" row says nothing and the panes are height-budgeted
            string arrow = r > 0 ? "▲" : "▼";
            string val = (r > 0 ? "+" : "-") + Math.Abs(r).ToString("N0");
            var color = r > 0 ? Theme.GoodBright : Theme.BadBright;
            Line(into, Loc.F("compare.derived-row", arrow, label, val), Theme.FsLabel, color, DerivedH);
        }

        /// <summary>A fixed-height compare line (Fixed/Label idiom).</summary>
        private static Text Line(RectTransform parent, string text, int fontSize, Color color, float height)
        {
            var lbl = PanelKit.Label(parent, text, fontSize, color, TextAnchor.MiddleLeft);
            PanelKit.Fixed(lbl.gameObject, height: height);
            return lbl;
        }

        private static List<string> FieldedParty(SaveState save)
        {
            var list = new List<string>();
            foreach (var id in save.Party) if (id != null) list.Add(id);
            return list;
        }

        private static string EquippedName(SaveState save, GameConfig cfg, string heroId, EquipSlot slot)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero != null && hero.Equipped.TryGetValue(slot, out var eqId))
            {
                var eq = save.Inventory.Find(i => i.Id == eqId);
                if (eq != null) return StatDisplay.BaseName(eq.BaseId);
            }
            return Loc.T("compare.empty-slot");
        }

        private static string HeroName(SaveState save, GameConfig cfg, string heroId)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero != null && cfg.Heroes.TryGetValue(hero.DefId, out var def) && !string.IsNullOrEmpty(def.Name))
                return StatDisplay.HeroName(hero.DefId, def.Name);
            return heroId;
        }
    }
}
