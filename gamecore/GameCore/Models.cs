using System.Collections.Generic;

namespace IdleGame.GameCore
{
    // ------------------------------------------------------------------------
    // game-core data model (C# port of the TS types).
    //
    // THE RULE (carried over from the web prototype): this assembly is pure
    // logic. No UnityEngine references anywhere in GameCore. Unity references
    // GameCore as the client; a .NET server can reuse it for authority.
    //
    // Three kinds of state, never mixed:
    //   - SaveState   : persisted
    //   - GameConfig  : static content, injected (see GameConfig.cs)
    //   - combat state: transient (added in M1)
    // ------------------------------------------------------------------------

    public enum Rarity { Normal, Magic, Rare, Unique }

    public enum EquipSlot { Weapon, Helm, Chest, Boots, Ring, Amulet }

    public enum StatKey { Hp, Atk, Def, Spd, CritChance, CritDmg }

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
        public int HighestRiftTier = 0;
        public int CurrentRiftTier = 1;
        public int AccountLevel = 1;
    }

    public sealed class SaveState
    {
        public int Version;
        public uint RngSeed;
        public int RngCursor;
        public List<HeroInstance> Heroes = new List<HeroInstance>();
        public string?[] Party = new string?[4]; // length 4; null = empty slot
        public List<Item> Inventory = new List<Item>();
        public Dictionary<string, long> Currencies = new Dictionary<string, long>();
        public ProgressState Progress = new ProgressState();
        public long LastClaimAt; // epoch ms (server-validated later)
    }
}
