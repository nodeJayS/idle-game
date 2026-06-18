#nullable enable
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>Shared client-side colors (rarity tints, etc.) reused across UI + juice.</summary>
    public static class Palette
    {
        public static Color Rarity(Rarity r) => r switch
        {
            IdleGame.GameCore.Rarity.Normal    => new Color(0.85f, 0.85f, 0.85f),
            IdleGame.GameCore.Rarity.Magic     => new Color(0.40f, 0.65f, 1.00f),
            IdleGame.GameCore.Rarity.Rare      => new Color(1.00f, 0.85f, 0.25f),
            IdleGame.GameCore.Rarity.Unique    => new Color(0.95f, 0.55f, 0.15f),
            IdleGame.GameCore.Rarity.Legendary => new Color(0.80f, 0.35f, 0.95f),
            _ => Color.white,
        };
    }
}
