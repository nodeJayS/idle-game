#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    // ------------------------------------------------------------------------
    // Static content definitions + the assembled GameConfig. Injected into
    // game-core functions (not a global) so a server can share the same config
    // and balance is swappable. In Unity these become ScriptableObjects or load
    // from JSON; GameConfig.Default() is the built-in content set.
    // ------------------------------------------------------------------------

    public sealed class HeroDef
    {
        public string DefId = "";
        public string Name = "";
        public string Class = "";
        public string Role = "melee"; // melee | ranged | support
        public StatBlock BaseStats = new StatBlock();
        public StatBlock GrowthPerLevel = new StatBlock();
        public List<string> Skills = new List<string>();
        public string Sprite = ""; // renderer hint only
    }

    public sealed class ItemBaseDef
    {
        public string BaseId = "";
        public EquipSlot Slot;
        public StatBlock BaseStats = new StatBlock();
        public List<StatKey> AllowedAffixes = new List<StatKey>();
        public string Sprite = "";
    }

    public sealed class AffixDef
    {
        public StatKey Stat;
        public double Weight;
        public double ValueMinPerItemLevel;
        public double ValueMaxPerItemLevel;
        public Rarity RarityFloor;
    }

    public sealed class MonsterDef
    {
        public string Id = "";
        public string Name = "";
        public StatBlock BaseStats = new StatBlock();
        public string LootTableId = "";
        public int XpReward;
        public int GoldReward;
        public string Sprite = "";
    }

    public sealed class StageDef
    {
        public int Stage;
        public int MonsterLevel;
        public int PackCount;
        public string BossId = "";
        public double DropRateMult;
        public int AffixItemLevel;

        /// <summary>Every 10th stage hosts a major (scaled) boss. Distinct content later.</summary>
        public bool IsMajorBoss => Stage % 10 == 0;
    }

    public sealed class SkillDef
    {
        public string Id = "";
        public string Name = "";
        public double CooldownMs;
        public double Range;
        public string Targeting = "nearest"; // nearest | lowestHp | self | aoe
        public double DamageMult = 1.0;       // effect shape finalized in M1
        public string? Sprite;
    }

    /// <summary>All tunable numbers. The file you edit constantly to balance.</summary>
    public sealed class BalanceConstants
    {
        public double IdleCapHours = 12;
        public double OfflineRate = 0.8;
        public int MaxLevel = 100;

        // A major boss (every 10th stage) multiplies the stage boss's scaled stats
        // on top of the normal monster-level scaling.
        public double MajorBossMult = 2.5;

        // Hero downing/respawn (M4.3). A downed party hero respawns after
        // RespawnBaseMs + RespawnPerLevelMs * level. A run that can't clear within
        // MaxRunSeconds is a loss (stuck/under-geared).
        public double RespawnBaseMs = 3000;
        public double RespawnPerLevelMs = 200;
        public double MaxRunSeconds = 120;

        // Base drop weights per rarity, indexed by (int)Rarity ascending:
        // [Normal, Magic, Rare, Unique, Legendary]. Must have one entry per Rarity.
        // The stage's DropRateMult biases this upward — see Loot.RollRarity.
        public double[] RarityBaseWeights = { 1000, 400, 120, 25, 4 };

        // Affix count (min, max) per rarity, indexed by (int)Rarity ascending:
        // [Normal, Magic, Rare, Unique, Legendary]. Counts cap at the eligible
        // affix-pool size for the item base — see Loot.RollAffixes.
        public (int min, int max)[] AffixCountByRarity = { (0, 0), (1, 2), (3, 4), (4, 5), (5, 6) };

        // Base chance a common monster drops an item. Bosses always drop. (DropRateMult
        // biases rarity, not drop chance, to avoid double-dipping.)
        public double DropChance = 0.35;

        public long XpCurve(int level) => (long)Math.Floor(100 * Math.Pow(1.15, level - 1));
        public long GoldPerSec(int stage) => (long)Math.Floor(5 * Math.Pow(1.18, stage));
        public long XpPerSec(int stage) => (long)Math.Floor(3 * Math.Pow(1.15, stage));
        public double LootRollsPerHour(int stage) => 20 + stage * 5;
    }

    public sealed class GameConfig
    {
        public Dictionary<string, HeroDef> Heroes = new Dictionary<string, HeroDef>();
        public Dictionary<string, ItemBaseDef> ItemBases = new Dictionary<string, ItemBaseDef>();
        public List<AffixDef> AffixPool = new List<AffixDef>();
        public Dictionary<string, MonsterDef> Monsters = new Dictionary<string, MonsterDef>();
        public List<StageDef> Stages = new List<StageDef>();
        public Dictionary<string, SkillDef> Skills = new Dictionary<string, SkillDef>();
        public BalanceConstants Balance = new BalanceConstants();

        private static StatBlock SB(params (StatKey k, double v)[] pairs)
        {
            var b = new StatBlock();
            foreach (var (k, v) in pairs) b[k] = v;
            return b;
        }

        /// <summary>The default content set.</summary>
        public static GameConfig Default()
        {
            var cfg = new GameConfig();

            cfg.Heroes["warrior_basic"] = new HeroDef
            {
                DefId = "warrior_basic", Name = "Warrior", Class = "Warrior", Role = "melee",
                BaseStats = SB((StatKey.Hp, 120), (StatKey.Atk, 14), (StatKey.Def, 8),
                               (StatKey.Spd, 1.0), (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.5)),
                GrowthPerLevel = SB((StatKey.Hp, 18), (StatKey.Atk, 3), (StatKey.Def, 1.5)),
                Skills = new List<string> { "cleave" }, Sprite = "warrior",
            };

            cfg.ItemBases["rusty_sword"] = new ItemBaseDef
            {
                BaseId = "rusty_sword", Slot = EquipSlot.Weapon, BaseStats = SB((StatKey.Atk, 6)),
                AllowedAffixes = new List<StatKey> { StatKey.Atk, StatKey.CritChance, StatKey.CritDmg, StatKey.Spd },
                Sprite = "sword",
            };
            cfg.ItemBases["leather_cap"] = new ItemBaseDef
            {
                BaseId = "leather_cap", Slot = EquipSlot.Helm, BaseStats = SB((StatKey.Def, 3), (StatKey.Hp, 10)),
                AllowedAffixes = new List<StatKey> { StatKey.Hp, StatKey.Def }, Sprite = "helm",
            };
            cfg.ItemBases["leather_vest"] = new ItemBaseDef
            {
                BaseId = "leather_vest", Slot = EquipSlot.Chest, BaseStats = SB((StatKey.Def, 5), (StatKey.Hp, 20)),
                AllowedAffixes = new List<StatKey> { StatKey.Hp, StatKey.Def }, Sprite = "chest",
            };

            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Hp, Weight = 30, ValueMinPerItemLevel = 4, ValueMaxPerItemLevel = 8, RarityFloor = Rarity.Magic });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Atk, Weight = 25, ValueMinPerItemLevel = 1, ValueMaxPerItemLevel = 2, RarityFloor = Rarity.Magic });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Def, Weight = 20, ValueMinPerItemLevel = 1, ValueMaxPerItemLevel = 2, RarityFloor = Rarity.Magic });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Spd, Weight = 8, ValueMinPerItemLevel = 0.01, ValueMaxPerItemLevel = 0.03, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.CritChance, Weight = 8, ValueMinPerItemLevel = 0.005, ValueMaxPerItemLevel = 0.015, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.CritDmg, Weight = 9, ValueMinPerItemLevel = 0.03, ValueMaxPerItemLevel = 0.08, RarityFloor = Rarity.Rare });

            cfg.Monsters["slime"] = new MonsterDef
            {
                Id = "slime", Name = "Slime",
                BaseStats = SB((StatKey.Hp, 30), (StatKey.Atk, 5), (StatKey.Def, 1), (StatKey.Spd, 0.8), (StatKey.CritDmg, 1.5)),
                LootTableId = "common", XpReward = 5, GoldReward = 2, Sprite = "slime",
            };
            cfg.Monsters["goblin"] = new MonsterDef
            {
                Id = "goblin", Name = "Goblin",
                BaseStats = SB((StatKey.Hp, 45), (StatKey.Atk, 8), (StatKey.Def, 2), (StatKey.Spd, 1.1), (StatKey.CritChance, 0.03), (StatKey.CritDmg, 1.5)),
                LootTableId = "common", XpReward = 9, GoldReward = 4, Sprite = "goblin",
            };
            cfg.Monsters["goblin_king"] = new MonsterDef
            {
                Id = "goblin_king", Name = "Goblin King",
                BaseStats = SB((StatKey.Hp, 320), (StatKey.Atk, 16), (StatKey.Def, 5), (StatKey.Spd, 0.9), (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.6)),
                LootTableId = "boss", XpReward = 60, GoldReward = 40, Sprite = "goblin_king",
            };

            for (int i = 0; i < 50; i++)
            {
                int stage = i + 1;
                cfg.Stages.Add(new StageDef
                {
                    Stage = stage, MonsterLevel = stage, PackCount = 3 + stage / 5,
                    BossId = "goblin_king", DropRateMult = 1 + stage * 0.05, AffixItemLevel = stage,
                });
            }

            cfg.Skills["cleave"] = new SkillDef
            {
                Id = "cleave", Name = "Cleave", CooldownMs = 3000, Range = 1.5,
                Targeting = "nearest", DamageMult = 1.4, Sprite = "cleave",
            };

            return cfg;
        }
    }
}
