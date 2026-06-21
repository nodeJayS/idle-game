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
    /// Encounter type (M8). Encounter = the classic clear-the-enemies fight. Farm = an
    /// endless zone: trash respawns up to a cap, never auto-wins, only a wipe loses.
    /// BossChallenge = a single boss under a short timer (the gate that advances a
    /// stage): win by killing it in time, lose on the timer expiring or a wipe.
    /// </summary>
    public enum EncounterKind { Encounter, Farm, BossChallenge }

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
        // Skill resource (M10). Heroes start full and regenerate via ManaRegen; basic
        // attacks are free, so today mana only fills — skills spend it once they fire.
        // Monsters leave MaxMana at 0 (they don't cast).
        public double Mana;
        public double MaxMana;

        // Skills (M11): the entity's loadout (skill ids), per-skill cooldown remaining,
        // and active timed buffs. Heroes get these from their SkillLoadout; bosses from
        // their MonsterDef. EffectiveStat folds buffs into the read used by combat.
        public List<string> Skills = new List<string>();
        public Dictionary<string, double> SkillCdMs = new Dictionary<string, double>();
        public List<ActiveBuff> Buffs = new List<ActiveBuff>();

        public string? TargetId;
        public double AttackCdMs;       // remaining cooldown
        public double AttackIntervalMs; // 1000 / Spd
        public string RefKind = "";     // "hero" | "monster"
        public string RefId = "";       // heroId or monster defId
        public bool IsBoss;
        // Party slot (0 = first slot). The lowest-slot living hero leads the Solo formation;
        // the rest follow in a triangle behind it. Non-heroes leave this at int.MaxValue.
        public int Slot = int.MaxValue;

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
        public EncounterKind Kind = EncounterKind.Encounter;
        public PartyTactic Tactic = PartyTactic.Solo;
        // Chosen formation leader (a hero RefId), mirrored from SaveState.LeaderHeroId. null
        // (or a downed/absent hero) => the lowest-slot living hero leads. See StepCombat.
        public string? LeaderRefId;
        public LootContext Loot;       // set by InitCombat; mode-agnostic drop params
        public List<CombatEntity> Entities = new List<CombatEntity>();
        public CombatStatus Status = CombatStatus.Running;
        public List<Item> PendingLoot = new List<Item>(); // drops accrued this run (M2)
        public int PendingXp;                             // XP accrued this run (M3)
        public long PendingGold;                          // gold accrued this run (M8)

        // Farm-mode spawning (M8): countdown to the next trash spawn, and a monotonic
        // counter used for unique entity ids + slime/goblin alternation.
        public double SpawnTimerMs;
        public int SpawnCount;
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
