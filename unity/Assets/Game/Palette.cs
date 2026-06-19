#nullable enable
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>Shared client-side colors (rarity tints, etc.) reused across UI + juice.</summary>
    public static class Palette
    {
        // Rarity ramp: Normal gray (borderless in the doll), Magic teal, Rare blue,
        // Unique yellow, Legendary green. Used for both text tints and equip borders.
        public static Color Rarity(Rarity r) => r switch
        {
            IdleGame.GameCore.Rarity.Normal    => new Color(0.78f, 0.80f, 0.82f),
            IdleGame.GameCore.Rarity.Magic     => new Color(0.30f, 0.80f, 0.72f),
            IdleGame.GameCore.Rarity.Rare      => new Color(0.32f, 0.55f, 1.00f),
            IdleGame.GameCore.Rarity.Unique    => new Color(1.00f, 0.84f, 0.25f),
            IdleGame.GameCore.Rarity.Legendary => new Color(0.40f, 0.90f, 0.45f),
            _ => Color.white,
        };

        /// <summary>True when this rarity should render with no visible equip border (Normal).</summary>
        public static bool Borderless(Rarity r) => r == IdleGame.GameCore.Rarity.Normal;
    }
}
