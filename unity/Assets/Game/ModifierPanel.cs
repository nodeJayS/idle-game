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

        private const float RowH = 66f;
        private const float HeaderH = 96f;

        private void Build()
        {
            _canvas = UiKit.CreateCanvas("ModifierCanvas", transform, sortOrder: 90);
            var owned = OwnedSorted();

            float bodyH = Mathf.Max(1, owned.Count) * RowH;
            float h = Mathf.Min(HeaderH + bodyH + 22f, 470f);
            var panel = UiKit.Panel(_canvas.transform, new Vector2(560f, h), new Color(0.09f, 0.10f, 0.13f, 0.98f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 50f); // above the bottom control bar

            var title = UiKit.Label(panel.transform, "Monster Modifiers", 22, TextAnchor.UpperLeft, new Vector2(360f, 30f), Vector2.zero);
            title.color = new Color(0.85f, 0.62f, 1f);
            AnchorTL(title, new Vector2(22f, -14f));

            var net = UiKit.Label(panel.transform, NetSummary(owned), 14, TextAnchor.UpperLeft, new Vector2(520f, 40f), Vector2.zero);
            net.color = new Color(0.80f, 0.84f, 0.90f);
            net.supportRichText = true;
            AnchorTL(net, new Vector2(22f, -50f));

            var close = UiKit.TextButton(panel.transform, "Close", new Vector2(84f, 30f), Vector2.zero, Close, 15);
            AnchorTR((RectTransform)close.transform, new Vector2(-14f, -14f));

            if (owned.Count == 0)
            {
                int firstAt = _cfg.Balance.ModifierNewEveryStages;
                var empty = UiKit.Label(panel.transform, $"Push deeper to unlock modifiers — your first unlocks at stage {firstAt}.", 15, TextAnchor.MiddleCenter, new Vector2(500f, 40f), Vector2.zero);
                empty.color = new Color(0.70f, 0.74f, 0.80f);
                AnchorTL(empty, new Vector2(30f, -HeaderH - 10f));
                return;
            }

            for (int i = 0; i < owned.Count; i++)
                BuildRow(panel.transform, owned[i].def, owned[i].strength, owned[i].active, -HeaderH - i * RowH);
        }

        private void BuildRow(Transform parent, ModifierDef def, int strength, bool active, float y)
        {
            var name = UiKit.Label(parent, $"{def.Name}   ·   str {strength}", 16, TextAnchor.UpperLeft, new Vector2(360f, 22f), Vector2.zero);
            name.color = new Color((float)def.TintR, (float)def.TintG, (float)def.TintB) * 1.15f;
            AnchorTL(name, new Vector2(22f, y - 6f));

            // Mechanical mods imprint their signature onto drops — flag it so the player connects the
            // dangerous mod to the exclusive loot it seeds.
            string mech = def.Mechanical ? "    ·    <color=#d99bff>✦ imprints gear</color>" : "";
            var sub = UiKit.Label(parent,
                $"{MonsterSummary(def, strength)}    ·    <color=#9fe0a0>{RewardSummary(def, strength)}</color>{mech}",
                12, TextAnchor.UpperLeft, new Vector2(440f, 30f), Vector2.zero);
            sub.color = new Color(0.78f, 0.82f, 0.88f);
            sub.supportRichText = true;
            AnchorTL(sub, new Vector2(22f, y - 32f));

            // Loadout cap: once MaxActiveModifiers are on, the remaining OFF rows are locked.
            bool atCap = _view.CurrentSave.Modifiers.Active.Count >= _cfg.Balance.MaxActiveModifiers;
            bool locked = !active && atCap;
            var btn = UiKit.TextButton(parent, active ? "ON" : (locked ? "FULL" : "OFF"), new Vector2(74f, 36f), Vector2.zero,
                locked ? (System.Action)(() => { }) : () => { _view.SetModifierActive(def.Id, !active); Rebuild(); }, 15);
            btn.interactable = !locked;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active ? new Color(0.30f, 0.55f, 0.33f)
                                       : locked ? new Color(0.22f, 0.24f, 0.28f) : new Color(0.30f, 0.32f, 0.38f);
            AnchorTR((RectTransform)btn.transform, new Vector2(-16f, y - 2f));
        }

        // ---- summaries ----

        private List<(ModifierDef def, int strength, bool active)> OwnedSorted()
        {
            var list = new List<(ModifierDef, int, bool)>();
            var mods = _view.CurrentSave.Modifiers;
            foreach (var id in _cfg.ModifierUnlockOrder) // farm-depth mods, in stable unlock order (boring → spicy)
                if (mods.Owned.TryGetValue(id, out var strength) && _cfg.Modifiers.TryGetValue(id, out var def))
                    list.Add((def, strength, mods.Active.Contains(id)));

            // Tower-gated mechanical mods aren't in the farm unlock order — append them after, ordered
            // by the floor they unlock at, so an earned mechanic (e.g. Volatile) shows in the loadout.
            var towerMods = new List<(string id, ModifierDef def)>();
            foreach (var kv in mods.Owned)
                if (!_cfg.ModifierUnlockOrder.Contains(kv.Key) && _cfg.Modifiers.TryGetValue(kv.Key, out var def))
                    towerMods.Add((kv.Key, def));
            towerMods.Sort((a, b) => a.def.TowerUnlockFloor.CompareTo(b.def.TowerUnlockFloor));
            foreach (var (id, def) in towerMods)
                list.Add((def, mods.Owned[id], mods.Active.Contains(id)));
            return list;
        }

        private string MonsterSummary(ModifierDef def, int strength)
        {
            var parts = new List<string>();
            foreach (var kv in def.StatPerStrength)
                parts.Add($"+{kv.Value * strength * 100:0}% {StatName(kv.Key)}");
            double frac = Mathf.Min((float)_cfg.Balance.ModifierBehaviorCap, (float)(def.BehaviorPerStrength * strength)) * 100;
            if (def.Behavior == ModifierBehavior.Vampiric) parts.Add($"lifesteal {frac:0}%");
            else if (def.Behavior == ModifierBehavior.Thorns) parts.Add($"reflect {frac:0}%");
            else if (def.Behavior == ModifierBehavior.Splash) parts.Add("attacks splash the party");
            return string.Join(", ", parts);
        }

        private static string RewardSummary(ModifierDef def, int strength)
        {
            double pct = def.RewardPerStrength * strength * 100;
            string channel = def.Reward switch
            {
                ModifierReward.Gold => "gold",
                ModifierReward.Xp => "XP",
                ModifierReward.DropRate => "drop rate",
                _ => "reward",
            };
            return $"+{pct:0}% {channel}";
        }

        private string NetSummary(List<(ModifierDef def, int strength, bool active)> owned)
        {
            int n = 0; double gold = 0, xp = 0, drop = 0;
            foreach (var (def, strength, active) in owned)
            {
                if (!active) continue;
                n++;
                double r = def.RewardPerStrength * strength * 100;
                switch (def.Reward)
                {
                    case ModifierReward.Gold: gold += r; break;
                    case ModifierReward.Xp: xp += r; break;
                    case ModifierReward.DropRate: drop += r; break;
                }
            }
            int max = _cfg.Balance.MaxActiveModifiers;
            if (n == 0) return $"None active (0/{max}) — slot modifiers for harder mobs and bigger rewards.";
            var parts = new List<string>();
            if (gold > 0) parts.Add($"+{gold:0}% gold");
            if (xp > 0) parts.Add($"+{xp:0}% XP");
            if (drop > 0) parts.Add($"+{drop:0}% drop rate");
            return $"<b>{n}/{max} active</b>   ·   {string.Join("   ", parts)}";
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
