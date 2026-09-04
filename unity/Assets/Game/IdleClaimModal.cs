#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The ONE boot arrival card (10.14b — the 30-second session, mobile arc MM2): the old idle-claim
    /// modal (design §7 — the offline-return moment) EXTENDED to also carry the daily-login (and any
    /// other <see cref="Goals.Claimables"/>) payoff, so a returning player gets one card and one tap
    /// instead of two sequential modals. Built on <see cref="PanelKit"/>.
    ///
    /// Read-only DISPLAY: it renders an <see cref="IdleReport"/> PREVIEW (<see cref="Idle.Preview"/>)
    /// plus the pending goal claims — nothing is granted until Collect, which routes the WHOLE arrival
    /// through <see cref="CombatView.ArriveClaim"/> (one atomic <see cref="Session.Arrive"/>). Because
    /// idle's LastClaimAt is untouched until then, the previewed numbers ARE what Collect grants (same
    /// save + same boot <c>now</c> ⇒ identical rolls), and a quit before Collect simply re-previews the
    /// same accrual next launch — nothing is lost.
    ///
    /// 10.23 polish: the payoff stopped being a list of labelled lines and became three TILES that
    /// land one after another and count up — the arc's card language (the gacha reveal got the same
    /// treatment), on the principle that the first thing a returning player sees should read as a
    /// reward rather than a receipt. Two rules the arc learned the hard way ride along: all timing is
    /// UNSCALED (a card built while the sim sits at 2× must not play at 2×), and Reduced Motion gets
    /// the RESULT, not the ritual — final numbers, no stagger, no count-up, identical information.
    /// </summary>
    public sealed class IdleClaimModal : MonoBehaviour
    {
        /// <summary>How long the numbers roll. Long enough to read as counting, short enough that
        /// the Collect tap is never waiting on it — and the card is fully readable throughout, since
        /// a player who taps early collects exactly what the preview promised.</summary>
        private const float CountUpMs = 900f;

        /// <summary>Tile entrance, and the gap between one tile and the next. The stagger is what
        /// turns three numbers into a sequence of small payoffs instead of one block appearing.</summary>
        private const float TileInMs = 160f;
        private const float TileStaggerMs = 90f;

        /// <summary>Tiles grow the last 8% into place — deeper than the panel's own 3.5% entrance
        /// (<see cref="UiMotion.PanelInMs"/>) because a tile is small and lands INSIDE an already-
        /// visible card, where a shallow scale reads as nothing at all.</summary>
        private const float TileInScale = 0.92f;

        /// <summary>One payoff tile: its own fade/scale entrance and its own rolling number.</summary>
        private sealed class Tile
        {
            public Text Value = null!;
            public CanvasGroup Group = null!;
            public RectTransform Rt = null!;
            public long Amount;
            public float DelayMs;
        }

        private readonly List<Tile> _tiles = new();
        private float _elapsedMs;
        private bool _animating;
        private long _now;         // the boot timestamp the card previewed against; Collect applies at it
        private CombatView? _view; // set while shown, so OnDestroy releases the HUD gate

        public void Show(CombatView view, IdleReport idle, List<GoalClaim> claims, long now)
        {
            _view = view;
            _now = now;
            view.PushLaunchModal(); // hide the IMGUI HUD (HP-bar dashes) that draws above uGUI

            bool hasIdle = !idle.IsEmpty; // daily may be claimable with no offline gap (idle-empty card)

            // a11y (10.20a): chrome metrics ride the text scale or the labels behead — uGUI Text
            // CLIPS to its rect, so a row sized for 100% hides its own number at 130%. Every band
            // below is pinned with PanelKit.Fixed (min == preferred) so a height-starved panel
            // crushes the Flex slack instead of a label, and the panel is sized from THE SAME
            // numbers — a card measured by guesswork is how the title got beheaded the first time.
            float ts = Settings.TextScale;
            float titleH = 36f * ts, capH = 20f * ts, tileH = 88f * ts;
            float claimH = 26f * ts, btnH = Theme.BtnH * ts;

            int kids = 2;                 // title + the Flex that pushes Collect down
            float content = titleH + btnH;
            kids++;                       // the Collect row
            if (hasIdle) { content += capH + tileH; kids += 2; }
            if (claims.Count > 0)
            {
                if (hasIdle) { content += capH; kids++; }        // the "Bonus" separator
                content += claims.Count * claimH;
                kids += claims.Count;
            }
            float height = Theme.PadL * 2f + content + Theme.Gap * (kids - 1) + 8f;

            PanelKit.Modal(transform, "IdleClaimCanvas", 90, new Vector2(440f * ts, height),
                           out var body, backdrop: Theme.BackdropDim);

            // Title leans on whichever half is present: the offline-return moment keeps its name; a
            // rare idle-empty arrival (clock crossed midnight with no accrual) reads as the daily.
            var title = PanelKit.Label(body, hasIdle ? Loc.T("arrival.idle-title") : Loc.T("arrival.daily-title"),
                                       Theme.FsTitle, Theme.TextBright, TextAnchor.MiddleCenter);
            PanelKit.Fixed(title.gameObject, height: titleH);

            if (hasIdle)
            {
                var ts2 = System.TimeSpan.FromMilliseconds(idle.ElapsedMs);
                string away = ts2.TotalHours >= 1 ? Loc.F("arrival.hours", (int)ts2.TotalHours, ts2.Minutes)
                                                  : Loc.F("arrival.minutes", ts2.Minutes, ts2.Seconds);
                string capped = idle.Capped ? Loc.T("arrival.capped") : "";
                // Centred and dim under the title: it's the CONTEXT for the numbers, not a number itself.
                var cap = PanelKit.Label(body, Loc.F("arrival.time", away, capped), Theme.FsLabel, Theme.TextDim,
                                         TextAnchor.MiddleCenter);
                PanelKit.Fixed(cap.gameObject, height: capH);

                var row = PanelKit.Row(body, tileH);
                AddTile(row, idle.Gold, Loc.T("arrival.gold-label"), Theme.AccentGold, 0);
                AddTile(row, idle.Xp, Loc.T("arrival.xp-label"), Theme.Info, 1);
                AddTile(row, idle.Items.Count, Loc.T("arrival.items-label"), Theme.TextBright, 2);
                _animating = true;
            }

            // Bonus rows: one clearly-separated line per pending goal claim (daily login etc.). Static —
            // the gem number is the exact grant from the claim's own preview (Goals.Claimables), so it
            // can't diverge from what Collect banks. A muted "Bonus" header separates them from the
            // tiles when both halves are present.
            if (claims.Count > 0)
            {
                if (hasIdle)
                {
                    var hdr = PanelKit.Label(body, Loc.T("arrival.bonus"), Theme.FsLabel, Theme.TextDim, TextAnchor.MiddleCenter);
                    PanelKit.Fixed(hdr.gameObject, height: capH);
                }
                foreach (var c in claims)
                {
                    var line = PanelKit.Label(body, Loc.F("arrival.claim-line", c.Label, Num.CompactFloor(c.Gems)),
                                              Theme.FsH2, Theme.AccentGold, TextAnchor.MiddleCenter);
                    PanelKit.Fixed(line.gameObject, height: claimH);
                }
            }

            PanelKit.Flex(body); // slack pushes Collect to the panel bottom

            var btnRow = PanelKit.Row(body, btnH);
            PanelKit.FlexSpacer(btnRow);
            // Collect applies the WHOLE arrival (idle + every goal claim) in one atomic Session.Arrive.
            PanelKit.ButtonCell(btnRow, Loc.T("arrival.collect"), () => { _view!.ArriveClaim(_now); Destroy(gameObject); },
                                width: 240f * ts, fontSize: Theme.FsH1);
            PanelKit.FlexSpacer(btnRow);

            if (Settings.ReducedMotion) Settle();
        }

        /// <summary>
        /// One tile: a rounded card carrying the number over its name. The number is the loud element
        /// (<see cref="Theme.FsOutcomeTitle"/>) and the name is a quiet caption under it — the reverse
        /// of the old "Gold:  1.2K" line, where the label read first and the payoff read second.
        /// </summary>
        private void AddTile(RectTransform row, long amount, string label, Color valueColor, int index)
        {
            var go = new GameObject("PayoffTile", typeof(RectTransform));
            go.transform.SetParent(row, false);

            var bg = go.AddComponent<Image>();
            bg.color = Theme.BgHudCard;
            UiKit.Round(bg, Theme.RadiusPanel);
            bg.raycastTarget = false;

            // Equal thirds: flexible width with no preferred width, so the three tiles split the row
            // whatever the panel ends up being (WindowSizer may clamp it on a narrow phone).
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 60f;

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = 0f;

            var rt = (RectTransform)go.transform;
            var value = PanelKit.Label(rt, "", Theme.FsOutcomeTitle, valueColor, TextAnchor.MiddleCenter);
            PanelKit.Label(rt, label, Theme.FsSmall, Theme.TextDim, TextAnchor.MiddleCenter);

            var tile = new Tile
            {
                Value = value,
                Group = go.AddComponent<CanvasGroup>(),
                Rt = rt,
                Amount = amount,
                DelayMs = index * TileStaggerMs,
            };
            // Start hidden and slightly small; Update lands it. Reduced Motion settles immediately
            // (Show calls Settle after the build), so this state is never seen there.
            tile.Group.alpha = 0f;
            rt.localScale = Vector3.one * TileInScale;
            RenderValue(tile, 0f);
            _tiles.Add(tile);
        }

        private void Update()
        {
            if (!_animating) return;
            // UNSCALED: the arrival card can be built while the sim is hit-stopped or at 2× (alt
            // modes pin timeScale off the mode kind), and the card's beat must not ride that.
            _elapsedMs += Time.unscaledDeltaTime * 1000f;

            bool done = true;
            foreach (var tile in _tiles)
            {
                float local = _elapsedMs - tile.DelayMs;

                float inT = Mathf.Clamp01(local / TileInMs);
                float e = UiMotion.EaseOut(inT);
                tile.Group.alpha = e;
                // Scale, not position: the tiles are layout children, and a layout group overwrites
                // anchoredPosition every frame — it leaves localScale alone (the PressSquash rule).
                tile.Rt.localScale = Vector3.one * Mathf.Lerp(TileInScale, 1f, e);

                // The number starts rolling as its own tile lands, so each tile is a complete beat.
                float countT = Mathf.Clamp01((local - TileInMs) / CountUpMs);
                RenderValue(tile, UiMotion.EaseOut(countT));
                if (inT < 1f || countT < 1f) done = false;
            }
            if (done) _animating = false;
        }

        /// <summary>Granted amounts FLOOR (design §7): a rolling number must never advertise more
        /// than is banked, including at the instant it lands.</summary>
        private static void RenderValue(Tile tile, float p) =>
            tile.Value.text = Num.CompactFloor((long)(tile.Amount * p));

        /// <summary>Jump straight to the final state — the Reduced Motion path, and the guarantee
        /// that the card is never left mid-tween.</summary>
        private void Settle()
        {
            _animating = false;
            foreach (var tile in _tiles)
            {
                tile.Group.alpha = 1f;
                tile.Rt.localScale = Vector3.one;
                RenderValue(tile, 1f);
            }
        }

        private void OnDestroy() => _view?.PopLaunchModal();
    }
}
