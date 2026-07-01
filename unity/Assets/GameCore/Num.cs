#nullable enable
using System;
using System.Globalization;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Number formatting for display — pure, so the Unity client and a future server UI
    /// format identically. Compact form: 1.2K / 3.4M / 5.6B / 7.8T … (design §7,
    /// "number formatting from day one").
    ///
    /// Rounding DIRECTION is a correctness rule (design §7), not a style choice — a display that
    /// rounds the wrong way lies to the player ("says I can afford it but can't"). Use:
    ///   • <see cref="CompactFloor"/> for a resource the player HAS and for count-UP timers — never
    ///     show more than they truly have / more elapsed than truly elapsed.
    ///   • <see cref="CompactCeil"/> for the COST of something and for count-DOWN timers — never show
    ///     less than what will be charged / hit 0 before the time is actually gone.
    ///   • <see cref="Compact"/> (round) only for neutral/illustrative numbers (stat sheets, %) that the
    ///     player never acts on against a threshold.
    /// </summary>
    public static class Num
    {
        private static readonly string[] Suffixes = { "", "K", "M", "B", "T", "Qa", "Qi" };

        private enum Rnd { Round, Floor, Ceil }

        // --- public API ---

        /// <summary>Compact magnitude, ROUNDED — for neutral values only (see class remarks).</summary>
        public static string Compact(long value, int decimals = 1) => Compact((double)value, decimals, Rnd.Round);

        /// <summary>Compact magnitude, rounded DOWN — for owned resources/currency and count-up timers,
        /// so the display never overstates what the player actually has.</summary>
        public static string CompactFloor(long value, int decimals = 1) => Compact((double)value, decimals, Rnd.Floor);

        /// <summary>Compact magnitude, rounded UP — for costs and count-down timers, so the display never
        /// understates what will be charged (or hits 0 before the time is truly gone).</summary>
        public static string CompactCeil(long value, int decimals = 1) => Compact((double)value, decimals, Rnd.Ceil);

        /// <summary>Compact magnitude, ROUNDED — the double overload for neutral values.</summary>
        public static string Compact(double value, int decimals = 1) => Compact(value, decimals, Rnd.Round);

        // --- core ---

        /// <summary>
        /// Compact magnitude string with an explicit rounding direction. Values below 1000 render
        /// plainly (as an integer, rounded per <paramref name="mode"/>); larger values use a 1000-step
        /// suffix with up to <paramref name="decimals"/> decimals, trailing zeros trimmed.
        /// </summary>
        private static string Compact(double value, int decimals, Rnd mode)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return value.ToString(CultureInfo.InvariantCulture);
            if (value < 0) return "-" + Compact(-value, decimals, mode);

            if (value < 1000)
            {
                long r = RoundInt(value, mode);
                if (r < 1000) return r.ToString(CultureInfo.InvariantCulture);
                value = r; // rounded up into the next tier (e.g. 999.6 ceil -> 1000 -> "1K")
            }

            int tier = (int)Math.Floor(Math.Log(value, 1000));
            if (tier >= Suffixes.Length) tier = Suffixes.Length - 1;

            double scaled = RoundTo(value / Math.Pow(1000, tier), decimals, mode);
            if (scaled >= 1000 && tier < Suffixes.Length - 1) // rounding pushed us up a tier
            {
                tier++;
                scaled = RoundTo(value / Math.Pow(1000, tier), decimals, mode);
            }

            string num = scaled.ToString("F" + decimals, CultureInfo.InvariantCulture);
            if (num.IndexOf('.') >= 0) num = num.TrimEnd('0').TrimEnd('.');
            return num + Suffixes[tier];
        }

        // Round a value to an integer in the requested direction.
        private static long RoundInt(double v, Rnd mode) => mode switch
        {
            Rnd.Floor => (long)Math.Floor(v),
            Rnd.Ceil => (long)Math.Ceiling(v),
            _ => (long)Math.Round(v, MidpointRounding.AwayFromZero),
        };

        // Round a value to `decimals` places in the requested direction.
        private static double RoundTo(double v, int decimals, Rnd mode)
        {
            double f = Math.Pow(10, decimals);
            double s = v * f;
            double r = mode switch
            {
                Rnd.Floor => Math.Floor(s),
                Rnd.Ceil => Math.Ceiling(s),
                _ => Math.Round(s, MidpointRounding.AwayFromZero),
            };
            return r / f;
        }
    }
}
