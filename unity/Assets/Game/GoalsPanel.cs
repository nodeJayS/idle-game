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
        private enum Tab { Today, Achievements, Codex, Login, Season }
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
            if (Progression.FeatureUnlocked(Feature.Achievements, save))
            {
                tabs.Add((Tab.Achievements, "Achievements"));
                tabs.Add((Tab.Codex, "Codex")); // the codex is achievement-family — same FTUE reveal (10.15)
                // Season shares the Achievements gate too (10.16b): the battle pass is fed BY completed
                // quests (an auto-pay ladder like the codex/achievements), so it belongs with the family
                // it lives alongside — a fresh save that hasn't met the member systems yet won't see it.
                tabs.Add((Tab.Season, "Season"));
            }
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
                case Tab.Codex:        BuildCodex(body, save);        break;
                case Tab.Season:       BuildSeason(body, save, now);  break;
                case Tab.Login:        BuildLogin(body, save, now);   break;
                default:               BuildToday(body, save, now);   break;
            }
        }

        // ---- Today: the rolling goal board (read-only mirror of the HUD panel, with room) ----

        private void BuildToday(RectTransform body, SaveState save, long now)
        {
            BuildLiveEvents(body, now); // 10.16b: the live-ops strip sits above the goal board
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

        // ---- Live events: the live-ops strip (10.16b, mobile arc MM4) ----

        /// <summary>The active live-ops events at <paramref name="now"/>: one two-line box each (name +
        /// ceil'd countdown, then the effect blurb). When nothing is live, name the NEXT window instead —
        /// the sooner of the next weekend zone boost (Sat 00:00 UTC) or mutated crypt (Wed 00:00 UTC),
        /// computed client-side (Events exposes no "next window" query and this slice must NOT edit
        /// GameCore). Countdowns display-CEIL the hours (the crypt-key countdown rule).</summary>
        private void BuildLiveEvents(RectTransform body, long now)
        {
            var head = PanelKit.Row(body, 22f);
            PanelKit.TextCell(head, "LIVE EVENTS", Theme.FsLabel, Theme.TextMuted, TextAnchor.MiddleLeft, flex: 1f);

            var active = Events.Active(_cfg, now);
            if (active.Count == 0)
            {
                // Idle strip: point at whichever window opens next so the section is never empty/dead.
                long satMs = NextWeekdayMidnightUtcMs(now, System.DayOfWeek.Saturday);
                long wedMs = NextWeekdayMidnightUtcMs(now, System.DayOfWeek.Wednesday);
                bool weekendSooner = satMs <= wedMs;
                long nextMs = weekendSooner ? satMs : wedMs;
                long hrs = (nextMs - now + 3_599_999) / 3_600_000; // ceil to whole hours
                string label = weekendSooner ? "Weekend boost" : "Mutated Crypt";
                var row = PanelKit.Row(body, CodexRowH);
                PanelKit.TextCell(row, $"{label} in {hrs}h", Theme.FsSmall, Theme.TextBody, TextAnchor.MiddleLeft, flex: 1f);
                return;
            }

            foreach (var ev in active)
            {
                long hrs = (ev.EndMs - now + 3_599_999) / 3_600_000; // ceil (never under-promise the window)
                var box = PanelKit.VStack(body, Theme.GapXs);
                var top = PanelKit.Row(box, HeadRowH);
                // 10.20c: name composed client-side via Loc off Id + ZoneIndex (EventInfo.Name is
                // untranslatable GameCore English, compat-only). The rest of this panel sweeps in phase 2.
                PanelKit.TextCell(top, StatDisplay.EventName(ev, _cfg), Theme.FsBody, Theme.AccentGold, TextAnchor.MiddleLeft, flex: 1f);
                PanelKit.TextCell(top, $"ends in {hrs}h", Theme.FsSmall, Theme.TextMuted, TextAnchor.MiddleRight, flex: 1f);
                var sub = PanelKit.Row(box, SubRowH);
                PanelKit.TextCell(sub, EventEffect(ev, now), Theme.FsTiny, Theme.TextBody, TextAnchor.MiddleLeft, flex: 1f);
            }
        }

        /// <summary>The one-line effect blurb for an active event, magnitudes read live from the config
        /// knobs (so a rebalance flows through). Zone name resolved from <see cref="Events.ZoneBoost"/>.</summary>
        private string EventEffect(EventInfo ev, long now)
        {
            if (ev.Id == Events.WeekendZoneBoostId)
            {
                var zb = Events.ZoneBoost(_cfg, now);
                string zone = zb != null && zb.Value.ZoneIndex < _cfg.Zones.Count
                    ? _cfg.Zones[zb.Value.ZoneIndex].Name : "a zone";
                return $"+{_cfg.Balance.EventZoneBoostMult * 100:0}% gold / XP / drops in {zone}";
            }
            if (ev.Id == Events.MutatedCryptId)
                return $"+1 crypt modifier, +{_cfg.Balance.EventCryptDustMult * 100:0}% dust";
            return "";
        }

        /// <summary>Epoch-ms of the next <paramref name="dow"/> at 00:00 UTC strictly after
        /// <paramref name="now"/> — the client-side stand-in for a "when does the next window open" query
        /// GameCore deliberately doesn't expose this slice. Mirrors Events' UTC day-boundary math.</summary>
        private static long NextWeekdayMidnightUtcMs(long now, System.DayOfWeek dow)
        {
            var utc = System.DateTimeOffset.FromUnixTimeMilliseconds(now).UtcDateTime;
            int delta = (((int)dow - (int)utc.DayOfWeek) + 7) % 7;
            var target = utc.Date.AddDays(delta == 0 ? 7 : delta); // today's midnight already passed ⇒ next week
            return new System.DateTimeOffset(target, System.TimeSpan.Zero).ToUnixTimeMilliseconds();
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

        // ---- Codex: the collection browse surface (monsters / sets / zones) ----

        // A compact, non-interactive codex row: name on the left, a status string on the right. The
        // codex is a long browse list (§7.5 sibling of the achievement ladder), so rows are text-only
        // and short — no bars, no buttons.
        private const float CodexRowH = 26f;

        private static void CodexRow(RectTransform list, string name, string status, Color statusColor)
        {
            var row = PanelKit.Row(list, CodexRowH);
            PanelKit.TextCell(row, name, Theme.FsSmall, Theme.TextBody, TextAnchor.MiddleLeft, flex: 1f);
            PanelKit.TextCell(row, status, Theme.FsSmall, statusColor, TextAnchor.MiddleRight, flex: 1f);
        }

        // A section divider inside the scroll list (MONSTERS / SETS / ZONES).
        private static void CodexHeader(RectTransform list, string title)
        {
            var row = PanelKit.Row(list, 22f);
            PanelKit.TextCell(row, title, Theme.FsLabel, Theme.TextMuted, TextAnchor.MiddleLeft, flex: 1f);
        }

        private void BuildCodex(RectTransform body, SaveState save)
        {
            // Header: the derived account drip + how many tiers feed it (gold accent, achievement-family).
            double pct = Codex.AccountBuffPct(save, _cfg) * 100.0;
            int tiers = Codex.CompletedTiers(save, _cfg);
            var head = PanelKit.Row(body, HeadRowH);
            PanelKit.TextCell(head, $"Codex bonus: +{pct:0.0}% Hp / Atk / Def  ({tiers} tiers completed)",
                Theme.FsLabel, Theme.AccentGold, TextAnchor.MiddleLeft, flex: 1f);

            var list = UiKit.ScrollColumnFill(body, spacing: Theme.GapS);

            // MONSTERS: lifetime kills, filled/empty tier pips, and progress to the next threshold.
            CodexHeader(list, "MONSTERS");
            int killTiers = _cfg.Balance.CodexKillTiers.Length;
            foreach (var m in Codex.MonsterEntries(save, _cfg))
            {
                string status;
                Color col;
                if (m.Maxed)
                {
                    status = new string('●', killTiers) + "  MAX";
                    col = Theme.TextDim;
                }
                else
                {
                    string pips = new string('●', m.TiersCompleted) + new string('○', killTiers - m.TiersCompleted);
                    status = $"{pips}  {Num.CompactFloor(m.Kills)}/{Num.CompactCeil(m.NextThreshold)}";
                    col = m.TiersCompleted > 0 ? Theme.AccentGoldWarm : Theme.TextMuted;
                }
                CodexRow(list, m.Name, status, col);
            }

            // SETS: reachable slots seen vs required, with a teal COMPLETE tag when the set is whole.
            CodexHeader(list, "SETS");
            foreach (var s in Codex.SetEntries(save, _cfg))
            {
                string status = s.Complete
                    ? $"{s.SlotsSeen}/{s.SlotsRequired} slots  ·  COMPLETE"
                    : $"{s.SlotsSeen}/{s.SlotsRequired} slots";
                CodexRow(list, s.Name, status, s.Complete ? Theme.SetBonus : Theme.TextMuted);
            }

            // ZONES: entered/cleared, derived from HighestStage.
            CodexHeader(list, "ZONES");
            foreach (var z in Codex.ZoneEntries(save, _cfg))
            {
                string status = z.Cleared ? "Cleared" : z.Entered ? "Entered" : "—";
                Color col = z.Cleared ? Theme.Good : z.Entered ? Theme.TextBody : Theme.TextDim;
                CodexRow(list, z.Name, status, col);
            }
        }

        // ---- Season: the monthly battle-pass track (10.16b, mobile arc MM4) ----

        private void BuildSeason(RectTransform body, SaveState save, long now)
        {
            var snap = Season.Snapshot(save, _cfg, now);
            int tierCount = _cfg.Balance.SeasonTierCount;

            // Header (gold): identity, points, tiers auto-paid / total, days left (already ceil'd by snap).
            var head = PanelKit.Row(body, HeadRowH);
            PanelKit.TextCell(head,
                $"Season {snap.Id} — {Num.CompactFloor(snap.Points)} pts  ·  tier {snap.TiersPaid}/{tierCount}  ·  {snap.DaysLeft}d left",
                Theme.FsLabel, Theme.AccentGold, TextAnchor.MiddleLeft, flex: 1f);

            // Progress toward the next tier (points ceil the threshold like a goal; 0 = capstone reached).
            var prog = PanelKit.Row(body, SubRowH);
            string progText = snap.NextTierAt > 0
                ? $"{Num.CompactFloor(snap.Points)} / {Num.CompactCeil(snap.NextTierAt)} pts to the next tier"
                : "All tiers complete — capstone reached.";
            PanelKit.TextCell(prog, progText, Theme.FsSmall, Theme.TextMuted, TextAnchor.MiddleLeft, flex: 1f);

            // The ladder — one compact row per tier (codex-row idiom, scroll). State colours: PAID rows
            // dim, the NEXT tier gold-highlighted, upcoming tiers body. NEXT = the first unpaid tier.
            var list = UiKit.ScrollColumnFill(body, spacing: Theme.GapS);
            foreach (var r in snap.Ladder)
            {
                bool paid = r.Tier <= snap.TiersPaid;
                bool next = r.Tier == snap.TiersPaid + 1;
                string reward = r.IsMilestone ? $"{Num.CompactFloor(r.Gems)} gems ★" : $"{Num.CompactFloor(r.Gold)} gold";
                string state = paid ? "PAID" : next ? "NEXT" : "";
                Color col = paid ? Theme.TextDim : next ? Theme.AccentGold : Theme.TextBody;
                var row = PanelKit.Row(list, CodexRowH);
                PanelKit.TextCell(row, $"Tier {r.Tier}", Theme.FsSmall, col, TextAnchor.MiddleLeft, flex: 1f);
                PanelKit.TextCell(row, state.Length > 0 ? $"{reward}   {state}" : reward,
                    Theme.FsSmall, col, TextAnchor.MiddleRight, flex: 1f);
            }

            // Explainer (muted) — the source of points + the monthly reset.
            var cap = PanelKit.Row(body, HeadRowH);
            PanelKit.TextCell(cap, "Complete quests to earn season points — the track resets monthly.",
                Theme.FsSmall, Theme.TextDim, TextAnchor.MiddleLeft, flex: 1f);
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
