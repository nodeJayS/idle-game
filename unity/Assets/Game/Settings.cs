#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Client-side player preferences (not game state — lives in PlayerPrefs, not the
    /// save). Kept separate from GameCore: these are presentation/accessibility knobs.
    /// </summary>
    public static class Settings
    {
        private const string CombatEffectsKey = "combatEffects";

        /// <summary>
        /// Floating damage numbers, crit screen-flash, and camera shake. On by default;
        /// off for players who find the motion/flash distracting. (Loot toasts and HP
        /// bars are informational and always shown.)
        /// </summary>
        public static bool CombatEffects
        {
            get => PlayerPrefs.GetInt(CombatEffectsKey, 1) != 0;
            set { PlayerPrefs.SetInt(CombatEffectsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }
}
