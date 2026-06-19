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
        public const double MeleeRange = 1.0;         // fallback range when an entity has no AttackRange stat
        public const double MoveSpeedTilesPerSec = 3.0;
        public const double DefaultStepMs = 1000.0 / 30.0;

        private static double AttackInterval(StatBlock s)
            => 1000.0 / Math.Max(0.1, s.Get(StatKey.Spd));

        /// <summary>Build the initial battle: party (left) vs the stage's pack + boss (right).</summary>
        public static CombatState InitCombat(IReadOnlyList<HeroInstance> party, int stage, GameConfig cfg, Rng rng)
        {
            var s = new CombatState { Stage = stage, Kind = EncounterKind.Encounter };
            AddParty(s, party, cfg);

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt);
            double scale = StageScale(rt);

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

        /// <summary>
        /// Build an endless farm zone (M8): party vs continuously-respawning trash (no
        /// boss). Trash is capped at Balance.MobCap and refilled one per
        /// Balance.SpawnIntervalMs in <see cref="StepCombat"/>; the run never auto-wins
        /// and only a full party wipe loses.
        /// </summary>
        public static CombatState InitFarm(IReadOnlyList<HeroInstance> party, int stage, GameConfig cfg, Rng rng)
        {
            var s = new CombatState { Stage = stage, Kind = EncounterKind.Farm };
            AddParty(s, party, cfg);

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt);

            int initial = Math.Min(cfg.Balance.SpawnBatchSize, cfg.Balance.MobCap);
            for (int i = 0; i < initial; i++) SpawnTrash(s, rt, cfg, rng);

            s.SpawnTimerMs = cfg.Balance.SpawnIntervalMs;
            return s;
        }

        /// <summary>
        /// Build the timed boss challenge (M8): party vs the stage's lone boss (mini, or
        /// the scaled major boss on every 10th stage). Win by killing it before
        /// Balance.BossChallengeSeconds elapses; lose on the timer or a wipe. Clearing
        /// it is what advances the stage.
        /// </summary>
        public static CombatState InitBossChallenge(IReadOnlyList<HeroInstance> party, int stage, GameConfig cfg, Rng rng)
        {
            var s = new CombatState { Stage = stage, Kind = EncounterKind.BossChallenge };
            AddParty(s, party, cfg);

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt);

            if (cfg.Monsters.TryGetValue(rt.BossId, out var boss))
            {
                double bossScale = rt.IsMajorBoss ? StageScale(rt) * cfg.Balance.MajorBossMult : StageScale(rt);
                s.Entities.Add(MakeMonster(boss, "EBOSS", new Vec2(4, 0), bossScale, true));
            }

            return s;
        }

        /// <summary>
        /// Re-derive each living party hero's combat stats from the current save (level
        /// + equipped gear), so leveling up or swapping gear takes effect immediately
        /// without restarting the encounter. An increase in max HP also heals by the
        /// gain (a level-up feels rewarding); a downed hero keeps its 0 HP and respawns
        /// at the new max. Mutates the combat state in place; deterministic.
        /// </summary>
        public static void RefreshPartyStats(CombatState s, SaveState save, GameConfig cfg)
        {
            foreach (var e in s.Entities)
            {
                if (e.Team != Team.Party || e.RefKind != "hero") continue;
                var hero = save.Heroes.Find(h => h.Id == e.RefId);
                if (hero == null) continue;

                var stats = Stats.ComputeHeroStats(hero, cfg, Stats.ResolveEquipped(save, hero));
                double oldMax = e.MaxHp;
                double newMax = stats.Get(StatKey.Hp);

                e.Stats = stats;
                e.MaxHp = newMax;
                e.AttackIntervalMs = AttackInterval(stats);
                if (e.Hp > 0) e.Hp = Math.Min(newMax, e.Hp + Math.Max(0.0, newMax - oldMax));
            }
        }

        private static double StageScale(StageDef rt) => 1.0 + 0.1 * (rt.MonsterLevel - 1);

        private static void AddParty(CombatState s, IReadOnlyList<HeroInstance> party, GameConfig cfg)
        {
            double px = -cfg.Balance.MapHalfWidth * 0.6; // party lines up on the left
            int n = party.Count;
            int idx = 0;
            foreach (var hero in party)
            {
                var stats = Stats.ComputeHeroStats(hero, cfg);
                double hp = stats.Get(StatKey.Hp);
                s.Entities.Add(new CombatEntity
                {
                    Id = "P" + idx + "_" + hero.Id,
                    Team = Team.Party,
                    Pos = new Vec2(px, (idx - (n - 1) / 2.0) * 2.0),
                    Stats = stats,
                    Hp = hp,
                    MaxHp = hp,
                    AttackIntervalMs = AttackInterval(stats),
                    RefKind = "hero",
                    RefId = hero.Id,
                    RespawnDurationMs = cfg.Balance.RespawnBaseMs + cfg.Balance.RespawnPerLevelMs * hero.Level,
                });
                idx++;
            }
        }

        /// <summary>Spawn one trash mob at a random spot on the enemy side (deterministic via rng).</summary>
        private static void SpawnTrash(CombatState s, StageDef rt, GameConfig cfg, Rng rng)
        {
            var mdef = (s.SpawnCount % 2 == 0) ? cfg.Monsters["slime"] : cfg.Monsters["goblin"];
            double w = cfg.Balance.MapHalfWidth, d = cfg.Balance.MapHalfDepth;
            // scattered across the right/centre of the field
            var pos = new Vec2(rng.RandRange(0.0, w - 1.0), rng.RandRange(-(d - 1.0), d - 1.0));
            s.Entities.Add(MakeMonster(mdef, "E" + s.SpawnCount, pos, StageScale(rt), false));
            s.SpawnCount++;
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

            // Respawn countdown (before acting + before the win/lose check) so a hero
            // who comes back this step prevents a false wipe. Downed in a prior step
            // counts down here; a hero downed this step starts counting next step.
            foreach (var e in s.Entities)
            {
                if (!e.Downed) continue;
                e.RespawnMs -= dtMs;
                if (e.RespawnMs <= 0)
                {
                    e.RespawnMs = 0;
                    e.Hp = e.MaxHp;
                    e.AttackCdMs = 0;
                    events.Add(new CombatEvent { Type = CombatEventType.Respawn, EntityId = e.Id });
                }
            }

            // Farm spawning: refill trash one per interval, up to the cap.
            if (s.Kind == EncounterKind.Farm)
            {
                s.SpawnTimerMs -= dtMs;
                if (s.SpawnTimerMs <= 0)
                {
                    int room = cfg.Balance.MobCap - CountAliveEnemies(s);
                    int n = Math.Min(cfg.Balance.SpawnBatchSize, room);
                    if (n > 0)
                    {
                        var rt = cfg.Stages.Find(r => r.Stage == s.Stage) ?? cfg.Stages[0];
                        for (int i = 0; i < n; i++) SpawnTrash(s, rt, cfg, rng);
                    }
                    s.SpawnTimerMs = cfg.Balance.SpawnIntervalMs;
                }
            }

            // HP regen: alive entities with HpRegen heal up to MaxHp. Deterministic
            // (no rng). Downed heroes (Hp 0) don't regen — respawn restores them.
            foreach (var e in s.Entities)
            {
                if (!e.Alive) continue;
                double regen = e.Stats.Get(StatKey.HpRegen);
                if (regen > 0) e.Hp = Math.Min(e.MaxHp, e.Hp + regen * dtMs / 1000.0);
            }

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
                double range = e.Stats.Get(StatKey.AttackRange);
                if (range <= 0) range = MeleeRange;

                if (dist <= range)
                {
                    if (e.AttackCdMs <= 0)
                    {
                        e.AttackCdMs = e.AttackIntervalMs;
                        ApplyHit(s, e, target, cfg, rng, events);

                        // Splash: the same swing also strikes enemies near the target
                        // (full damage, each rolls its own crit). Warrior/magician only.
                        double splash = e.Stats.Get(StatKey.SplashRadius);
                        if (splash > 0)
                        {
                            var extra = new List<CombatEntity>();
                            foreach (var o in s.Entities)
                                if (o.Team == target.Team && o.Alive && !ReferenceEquals(o, target) &&
                                    Vec2.Distance(o.Pos, target.Pos) <= splash)
                                    extra.Add(o);
                            foreach (var o in extra) ApplyHit(s, e, o, cfg, rng, events);
                        }
                    }
                }
                else
                {
                    MoveToward(e, target.Pos, MoveSpeedTilesPerSec * dtMs / 1000.0);
                }
            }

            // A downed hero has Hp 0 (Alive == false), so an all-at-once wipe makes
            // partyAlive false -> Lost.
            bool partyAlive = s.Entities.Any(e => e.Team == Team.Party && e.Alive);
            if (s.Kind == EncounterKind.Farm)
            {
                // Endless: never auto-wins, no timeout — only a wipe ends it.
                if (!partyAlive) s.Status = CombatStatus.Lost;
                // Dead trash would otherwise pile up forever — prune it (rewards already
                // accrued at death; living entities keep their order, so determinism holds).
                s.Entities.RemoveAll(e => e.Team == Team.Enemy && !e.Alive);
            }
            else
            {
                bool enemyAlive = s.Entities.Any(e => e.Team == Team.Enemy && e.Alive);
                double timeoutSec = s.Kind == EncounterKind.BossChallenge
                    ? cfg.Balance.BossChallengeSeconds
                    : cfg.Balance.MaxRunSeconds;
                if (!partyAlive) s.Status = CombatStatus.Lost;
                else if (!enemyAlive) s.Status = CombatStatus.Won;
                else if (s.TimeMs >= timeoutSec * 1000.0) s.Status = CombatStatus.Lost;
            }

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

        /// <summary>Apply one hit (own crit roll) from attacker to victim; handle death.</summary>
        private static void ApplyHit(CombatState s, CombatEntity attacker, CombatEntity victim,
                                     GameConfig cfg, Rng rng, List<CombatEvent> events)
        {
            double dmg = Math.Max(1.0, attacker.Stats.Get(StatKey.Atk) - victim.Stats.Get(StatKey.Def));
            bool crit = rng.Next() < attacker.Stats.Get(StatKey.CritChance);
            if (crit) dmg *= Math.Max(1.0, attacker.Stats.Get(StatKey.CritDmg));

            victim.Hp -= dmg;
            events.Add(new CombatEvent
            {
                Type = CombatEventType.Hit,
                SourceId = attacker.Id,
                TargetId = victim.Id,
                Amount = dmg,
                Crit = crit,
            });

            if (victim.Hp <= 0) HandleDeath(s, victim, cfg, rng, events);
        }

        /// <summary>Resolve a killed entity: party heroes are downed (respawn); monsters
        /// die and yield XP/gold/loot.</summary>
        private static void HandleDeath(CombatState s, CombatEntity target, GameConfig cfg, Rng rng, List<CombatEvent> events)
        {
            target.Hp = 0;
            events.Add(new CombatEvent { Type = CombatEventType.Death, EntityId = target.Id });

            if (target.Team == Team.Party)
            {
                target.RespawnMs = target.RespawnDurationMs; // downed, not dead
                return;
            }

            if (target.IsBoss)
                events.Add(new CombatEvent { Type = CombatEventType.BossDefeated, Stage = s.Stage });

            // Loot + XP only from real monsters (guards synthetic test entities).
            if (target.RefKind == "monster" && cfg.Monsters.TryGetValue(target.RefId, out var mdef))
            {
                double mult = cfg.Balance.KillRewardMult(s.Stage);
                s.PendingXp += (int)Math.Floor(mdef.XpReward * mult);
                s.PendingGold += (long)Math.Floor(mdef.GoldReward * mult);

                var drop = Loot.RollDrop(rng, mdef, s.Loot, cfg);
                if (drop != null)
                {
                    s.PendingLoot.Add(drop);
                    events.Add(new CombatEvent { Type = CombatEventType.LootDrop, EntityId = target.Id, Item = drop });
                }
            }
        }

        private static int CountAliveEnemies(CombatState s)
        {
            int n = 0;
            foreach (var e in s.Entities) if (e.Team == Team.Enemy && e.Alive) n++;
            return n;
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
