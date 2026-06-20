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
        public const double MoveSpeedTilesPerSec = 3.0; // fallback when an entity has no MoveSpd stat
        public const double DefaultStepMs = 1000.0 / 30.0;

        // Attack/cast cadence scales with AtkSpd (attacks per second at 1.0). Missing/zero
        // defaults to 1.0 so synthetic entities still act at a sane rate.
        private static double AttackSpeedOf(StatBlock s)
        {
            double a = s.Get(StatKey.AtkSpd);
            return a > 0 ? a : 1.0;
        }

        private static double AttackInterval(StatBlock s) => 1000.0 / AttackSpeedOf(s);

        // Attack cadence at action time, folding in active buffs (e.g. a Frenzy AtkSpd buff) and
        // current gear — so attack-speed boosts actually quicken basic attacks, not just skills.
        private static double EffectiveAttackIntervalMs(CombatEntity e)
        {
            double aps = e.EffectiveStat(StatKey.AtkSpd);
            return 1000.0 / (aps > 0 ? aps : 1.0);
        }

        /// <summary>Build the initial battle: party (left) vs the stage's pack + boss (right).</summary>
        public static CombatState InitCombat(IReadOnlyList<HeroInstance> party, int stage, GameConfig cfg, Rng rng)
        {
            var s = new CombatState { Stage = stage, Kind = EncounterKind.Encounter };
            AddParty(s, party, cfg);

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt);
            double scale = StageScale(rt, cfg);

            for (int j = 0; j < rt.PackCount; j++)
            {
                var mdef = (j % 2 == 0) ? cfg.Monsters["slime"] : cfg.Monsters["goblin"];
                s.Entities.Add(MakeMonster(cfg, mdef, "E" + j, new Vec2(3, j * 1.5), scale, false, HpScale(rt, cfg)));
            }

            if (cfg.Monsters.TryGetValue(rt.BossId, out var boss))
            {
                double major = rt.IsMajorBoss ? cfg.Balance.MajorBossMult : 1.0;
                s.Entities.Add(MakeMonster(cfg, boss, "EBOSS", new Vec2(5, rt.PackCount * 0.75),
                    scale * major, true, HpScale(rt, cfg) * cfg.Balance.BossHpMult * major));
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
            if (initial > 0) SpawnPack(s, rt, cfg, rng, initial);

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
                double major = rt.IsMajorBoss ? cfg.Balance.MajorBossMult : 1.0;
                s.Entities.Add(MakeMonster(cfg, boss, "EBOSS", new Vec2(4, 0),
                    StageScale(rt, cfg) * major, true, HpScale(rt, cfg) * cfg.Balance.BossHpMult * major));
            }

            return s;
        }

        /// <summary>
        /// C1 — convert a live farm encounter into the timed boss challenge IN PLACE: despawn all
        /// trash, restore the party, and make the stage's boss appear a short distance ahead of the
        /// party on the SAME map (no scene reset / separate arena). Switches Kind and resets the
        /// challenge timer; the existing BossChallenge step logic then runs the fight. Mutates s.
        /// </summary>
        public static void EnterBossChallenge(CombatState s, GameConfig cfg)
        {
            s.Entities.RemoveAll(e => e.Team == Team.Enemy); // trash despawns
            RestoreParty(s);

            var rt = cfg.Stages.Find(r => r.Stage == s.Stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt);
            if (cfg.Monsters.TryGetValue(rt.BossId, out var boss))
            {
                double major = rt.IsMajorBoss ? cfg.Balance.MajorBossMult : 1.0;
                var c = PartyCentroid(s);
                double w = cfg.Balance.MapHalfWidth - 1.0, d = cfg.Balance.MapHalfDepth - 1.0;
                var pos = new Vec2(Math.Clamp(c.X + cfg.Balance.BossSpawnDistance, -w, w), Math.Clamp(c.Y, -d, d));
                s.Entities.Add(MakeMonster(cfg, boss, "EBOSS", pos,
                    StageScale(rt, cfg) * major, true, HpScale(rt, cfg) * cfg.Balance.BossHpMult * major));
            }

            s.Kind = EncounterKind.BossChallenge;
            s.TimeMs = 0;
            s.SpawnTimerMs = 0;
            s.Status = CombatStatus.Running;
        }

        /// <summary>
        /// C1 — return a boss challenge (or a wiped farm) to farming IN PLACE: despawn the boss,
        /// restore the party, and resume the farm for <paramref name="stage"/> on the same map. The
        /// first trash pack is gated by <paramref name="spawnDelayMs"/> — a normal beat after a win,
        /// or a longer anti-spam cooldown after a flee/fail so packs can't be refreshed on demand by
        /// spamming challenge→flee. Mutates s.
        /// </summary>
        public static void ResumeFarm(CombatState s, int stage, GameConfig cfg, double spawnDelayMs)
        {
            s.Entities.RemoveAll(e => e.Team == Team.Enemy); // boss / leftovers despawn
            RestoreParty(s);

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Stage = stage;
            s.Loot = LootContext.ForStage(rt);
            s.Kind = EncounterKind.Farm;
            s.TimeMs = 0;
            s.Status = CombatStatus.Running;
            s.SpawnTimerMs = spawnDelayMs; // lull before the next pack (no instant respawn)
        }

        /// <summary>Heal the party to full and clear downed / cooldown / buff state — a phase
        /// transition (farm↔boss) begins a fresh fight on the same map, mirroring the clean slate
        /// the old rebuild-from-scratch flow gave.</summary>
        private static void RestoreParty(CombatState s)
        {
            foreach (var e in s.Entities)
            {
                if (e.Team != Team.Party) continue;
                e.Hp = e.MaxHp;
                e.Mana = e.MaxMana;
                e.RespawnMs = 0;
                e.AttackCdMs = 0;
                e.Buffs.Clear();
                e.SkillCdMs.Clear();
            }
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
                double oldMaxMana = e.MaxMana;
                double newMaxMana = stats.Get(StatKey.MaxMana);

                e.Stats = stats;
                e.MaxHp = newMax;
                e.AttackIntervalMs = AttackInterval(stats);
                if (e.Hp > 0) e.Hp = Math.Min(newMax, e.Hp + Math.Max(0.0, newMax - oldMax));

                // Grow current mana by the max gain too, so a level-up/gear swap doesn't
                // shrink the pool; clamp to the new max.
                e.MaxMana = newMaxMana;
                e.Mana = Math.Min(newMaxMana, e.Mana + Math.Max(0.0, newMaxMana - oldMaxMana));
            }
        }

        /// <summary>
        /// Hot-swap the live party to match the save's fielded heroes WITHOUT restarting
        /// the run (roster swaps): benched heroes' entities are removed, and newly-fielded
        /// heroes get a fresh, fully-statted entity on the party line. Combat keeps running
        /// around the change. Intended for farm mode. Mutates state; deterministic (combat
        /// acts in Id order, so entity add order never affects the sim).
        /// </summary>
        public static void ReconcileParty(CombatState s, SaveState save, GameConfig cfg)
        {
            // fielded hero id -> party slot index (slot only drives spawn placement)
            var slotOf = new Dictionary<string, int>();
            for (int i = 0; i < save.Party.Length; i++)
            {
                var id = save.Party[i];
                if (id != null) slotOf[id] = i;
            }

            // drop benched heroes
            s.Entities.RemoveAll(e => e.Team == Team.Party && e.RefKind == "hero" && !slotOf.ContainsKey(e.RefId));

            // add newly fielded heroes (skip any already on the field) — a roster swap drops
            // the new hero in at its slot's centre cluster spot ("in place"), no recentering.
            foreach (var kv in slotOf)
            {
                if (s.Entities.Exists(e => e.Team == Team.Party && e.RefKind == "hero" && e.RefId == kv.Key)) continue;
                var hero = save.Heroes.Find(h => h.Id == kv.Key);
                if (hero == null) continue;

                var stats = Stats.ComputeHeroStats(hero, cfg, Stats.ResolveEquipped(save, hero));
                double hp = stats.Get(StatKey.Hp);
                double mana = stats.Get(StatKey.MaxMana);
                int idx = kv.Value;
                s.Entities.Add(new CombatEntity
                {
                    Id = "P" + idx + "_" + hero.Id,
                    Team = Team.Party,
                    Pos = PartyStartPos(idx),
                    Stats = stats,
                    Hp = hp,
                    MaxHp = hp,
                    Mana = mana,
                    MaxMana = mana,
                    AttackIntervalMs = AttackInterval(stats),
                    RefKind = "hero",
                    RefId = hero.Id,
                    BodyRadius = cfg.Balance.UnitRadius,
                    Skills = new List<string>(hero.SkillLoadout),
                    RespawnDurationMs = cfg.Balance.RespawnBaseMs + cfg.Balance.RespawnPerLevelMs * hero.Level,
                });
            }
        }

        /// <summary>Geometric per-stage atk/def scale (gentle, so trash stays survivable).</summary>
        private static double StageScale(StageDef rt, GameConfig cfg) =>
            Math.Pow(cfg.Balance.MonsterDmgGrowth, rt.MonsterLevel - 1);

        /// <summary>Geometric per-stage HP scale (steep, the DPS-check gate). Bosses layer
        /// BossHpMult (and major bosses MajorBossMult) on top of this.</summary>
        private static double HpScale(StageDef rt, GameConfig cfg) =>
            Math.Pow(cfg.Balance.MonsterHpGrowth, rt.MonsterLevel - 1);

        /// <summary>Centre of the living party — the focus that trash spawning and culling
        /// (and the client camera) track. Falls back to the origin when all are down.</summary>
        private static Vec2 PartyCentroid(CombatState s)
        {
            double sx = 0, sy = 0; int n = 0;
            foreach (var e in s.Entities)
                if (e.Team == Team.Party && e.Alive) { sx += e.Pos.X; sy += e.Pos.Y; n++; }
            return n > 0 ? new Vec2(sx / n, sy / n) : new Vec2(0, 0);
        }

        /// <summary>Party spawn point: a tight cluster at the map CENTER (mobs now spawn all
        /// around them). The index fans heroes into a small 2-wide grid so they don't stack.</summary>
        private static Vec2 PartyStartPos(int idx)
        {
            const double s = 1.4;
            int col = idx % 2, row = idx / 2;
            return new Vec2((col - 0.5) * s, (row - 0.5) * s);
        }

        private static void AddParty(CombatState s, IReadOnlyList<HeroInstance> party, GameConfig cfg)
        {
            int idx = 0;
            foreach (var hero in party)
            {
                var stats = Stats.ComputeHeroStats(hero, cfg);
                double hp = stats.Get(StatKey.Hp);
                double mana = stats.Get(StatKey.MaxMana);
                s.Entities.Add(new CombatEntity
                {
                    Id = "P" + idx + "_" + hero.Id,
                    Team = Team.Party,
                    Pos = PartyStartPos(idx),
                    Stats = stats,
                    Hp = hp,
                    MaxHp = hp,
                    Mana = mana,
                    MaxMana = mana,
                    AttackIntervalMs = AttackInterval(stats),
                    RefKind = "hero",
                    RefId = hero.Id,
                    BodyRadius = cfg.Balance.UnitRadius,
                    Skills = new List<string>(hero.SkillLoadout),
                    RespawnDurationMs = cfg.Balance.RespawnBaseMs + cfg.Balance.RespawnPerLevelMs * hero.Level,
                });
                idx++;
            }
        }

        /// <summary>Spawn one trash mob at a random spot anywhere on the map (deterministic
        /// via rng) — mobs surround the centred party rather than arriving as a side wave.</summary>
        /// <summary>
        /// Spawn a PACK: <paramref name="count"/> mobs clustered tightly at a single point in
        /// the ring around the party (PoE-style — packs with quiet gaps between, not an even
        /// scatter). The pack centre rings the group so packs appear near it wherever it roams.
        /// </summary>
        private static void SpawnPack(CombatState s, StageDef rt, GameConfig cfg, Rng rng, int count)
        {
            double w = cfg.Balance.MapHalfWidth - 1.0, d = cfg.Balance.MapHalfDepth - 1.0;
            var c = PartyCentroid(s);
            double ang = rng.RandRange(0, 2.0 * Math.PI);
            double rad = rng.RandRange(cfg.Balance.SpawnRingInner, cfg.Balance.SpawnRingOuter);
            var center = new Vec2(c.X + Math.Cos(ang) * rad, c.Y + Math.Sin(ang) * rad);
            double pr = cfg.Balance.PackRadius;

            for (int i = 0; i < count; i++)
            {
                var mdef = (s.SpawnCount % 2 == 0) ? cfg.Monsters["slime"] : cfg.Monsters["goblin"];
                var pos = new Vec2(Math.Clamp(center.X + rng.RandRange(-pr, pr), -w, w),
                                   Math.Clamp(center.Y + rng.RandRange(-pr, pr), -d, d));
                var mob = MakeMonster(cfg, mdef, "E" + s.SpawnCount, pos, StageScale(rt, cfg), false, HpScale(rt, cfg));
                mob.Aggro = false;          // ambles until a hero hits it
                mob.WanderTarget = pos;     // idle in place until...
                mob.WanderCdMs = rng.RandRange(0, cfg.Balance.WanderMaxMs); // ...a staggered first repick
                s.Entities.Add(mob);
                s.SpawnCount++;
            }
        }

        private static CombatEntity MakeMonster(GameConfig cfg, MonsterDef def, string id, Vec2 pos, double scale, bool isBoss, double hpScale = -1)
        {
            double hs = hpScale < 0 ? scale : hpScale; // trash passes a steeper HP scale; bosses use `scale`
            var stats = new StatBlock();
            foreach (var kv in def.BaseStats) stats[kv.Key] = kv.Value;
            // scale the "size" stats with monster level; leave rate/crit stats as-is
            stats[StatKey.Hp] = stats.Get(StatKey.Hp) * hs;
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
                BodyRadius = isBoss ? cfg.Balance.BossRadius : cfg.Balance.UnitRadius,
                Skills = new List<string>(def.Skills),
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
                    e.Mana = e.MaxMana; // come back ready to cast
                    e.AttackCdMs = 0;
                    events.Add(new CombatEvent { Type = CombatEventType.Respawn, EntityId = e.Id });
                }
            }

            // Farm spawning: drop a fresh pack near the party each interval, up to the cap.
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
                        SpawnPack(s, rt, cfg, rng, n);
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

                // Mana regen (M10): fills toward MaxMana.
                double mregen = e.Stats.Get(StatKey.ManaRegen);
                if (mregen > 0 && e.MaxMana > 0) e.Mana = Math.Min(e.MaxMana, e.Mana + mregen * dtMs / 1000.0);

                // Skill cooldowns + buff durations tick down (M11).
                for (int i = 0; i < e.Skills.Count; i++)
                {
                    var id = e.Skills[i];
                    if (e.SkillCdMs.TryGetValue(id, out var cd) && cd > 0)
                        e.SkillCdMs[id] = Math.Max(0, cd - dtMs);
                }
                for (int i = e.Buffs.Count - 1; i >= 0; i--)
                {
                    e.Buffs[i].RemainingMs -= dtMs;
                    if (e.Buffs[i].RemainingMs <= 0) e.Buffs.RemoveAt(i);
                }
            }

            var actors = s.Entities.Where(e => e.Alive)
                                   .OrderBy(e => e.Id, StringComparer.Ordinal)
                                   .ToList();

            // In Group tactic the whole party shares one focus target (recomputed each
            // step); Solo and all monsters use their own nearest enemy.
            var groupTarget = s.Tactic == PartyTactic.Group ? FindGroupTarget(s) : null;
            // Solo party: travel anchor = the enemy pack nearest the party centre (heroes head
            // here when nothing's in their personal engage range, so the group stays cohesive).
            var centroid = PartyCentroid(s);
            var anchor = s.Tactic == PartyTactic.Solo ? FindNearestEnemyTo(s, centroid) : null;

            foreach (var e in actors)
            {
                if (!e.Alive) continue; // could have died earlier this step

                if (e.AttackCdMs > 0) e.AttackCdMs = Math.Max(0, e.AttackCdMs - dtMs);

                // Idle (non-aggro) trash just ambles randomly; it doesn't seek or attack the
                // party until something hits it (ApplyHit flips Aggro on).
                if (e.Team == Team.Enemy && !e.Aggro) { Wander(e, cfg, dtMs, rng); continue; }

                // A ready skill replaces this step's basic attack/move (M11).
                if (TryCastSkill(s, e, cfg, rng, events)) continue;

                CombatEntity? target;
                if (e.Team == Team.Party && s.Tactic == PartyTactic.Solo)
                {
                    // Leashed individuality: fight the nearest enemy in personal engage range;
                    // if none is close, travel toward the pack nearest the party centre so the
                    // group stays together instead of one hero sprinting off solo.
                    target = FindNearestEnemyWithin(s, e, cfg.Balance.EngageRadius);
                    if (target == null)
                    {
                        e.TargetId = anchor?.Id;
                        if (anchor != null)
                        {
                            double ms0 = e.EffectiveStat(StatKey.MoveSpd);
                            if (ms0 <= 0) ms0 = MoveSpeedTilesPerSec;
                            MoveToward(e, anchor.Pos, ms0 * dtMs / 1000.0);
                        }
                        continue;
                    }
                }
                else
                {
                    target = (e.Team == Team.Party && groupTarget != null && groupTarget.Alive)
                        ? groupTarget
                        : FindNearestEnemy(s, e);
                }
                e.TargetId = target?.Id;
                if (target == null) continue;

                double dist = Vec2.Distance(e.Pos, target.Pos);
                double range = e.Stats.Get(StatKey.AttackRange);
                if (range <= 0) range = MeleeRange;
                // Reach a target's body, not its centre — so a chunky boss the separation
                // pass holds at arm's length is still meleeable.
                range += target.BodyRadius;

                if (dist <= range)
                {
                    if (e.AttackCdMs <= 0)
                    {
                        e.AttackCdMs = EffectiveAttackIntervalMs(e);
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
                {
                    double moveSpd = e.EffectiveStat(StatKey.MoveSpd);
                    if (moveSpd <= 0) moveSpd = MoveSpeedTilesPerSec; // fallback for entities w/o the stat
                    MoveToward(e, target.Pos, moveSpd * dtMs / 1000.0);
                }
                }
            }

            // Soft-body separation: push overlapping units apart so they occupy space
            // instead of stacking. Runs AFTER movement/attacks (which already resolved at
            // their pre-push positions), so it only affects spacing, never hit outcomes.
            ResolveCollisions(s, cfg);

            // A downed hero has Hp 0 (Alive == false), so an all-at-once wipe makes
            // partyAlive false -> Lost.
            bool partyAlive = s.Entities.Any(e => e.Team == Team.Party && e.Alive);
            if (s.Kind == EncounterKind.Farm)
            {
                // Endless: never auto-wins, no timeout — only a wipe ends it.
                if (!partyAlive) s.Status = CombatStatus.Lost;
                // Dead trash would otherwise pile up forever — prune it (rewards already
                // accrued at death; living entities keep their order, so determinism holds).
                // Living trash is NOT culled by distance: a sparse field where mobs persist
                // makes each kill feel more impactful (and future AoE will thin packs).
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

        /// <summary>Apply one hit (own crit roll) from attacker to victim; handle death.
        /// <paramref name="mult"/> scales the base damage (1.0 = basic attack, &gt;1 = skill).
        /// Reads EffectiveStat so active buffs (e.g. War Cry) feed into the calc.</summary>
        private static void ApplyHit(CombatState s, CombatEntity attacker, CombatEntity victim,
                                     GameConfig cfg, Rng rng, List<CombatEvent> events, double mult = 1.0)
        {
            if (victim.Team == Team.Enemy) victim.Aggro = true; // being hit wakes idle trash

            double dmg = Math.Max(1.0, attacker.EffectiveStat(StatKey.Atk) - victim.EffectiveStat(StatKey.Def)) * mult;
            bool crit = rng.Next() < attacker.EffectiveStat(StatKey.CritChance);
            if (crit) dmg *= Math.Max(1.0, attacker.EffectiveStat(StatKey.CritDmg));

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

        /// <summary>
        /// Try to cast the first ready skill in the entity's loadout (off cooldown, mana
        /// available, valid target). On success: spend mana, set cooldown, apply the
        /// effect, emit a SkillCast event, and return true (the caller then skips this
        /// step's basic attack). Deterministic — rng is only used inside ApplyHit.
        /// </summary>
        private static bool TryCastSkill(CombatState s, CombatEntity e, GameConfig cfg, Rng rng, List<CombatEvent> events)
        {
            foreach (var id in e.Skills)
            {
                if (!cfg.Skills.TryGetValue(id, out var sk)) continue;            // unknown id
                if (e.SkillCdMs.TryGetValue(id, out var cd) && cd > 0) continue;  // on cooldown
                if (e.Mana < sk.ManaCost) continue;                              // not enough mana

                switch (sk.Effect)
                {
                    case SkillEffectKind.Damage:
                    {
                        var target = PickDamageTarget(s, e, sk);
                        if (target == null) continue;
                        CastStart(e, sk, target.Id, events);
                        ApplyHit(s, e, target, cfg, rng, events, sk.DamageMult);
                        if (sk.AoeRadius > 0)
                        {
                            foreach (var o in s.Entities)
                                if (o.Team == target.Team && o.Alive && !ReferenceEquals(o, target)
                                    && Vec2.Distance(o.Pos, target.Pos) <= sk.AoeRadius)
                                    ApplyHit(s, e, o, cfg, rng, events, sk.DamageMult);
                        }
                        return true;
                    }
                    case SkillEffectKind.Heal:
                    {
                        var ally = PickHealTarget(s, e, sk);
                        if (ally == null) continue;
                        CastStart(e, sk, ally.Id, events);
                        double heal = Math.Max(1.0, e.EffectiveStat(StatKey.Atk) * sk.DamageMult);
                        ally.Hp = Math.Min(ally.MaxHp, ally.Hp + heal);
                        events.Add(new CombatEvent { Type = CombatEventType.Heal, SourceId = e.Id, TargetId = ally.Id, Amount = heal });
                        return true;
                    }
                    case SkillEffectKind.Buff:
                    {
                        if (!AnyEnemyAlive(s, e.Team)) continue; // don't waste buffs out of combat
                        CastStart(e, sk, e.Id, events);
                        e.Buffs.Add(new ActiveBuff { Stat = sk.BuffStat, Amount = sk.BuffAmount, RemainingMs = sk.BuffDurationMs });
                        return true;
                    }
                }
            }
            return false;
        }

        private static void CastStart(CombatEntity e, SkillDef sk, string? targetId, List<CombatEvent> events)
        {
            e.Mana = Math.Max(0, e.Mana - sk.ManaCost);
            // Attack speed also quickens casts: higher AtkSpd => shorter effective cooldown.
            double atkSpd = e.EffectiveStat(StatKey.AtkSpd);
            e.SkillCdMs[sk.Id] = sk.CooldownMs / (atkSpd > 0 ? atkSpd : 1.0);
            events.Add(new CombatEvent { Type = CombatEventType.SkillCast, SourceId = e.Id, TargetId = targetId, SkillId = sk.Id });
        }

        /// <summary>Enemy for a damage skill within its range: lowest-HP for "lowestHp",
        /// otherwise nearest (also the primary for "aoe"). Stable Id tie-break.</summary>
        private static CombatEntity? PickDamageTarget(CombatState s, CombatEntity e, SkillDef sk)
        {
            CombatEntity? best = null;
            double bestKey = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team == e.Team) continue;
                double d = Vec2.Distance(e.Pos, o.Pos);
                if (d > sk.Range + o.BodyRadius) continue; // measured to the target's body edge
                double key = sk.Targeting == "lowestHp" ? o.Hp : d;
                if (key < bestKey || (key == bestKey && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                {
                    bestKey = key;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The most-hurt living ally (lowest HP fraction) in range; null if none need it.</summary>
        private static CombatEntity? PickHealTarget(CombatState s, CombatEntity e, SkillDef sk)
        {
            CombatEntity? best = null;
            double bestFrac = 1.0;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team != e.Team || o.MaxHp <= 0) continue;
                double frac = o.Hp / o.MaxHp;
                if (frac >= 1.0) continue;
                if (Vec2.Distance(e.Pos, o.Pos) > sk.Range) continue;
                if (frac < bestFrac || (frac == bestFrac && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                {
                    bestFrac = frac;
                    best = o;
                }
            }
            return best;
        }

        private static bool AnyEnemyAlive(CombatState s, Team team)
        {
            foreach (var o in s.Entities) if (o.Alive && o.Team != team) return true;
            return false;
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

                // Bosses drop a guaranteed Unique/Legendary bundle (+ ordinary extras),
                // sized by boss tier; trash uses the scarce per-kill chance (Rare-capped).
                if (target.IsBoss)
                {
                    bool isMajor = (cfg.Stages.Find(st => st.Stage == s.Stage)?.IsMajorBoss) ?? false;
                    foreach (var drop in Loot.RollBossDrops(rng, s.Loot, cfg, isMajor))
                    {
                        s.PendingLoot.Add(drop);
                        events.Add(new CombatEvent { Type = CombatEventType.LootDrop, EntityId = target.Id, Item = drop });
                    }
                }
                else
                {
                    var drop = Loot.RollDrop(rng, mdef, s.Loot, cfg);
                    if (drop != null)
                    {
                        s.PendingLoot.Add(drop);
                        events.Add(new CombatEvent { Type = CombatEventType.LootDrop, EntityId = target.Id, Item = drop });
                    }
                }
            }
        }

        private static int CountAliveEnemies(CombatState s)
        {
            int n = 0;
            foreach (var e in s.Entities) if (e.Team == Team.Enemy && e.Alive) n++;
            return n;
        }

        /// <summary>The enemy nearest the living party's centroid (stable tie-break by Id).</summary>
        private static CombatEntity? FindGroupTarget(CombatState s)
        {
            double cx = 0, cy = 0; int n = 0;
            foreach (var e in s.Entities)
                if (e.Team == Team.Party && e.Alive) { cx += e.Pos.X; cy += e.Pos.Y; n++; }
            if (n == 0) return null;
            var centre = new Vec2(cx / n, cy / n);

            CombatEntity? best = null;
            double bestDist = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team != Team.Enemy) continue;
                double d = Vec2.Distance(centre, o.Pos);
                if (d < bestDist || (d == bestDist && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                {
                    bestDist = d;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The enemy nearest a world point (the Solo party's travel anchor). Stable Id tie-break.</summary>
        private static CombatEntity? FindNearestEnemyTo(CombatState s, Vec2 point)
        {
            CombatEntity? best = null;
            double bestDist = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team != Team.Enemy) continue;
                double d = Vec2.Distance(point, o.Pos);
                if (d < bestDist || (d == bestDist && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                {
                    bestDist = d;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The nearest enemy within <paramref name="radius"/> of <paramref name="self"/>,
        /// or null if none is that close (Solo party engages individually within range).</summary>
        private static CombatEntity? FindNearestEnemyWithin(CombatState s, CombatEntity self, double radius)
        {
            CombatEntity? best = null;
            double bestDist = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team == self.Team) continue;
                double d = Vec2.Distance(self.Pos, o.Pos);
                if (d > radius) continue;
                if (d < bestDist || (d == bestDist && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                {
                    bestDist = d;
                    best = o;
                }
            }
            return best;
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

        /// <summary>
        /// Soft-body separation: push overlapping LIVING units apart so two units never
        /// stand on the same point. Deterministic — pure arithmetic over entities in stable
        /// Id order (no rng). The push is split by the OPPOSITE body's radius, so a heavy
        /// boss barely budges while light trash is shoved clear; exactly-stacked units
        /// separate along a fixed axis (smaller Id moves -x). Pushes are clamped to the map.
        /// A few relaxation passes (Balance.CollisionIterations) settle dense crowds.
        /// </summary>
        private static void ResolveCollisions(CombatState s, GameConfig cfg)
        {
            var bodies = s.Entities.Where(e => e.Alive)
                                   .OrderBy(e => e.Id, StringComparer.Ordinal)
                                   .ToList();
            double w = cfg.Balance.MapHalfWidth, d = cfg.Balance.MapHalfDepth;
            int iters = Math.Max(1, cfg.Balance.CollisionIterations);

            for (int it = 0; it < iters; it++)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    for (int j = i + 1; j < bodies.Count; j++)
                    {
                        var a = bodies[i];
                        var b = bodies[j];
                        double rsum = a.BodyRadius + b.BodyRadius;
                        if (rsum <= 0) continue;

                        double dx = b.Pos.X - a.Pos.X, dy = b.Pos.Y - a.Pos.Y;
                        double d2 = dx * dx + dy * dy;
                        if (d2 >= rsum * rsum) continue; // not overlapping

                        double dist = Math.Sqrt(d2);
                        double nx, ny;
                        if (dist > 1e-9) { nx = dx / dist; ny = dy / dist; }
                        else { nx = 1; ny = 0; dist = 0; } // exactly stacked: fixed axis (a has smaller Id)

                        double overlap = rsum - dist;
                        double aShare = b.BodyRadius / rsum; // heavier (bigger) body moves less
                        double bShare = a.BodyRadius / rsum;
                        a.Pos = new Vec2(Math.Clamp(a.Pos.X - nx * overlap * aShare, -w, w),
                                         Math.Clamp(a.Pos.Y - ny * overlap * aShare, -d, d));
                        b.Pos = new Vec2(Math.Clamp(b.Pos.X + nx * overlap * bShare, -w, w),
                                         Math.Clamp(b.Pos.Y + ny * overlap * bShare, -d, d));
                    }
                }
            }
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

        /// <summary>Idle amble: stroll toward a random point within the field, repicking when
        /// reached or after a random interval. Deterministic (rng-driven). Clamped to the map
        /// so wanderers never drift out of bounds.</summary>
        private static void Wander(CombatEntity e, GameConfig cfg, double dtMs, Rng rng)
        {
            // Repick a fresh destination only when the timer elapses (gated purely by the
            // staggered cooldown, so a batch of spawns doesn't all turn on the same frame).
            e.WanderCdMs -= dtMs;
            if (e.WanderCdMs <= 0)
            {
                double w = cfg.Balance.MapHalfWidth - 0.5, d = cfg.Balance.MapHalfDepth - 0.5, r = cfg.Balance.WanderRadius;
                // random local step; if it would leave the field, reflect inward instead of
                // clamping to the edge (clamping makes mobs pile along / trace the rectangle).
                double ox = rng.RandRange(-r, r), oy = rng.RandRange(-r, r);
                double tx = e.Pos.X + ox; if (tx < -w || tx > w) tx = e.Pos.X - ox;
                double ty = e.Pos.Y + oy; if (ty < -d || ty > d) ty = e.Pos.Y - oy;
                e.WanderTarget = new Vec2(Math.Clamp(tx, -w, w), Math.Clamp(ty, -d, d));
                e.WanderCdMs = rng.RandRange(cfg.Balance.WanderMinMs, cfg.Balance.WanderMaxMs);
            }
            double speed = e.EffectiveStat(StatKey.MoveSpd);
            if (speed <= 0) speed = MoveSpeedTilesPerSec;
            MoveToward(e, e.WanderTarget, speed * cfg.Balance.WanderSpeedMult * dtMs / 1000.0);
        }
    }
}
