#nullable enable
using System;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Player-facing combat readouts derived from a hero's <see cref="StatBlock"/> — DPS and
    /// Effective Life — for the stats panel and the equipment hover preview (B2). Pure GameCore:
    /// these mirror the exact combat math in <see cref="Combat"/> (ApplyHit + AttackInterval) so
    /// the numbers a player reads match what the sim actually does. No buffs are folded in: these
    /// are the "sheet" values from base + level + gear, the right basis for comparing equipment.
    /// </summary>
    public static class DerivedStats
    {
        /// <summary>Attacks (or casts) per second. Mirrors <c>Combat.AttackSpeedOf</c>: the AtkSpd
        /// stat, defaulting to 1.0 when missing or non-positive.</summary>
        public static double AttacksPerSecond(StatBlock s)
        {
            double a = s.Get(StatKey.AtkSpd);
            return a > 0 ? a : 1.0;
        }

        /// <summary>Expected damage multiplier from crits per hit. The sim rolls a crit with
        /// probability CritChance and, on a crit, multiplies damage by max(1, CritDmg); so the
        /// average factor is 1 + p·(max(1,CritDmg) − 1), with p clamped to [0,1].</summary>
        public static double CritMultiplier(StatBlock s)
        {
            double p = Math.Clamp(s.Get(StatKey.CritChance), 0.0, 1.0);
            double critDmg = Math.Max(1.0, s.Get(StatKey.CritDmg));
            return 1.0 + p * (critDmg - 1.0);
        }

        /// <summary>
        /// Basic-attack DPS against an unarmored target: max(1, Atk) × attacks/sec × expected crit
        /// multiplier. Matches the per-hit damage in <see cref="Combat"/> (max(1, Atk − Def), here
        /// with Def = 0) at the basic-attack rate. Target Def is excluded on purpose — flat
        /// mitigation is target-specific, so the sheet number is the unmitigated rate. Skills and
        /// splash are situational and not counted.
        /// </summary>
        public static double Dps(StatBlock s)
            => Math.Max(1.0, s.Get(StatKey.Atk)) * AttacksPerSecond(s) * CritMultiplier(s);

        /// <summary>
        /// Effective Life: raw incoming damage absorbed before dying, against an attacker whose
        /// hits are <paramref name="referenceHit"/> each. The sim mitigates flatly — a hit costs
        /// max(1, R − Def) — so hits survived = Hp / max(1, R − Def) and the raw damage soaked is
        /// Hp × R / max(1, R − Def). With Def = 0 this is just Hp; high Def (≥ R) floors the per-hit
        /// cost at 1, making each point of life go a long way (exactly as the sim plays out).
        /// </summary>
        public static double EffectiveHp(StatBlock s, double referenceHit)
        {
            double r = Math.Max(1.0, referenceHit);
            double perHit = Math.Max(1.0, r - s.Get(StatKey.Def));
            return s.Get(StatKey.Hp) * r / perHit;
        }

        /// <summary>
        /// A representative incoming hit for a stage: the stage boss's Atk scaled by the per-stage
        /// damage growth (the same geometric scale the sim applies, sans the major-boss multiplier).
        /// The boss is the survival gate that advances the stage, so Effective Life read against it
        /// answers "how well does this build survive here?". Falls back to 1.0 for an unknown stage.
        /// </summary>
        public static double StageReferenceHit(GameConfig cfg, int stage)
        {
            var rt = cfg.StageFor(stage);
            if (rt == null) return 1.0;
            double scale = cfg.Balance.MonsterDmgMult(rt.MonsterLevel);
            double baseAtk = cfg.Monsters.TryGetValue(rt.BossId, out var boss) ? boss.BaseStats.Get(StatKey.Atk) : 1.0;
            return baseAtk * scale;
        }

        /// <summary>Effective Life against the given stage's representative boss hit
        /// (<see cref="StageReferenceHit"/>).</summary>
        public static double EffectiveHp(StatBlock s, GameConfig cfg, int stage)
            => EffectiveHp(s, StageReferenceHit(cfg, stage));
    }
}
