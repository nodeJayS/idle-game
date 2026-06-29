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
        // NOTE: AttackRange + SplashRadius are deliberately OMITTED — they're under-the-hood combat
        // mechanics (the player sees their effect in the fight, not a number), so they stay off the
        // hero stat sheet + equip-compare to cut clutter. Label/Value still handle them for the few
        // surfaces that read an item's own affixes (e.g. an imprinted-gear tooltip).
        public static readonly StatKey[] Order =
        {
            StatKey.Hp, StatKey.Def, StatKey.HpRegen,
            StatKey.Atk, StatKey.AtkSpd, StatKey.CritChance, StatKey.CritDmg,
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

        /// <summary>Prettified base name, e.g. "rusty_sword" → "Rusty Sword".</summary>
        public static string PrettyBase(string baseId)
        {
            var parts = baseId.Split('_');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }

        /// <summary>The item's display name — NO rarity word (rarity reads via tile color + a status line).
        /// Imprinted gear is titled with the mechanical modifier that stamped it, e.g. "Volatile Rusty
        /// Sword".</summary>
        public static string ItemName(Item item, GameConfig cfg)
        {
            var src = Loot.ImprintSource(item, cfg);
            string baseName = PrettyBase(item.BaseId);
            return src != null ? src.Name + " " + baseName : baseName;
        }

        /// <summary>Player-facing rarity name for the detail status line ("Rare", "Legendary", …).</summary>
        public static string RarityName(Rarity r) => r.ToString();

        /// <summary>Player-facing flavor for an imprint affix (a mechanical-modifier signature) — shown
        /// instead of a raw stat number, since these are under-the-hood mechanics. E.g. heroes already
        /// splash, so the splash imprint reads as "wider splash radius".</summary>
        public static string ImprintBlurb(StatKey k) => k switch
        {
            StatKey.SplashRadius => "wider splash radius",
            StatKey.AttackRange => "longer attack range",
            _ => Label(k).ToLower(),
        };

        /// <summary>Signed delta for the compare pane, e.g. "+12", "-3%".</summary>
        public static string Delta(StatKey k, double v)
        {
            string sign = v > 0 ? "+" : "-";
            return sign + Value(k, System.Math.Abs(v));
        }
    }
}
