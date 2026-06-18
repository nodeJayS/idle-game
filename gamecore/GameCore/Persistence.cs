using System.Text.Json;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Save (de)serialization. This is the ONE adapter that is host-specific:
    /// it uses System.Text.Json for .NET (server + tests). In Unity, swap this
    /// for JsonUtility/Newtonsoft if System.Text.Json isn't available — the pure
    /// logic (Save/Party/Rng) has no dependency on it.
    /// </summary>
    public static class Persistence
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true, // our models use public fields
        };

        public static string Serialize(SaveState save) => JsonSerializer.Serialize(save, Options);

        public static SaveState Deserialize(string json)
        {
            var save = JsonSerializer.Deserialize<SaveState>(json, Options);
            return Save.Migrate(save);
        }
    }
}
