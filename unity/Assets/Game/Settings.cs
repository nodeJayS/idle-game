#nullable enable
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Client-side display/accessibility preferences (PlayerPrefs, not the gameplay
    /// save). Each visual effect is an independent toggle, graphics-settings style.
    /// All default on.
    /// </summary>
    public static class Settings
    {
        /// <summary>Party tactic preference (gameplay, not a visual): true = group focus-fire,
        /// false = solo. Off by default. Persisted so it carries across runs.</summary>
        public static bool GroupMovement
        {
            get => PlayerPrefs.GetInt("tacticGroup", 0) != 0;
            set { PlayerPrefs.SetInt("tacticGroup", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool DamageNumbers   { get => Get("fxDamage");      set => Set("fxDamage", value); }
        public static bool ScreenShake     { get => Get("fxShake");       set => Set("fxShake", value); }
        public static bool LootFeed        { get => Get("fxToasts");      set => Set("fxToasts", value); }
        public static bool Projectiles     { get => Get("fxProjectiles"); set => Set("fxProjectiles", value); }
        public static bool SpawnAnimations { get => Get("fxSpawnAnim");   set => Set("fxSpawnAnim", value); }

        // Chat window layout — remembered across runs. X/Y are the window's top-left anchored
        // position; W/H its size. Locked freezes drag + resize; Collapsed = minimized to the bar.
        public static float ChatX        { get => PlayerPrefs.GetFloat("chatX", 12f);  set => SetF("chatX", value); }
        public static float ChatY        { get => PlayerPrefs.GetFloat("chatY", 300f); set => SetF("chatY", value); }
        public static float ChatW        { get => PlayerPrefs.GetFloat("chatW", 250f); set => SetF("chatW", value); }
        public static float ChatH        { get => PlayerPrefs.GetFloat("chatH", 190f); set => SetF("chatH", value); }
        public static bool  ChatLocked   { get => PlayerPrefs.GetInt("chatLock", 0) != 0;      set => Set("chatLock", value); }
        public static bool  ChatCollapsed { get => PlayerPrefs.GetInt("chatCollapsed", 0) != 0; set => Set("chatCollapsed", value); }

        /// <summary>Auto-salvage threshold: drops at or below this rarity convert to scrap on
        /// pickup instead of taking a bag slot. null = off (default — never auto-discards).
        /// Stored as an int: -1 = off, otherwise (int)Rarity.</summary>
        public static Rarity? AutoSalvageMax
        {
            get { int v = PlayerPrefs.GetInt("autoSalvage", -1); return v < 0 ? (Rarity?)null : (Rarity)v; }
            set { PlayerPrefs.SetInt("autoSalvage", value == null ? -1 : (int)value.Value); PlayerPrefs.Save(); }
        }

        private static bool Get(string key) => PlayerPrefs.GetInt(key, 1) != 0;
        private static void Set(string key, bool v) { PlayerPrefs.SetInt(key, v ? 1 : 0); PlayerPrefs.Save(); }
        private static void SetF(string key, float v) { PlayerPrefs.SetFloat(key, v); PlayerPrefs.Save(); }
    }
}
