#nullable enable
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>Shared client-side colors (rarity tints, etc.) reused across UI + juice.</summary>
    public static class Palette
    {
        // Rarity ramp: Normal gray (borderless in the doll), Rare blue, Unique yellow,
        // Legendary green, Mythic red. Used for both text tints and equip borders.
        public static Color Rarity(Rarity r) => r switch
        {
            IdleGame.GameCore.Rarity.Normal    => new Color(0.78f, 0.80f, 0.82f),
            IdleGame.GameCore.Rarity.Rare      => new Color(0.32f, 0.55f, 1.00f),
            IdleGame.GameCore.Rarity.Unique    => new Color(1.00f, 0.84f, 0.25f),
            IdleGame.GameCore.Rarity.Legendary => new Color(0.40f, 0.90f, 0.45f),
            IdleGame.GameCore.Rarity.Mythic    => new Color(0.90f, 0.15f, 0.15f),
            _ => Color.white,
        };

        /// <summary>True when this rarity should render with no visible equip border (Normal).</summary>
        public static bool Borderless(Rarity r) => r == IdleGame.GameCore.Rarity.Normal;

        /// <summary>The rarity's GLYPH mark (10.20b) — the shape channel drawn beside every rarity
        /// color so tiers stay tellable without color vision. One glyph per tier, escalating in
        /// visual weight with the ramp; the set (● ■ ◆ ★) is UIFont-coverage-verified on this
        /// project's font. The set deliberately avoids ▲/▼ (the codebase-wide upgrade/delta
        /// vocabulary — UpgradeTell, compare deltas, tuning chips) and ✦ (the imprint badge), so a
        /// rarity mark can never be misread as a verdict. Normal is DELIBERATELY unmarked — it
        /// matches the borderless treatment (<see cref="Borderless"/>): baseline gear carries no
        /// signal, so a mark's absence IS the tell.</summary>
        public static string RarityMark(Rarity r) => r switch
        {
            IdleGame.GameCore.Rarity.Rare      => "●",
            IdleGame.GameCore.Rarity.Unique    => "■",
            IdleGame.GameCore.Rarity.Legendary => "◆",
            IdleGame.GameCore.Rarity.Mythic    => "★",
            _ => "",
        };
    }
}
