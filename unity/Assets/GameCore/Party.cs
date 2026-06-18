#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Party / hero acquisition. THE GACHA PLUG POINT lives here later:
    /// AcquireHero will be called by progression now and by a gacha roll later
    /// (same entry point, no downstream changes).
    /// </summary>
    public static class Party
    {
        /// <summary>
        /// Place an owned hero into one of the 4 party slots (or null to clear).
        /// Pure: returns a new SaveState; the input is not mutated.
        /// </summary>
        public static SaveState SetPartySlot(SaveState save, int slot, string? heroId)
        {
            if (slot < 0 || slot >= save.Party.Length)
                throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} out of range (0..{save.Party.Length - 1})");

            if (heroId != null && !save.Heroes.Exists(h => h.Id == heroId))
                throw new InvalidOperationException($"SetPartySlot: hero \"{heroId}\" not owned");

            var nextParty = (string?[])save.Party.Clone();
            nextParty[slot] = heroId;

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = nextParty,
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                LastClaimAt = save.LastClaimAt,
            };
        }
    }
}
