#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    // ------------------------------------------------------------------------
    // Transient combat sim state. NEVER persisted. Unlike SaveState reducers
    // (which are pure), the combat sim mutates CombatState in place per fixed
    // step for performance — it's deterministic given the same seed + inputs,
    // which is what matters for testing and future server re-validation.
    // ------------------------------------------------------------------------

    public struct Vec2
    {
        public double X;
        public double Y;

        public Vec2(double x, double y) { X = x; Y = y; }

        public static double Distance(Vec2 a, Vec2 b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public enum Team { Party, Enemy }

    public enum CombatStatus { Running, Won, Lost }

    /// <summary>
    /// Pack variety (Lever 1): a spawned trash mob's rank. Normal = ordinary trash.
    /// Elite/Rare are highlighted, tougher, bigger, and drop a boosted loot bundle — the
    /// PoE blue/yellow-pack risk/reward spike on the open field. Rolled per-mob at spawn
    /// (farm packs only); bosses are their own thing (IsBoss), not a rank.
    /// </summary>
    public enum MonsterRank { Normal, Elite, Rare }

    /// <summary>
    /// Encounter type (M8). Encounter = the classic clear-the-enemies fight. Farm = an
    /// endless zone: trash respawns up to a cap, never auto-wins, only a wipe loses.
    /// BossChallenge = a single boss under a short timer (the gate that advances a
    /// stage): win by killing it in time, lose on the timer expiring or a wipe.
    /// </summary>
    /// Tower = a Tower-of-Ascension floor: a bounded one-clear fight (steeper, modified pack;
    /// no respawns, no farm income) that advances the tower track on a win.
    /// Dungeon = a procedurally-generated roguelite floor: the party auto-battles through grid-walkable
    /// rooms/corridors toward the boss; win = boss dead, lose = wipe or the failsafe timeout. No respawns.
    public enum EncounterKind { Encounter, Farm, BossChallenge, Tower, Dungeon }

    /// <summary>
    /// How the party moves/targets. Solo = formation travel: the lowest-slot living hero
    /// LEADS toward the nearest pack and the rest hold a triangle behind it, only breaking
    /// off to fight an enemy that's right next to them — so the team reads as a cohesive
    /// unit instead of fanning out. Group = the whole party focus-fires the enemy nearest
    /// the party's centre and clusters on it.
    /// </summary>
    public enum PartyTactic { Solo, Group }

    public sealed class CombatEntity
    {
        public string Id = "";
        public Team Team;
        public Vec2 Pos;
        // Soft-body radius (M-feel): the unit occupies a circle of this radius, so units
        // can't stack on the same point — overlaps are pushed apart each step. Set from
        // Balance.UnitRadius (heroes/trash) or Balance.BossRadius at creation. Attack/skill
        // ranges count from a target's body edge, so a chunky body stays reachable.
        public double BodyRadius = 0.45;
        public StatBlock Stats = new StatBlock();
        public double Hp;
        public double MaxHp;

        // Skills (M11): the entity's loadout (skill ids), per-skill cooldown remaining,
        // and active timed buffs. Heroes get these from their revealed kit actives (Skills.ActiveKit); bosses from
        // their MonsterDef. EffectiveStat folds buffs into the read used by combat.
        public List<string> Skills = new List<string>();
        public Dictionary<string, double> SkillCdMs = new Dictionary<string, double>();
        // Build depth (Lever 3): invested rank per skill (heroes only; bosses stay rank 0). A skill's
        // primary magnitude scales by (1 + SkillDef.EffectPerRank*rank). Mirrors HeroInstance.SkillRanks (MaxRank => mastery, see Skills.EffectiveRank).
        public Dictionary<string, int> SkillRanks = new Dictionary<string, int>();
        public List<ActiveBuff> Buffs = new List<ActiveBuff>();

        public string? TargetId;
        public double AttackCdMs;       // remaining cooldown
        public double AttackIntervalMs; // 1000 / Spd
        public string RefKind = "";     // "hero" | "monster"
        public string RefId = "";       // heroId or monster defId
        public bool IsBoss;
        // Pack variety: Elite/Rare trash (highlighted, tougher, better loot). Normal for
        // heroes, bosses, and ordinary trash. Set by Combat.ApplyRank at spawn.
        public MonsterRank Rank = MonsterRank.Normal;

        // Monster modifiers (Lever 1), set by Combat.ApplyModifier at spawn (farm trash) or boss
        // init. ModTypes = the modifier typeIds on this mob (drives the client aura tell + marks a
        // modified kill). Lifesteal/ThornsReflect = precomputed behavior fractions read per-hit in
        // ApplyHit. Gold/Xp/DropRate buffs are folded once at apply time so HandleDeath needs no
        // save lookup: GoldMult/XpMult start at 1 (multiply the payout), DropRateBonus adds to the
        // loot context's rarity bias.
        public List<string> ModTypes = new List<string>();
        public double Lifesteal;      // heals this fraction of damage it deals (Vampiric)
        public double ThornsReflect;  // reflects this fraction of damage taken (Thorns)
        public double GoldMult = 1.0;
        public double XpMult = 1.0;
        public double DropRateBonus;  // additive bonus to LootContext.DropRateMult on this kill
        // Party slot (0 = first slot). The lowest-slot living hero leads the Solo formation;
        // the rest follow in a triangle behind it. Non-heroes leave this at int.MaxValue.
        public int Slot = int.MaxValue;

        // Dungeon (roguelite): the room this enemy was SEEDED in (its spawn's RoomId), so the
        // room-sweep objective can attribute a mob that wandered/chased into a corridor to its home
        // room. Set at dungeon spawn seeding; heroes and every non-dungeon entity leave this -1.
        public int DungeonRoomId = -1;

        // Role axis (formation): a ranged hero (HeroDef.Role == "ranged") parks at casting
        // distance, fires at what's already in reach mid-regroup, and backpedals from anything
        // that closes on it. Set in AddParty from the hero's def; every other entity leaves it
        // false (melee flanks the leader's shoulder; the leader defaults to the first melee hero).
        public bool RangedRole;

        // Aggro (M-combat): a non-aggro enemy ambles randomly (WanderTarget) and ignores the
        // party until something hits it (then it fights back). Defaults TRUE so heroes, bosses,
        // and synthetic test entities behave normally; farm trash is spawned non-aggro.
        public bool Aggro = true;
        public Vec2 WanderTarget;
        public double WanderCdMs;

        // Hero downing (M4.3): a party hero at 0 HP is "downed", not dead — it
        // respawns after RespawnMs counts down. RespawnDurationMs is the level-scaled
        // base set at InitCombat. Monsters leave these at 0 (they die permanently).
        public double RespawnMs;
        public double RespawnDurationMs;

        public bool Alive => Hp > 0;
        public bool Downed => Hp <= 0 && RespawnMs > 0;

        /// <summary>Base stat plus any active buffs on it — the value combat should read.</summary>
        public double EffectiveStat(StatKey k)
        {
            double v = Stats.Get(k);
            foreach (var b in Buffs) if (b.Stat == k) v += b.Amount;
            return v;
        }
    }

    /// <summary>A timed additive stat buff on a combat entity (M11 skills).</summary>
    public sealed class ActiveBuff
    {
        public StatKey Stat;
        public double Amount;
        public double RemainingMs;
    }

    public sealed class CombatState
    {
        public double TimeMs;
        public int Stage;
        // Arena (M8 slice 1): the per-stage layout this encounter is fought on, pinned at
        // init/transition. Transient like the rest of CombatState — a hand-built state leaves it
        // null, which keeps synthetic fights (and legacy saves) on the open plane. null => no
        // walkable clamp; movement/spawns behave exactly as they did pre-arena.
        public string? ArenaId;
        // Dungeon (roguelite slice 3a): the grid-walkable surface for a Kind == Dungeon run — set by
        // Combat.InitDungeon, null everywhere else. When non-null it OVERRIDES ArenaId as the arena
        // surface (ArenaOf returns it first) and unlocks the room-gated targeting + BFS leader travel.
        // Transient like the rest of CombatState; never persisted.
        public DungeonArena? Dungeon;
        public int TowerFloor;         // the tower floor this fight represents (Kind == Tower); 0 otherwise
        public EncounterKind Kind = EncounterKind.Encounter;
        public PartyTactic Tactic = PartyTactic.Solo;
        // Chosen formation leader (a hero RefId), mirrored from SaveState.LeaderHeroId. null
        // (or a downed/absent hero) => the lowest-slot living hero leads. See StepCombat.
        public string? LeaderRefId;
        // Sticky Solo-formation heading (leader→pack). Frozen while the leader is engaged so the
        // follower wing holds the approach direction instead of whipping around the noisy ~1-unit
        // vector to a pack the leader stands inside; only refreshed while traveling. See StepCombat.
        public Vec2 FormationHeading;
        public LootContext Loot;       // set by InitCombat; mode-agnostic drop params
        public List<CombatEntity> Entities = new List<CombatEntity>();
        public CombatStatus Status = CombatStatus.Running;
        public List<Item> PendingLoot = new List<Item>(); // drops accrued this run (M2)
        public long PendingXp;                            // XP accrued this run (M3); long for deep-stage scaling
        public long PendingGold;                          // gold accrued this run (M8)

        // Farm-mode spawning (M8): countdown to the next trash spawn, and a monotonic
        // counter used for unique entity ids + slime/goblin alternation.
        public double SpawnTimerMs;
        public int SpawnCount;

        // Active monster modifiers (Lever 1): applied to every spawned farm trash mob. Set by the
        // client from the save (Modifiers.ResolveActive). Empty in boss/encounter modes — the boss
        // gets only its own inherent modifier, applied directly at init.
        public List<ModifierInstance> ActiveModifiers = new List<ModifierInstance>();
    }

    /// <summary>An active monster modifier handed to the sim by the client (resolved from the
    /// save: a <see cref="ModifierDef"/> + its banked strength). Farm trash gets all of these
    /// applied at spawn. Transient — never persisted (the SaveState holds the owned/active ids).</summary>
    public sealed class ModifierInstance
    {
        public ModifierDef Def = null!;
        public int Strength;
        public double Tuning = 1.0; // shop tuning multiplier on danger+reward (1.0 = untuned)
    }

    public enum CombatEventType { Hit, Death, LootDrop, LevelUp, WaveCleared, BossDefeated, Respawn, SkillCast, Heal }

    /// <summary>Flat event the renderer reacts to (damage numbers, deaths, etc.).</summary>
    public sealed class CombatEvent
    {
        public CombatEventType Type;
        public string? SourceId;
        public string? TargetId;
        public double Amount;
        public bool Crit;
        public string? EntityId;
        public int Stage;
        public Item? Item;          // set on LootDrop; EntityId = the monster that dropped it
        public string? SkillId;     // set on SkillCast: which skill fired (renderer FX hook)
    }
}
