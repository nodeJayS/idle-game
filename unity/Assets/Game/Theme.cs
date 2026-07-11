#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// The single source for UI constants (colors, font sizes, spacing). Screens read
    /// these instead of inlining literals so a restyle happens in one place. Values were
    /// consolidated from the hand-placed screens — this is a pixel-faithful extraction,
    /// not a new look. Rarity/verdict colors stay in <see cref="Palette"/>/<see cref="UpgradeTell"/>
    /// because they're data-driven, not theme chrome.
    /// </summary>
    public static class Theme
    {
        // ---- Colors: surfaces ----
        /// <summary>Full-screen opaque backdrop behind a management view.</summary>
        public static readonly Color Backdrop = new(0.06f, 0.06f, 0.09f, 1f);
        /// <summary>The window/panel body.</summary>
        public static readonly Color BgPanel = new(0.10f, 0.10f, 0.14f, 1f);
        /// <summary>An inset box / detail pane inside a panel.</summary>
        public static readonly Color BgInset = new(0.07f, 0.07f, 0.10f, 1f);

        // ---- Colors: buttons ----
        public static readonly Color BtnPrimary = new(0.22f, 0.30f, 0.45f);
        public static readonly Color BtnSelected = new(0.30f, 0.45f, 0.65f);
        public static readonly Color BtnDisabled = new(0.22f, 0.24f, 0.28f);
        /// <summary>Disabled action buttons in the item detail pane (a hair warmer than <see cref="BtnDisabled"/>).</summary>
        public static readonly Color BtnDisabledDark = new(0.24f, 0.24f, 0.28f);
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
        public static readonly Color RowMuted = new(0.18f, 0.18f, 0.23f);
        /// <summary>An empty slot row — darker still than muted.</summary>
        public static readonly Color RowEmpty = new(0.12f, 0.13f, 0.16f);

        // ---- Colors: text ----
        public static readonly Color TextBright = new(0.85f, 0.9f, 1f);
        /// <summary>Ordinary body copy — a hair brighter than <see cref="TextMuted"/>.</summary>
        public static readonly Color TextBody = new(0.78f, 0.80f, 0.85f);
        public static readonly Color TextMuted = new(0.7f, 0.74f, 0.8f);
        public static readonly Color TextDim = new(0.6f, 0.62f, 0.68f);
        /// <summary>A slightly warmer dim, used for right-aligned captions/notes.</summary>
        public static readonly Color TextDim2 = new(0.62f, 0.66f, 0.72f);
        /// <summary>Greyed text on a locked/disabled row (darker than dim — reads inert).</summary>
        public static readonly Color TextDisabled = new(0.55f, 0.57f, 0.62f);

        // ---- Colors: accents / semantic ----
        public static readonly Color AccentGold = new(1f, 0.85f, 0.4f);
        /// <summary>The stat-sheet derived-row accent (very slightly warmer than <see cref="AccentGold"/>).</summary>
        public static readonly Color AccentGoldWarm = new(1f, 0.86f, 0.45f);
        public static readonly Color Good = new(0.45f, 0.9f, 0.5f);
        public static readonly Color Bad = new(0.95f, 0.45f, 0.45f);
        /// <summary>A warm reddish tone for "locked / can't yet" hints.</summary>
        public static readonly Color Warn = new(0.95f, 0.62f, 0.55f);
        /// <summary>A muted gold — an auto-derived (not explicitly picked) leader.</summary>
        public static readonly Color LeaderAuto = new(0.75f, 0.68f, 0.42f);
        /// <summary>Info blue — the "Equipped" status line.</summary>
        public static readonly Color Info = new(0.6f, 0.85f, 1f);
        /// <summary>Brighter <see cref="Good"/>/<see cref="Bad"/> pair for headline delta rows.</summary>
        public static readonly Color GoodBright = new(0.55f, 1f, 0.6f);
        public static readonly Color BadBright = new(1f, 0.5f, 0.5f);
        /// <summary>Active-skill section blue.</summary>
        public static readonly Color ActiveSkill = new(0.55f, 0.7f, 0.95f);
        /// <summary>Row-background tint behind an unlocked active skill (the ones that cast).</summary>
        public static readonly Color ActiveRowTint = new(0.18f, 0.28f, 0.42f, 0.55f);
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
        /// <summary>The "Off" state of a header toggle (auto-equip / auto-salvage).</summary>
        public static readonly Color ToggleOff = new(0.7f, 0.72f, 0.78f);
        /// <summary>Warm gold padlock — locked-item tags, tile badges and button text.</summary>
        public static readonly Color LockGold = new(1f, 0.82f, 0.35f);

        // ---- Colors: dropdown ----
        /// <summary>Backdrop behind expanded dropdown options (darker than <see cref="Backdrop"/>).</summary>
        public static readonly Color BgDropdown = new(0.05f, 0.05f, 0.08f, 1f);
        /// <summary>The currently-selected dropdown option row.</summary>
        public static readonly Color DropdownSelected = new(0.24f, 0.34f, 0.5f);

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
        public const float RowH = 32f;
        public const float RowHs = 30f;
        public const float BtnH = 48f;
        public const float BtnHs = 44f;
        public const float CloseW = 140f;
    }
}
