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

    public enum SkillEffectKind { Damage, Heal, Buff, Dash }

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
        // 2+2 template (design §7.2): investing a skill point raises this skill's rank; its primary
        // magnitude (Damage/Heal DamageMult, or Buff BuffAmount) scales by (1 + EffectPerRank*eff)
        // where eff = Skills.EffectiveRank — at MaxRank the skill masters, counting as
        // rank + MasteryBonusRanks (the chunky "push one skill to 5" payoff). Rank 0 = base.
        public int MaxRank = 5;
        public double EffectPerRank = 0.12;   // +12% of the base effect per invested rank
        // A passive skill is an always-on stat node, never cast: each invested rank adds StatPerRank
        // to PassiveStat (mastery-boosted at MaxRank like actives), folded into
        // Stats.ComputeHeroStats (so it flows into the stat sheet, DPS/Eff-Life, and the Lever 2
        // power compare for free). Rank 0 = +0 = unchanged behavior.
        public bool Passive;                  // false = active cast (default); true = passive stat node
        public StatKey PassiveStat;           // Passive: which stat each rank raises
        public double StatPerRank;            // Passive: additive amount of PassiveStat per rank
        // Kit reveal cadence (§7.2): the skill exists (casts / can be ranked) only once the hero
        // reaches UnlockLevel. No prereq trees — the 2+2 kit is flat.
        public int UnlockLevel = 1;           // hero level required before this skill is part of the kit
    }

    /// <summary>
    /// A monster modifier type (the risk/reward knob — Lever 1). Applied to farm trash when the
    /// player toggles it active, and exhibited by a stage's boss (the source you fight + bank).
    /// Effects scale with the banked STRENGTH (= the stage it was earned at): each affected stat
    /// is multiplied by (1 + StatPerStrength*strength); a behavior fraction =
    /// BehaviorPerStrength*strength (clamped to <see cref="BalanceConstants.ModifierBehaviorCap"/>);
    /// the thematic reward bonus fraction = RewardPerStrength*strength. Stronger = bigger buff AND
    /// bigger reward. Stat coefficients use stats every mob has non-zero (Hp/Atk/MoveSpd/AtkSpd)
    /// so multipliers always bite.
    /// </summary>
    public sealed class ModifierDef
    {
        public string Id = "";
        public string Name = "";
        public StatBlock StatPerStrength = new StatBlock(); // per-strength stat-mult coefficients
        public ModifierBehavior Behavior = ModifierBehavior.None;
        public double BehaviorPerStrength;                  // lifesteal/thorns fraction per strength
        public List<RewardPart> Rewards = new List<RewardPart>(); // reward split (one part, or hybrid)
        public double TintR, TintG, TintB;                 // client aura tint (engine-free RGB 0..1)

        // Loot-imprint (mechanical modifiers) — the headline hook. A Mechanical modifier makes the
        // monster fight nastier via a real combat mechanic (e.g. Behavior == Splash), AND a kill can
        // *imprint* that signature onto its drop: a build-defining Affix (ImprintStat) the normal
        // affix pool never rolls, so imprinted gear is obtainable ONLY by farming the dangerous mod.
        // The affix folds into Stats.ComputeHeroStats like any other, so it flows into combat + the
        // DPS/Eff-Life power compare for free. ImprintChance gates the roll per drop; the stamped
        // value = ImprintPerStrength × the modifier's strength. Non-mechanical mods leave these zero.
        public bool Mechanical;
        public StatKey ImprintStat;          // the build stat stamped onto drops (e.g. SplashRadius)
        public double ImprintPerStrength;    // affix value per strength when a drop is imprinted
        public double ImprintChance;         // chance a drop from this mob carries the imprint (0..1)

        // Acquisition gate. 0 = unlocked by FARM DEPTH via ModifierUnlockOrder (the boring stat mods).
        // >0 = TOWER-GATED: owned only once the player has cleared this Tower floor — so a mechanical
        // mod feels like an earned Tower reward (on top of the milestone account buffs) rather than a
        // silent farm tick. Granted in Modifiers.SyncToStage from TowerState.HighestFloor. Rare mods
        // unlock in PAIRS: two of the same ImprintSlot share a TowerUnlockFloor (anti-target-farming —
        // see Modifiers).
        public int TowerUnlockFloor;

        // Which imprint slot a Mechanical (rare) mod occupies — its imprint is a prefix or a suffix on
        // the dropped item, and it counts against that slot's loadout cap. Ignored for normal mods.
        public ImprintSlot ImprintSlot;
    }

    /// <summary>One rung of an achievement ladder: reach <see cref="Threshold"/> on the parent
    /// achievement's metric to claim a one-time reward. Rewards are a milestone BONUS on top of
    /// normal income (chunkier than a goal-board payout, since each mints once for good).</summary>
    public sealed class AchievementTier
    {
        public long Threshold;   // metric value that completes this tier
        public long RewardGold;
        public long RewardScrap;
        public int RewardXp;
    }

    /// <summary>A lifetime achievement (Lever 4): a tiered ladder over one <see cref="AchievementMetric"/>.
    /// Tiers are ascending by threshold; crossing one pays out once (see <see cref="Achievements"/>).
    /// <see cref="Unit"/> is a client label hint ("monsters", "gold", "stage").</summary>
    public sealed class AchievementDef
    {
        public string Id = "";
        public string Name = "";
        public AchievementMetric Metric;
        public string Unit = "";
        public List<AchievementTier> Tiers = new List<AchievementTier>();
    }

    /// <summary>All tunable numbers. The file you edit constantly to balance.</summary>
    public sealed class BalanceConstants
    {
        // Monster modifiers (Lever 1): hard cap on a behavior fraction (lifesteal / thorns reflect)
        // so a very deep modifier can't reach 100%+ sustain/reflect.
        public double ModifierBehaviorCap = 0.6;

        // Loot legibility (Lever 2): a candidate item's power swap (Upgrades.PowerScore) within
        // ±this fraction reads as a Sidegrade, not an up/down-grade — so a 0.1% wiggle doesn't flash
        // a green ▲ and auto-equip doesn't churn on noise. 0.005 = 0.5%.
        public double UpgradeBandPct = 0.005;

        // Daily login (Lever 4 — the premium-currency hook). Claiming once per UTC day grants GEMS
        // (the premium currency, in Currencies[PremiumCurrency]); consecutive days build a streak for
        // bigger rewards, a missed day resets it to 1. Gems are the seed of the future gacha/
        // microtransaction economy and are NOT earnable any other way yet. Reward at streak S =
        // BaseGems + StreakBonus·min(S-1, StreakCap-1), plus MilestoneGems every MilestoneEvery days.
        public string PremiumCurrency = "gems";
        public long DailyLoginBaseGems = 10;       // gems for a day-1 claim
        public long DailyLoginStreakBonus = 2;     // + this per extra streak day, up to the cap
        public int DailyLoginStreakCap = 7;        // streak bonus stops growing past this day (plateaus)
        public long DailyLoginMilestoneGems = 50;  // extra gems every DailyLoginMilestoneEvery-th day
        public int DailyLoginMilestoneEvery = 7;   // a bonus lands on day 7, 14, 21…

        public double IdleCapHours = 12;
        // Offline yield as a fraction of the online rate (gold, XP, and loot rolls
        // alike) — starts at 70% to nudge active play; tune freely later.
        public double OfflineRate = 0.70;
        public int MaxLevel = 100;

        // 2+2 hero template (design §7.2): every hero's kit is exactly 2 actives + 2 passives
        // (HeroDef.Skills), always on once revealed by UnlockLevel — no loadout choice. Skill
        // points arrive 1 per SkillPointsEveryLevels hero levels (derived Level/5, never
        // persisted), each skill caps at its MaxRank (5) ⇒ 20 points at level 100 maxes the kit;
        // the build choice is ORDERING. At MaxRank a skill masters: it counts as
        // rank + MasteryBonusRanks, so focusing a skill to 5 beats spreading evenly.
        public int SkillPointsEveryLevels = 5;
        public int MasteryBonusRanks = 2;

        // Goal ladder: how many short-term goals sit on the rolling board at once (always a
        // few near-term carrots). Targets/rewards scale with highest stage in Quests.cs.
        public int QuestBoardSize = 3;

        // A major boss (every 10th stage) multiplies the stage boss's scaled stats
        // on top of the normal monster-level scaling. NOTE: this scales the boss's
        // DAMAGE as well as HP — sim runs against a real 3-hero save showed 2.5
        // one-shots a frontier party (wipe) while 2.0 gives a ~10-15s fight with
        // headroom under the 30s challenge cap. Tune against that wipe cliff.
        public double MajorBossMult = 2.0;

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

        // Solo party leash: the LEADER (lowest slot) heads for any pack within this distance
        // and pulls the team onto it; followers stay in formation unless an enemy comes within
        // FormationBreakRadius, so the group advances as one and only spreads to fight up close.
        public double EngageRadius = 14;

        // Formation (Solo): the followers hold a triangle behind the leader, facing the pack.
        // Back = how far behind the leader the rank sits; Side = its left/right spread; pairs
        // step back another Back per row. Deadzone = stop fidgeting once roughly in slot.
        // FormationBreakRadius = a follower only abandons formation for an enemy this close.
        public double FormationBack = 1.8;
        public double FormationSide = 1.6;
        public double FormationDeadzone = 0.6;
        public double FormationBreakRadius = 6.0;

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
        public double BossHpMult = 2.0;        // a boss is ~2x a same-stage trash mob (cut again for the 3-hero party cap)

        // Tower of Ascension (alt mode): a ONE-CLEAR-PER-FLOOR track, distinct from the farmable
        // ladder. STEEPER than the ladder on BOTH axes (HP and damage), so it gates on built power
        // and can't be out-leveled by camping; rotating per-floor modifiers add a puzzle layer; and
        // a permanent account-wide buff drops every TowerMilestoneEvery floors. No idle income.
        public int TowerFloors = 30;                 // launch height (vertical slice); extend to 100 later
        public double TowerHpGrowth = 1.50;          // +50%/floor HP (brutal vs ladder 1.18 — a hard DPS check)
        public double TowerDmgGrowth = 1.20;         // +20%/floor atk+def (vs ladder 1.08 — hits hard)
        public int TowerModifierFromFloor = 3;       // floors 1-2 are a gentle ramp; modifiers start here
        public int TowerMilestoneEvery = 10;         // permanent account buff every N floors cleared
        public double TowerMilestoneStatPct = 0.05;  // +5% Hp/Atk/Def (account-wide) per milestone
        public int TowerPackBase = 4;                // trash mobs on floor 1
        public int TowerPackPerFloors = 5;           // +1 mob every N floors (a slowly thickening pack)

        // Modifiers (Lever 1, the risk/reward farm knob) — acquisition + upgrade are driven by FARM
        // DEPTH (highest stage reached), not hero level: you unlock a new modifier every
        // ModifierNewEveryStages and ALL owned modifiers gain +1 strength every ModifierUpgradeEvery
        // stages. The player slots up to MaxActiveModifiers of them as an account-wide loadout.
        public int ModifierNewEveryStages = 10;      // unlock the next modifier in ModifierUnlockOrder every N stages
        public int ModifierUpgradeEveryStages = 5;   // +1 strength to ALL owned modifiers every N stages
        public int MaxActiveModifiers = 3;           // NORMAL-pool loadout cap — how many boring stat mods at once

        // Rare (mechanical, imprint-bearing) mods are a SEPARATE loadout from the normal pool, capped
        // PER SLOT: up to MaxActiveRarePerSlot prefixes + that many suffixes. Anti-target-farming: a
        // rare slot only APPLIES (and can imprint) when ≥ MinActiveRarePerSlot of it are active — so a
        // drop is always randomized across ≥2 possible imprints, never one you can target-farm. With
        // both at 2, a slot is effectively all-or-nothing (run both, or neither). See Modifiers.
        public int MaxActiveRarePerSlot = 2;
        public int MinActiveRarePerSlot = 2;

        // Modifier shop: spend gold+scrap to gamble a mod's tuning (a multiplier on BOTH its danger AND
        // reward, floored at 1.0). Each upgrade rolls a delta in [RollMin, RollMax] onto the tuning; the
        // cost scales with the mod's current tuning (tuning^CostExp) as a soft cap. Tunables.
        public long ModShopBaseGold = 2000;
        public long ModShopBaseScrap = 30;
        public double ModShopCostExp = 4.0;      // gold+scrap cost ≈ base × tuning^4
        public double ModShopRollMin = -0.05;    // −5%
        public double ModShopRollMax = 0.05;     // +5% (symmetric; floored at 1.0 so it can't drop below base)

        // Reforge (item shop): the SAME gamble verb pointed at gear — spend gold+scrap to re-roll an
        // item's normal affix values by ModShopRollMin/Max, clamped to each affix's legit [min,max] for
        // its item level (imprint affixes are left untouched). Cost scales with item level + rarity.
        public long ReforgeBaseGold = 500;
        public long ReforgeBaseScrap = 10;

        // Chaining (rare prefix): after a basic hit, the strike arcs to nearby enemies (or party
        // members, for a Chaining monster). ChainRange is the per-jump reach — kept moderate so it
        // feels like an arc, not a screen-wide zap. ChainCount (the StatKey) is floored to an int and
        // clamped to MaxChainJumps.
        public double ChainRange = 3.0;
        public int MaxChainJumps = 3;

        // Pack variety (Lever 1): per-mob chance, rolled at farm spawn, to promote ordinary
        // trash to a highlighted, tougher rank with a boosted loot bundle. Rare is checked
        // first, then Elite; the rest stay Normal. Stat mults make them a real wall to chew
        // through (the spike), reward/drop mults make killing one feel worth it (the payoff).
        public double RareChance = 0.025;       // ~1 in 40 mobs is a yellow-tier Rare
        public double EliteChance = 0.10;        // ~1 in 10 is a blue-tier Elite
        public double EliteHpMult = 3.0,  EliteAtkMult = 1.4;
        public double RareHpMult  = 6.5,  RareAtkMult  = 1.8;
        public double EliteRewardMult = 4.0,  RareRewardMult = 10.0; // xp + gold
        public double EliteBodyMult = 1.35, RareBodyMult = 1.7;       // chunkier bodies (also a visual tell)
        // Rank loot bundles: a count of guaranteed items at a boosted DropRateMult (richer
        // rarity), still capped at the trash ceiling (Rare) — Unique+ stays boss-only.
        public int EliteDropCount = 2,  RareDropCount = 4;
        public double EliteDropRateMult = 2.5, RareDropRateMult = 5.0;

        // Base drop weights per rarity, indexed by (int)Rarity ascending:
        // [Normal, Rare, Unique, Legendary, Mythic]. Must have one entry per Rarity.
        // Clean ×5 geometric ramp; Mythic is the extreme chase (~0.08% even uncapped).
        // The stage's DropRateMult biases this upward — see Loot.RollRarity.
        public double[] RarityBaseWeights = { 1000, 200, 40, 8, 1 };

        // Affix count (min, max) per rarity, indexed by (int)Rarity ascending:
        // [Normal, Rare, Unique, Legendary, Mythic]. Rare merges the old Magic+Rare
        // farm tiers (2-3); the boss tiers step up from there, Mythic the most.
        // Counts cap at the eligible affix-pool size for the item base — see Loot.RollAffixes.
        public (int min, int max)[] AffixCountByRarity = { (0, 0), (2, 3), (4, 5), (5, 6), (6, 7) };

        // Per-kill chance a common monster drops an item. Tuned for a steady loot "rain"
        // (PoE/Maple cadence): most drops get auto-salvaged to scrap (a number that keeps
        // climbing), with the occasional keeper. PRIMARY loot-cadence dial. Trash drops are
        // capped at Rare (TrashRarityCap); Unique+ comes only from boss bundles below.
        public double DropChance = 0.12;

        // Highest rarity a common/trash/idle drop can roll. Unique/Legendary/Mythic are
        // boss-exclusive (guaranteed bundles), so the open-world ceiling is Rare.
        public Rarity TrashRarityCap = Rarity.Rare;

        // Boss guaranteed loot (PoE-style chase items). Each boss drops a bundle of
        // Unique+ items — count by boss tier — plus a few ordinary extras. Per bundle
        // item (one rng draw): BossMythicChance => Mythic, else BossLegendaryChance =>
        // Legendary, otherwise Unique. Mythic is the extreme long-tail chase.
        public double BossMythicChance = 0.002;
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
        // [Normal, Rare, Unique, Legendary, Mythic]. Plus the item's level. Tune later.
        public long[] ScrapValueByRarity = { 1, 5, 20, 50, 150 };
        public long ScrapValue(Rarity rarity, int itemLevel)
            => ScrapValueByRarity[(int)rarity] + Math.Max(0, itemLevel);

        // Hero leveling is a LONG-HAUL chase — level 100 is meant to take months of farming, not days.
        // XpCurve(level) is the XP from `level` to `level+1`, geometric. The curve stays gentle through
        // the early skill-unlock levels (~5–18) then compounds hard, so the back half is the grind.
        // (Xp is stored as long, so the deep levels reaching into the billions are safe.) Tune freely.
        public double XpBaseCost = 600;   // XP for level 1→2
        public double XpGrowth = 1.19;    // per-level multiplier (was 1.15 — ~140× more total XP to 100)
        public long XpCurve(int level) => (long)Math.Floor(XpBaseCost * Math.Pow(XpGrowth, level - 1));

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
        // Monster modifier catalog + the order they cycle across stages (each stage's boss owns
        // ModifierCycle[(stage-1) % count] — see ModifierTypeForStage). Lever 1.
        public Dictionary<string, ModifierDef> Modifiers = new Dictionary<string, ModifierDef>();
        public List<string> ModifierCycle = new List<string>();
        // The order modifiers UNLOCK as farm depth grows (one per ModifierNewEveryStages). Boring
        // income mods first, the spicier behavioral ones later; mechanical/loot-imprint mods append here.
        public List<string> ModifierUnlockOrder = new List<string>();
        // Lifetime achievement ladder (Lever 4 — the permanent milestone hooks). See Achievements.cs.
        public List<AchievementDef> Achievements = new List<AchievementDef>();
        public BalanceConstants Balance = new BalanceConstants();

        /// <summary>The modifier type a stage's boss exhibits (and grants on a kill). Cycles the
        /// curated <see cref="ModifierCycle"/> so deeper bosses re-grant types at higher strength.
        /// null if no modifiers are defined.</summary>
        public string? ModifierTypeForStage(int stage)
        {
            if (ModifierCycle.Count == 0) return null;
            int i = ((stage - 1) % ModifierCycle.Count + ModifierCycle.Count) % ModifierCycle.Count;
            return ModifierCycle[i];
        }

        /// <summary>The modifier a tower FLOOR exhibits — its puzzle layer. Gentle ramp: floors below
        /// <see cref="BalanceConstants.TowerModifierFromFloor"/> have none; deeper floors cycle the
        /// curated <see cref="ModifierCycle"/>. null when no modifiers exist or the floor is in the ramp.</summary>
        public string? TowerModifierForFloor(int floor)
        {
            if (ModifierCycle.Count == 0 || floor < Balance.TowerModifierFromFloor) return null;
            int i = ((floor - 1) % ModifierCycle.Count + ModifierCycle.Count) % ModifierCycle.Count;
            return ModifierCycle[i];
        }

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

        // Reward split for a modifier — one part, or a hybrid (e.g. RW((Gold,0.04),(DropRate,0.04))).
        private static List<RewardPart> RW(params (ModifierReward channel, double per)[] parts)
        {
            var list = new List<RewardPart>(parts.Length);
            foreach (var (channel, per) in parts) list.Add(new RewardPart { Channel = channel, PerStrength = per });
            return list;
        }

        // One achievement tier (threshold + gold/scrap/XP reward), and an achievement (tiers ascending).
        private static AchievementTier AT(long threshold, long gold, long scrap, int xp)
            => new AchievementTier { Threshold = threshold, RewardGold = gold, RewardScrap = scrap, RewardXp = xp };
        private static AchievementDef Ach(string id, string name, AchievementMetric metric, string unit, params AchievementTier[] tiers)
            => new AchievementDef { Id = id, Name = name, Metric = metric, Unit = unit, Tiers = new List<AchievementTier>(tiers) };

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
                // 2+2 kit (§7.2): spinning AoE + a shield-charge gap closer; armor + health passives.
                Skills = new List<string> { "cycloneslash", "toughness", "shieldcharge", "vitality" }, Sprite = "warrior",
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
                // 2+2 kit (§7.2): fire nuke + AoE fireball; spell-power + mana passives.
                Skills = new List<string> { "firebolt", "pyromancy", "fireball", "attunement" }, Sprite = "magician", AttackFx = "fireball",
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
                // 2+2 kit (§7.2): fast stab + heavy vital strike; crit-chance + crit-damage passives.
                Skills = new List<string> { "shadowstab", "precision", "vitalstrike", "killerinstinct" },
                Sprite = "thief",
            };

            // Hero #4 — the first hero authored ON the 2+2 template (§7.2): a durable frost
            // caster. Sturdier but slower than the fire Magician; kit params are dummy ice
            // flavor for now (bespoke frost mechanics/VFX are a later pass).
            cfg.Heroes["icemage_basic"] = new HeroDef
            {
                DefId = "icemage_basic", Name = "Ice Mage", Class = "Ice Mage", Role = "ranged",
                BaseStats = SB((StatKey.Hp, 82), (StatKey.Atk, 15), (StatKey.Def, 6),
                               (StatKey.MoveSpd, 3.0), (StatKey.AtkSpd, 1.0),  // slower caster than the fire mage
                               (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.5),
                               (StatKey.HpRegen, 1.2),
                               (StatKey.AttackRange, 6.0),
                               (StatKey.SplashRadius, 0.8),
                               (StatKey.MaxMana, 130), (StatKey.ManaRegen, 7)), // deepest pool of the roster
                GrowthPerLevel = SB((StatKey.Hp, 13), (StatKey.Atk, 3.5), (StatKey.Def, 1.3), (StatKey.MaxMana, 6)),
                // 2+2 kit (§7.2): frost nuke + AoE blizzard; armor + mana-flow passives.
                Skills = new List<string> { "frostbolt", "permafrost", "blizzard", "frostflow" }, Sprite = "icemage",
            };

            // Hero #5 — the party's first SUPPORT (and first male-body hero): a holy caster
            // whose identity is the party heal-over-time, with a modest AoE smite for
            // downtime. Low Atk (his power is the heal, which scales off MaxHp, not Atk).
            cfg.Heroes["priest_basic"] = new HeroDef
            {
                DefId = "priest_basic", Name = "Priest", Class = "Priest", Role = "ranged",
                BaseStats = SB((StatKey.Hp, 90), (StatKey.Atk, 12), (StatKey.Def, 5),
                               (StatKey.MoveSpd, 3.0), (StatKey.AtkSpd, 0.95),
                               (StatKey.CritChance, 0.04), (StatKey.CritDmg, 1.5),
                               (StatKey.HpRegen, 1.2),
                               (StatKey.AttackRange, 6.0),
                               (StatKey.SplashRadius, 0.7),
                               (StatKey.MaxMana, 140), (StatKey.ManaRegen, 7)),
                GrowthPerLevel = SB((StatKey.Hp, 12), (StatKey.Atk, 3), (StatKey.Def, 1.2), (StatKey.MaxMana, 6)),
                // 2+2 kit (§7.2): party HoT + AoE smite; sustain + mana-flow passives.
                Skills = new List<string> { "sanctify", "devotion", "holysmite", "benediction" },
                Sprite = "priest", AttackFx = "holybolt",
            };

            // Progression unlocks: you start with just the Warrior; clearing stage 3 adds
            // the Magician, stage 5 the Thief, stage 8 the Ice Mage, stage 12 the Priest.
            // (More heroes slot in here.)
            cfg.HeroUnlocks[3] = "magician_basic";
            cfg.HeroUnlocks[5] = "thief_basic";
            cfg.HeroUnlocks[8] = "icemage_basic";
            cfg.HeroUnlocks[12] = "priest_basic";

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

            // All floors sit at Rare (Normal rolls nothing, Rare+ rolls everything) — the
            // old Magic-tier "basic stats only" split left with the Magic rarity itself.
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Hp, Weight = 30, ValueMinPerItemLevel = 4, ValueMaxPerItemLevel = 8, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Atk, Weight = 25, ValueMinPerItemLevel = 1, ValueMaxPerItemLevel = 2, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Def, Weight = 20, ValueMinPerItemLevel = 1, ValueMaxPerItemLevel = 2, RarityFloor = Rarity.Rare });
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

            // Warrior actives: a spinning AoE slash + a shield-charge gap closer.
            // (cleave/warcry retired 2026-07-02 — Save.Migrate transfers invested ranks.)
            cfg.Skills["cycloneslash"] = new SkillDef
            {
                Id = "cycloneslash", Name = "Cyclone Slash", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 5000, Range = 1.8, AoeRadius = 2.2, DamageMult = 1.5, ManaCost = 18, Sprite = "cleave",
            };
            cfg.Skills["shieldcharge"] = new SkillDef
            {
                Id = "shieldcharge", Name = "Shield Charge", Effect = SkillEffectKind.Dash, Targeting = "nearest",
                CooldownMs = 8000, Range = 6.0, DamageMult = 2.0, ManaCost = 20, Sprite = "charge",
                UnlockLevel = 10,
            };
            // Library rows (no kit uses these today — §7.2 keeps the archetype shelf stocked).
            cfg.Skills["cleave"] = new SkillDef
            {
                Id = "cleave", Name = "Cleave", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 4000, Range = 1.8, AoeRadius = 1.6, DamageMult = 1.6, ManaCost = 15, Sprite = "cleave",
            };
            cfg.Skills["warcry"] = new SkillDef
            {
                Id = "warcry", Name = "War Cry", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 9000, Range = 0, BuffStat = StatKey.Atk, BuffAmount = 10, BuffDurationMs = 6000,
                ManaCost = 20, Sprite = "warcry", UnlockLevel = 10,
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
                UnlockLevel = 8,
            };
            cfg.Skills["bulwark"] = new SkillDef
            {
                Id = "bulwark", Name = "Bulwark", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 12000, Range = 0, BuffStat = StatKey.Def, BuffAmount = 15, BuffDurationMs = 6000,
                ManaCost = 20, Sprite = "warcry", UnlockLevel = 14,
            };
            cfg.Skills["frenzy"] = new SkillDef
            {
                Id = "frenzy", Name = "Frenzy", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.AtkSpd, BuffAmount = 0.5, BuffDurationMs = 6000,
                ManaCost = 25, Sprite = "warcry", UnlockLevel = 18,
            };
            // Fire Wizard — AoE fireball, a heavy single nuke, a big AoE ultimate, an attack-speed buff.
            cfg.Skills["fireball"] = new SkillDef
            {
                Id = "fireball", Name = "Fireball", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 5000, Range = 6.0, AoeRadius = 2.2, DamageMult = 1.6, ManaCost = 30, Sprite = "firebolt",
                UnlockLevel = 10,
            };
            cfg.Skills["scorch"] = new SkillDef
            {
                Id = "scorch", Name = "Scorch", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 4500, Range = 6.0, DamageMult = 2.6, ManaCost = 28, Sprite = "firebolt",
                UnlockLevel = 8,
            };
            cfg.Skills["inferno"] = new SkillDef
            {
                Id = "inferno", Name = "Inferno", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 12000, Range = 6.0, AoeRadius = 3.2, DamageMult = 2.2, ManaCost = 50, Sprite = "quake",
                UnlockLevel = 16,
            };
            cfg.Skills["haste"] = new SkillDef
            {
                Id = "haste", Name = "Haste", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.AtkSpd, BuffAmount = 0.6, BuffDurationMs = 6000,
                ManaCost = 25, Sprite = "warcry", UnlockLevel = 12,
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
                UnlockLevel = 10,
            };
            cfg.Skills["bladewhirl"] = new SkillDef
            {
                Id = "bladewhirl", Name = "Bladewhirl", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 5500, Range = 2.0, AoeRadius = 2.2, DamageMult = 1.4, ManaCost = 26, Sprite = "cleave",
                UnlockLevel = 8,
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
                ManaCost = 25, Sprite = "warcry", UnlockLevel = 10,
            };
            cfg.Skills["lethality"] = new SkillDef
            {
                Id = "lethality", Name = "Lethality", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 12000, Range = 0, BuffStat = StatKey.CritDmg, BuffAmount = 0.6, BuffDurationMs = 6000,
                ManaCost = 25, Sprite = "warcry", UnlockLevel = 16,
            };

            // Passive nodes (Lever 3 slice 2): always-on, never cast — invest points to rank them and
            // they add StatPerRank·rank to PassiveStat via Stats.ComputeHeroStats (flowing into the
            // stat sheet + DPS/Eff-Life + Lever 2 power compare). Two per class, themed to its
            // identity. StatPerRank values are gentle starting points (tuned by feel later); at the
            // default MaxRank=5 each tops out at ~a few levels' worth of growth. Rank 0 = +0.
            cfg.Skills["toughness"] = new SkillDef   // Warrior: stack armor
            {
                Id = "toughness", Name = "Toughness", Passive = true, UnlockLevel = 5,
                PassiveStat = StatKey.Def, StatPerRank = 2.0, Sprite = "bulwark",
            };
            cfg.Skills["vitality"] = new SkillDef    // Warrior: deeper health pool
            {
                Id = "vitality", Name = "Vitality", Passive = true, UnlockLevel = 15,
                PassiveStat = StatKey.Hp, StatPerRank = 12.0, Sprite = "bulwark",
            };
            cfg.Skills["pyromancy"] = new SkillDef    // Magician: raw spell power
            {
                Id = "pyromancy", Name = "Pyromancy", Passive = true, UnlockLevel = 5,
                PassiveStat = StatKey.Atk, StatPerRank = 2.0, Sprite = "fireball",
            };
            cfg.Skills["attunement"] = new SkillDef   // Magician: bigger mana pool
            {
                Id = "attunement", Name = "Attunement", Passive = true, UnlockLevel = 15,
                PassiveStat = StatKey.MaxMana, StatPerRank = 10.0, Sprite = "fireball",
            };
            cfg.Skills["precision"] = new SkillDef     // Thief: more crits
            {
                Id = "precision", Name = "Deadly Precision", Passive = true, UnlockLevel = 5,
                PassiveStat = StatKey.CritChance, StatPerRank = 0.02, Sprite = "warcry",
            };
            cfg.Skills["killerinstinct"] = new SkillDef // Thief: harder crits
            {
                Id = "killerinstinct", Name = "Killer Instinct", Passive = true, UnlockLevel = 15,
                PassiveStat = StatKey.CritDmg, StatPerRank = 0.08, Sprite = "warcry",
            };

            // Ice Mage kit (§7.2 cadence 1/5/10/15) — dummy ice flavor on existing archetypes:
            // a slow heavy nuke, a wide AoE, armor + mana-flow passives. Sprites reuse existing
            // cast FX until a frost VFX pass.
            cfg.Skills["frostbolt"] = new SkillDef
            {
                Id = "frostbolt", Name = "Frostbolt", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 4200, Range = 6.0, DamageMult = 2.2, ManaCost = 24, Sprite = "firebolt",
            };
            cfg.Skills["permafrost"] = new SkillDef  // Ice Mage: rimed armor
            {
                Id = "permafrost", Name = "Permafrost", Passive = true, UnlockLevel = 5,
                PassiveStat = StatKey.Def, StatPerRank = 1.5, Sprite = "bulwark",
            };
            cfg.Skills["blizzard"] = new SkillDef
            {
                Id = "blizzard", Name = "Blizzard", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 6500, Range = 6.0, AoeRadius = 2.6, DamageMult = 1.5, ManaCost = 34, Sprite = "quake",
                UnlockLevel = 10,
            };
            cfg.Skills["frostflow"] = new SkillDef   // Ice Mage: glacial mana current
            {
                Id = "frostflow", Name = "Frostflow", Passive = true, UnlockLevel = 15,
                PassiveStat = StatKey.ManaRegen, StatPerRank = 0.8, Sprite = "fireball",
            };

            // Priest kit (§7.2 cadence 1/5/10/15) — the party HoT is the identity: every
            // living ally regens BuffAmount x MaxHp per second for the duration (rank
            // scales the rate). Gated in Combat.TryCastSkill on someone being hurt.
            cfg.Skills["sanctify"] = new SkillDef
            {
                Id = "sanctify", Name = "Sanctify", Effect = SkillEffectKind.Buff, Targeting = "party",
                CooldownMs = 15000, Range = 0, BuffStat = StatKey.HpRegenPct, BuffAmount = 0.20,
                BuffDurationMs = 10000, ManaCost = 45, Sprite = "warcry",
            };
            cfg.Skills["devotion"] = new SkillDef    // Priest: enduring body
            {
                Id = "devotion", Name = "Devotion", Passive = true, UnlockLevel = 5,
                PassiveStat = StatKey.HpRegen, StatPerRank = 0.6, Sprite = "bulwark",
            };
            cfg.Skills["holysmite"] = new SkillDef
            {
                Id = "holysmite", Name = "Holy Smite", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 6000, Range = 6.0, AoeRadius = 2.4, DamageMult = 1.6, ManaCost = 32, Sprite = "quake",
                UnlockLevel = 10,
            };
            cfg.Skills["benediction"] = new SkillDef // Priest: flowing grace
            {
                Id = "benediction", Name = "Benediction", Passive = true, UnlockLevel = 15,
                PassiveStat = StatKey.ManaRegen, StatPerRank = 0.8, Sprite = "fireball",
            };

            // Boss signature: a wide quake (free — bosses have no mana pool).
            cfg.Skills["boss_quake"] = new SkillDef
            {
                Id = "boss_quake", Name = "Quake", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 8000, Range = 3.0, AoeRadius = 3.0, DamageMult = 1.4, ManaCost = 0, Sprite = "quake",
            };

            // Monster modifiers (Lever 1) — the player-controlled risk/reward knob. Each stage's
            // boss exhibits one of these (cycled below) and grants it on a kill at strength = stage;
            // toggle owned ones onto farm trash for harder mobs + a thematic reward. Coefficients
            // are gentle starting points (tuned by feel later). Two carry real per-hit behaviors
            // (Vampiric lifesteal, Thorns reflect); Swift/Armored are stat-only.
            cfg.Modifiers["vampiric"] = new ModifierDef
            {
                Id = "vampiric", Name = "Vampiric",
                StatPerStrength = SB((StatKey.Hp, 0.05), (StatKey.Atk, 0.02)),
                Behavior = ModifierBehavior.Vampiric, BehaviorPerStrength = 0.010,
                Rewards = RW((ModifierReward.Gold, 0.04), (ModifierReward.Xp, 0.04)), // hybrid: gold + XP
                TintR = 0.85, TintG = 0.20, TintB = 0.20, // blood red
            };
            cfg.Modifiers["swift"] = new ModifierDef
            {
                Id = "swift", Name = "Swift",
                StatPerStrength = SB((StatKey.MoveSpd, 0.05), (StatKey.AtkSpd, 0.04)),
                Behavior = ModifierBehavior.None,
                Rewards = RW((ModifierReward.Xp, 0.04), (ModifierReward.DropRate, 0.04)), // hybrid: XP + drop
                TintR = 0.95, TintG = 0.85, TintB = 0.20, // yellow
            };
            cfg.Modifiers["armored"] = new ModifierDef
            {
                Id = "armored", Name = "Armored",
                StatPerStrength = SB((StatKey.Hp, 0.12)),       // pure tank (big HP)
                Behavior = ModifierBehavior.None,
                Rewards = RW((ModifierReward.DropRate, 0.05)),
                TintR = 0.60, TintG = 0.70, TintB = 0.85, // steel blue
            };
            cfg.Modifiers["thorns"] = new ModifierDef
            {
                Id = "thorns", Name = "Thorns",
                StatPerStrength = SB((StatKey.Hp, 0.05), (StatKey.Atk, 0.02)),
                Behavior = ModifierBehavior.Thorns, BehaviorPerStrength = 0.008,
                Rewards = RW((ModifierReward.Gold, 0.035), (ModifierReward.DropRate, 0.035)), // hybrid: gold + drop
                TintR = 0.90, TintG = 0.50, TintB = 0.15, // orange
            };
            // "Boring" early modifiers (Lever 1 skeleton): a small monster-HP bump (the risk) for a
            // clean income reward (the upside). Same ModifierDef shape as the behavioral ones, no new
            // mechanics — they just front-load the unlock order before the spicier types.
            cfg.Modifiers["prosperous"] = new ModifierDef
            {
                Id = "prosperous", Name = "Prosperous",
                StatPerStrength = SB((StatKey.Hp, 0.06)),
                Rewards = RW((ModifierReward.Gold, 0.10)),
                TintR = 0.95, TintG = 0.82, TintB = 0.30, // gold
            };
            cfg.Modifiers["studious"] = new ModifierDef
            {
                Id = "studious", Name = "Studious",
                StatPerStrength = SB((StatKey.Hp, 0.06)),
                Rewards = RW((ModifierReward.Xp, 0.10)),
                TintR = 0.45, TintG = 0.80, TintB = 0.95, // cyan
            };
            cfg.Modifiers["bountiful"] = new ModifierDef
            {
                Id = "bountiful", Name = "Bountiful",
                StatPerStrength = SB((StatKey.Hp, 0.08)),
                Rewards = RW((ModifierReward.DropRate, 0.06)),
                TintR = 0.55, TintG = 0.85, TintB = 0.45, // green
            };
            // Mechanical / loot-imprint modifier (the headline hook), TOWER-GATED at floor 10 — an
            // earned reward, not a farm-depth tick. Volatile mobs' attacks SPLASH onto the whole party
            // (BehaviorPerStrength = splash radius per strength, in tiles — see Combat.ApplyModifier);
            // killing them can imprint a +SplashRadius affix onto the drop, so YOUR attacks splash too.
            // SplashRadius is in no base's AllowedAffixes, so this gear is obtainable ONLY by farming
            // Volatile. Modest HP bump is the risk; DropRate is the upside. NOT in ModifierUnlockOrder.
            cfg.Modifiers["volatile"] = new ModifierDef
            {
                Id = "volatile", Name = "Volatile",
                // Premium mod, premium tradeoff: these mobs are far nastier than the boring stat mods —
                // tanky AND they hit hard, and the hit SPLASHES the whole party. The danger matches the
                // imprint (your gear's attacks splash wider in return).
                StatPerStrength = SB((StatKey.Hp, 0.12), (StatKey.Atk, 0.10)),
                Behavior = ModifierBehavior.Splash, BehaviorPerStrength = 0.25, // +0.25 tiles splash / strength
                Rewards = RW((ModifierReward.DropRate, 0.06)),
                Mechanical = true, ImprintSlot = ImprintSlot.Prefix,
                // Imprinted gear is EXTREMELY rare — a lucky stamp on a Volatile kill, not a reliable farm.
                ImprintStat = StatKey.SplashRadius, ImprintPerStrength = 0.12, ImprintChance = 0.03,
                TowerUnlockFloor = 5, // PREFIX pair with Chaining (same floor) — unlock together
                TintR = 0.80, TintG = 0.35, TintB = 0.95, // arcane violet
            };
            // Chaining (rare PREFIX) — Volatile's pair, unlocked together at floor 5. The mob's hits arc
            // to a nearby party member (Behavior=Chain grants an additive ChainCount, floored in combat);
            // the imprint stamps +ChainCount onto YOUR gear so your attacks chain to extra enemies.
            cfg.Modifiers["chaining"] = new ModifierDef
            {
                Id = "chaining", Name = "Chaining",
                StatPerStrength = SB((StatKey.Hp, 0.12), (StatKey.Atk, 0.10)),
                Behavior = ModifierBehavior.Chain, BehaviorPerStrength = 0.34, // mob chain jumps = floor(0.34·str)
                Rewards = RW((ModifierReward.Xp, 0.06)),
                Mechanical = true, ImprintSlot = ImprintSlot.Prefix,
                ImprintStat = StatKey.ChainCount, ImprintPerStrength = 0.34, ImprintChance = 0.03,
                TowerUnlockFloor = 5,
                TintR = 0.35, TintG = 0.75, TintB = 0.95, // electric cyan
            };
            // Rare SUFFIX pair — of Leeching / of Thorns (unlock together at floor 10). The monster side
            // reuses the existing Vampiric/Thorns per-hit behaviors; the imprint stamps the matching
            // EXCLUSIVE stat onto YOUR gear (Lifesteal / ThornsReflect), read in Combat.ApplyHit so a
            // hero leeches/reflects just like a modded monster. Premium tradeoff: these mobs are tanky
            // AND hit hard on top of the sustain/punish behavior.
            cfg.Modifiers["leeching"] = new ModifierDef
            {
                Id = "leeching", Name = "Leeching",
                StatPerStrength = SB((StatKey.Hp, 0.12), (StatKey.Atk, 0.10)),
                Behavior = ModifierBehavior.Vampiric, BehaviorPerStrength = 0.020, // mob lifesteal
                Rewards = RW((ModifierReward.Gold, 0.06)),
                Mechanical = true, ImprintSlot = ImprintSlot.Suffix,
                ImprintStat = StatKey.Lifesteal, ImprintPerStrength = 0.010, ImprintChance = 0.03,
                TowerUnlockFloor = 10,
                TintR = 0.85, TintG = 0.20, TintB = 0.45, // crimson
            };
            cfg.Modifiers["barbed"] = new ModifierDef
            {
                Id = "barbed", Name = "Thorns",
                StatPerStrength = SB((StatKey.Hp, 0.12), (StatKey.Atk, 0.10)),
                Behavior = ModifierBehavior.Thorns, BehaviorPerStrength = 0.015, // mob reflect
                Rewards = RW((ModifierReward.DropRate, 0.06)),
                Mechanical = true, ImprintSlot = ImprintSlot.Suffix,
                ImprintStat = StatKey.ThornsReflect, ImprintPerStrength = 0.010, ImprintChance = 0.03,
                TowerUnlockFloor = 10,
                TintR = 0.95, TintG = 0.55, TintB = 0.20, // amber
            };

            // Stage→type cycle: boss at stage 1=Vampiric, 2=Swift, 3=Armored, 4=Thorns, 5=Vampiric…
            // so all types are reachable early and re-clears bank stronger versions.
            cfg.ModifierCycle = new List<string> { "vampiric", "swift", "armored", "thorns" };
            // Unlock order as farm depth grows (Modifiers.SyncToStage): boring income mods first, then
            // the behavioral types. With ModifierNewEveryStages=10, that's one unlock per 10 stages.
            // Farm-depth unlocks only (the boring stat mods). Mechanical mods like "volatile" are
            // TOWER-GATED (ModifierDef.TowerUnlockFloor) and deliberately NOT listed here.
            cfg.ModifierUnlockOrder = new List<string>
            {
                "prosperous", "studious", "bountiful", "armored", "swift", "vampiric", "thorns",
            };

            // Achievements (Lever 4) — the permanent milestone ladder that spans the whole game, fed
            // by the same events as the goal board. Each tier mints ONCE (a chunky bonus, not income);
            // thresholds climb geometrically so there's always a next rung, and the deep tiers are a
            // months-out chase alongside the level-100 grind. Tuned by feel (see docs).
            cfg.Achievements.Add(Ach("slayer", "Monster Slayer", AchievementMetric.MonstersKilled, "monsters",
                AT(100, 500, 20, 200), AT(1_000, 5_000, 100, 2_000), AT(10_000, 50_000, 500, 20_000),
                AT(100_000, 500_000, 3_000, 200_000), AT(1_000_000, 5_000_000, 15_000, 2_000_000)));
            cfg.Achievements.Add(Ach("boss_hunter", "Boss Hunter", AchievementMetric.BossesKilled, "bosses",
                AT(10, 1_000, 30, 500), AT(50, 8_000, 150, 4_000), AT(250, 60_000, 800, 30_000),
                AT(1_000, 400_000, 4_000, 200_000)));
            cfg.Achievements.Add(Ach("salvager", "Salvager", AchievementMetric.ItemsSalvaged, "items",
                AT(100, 400, 40, 150), AT(1_000, 4_000, 300, 1_500), AT(10_000, 40_000, 2_000, 15_000),
                AT(100_000, 400_000, 12_000, 150_000)));
            cfg.Achievements.Add(Ach("tycoon", "Tycoon", AchievementMetric.GoldEarned, "gold",
                AT(10_000, 1_000, 50, 1_000), AT(100_000, 8_000, 300, 8_000), AT(1_000_000, 60_000, 1_500, 60_000),
                AT(100_000_000, 4_000_000, 20_000, 2_000_000)));
            cfg.Achievements.Add(Ach("collector", "Collector", AchievementMetric.RarePlusFound, "rare+ items",
                AT(25, 1_000, 50, 500), AT(100, 6_000, 200, 3_000), AT(500, 40_000, 1_000, 20_000),
                AT(2_500, 300_000, 6_000, 150_000)));
            cfg.Achievements.Add(Ach("explorer", "Explorer", AchievementMetric.HighestStage, "stage",
                AT(10, 2_000, 50, 1_000), AT(25, 15_000, 300, 8_000), AT(50, 120_000, 1_500, 60_000),
                AT(75, 800_000, 8_000, 400_000), AT(100, 6_000_000, 40_000, 3_000_000)));
            cfg.Achievements.Add(Ach("ascendant", "Ascendant", AchievementMetric.HighestTowerFloor, "floor",
                AT(5, 3_000, 80, 1_500), AT(10, 20_000, 400, 10_000), AT(20, 150_000, 2_000, 80_000),
                AT(30, 1_000_000, 10_000, 500_000)));
            cfg.Achievements.Add(Ach("veteran", "Veteran", AchievementMetric.HeroLevel, "level",
                AT(10, 2_000, 60, 0), AT(25, 15_000, 300, 0), AT(50, 120_000, 1_500, 0),
                AT(75, 800_000, 8_000, 0), AT(100, 6_000_000, 40_000, 0)));

            return cfg;
        }
    }
}
