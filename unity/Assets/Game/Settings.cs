#nullable enable
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Client-side display/accessibility preferences (PlayerPrefs, not the gameplay
    /// save). Each visual effect is an independent toggle, graphics-settings style.
    /// All default on.
    /// </summary>
    public static class Settings
    {
        public static bool DamageNumbers   { get => Get("fxDamage");      set => Set("fxDamage", value); }
        public static bool ScreenShake     { get => Get("fxShake");       set => Set("fxShake", value); }
        public static bool LootToasts      { get => Get("fxToasts");      set => Set("fxToasts", value); }
        public static bool Projectiles     { get => Get("fxProjectiles"); set => Set("fxProjectiles", value); }
        public static bool SpawnAnimations { get => Get("fxSpawnAnim");   set => Set("fxSpawnAnim", value); }

        private static bool Get(string key) => PlayerPrefs.GetInt(key, 1) != 0;
        private static void Set(string key, bool v) { PlayerPrefs.SetInt(key, v ? 1 : 0); PlayerPrefs.Save(); }
    }
}
