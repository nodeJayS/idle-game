#nullable enable
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Canonical player-facing stat presentation — display order, labels, and value
    /// formatting. One source of truth so item tooltips, the compare pane, and the
    /// character stat list all read consistently. Pure presentation (no game rules).
    /// </summary>
    public static class StatDisplay
    {
        // Grouped by role: survivability, then offense, then resource, then mobility.
        public static readonly StatKey[] Order =
        {
            StatKey.Hp, StatKey.Def, StatKey.HpRegen,
            StatKey.Atk, StatKey.AtkSpd, StatKey.CritChance, StatKey.CritDmg, StatKey.AttackRange, StatKey.SplashRadius,
            StatKey.MaxMana, StatKey.ManaRegen,
            StatKey.MoveSpd,
        };

        /// <summary>Rank of a stat in the canonical order (for sorting affix lists).</summary>
        public static int Rank(StatKey k)
        {
            for (int i = 0; i < Order.Length; i++) if (Order[i] == k) return i;
            return Order.Length;
        }

        public static string Label(StatKey k) => k switch
        {
            StatKey.Hp => "Life",
            StatKey.Def => "Defense",
            StatKey.HpRegen => "Life Regen",
            StatKey.Atk => "Attack",
            StatKey.AtkSpd => "Attack Speed",
            StatKey.CritChance => "Crit Chance",
            StatKey.CritDmg => "Crit Damage",
            StatKey.AttackRange => "Range",
            StatKey.SplashRadius => "Splash",
            StatKey.MaxMana => "Mana",
            StatKey.ManaRegen => "Mana Regen",
            StatKey.MoveSpd => "Move Speed",
            _ => k.ToString(),
        };

        /// <summary>Format an absolute value, e.g. "420", "7%", "x1.50", "2/s", "1.15".</summary>
        public static string Value(StatKey k, double v) => k switch
        {
            StatKey.CritChance => Mathf.RoundToInt((float)(v * 100)) + "%",
            StatKey.CritDmg => "x" + v.ToString("0.##"),
            StatKey.HpRegen => v.ToString("0.#") + "/s",
            StatKey.ManaRegen => v.ToString("0.#") + "/s",
            StatKey.AttackRange => v.ToString("0.#"),
            StatKey.SplashRadius => v.ToString("0.#"),
            StatKey.AtkSpd => v.ToString("0.##"),
            StatKey.MoveSpd => v.ToString("0.##"),
            _ => Mathf.RoundToInt((float)v).ToString(), // Hp, Def, Atk, Mana
        };

        /// <summary>Signed delta for the compare pane, e.g. "+12", "-3%".</summary>
        public static string Delta(StatKey k, double v)
        {
            string sign = v > 0 ? "+" : "-";
            return sign + Value(k, System.Math.Abs(v));
        }
    }
}
