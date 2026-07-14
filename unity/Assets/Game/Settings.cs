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

        /// <summary>Master output volume (0..1, default 1). Drives <see cref="UnityEngine.AudioListener.volume"/>
        /// — a single global attenuation over every channel. Set at boot and live on slider drag.</summary>
        public static float MasterVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat("volMaster", 1f));
            set => SetF("volMaster", Mathf.Clamp01(value));
        }

        /// <summary>SFX volume (0..1, default 1). Multiplied into every one-shot in <see cref="SoundFx.Play"/>;
        /// composes on top of <see cref="MasterVolume"/> (which is the AudioListener global). All audio is SFX
        /// today (no BGM), but the two sliders stay independent so a future music channel slots in cleanly.</summary>
        public static float SfxVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat("volSfx", 1f));
            set => SetF("volSfx", Mathf.Clamp01(value));
        }

        /// <summary>Ambience-bed volume (0..1, default 0.6) — the 10.9c zone beds, independent of
        /// one-shot SFX so either can be turned down without losing the other.</summary>
        public static float AmbienceVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat("volAmb", 0.6f));
            set => SetF("volAmb", Mathf.Clamp01(value));
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

        // Quest window layout — same convention as chat. Defaults sit it top-right (the canvas
        // is a fixed 1280 logical units wide, match-width, so X is stable across resolutions).
        public static float QuestX        { get => PlayerPrefs.GetFloat("questX", 1002f); set => SetF("questX", value); }
        public static float QuestY        { get => PlayerPrefs.GetFloat("questY", 332f);  set => SetF("questY", value); }
        public static float QuestW        { get => PlayerPrefs.GetFloat("questW", 264f);  set => SetF("questW", value); }
        public static float QuestH        { get => PlayerPrefs.GetFloat("questH", 168f);  set => SetF("questH", value); }
        public static bool  QuestLocked    { get => PlayerPrefs.GetInt("questLock", 0) != 0;      set => Set("questLock", value); }
        public static bool  QuestCollapsed { get => PlayerPrefs.GetInt("questCollapsed", 0) != 0; set => Set("questCollapsed", value); }

        /// <summary>LEGACY (10.5a): the auto-salvage threshold now lives in the save as the
        /// per-slot loot filter (<see cref="LootFilterState"/>). This pref survives ONLY as the
        /// one-time migration source — Bootstrap.Continue seeds the filter from it and clears it.
        /// Stored as an int: -1 = off, otherwise (int)Rarity.</summary>
        public static Rarity? AutoSalvageMax
        {
            get { int v = PlayerPrefs.GetInt("autoSalvage", -1); return v < 0 ? (Rarity?)null : (Rarity)v; }
            set { PlayerPrefs.SetInt("autoSalvage", value == null ? -1 : (int)value.Value); PlayerPrefs.Save(); }
        }

        /// <summary>Auto-equip-if-better (Lever 2): when a banked drop is a genuine power upgrade
        /// for a fielded hero, equip it automatically. Off by default — equipping silently changes
        /// your build, so it's opt-in. Reads through the pure <see cref="Upgrades.AutoEquipIfBetter"/>.</summary>
        public static bool AutoEquipUpgrades
        {
            get => PlayerPrefs.GetInt("autoEquip", 0) != 0;
            set { PlayerPrefs.SetInt("autoEquip", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // ---- Quality tier (10.12d): the weak-hardware levers, applied by GraphicsQuality ----
        // Render scale is THE lever (the benchmark shows the game is GPU-fill-bound on weak
        // machines, ~2ms/frame on a desktop at 720p): 1.0 = native, 0.75/0.6 trade sharpness
        // for fill rate on the URP pipeline asset. Shadows/post are the secondary cuts.
        public static float RenderScale { get => Mathf.Clamp(PlayerPrefs.GetFloat("gfxScale", 1f), 0.5f, 1f); set => SetF("gfxScale", Mathf.Clamp(value, 0.5f, 1f)); }
        public static bool Shadows      { get => Get("gfxShadows");  set => Set("gfxShadows", value); }
        public static bool PostFx       { get => Get("gfxPost");     set => Set("gfxPost", value); }

        private static bool Get(string key) => PlayerPrefs.GetInt(key, 1) != 0;
        private static void Set(string key, bool v) { PlayerPrefs.SetInt(key, v ? 1 : 0); PlayerPrefs.Save(); }
        private static void SetF(string key, float v) { PlayerPrefs.SetFloat(key, v); PlayerPrefs.Save(); }
    }
}
