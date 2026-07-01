#nullable enable
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    // ------------------------------------------------------------------------
    // GameCore data model.
    //
    // THE RULE: this assembly is pure logic. No UnityEngine references anywhere
    // in GameCore. Unity references GameCore as the client; a .NET server can
    // reuse it for authority.
    //
    // Three kinds of state, never mixed:
    //   - SaveState   : persisted
    //   - GameConfig  : static content, injected (see GameConfig.cs)
    //   - combat state: transient
    // ------------------------------------------------------------------------

    // Ascending value — order matters: AffixDef.RarityFloor and the rarity-roll
    // bias both rely on (int)Rarity as the rank (Normal = 0 … Mythic = 4).
    // 2026-07 refactor dropped Magic and added Mythic on top; persisted (int)Rarity
    // shifted, so old saves reseed at New Game (deliberate — no migration).
    public enum Rarity { Normal, Rare, Unique, Legendary, Mythic }

    // Appended (not reordered) so persisted slot values stay stable. MapleStory-style
    // gear sheet: armor pieces + a weapon/offhand pair, plus two accessory slots.
    public enum EquipSlot { Weapon, Helm, Chest, Boots, Ring, Amulet, Offhand, Gloves, Cape }

    // Appended (not reordered) so persisted StatKey values stay stable across saves.
    // MoveSpd = movement (tiles/sec); AtkSpd = action rate (attacks, spells, heals — and
    // client animation speed). MaxMana/ManaRegen back the skill resource.
    // Lifesteal/ThornsReflect appended last (stable persisted values). They are EXCLUSIVE imprint
    // stats — in no item base's AllowedAffixes, so they only ever reach gear via Loot.ImprintDrop
    // (the rare suffix mods of Leeching / of Thorns). Read in Combat.ApplyHit alongside the same-named
    // monster-behavior fields, so a hero with the stat leeches/reflects exactly like a modded monster.
    public enum StatKey { Hp, Atk, Def, MoveSpd, CritChance, CritDmg, HpRegen, AttackRange, SplashRadius, MaxMana, ManaRegen, AtkSpd, Lifesteal, ThornsReflect, ChainCount }

    /// <summary>A bag of stat values. Partial blocks simply omit keys.</summary>
    public sealed class StatBlock : Dictionary<StatKey, double>
    {
        public StatBlock() { }
        public StatBlock(IDictionary<StatKey, double> src) : base(src) { }

        public double Get(StatKey k) => TryGetValue(k, out var v) ? v : 0.0;
    }

    public sealed class HeroInstance
    {
        public string Id = "";
        public string DefId = "";
        public int Level = 1;
        public long Xp = 0; // remainder toward the next level; long so a months-long curve can't overflow
        public Dictionary<EquipSlot, string> Equipped = new Dictionary<EquipSlot, string>();
        public List<string> SkillLoadout = new List<string>();
        // Build depth (Lever 3): skillId -> points invested (rank). Absent = rank 0 (= base, today's
        // behavior). Points are EARNED from hero level and SPENT here; unspent is derived, never
        // persisted separately (see Skills.UnspentPoints). Threaded like SkillLoadout through every
        // HeroInstance copy site, or an equip/level/loadout edit silently resets it.
        public Dictionary<string, int> SkillRanks = new Dictionary<string, int>();
    }

    public sealed class Affix
    {
        public StatKey Stat;
        public double Value;
    }

    public sealed class Item
    {
        public string Id = "";
        public string BaseId = "";
        public Rarity Rarity;
        public int ItemLevel;
        public List<Affix> Affixes = new List<Affix>();
    }

    public sealed class ProgressState
    {
        public int HighestStage = 0;
        public int CurrentStage = 1;
        public int AccountLevel = 1;
        // Tower of Ascension (alt mode) progress. Nested here so it rides the existing
        // `Progress` reference-threading (only the 2 `new ProgressState{}` reducers must carry it),
        // and can grow without touching every SaveState copy site.
        public TowerState Tower = new TowerState();
        // Lifetime achievement ladder (Lever 4). Nested here for the same reason as Tower — the
        // ProgressState constructors carry it forward, so the ~20 other save-copy sites get it free.
        public AchievementState Achievements = new AchievementState();
        // Daily-login streak (Lever 4 — premium currency). Nested here for the same threading reason.
        public DailyLoginState Daily = new DailyLoginState();
    }

    /// <summary>Tower of Ascension progress (a separate one-clear-per-floor track, distinct from the
    /// farmable stage ladder). <see cref="HighestFloor"/> is the deepest floor cleared (0 = none);
    /// the next attemptable floor is HighestFloor + 1. Permanent account-wide buffs are *derived*
    /// from this (every N floors → a milestone), so nothing else needs persisting.</summary>
    public sealed class TowerState
    {
        public int HighestFloor = 0;
    }

    /// <summary>What a goal tracks. Each maps to a game event the client feeds into
    /// <see cref="Quests"/>.Advance (kills, stage clears, salvages, gold earned, rare drops).</summary>
    public enum QuestKind { KillMonsters, ClearStages, SalvageItems, EarnGold, FindRarePlus }

    /// <summary>One active goal on the board: accrue <see cref="Progress"/> up to
    /// <see cref="Target"/>, then it pays out and a fresh goal rolls in.</summary>
    public sealed class Quest
    {
        public QuestKind Kind;
        public long Target;
        public long Progress;
        public long RewardGold;
        public int RewardXp;
    }

    /// <summary>The rolling goal board (always a few near-term carrots). <see cref="RollCount"/>
    /// is a monotonic cursor so replacements cycle goal kinds deterministically.</summary>
    public sealed class QuestBoard
    {
        public List<Quest> Active = new List<Quest>();
        public int RollCount;
    }

    /// <summary>A lifetime stat an achievement tracks (Lever 4). ADD-metrics accumulate forever
    /// (kills, gold earned, salvages, rare+ finds, bosses); MAX-metrics keep a peak (deepest stage /
    /// tower floor, highest hero level). The client feeds these from the SAME game events it already
    /// feeds the goal board. Appended, not reordered — persisted as dictionary keys.</summary>
    public enum AchievementMetric
    {
        MonstersKilled, BossesKilled, ItemsSalvaged, GoldEarned, RarePlusFound,
        HighestStage, HighestTowerFloor, HeroLevel,
    }

    /// <summary>
    /// Lifetime achievement progress (Lever 4 — the permanent milestone ladder, distinct from the
    /// rolling <see cref="QuestBoard"/> that resets on completion). <see cref="Counters"/> hold the
    /// lifetime value per metric; <see cref="Claimed"/> records how many tiers of each achievement
    /// have already paid out, so a reward mints exactly once. Nested under <see cref="ProgressState"/>
    /// (like <see cref="TowerState"/>) so it rides the existing Progress reference-threading — only
    /// the ProgressState constructors carry it, not every SaveState copy site.
    /// </summary>
    public sealed class AchievementState
    {
        public Dictionary<AchievementMetric, long> Counters = new Dictionary<AchievementMetric, long>(); // metric -> lifetime value
        public Dictionary<string, int> Claimed = new Dictionary<string, int>();                          // achId -> tiers paid out
    }

    /// <summary>
    /// Daily-login streak state (Lever 4 — the premium-currency hook). Tracks the day of the last claim
    /// (a UTC day-index = epoch-ms / 86_400_000), the consecutive-day <see cref="Streak"/>, and lifetime
    /// <see cref="TotalClaims"/>. A claim grants GEMS — the PREMIUM currency, held in
    /// <see cref="SaveState.Currencies"/> and (for now) earnable no other way — the seed of the future
    /// gacha/microtransaction economy. Nested under <see cref="ProgressState"/> (like
    /// <see cref="TowerState"/> / <see cref="AchievementState"/>) so it rides the Progress threading.
    /// </summary>
    public sealed class DailyLoginState
    {
        public long LastClaimDay; // UTC day-index of the last claim; 0 = never claimed
        public int Streak;        // consecutive-day streak (1 on the first claim or after a break)
        public int TotalClaims;   // lifetime daily claims
    }

    /// <summary>Which reward channel a monster modifier boosts (thematic per type — see
    /// <see cref="Modifiers"/> / <see cref="GameConfig"/>). Gold/XP scale the per-kill payout;
    /// DropRate biases loot rarity upward.</summary>
    public enum ModifierReward { Gold, Xp, DropRate }

    /// <summary>One channel of a modifier's reward split. A mod can pay several at once (a "hybrid"
    /// reward, e.g. half gold + half drop rate); a plain mod is just a single part.</summary>
    public struct RewardPart
    {
        public ModifierReward Channel;
        public double PerStrength;
    }

    /// <summary>An optional per-hit combat behavior a modifier grants the monster, beyond stat
    /// multipliers. Vampiric = heals a fraction of damage it deals; Thorns = reflects a fraction
    /// of damage taken back to the attacker; Splash = its attacks hit the whole party in a radius
    /// (a real combat mechanic — the basis of the first loot-imprint modifier). None = stat-only.</summary>
    public enum ModifierBehavior { None, Vampiric, Thorns, Splash, Chain }

    /// <summary>Which imprint slot a rare (mechanical) modifier occupies — PoE-style. An item can hold
    /// at most ONE prefix imprint and ONE suffix imprint. Prefix imprints title the gear with a leading
    /// word ("Volatile Sword"); suffix imprints add a trailing phrase ("Sword of Leeching").</summary>
    public enum ImprintSlot { Prefix, Suffix }

    /// <summary>
    /// Banked monster modifiers (the risk/reward knob). <see cref="Owned"/> maps a modifier
    /// typeId to the best strength banked (= highest stage of that type's boss you've cleared);
    /// <see cref="Active"/> are the types currently toggled ON, which apply to farm trash —
    /// harder mobs for a thematic reward bonus. Stack freely. Persisted; threaded like
    /// <see cref="SaveState.Quests"/> through every reducer copy site.
    /// </summary>
    public sealed class MonsterModifiers
    {
        public Dictionary<string, int> Owned = new Dictionary<string, int>(); // typeId -> best strength
        public List<string> Active = new List<string>();                      // typeIds applied to farm trash
        // Modifier-shop tuning: typeId -> a multiplier (≥1.0, absent = 1.0) on BOTH the mod's monster
        // danger AND its reward, gambled up/down (floored at 1.0) by spending gold+scrap. Persisted.
        public Dictionary<string, double> Tuning = new Dictionary<string, double>();
    }

    public sealed class SaveState
    {
        public int Version;
        public uint RngSeed;
        public int RngCursor;
        public List<HeroInstance> Heroes = new List<HeroInstance>();
        public string?[] Party = new string?[Save.PartySize]; // null = empty slot
        // Chosen formation leader (a fielded hero id). null = auto: the lowest-slot living
        // hero leads. The combat sim reads this via CombatState.LeaderRefId.
        public string? LeaderHeroId;
        public List<Item> Inventory = new List<Item>();
        public Dictionary<string, long> Currencies = new Dictionary<string, long>();
        public ProgressState Progress = new ProgressState();
        public QuestBoard Quests = new QuestBoard();
        public MonsterModifiers Modifiers = new MonsterModifiers();
        public long LastClaimAt; // epoch ms (server-validated later)
    }
}
