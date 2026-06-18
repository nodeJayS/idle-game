#nullable enable
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Inventory reducers — pure: each returns a NEW SaveState, input untouched
    /// (same convention as <see cref="Party"/>). Equip/compare arrive in M2.5.
    /// </summary>
    public static class Inventory
    {
        /// <summary>Append items (e.g. a run's PendingLoot) to the inventory.</summary>
        public static SaveState AddItems(SaveState save, IReadOnlyList<Item> items)
        {
            var nextInventory = new List<Item>(save.Inventory);
            nextInventory.AddRange(items);

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                Inventory = nextInventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                LastClaimAt = save.LastClaimAt,
            };
        }
    }
}
