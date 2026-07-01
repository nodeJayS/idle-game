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

        public bool IsOpen => _canvas != null;

        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }

        public void Toggle() { if (IsOpen) Close(); else Build(); }

        public void Close()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
        }

        private void Rebuild() { Close(); Build(); }

        // ---- build ----

        private const float RowH = 60f;
        private const float SectionH = 26f;
        private const float HeaderH = 92f;

        private void Build()
        {
            _canvas = UiKit.CreateCanvas("ModifierCanvas", transform, sortOrder: 90);
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
            int sections = (normals.Count > 0 ? 1 : 0) + (prefixes.Count > 0 ? 1 : 0) + (suffixes.Count > 0 ? 1 : 0);
            float h = Mathf.Min(HeaderH + rowCount * RowH + sections * SectionH + 16f, 600f);

            var panel = UiKit.Panel(_canvas.transform, new Vector2(580f, h), new Color(0.09f, 0.10f, 0.13f, 0.98f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 40f); // above the bottom control bar

            var title = UiKit.Label(panel.transform, "Monster Modifiers", 22, TextAnchor.UpperLeft, new Vector2(360f, 30f), Vector2.zero);
            title.color = new Color(0.85f, 0.62f, 1f);
            AnchorTL(title, new Vector2(22f, -14f));

            var net = UiKit.Label(panel.transform, NetSummary(applied), 14, TextAnchor.UpperLeft, new Vector2(540f, 40f), Vector2.zero);
            net.color = new Color(0.80f, 0.84f, 0.90f);
            net.supportRichText = true;
            AnchorTL(net, new Vector2(22f, -48f));

            var close = UiKit.TextButton(panel.transform, "Close", new Vector2(84f, 30f), Vector2.zero, Close, 15);
            AnchorTR((RectTransform)close.transform, new Vector2(-14f, -14f));

            if (rowCount == 0)
            {
                int firstAt = _cfg.Balance.ModifierNewEveryStages;
                var empty = UiKit.Label(panel.transform, $"Push deeper to unlock modifiers — your first unlocks at stage {firstAt}.", 15, TextAnchor.MiddleCenter, new Vector2(520f, 40f), Vector2.zero);
                empty.color = new Color(0.70f, 0.74f, 0.80f);
                AnchorTL(empty, new Vector2(30f, -HeaderH - 10f));
                return;
            }

            float y = -HeaderH;
            y = Section(panel.transform, "Normal", normals, applied, y, _cfg.Balance.MaxActiveModifiers);
            y = Section(panel.transform, "Rare — Prefix", prefixes, applied, y, _cfg.Balance.MaxActiveRarePerSlot);
            y = Section(panel.transform, "Rare — Suffix", suffixes, applied, y, _cfg.Balance.MaxActiveRarePerSlot);
        }

        /// <summary>Draw a pool's header (name + n/cap, plus a "needs N to apply" hint for a half-filled
        /// rare slot) then its rows. Returns the next y. No-op for an empty pool.</summary>
        private float Section(Transform parent, string title, List<(ModifierDef def, int strength)> rows,
                              HashSet<string> applied, float y, int cap)
        {
            if (rows.Count == 0) return y;
            var mods = _view.CurrentSave.Modifiers;
            int activeN = 0;
            foreach (var (def, _) in rows) if (mods.Active.Contains(def.Id)) activeN++;

            string hint = "";
            bool rare = rows[0].def.Mechanical;
            if (rare && activeN > 0 && activeN < _cfg.Balance.MinActiveRarePerSlot)
                hint = $"   <color=#e0a070>· needs {_cfg.Balance.MinActiveRarePerSlot} to apply</color>";

            var hdr = UiKit.Label(parent, $"{title}   <color=#8a8f99>{activeN}/{cap}</color>{hint}", 13,
                                  TextAnchor.UpperLeft, new Vector2(540f, 20f), Vector2.zero);
            hdr.color = new Color(0.62f, 0.66f, 0.74f); hdr.supportRichText = true;
            AnchorTL(hdr, new Vector2(20f, y - 4f));
            y -= SectionH;

            foreach (var (def, strength) in rows)
            {
                BuildRow(parent, def, strength, mods.Active.Contains(def.Id), applied.Contains(def.Id), y);
                y -= RowH;
            }
            return y;
        }

        private void BuildRow(Transform parent, ModifierDef def, int strength, bool active, bool applies, float y)
        {
            var save = _view.CurrentSave;
            double tuning = Modifiers.TuningOf(save, def.Id);
            double eff = strength * tuning; // shop tuning scales BOTH danger and reward
            string tuned = tuning > 1.0001 ? $"   <color=#ffd27f>+{(tuning - 1) * 100:0}% tuned</color>" : "";

            var name = UiKit.Label(parent, $"{def.Name}   ·   str {strength}{tuned}", 16, TextAnchor.UpperLeft, new Vector2(360f, 22f), Vector2.zero);
            name.color = new Color((float)def.TintR, (float)def.TintG, (float)def.TintB) * 1.15f;
            name.supportRichText = true;
            AnchorTL(name, new Vector2(34f, y - 4f));

            string mech = def.Mechanical ? "  ·  <color=#d99bff>✦ imprints</color>" : "";
            string inert = (active && !applies) ? "  ·  <color=#e0a070>inert (needs pair)</color>" : "";
            var sub = UiKit.Label(parent,
                $"{MonsterSummary(def, eff)}  ·  <color=#9fe0a0>{RewardSummary(def, eff)}</color>{mech}{inert}",
                12, TextAnchor.UpperLeft, new Vector2(340f, 30f), Vector2.zero);
            sub.color = new Color(0.78f, 0.82f, 0.88f);
            sub.supportRichText = true;
            AnchorTL(sub, new Vector2(34f, y - 28f));

            // ON/OFF (rightmost). Lock OFF rows only when THIS mod's own pool is full (per-pool cap).
            bool full = !active && Modifiers.PoolFull(save, _cfg, def);
            var btn = UiKit.TextButton(parent, active ? "ON" : (full ? "FULL" : "OFF"), new Vector2(60f, 32f), Vector2.zero,
                full ? (System.Action)(() => { }) : () => { _view.SetModifierActive(def.Id, !active); Rebuild(); }, 14);
            btn.interactable = !full;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active ? new Color(0.30f, 0.55f, 0.33f)
                                       : full ? new Color(0.22f, 0.24f, 0.28f) : new Color(0.30f, 0.32f, 0.38f);
            AnchorTR((RectTransform)btn.transform, new Vector2(-14f, y - 2f));

            // Upgrade (shop): gamble tuning with gold+scrap; cost rises as the mod climbs.
            var (g, s) = Modifiers.UpgradeCost(save, _cfg, def.Id);
            bool canUp = Modifiers.CanUpgrade(save, _cfg, def.Id);
            var up = UiKit.TextButton(parent, $"⬆ {Num.Compact(g)}g+{Num.Compact(s)}s", new Vector2(120f, 32f), Vector2.zero,
                canUp ? () => { _view.UpgradeModifier(def.Id); Rebuild(); } : (System.Action)(() => { }), 12);
            up.interactable = canUp;
            var upImg = up.GetComponent<Image>();
            if (upImg != null) upImg.color = canUp ? new Color(0.34f, 0.30f, 0.46f) : new Color(0.22f, 0.22f, 0.26f);
            AnchorTR((RectTransform)up.transform, new Vector2(-84f, y - 2f));
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

        private string MonsterSummary(ModifierDef def, double eff)
        {
            var parts = new List<string>();
            foreach (var kv in def.StatPerStrength)
                parts.Add($"+{kv.Value * eff * 100:0}% {StatName(kv.Key)}");
            double frac = Mathf.Min((float)_cfg.Balance.ModifierBehaviorCap, (float)(def.BehaviorPerStrength * eff)) * 100;
            if (def.Behavior == ModifierBehavior.Vampiric) parts.Add($"lifesteal {frac:0}%");
            else if (def.Behavior == ModifierBehavior.Thorns) parts.Add($"reflect {frac:0}%");
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

        private static string RewardSummary(ModifierDef def, double eff)
        {
            var parts = new List<string>();
            foreach (var p in def.Rewards) // hybrid mods list several channels
                parts.Add($"+{p.PerStrength * eff * 100:0}% {ChannelName(p.Channel)}");
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

        private static void AnchorTL(Component c, Vector2 pos)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
        }

        private static void AnchorTR(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = pos;
        }
    }
}
