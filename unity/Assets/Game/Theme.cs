#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// The single source for UI constants (colors, font sizes, spacing). Screens read
    /// these instead of inlining literals so a restyle happens in one place. Rarity/verdict
    /// colors stay in <see cref="Palette"/>/<see cref="UpgradeTell"/> because they're
    /// data-driven, not theme chrome.
    /// <para>The values are no longer the pixel-faithful extraction they started as: slice P1
    /// re-skinned the chrome warm so the HUD and the faceted <i>Tunic</i> world read as one
    /// product instead of two (see the colour note below).</para>
    /// </summary>
    public static class Theme
    {
        // ==== Colours ================================================================
        // DIRECTION (slice P1): warm ink & parchment-text — deep warm brown-black grounds under
        // cream text and gold accents. NOT a light-mode flip: dense loot tables are read for
        // hours, so the surfaces stay dark and only their HUE moves. The old kit was slate-navy
        // (every surface had blue as its highest channel) against a world of faceted greens and
        // browns; the two read as different products.
        //
        // THE RULE for any new chrome token: RED is the highest channel, BLUE the lowest, and a
        // token's luminance/contrast against its NEIGHBOURS is what carries its meaning — pick
        // the brightness first (relative to the surface it sits on), then rotate it into the
        // family. Exceptions are deliberate and listed below: anything that encodes DATA rather
        // than chrome (good/bad, rarity, skill kinds, progress fills, the danger/enhance/reforge
        // verbs) keeps its hue, because the player reads meaning off those hues.

        // ---- Colors: surfaces ----
        /// <summary>Full-screen opaque backdrop behind a management view.</summary>
        public static readonly Color Backdrop = new(0.075f, 0.062f, 0.052f, 1f);
        /// <summary>The window/panel body.</summary>
        public static readonly Color BgPanel = new(0.132f, 0.110f, 0.090f, 1f);
        /// <summary>An inset box / detail pane inside a panel.</summary>
        public static readonly Color BgInset = new(0.093f, 0.077f, 0.063f, 1f);
        /// <summary>The floating in-world HUD panels (chat, quest board) — a touch under
        /// <see cref="BgPanel"/> and slightly translucent, so the world reads through without the
        /// text losing its ground. A token rather than three copied literals: these are the only
        /// surfaces that sit ON the world instead of on a backdrop, and they drifted cold when the
        /// kit went warm (P1) precisely because each view inlined its own colour.</summary>
        public static readonly Color BgHudPanel = new(0.105f, 0.088f, 0.072f, 0.92f);
        /// <summary>A hovering tooltip/preview card over the world — darker and near-opaque so
        /// dense affix text stays readable against arbitrary terrain.</summary>
        public static readonly Color BgHudCard = new(0.082f, 0.068f, 0.056f, 0.99f);
        /// <summary>The draggable title bar on a floating HUD panel — a step ABOVE
        /// <see cref="BgHudPanel"/> so the grab handle reads as raised. The locked variant
        /// (<see cref="BgHudHeaderLocked"/>) was always warm; this one was the cold half of the pair.</summary>
        public static readonly Color BgHudHeader = new(0.225f, 0.188f, 0.148f, 0.96f);
        /// <summary>The same bar while the panel is LOCKED — a desaturated red-brown, so "pinned"
        /// reads at a glance without a label.</summary>
        public static readonly Color BgHudHeaderLocked = new(0.20f, 0.17f, 0.17f, 0.96f);

        // ---- Colors: buttons ----
        public static readonly Color BtnPrimary = new(0.355f, 0.278f, 0.185f);
        public static readonly Color BtnSelected = new(0.520f, 0.400f, 0.240f);
        public static readonly Color BtnDisabled = new(0.225f, 0.205f, 0.180f);
        /// <summary>Disabled action buttons in the item detail pane (a hair warmer than <see cref="BtnDisabled"/>).</summary>
        public static readonly Color BtnDisabledDark = new(0.245f, 0.220f, 0.190f);
        /// <summary>The Enhance verb — green like the stat gain it buys.</summary>
        public static readonly Color BtnEnhance = new(0.26f, 0.42f, 0.32f);
        /// <summary>The Reforge gamble verb — violet like the affix reroll.</summary>
        public static readonly Color BtnReforge = new(0.34f, 0.30f, 0.46f);
        /// <summary>Lock toggle when the item IS locked (warm gold ground under <see cref="LockGold"/> text).</summary>
        public static readonly Color BtnLockOn = new(0.46f, 0.38f, 0.16f);
        /// <summary>Lock toggle when the item is unlocked — deliberately neutral.</summary>
        public static readonly Color BtnLockOff = new(0.26f, 0.26f, 0.30f);
        /// <summary>A destructive verb at rest (Salvage all) — dark red, reads dangerous but calm.</summary>
        public static readonly Color BtnDanger = new(0.42f, 0.26f, 0.26f);
        /// <summary>An ARMED destructive confirm (second click executes) — hot red.</summary>
        public static readonly Color BtnDangerArmed = new(0.62f, 0.22f, 0.22f);

        // ---- Colors: list rows ----
        /// <summary>A muted row (e.g. a benched hero) — reads as present but de-emphasized.</summary>
        public static readonly Color RowMuted = new(0.205f, 0.172f, 0.138f);
        /// <summary>An empty slot row — darker still than muted.</summary>
        public static readonly Color RowEmpty = new(0.148f, 0.124f, 0.101f);

        // ---- Colors: text ----
        public static readonly Color TextBright = new(0.972f, 0.945f, 0.870f);
        /// <summary>Ordinary body copy — a hair brighter than <see cref="TextMuted"/>.</summary>
        public static readonly Color TextBody = new(0.885f, 0.845f, 0.755f);
        public static readonly Color TextMuted = new(0.775f, 0.725f, 0.635f);
        public static readonly Color TextDim = new(0.645f, 0.598f, 0.518f);
        /// <summary>A hair brighter than <see cref="TextDim"/>, used for right-aligned captions/notes.</summary>
        public static readonly Color TextDim2 = new(0.672f, 0.622f, 0.538f);
        /// <summary>Greyed text on a locked/disabled row (darker than dim — reads inert).</summary>
        public static readonly Color TextDisabled = new(0.545f, 0.505f, 0.440f);

        // ---- Colors: accents / semantic ----
        public static readonly Color AccentGold = new(1f, 0.85f, 0.4f);
        /// <summary>The stat-sheet derived-row accent (very slightly warmer than <see cref="AccentGold"/>).</summary>
        public static readonly Color AccentGoldWarm = new(1f, 0.86f, 0.45f);
        public static readonly Color Good = new(0.45f, 0.9f, 0.5f);
        public static readonly Color Bad = new(0.95f, 0.45f, 0.45f);
        /// <summary>A warm reddish tone for "locked / can't yet" hints.</summary>
        public static readonly Color Warn = new(0.95f, 0.62f, 0.55f);
        /// <summary>A muted gold — an auto-derived (not explicitly picked) leader. Already sat in
        /// the warm family (it's <see cref="AccentGold"/> knocked down), so P1 left it alone.</summary>
        public static readonly Color LeaderAuto = new(0.75f, 0.68f, 0.42f);
        /// <summary>Info blue — the "Equipped" status line.</summary>
        public static readonly Color Info = new(0.6f, 0.85f, 1f);
        /// <summary>Brighter <see cref="Good"/>/<see cref="Bad"/> pair for headline delta rows.</summary>
        public static readonly Color GoodBright = new(0.55f, 1f, 0.6f);
        public static readonly Color BadBright = new(1f, 0.5f, 0.5f);
        /// <summary>Active-skill section blue.</summary>
        public static readonly Color ActiveSkill = new(0.55f, 0.7f, 0.95f);
        /// <summary>Row-background tint behind an unlocked active skill (the ones that cast) — a
        /// lit ground in the button family, NOT a second copy of <see cref="ActiveSkill"/>: the blue
        /// skill name reads harder against warm than it did against its own hue.</summary>
        public static readonly Color ActiveRowTint = new(0.365f, 0.280f, 0.180f, 0.55f);
        /// <summary>Passive-skill section green.</summary>
        public static readonly Color PassiveSkill = new(0.6f, 0.85f, 0.65f);
        /// <summary>An invested passive's name — pale green, one step up from <see cref="TextBody"/>.</summary>
        public static readonly Color PassiveBright = new(0.82f, 1f, 0.86f);
        /// <summary>A passive's effect sub-line — green-tinted muted.</summary>
        public static readonly Color PassiveDim = new(0.66f, 0.78f, 0.70f);
        /// <summary>The red "MASTERY" badge on a max-ranked skill.</summary>
        public static readonly Color Mastery = new(0.95f, 0.35f, 0.3f);
        /// <summary>Imprinted-affix violet.</summary>
        public static readonly Color Imprint = new(0.85f, 0.6f, 1f);
        /// <summary>Bag count when loose items exceed the cap (idle/boss spillover keeps dropping).</summary>
        public static readonly Color Overfull = new(1f, 0.6f, 0.4f);
        /// <summary>The "Off" state of a header toggle (auto-equip / auto-salvage) — a notch under
        /// <see cref="TextMuted"/> so "off" still reads de-emphasized next to body copy.</summary>
        public static readonly Color ToggleOff = new(0.745f, 0.705f, 0.625f);
        /// <summary>Warm gold padlock — locked-item tags, tile badges and button text.</summary>
        public static readonly Color LockGold = new(1f, 0.82f, 0.35f);

        // ---- Colors: progress bars ----
        /// <summary>Progress-bar track (the dark groove under a fill).</summary>
        public static readonly Color BarTrack = new(0.175f, 0.148f, 0.120f);
        /// <summary>Progress-bar fill — the standard green (goal boards, completed ladders).</summary>
        public static readonly Color BarGreen = new(0.40f, 0.72f, 0.46f);
        /// <summary>Progress-bar fill — gold, a lifetime ladder still in progress.</summary>
        public static readonly Color BarGold = new(0.85f, 0.70f, 0.35f);

        // ---- Colors: dropdown ----
        /// <summary>Backdrop behind expanded dropdown options (darker than <see cref="Backdrop"/>).</summary>
        public static readonly Color BgDropdown = new(0.058f, 0.048f, 0.040f, 1f);
        /// <summary>The currently-selected dropdown option row — one step over <see cref="BtnPrimary"/>,
        /// well short of <see cref="BtnSelected"/> (a list cursor, not a committed choice).</summary>
        public static readonly Color DropdownSelected = new(0.390f, 0.305f, 0.200f);

        // ---- Font sizes ----
        public const int FsTitle = 28;
        public const int FsH1 = 22;
        public const int FsH2 = 18;
        public const int FsSubTab = 17;
        public const int FsBody = 15;
        public const int FsLabel = 14;
        public const int FsSmall = 13;
        public const int FsTiny = 12;
        public const int FsMicro = 10;

        // ---- Metrics ----
        public const float PadL = 16f;
        public const float Gap = 12f;
        public const float GapS = 8f;
        public const float GapXs = 4f;
        // 44 = the touch floor for INTERACTIVE rows (10.13c; ROADMAP 44pt rule) — ListRow and any
        // Row that carries a Button reads its height from RowH. RowHs is the COMPACT tier for
        // secondary controls and non-button rows; anything Button-bearing built on RowHs must pin
        // its own >= TouchMin height (40 alone is under the floor).
        public const float RowH = 44f;
        public const float RowHs = 40f;
        /// <summary>The touch-target floor (10.13c; ROADMAP 44pt rule): an interactive control must
        /// never size under this on either axis. Rows built on the compact <see cref="RowHs"/> that
        /// carry a Button pin THIS as their height where no other Theme constant (BtnH/BtnHs) fits.</summary>
        public const float TouchMin = 44f;
        public const float BtnH = 48f;
        public const float BtnHs = 44f;
        public const float CloseW = 140f;

        // ---- Corner radii ----
        // Feed these to UiKit.RoundedRect/SoftShadow, which bakes one 9-sliced sprite per radius.
        // The ladder is by ELEVATION, not by size: the further a surface sits from the world, the
        // rounder it gets, so a window reads as a soft object sitting on top of the diorama while
        // the dense cells inside stay crisp enough to line up in a grid.
        public const int RadiusWindow = 14;
        public const int RadiusPanel = 10;
        public const int RadiusButton = 8;
        public const int RadiusTile = 6;

        // ==== HUD ====================================================================
        // Shared HUD geometry: the ambient in-world chrome (IMGUI control bar / party chips,
        // TopBar) keys its screen-edge margins and the control-bar band off these so corner
        // regions can be reserved consistently instead of each draw copying a literal.

        /// <summary>Screen-edge safe margin for HUD elements.</summary>
        public const float HudPad = 16f;
        /// <summary>Gap between adjacent HUD controls (control-bar buttons).</summary>
        public const float HudGap = 12f;
        /// <summary>Control-bar button height — the bottom band the bar occupies.</summary>
        public const float HudBarH = 80f;

        // ==== modals =================================================================
        // Extracted from the three hand-placed modals (idle claim, outcome card, gacha) when they
        // migrated onto PanelKit; their GROUNDS were rotated warm with everything else in P1, while
        // the win-green / gacha-gold accents stayed put because they're signals, not chrome.

        /// <summary>Click-blocking dim behind the idle-claim modal.</summary>
        public static readonly Color BackdropDim = new(0f, 0f, 0f, 0.6f);

        // Outcome card (was CombatView.DrawOutcome, IMGUI). Win = bordered green panel; the
        // loss banner is bare white text over the world with its own summary tint.
        public static readonly Color OutcomeWinBorder = new(0.40f, 0.70f, 0.45f, 0.95f);
        /// <summary>Win-card ground. Warm like every other surface, but held a touch greener than
        /// <see cref="BgPanel"/> so the green border/title still read as one object with it.</summary>
        public static readonly Color OutcomeWinBg = new(0.130f, 0.128f, 0.086f, 0.98f);
        public static readonly Color OutcomeWinTitle = new(0.6f, 0.95f, 0.6f);
        public static readonly Color OutcomeWinSub = new(0.8f, 0.85f, 0.8f);
        public static readonly Color OutcomeLossSummary = new(0.85f, 0.8f, 0.75f);
        public const int FsOutcomeTitle = 30;
        public const int FsOutcomeBanner = 34;

        // Gacha (Summon). Panel keeps a hair of translucency (0.98) so the game ghosts through
        // — there is NO backdrop, and that faint show-through is the intended read.
        public static readonly Color GachaPanelBg = new(0.118f, 0.098f, 0.080f, 0.98f);
        public static readonly Color GachaGold = new(1f, 0.82f, 0.32f);       // Summon title + featured reveal accent
        public static readonly Color GachaBannerName = new(1f, 0.88f, 0.55f);
        /// <summary>An affordable Roll — warm gold (disabled falls back to <see cref="BtnDisabled"/>).
        /// Pushed more SATURATED than it was: on a warm ground the old value would have collapsed
        /// into <see cref="BtnPrimary"/>, and the Roll has to look like the special verb it is.</summary>
        public static readonly Color BtnGold = new(0.500f, 0.380f, 0.130f);
        public static readonly Color RevealCommon = new(0.55f, 0.62f, 0.95f); // non-featured reveal accent
        public static readonly Color RevealNewTag = new(1f, 0.92f, 0.5f);
        public static readonly Color RevealSubTag = new(0.905f, 0.865f, 0.780f); // dupe tag + settle hint (hint at 0.85 alpha)
        public const int FsRevealName = 40;

        /// <summary>Gear-set identity/bonus lines (§6.2) — teal, distinct from passive green
        /// and Info blue; inactive bonus tiers fall back to <see cref="TextDim"/>.</summary>
        public static readonly Color SetBonus = new(0.55f, 0.9f, 0.8f);
    }
}
