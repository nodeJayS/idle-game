#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// The ONE randomness engine (mulberry32). Deterministic: same (seed, cursor)
    /// produces the same sequence, so a server can re-validate the client and loot
    /// is unit-testable. Loot uses this now; gacha (pity/rates) sits on top later.
    /// </summary>
    public sealed class Rng
    {
        private uint _state;

        /// <summary>How many values have been drawn from the seed so far.</summary>
        public int Cursor { get; private set; }

        public Rng(uint seed, int cursor = 0)
        {
            unchecked
            {
                _state = seed + (uint)(cursor * 0x6D2B79F5);
            }
            Cursor = cursor;
        }

        /// <summary>Returns a double in [0, 1) and advances the cursor.</summary>
        public double Next()
        {
            Cursor++;
            unchecked
            {
                _state += 0x6D2B79F5u;
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        /// <summary>Weighted pick from (item, weight) entries; weights must be &gt; 0.</summary>
        public T WeightedPick<T>(IReadOnlyList<(T item, double weight)> entries)
        {
            if (entries.Count == 0) throw new ArgumentException("WeightedPick: empty entries");
            double total = 0;
            for (int i = 0; i < entries.Count; i++) total += entries[i].weight;
            double roll = Next() * total;
            for (int i = 0; i < entries.Count; i++)
            {
                roll -= entries[i].weight;
                if (roll < 0) return entries[i].item;
            }
            return entries[entries.Count - 1].item; // float safety net
        }

        /// <summary>Integer in [min, max] inclusive.</summary>
        public int RandInt(int min, int max) => min + (int)(Next() * (max - min + 1));

        /// <summary>Float in [min, max).</summary>
        public double RandRange(double min, double max) => min + Next() * (max - min);
    }
}
