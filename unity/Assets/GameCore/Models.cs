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
        public long LastClaimAt; // epoch ms (server-validated later)
    }
}
