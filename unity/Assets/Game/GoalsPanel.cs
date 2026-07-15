#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The Goals hub (game-design §7.5): quests + achievements + daily login in ONE window — a
    /// consolidation + visibility surface, not a claim queue (quests and achievements auto-pay;
    /// the daily login is the only manual claim, surfaced by <see cref="Goals.Claimables"/>).
    /// Three tabs: <b>Today</b> = the rolling goal board, read-only (the ambient QuestPanel HUD
    /// stays — glanceability is its job, the hub's is depth); <b>Achievements</b> = the lifetime
    /// ladder (absorbed from the retired AchievementsPanel, hidden until its FTUE reveal);
    /// <b>Login</b> = streak state, the claim, and tomorrow's preview. Built on PanelKit/Theme;
    /// every mutation routes through <see cref="CombatView"/> so feed lines + save flush fire.
    /// </summary>
    public sealed class GoalsPanel : MonoBehaviour
    {
        // Tier labels; the catalog tops out at 5 tiers, a couple spare for headroom.
        private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        private const float BarH = 6f;      // progress-bar track height
        private const float HeadRowH = 24f; // goal/achievement title row
        private const float SubRowH = 18f;  // counter sub-row

        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        private GameObject? _panel;
        private enum Tab { Today, Achievements, Login }
        private Tab _tab = Tab.Today;

        public bool IsOpen => _panel != null;

        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }

        public void Toggle() { if (IsOpen) Close(); else Build(); }

        public void Close()
        {
            if (_panel != null) Destroy(_panel);
            _panel = null;
        }

        private void Rebuild() { Close(); Build(); }

        private static long NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void Build()
        {
            var save = _view.CurrentSave;
            long now = NowMs();

            _panel = PanelKit.Window(transform, "Goals", Close, out var body, "GoalsCanvas",
                                     sortOrder: 97, max: new Vector2(760, 640));
            PanelKit.Stack(body);

            // Tabs — the Achievements tab hides until its FTUE reveal (§7.5); the button outside
            // reveals with the earliest member system, so a fresh save can be here without it.
            var tabs = new List<(Tab tab, string label)> { (Tab.Today, "Today") };
            if (Progression.FeatureUnlocked(Feature.Achievements, save)) tabs.Add((Tab.Achievements, "Achievements"));
            tabs.Add((Tab.Login, "Login"));
            if (tabs.FindIndex(t => t.tab == _tab) < 0) _tab = Tab.Today; // selected tab currently hidden
            var labels = new string[tabs.Count];
            for (int i = 0; i < tabs.Count; i++) labels[i] = tabs[i].label;
            PanelKit.TabBar(body, labels, tabs.FindIndex(t => t.tab == _tab),
                i => { _tab = tabs[i].tab; Rebuild(); });

            // Claim strip, visible from every tab: what's waiting (today: at most the daily login)
            // and the one Claim-all verb. Amounts come straight off Goals.Claimables — the same
            // list the control-bar pip keys off, so strip, pip, and grant always agree.
            var claimables = Goals.Claimables(save, _cfg, now);
            long totalGems = 0;
            foreach (var c in claimables) totalGems += c.Gems;
            var strip = PanelKit.Row(body, Theme.TouchMin); // "Claim all" button: pin the 44 touch floor (10.13c)
            string stripText = claimables.Count == 0
                ? "Nothing waiting — quests and achievements pay out automatically."
                : claimables.Count == 1
                    ? $"Waiting: {claimables[0].Label}  ·  +{Num.CompactFloor(totalGems)} gems"
                    : $"Waiting: {claimables.Count} claims  ·  +{Num.CompactFloor(totalGems)} gems";
            PanelKit.TextCell(strip, stripText, Theme.FsSmall,
                claimables.Count > 0 ? Theme.AccentGold : Theme.TextDim, TextAnchor.MiddleLeft, flex: 1f);
            PanelKit.ButtonCell(strip, "Claim all", () => { _view.ClaimAllGoals(NowMs()); Rebuild(); },
                width: Theme.CloseW, fontSize: Theme.FsLabel, enabled: claimables.Count > 0);

            switch (_tab)
            {
                case Tab.Achievements: BuildAchievements(body, save); break;
                case Tab.Login:        BuildLogin(body, save, now);   break;
                default:               BuildToday(body, save);        break;
            }
        }

        // ---- Today: the rolling goal board (read-only mirror of the HUD panel, with room) ----

        private void BuildToday(RectTransform body, SaveState save)
        {
            var list = UiKit.ScrollColumnFill(body, spacing: Theme.GapS);
            foreach (var q in save.Quests.Active)
            {
                var box = PanelKit.VStack(list, Theme.GapXs);
                var head = PanelKit.Row(box, HeadRowH);
                PanelKit.TextCell(head, QuestPanel.QuestLabel(q), Theme.FsBody, Theme.TextBright,
                    TextAnchor.MiddleLeft, flex: 1f);
                // progress floors, the goal ceils (game-design §7 — never show a goal as met early)
                PanelKit.TextCell(head, $"{Num.CompactFloor(q.Progress)} / {Num.CompactCeil(q.Target)}",
                    Theme.FsSmall, Theme.TextMuted, TextAnchor.MiddleRight, flex: 1f);
                float frac = q.Target > 0 ? Mathf.Clamp01((float)((double)q.Progress / q.Target)) : 0f;
                Bar(box, frac, Theme.BarGreen);
            }

            var cap = PanelKit.Row(body, HeadRowH);
            PanelKit.TextCell(cap, "Quests pay out automatically when completed.", Theme.FsSmall,
                Theme.TextDim, TextAnchor.MiddleLeft, flex: 1f);
        }

        // ---- Achievements: the lifetime ladder (absorbed from AchievementsPanel) ----

        private void BuildAchievements(RectTransform body, SaveState save)
        {
            int earned = 0, total = 0;
            foreach (var def in _cfg.Achievements) { earned += Achievements.TiersClaimed(save, def); total += def.Tiers.Count; }
            var sum = PanelKit.Row(body, HeadRowH);
            PanelKit.TextCell(sum, $"{earned} / {total} tiers earned", Theme.FsLabel, Theme.TextBody,
                TextAnchor.MiddleLeft, flex: 1f);

            var list = UiKit.ScrollColumnFill(body, spacing: Theme.GapS);
            foreach (var def in _cfg.Achievements)
            {
                int claimed = Achievements.TiersClaimed(save, def);
                long counter = Achievements.Counter(save, def.Metric);
                bool complete = claimed >= def.Tiers.Count;

                var box = PanelKit.VStack(list, Theme.GapXs);

                // Name + the tier being worked toward; reward preview for that tier on the right.
                string tierName = complete ? "✓" : Roman[Mathf.Min(claimed, Roman.Length - 1)];
                var head = PanelKit.Row(box, HeadRowH);
                PanelKit.TextCell(head, $"{def.Name}   {tierName}", Theme.FsBody,
                    complete ? Theme.Good : Theme.AccentGoldWarm, TextAnchor.MiddleLeft, flex: 1f);
                PanelKit.TextCell(head, complete ? "COMPLETE" : RewardText(def.Tiers[claimed]),
                    Theme.FsSmall, Theme.Good, TextAnchor.MiddleRight, flex: 1f);

                // Rounding (game-design §7): progress floors (never overstate), the goal ceils
                // (never show a target as reached before the real threshold is).
                long prev = claimed > 0 ? def.Tiers[claimed - 1].Threshold : 0;
                long nextT = complete ? def.Tiers[def.Tiers.Count - 1].Threshold : def.Tiers[claimed].Threshold;
                string sub = complete
                    ? $"{Num.CompactFloor(counter)} {def.Unit}  ·  all tiers earned"
                    : $"{Num.CompactFloor(counter)} / {Num.CompactCeil(nextT)} {def.Unit}";
                var subRow = PanelKit.Row(box, SubRowH);
                PanelKit.TextCell(subRow, sub, Theme.FsTiny, Theme.TextBody, TextAnchor.MiddleLeft, flex: 1f);

                // Progress toward the next threshold (full + green once the ladder is complete).
                float frac = complete ? 1f
                    : nextT > prev ? Mathf.Clamp01((float)((double)(counter - prev) / (nextT - prev))) : 0f;
                Bar(box, frac, complete ? Theme.BarGreen : Theme.BarGold);
            }
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

        // ---- Login: streak state, the one manual claim, tomorrow's preview ----

        private void BuildLogin(RectTransform body, SaveState save, long now)
        {
            var box = PanelKit.Section(body, "Daily login");
            var d = save.Progress.Daily;
            PanelKit.KeyValueRow(box, "Current streak", d.Streak == 1 ? "1 day" : $"{d.Streak} days", Theme.TextBright);

            var (gems, streak, canClaim) = DailyLogin.Preview(save, _cfg, now);
            if (canClaim)
            {
                // Same reducer path as the launch modal (CombatView.ClaimDailyLogin): feed line +
                // immediate save flush for free; the preview above is what that claim will grant.
                var row = PanelKit.Row(box, Theme.BtnHs);
                PanelKit.TextCell(row, $"Today: +{Num.CompactFloor(gems)} gems (day {streak})",
                    Theme.FsBody, Theme.AccentGold, TextAnchor.MiddleLeft, flex: 1f);
                PanelKit.ButtonCell(row, "Claim", () => { _view.ClaimDailyLogin(NowMs()); Rebuild(); },
                    width: Theme.CloseW, fontSize: Theme.FsBody);
            }
            else
            {
                PanelKit.Label(box, "Claimed today ✓", Theme.FsBody, Theme.TextDim, TextAnchor.MiddleLeft);
            }

            // "Tomorrow" reads as "after I claim today" — so while today is still claimable,
            // preview against the hypothetically-claimed save (pure reducer, result discarded).
            // Raw PreviewNext on the as-is save would show the streak-RESET value next to an
            // unclaimed day-5 line, which reads like a threat instead of a promise.
            var basis = save;
            if (canClaim) { var (claimedSave, _, _, _) = DailyLogin.Claim(save, _cfg, now); basis = claimedSave; }
            var (nextGems, nextStreak) = DailyLogin.PreviewNext(basis, _cfg, now);
            PanelKit.Label(box, $"Tomorrow: +{Num.CompactFloor(nextGems)} gems (day {nextStreak})",
                Theme.FsSmall, Theme.TextMuted, TextAnchor.MiddleLeft);

            PanelKit.Flex(body); // keep the section at the top; the slack lives below it
        }

        // ---- shared ----

        /// <summary>A layout-friendly progress bar: a full-width track (LayoutElement height) whose
        /// fill is a child stretched by anchorMax.x — the QuestPanel bar idiom inside a layout cell
        /// (anchors, not positions, so it reflows with the window).</summary>
        private static void Bar(RectTransform parent, float frac, Color fill)
        {
            var track = new GameObject("Bar", typeof(RectTransform));
            track.transform.SetParent(parent, false);
            track.AddComponent<Image>().color = Theme.BarTrack;
            var le = track.AddComponent<LayoutElement>();
            le.preferredHeight = BarH; le.minHeight = BarH;
            le.flexibleHeight = 0f; le.flexibleWidth = 1f;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(track.transform, false);
            var frt = (RectTransform)fillGo.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(Mathf.Max(0.0001f, Mathf.Clamp01(frac)), 1f); // anchorMax.x grows the fill
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            fillGo.AddComponent<Image>().color = fill;
        }
    }
}
