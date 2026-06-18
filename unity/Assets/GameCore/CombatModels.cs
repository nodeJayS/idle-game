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

    public sealed class CombatEntity
    {
        public string Id = "";
        public Team Team;
        public Vec2 Pos;
        public StatBlock Stats = new StatBlock();
        public double Hp;
        public double MaxHp;
        public string? TargetId;
        public double AttackCdMs;       // remaining cooldown
        public double AttackIntervalMs; // 1000 / Spd
        public string RefKind = "";     // "hero" | "monster"
        public string RefId = "";       // heroId or monster defId
        public bool IsBoss;

        // Hero downing (M4.3): a party hero at 0 HP is "downed", not dead — it
        // respawns after RespawnMs counts down. RespawnDurationMs is the level-scaled
        // base set at InitCombat. Monsters leave these at 0 (they die permanently).
        public double RespawnMs;
        public double RespawnDurationMs;

        public bool Alive => Hp > 0;
        public bool Downed => Hp <= 0 && RespawnMs > 0;
    }

    public sealed class CombatState
    {
        public double TimeMs;
        public int Stage;
        public EncounterKind Kind = EncounterKind.Encounter;
        public LootContext Loot;       // set by InitCombat; mode-agnostic drop params
        public List<CombatEntity> Entities = new List<CombatEntity>();
        public CombatStatus Status = CombatStatus.Running;
        public List<Item> PendingLoot = new List<Item>(); // drops accrued this run (M2)
        public int PendingXp;                             // XP accrued this run (M3)

        // Farm-mode spawning (M8): countdown to the next trash spawn, and a monotonic
        // counter used for unique entity ids + slime/goblin alternation.
        public double SpawnTimerMs;
        public int SpawnCount;
    }

    public enum CombatEventType { Hit, Death, LootDrop, LevelUp, WaveCleared, BossDefeated, Respawn }

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
    }
}
