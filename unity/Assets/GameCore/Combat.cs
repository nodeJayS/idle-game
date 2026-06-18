#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Deterministic auto-battle (M1). Light spatial model: each entity walks
    /// toward its nearest enemy and auto-attacks when in range and off cooldown.
    /// Enough to LOOK like an ARPG while staying cheap and deterministic; the
    /// renderer interpolates positions between fixed steps.
    ///
    /// Determinism: entities act in a stable order (sorted by Id); RNG is only
    /// consumed for crit rolls, in that same order. Same seed + inputs => same
    /// fight, which is what makes it unit-testable and server-verifiable later.
    /// </summary>
    public static class Combat
    {
        public const double AttackRange = 1.0;        // tiles (melee)
        public const double MoveSpeedTilesPerSec = 3.0;
        public const double DefaultStepMs = 1000.0 / 30.0;

        private static double AttackInterval(StatBlock s)
            => 1000.0 / Math.Max(0.1, s.Get(StatKey.Spd));

        /// <summary>Build the initial battle: party (left) vs the stage's pack + boss (right).</summary>
        public static CombatState InitCombat(IReadOnlyList<HeroInstance> party, int stage, GameConfig cfg, Rng rng)
        {
            var s = new CombatState { Stage = stage };

            int idx = 0;
            foreach (var hero in party)
            {
                var stats = Stats.ComputeHeroStats(hero, cfg);
                double hp = stats.Get(StatKey.Hp);
                s.Entities.Add(new CombatEntity
                {
                    Id = "P" + idx + "_" + hero.Id,
                    Team = Team.Party,
                    Pos = new Vec2(-3, idx * 1.5),
                    Stats = stats,
                    Hp = hp,
                    MaxHp = hp,
                    AttackIntervalMs = AttackInterval(stats),
                    RefKind = "hero",
                    RefId = hero.Id,
                });
                idx++;
            }

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt);
            double scale = 1.0 + 0.1 * (rt.MonsterLevel - 1);

            for (int j = 0; j < rt.PackCount; j++)
            {
                var mdef = (j % 2 == 0) ? cfg.Monsters["slime"] : cfg.Monsters["goblin"];
                s.Entities.Add(MakeMonster(mdef, "E" + j, new Vec2(3, j * 1.5), scale, false));
            }

            if (cfg.Monsters.TryGetValue(rt.BossId, out var boss))
            {
                double bossScale = rt.IsMajorBoss ? scale * cfg.Balance.MajorBossMult : scale;
                s.Entities.Add(MakeMonster(boss, "EBOSS", new Vec2(5, rt.PackCount * 0.75), bossScale, true));
            }

            return s;
        }

        private static CombatEntity MakeMonster(MonsterDef def, string id, Vec2 pos, double scale, bool isBoss)
        {
            var stats = new StatBlock();
            foreach (var kv in def.BaseStats) stats[kv.Key] = kv.Value;
            // scale the "size" stats with monster level; leave rate/crit stats as-is
            stats[StatKey.Hp] = stats.Get(StatKey.Hp) * scale;
            stats[StatKey.Atk] = stats.Get(StatKey.Atk) * scale;
            stats[StatKey.Def] = stats.Get(StatKey.Def) * scale;
            double hp = stats.Get(StatKey.Hp);

            return new CombatEntity
            {
                Id = id,
                Team = Team.Enemy,
                Pos = pos,
                Stats = stats,
                Hp = hp,
                MaxHp = hp,
                AttackIntervalMs = AttackInterval(stats),
                RefKind = "monster",
                RefId = def.Id,
                IsBoss = isBoss,
            };
        }

        /// <summary>Advance the sim one fixed step. Mutates state; returns this step's events.</summary>
        public static List<CombatEvent> StepCombat(CombatState s, double dtMs, GameConfig cfg, Rng rng)
        {
            var events = new List<CombatEvent>();
            if (s.Status != CombatStatus.Running) return events;

            s.TimeMs += dtMs;

            var actors = s.Entities.Where(e => e.Alive)
                                   .OrderBy(e => e.Id, StringComparer.Ordinal)
                                   .ToList();

            foreach (var e in actors)
            {
                if (!e.Alive) continue; // could have died earlier this step

                if (e.AttackCdMs > 0) e.AttackCdMs = Math.Max(0, e.AttackCdMs - dtMs);

                var target = FindNearestEnemy(s, e);
                e.TargetId = target?.Id;
                if (target == null) continue;

                double dist = Vec2.Distance(e.Pos, target.Pos);
                if (dist <= AttackRange)
                {
                    if (e.AttackCdMs <= 0)
                    {
                        double dmg = Math.Max(1.0, e.Stats.Get(StatKey.Atk) - target.Stats.Get(StatKey.Def));
                        bool crit = rng.Next() < e.Stats.Get(StatKey.CritChance);
                        if (crit) dmg *= Math.Max(1.0, e.Stats.Get(StatKey.CritDmg));

                        target.Hp -= dmg;
                        e.AttackCdMs = e.AttackIntervalMs;
                        events.Add(new CombatEvent
                        {
                            Type = CombatEventType.Hit,
                            SourceId = e.Id,
                            TargetId = target.Id,
                            Amount = dmg,
                            Crit = crit,
                        });

                        if (target.Hp <= 0)
                        {
                            target.Hp = 0;
                            events.Add(new CombatEvent { Type = CombatEventType.Death, EntityId = target.Id });
                            if (target.IsBoss)
                                events.Add(new CombatEvent { Type = CombatEventType.BossDefeated, Stage = s.Stage });

                            // Loot + XP only from real monsters (guards synthetic test/party entities).
                            if (target.Team == Team.Enemy && target.RefKind == "monster" &&
                                cfg.Monsters.TryGetValue(target.RefId, out var mdef))
                            {
                                s.PendingXp += mdef.XpReward;

                                var drop = Loot.RollDrop(rng, mdef, s.Loot, cfg);
                                if (drop != null)
                                {
                                    s.PendingLoot.Add(drop);
                                    events.Add(new CombatEvent
                                    {
                                        Type = CombatEventType.LootDrop,
                                        EntityId = target.Id,
                                        Item = drop,
                                    });
                                }
                            }
                        }
                    }
                }
                else
                {
                    MoveToward(e, target.Pos, MoveSpeedTilesPerSec * dtMs / 1000.0);
                }
            }

            bool partyAlive = s.Entities.Any(e => e.Team == Team.Party && e.Alive);
            bool enemyAlive = s.Entities.Any(e => e.Team == Team.Enemy && e.Alive);
            if (!partyAlive) s.Status = CombatStatus.Lost;
            else if (!enemyAlive) s.Status = CombatStatus.Won;

            return events;
        }

        /// <summary>Run fixed steps until the fight ends (or maxSteps). Used for instant/offline resolution.</summary>
        public static List<CombatEvent> RunToEnd(CombatState s, GameConfig cfg, Rng rng,
                                                 int maxSteps = 100000, double dtMs = DefaultStepMs)
        {
            var all = new List<CombatEvent>();
            int steps = 0;
            while (s.Status == CombatStatus.Running && steps < maxSteps)
            {
                all.AddRange(StepCombat(s, dtMs, cfg, rng));
                steps++;
            }
            return all;
        }

        private static CombatEntity? FindNearestEnemy(CombatState s, CombatEntity self)
        {
            CombatEntity? best = null;
            double bestDist = double.MaxValue;
            foreach (var other in s.Entities)
            {
                if (!other.Alive || other.Team == self.Team) continue;
                double d = Vec2.Distance(self.Pos, other.Pos);
                if (d < bestDist || (d == bestDist && best != null &&
                                     string.CompareOrdinal(other.Id, best.Id) < 0))
                {
                    bestDist = d;
                    best = other;
                }
            }
            return best;
        }

        private static void MoveToward(CombatEntity e, Vec2 dest, double maxStep)
        {
            double dx = dest.X - e.Pos.X;
            double dy = dest.Y - e.Pos.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist <= maxStep || dist == 0)
            {
                e.Pos = new Vec2(dest.X, dest.Y);
                return;
            }
            e.Pos = new Vec2(e.Pos.X + dx / dist * maxStep, e.Pos.Y + dy / dist * maxStep);
        }
    }
}
