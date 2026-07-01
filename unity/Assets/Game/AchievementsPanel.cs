#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Achievements screen (Lever 4 — the permanent milestone ladder). Lists every achievement with
    /// the tier you're currently working toward, lifetime progress, a bar to the next threshold, and
    /// the reward it pays. A toggled overlay (build on open, destroy on close) like
    /// <see cref="ModifierPanel"/>, opened from the control bar. Read-only over the save — the crediting
    /// happens in <see cref="CombatView"/>'s Award() as game events flow in.
    /// </summary>
    public sealed class AchievementsPanel : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;
        private Canvas? _canvas;

        public bool IsOpen => _canvas != null;
        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }
        public void Toggle() { if (IsOpen) Close(); else Build(); }
        public void Close() { if (_canvas != null) Destroy(_canvas.gameObject); _canvas = null; }

        private const float RowH = 60f;
        private const float HeaderH = 74f;
        private const float PanelW = 600f;

        // Tier labels; the catalog tops out at 5 tiers, a couple spare for headroom.
        private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        private void Build()
        {
            _canvas = UiKit.CreateCanvas("AchievementsCanvas", transform, sortOrder: 90);
            var save = _view.CurrentSave;

            int rows = _cfg.Achievements.Count;
            float h = Mathf.Min(HeaderH + rows * RowH + 16f, 660f);

            var panel = UiKit.Panel(_canvas.transform, new Vector2(PanelW, h), new Color(0.09f, 0.10f, 0.13f, 0.98f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 40f); // above the bottom control bar

            var title = UiKit.Label(panel.transform, "Achievements", 22, TextAnchor.UpperLeft, new Vector2(360f, 30f), Vector2.zero);
            title.color = new Color(1f, 0.82f, 0.35f);
            AnchorTL(title, new Vector2(22f, -14f));

            int earned = 0, total = 0;
            foreach (var def in _cfg.Achievements) { earned += Achievements.TiersClaimed(save, def); total += def.Tiers.Count; }
            var summary = UiKit.Label(panel.transform, $"{earned} / {total} tiers earned", 14, TextAnchor.UpperLeft, new Vector2(360f, 22f), Vector2.zero);
            summary.color = new Color(0.80f, 0.84f, 0.90f);
            AnchorTL(summary, new Vector2(24f, -46f));

            var close = UiKit.TextButton(panel.transform, "Close", new Vector2(84f, 30f), Vector2.zero, Close, 15);
            AnchorTR((RectTransform)close.transform, new Vector2(-14f, -14f));

            float y = -HeaderH;
            foreach (var def in _cfg.Achievements) { BuildRow(panel.transform, def, save, y); y -= RowH; }
        }

        private void BuildRow(Transform parent, AchievementDef def, SaveState save, float y)
        {
            int claimed = Achievements.TiersClaimed(save, def);
            long counter = Achievements.Counter(save, def.Metric);
            bool complete = claimed >= def.Tiers.Count;

            string tierName = complete ? "✓" : Roman[Mathf.Min(claimed, Roman.Length - 1)];
            var name = UiKit.Label(parent, $"{def.Name}   {tierName}", 16, TextAnchor.UpperLeft, new Vector2(320f, 22f), Vector2.zero);
            name.color = complete ? new Color(0.55f, 0.85f, 0.55f) : new Color(1f, 0.86f, 0.5f);
            AnchorTL(name, new Vector2(26f, y - 4f));

            long prev = claimed > 0 ? def.Tiers[claimed - 1].Threshold : 0;
            long nextT = complete ? def.Tiers[def.Tiers.Count - 1].Threshold : def.Tiers[claimed].Threshold;

            // Rounding (game-design §7): progress floors (never overstate), the goal ceils
            // (never show a target as reached before the real threshold is).
            string sub = complete
                ? $"{Num.CompactFloor(counter)} {def.Unit}  ·  all tiers earned"
                : $"{Num.CompactFloor(counter)} / {Num.CompactCeil(nextT)} {def.Unit}";
            var subLbl = UiKit.Label(parent, sub, 12, TextAnchor.UpperLeft, new Vector2(340f, 18f), Vector2.zero);
            subLbl.color = new Color(0.75f, 0.79f, 0.85f);
            AnchorTL(subLbl, new Vector2(26f, y - 27f));

            // Reward preview for the tier being worked toward (right side).
            var rw = UiKit.Label(parent, complete ? "COMPLETE" : RewardText(def.Tiers[claimed]), 13,
                                 TextAnchor.UpperRight, new Vector2(230f, 20f), Vector2.zero);
            rw.color = complete ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.62f, 0.88f, 0.66f);
            AnchorTR((RectTransform)rw.transform, new Vector2(-24f, y - 6f));

            // Progress bar toward the next threshold (full + green when the ladder is complete).
            float frac = complete ? 1f
                : nextT > prev ? Mathf.Clamp01((float)((double)(counter - prev) / (nextT - prev))) : 0f;
            BuildBar(parent, y - 46f, frac, complete);
        }

        private static void BuildBar(Transform parent, float y, float frac, bool complete)
        {
            var track = new GameObject("Bar", typeof(RectTransform));
            track.transform.SetParent(parent, false);
            var trt = (RectTransform)track.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.sizeDelta = new Vector2(PanelW - 52f, 6f);
            trt.anchoredPosition = new Vector2(26f, y);
            track.AddComponent<Image>().color = new Color(0.15f, 0.16f, 0.20f);

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(track.transform, false);
            var frt = (RectTransform)fillGo.transform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(Mathf.Max(0.0001f, frac), 1f); // anchorMax.x grows the fill
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            fillGo.AddComponent<Image>().color = complete ? new Color(0.40f, 0.72f, 0.46f) : new Color(0.85f, 0.70f, 0.35f);
        }

        private static string RewardText(AchievementTier t)
        {
            // Promised rewards floor (game-design §7): never advertise more than is granted.
            var bits = new List<string>();
            if (t.RewardGold > 0) bits.Add($"{Num.CompactFloor(t.RewardGold)}g");
            if (t.RewardScrap > 0) bits.Add($"{Num.CompactFloor(t.RewardScrap)}s");
            if (t.RewardXp > 0) bits.Add($"{Num.CompactFloor(t.RewardXp)}xp");
            return bits.Count > 0 ? "+ " + string.Join("  ", bits) : "";
        }

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
