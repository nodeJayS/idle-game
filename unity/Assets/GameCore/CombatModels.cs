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

        public bool Alive => Hp > 0;
    }

    public sealed class CombatState
    {
        public double TimeMs;
        public int Tier;
        public LootContext Loot;       // set by InitCombat; mode-agnostic drop params
        public List<CombatEntity> Entities = new List<CombatEntity>();
        public CombatStatus Status = CombatStatus.Running;
        public List<Item> PendingLoot = new List<Item>(); // drops accrued this run (M2)
    }

    public enum CombatEventType { Hit, Death, LootDrop, LevelUp, WaveCleared, BossDefeated }

    /// <summary>Flat event the renderer reacts to (damage numbers, deaths, etc.).</summary>
    public sealed class CombatEvent
    {
        public CombatEventType Type;
        public string? SourceId;
        public string? TargetId;
        public double Amount;
        public bool Crit;
        public string? EntityId;
        public int Tier;
        public Item? Item;          // set on LootDrop; EntityId = the monster that dropped it
    }
}
