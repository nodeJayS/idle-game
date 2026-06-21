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
        public List<string> Skills = new List<string>(); // M11: skills this monster casts (e.g. boss signature)
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

    public enum SkillEffectKind { Damage, Heal, Buff }

    public sealed class SkillDef
    {
        public string Id = "";
        public string Name = "";
        public double CooldownMs;
        public double Range;
        public string Targeting = "nearest"; // nearest | lowestHp | self | aoe
        public SkillEffectKind Effect = SkillEffectKind.Damage;
        public double ManaCost;               // basic attacks are free; skills cost mana
        public double DamageMult = 1.0;       // x caster Atk, for Damage and Heal scaling
        public double AoeRadius;              // Damage: also hit enemies within this of the primary target
        public StatKey BuffStat;              // Buff: which stat to raise (self)
        public double BuffAmount;             // Buff: additive amount
        public double BuffDurationMs;         // Buff: how long it lasts
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

        // Skills (Lever 3): how many of a hero's known skills can be slotted active at once.
        // HeroDef.Skills is the known pool; HeroInstance.SkillLoadout is the chosen subset.
        public int MaxActiveSkills = 4;

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
        public int MobCap = 60;            // dense packs to mow through (ARPG / MS2 horde feel)
        public int SpawnBatchSize = 10;    // mobs per PACK (a spawn drops a tight cluster, not a scatter)
        public double SpawnIntervalMs = 900; // refill fast so the field stays full of things to kill

        // Trash spawns as PACKS in a ring around the PARTY (not a fixed box): each spawn is a
        // tight cluster (PackRadius) at one ring point, so packs appear near the group wherever
        // it roams with quiet gaps between. Spawned mobs persist and wander (no distance cull).
        public double SpawnRingInner = 10;  // packs ring closer to the party so there's always
        public double SpawnRingOuter = 26;  // something in engage range — no dead walking time
        public double PackRadius = 3.5;

        // Boss challenge (M8): seconds to kill the stage's boss and advance.
        public double BossChallengeSeconds = 30;
        // Boss challenge (C1): pressing Challenge despawns the trash and the boss appears this far
        // ahead of the party on the SAME map (a step or two away — not a separate arena). After a
        // flee/fail, trash stays gone for BossFleeCooldownMs before repopulating, so spamming
        // challenge→flee can't be used to refresh packs on demand.
        public double BossSpawnDistance = 8;
        public double BossFleeCooldownMs = 4000;

        // Play area: half-extents of the field. The party starts at the centre; trash
        // spawns scattered across the whole field (~2.5x the old area) so heroes range
        // out to hunt it. Precursor to real terrain/maps later.
        public double MapHalfWidth = 200;
        public double MapHalfDepth = 140;

        // Unit bodies (M-feel): every unit occupies a soft circle so two units can't stand
        // on the same point. Overlapping LIVING units are pushed apart each step — split by
        // the opposite body's radius, so a boss barely budges while trash is shoved clear.
        // Bosses are chunkier. Attack/skill ranges count from the target's body edge, so a
        // big body stays reachable. CollisionIterations relaxation passes per step trade
        // crowd tightness against cost (O(n^2) per pass; n is small — party + MobCap).
        // Radii roughly match the client's visual bodies (capsule ~0.35, boss cube ~0.56
        // half-width) plus a little personal space, so the spacing the player sees matches
        // the sim. Tune alongside CombatView's primitive scales.
        public double UnitRadius = 0.45;
        public double BossRadius = 0.7;
        public int CollisionIterations = 2;

        // Solo party leash: a hero fights enemies within this distance of itself (individual
        // combat); when nothing is that close, heroes travel together toward the pack nearest
        // the party centre, so they read as separate units without scattering across the map.
        public double EngageRadius = 14;

        // Wander (idle trash): a non-aggro mob ambles between random points within
        // WanderRadius of itself, repicking every WanderMin..MaxMs, at WanderSpeedMult of
        // its move speed — until a hero hits it and it aggros.
        public double WanderRadius = 5.0;
        public double WanderMinMs = 1500;
        public double WanderMaxMs = 3500;
        public double WanderSpeedMult = 0.5;

        // Difficulty scales GEOMETRICALLY per stage so each stage (and 10-boss wall) is a
        // real gate and the climb to stage 100 stays long. HP grows steeply (the DPS-check
        // gate); damage grows gently so trash doesn't one-shot the party. Bosses multiply
        // HP by BossHpMult on top, and major bosses (every 10th) by MajorBossMult again.
        public double MonsterHpGrowth = 1.18;  // +18% HP per stage level (×~4.4 by 10, ×~1.3M by 100)
        public double MonsterDmgGrowth = 1.08; // +8% atk/def per stage level (survivable)
        public double BossHpMult = 2.5;        // a boss is ~2.5x a same-stage trash mob (cut from 5 for faster boss kills)

        // Base drop weights per rarity, indexed by (int)Rarity ascending:
        // [Normal, Magic, Rare, Unique, Legendary]. Must have one entry per Rarity.
        // The stage's DropRateMult biases this upward — see Loot.RollRarity.
        public double[] RarityBaseWeights = { 1000, 400, 120, 25, 4 };

        // Affix count (min, max) per rarity, indexed by (int)Rarity ascending:
        // [Normal, Magic, Rare, Unique, Legendary]. Counts cap at the eligible
        // affix-pool size for the item base — see Loot.RollAffixes.
        public (int min, int max)[] AffixCountByRarity = { (0, 0), (1, 2), (3, 4), (4, 5), (5, 6) };

        // Per-kill chance a common monster drops an item. Tuned for a steady loot "rain"
        // (PoE/Maple cadence): most drops get auto-salvaged to scrap (a number that keeps
        // climbing), with the occasional keeper. PRIMARY loot-cadence dial. Trash drops are
        // capped at Rare (TrashRarityCap); Unique/Legendary come only from boss bundles below.
        public double DropChance = 0.12;

        // Highest rarity a common/trash/idle drop can roll. Unique/Legendary are
        // boss-exclusive (guaranteed bundles), so the open-world ceiling is Rare.
        public Rarity TrashRarityCap = Rarity.Rare;

        // Boss guaranteed loot (PoE-style chase items). Each boss drops a bundle of
        // Unique/Legendary items — count by boss tier — plus a few ordinary extras.
        // Per bundle item: BossLegendaryChance => Legendary, otherwise Unique.
        public double BossLegendaryChance = 0.01;
        public (int min, int max) MajorBossUniques = (5, 7);
        public (int min, int max) MiniBossUniques = (1, 2);
        public int MajorBossExtras = 4; // ordinary Normal–Rare extras on top
        public int MiniBossExtras = 2;

        // Inventory: max LOOSE (unequipped) items the bag holds. Equipped gear doesn't
        // count. Live farm drops are blocked once full; idle accrual and boss/special
        // drops are allowed to OVERFILL past it (e.g. 106/100). Auto-salvage (within the
        // player's threshold) and manual salvage are the ways back under — owned items
        // are never destroyed automatically.
        public int InventoryCap = 100;

        // Scrap (salvage currency) yielded per item, indexed by (int)Rarity ascending:
        // [Normal, Magic, Rare, Unique, Legendary]. Plus the item's level. Tune later.
        public long[] ScrapValueByRarity = { 1, 3, 8, 20, 50 };
        public long ScrapValue(Rarity rarity, int itemLevel)
            => ScrapValueByRarity[(int)rarity] + Math.Max(0, itemLevel);

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
        // Idle loot is scarce too (mirrors active farming's slow trickle). Still monotonic
        // across stages/tiers; long high-stage idles can overfill the bag (allowed).
        public double LootRollsPerHour(int stage) => 6 + 0.4 * (stage - 1) + 6 * Tier(stage);

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

        /// <summary>Heroes granted by clearing a stage: highestStage reached >= key ⇒ acquire
        /// the hero def (value), once. The progression path to "20+ characters"; gacha later
        /// becomes another source feeding the same <see cref="Party.AcquireHero"/>.</summary>
        public Dictionary<int, string> HeroUnlocks = new Dictionary<int, string>();

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
                               (StatKey.MoveSpd, 3.0), (StatKey.AtkSpd, 0.85), // sturdy but swings slower than the mage
                               (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.5),
                               (StatKey.HpRegen, 1.5),                 // very small sustain (hp/sec)
                               (StatKey.AttackRange, 1.2),             // melee
                               (StatKey.SplashRadius, 1.0),            // slightly wider cleave (melee perk)
                               (StatKey.MaxMana, 50), (StatKey.ManaRegen, 3)), // shallow pool, slow regen
                GrowthPerLevel = SB((StatKey.Hp, 18), (StatKey.Atk, 3), (StatKey.Def, 1.5), (StatKey.MaxMana, 2)),
                // Known pool (6); first MaxActiveSkills are the starting active bar (see Skills.DefaultLoadout).
                Skills = new List<string> { "cleave", "bash", "warcry", "whirlwind", "bulwark", "frenzy" }, Sprite = "warrior",
            };

            cfg.Heroes["magician_basic"] = new HeroDef
            {
                DefId = "magician_basic", Name = "Magician", Class = "Magician", Role = "ranged",
                // fragile (low HP/Def) but hits harder from range, with a tighter AoE
                BaseStats = SB((StatKey.Hp, 72), (StatKey.Atk, 17), (StatKey.Def, 4),
                               (StatKey.MoveSpd, 3.0), (StatKey.AtkSpd, 1.15), // fragile but casts/attacks faster
                               (StatKey.CritChance, 0.07), (StatKey.CritDmg, 1.5),
                               (StatKey.HpRegen, 1.0),
                               (StatKey.AttackRange, 6.0),             // max reach; still fine point-blank
                               (StatKey.SplashRadius, 0.75),           // tight AoE (same as warrior)
                               (StatKey.MaxMana, 120), (StatKey.ManaRegen, 6)), // deep pool, fast regen (caster)
                GrowthPerLevel = SB((StatKey.Hp, 11), (StatKey.Atk, 4), (StatKey.Def, 1), (StatKey.MaxMana, 5)),
                // Known pool (6); first MaxActiveSkills are the starting active bar (see Skills.DefaultLoadout).
                Skills = new List<string> { "firebolt", "fireball", "mend", "scorch", "inferno", "haste" }, Sprite = "magician", AttackFx = "fireball",
            };

            cfg.Heroes["thief_basic"] = new HeroDef
            {
                DefId = "thief_basic", Name = "Thief", Class = "Thief", Role = "melee",
                // glass-cannon assassin: lowest HP/Def of the roster, fastest swings, and crit-built
                // (high CritChance + CritDmg) so its DPS lives in single-target burst, not durability.
                BaseStats = SB((StatKey.Hp, 64), (StatKey.Atk, 16), (StatKey.Def, 3),
                               (StatKey.MoveSpd, 3.4), (StatKey.AtkSpd, 1.45), // fastest mover + attacker
                               (StatKey.CritChance, 0.22), (StatKey.CritDmg, 1.9), // crit is the whole identity
                               (StatKey.HpRegen, 1.0),
                               (StatKey.AttackRange, 1.2),             // melee, same reach as the warrior
                               (StatKey.SplashRadius, 0.5),            // narrow — a duelist, not a cleaver
                               (StatKey.MaxMana, 70), (StatKey.ManaRegen, 5)), // mid pool to fuel quick skills
                GrowthPerLevel = SB((StatKey.Hp, 10), (StatKey.Atk, 4), (StatKey.Def, 1), (StatKey.MaxMana, 3)),
                // Known pool (6); first MaxActiveSkills are the starting active bar (see Skills.DefaultLoadout).
                Skills = new List<string> { "shadowstab", "vitalstrike", "bladewhirl", "pinpoint", "quickstep", "lethality" },
                Sprite = "thief",
            };

            // Progression unlocks: you start with just the Warrior; clearing stage 3 adds
            // the Magician, and stage 5 the Thief. (More heroes/classes slot in here as content grows.)
            cfg.HeroUnlocks[3] = "magician_basic";
            cfg.HeroUnlocks[5] = "thief_basic";

            cfg.ItemBases["rusty_sword"] = new ItemBaseDef
            {
                BaseId = "rusty_sword", Slot = EquipSlot.Weapon, BaseStats = SB((StatKey.Atk, 6)),
                AllowedAffixes = new List<StatKey> { StatKey.Atk, StatKey.CritChance, StatKey.CritDmg, StatKey.AtkSpd },
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
            cfg.ItemBases["wooden_shield"] = new ItemBaseDef
            {
                BaseId = "wooden_shield", Slot = EquipSlot.Offhand, BaseStats = SB((StatKey.Def, 4), (StatKey.Hp, 12)),
                AllowedAffixes = new List<StatKey> { StatKey.Hp, StatKey.Def }, Sprite = "shield",
            };
            cfg.ItemBases["leather_gloves"] = new ItemBaseDef
            {
                BaseId = "leather_gloves", Slot = EquipSlot.Gloves, BaseStats = SB((StatKey.Def, 2), (StatKey.Atk, 2)),
                AllowedAffixes = new List<StatKey> { StatKey.Atk, StatKey.Def, StatKey.CritChance, StatKey.AtkSpd }, Sprite = "gloves",
            };
            cfg.ItemBases["leather_boots"] = new ItemBaseDef
            {
                BaseId = "leather_boots", Slot = EquipSlot.Boots, BaseStats = SB((StatKey.Def, 2), (StatKey.MoveSpd, 0.3)),
                AllowedAffixes = new List<StatKey> { StatKey.Def, StatKey.Hp, StatKey.MoveSpd }, Sprite = "boots",
            };
            cfg.ItemBases["linen_cape"] = new ItemBaseDef
            {
                BaseId = "linen_cape", Slot = EquipSlot.Cape, BaseStats = SB((StatKey.Def, 2), (StatKey.Hp, 8)),
                AllowedAffixes = new List<StatKey> { StatKey.Hp, StatKey.Def }, Sprite = "cape",
            };
            cfg.ItemBases["copper_ring"] = new ItemBaseDef
            {
                BaseId = "copper_ring", Slot = EquipSlot.Ring, BaseStats = SB((StatKey.Atk, 3)),
                AllowedAffixes = new List<StatKey> { StatKey.Atk, StatKey.CritChance, StatKey.CritDmg }, Sprite = "ring",
            };
            cfg.ItemBases["bone_amulet"] = new ItemBaseDef
            {
                // accessory that ties into the new mana resource (MaxMana is a base stat;
                // there's no mana affix yet — those arrive with skills).
                BaseId = "bone_amulet", Slot = EquipSlot.Amulet, BaseStats = SB((StatKey.Hp, 8), (StatKey.MaxMana, 15)),
                AllowedAffixes = new List<StatKey> { StatKey.Hp, StatKey.Def }, Sprite = "amulet",
            };

            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Hp, Weight = 30, ValueMinPerItemLevel = 4, ValueMaxPerItemLevel = 8, RarityFloor = Rarity.Magic });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Atk, Weight = 25, ValueMinPerItemLevel = 1, ValueMaxPerItemLevel = 2, RarityFloor = Rarity.Magic });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Def, Weight = 20, ValueMinPerItemLevel = 1, ValueMaxPerItemLevel = 2, RarityFloor = Rarity.Magic });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.AtkSpd, Weight = 8, ValueMinPerItemLevel = 0.01, ValueMaxPerItemLevel = 0.03, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.MoveSpd, Weight = 6, ValueMinPerItemLevel = 0.02, ValueMaxPerItemLevel = 0.05, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.CritChance, Weight = 8, ValueMinPerItemLevel = 0.005, ValueMaxPerItemLevel = 0.015, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.CritDmg, Weight = 9, ValueMinPerItemLevel = 0.03, ValueMaxPerItemLevel = 0.08, RarityFloor = Rarity.Rare });

            cfg.Monsters["slime"] = new MonsterDef
            {
                Id = "slime", Name = "Slime",
                BaseStats = SB((StatKey.Hp, 34), (StatKey.Atk, 3), (StatKey.Def, 0), (StatKey.MoveSpd, 2.6), (StatKey.AtkSpd, 0.8), (StatKey.CritDmg, 1.5)),
                LootTableId = "common", XpReward = 12, GoldReward = 3, Sprite = "slime",
            };
            cfg.Monsters["goblin"] = new MonsterDef
            {
                Id = "goblin", Name = "Goblin",
                BaseStats = SB((StatKey.Hp, 52), (StatKey.Atk, 5), (StatKey.Def, 1), (StatKey.MoveSpd, 3.0), (StatKey.AtkSpd, 1.1), (StatKey.CritChance, 0.03), (StatKey.CritDmg, 1.5)),
                LootTableId = "common", XpReward = 20, GoldReward = 6, Sprite = "goblin",
            };
            cfg.Monsters["goblin_king"] = new MonsterDef
            {
                Id = "goblin_king", Name = "Goblin King",
                BaseStats = SB((StatKey.Hp, 160), (StatKey.Atk, 12), (StatKey.Def, 3), (StatKey.MoveSpd, 2.6), (StatKey.AtkSpd, 0.9), (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.6)),
                LootTableId = "boss", XpReward = 60, GoldReward = 40, Sprite = "goblin_king", SpawnStyle = "rise",
                Skills = new List<string> { "boss_quake" },
            };

            for (int i = 0; i < 100; i++)
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

            // Warrior: melee cleave (AoE) + a self attack buff. Magician: ranged nuke + a heal.
            cfg.Skills["cleave"] = new SkillDef
            {
                Id = "cleave", Name = "Cleave", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 4000, Range = 1.8, AoeRadius = 1.6, DamageMult = 1.6, ManaCost = 15, Sprite = "cleave",
            };
            cfg.Skills["warcry"] = new SkillDef
            {
                Id = "warcry", Name = "War Cry", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 9000, Range = 0, BuffStat = StatKey.Atk, BuffAmount = 10, BuffDurationMs = 6000,
                ManaCost = 20, Sprite = "warcry",
            };
            cfg.Skills["firebolt"] = new SkillDef
            {
                Id = "firebolt", Name = "Firebolt", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 3500, Range = 6.0, DamageMult = 1.8, ManaCost = 20, Sprite = "firebolt",
            };
            cfg.Skills["mend"] = new SkillDef
            {
                Id = "mend", Name = "Mend", Effect = SkillEffectKind.Heal, Targeting = "lowestHp",
                CooldownMs = 7000, Range = 8.0, DamageMult = 2.0, ManaCost = 30, Sprite = "mend",
            };

            // Lever 3 expanded kits (6 known per hero, pick 4). Sprites reuse existing FX for now;
            // bespoke VFX is a later polish pass. All use the existing damage/heal/buff effect kinds.
            // Warrior — single-target burst, big AoE, a defensive cooldown, an attack-speed tempo buff.
            cfg.Skills["bash"] = new SkillDef
            {
                Id = "bash", Name = "Bash", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 3000, Range = 1.4, DamageMult = 2.4, ManaCost = 15, Sprite = "cleave",
            };
            cfg.Skills["whirlwind"] = new SkillDef
            {
                Id = "whirlwind", Name = "Whirlwind", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 6000, Range = 1.8, AoeRadius = 2.6, DamageMult = 1.4, ManaCost = 28, Sprite = "cleave",
            };
            cfg.Skills["bulwark"] = new SkillDef
            {
                Id = "bulwark", Name = "Bulwark", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 12000, Range = 0, BuffStat = StatKey.Def, BuffAmount = 15, BuffDurationMs = 6000,
                ManaCost = 20, Sprite = "warcry",
            };
            cfg.Skills["frenzy"] = new SkillDef
            {
                Id = "frenzy", Name = "Frenzy", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.AtkSpd, BuffAmount = 0.5, BuffDurationMs = 6000,
                ManaCost = 25, Sprite = "warcry",
            };
            // Fire Wizard — AoE fireball, a heavy single nuke, a big AoE ultimate, an attack-speed buff.
            cfg.Skills["fireball"] = new SkillDef
            {
                Id = "fireball", Name = "Fireball", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 5000, Range = 6.0, AoeRadius = 2.2, DamageMult = 1.6, ManaCost = 30, Sprite = "firebolt",
            };
            cfg.Skills["scorch"] = new SkillDef
            {
                Id = "scorch", Name = "Scorch", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 4500, Range = 6.0, DamageMult = 2.6, ManaCost = 28, Sprite = "firebolt",
            };
            cfg.Skills["inferno"] = new SkillDef
            {
                Id = "inferno", Name = "Inferno", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 12000, Range = 6.0, AoeRadius = 3.2, DamageMult = 2.2, ManaCost = 50, Sprite = "quake",
            };
            cfg.Skills["haste"] = new SkillDef
            {
                Id = "haste", Name = "Haste", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.AtkSpd, BuffAmount = 0.6, BuffDurationMs = 6000,
                ManaCost = 25, Sprite = "warcry",
            };

            // Thief — single-target assassin: a fast cheap stab, a heavy nuke, a tight AoE, and
            // three crit/tempo self-buffs (the build choice is which buffs make the 4-slot bar).
            // All on existing damage/buff kinds; sprites reuse warrior/mage FX until bespoke VFX.
            cfg.Skills["shadowstab"] = new SkillDef
            {
                Id = "shadowstab", Name = "Shadowstab", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 2200, Range = 1.4, DamageMult = 2.6, ManaCost = 12, Sprite = "cleave",
            };
            cfg.Skills["vitalstrike"] = new SkillDef
            {
                Id = "vitalstrike", Name = "Vital Strike", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 5000, Range = 1.4, DamageMult = 3.8, ManaCost = 30, Sprite = "cleave",
            };
            cfg.Skills["bladewhirl"] = new SkillDef
            {
                Id = "bladewhirl", Name = "Bladewhirl", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 5500, Range = 2.0, AoeRadius = 2.2, DamageMult = 1.4, ManaCost = 26, Sprite = "cleave",
            };
            cfg.Skills["pinpoint"] = new SkillDef
            {
                Id = "pinpoint", Name = "Pinpoint", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.CritChance, BuffAmount = 0.25, BuffDurationMs = 6000,
                ManaCost = 20, Sprite = "warcry",
            };
            cfg.Skills["quickstep"] = new SkillDef
            {
                Id = "quickstep", Name = "Quickstep", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.AtkSpd, BuffAmount = 0.6, BuffDurationMs = 6000,
                ManaCost = 25, Sprite = "warcry",
            };
            cfg.Skills["lethality"] = new SkillDef
            {
                Id = "lethality", Name = "Lethality", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 12000, Range = 0, BuffStat = StatKey.CritDmg, BuffAmount = 0.6, BuffDurationMs = 6000,
                ManaCost = 25, Sprite = "warcry",
            };

            // Boss signature: a wide quake (free — bosses have no mana pool).
            cfg.Skills["boss_quake"] = new SkillDef
            {
                Id = "boss_quake", Name = "Quake", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 8000, Range = 3.0, AoeRadius = 3.0, DamageMult = 1.4, ManaCost = 0, Sprite = "quake",
            };

            return cfg;
        }
    }
}
