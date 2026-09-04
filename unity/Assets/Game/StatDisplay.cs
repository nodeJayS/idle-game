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
        // Grouped by role: survivability, then offense, then mobility. (Mana was removed —
        // skills are cooldown-only, so there's no resource group.)
        // NOTE: AttackRange + SplashRadius are deliberately OMITTED — they're under-the-hood combat
        // mechanics (the player sees their effect in the fight, not a number), so they stay off the
        // hero stat sheet + equip-compare to cut clutter. Label/Value still handle them for the few
        // surfaces that read an item's own affixes (e.g. an imprinted-gear tooltip).
        public static readonly StatKey[] Order =
        {
            StatKey.Hp, StatKey.Def, StatKey.HpRegen,
            StatKey.Atk, StatKey.AtkSpd, StatKey.CritChance, StatKey.CritDmg,
            StatKey.MoveSpd,
        };

        /// <summary>Rank of a stat in the canonical order (for sorting affix lists).</summary>
        public static int Rank(StatKey k)
        {
            for (int i = 0; i < Order.Length; i++) if (Order[i] == k) return i;
            return Order.Length;
        }

        /// <summary>The stat's player-facing name. The enum-name fallback is a DEVELOPER backstop for
        /// a stat nobody has named yet — it is not a translatable string, and a player should never
        /// see it.</summary>
        public static string Label(StatKey k) => k switch
        {
            StatKey.Hp => Loc.T("stat.hp"),
            StatKey.Def => Loc.T("stat.def"),
            StatKey.HpRegen => Loc.T("stat.hp-regen"),
            StatKey.Atk => Loc.T("stat.atk"),
            StatKey.AtkSpd => Loc.T("stat.atk-spd"),
            StatKey.CritChance => Loc.T("stat.crit-chance"),
            StatKey.CritDmg => Loc.T("stat.crit-dmg"),
            StatKey.AttackRange => Loc.T("stat.attack-range"),
            StatKey.SplashRadius => Loc.T("stat.splash-radius"),
            StatKey.MoveSpd => Loc.T("stat.move-spd"),
            _ => k.ToString(),
        };

        /// <summary>Format an absolute value, e.g. "420", "7%", "x1.50", "2/s", "1.15". The UNIT is
        /// tabled (a language may mark percent or a rate differently) while the number itself is
        /// still formatted here and passed in as text — pre-formatting is the contract Loc.F asks
        /// for, and it keeps these renderings byte-identical to the literals they replaced.</summary>
        public static string Value(StatKey k, double v) => k switch
        {
            StatKey.CritChance => Loc.F("stat.unit-percent", Mathf.RoundToInt((float)(v * 100))),
            StatKey.CritDmg => Loc.F("stat.unit-multiplier", v.ToString("0.##")),
            StatKey.HpRegen => Loc.F("stat.unit-per-second", v.ToString("0.#")),
            StatKey.AttackRange => v.ToString("0.#"),
            StatKey.SplashRadius => v.ToString("0.#"),
            StatKey.AtkSpd => v.ToString("0.##"),
            StatKey.MoveSpd => v.ToString("0.##"),
            _ => Mathf.RoundToInt((float)v).ToString(), // Hp, Def, Atk
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
            // Content lookup by STABLE id, English config name as the fallback (10.20 phase 2): a
            // language pack translates "rusty_sword" if it wants to, and until one does the
            // prettified id renders exactly as before. The affix GRAMMAR is tabled separately,
            // because "X of Y" is a English possessive shape that other languages invert.
            string name = BaseName(item.BaseId);
            var pre = Loot.ImprintForSlot(item, cfg, ImprintSlot.Prefix);
            var suf = Loot.ImprintForSlot(item, cfg, ImprintSlot.Suffix);
            if (pre != null) name = Loc.F("item.name-prefixed", ModifierName(pre), name);   // "Volatile Rusty Sword"
            if (suf != null) name = Loc.F("item.name-suffixed", name, ModifierName(suf));   // "… of Leeching"
            if (item.Enhance > 0) name = Loc.F("item.name-enhanced", item.Enhance, name);   // "+7 Rusty Sword"
            return name;
        }

        // ---- content names (10.20 phase 2) -----------------------------------------------------
        // GameCore config carries English display names; these are the ONE seam each kind of name
        // passes through, so a language pack overrides by stable id and everything else keeps
        // reading the config. Deliberately not a single generic helper: the id namespaces differ,
        // and a caller picking the wrong prefix should not compile.

        /// <summary>An item base's display name ("rusty_sword" → "Rusty Sword", or a pack's word).</summary>
        public static string BaseName(string baseId) => Loc.Content("item." + baseId, PrettyBase(baseId));

        /// <summary>A modifier's display name — imprint titles, the modifier panel, boss context.</summary>
        public static string ModifierName(ModifierDef def) => Loc.Content("modifier." + def.Id, def.Name);

        /// <summary>A zone's display name.</summary>
        public static string ZoneName(ZoneDef zone) => Loc.Content("zone." + zone.Id, zone.Name);

        /// <summary>A set's display name, keyed by the set id the item carries.</summary>
        public static string SetName(string setId, string configName) => Loc.Content("set." + setId, configName);

        /// <summary>A hero's display name, keyed by its DEF id (the class), not the instance id.</summary>
        public static string HeroName(string defId, string configName) => Loc.Content("hero." + defId, configName);

        /// <summary>Player-facing rarity name for the detail status line ("Rare", "Legendary", …).
        /// 10.20c: routes through the string table keyed off the enum name — this method stays THE
        /// seam (RarityTag and every display site ride it), so the table swap happened in one place.
        /// The composed key is invisible to LocTests' static scan; ComposedRarityKeysExist pins it.</summary>
        public static string RarityName(Rarity r) => Loc.T("rarity." + r.ToString().ToLowerInvariant());

        /// <summary>Display name for a live-ops event banner, composed CLIENT-side via Loc off the
        /// stable event id (the 10.20c leak fix: EventInfo.Name is GameCore-composed English and
        /// can't be translated). The weekend boost names its zone from <see cref="EventInfo.ZoneIndex"/>;
        /// "Zone" mirrors Events.Active's own defensive fallback. Unknown future ids fall back to
        /// the GameCore-composed Name so a new event is never blank.</summary>
        public static string EventName(EventInfo ev, GameConfig cfg) => ev.Id switch
        {
            Events.WeekendZoneBoostId => Loc.F("event.weekend-boost",
                ev.ZoneIndex >= 0 && ev.ZoneIndex < cfg.Zones.Count
                    ? ZoneName(cfg.Zones[ev.ZoneIndex])
                    : Loc.T("event.zone-fallback")),
            Events.MutatedCryptId => Loc.T("event.mutated-crypt"),
            _ => ev.Name,
        };

        /// <summary>The rarity name led by its <see cref="Palette.RarityMark"/> glyph ("● Rare") — the
        /// text form of the 10.20b shape channel, for every DISPLAY site that renders a rarity word in
        /// its rarity color. Collapses to the bare name for Normal (unmarked by design). A plain space,
        /// not a thin space — UIFont's thin-space coverage is unverified, and a tofu box would be worse
        /// than the wider gap.</summary>
        public static string RarityTag(Rarity r)
        {
            string mark = Palette.RarityMark(r);
            return mark.Length > 0 ? mark + " " + RarityName(r) : RarityName(r);
        }

        /// <summary>Player-facing flavor for an imprint affix (a mechanical-modifier signature) — shown
        /// instead of a raw stat number, since these are under-the-hood mechanics. E.g. heroes already
        /// splash, so the splash imprint reads as "wider splash radius".</summary>
        public static string ImprintBlurb(StatKey k) => k switch
        {
            StatKey.SplashRadius => Loc.T("imprint.splash-radius"),
            StatKey.AttackRange => Loc.T("imprint.attack-range"),
            StatKey.Lifesteal => Loc.T("imprint.lifesteal"),
            StatKey.ThornsReflect => Loc.T("imprint.thorns-reflect"),
            StatKey.ChainCount => Loc.T("imprint.chain-count"),
            // Lowercasing a translated stat label is an English habit (German nouns stay
            // capitalised), but it is the same fallback as before and only reaches an imprint
            // stat nobody has written a blurb for yet.
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
