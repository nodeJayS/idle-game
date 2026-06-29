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
    // bias both rely on (int)Rarity as the rank (Normal = 0 … Legendary = 4).
    public enum Rarity { Normal, Magic, Rare, Unique, Legendary }

    // Appended (not reordered) so persisted slot values stay stable. MapleStory-style
    // gear sheet: armor pieces + a weapon/offhand pair, plus two accessory slots.
    public enum EquipSlot { Weapon, Helm, Chest, Boots, Ring, Amulet, Offhand, Gloves, Cape }

    // Appended (not reordered) so persisted StatKey values stay stable across saves.
    // MoveSpd = movement (tiles/sec); AtkSpd = action rate (attacks, spells, heals — and
    // client animation speed). MaxMana/ManaRegen back the skill resource.
    public enum StatKey { Hp, Atk, Def, MoveSpd, CritChance, CritDmg, HpRegen, AttackRange, SplashRadius, MaxMana, ManaRegen, AtkSpd }

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
        public int Xp = 0;
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

    /// <summary>Which reward channel a monster modifier boosts (thematic per type — see
    /// <see cref="Modifiers"/> / <see cref="GameConfig"/>). Gold/XP scale the per-kill payout;
    /// DropRate biases loot rarity upward.</summary>
    public enum ModifierReward { Gold, Xp, DropRate }

    /// <summary>An optional per-hit combat behavior a modifier grants the monster, beyond stat
    /// multipliers. Vampiric = heals a fraction of damage it deals; Thorns = reflects a fraction
    /// of damage taken back to the attacker; Splash = its attacks hit the whole party in a radius
    /// (a real combat mechanic — the basis of the first loot-imprint modifier). None = stat-only.</summary>
    public enum ModifierBehavior { None, Vampiric, Thorns, Splash }

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
