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
        public string Sprite = "";        // renderer hint only
        public string AttackFx = "melee"; // renderer hint: basic-attack visual (e.g. "fireball")
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
        public string SpawnStyle = "pop"; // renderer hint: how this monster animates in
        public string AttackFx = "melee"; // renderer hint: basic-attack visual
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
        // Offline yield as a fraction of the online rate (gold, XP, and loot rolls
        // alike) — starts at 70% to nudge active play; tune freely later.
        public double OfflineRate = 0.70;
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

        // Farm zones (M8/M9): max concurrent trash, and how big a wave spawns each
        // interval (scattered across the field) until the cap is reached.
        public int MobCap = 30;
        public int SpawnBatchSize = 10;
        public double SpawnIntervalMs = 10000;

        // Boss challenge (M8): seconds to kill the stage's boss and advance.
        public double BossChallengeSeconds = 60;

        // Play area (M9): half-extents of the field. Party stands on the left; trash
        // spawns scattered across it. Precursor to real terrain/maps later.
        public double MapHalfWidth = 12;
        public double MapHalfDepth = 8;

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

        // Tiered rate model (M8): rates grow a little each stage and jump significantly
        // each time you cross a real boss (every StagesPerTier). Stage drop params
        // (StageDef.DropRateMult / AffixItemLevel) use the same tier in GameConfig.Default.
        public int StagesPerTier = 10;
        public double RatePerStageMult = 1.06; // small incremental growth per stage
        public double RateTierMult = 2.2;      // significant jump per tier (after a real boss)
        public double DropRatePerStage = 0.04; // additive rarity bias per stage within a tier
        public double DropRateTierBonus = 0.4; // additive rarity bias per tier
        public int ItemLevelTierBonus = 6;     // item-power bump per tier

        /// <summary>0-based difficulty tier: stages 1..N => 0, N+1..2N => 1, … (N = StagesPerTier).</summary>
        public int Tier(int stage) => Math.Max(0, (stage - 1) / StagesPerTier);

        private double TierScale(int stage) =>
            Math.Pow(RatePerStageMult, Math.Max(0, stage - 1)) * Math.Pow(RateTierMult, Tier(stage));

        public long GoldPerSec(int stage) => (long)Math.Floor(5 * TierScale(stage));
        public long XpPerSec(int stage) => (long)Math.Floor(3 * TierScale(stage));
        public double LootRollsPerHour(int stage) => 20 + 5 * (stage - 1) + 40 * Tier(stage);

        /// <summary>Per-kill XP/gold multiplier by stage — deeper stages pay more (same tier curve as idle).</summary>
        public double KillRewardMult(int stage) => TierScale(stage);
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
                               (StatKey.Spd, 1.0), (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.5),
                               (StatKey.HpRegen, 1.5),                 // very small sustain (hp/sec)
                               (StatKey.AttackRange, 1.2),             // melee
                               (StatKey.SplashRadius, 1.0),            // slightly wider cleave (melee perk)
                               (StatKey.MaxMana, 50), (StatKey.ManaRegen, 3)), // shallow pool, slow regen
                GrowthPerLevel = SB((StatKey.Hp, 18), (StatKey.Atk, 3), (StatKey.Def, 1.5), (StatKey.MaxMana, 2)),
                Skills = new List<string> { "cleave" }, Sprite = "warrior",
            };

            cfg.Heroes["magician_basic"] = new HeroDef
            {
                DefId = "magician_basic", Name = "Magician", Class = "Magician", Role = "ranged",
                // fragile (low HP/Def) but hits harder from range, with a tighter AoE
                BaseStats = SB((StatKey.Hp, 72), (StatKey.Atk, 17), (StatKey.Def, 4),
                               (StatKey.Spd, 1.0), (StatKey.CritChance, 0.07), (StatKey.CritDmg, 1.5),
                               (StatKey.HpRegen, 1.0),
                               (StatKey.AttackRange, 6.0),             // max reach; still fine point-blank
                               (StatKey.SplashRadius, 0.75),           // tight AoE (same as warrior)
                               (StatKey.MaxMana, 120), (StatKey.ManaRegen, 6)), // deep pool, fast regen (caster)
                GrowthPerLevel = SB((StatKey.Hp, 11), (StatKey.Atk, 4), (StatKey.Def, 1), (StatKey.MaxMana, 5)),
                Skills = new List<string> { "firebolt" }, Sprite = "magician", AttackFx = "fireball",
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
                BaseStats = SB((StatKey.Hp, 18), (StatKey.Atk, 3), (StatKey.Def, 0), (StatKey.Spd, 0.8), (StatKey.CritDmg, 1.5)),
                LootTableId = "common", XpReward = 12, GoldReward = 3, Sprite = "slime",
            };
            cfg.Monsters["goblin"] = new MonsterDef
            {
                Id = "goblin", Name = "Goblin",
                BaseStats = SB((StatKey.Hp, 28), (StatKey.Atk, 5), (StatKey.Def, 1), (StatKey.Spd, 1.1), (StatKey.CritChance, 0.03), (StatKey.CritDmg, 1.5)),
                LootTableId = "common", XpReward = 20, GoldReward = 6, Sprite = "goblin",
            };
            cfg.Monsters["goblin_king"] = new MonsterDef
            {
                Id = "goblin_king", Name = "Goblin King",
                BaseStats = SB((StatKey.Hp, 160), (StatKey.Atk, 12), (StatKey.Def, 3), (StatKey.Spd, 0.9), (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.6)),
                LootTableId = "boss", XpReward = 60, GoldReward = 40, Sprite = "goblin_king", SpawnStyle = "rise",
            };

            for (int i = 0; i < 50; i++)
            {
                int stage = i + 1;
                int tier = cfg.Balance.Tier(stage);
                cfg.Stages.Add(new StageDef
                {
                    Stage = stage, MonsterLevel = stage, PackCount = 3 + stage / 5,
                    BossId = "goblin_king",
                    DropRateMult = 1 + cfg.Balance.DropRatePerStage * (stage - 1) + cfg.Balance.DropRateTierBonus * tier,
                    AffixItemLevel = stage + cfg.Balance.ItemLevelTierBonus * tier,
                });
            }

            cfg.Skills["cleave"] = new SkillDef
            {
                Id = "cleave", Name = "Cleave", CooldownMs = 3000, Range = 1.5,
                Targeting = "nearest", DamageMult = 1.4, Sprite = "cleave",
            };
            cfg.Skills["firebolt"] = new SkillDef
            {
                Id = "firebolt", Name = "Firebolt", CooldownMs = 2500, Range = 6.0,
                Targeting = "nearest", DamageMult = 1.3, Sprite = "firebolt",
            };

            return cfg;
        }
    }
}
