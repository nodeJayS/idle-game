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

    /// <summary>
    /// A hero FAMILY — the content backbone (design decision 2026-07-02). Two axes,
    /// never merged: Archetype answers "who is this hero's family?" (the stat template
    /// new classes copy from, the shared passive pool, gacha/roster grouping, which
    /// MS2 animation library the art draws on); HeroDef.Role answers "how does the sim
    /// fight them?" (melee/ranged/support). An Archer is a Rogue with Role=ranged.
    /// Three families — Warrior, Rogue, Magician; add a fourth only when a class
    /// genuinely fits none.
    /// </summary>
    public sealed class ArchetypeDef
    {
        public string Id = "";
        public string Name = "";
        // The template a new class starts from. Classes carry only per-key OVERRIDES;
        // GameConfig resolves them into the HeroDef at build time, so runtime code
        // keeps reading plain HeroDef.BaseStats/GrowthPerLevel.
        public StatBlock BaseStats = new StatBlock();
        public StatBlock GrowthPerLevel = new StatBlock();
        // The shared passive library (§7.2) this family's 2+2 kits draw from.
        public List<string> PassivePool = new List<string>();
    }

    public sealed class HeroDef
    {
        public string DefId = "";
        public string Name = "";       // the CLASS ("Knight", "Fire Mage", "Ninja"...)
        public string Archetype = "";  // the FAMILY ("warrior" | "rogue" | "magician")
        public string Role = "melee";  // melee | ranged | support — the sim's axis
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

    /// <summary>
    /// A themed ZONE — the "depth feels like travel" beat (roadmap item 4). Every
    /// ~10 stages (one <see cref="BalanceConstants.StagesPerTier"/> tier, so zones stay
    /// in lockstep with the rate/modifier tiers) is one zone: its own trash roster,
    /// its own boss, and engine-free palette/prop hints the client uses to reskin the
    /// faceted world. Art rule (2026-07-02): zone monsters are LOW-POLY faceted Tunic
    /// style via the scripted-Blender pipeline — the MS2 pipeline is for HEROES ONLY.
    /// </summary>
    public sealed class ZoneDef
    {
        public string Id = "";
        public string Name = "";
        public List<string> TrashMonsters = new List<string>(); // cycled by spawn index
        public string BossId = "";
        // Zone drop table (the "farm destinations" hook): drops in this zone favor one
        // equip slot ("boots drop best in the ruins") by Balance.ZoneFavoredSlotMult.
        // null = uniform (the intro zone). Rides LootContext so Loot stays mode-agnostic.
        public EquipSlot? FavoredSlot;
        // Client hints only (engine-free RGB 0..1, like ModifierDef tints): the faceted
        // ground palette and an accent for props/fog. PropSet names the prop family the
        // client scatters ("forest", "ruins", …) — unknown sets fall back to the default look.
        public double GroundR, GroundG, GroundB;
        public double AccentR, AccentG, AccentB;
        public string PropSet = "";
        // Arena hint (M8 slice 1): the per-stage layout id fought in this zone (client renders the
        // walkable region + height tiers). "" = no arena → the open plane. See GameConfig.Arenas.
        public string ArenaId = "";
    }

    /// <summary>One crypt depth TIER (the roguelite mode's theme band): floors travel these in
    /// <see cref="BalanceConstants.CryptTierFloors"/>-floor bands, cycling when the list runs out.
    /// ThemeKey is a client rendering hint (DungeonTheme key); the roster/boss are sim content.</summary>
    public sealed class CryptTierDef
    {
        public string ThemeKey = "crypt";
        public List<string> TrashRoster = new List<string>(); // cycled by spawn index
        public string BossId = "";
    }

    /// <summary>One crypt BOON track: a permanent account-wide stat buff bought with grave dust
    /// (the crypt's end-of-run chest currency). Each purchased rank multiplies <see cref="Stat"/>
    /// by (1 + <see cref="BalanceConstants.CryptBoonStatPct"/>); cost escalates geometrically.</summary>
    public sealed class CryptBoonDef
    {
        public string Id = "";
        public string Name = "";
        public StatKey Stat;
    }

    /// <summary>One row of the §7.3 depth-band ENCOUNTER TABLE: the per-room mob budgets a crypt
    /// floor uses from <see cref="MinDepth"/> down (the deepest row whose MinDepth ≤ floor wins —
    /// see Crypt.EncounterForFloor). Content-as-data; tuned by BalanceSim's dungeon mode.</summary>
    public sealed class CryptEncounterDef
    {
        public int MinDepth = 1;
        public int CombatWaves = 1;  // waves per combat/key room
        public int MobsPerWave = 5;
        public int EliteCount = 1;   // elites in the Elite room
        public int EliteEscort = 3;
        public int BossAdds = 2;
        // §7.3 chest room: chest count, wooden/iron/golden tier weights, per-chest mimic odds,
        // and the deep-band chance a golden chest's first item goes Mythic.
        public int ChestCount = 1;
        public int ChestWeightWooden = 70;
        public int ChestWeightIron = 25;
        public int ChestWeightGolden = 5;
        public double MimicChance = 0.15;
        public double GoldenMythicChance;
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

    /// <summary>
    /// A gacha banner — the gem SINK (roadmap 3). One roll spends <see cref="CostGems"/> of the
    /// premium currency and weighted-picks a hero from <see cref="Pool"/> (heroDefId → weight). A
    /// pity counter (in the save, keyed by <see cref="Id"/>) increments on every roll that does NOT
    /// yield <see cref="FeaturedHeroDefId"/>; hitting <see cref="PityCount"/> forces the featured hero
    /// and resets, and naturally drawing the featured hero resets it too. A NEW hero joins the roster
    /// (<see cref="Party.AcquireHero"/>); a DUPE converts to <see cref="DupeXp"/> hero XP +
    /// <see cref="DupeScrap"/> account scrap. Dupe rewards live on the banner (like <see cref="CostGems"/>)
    /// so a banner is a fully self-contained economy knob. No live banner ships in slice 1 —
    /// <see cref="GameConfig.Banners"/> is empty in Default(); tests build fixtures; the Ice Mage
    /// comeback banner arrives in slice 3.
    /// </summary>
    public sealed class GachaBannerDef
    {
        public string Id = "";
        public string Name = "";
        public long CostGems;                 // premium currency spent per roll
        public string FeaturedHeroDefId = ""; // the pity target + the "is featured" flag on a result
        public List<GachaPoolEntry> Pool = new List<GachaPoolEntry>(); // weighted hero pool
        public int PityCount;                 // rolls-without-featured that FORCE the featured hero (0 = no pity)
        public long DupeXp;                    // XP granted to the rolled hero when it's a dupe
        public long DupeScrap;                 // account scrap granted when the roll is a dupe
    }

    /// <summary>One weighted entry in a banner's pool: a hero def and its pick weight (&gt; 0).</summary>
    public sealed class GachaPoolEntry
    {
        public string HeroDefId = "";
        public double Weight;
    }

    /// <summary>All tunable numbers. The file you edit constantly to balance.</summary>
    public sealed class BalanceConstants
    {
        // Monster modifiers (Lever 1): hard cap on a behavior fraction (lifesteal / thorns reflect)
        // so a very deep modifier can't reach 100%+ sustain/reflect.
        public double ModifierBehaviorCap = 0.6;

        // Thorns — capped mirror (§5.3, locked 2026-07-09). Reflect stays damage-proportional (the
        // ModifierBehaviorCap fraction of the hit) but is ALSO capped per hit at this fraction of the
        // ATTACKER's MaxHp, so a thorns boss can never one-shot a heavy hitter no matter how hard it
        // swings — sustain (lifesteal, Priest) is the intended counter-build. One site in ApplyHit
        // covers every source (mob self-mod field + gear imprint stat).
        public double ThornsReflectHpCap = 0.025;

        // Loot legibility (Lever 2): a candidate item's power swap (Upgrades.PowerScore) within
        // ±this fraction reads as a Sidegrade, not an up/down-grade — so a 0.1% wiggle doesn't flash
        // a green ▲ and auto-equip doesn't churn on noise. 0.005 = 0.5%.
        public double UpgradeBandPct = 0.005;

        // Zone drop tables (roadmap 4): how strongly a zone's FavoredSlot outweighs the
        // other bases in the drop pick. 3x over 5 single-base slots ≈ 43% of drops vs the
        // uniform 20% — noticeable enough to park a farm on, not a hard lock.
        public double ZoneFavoredSlotMult = 3.0;

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

        // Dungeon (roguelite slice 3a): the anti-wallhack corridor-sight range — a unit standing in a
        // corridor may only target across a doorway when within this euclidean distance (in-room pairs
        // see each other freely). DungeonMaxRunSeconds is the failsafe timeout: a run that hasn't cleared
        // the boss by then is Lost (stuck / under-geared), mirroring MaxRunSeconds for the endless field.
        public double DungeonCorridorSight = 6.5;
        // Failsafe timeout for a full-clear run: the party sweeps the WHOLE crypt room by room (minutes,
        // not the ~16s a single boss room took), so this is bumped to 900 to leave headroom.
        public double DungeonMaxRunSeconds = 900;
        // §7.3 room-clear reward beat: clearing a sealed room pays a gold burst worth this many
        // average trash kills of the floor's roster (stage-scaled at init; floors display-floor it).
        public double DungeonRoomClearMobEquiv = 2.0;
        // §7.3 chests: a hero within this range of an unopened chest pops it (auto-open beat).
        public double DungeonChestOpenRadius = 1.6;
        // §7.3 floor guardian: base HP scale for the MINI-BOSS capping floors 1–2 of a run. Its
        // Elite rank stacks EliteHpMult (×3) on top ⇒ net ×1.5 a trash mob — a real wall, but
        // under the final floor's true boss (BossHpMult ×2).
        public double DungeonMiniBossHpMult = 0.5;
        // Per-tier chest payouts (index = ChestTier wooden/iron/golden): gold = the room-clear
        // burst × mult; items = count range (first item's rarity floors at Normal/Rare/Unique);
        // grave dust = range (wooden pays none). Mimics pay their chest + one bonus item.
        public double[] DungeonChestGoldMult = { 1.0, 2.0, 4.0 };
        public (int min, int max)[] DungeonChestItems = { (1, 2), (2, 3), (3, 3) };
        public (int min, int max)[] DungeonChestDust = { (0, 0), (5, 10), (15, 25) };
        // Ranged followers park this far behind the leader INSIDE a dungeon (vs FormationRangedBack 4.6
        // in the open field): corridor fights are close-quarters, and the open-field standoff left the
        // caster a full room behind the front line (user-caught).
        public double DungeonRangedBack = 2.6;

        // Crypt meta (roguelite mode, user-approved design 2026-07-06): entry is KEY-gated (1 key
        // recharges per UTC day, banked to CryptKeyBank); a run = CryptFloorsPerRun floors back-to-back
        // starting at DepthRecord+1. Floors ramp geometrically ON TOP of current-stage monster scaling
        // (gentler than Tower — a floor is a 100+-mob crawl, not one fight). Every FIRST clear of a
        // floor pays CryptGemsPerFloor gems (Tower-style drip); the §7.3 reward vault's chests pay
        // GRAVE DUST (the crypt-only currency, key CryptDustCurrency) into the run as it's walked — a
        // wipe forfeits whatever's still unwalked but keeps everything already dropped.
        // Dust buys permanent account boons: rank r of a boon costs ceil(Base × Growth^r), each rank
        // adds CryptBoonStatPct to its stat, capped at CryptBoonMaxRank.
        public int CryptKeyBank = 2;              // max banked keys
        public int CryptFloorsPerRun = 3;         // floors per run (descend beats between them)
        public int CryptMaxDepth = 60;            // content height (like TowerFloors)
        public int CryptTierFloors = 10;          // floors per theme tier (crypt→molten→frost, cycling)
        public int CryptGemsPerFloor = 5;         // first-clear gem pay per NEW depth floor
        public double CryptHpGrowth = 1.06;       // per-floor monster HP ramp (on top of stage scaling)
        public double CryptDmgGrowth = 1.04;      // per-floor monster atk ramp
        public string CryptDustCurrency = "grave_dust";
        public long CryptBoonBaseCost = 20;       // rank-0→1 boon cost (grave dust)
        public double CryptBoonCostGrowth = 1.5;  // geometric cost growth per rank
        public int CryptBoonMaxRank = 10;
        public double CryptBoonStatPct = 0.02;    // +2% of the boon's stat per rank
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

        // Arena height (M8 slice 1): world units of vertical rise per ArenaShape.Tier — the client
        // renders a tier-N platform at Y = N * this. Engine-free; slice 2 reads it (no sim rule does).
        public double TerrainTierHeight = 0.7;

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
        public double FormationMeleeBack = 0.6;   // melee followers flank at the leader's shoulder
        public double FormationRangedBack = 4.6;  // ranged followers park at casting distance
        public double PanicRadius = 1.8;          // a ranged follower with an enemy this close backpedals while it keeps firing
        public double PanicHoldDist = 2.0;  // a panicked caster runs to the leader and holds once this close — never kites off into the wild
        public double FormationDeadzone = 0.6;
        public double FormationBreakRadius = 6.0;
        // A follower farther than FormationBreakRadius from its slot regroups at
        // max(own, leader) MoveSpd × this, so an ungeared follower can never be
        // outrun by a geared leader between packs.
        public double RegroupHustleMult = 1.4;
        public double TankAggroBias = 2.0;  // monsters count MELEE heroes this much closer when picking a target — tanks soak attention

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
        public double MonsterHpGrowth = 1.18;  // tier-0 HP growth anchor; deep tiers TAPER below (§5.3, 10.1c)
        public double MonsterDmgGrowth = 1.08; // +8% atk/def per stage level (survivable)
        public double BossHpMult = 2.0;        // a boss is ~2x a same-stage trash mob (cut again for the 3-hero party cap)

        // Curve taper (§5.3, locked 2026-07-09 — 10.1c). A FLAT 1.18^stage HP curve mathematically
        // ends the ladder near stage 53 vs linear player power. Instead the per-stage HP growth EASES
        // down by 10-stage difficulty tier: early tiers keep ~1.18 (stages 1-30 play nearly unchanged),
        // deep tiers decline toward ~1.07 so ON-CURVE gear reaches a SOFT WALL at ~stage 80 and 81-100
        // becomes the prestige band (near-mythic gear + account stacks). Index = Tier(stage), clamped
        // to the last entry. Tune this table against the REBASED player power (10.1b), not the old one.
        public double[] MonsterHpGrowthByTier = { 1.18, 1.18, 1.175, 1.12, 1.09, 1.05, 1.025, 1.017, 1.013, 1.009 };

        /// <summary>Cumulative monster-HP multiplier at a monster level (=stage) — the tapered
        /// replacement for the flat MonsterHpGrowth^(level-1). Pure/deterministic: the growth used to
        /// REACH each level is that level's tier rate, accumulated from level 1 (=×1.0). Bosses layer
        /// BossHpMult/MajorBossMult on top of this, unchanged.</summary>
        public double MonsterHpMult(int monsterLevel)
        {
            double mult = 1.0;
            int last = MonsterHpGrowthByTier.Length - 1;
            for (int lvl = 2; lvl <= monsterLevel; lvl++)
                mult *= MonsterHpGrowthByTier[Math.Min(last, Tier(lvl))];
            return mult;
        }

        // Damage taper (§5.3, 10.1c). At deep stages the flat 1.08^stage atk/def growth WIPES the
        // party (a survival wall) before HP even becomes the DPS wall — so damage eases by tier too,
        // on the same model as MonsterHpGrowthByTier. Tier-0 stays MonsterDmgGrowth (1.08) so stages
        // 1-30 are unchanged and DerivedStats' survival read is unaffected there.
        public double[] MonsterDmgGrowthByTier = { 1.08, 1.08, 1.08, 1.055, 1.04, 1.025, 1.017, 1.013, 1.011, 1.008 };

        /// <summary>Cumulative monster atk/def multiplier at a monster level (=stage) — the tapered
        /// replacement for the flat MonsterDmgGrowth^(level-1). Pure/deterministic, same shape as
        /// <see cref="MonsterHpMult"/>. Major bosses layer MajorBossMult on top, unchanged.</summary>
        public double MonsterDmgMult(int monsterLevel)
        {
            double mult = 1.0;
            int last = MonsterDmgGrowthByTier.Length - 1;
            for (int lvl = 2; lvl <= monsterLevel; lvl++)
                mult *= MonsterDmgGrowthByTier[Math.Min(last, Tier(lvl))];
            return mult;
        }

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
        public int TowerGemsPerFloor = 10;           // gems granted for each first-time floor clear (a steady gem drip)
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

        // ---- Gear enhancement (2026-07-02) — THE scrap sink. +N multiplies the item
        // BASE's stats by EnhanceBasePctPerLevel each (affixes untouched: rolls are
        // Reforge's domain). +1..+5 always land; +6..+9 can fail costing only the
        // scrap; +10..+15 fails DROP one level (the item itself is never destroyed).
        // Cost escalates geometrically off the item's scrap value, so to +15 costs
        // ~100x what the item salvages for — a deep, self-scaling sink.
        public int EnhanceMax = 15;
        public double EnhanceBasePctPerLevel = 0.05;
        // success chance of ATTEMPTING level N = EnhanceSuccess[N-1]
        public double[] EnhanceSuccess = { 1, 1, 1, 1, 1, 0.9, 0.8, 0.7, 0.6, 0.5, 0.45, 0.4, 0.35, 0.3, 0.25 };
        public int EnhanceDropFrom = 10;   // failed attempts at/above this level lose a level
        public double EnhanceCostBase = 0.6;
        public double EnhanceCostGrowth = 1.3;
        public long EnhanceCost(Item item) => (long)Math.Ceiling(
            ScrapValue(item.Rarity, item.ItemLevel)
            * EnhanceCostBase * Math.Pow(EnhanceCostGrowth, item.Enhance));

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
        public Dictionary<string, ArchetypeDef> Archetypes = new Dictionary<string, ArchetypeDef>();
        public Dictionary<string, HeroDef> Heroes = new Dictionary<string, HeroDef>();
        public Dictionary<string, ItemBaseDef> ItemBases = new Dictionary<string, ItemBaseDef>();
        public List<AffixDef> AffixPool = new List<AffixDef>();
        public Dictionary<string, MonsterDef> Monsters = new Dictionary<string, MonsterDef>();
        public List<StageDef> Stages = new List<StageDef>();
        // Themed zones, one per StagesPerTier band ascending (zone[0] = stages 1..10, …).
        // Deeper stages than the table clamp to the last zone. See ZoneForStage.
        public List<ZoneDef> Zones = new List<ZoneDef>();
        // Per-stage arena layouts (M8 slice 1), keyed by arena id. A zone points at one via
        // ZoneDef.ArenaId; empty/missing => the open plane (legacy behavior). See ArenaForStage.
        public Dictionary<string, ArenaLayout> Arenas = new Dictionary<string, ArenaLayout>();
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
        // Gacha banners (roadmap 3 — the gem SINK), keyed by banner id. Empty in Default() for slice 1
        // (no live content); the Ice Mage comeback banner arrives in slice 3. See Gacha.cs.
        public Dictionary<string, GachaBannerDef> Banners = new Dictionary<string, GachaBannerDef>();
        // Crypt (roguelite) depth tiers + boon tracks. Tiers theme the floor bands (crypt → molten →
        // frost, cycling); boons are the grave-dust sink. See Crypt.cs.
        public List<CryptTierDef> CryptTiers = new List<CryptTierDef>();
        public List<CryptBoonDef> CryptBoons = new List<CryptBoonDef>();
        // §7.3 depth-band encounter table (per-room mob budgets by floor depth). MinDepth-ascending.
        public List<CryptEncounterDef> CryptEncounters = new List<CryptEncounterDef>();
        public BalanceConstants Balance = new BalanceConstants();

        /// <summary>The zone a stage belongs to: one zone per StagesPerTier band (the same
        /// tier the rate/modifier curves use), clamped to the last zone for stages past the
        /// table. null when no zones are defined (legacy configs — spawns fall back).</summary>
        public ZoneDef? ZoneForStage(int stage)
        {
            if (Zones.Count == 0) return null;
            return Zones[Math.Clamp(Balance.Tier(stage), 0, Zones.Count - 1)];
        }

        /// <summary>The arena layout a stage is fought on: resolve the stage's zone, return its
        /// layout when the zone names a non-empty ArenaId present in <see cref="Arenas"/>, else null
        /// (the open plane). Tower floors reuse this by passing the floor (floors travel the zones).</summary>
        public ArenaLayout? ArenaForStage(int stage)
        {
            var zone = ZoneForStage(stage);
            if (zone == null || zone.ArenaId.Length == 0) return null;
            return Arenas.TryGetValue(zone.ArenaId, out var layout) ? layout : null;
        }

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

        // Zone content helpers. Trash defs use LootTableId "common" and the slime/goblin pay
        // bands (fodder 12xp/3g, striker 20xp/6g); zone bosses clone the goblin_king band
        // ("boss" table, 60xp/40g, rise-in, the quake signature). Base stats stay inside the
        // slime/goblin envelope on purpose: the geometric stage curve owns DIFFICULTY, so a
        // zone roster is a FLAVOR profile (fast/frail vs slow/tanky), not a power step.
        private static MonsterDef Trash(string id, string name, double hp, double atk, double def,
            double moveSpd, double atkSpd, double crit, int xp, int gold)
            => new MonsterDef
            {
                Id = id, Name = name, LootTableId = "common", XpReward = xp, GoldReward = gold, Sprite = id,
                BaseStats = SB((StatKey.Hp, hp), (StatKey.Atk, atk), (StatKey.Def, def),
                               (StatKey.MoveSpd, moveSpd), (StatKey.AtkSpd, atkSpd),
                               (StatKey.CritChance, crit), (StatKey.CritDmg, 1.5)),
            };

        private static MonsterDef BossMob(string id, string name, double hp, double atk, double def,
            double moveSpd, double atkSpd)
            => new MonsterDef
            {
                Id = id, Name = name, LootTableId = "boss", XpReward = 60, GoldReward = 40, Sprite = id,
                SpawnStyle = "rise", Skills = new List<string> { "boss_quake" },
                BaseStats = SB((StatKey.Hp, hp), (StatKey.Atk, atk), (StatKey.Def, def),
                               (StatKey.MoveSpd, moveSpd), (StatKey.AtkSpd, atkSpd),
                               (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.6)),
            };

        private static ZoneDef Zone(string id, string name, string propSet,
            (double r, double g, double b) ground, (double r, double g, double b) accent,
            string boss, EquipSlot? favored, params string[] trash)
            => new ZoneDef
            {
                Id = id, Name = name, PropSet = propSet, BossId = boss, FavoredSlot = favored,
                TrashMonsters = new List<string>(trash),
                GroundR = ground.r, GroundG = ground.g, GroundB = ground.b,
                AccentR = accent.r, AccentG = accent.g, AccentB = accent.b,
            };

        // Arena content helpers (M8 slice 1). An arena reads as data: a list of shapes whose UNION is
        // the walkable region. Rect(cx,cz, halfW,halfD, tier) and Disc(cx,cz, radius, tier) build one
        // piece; Arena(id, shapes) assembles the layout. Authoring rules (enforced by ArenaTests, not
        // code): union connected + star-ish around origin, tier-0 covers the r≈32 spawn bubble, every
        // Tier>0 shape overlaps a lower-tier shape (the ramp band), perimeter gaps are shallow bays only.
        private static ArenaShape Rect(double cx, double cz, double halfW, double halfD, int tier = 0)
            => new ArenaShape { Kind = ArenaShapeKind.Rect, X = cx, Y = cz, HalfW = halfW, HalfD = halfD, Tier = tier };
        private static ArenaShape Disc(double cx, double cz, double radius, int tier = 0)
            => new ArenaShape { Kind = ArenaShapeKind.Disc, X = cx, Y = cz, HalfW = radius, Tier = tier };
        private static ArenaLayout Arena(string id, params ArenaShape[] shapes)
            => new ArenaLayout { Id = id, Shapes = new List<ArenaShape>(shapes) };

        // One achievement tier (threshold + gold/scrap/XP reward), and an achievement (tiers ascending).
        private static AchievementTier AT(long threshold, long gold, long scrap, int xp)
            => new AchievementTier { Threshold = threshold, RewardGold = gold, RewardScrap = scrap, RewardXp = xp };
        private static AchievementDef Ach(string id, string name, AchievementMetric metric, string unit, params AchievementTier[] tiers)
            => new AchievementDef { Id = id, Name = name, Metric = metric, Unit = unit, Tiers = new List<AchievementTier>(tiers) };

        /// <summary>Resolve a class onto its archetype template: the def's stat blocks
        /// carry only per-key OVERRIDES; everything else comes from the family. Runtime
        /// code keeps reading plain HeroDef.BaseStats/GrowthPerLevel — the merge happens
        /// once, here, at config build.</summary>
        private static HeroDef FromArchetype(ArchetypeDef arch, HeroDef def)
        {
            def.Archetype = arch.Id;
            def.BaseStats = Merge(arch.BaseStats, def.BaseStats);
            def.GrowthPerLevel = Merge(arch.GrowthPerLevel, def.GrowthPerLevel);
            return def;
        }

        private static StatBlock Merge(StatBlock template, StatBlock overrides)
        {
            var merged = new StatBlock(template);
            foreach (var kv in overrides) merged[kv.Key] = kv.Value;
            return merged;
        }

        /// <summary>The default content set.</summary>
        public static GameConfig Default()
        {
            var cfg = new GameConfig();

            // ---- Archetypes (the roster backbone, 2026-07-02) --------------------------
            // Three families; a class = FromArchetype(family, overrides). The family is
            // content DNA (stat template, passive pool, MS2 anim library, future gacha
            // banners); HeroDef.Role stays the sim's mechanical axis. An Archer would be
            // a Rogue with Role=ranged; a Brawler a Warrior with speed overrides.

            var warrior = cfg.Archetypes["warrior"] = new ArchetypeDef
            {
                Id = "warrior", Name = "Warrior", // tanky front line: high Hp/Def, slow swings
                BaseStats = SB((StatKey.Hp, 120), (StatKey.Atk, 14), (StatKey.Def, 8),
                               (StatKey.MoveSpd, 3.0), (StatKey.AtkSpd, 0.85),
                               (StatKey.CritChance, 0.05), (StatKey.CritDmg, 1.5),
                               (StatKey.HpRegen, 1.5),
                               (StatKey.AttackRange, 1.2),
                               (StatKey.SplashRadius, 1.0)),           // wide cleave (melee perk)
                GrowthPerLevel = SB((StatKey.Hp, 38), (StatKey.Atk, 6.5), (StatKey.Def, 3.2)),
                PassivePool = new List<string> { "toughness", "vitality" },
            };
            var rogue = cfg.Archetypes["rogue"] = new ArchetypeDef
            {
                Id = "rogue", Name = "Rogue", // crit glass cannon: fastest, most fragile
                BaseStats = SB((StatKey.Hp, 64), (StatKey.Atk, 16), (StatKey.Def, 3),
                               (StatKey.MoveSpd, 3.4), (StatKey.AtkSpd, 1.45),
                               (StatKey.CritChance, 0.22), (StatKey.CritDmg, 1.9), // crit IS the identity
                               (StatKey.HpRegen, 1.0),
                               (StatKey.AttackRange, 1.2),
                               (StatKey.SplashRadius, 0.5)),           // duelists, not cleavers
                GrowthPerLevel = SB((StatKey.Hp, 22), (StatKey.Atk, 9), (StatKey.Def, 2.2)),
                PassivePool = new List<string> { "precision", "killerinstinct" },
            };
            var magician = cfg.Archetypes["magician"] = new ArchetypeDef
            {
                Id = "magician", Name = "Magician", // fragile ranged casters, hardest-hitting spells
                BaseStats = SB((StatKey.Hp, 72), (StatKey.Atk, 17), (StatKey.Def, 4),
                               // slightly above the Warrior template (3.0) so casters don't trail
                               // the melee dash to every pack — they park farther out (standoff ~4.6)
                               // at equal speed and lagged; rogue (3.4) stays fastest. Play-tuning candidate.
                               (StatKey.MoveSpd, 3.15), (StatKey.AtkSpd, 1.15),
                               (StatKey.CritChance, 0.07), (StatKey.CritDmg, 1.5),
                               (StatKey.HpRegen, 1.0),
                               (StatKey.AttackRange, 6.0),             // max reach; still fine point-blank
                               (StatKey.SplashRadius, 0.75)),
                GrowthPerLevel = SB((StatKey.Hp, 23), (StatKey.Atk, 9), (StatKey.Def, 2.2)),
                PassivePool = new List<string> { "pyromancy", "attunement", "permafrost", "frostflow", "devotion", "benediction" },
            };

            // ---- Classes (def ids are save/asset keys — they never change) --------------

            // Knight IS the Warrior template (renamed from "Warrior" 2026-07-02).
            cfg.Heroes["warrior_basic"] = FromArchetype(warrior, new HeroDef
            {
                DefId = "warrior_basic", Name = "Knight", Role = "melee",
                // 2+2 kit (§7.2): spinning AoE + a shield-charge gap closer; armor + health passives.
                Skills = new List<string> { "cycloneslash", "toughness", "shieldcharge", "vitality" }, Sprite = "warrior",
            });

            // Fire Mage IS the Magician template (renamed from "Magician" 2026-07-02).
            cfg.Heroes["magician_basic"] = FromArchetype(magician, new HeroDef
            {
                DefId = "magician_basic", Name = "Fire Mage", Role = "ranged",
                // 2+2 kit (§7.2): fire nuke + AoE fireball; spell-power + focus passives.
                Skills = new List<string> { "firebolt", "pyromancy", "fireball", "attunement" }, Sprite = "magician", AttackFx = "fireball",
            });

            // Assassin IS the Rogue template (renamed from "Thief" 2026-07-02); the
            // future Ninja is the same family with Role=ranged (throwing stars).
            cfg.Heroes["thief_basic"] = FromArchetype(rogue, new HeroDef
            {
                DefId = "thief_basic", Name = "Assassin", Role = "melee",
                // 2+2 kit (§7.2): fast stab + heavy vital strike; crit-chance + crit-damage passives.
                Skills = new List<string> { "shadowstab", "precision", "vitalstrike", "killerinstinct" },
                Sprite = "thief",
            });

            // Ice Mage — BANNER-GATED (slice 3, 2026-07-04): no longer unobtainable. She's
            // absent from HeroUnlocks (no stage grants her), but the live "Winter's Return"
            // gacha banner (Default() ~L1320) has her in its pool, and SyncHeroUnlocks keeps
            // every banner-pool hero obtainable while its banner is live (Progression.cs ~L167)
            // — so a roll-granted Ice Mage survives a sync. Pull the banner from config and she
            // shelves again (the same removal path). Magician family: sturdier/slower than the
            // Fire Mage. AttackFx "icebolt" = the pale-ice basic bolt (CombatView frost VFX pass).
            cfg.Heroes["icemage_basic"] = FromArchetype(magician, new HeroDef
            {
                DefId = "icemage_basic", Name = "Ice Mage", Role = "ranged",
                BaseStats = SB((StatKey.Hp, 82), (StatKey.Atk, 15), (StatKey.Def, 6),
                               (StatKey.AtkSpd, 1.0), (StatKey.CritChance, 0.05),
                               (StatKey.HpRegen, 1.2), (StatKey.SplashRadius, 0.8)),
                GrowthPerLevel = SB((StatKey.Hp, 28), (StatKey.Atk, 8), (StatKey.Def, 2.8)),
                // 2+2 kit (§7.2): frost nuke + AoE blizzard; armor + cadence passives.
                Skills = new List<string> { "frostbolt", "permafrost", "blizzard", "frostflow" },
                Sprite = "icemage", AttackFx = "icebolt",
            });

            // Priest — Magician family, the party's first SUPPORT (and first male-body
            // hero): the party heal-over-time is the identity; low Atk on purpose (the
            // heal scales off MaxHp, not Atk).
            cfg.Heroes["priest_basic"] = FromArchetype(magician, new HeroDef
            {
                DefId = "priest_basic", Name = "Priest", Role = "ranged",
                BaseStats = SB((StatKey.Hp, 90), (StatKey.Atk, 12), (StatKey.Def, 5),
                               (StatKey.AtkSpd, 0.95), (StatKey.CritChance, 0.04),
                               (StatKey.HpRegen, 1.2), (StatKey.SplashRadius, 0.7)),
                GrowthPerLevel = SB((StatKey.Hp, 26), (StatKey.Atk, 6.5), (StatKey.Def, 2.6)),
                // 2+2 kit (§7.2): party HoT + AoE smite; sustain + grace passives.
                Skills = new List<string> { "sanctify", "devotion", "holysmite", "benediction" },
                Sprite = "priest", AttackFx = "holybolt",
            });

            // Progression unlocks: you start with just the Knight; reaching stage 3 adds
            // the Fire Mage, stage 5 the Priest, stage 10 the Assassin. Retroactive:
            // Progression.SyncHeroUnlocks grants any table row at/below HighestStage on
            // load, and REMOVES owned heroes whose def leaves this table (that's how the
            // Ice Mage is shelved — its def stays below for the future reintroduction).
            cfg.HeroUnlocks[3] = "magician_basic";
            cfg.HeroUnlocks[5] = "priest_basic";
            cfg.HeroUnlocks[10] = "thief_basic";

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
            // (Slot trim 2026-07-02: wooden_shield/linen_cape/copper_ring/bone_amulet
            // deleted with their Offhand/Cape/Ring/Amulet slots — 5 chunky slots beat 9
            // thin ones; legacy items dissolve into scrap via Inventory.PruneUnknownGear.)

            // All floors sit at Rare (Normal rolls nothing, Rare+ rolls everything) — the
            // old Magic-tier "basic stats only" split left with the Magic rarity itself.
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Hp, Weight = 30, ValueMinPerItemLevel = 1.2, ValueMaxPerItemLevel = 2.4, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Atk, Weight = 25, ValueMinPerItemLevel = 0.3, ValueMaxPerItemLevel = 0.6, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.Def, Weight = 20, ValueMinPerItemLevel = 0.3, ValueMaxPerItemLevel = 0.6, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.AtkSpd, Weight = 8, ValueMinPerItemLevel = 0.004, ValueMaxPerItemLevel = 0.012, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.MoveSpd, Weight = 6, ValueMinPerItemLevel = 0.012, ValueMaxPerItemLevel = 0.03, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.CritChance, Weight = 8, ValueMinPerItemLevel = 0.002, ValueMaxPerItemLevel = 0.006, RarityFloor = Rarity.Rare });
            cfg.AffixPool.Add(new AffixDef { Stat = StatKey.CritDmg, Weight = 9, ValueMinPerItemLevel = 0.012, ValueMaxPerItemLevel = 0.03, RarityFloor = Rarity.Rare });

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

            // ---- Zones (roadmap item 4) — one themed band per 10-stage tier. Each brings
            // a 2-mob trash roster + its own boss (defs via the Trash/BossMob band helpers —
            // flavor profiles, not power steps) and palette/prop hints for the faceted world.
            // Zone 1 keeps the original slime/goblin/goblin_king so the early game look is
            // unchanged. Trash cadence: fodder (slime band, 12/3) then striker (goblin band, 20/6).
            cfg.Monsters["bone_rattler"] = Trash("bone_rattler", "Bone Rattler", 30, 3.5, 0, 2.8, 0.9, 0, 12, 3);
            cfg.Monsters["stone_sentry"] = Trash("stone_sentry", "Stone Sentry", 60, 4.5, 2, 2.4, 0.8, 0, 20, 6);
            cfg.Monsters["grave_knight"] = BossMob("grave_knight", "Grave Knight", 170, 11, 4, 2.4, 0.85);

            cfg.Monsters["bog_toad"] = Trash("bog_toad", "Bog Toad", 40, 3, 1, 2.4, 0.75, 0, 12, 3);
            cfg.Monsters["marsh_wisp"] = Trash("marsh_wisp", "Marsh Wisp", 30, 5.5, 0, 3.2, 1.2, 0.05, 20, 6);
            cfg.Monsters["bog_horror"] = BossMob("bog_horror", "Bog Horror", 180, 11, 2, 2.2, 0.8);

            // ---- SDF blob-shell family (ROADMAP 4, slice 3): a slime/shambler/spirit trio that
            // JOINS the swamp roster (bog_toad + marsh_wisp stay). Same zone-3 flavor envelope
            // (bog_toad 40hp/3dmg tanky-fodder, marsh_wisp 30hp/5.5dmg glassy) — flavor, not a
            // power step (the geometric stage curve owns difficulty). SpawnStyle "rise" reuses the
            // boss/goblin_king rise-from-ground tell (a slime welling up out of the bog reads best).
            // Mire Slime: the tank of the trio — most HP, least damage, crawls (slowest MoveSpd).
            cfg.Monsters["mire_slime"] = Trash("mire_slime", "Mire Slime", 58, 2.5, 1, 1.8, 0.7, 0, 14, 4);
            cfg.Monsters["mire_slime"].SpawnStyle = "rise";
            // Bog Shambler: the middling melee walker — stats sit between bog_toad and stone_sentry,
            // kept zone-3 appropriate (not a boss). A plodding legged gait (Walk family, client-side).
            cfg.Monsters["bog_shambler"] = Trash("bog_shambler", "Bog Shambler", 50, 4, 2, 2.5, 0.85, 0.02, 18, 5);
            // Fen Spirit: fast, fragile, and genuinely RANGED. The Trash helper only makes melee
            // mobs (no AttackRange => Combat.MeleeRange 1.0), so this one is authored inline to set
            // AttackRange > 2.0 (Combat's melee/ranged cutoff) and an AttackFx projectile hint so the
            // client fires a bolt instead of a swing. Fewest HP, quickest MoveSpd of the trio.
            cfg.Monsters["fen_spirit"] = new MonsterDef
            {
                Id = "fen_spirit", Name = "Fen Spirit", LootTableId = "common", XpReward = 20, GoldReward = 6, Sprite = "fen_spirit",
                AttackFx = "icebolt", // reuse the shipped pale-ice bolt (reads as a spectral mote); no new FX asset
                BaseStats = SB((StatKey.Hp, 26), (StatKey.Atk, 6), (StatKey.Def, 0),
                               (StatKey.MoveSpd, 3.6), (StatKey.AtkSpd, 1.25),
                               (StatKey.CritChance, 0.06), (StatKey.CritDmg, 1.5),
                               (StatKey.AttackRange, 3.2)), // > 2.0 => Combat treats it as ranged
            };

            cfg.Monsters["dust_scarab"] = Trash("dust_scarab", "Dust Scarab", 36, 3.5, 2, 2.8, 1.0, 0, 12, 3);
            cfg.Monsters["dune_stalker"] = Trash("dune_stalker", "Dune Stalker", 48, 6, 1, 3.4, 1.15, 0.06, 20, 6);
            cfg.Monsters["dune_wurm"] = BossMob("dune_wurm", "Dune Wurm", 200, 10, 3, 2.0, 0.7);

            cfg.Monsters["ice_sprite"] = Trash("ice_sprite", "Ice Sprite", 32, 4, 0, 3.0, 1.1, 0, 12, 3);
            cfg.Monsters["frost_wolf"] = Trash("frost_wolf", "Frost Wolf", 50, 5.5, 1, 3.8, 1.2, 0.05, 20, 6);
            cfg.Monsters["glacier_golem"] = BossMob("glacier_golem", "Glacier Golem", 220, 10, 5, 1.8, 0.6);

            cfg.Monsters["magma_imp"] = Trash("magma_imp", "Magma Imp", 34, 5, 1, 3.0, 1.05, 0, 12, 3);
            cfg.Monsters["cinder_hound"] = Trash("cinder_hound", "Cinder Hound", 48, 6, 1, 3.6, 1.25, 0.04, 20, 6);
            cfg.Monsters["ash_tyrant"] = BossMob("ash_tyrant", "Ash Tyrant", 180, 14, 3, 2.6, 0.9);

            cfg.Monsters["cave_bat"] = Trash("cave_bat", "Cave Bat", 28, 4, 0, 3.8, 1.3, 0, 12, 3);
            cfg.Monsters["gloom_shade"] = Trash("gloom_shade", "Gloom Shade", 46, 6, 1, 3.0, 1.0, 0.10, 20, 6);
            cfg.Monsters["nightmare_maw"] = BossMob("nightmare_maw", "Nightmare Maw", 190, 13, 3, 2.4, 0.85);

            cfg.Monsters["tide_crab"] = Trash("tide_crab", "Tide Crab", 55, 4, 3, 2.2, 0.75, 0, 12, 3);
            cfg.Monsters["storm_caller"] = Trash("storm_caller", "Storm Caller", 40, 6.5, 0, 3.0, 1.15, 0.06, 20, 6);
            cfg.Monsters["tempest_naga"] = BossMob("tempest_naga", "Tempest Naga", 185, 13, 3, 2.8, 1.0);

            cfg.Monsters["void_wisp"] = Trash("void_wisp", "Void Wisp", 34, 5.5, 0, 3.2, 1.2, 0.05, 12, 3);
            cfg.Monsters["rune_construct"] = Trash("rune_construct", "Rune Construct", 58, 5, 3, 2.3, 0.75, 0, 20, 6);
            cfg.Monsters["riftwalker"] = BossMob("riftwalker", "Riftwalker", 190, 13, 4, 2.7, 1.0);

            cfg.Monsters["crown_seraph"] = Trash("crown_seraph", "Crown Seraph", 42, 6, 2, 3.0, 1.1, 0.06, 12, 3);
            cfg.Monsters["chaos_spawn"] = Trash("chaos_spawn", "Chaos Spawn", 52, 6.5, 1, 3.2, 1.15, 0.08, 20, 6);
            cfg.Monsters["world_ender"] = BossMob("world_ender", "World Ender", 210, 14, 4, 2.5, 0.85);

            // Zone 1's palette = the client's shipped ground/foliage colours exactly, so
            // the early game keeps its current look (the client lerps props toward Accent).
            // FavoredSlot gives each zone a drop-table identity ("where do I park my farm
            // tonight"): zone 1 stays uniform (the intro), the rest cycle the 5 slots so
            // every slot has a best-in-class zone in both halves of the ladder.
            cfg.Zones.Add(Zone("verdant_woods", "Verdant Woods", "forest",
                (0.40, 0.57, 0.33), (0.44, 0.62, 0.30), "goblin_king", null, "slime", "goblin"));
            cfg.Zones.Add(Zone("ruined_courtyard", "Ruined Courtyard", "ruins",
                (0.55, 0.54, 0.50), (0.42, 0.40, 0.38), "grave_knight", EquipSlot.Boots, "bone_rattler", "stone_sentry"));
            cfg.Zones.Add(Zone("murkwater_swamp", "Murkwater Swamp", "swamp",
                (0.35, 0.42, 0.30), (0.24, 0.32, 0.22), "bog_horror", EquipSlot.Helm,
                "bog_toad", "marsh_wisp", "mire_slime", "bog_shambler", "fen_spirit"));
            cfg.Zones.Add(Zone("amber_dunes", "Amber Dunes", "desert",
                (0.80, 0.68, 0.42), (0.66, 0.52, 0.30), "dune_wurm", EquipSlot.Gloves, "dust_scarab", "dune_stalker"));
            cfg.Zones.Add(Zone("frostpeak_tundra", "Frostpeak Tundra", "tundra",
                (0.82, 0.87, 0.92), (0.58, 0.70, 0.82), "glacier_golem", EquipSlot.Chest, "ice_sprite", "frost_wolf"));
            cfg.Zones.Add(Zone("ember_caldera", "Ember Caldera", "volcano",
                (0.35, 0.25, 0.22), (0.78, 0.30, 0.16), "ash_tyrant", EquipSlot.Weapon, "magma_imp", "cinder_hound"));
            cfg.Zones.Add(Zone("gloom_hollow", "Gloom Hollow", "cavern",
                (0.28, 0.26, 0.34), (0.45, 0.38, 0.58), "nightmare_maw", EquipSlot.Boots, "cave_bat", "gloom_shade"));
            cfg.Zones.Add(Zone("storm_coast", "Storm Coast", "coast",
                (0.42, 0.50, 0.58), (0.30, 0.42, 0.55), "tempest_naga", EquipSlot.Helm, "tide_crab", "storm_caller"));
            cfg.Zones.Add(Zone("astral_ruins", "Astral Ruins", "astral",
                (0.40, 0.34, 0.55), (0.60, 0.48, 0.85), "riftwalker", EquipSlot.Gloves, "void_wisp", "rune_construct"));
            cfg.Zones.Add(Zone("crown_of_the_world", "Crown of the World", "summit",
                (0.75, 0.70, 0.55), (0.90, 0.82, 0.55), "world_ender", EquipSlot.Weapon, "crown_seraph", "chaos_spawn"));

            // ---- Crypt depth tiers + boons (roguelite meta, 2026-07-06) -----------------
            // Floors travel the tiers in CryptTierFloors bands, cycling: 1-10 crypt, 11-20 molten,
            // 21-30 frost, 31-40 crypt again… Rosters/bosses borrow the matching zones' casts (the
            // monsters and their client models already exist); themes are DungeonTheme keys.
            cfg.CryptTiers.Add(new CryptTierDef
            {
                ThemeKey = "crypt",
                TrashRoster = new List<string> { "cave_bat", "gloom_shade" },
                BossId = "nightmare_maw",
            });
            cfg.CryptTiers.Add(new CryptTierDef
            {
                ThemeKey = "molten",
                TrashRoster = new List<string> { "magma_imp", "cinder_hound" },
                BossId = "ash_tyrant",
            });
            cfg.CryptTiers.Add(new CryptTierDef
            {
                ThemeKey = "frost",
                TrashRoster = new List<string> { "ice_sprite", "frost_wolf" },
                BossId = "glacier_golem",
            });
            // Three boon tracks — the grave-dust sink (permanent, account-wide).
            cfg.CryptBoons.Add(new CryptBoonDef { Id = "vigor", Name = "Vigor", Stat = StatKey.Hp });
            cfg.CryptBoons.Add(new CryptBoonDef { Id = "ferocity", Name = "Ferocity", Stat = StatKey.Atk });
            cfg.CryptBoons.Add(new CryptBoonDef { Id = "bulwark", Name = "Bulwark", Stat = StatKey.Def });
            // §7.3 depth-band encounter table (starting values; BalanceSim dungeon mode tunes).
            cfg.CryptEncounters.Add(new CryptEncounterDef
            {
                MinDepth = 1, CombatWaves = 1, MobsPerWave = 5, EliteCount = 1, EliteEscort = 3, BossAdds = 2,
                ChestCount = 1, ChestWeightWooden = 70, ChestWeightIron = 25, ChestWeightGolden = 5,
            });
            cfg.CryptEncounters.Add(new CryptEncounterDef
            {
                MinDepth = 11, CombatWaves = 2, MobsPerWave = 4, EliteCount = 1, EliteEscort = 4, BossAdds = 3,
                ChestCount = 2, ChestWeightWooden = 45, ChestWeightIron = 40, ChestWeightGolden = 15,
            });
            cfg.CryptEncounters.Add(new CryptEncounterDef
            {
                MinDepth = 21, CombatWaves = 2, MobsPerWave = 5, EliteCount = 2, EliteEscort = 3, BossAdds = 4,
                ChestCount = 2, ChestWeightWooden = 30, ChestWeightIron = 45, ChestWeightGolden = 25,
            });
            cfg.CryptEncounters.Add(new CryptEncounterDef
            {
                MinDepth = 41, CombatWaves = 3, MobsPerWave = 4, EliteCount = 2, EliteEscort = 4, BossAdds = 4,
                ChestCount = 3, ChestWeightWooden = 15, ChestWeightIron = 45, ChestWeightGolden = 40,
                GoldenMythicChance = 0.10,
            });

            // ---- Arenas (ROADMAP 8, slice 1) — one PLACE per zone, id arena_<zoneId>. Each is the
            // walkable UNION of its shapes. Every layout's tier-0 base covers the r≈32 spawn bubble
            // around the origin (party centre + spawn ring 26 + wander 5 + pad), stays connected and
            // star-ish around origin, and every raised (Tier>0) shape overlaps a lower-tier shape by a
            // ≥2-tile band = the ramp the client draws up. Height tiers are cosmetic data this slice.
            // Geometry stays SIMPLE and well inside the legacy 200×140 field; zones vary so slice-2
            // dioramas feel distinct (forest glade, ruined plaza, swamp with a water bay, dune bowl…).

            // Verdant Woods: a round forest glade with two low mossy knolls the party fights over.
            cfg.Arenas["arena_verdant_woods"] = Arena("arena_verdant_woods",
                Disc(0, 0, 40, 0),
                Disc(24, 14, 10, 1), Disc(-22, -12, 9, 1));

            // Ruined Courtyard: a squared flagstone plaza with two raised terraces, one stacking a dais.
            cfg.Arenas["arena_ruined_courtyard"] = Arena("arena_ruined_courtyard",
                Rect(0, 0, 42, 34, 0),
                Rect(28, 18, 12, 10, 1), Rect(-30, 16, 11, 9, 1), Rect(30, 20, 6, 5, 2));

            // Murkwater Swamp: a mud-flat disc (covers the whole spawn bubble) with a shallow water BAY
            // biting the south PERIMETER — the bay lives beyond the bubble, cut by omitting the far-south
            // extension the flanking banks give east/west (mouth ≥8, depth ≤6). Two hummocks rise above.
            cfg.Arenas["arena_murkwater_swamp"] = Arena("arena_murkwater_swamp",
                Disc(0, 0, 33, 0),               // core flat: fully covers the r≈32 bubble
                Rect(-30, -30, 12, 8, 0), Rect(30, -30, 12, 8, 0), // SE/SW banks; the gap between = the bay
                Disc(20, 16, 9, 1), Disc(-24, 14, 8, 1));

            // Amber Dunes: a sand bowl (disc) with two dune crests and a wind-carved high shelf.
            cfg.Arenas["arena_amber_dunes"] = Arena("arena_amber_dunes",
                Disc(0, 0, 41, 0),
                Disc(-20, 18, 11, 1), Disc(22, -16, 10, 1), Disc(-18, 20, 6, 2));

            // Frostpeak Tundra: a broad snowfield with two ice shelves the fight climbs onto.
            cfg.Arenas["arena_frostpeak_tundra"] = Arena("arena_frostpeak_tundra",
                Rect(0, 0, 46, 30, 0),
                Rect(-26, 14, 13, 11, 1), Rect(28, -12, 12, 10, 1));

            // Ember Caldera: a rock apron (disc covers the bubble) with a lava INLET biting the east
            // PERIMETER — omitted between the north/south lips that extend east past the core — and a
            // stacked central rise (tier-1 with a tier-2 cap) reading as the caldera cone.
            cfg.Arenas["arena_ember_caldera"] = Arena("arena_ember_caldera",
                Disc(-2, 0, 33, 0),              // core apron: fully covers the r≈32 bubble
                Rect(30, 22, 10, 8, 0), Rect(30, -22, 10, 8, 0), // NE/SE lips; the gap between = the inlet
                Disc(-8, 4, 12, 1), Disc(-8, 4, 6, 2));

            // Gloom Hollow: a cavern floor with two stalagmite plinths, one topped by a higher ledge.
            cfg.Arenas["arena_gloom_hollow"] = Arena("arena_gloom_hollow",
                Disc(0, 0, 39, 0),
                Disc(18, 16, 9, 1), Disc(-20, -14, 9, 1), Disc(18, 16, 5, 2));

            // Storm Coast: a headland (disc covers the bubble) with a shallow COVE biting the south
            // PERIMETER — omitted between the two shoulders that reach south past the core — and two
            // sea-stack rises the fight climbs onto.
            cfg.Arenas["arena_storm_coast"] = Arena("arena_storm_coast",
                Disc(0, 0, 33, 0),               // headland core: fully covers the r≈32 bubble
                Rect(-30, -30, 11, 8, 0), Rect(30, -30, 11, 8, 0), // shoulders framing the cove mouth
                Disc(24, 14, 10, 1), Disc(-26, 12, 8, 1));

            // Astral Ruins: floating-plaza vibe — a disc base with three rune platforms, one elevated.
            cfg.Arenas["arena_astral_ruins"] = Arena("arena_astral_ruins",
                Disc(0, 0, 40, 0),
                Rect(22, 16, 10, 8, 1), Rect(-24, 14, 9, 8, 1), Rect(0, -26, 10, 8, 1),
                Rect(22, 16, 5, 4, 2));

            // Crown of the World: a summit plateau with two ascending terraces climbing to a peak dais.
            cfg.Arenas["arena_crown_of_the_world"] = Arena("arena_crown_of_the_world",
                Rect(0, 0, 44, 32, 0),
                Rect(-24, 16, 14, 12, 1), Rect(-24, 16, 8, 7, 2), Rect(26, -14, 12, 10, 1));

            // Point every zone at its arena (id convention arena_<zoneId>).
            foreach (var z in cfg.Zones)
                if (cfg.Arenas.ContainsKey("arena_" + z.Id)) z.ArenaId = "arena_" + z.Id;

            for (int i = 0; i < 100; i++)
            {
                int stage = i + 1;
                int tier = cfg.Balance.Tier(stage);
                cfg.Stages.Add(new StageDef
                {
                    Stage = stage, MonsterLevel = stage, PackCount = 3 + stage / 5,
                    BossId = cfg.ZoneForStage(stage)?.BossId ?? "goblin_king", // each zone brings its own boss
                    DropRateMult = 1 + cfg.Balance.DropRatePerStage * (stage - 1) + cfg.Balance.DropRateTierBonus * tier,
                    AffixItemLevel = stage + cfg.Balance.ItemLevelTierBonus * tier,
                });
            }

            // Warrior actives: a spinning AoE slash + a shield-charge gap closer.
            // (cleave/warcry retired 2026-07-02 — Save.Migrate transfers invested ranks.)
            cfg.Skills["cycloneslash"] = new SkillDef
            {
                Id = "cycloneslash", Name = "Cyclone Slash", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 5000, Range = 1.8, AoeRadius = 2.2, DamageMult = 1.5, Sprite = "cleave",
            };
            cfg.Skills["shieldcharge"] = new SkillDef
            {
                Id = "shieldcharge", Name = "Shield Charge", Effect = SkillEffectKind.Dash, Targeting = "nearest",
                CooldownMs = 8000, Range = 6.0, DamageMult = 2.0, Sprite = "charge",
                UnlockLevel = 10,
            };
            // Library rows (no kit uses these today — §7.2 keeps the archetype shelf stocked).
            cfg.Skills["cleave"] = new SkillDef
            {
                Id = "cleave", Name = "Cleave", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 4000, Range = 1.8, AoeRadius = 1.6, DamageMult = 1.6, Sprite = "cleave",
            };
            cfg.Skills["warcry"] = new SkillDef
            {
                Id = "warcry", Name = "War Cry", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 9000, Range = 0, BuffStat = StatKey.Atk, BuffAmount = 10, BuffDurationMs = 6000, Sprite = "warcry", UnlockLevel = 10,
            };
            cfg.Skills["firebolt"] = new SkillDef
            {
                Id = "firebolt", Name = "Firebolt", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 3500, Range = 6.0, DamageMult = 1.8, Sprite = "firebolt",
            };
            cfg.Skills["mend"] = new SkillDef
            {
                Id = "mend", Name = "Mend", Effect = SkillEffectKind.Heal, Targeting = "lowestHp",
                CooldownMs = 7000, Range = 8.0, DamageMult = 2.0, Sprite = "mend",
            };

            // Lever 3 expanded kits (6 known per hero, pick 4). Sprites reuse existing FX for now;
            // bespoke VFX is a later polish pass. All use the existing damage/heal/buff effect kinds.
            // Warrior — single-target burst, big AoE, a defensive cooldown, an attack-speed tempo buff.
            cfg.Skills["bash"] = new SkillDef
            {
                Id = "bash", Name = "Bash", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 3000, Range = 1.4, DamageMult = 2.4, Sprite = "cleave",
            };
            cfg.Skills["whirlwind"] = new SkillDef
            {
                Id = "whirlwind", Name = "Whirlwind", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 6000, Range = 1.8, AoeRadius = 2.6, DamageMult = 1.4, Sprite = "cleave",
                UnlockLevel = 8,
            };
            cfg.Skills["bulwark"] = new SkillDef
            {
                Id = "bulwark", Name = "Bulwark", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 12000, Range = 0, BuffStat = StatKey.Def, BuffAmount = 15, BuffDurationMs = 6000, Sprite = "warcry", UnlockLevel = 14,
            };
            cfg.Skills["frenzy"] = new SkillDef
            {
                Id = "frenzy", Name = "Frenzy", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.AtkSpd, BuffAmount = 0.5, BuffDurationMs = 6000, Sprite = "warcry", UnlockLevel = 18,
            };
            // Fire Wizard — AoE fireball, a heavy single nuke, a big AoE ultimate, an attack-speed buff.
            cfg.Skills["fireball"] = new SkillDef
            {
                Id = "fireball", Name = "Fireball", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 5000, Range = 6.0, AoeRadius = 2.2, DamageMult = 1.6, Sprite = "firebolt",
                UnlockLevel = 10,
            };
            cfg.Skills["scorch"] = new SkillDef
            {
                Id = "scorch", Name = "Scorch", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 4500, Range = 6.0, DamageMult = 2.6, Sprite = "firebolt",
                UnlockLevel = 8,
            };
            cfg.Skills["inferno"] = new SkillDef
            {
                Id = "inferno", Name = "Inferno", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 12000, Range = 6.0, AoeRadius = 3.2, DamageMult = 2.2, Sprite = "quake",
                UnlockLevel = 16,
            };
            cfg.Skills["haste"] = new SkillDef
            {
                Id = "haste", Name = "Haste", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.AtkSpd, BuffAmount = 0.6, BuffDurationMs = 6000, Sprite = "warcry", UnlockLevel = 12,
            };

            // Thief — single-target assassin: a fast cheap stab, a heavy nuke, a tight AoE, and
            // three crit/tempo self-buffs (the build choice is which buffs make the 4-slot bar).
            // All on existing damage/buff kinds; sprites reuse warrior/mage FX until bespoke VFX.
            cfg.Skills["shadowstab"] = new SkillDef
            {
                Id = "shadowstab", Name = "Shadowstab", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 2200, Range = 1.4, DamageMult = 2.6, Sprite = "cleave",
            };
            cfg.Skills["vitalstrike"] = new SkillDef
            {
                Id = "vitalstrike", Name = "Vital Strike", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 5000, Range = 1.4, DamageMult = 3.8, Sprite = "cleave",
                UnlockLevel = 10,
            };
            cfg.Skills["bladewhirl"] = new SkillDef
            {
                Id = "bladewhirl", Name = "Bladewhirl", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 5500, Range = 2.0, AoeRadius = 2.2, DamageMult = 1.4, Sprite = "cleave",
                UnlockLevel = 8,
            };
            cfg.Skills["pinpoint"] = new SkillDef
            {
                Id = "pinpoint", Name = "Pinpoint", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.CritChance, BuffAmount = 0.25, BuffDurationMs = 6000, Sprite = "warcry",
            };
            cfg.Skills["quickstep"] = new SkillDef
            {
                Id = "quickstep", Name = "Quickstep", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 10000, Range = 0, BuffStat = StatKey.AtkSpd, BuffAmount = 0.6, BuffDurationMs = 6000, Sprite = "warcry", UnlockLevel = 10,
            };
            cfg.Skills["lethality"] = new SkillDef
            {
                Id = "lethality", Name = "Lethality", Effect = SkillEffectKind.Buff, Targeting = "self",
                CooldownMs = 12000, Range = 0, BuffStat = StatKey.CritDmg, BuffAmount = 0.6, BuffDurationMs = 6000, Sprite = "warcry", UnlockLevel = 16,
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
            cfg.Skills["attunement"] = new SkillDef   // Magician: sharpened focus (mana removed)
            {
                Id = "attunement", Name = "Attunement", Passive = true, UnlockLevel = 15,
                PassiveStat = StatKey.Atk, StatPerRank = 2.5, Sprite = "fireball",
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

            // Ice Mage kit (§7.2 cadence 1/5/10/15): a slow heavy nuke, a wide AoE, armor +
            // cadence passives. The frost VFX pass (slice 3, CombatView.cs) gives "frostbolt"
            // and "blizzard" real frost visuals; the damage-dealing skills point at them (the
            // passives still ride the shared bulwark/fireball cast icons).
            cfg.Skills["frostbolt"] = new SkillDef
            {
                Id = "frostbolt", Name = "Frostbolt", Effect = SkillEffectKind.Damage, Targeting = "nearest",
                CooldownMs = 4200, Range = 6.0, DamageMult = 2.2, Sprite = "frostbolt",
            };
            cfg.Skills["permafrost"] = new SkillDef  // Ice Mage: rimed armor
            {
                Id = "permafrost", Name = "Permafrost", Passive = true, UnlockLevel = 5,
                PassiveStat = StatKey.Def, StatPerRank = 1.5, Sprite = "bulwark",
            };
            cfg.Skills["blizzard"] = new SkillDef
            {
                Id = "blizzard", Name = "Blizzard", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 6500, Range = 6.0, AoeRadius = 2.6, DamageMult = 1.5, Sprite = "blizzard",
                UnlockLevel = 10,
            };
            cfg.Skills["frostflow"] = new SkillDef   // Ice Mage: glacial cadence (mana removed)
            {
                Id = "frostflow", Name = "Frostflow", Passive = true, UnlockLevel = 15,
                PassiveStat = StatKey.AtkSpd, StatPerRank = 0.03, Sprite = "fireball",
            };

            // Priest kit (§7.2 cadence 1/5/10/15) — the party HoT is the identity: every
            // living ally regens BuffAmount x MaxHp per second for the duration (rank
            // scales the rate). Gated in Combat.TryCastSkill on someone being hurt.
            cfg.Skills["sanctify"] = new SkillDef
            {
                Id = "sanctify", Name = "Sanctify", Effect = SkillEffectKind.Buff, Targeting = "party",
                CooldownMs = 15000, Range = 0, BuffStat = StatKey.HpRegenPct, BuffAmount = 0.20,
                BuffDurationMs = 10000, Sprite = "warcry",
            };
            cfg.Skills["devotion"] = new SkillDef    // Priest: enduring body
            {
                Id = "devotion", Name = "Devotion", Passive = true, UnlockLevel = 5,
                PassiveStat = StatKey.HpRegen, StatPerRank = 0.6, Sprite = "bulwark",
            };
            cfg.Skills["holysmite"] = new SkillDef
            {
                Id = "holysmite", Name = "Holy Smite", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 6000, Range = 6.0, AoeRadius = 2.4, DamageMult = 1.6, Sprite = "quake",
                UnlockLevel = 10,
            };
            cfg.Skills["benediction"] = new SkillDef // Priest: flowing grace (mana removed)
            {
                Id = "benediction", Name = "Benediction", Passive = true, UnlockLevel = 15,
                PassiveStat = StatKey.HpRegen, StatPerRank = 0.6, Sprite = "fireball",
            };

            // Boss signature: a wide quake (free — bosses have no mana pool).
            cfg.Skills["boss_quake"] = new SkillDef
            {
                Id = "boss_quake", Name = "Quake", Effect = SkillEffectKind.Damage, Targeting = "aoe",
                CooldownMs = 8000, Range = 3.0, AoeRadius = 3.0, DamageMult = 1.4, Sprite = "quake",
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

            // ---- Gacha banners (roadmap 3, slice 3) — the gem SINK goes live. ------------
            // "Winter's Return": the Ice Mage comeback banner. The Ice Mage def is shelved
            // from the stage unlock table (see ~L762), so THIS banner is the only way to
            // obtain her — and SyncHeroUnlocks keeps every banner-pool hero obtainable while
            // the banner is live (Progression.cs ~L167). CostGems 10 = one day-1 daily login,
            // so a single login funds a roll. Pool: featured icemage weight 1 against four
            // owned-roster fillers weight 3 each ⇒ natural featured ≈ 1/13 ≈ 7.7%; PityCount
            // 20 guarantees her by the 20th roll. Dupe rewards ≈ 25 min of active mid-game
            // (~stage 40) farming: XpPerKill≈16·TierScale(40)≈1.65k over ~1 kill/s for 1500s
            // ⇒ ~2M XP; scrap sized to the same window's salvage trickle ⇒ 500.
            cfg.Banners["winters_return"] = new GachaBannerDef
            {
                Id = "winters_return", Name = "Winter's Return",
                CostGems = 10, FeaturedHeroDefId = "icemage_basic",
                PityCount = 20, DupeXp = 2_000_000, DupeScrap = 500,
                Pool = new List<GachaPoolEntry>
                {
                    new GachaPoolEntry { HeroDefId = "icemage_basic",    Weight = 1 },
                    new GachaPoolEntry { HeroDefId = "warrior_basic",    Weight = 3 },
                    new GachaPoolEntry { HeroDefId = "magician_basic",   Weight = 3 },
                    new GachaPoolEntry { HeroDefId = "thief_basic",      Weight = 3 },
                    new GachaPoolEntry { HeroDefId = "priest_basic",     Weight = 3 },
                },
            };

            return cfg;
        }
    }
}
