#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The Unity host's save (de)serialization + file I/O — the one platform-specific
    /// adapter on the client (mirrors gamecore/Adapters/Persistence.cs, which is the
    /// .NET/server/test adapter). Uses Newtonsoft to (de)serialize the field-based
    /// <see cref="SaveState"/> POCOs to <see cref="Application.persistentDataPath"/>.
    /// All game rules stay in GameCore; this only moves bytes.
    /// </summary>
    public static class SaveStore
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // our models expose public fields, not properties
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver(),
            Formatting = Formatting.Indented,
        };

        /// <summary>Benchmark sandbox (10.12d tooling): `-benchmark` sessions redirect EVERY save
        /// read/write to a throwaway file so the scripted run can never touch a real player's
        /// save.json (CombatView writes on mode transitions; Autosave writes on a timer).</summary>
        public static string? FileNameOverride;

        private static string Path =>
            System.IO.Path.Combine(Application.persistentDataPath, FileNameOverride ?? "save.json");

        public static bool Exists() => File.Exists(Path);

        /// <summary>Load + migrate the save, or null if there's no file / it's unreadable.</summary>
        public static SaveState? Load()
        {
            try
            {
                if (!File.Exists(Path)) return null;
                var json = File.ReadAllText(Path);
                var save = JsonConvert.DeserializeObject<SaveState>(json, Settings);
                return IdleGame.GameCore.Save.Migrate(save); // version check + null back-fill
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveStore] Load failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Write the save atomically (temp file then replace) so a crash mid-write can't corrupt it.</summary>
        public static void Save(SaveState save)
        {
            try
            {
                var json = JsonConvert.SerializeObject(save, Settings);
                var tmp = Path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(Path)) File.Replace(tmp, Path, null);
                else File.Move(tmp, Path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveStore] Save failed: {e.Message}");
            }
        }

        public static void Delete()
        {
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception e) { Debug.LogError($"[SaveStore] Delete failed: {e.Message}"); }
        }
    }
}
