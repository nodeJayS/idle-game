#nullable enable
using System;
using System.Globalization;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Number formatting for display — pure, so the Unity client and a future server UI
    /// format identically. Compact form: 1.2K / 3.4M / 5.6B / 7.8T … (design §7,
    /// "number formatting from day one").
    /// </summary>
    public static class Num
    {
        private static readonly string[] Suffixes = { "", "K", "M", "B", "T", "Qa", "Qi" };

        public static string Compact(long value, int decimals = 1) => Compact((double)value, decimals);

        /// <summary>
        /// Compact magnitude string. Values below 1000 render plainly (rounded to an
        /// integer); larger values use a 1000-step suffix with up to <paramref name="decimals"/>
        /// decimals, trailing zeros trimmed (1000 → "1K", 1500 → "1.5K", 999999 → "1M").
        /// </summary>
        public static string Compact(double value, int decimals = 1)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return value.ToString(CultureInfo.InvariantCulture);
            if (value < 0) return "-" + Compact(-value, decimals);

            if (value < 1000)
            {
                long r = (long)Math.Round(value, MidpointRounding.AwayFromZero);
                if (r < 1000) return r.ToString(CultureInfo.InvariantCulture);
                value = r; // rounded up into the next tier (e.g. 999.6 -> 1000 -> "1K")
            }

            int tier = (int)Math.Floor(Math.Log(value, 1000));
            if (tier >= Suffixes.Length) tier = Suffixes.Length - 1;

            double scaled = Math.Round(value / Math.Pow(1000, tier), decimals, MidpointRounding.AwayFromZero);
            if (scaled >= 1000 && tier < Suffixes.Length - 1) // rounding pushed us up a tier
            {
                tier++;
                scaled = Math.Round(value / Math.Pow(1000, tier), decimals, MidpointRounding.AwayFromZero);
            }

            string num = scaled.ToString("F" + decimals, CultureInfo.InvariantCulture);
            if (num.IndexOf('.') >= 0) num = num.TrimEnd('0').TrimEnd('.');
            return num + Suffixes[tier];
        }
    }
}
