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
        private const double ArriveDepth = 0.05;      // an approaching attacker stops this far INSIDE its reach
                                                      // so it doesn't overshoot the rim contact point and get
                                                      // shoved back out by ResolveCollisions (the in/out flap)
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

        /// <summary>The arena a live state is fought on, or null (open plane). Resolves the pinned
        /// <see cref="CombatState.ArenaId"/> against the config; a null id or an unknown layout both
        /// mean "no arena", so legacy states and synthetic fights stay on the open plane.</summary>
        private static IArenaSurface? ArenaOf(CombatState s, GameConfig cfg)
            => s.Dungeon != null ? s.Dungeon
             : (s.ArenaId != null && cfg.Arenas.TryGetValue(s.ArenaId, out var a) ? a : null);

        /// <summary>The trash def for spawn number <paramref name="index"/> at a stage:
        /// cycles the stage's ZONE roster (zones = the themed ~10-stage bands; Tower floors
        /// pass the floor so the climb travels the same zones). Falls back to the legacy
        /// slime/goblin alternation when no zone or roster entry resolves.</summary>
        private static MonsterDef TrashDef(GameConfig cfg, int stage, long index)
        {
            var zone = cfg.ZoneForStage(stage);
            if (zone != null && zone.TrashMonsters.Count > 0
                && cfg.Monsters.TryGetValue(zone.TrashMonsters[(int)(index % zone.TrashMonsters.Count)], out var m))
                return m;
            return (index % 2 == 0) ? cfg.Monsters["slime"] : cfg.Monsters["goblin"];
        }

        /// <summary>Build the initial battle: party (left) vs the stage's pack + boss (right).</summary>
        public static CombatState InitCombat(IReadOnlyList<HeroInstance> party, int stage, GameConfig cfg, Rng rng)
        {
            var s = new CombatState { Stage = stage, Kind = EncounterKind.Encounter };
            s.ArenaId = cfg.ArenaForStage(stage)?.Id;
            AddParty(s, party, cfg);

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt, cfg);
            double scale = StageScale(rt, cfg);

            for (int j = 0; j < rt.PackCount; j++)
            {
                var mdef = TrashDef(cfg, stage, j);
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
        public static CombatState InitFarm(IReadOnlyList<HeroInstance> party, int stage, GameConfig cfg, Rng rng,
                                           IReadOnlyList<ModifierInstance>? activeModifiers = null)
        {
            var s = new CombatState { Stage = stage, Kind = EncounterKind.Farm };
            s.ArenaId = cfg.ArenaForStage(stage)?.Id;
            if (activeModifiers != null) s.ActiveModifiers = new List<ModifierInstance>(activeModifiers);
            AddParty(s, party, cfg);

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt, cfg);

            int initial = Math.Min(cfg.Balance.SpawnBatchSize, cfg.Balance.MobCap);
            if (initial > 0) SpawnPack(s, rt, cfg, rng, initial, ArenaOf(s, cfg));

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
            s.ArenaId = cfg.ArenaForStage(stage)?.Id;
            AddParty(s, party, cfg);

            var rt = cfg.Stages.Find(r => r.Stage == stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt, cfg);

            if (cfg.Monsters.TryGetValue(rt.BossId, out var boss))
            {
                double major = rt.IsMajorBoss ? cfg.Balance.MajorBossMult : 1.0;
                s.Entities.Add(MakeMonster(cfg, boss, "EBOSS", new Vec2(4, 0),
                    StageScale(rt, cfg) * major, true, HpScale(rt, cfg) * cfg.Balance.BossHpMult * major));
            }

            ApplyBossModifier(s, stage, cfg); // the boss exhibits its stage's modifier (the source)
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
            s.Loot = LootContext.ForStage(rt, cfg);
            if (cfg.Monsters.TryGetValue(rt.BossId, out var boss))
            {
                double major = rt.IsMajorBoss ? cfg.Balance.MajorBossMult : 1.0;
                var c = PartyCentroid(s);
                double w = cfg.Balance.MapHalfWidth - 1.0, d = cfg.Balance.MapHalfDepth - 1.0;
                var pos = new Vec2(Math.Clamp(c.X + cfg.Balance.BossSpawnDistance, -w, w), Math.Clamp(c.Y, -d, d));
                var arena = ArenaOf(s, cfg); // same stage/map: ArenaId unchanged, but keep the boss walkable
                if (arena != null) pos = arena.Clamp(pos);
                s.Entities.Add(MakeMonster(cfg, boss, "EBOSS", pos,
                    StageScale(rt, cfg) * major, true, HpScale(rt, cfg) * cfg.Balance.BossHpMult * major));
            }

            ApplyBossModifier(s, s.Stage, cfg); // boss exhibits its stage's modifier (the source)
            s.Kind = EncounterKind.BossChallenge;
            s.TimeMs = 0;
            s.SpawnTimerMs = 0;
            s.Status = CombatStatus.Running;
        }

        /// <summary>
        /// Tower of Ascension: build a FRESH tower-floor fight — its own CombatState, like
        /// <see cref="InitDungeon"/> (mode isolation, user call 2026-07-06: alt modes LOAD into their
        /// own map; the old in-place EnterTower shared the campaign state and leaked positions both
        /// ways). Party starts at the map centre on the FLOOR's zone arena (floors travel the zones);
        /// floor N's pack spawns a step ahead — scaled by the tower's OWN steep curve
        /// (<see cref="Tower.FloorHpMult"/> / <see cref="Tower.FloorDmgMult"/>, not the stage ladder),
        /// wearing the floor's modifier as a full (not behavior-only) buff so it bites, plus a guardian
        /// boss on milestone floors. The fight is bounded (no respawns; win = all enemies dead, lose =
        /// wipe / failsafe timeout) and grants no farm income (see HandleDeath).
        /// </summary>
        public static CombatState InitTower(IReadOnlyList<HeroInstance> party, int floor, GameConfig cfg, Rng rng)
        {
            var s = new CombatState { Stage = floor, Kind = EncounterKind.Tower, TowerFloor = floor };
            s.ArenaId = cfg.ArenaForStage(floor)?.Id;
            // Tower kills pay no farm income (HandleDeath gates on Kind), but set a coherent loot
            // context anyway — every Init* does, and a zeroed struct would be a trap for future code.
            var rt = cfg.Stages.Find(r => r.Stage == floor) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt, cfg);
            AddParty(s, party, cfg);

            // Party cluster sits at the origin (inside every zone arena by the authoring rule);
            // clamp defensively anyway so a future exotic arena can't strand anyone.
            var arena = ArenaOf(s, cfg);
            if (arena != null)
                foreach (var e in s.Entities)
                    if (e.Team == Team.Party) e.Pos = arena.Clamp(e.Pos);

            double hpScale = Tower.FloorHpMult(floor, cfg);
            double dmgScale = Tower.FloorDmgMult(floor, cfg);
            var c = PartyCentroid(s);
            double w = cfg.Balance.MapHalfWidth - 1.0, d = cfg.Balance.MapHalfDepth - 1.0;

            int pack = cfg.Balance.TowerPackBase + floor / Math.Max(1, cfg.Balance.TowerPackPerFloors);
            for (int j = 0; j < pack; j++)
            {
                var mdef = TrashDef(cfg, floor, j); // floor as zone key — the climb travels the zones
                var pos = new Vec2(Math.Clamp(c.X + cfg.Balance.BossSpawnDistance + j * 0.6, -w, w),
                                   Math.Clamp(c.Y + (j - pack / 2) * 1.0, -d, d));
                if (arena != null) pos = arena.Clamp(pos);
                s.Entities.Add(MakeMonster(cfg, mdef, "E" + j, pos, dmgScale, false, hpScale));
            }

            // Guardian boss on milestone floors (the floor you bank a permanent buff on):
            // the floor's ZONE boss, falling back to the first stage's boss pre-zones.
            string guardianId = cfg.ZoneForStage(floor)?.BossId
                ?? (cfg.Stages.Count > 0 ? cfg.Stages[0].BossId : "");
            if (floor % Math.Max(1, cfg.Balance.TowerMilestoneEvery) == 0
                && cfg.Monsters.TryGetValue(guardianId, out var boss))
            {
                var pos = new Vec2(Math.Clamp(c.X + cfg.Balance.BossSpawnDistance, -w, w), Math.Clamp(c.Y, -d, d));
                if (arena != null) pos = arena.Clamp(pos);
                s.Entities.Add(MakeMonster(cfg, boss, "EBOSS", pos, dmgScale, true, hpScale * cfg.Balance.BossHpMult));
            }

            // Floor modifier (the puzzle layer) on every enemy, at strength = floor — full buff.
            string? modId = cfg.TowerModifierForFloor(floor);
            if (modId != null && cfg.Modifiers.TryGetValue(modId, out var modDef))
                foreach (var e in s.Entities)
                    if (e.Team == Team.Enemy) ApplyModifier(e, modDef, floor, 1.0, cfg, behaviorOnly: false);

            return s;
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
            s.Loot = LootContext.ForStage(rt, cfg);
            s.Kind = EncounterKind.Farm;
            s.TimeMs = 0;
            s.Status = CombatStatus.Running;
            s.SpawnTimerMs = spawnDelayMs; // lull before the next pack (no instant respawn)

            // A stage hop can cross into a new zone/arena — re-resolve and clamp the party into it so
            // nobody resumes stranded off the walkable region.
            s.ArenaId = cfg.ArenaForStage(stage)?.Id;
            var arena = ArenaOf(s, cfg);
            if (arena != null)
                foreach (var e in s.Entities)
                    if (e.Team == Team.Party) e.Pos = arena.Clamp(e.Pos);
        }

        /// <summary>
        /// Roguelite dungeon (roguelite mode): build a FRESH, fully-separate combat state for a generated
        /// crypt run — never reuses/mutates the live farm state, so the two modes can't leak into each
        /// other. Kind = Dungeon, Stage = <paramref name="stage"/> (drives difficulty scaling exactly like
        /// a stage fight), the <see cref="DungeonArena"/> wraps <paramref name="d"/> as the walkable
        /// surface (ArenaId null), and the party is added at the entrance room centre (fanned by the usual
        /// PartyStartPos cluster, Clamp'd onto the grid). Then it materialises the dungeon's authored
        /// spawns: each <see cref="DungeonSpawn"/> becomes a monster at its cell centre — Tier 0 = a roster
        /// trash mob (cycled by spawn index), Tier 1 = the same promoted to Elite (<see cref="ApplyRank"/>),
        /// Tier 2 = the floor boss (IsBoss, BossHpMult). Stats scale via the same StageScale/HpScale the
        /// farm uses. Every enemy is stamped with its spawn's <see cref="DungeonSpawn.RoomId"/> (drives the
        /// room-sweep objective). Non-boss spawns start non-aggro and idle in place (staggered wander
        /// repick via rng, the SpawnPack pattern); the boss stays aggro. Deterministic:
        /// <paramref name="d"/>.Spawns is iterated in list order. Returns the new state.
        /// </summary>
        public static CombatState InitDungeon(IReadOnlyList<HeroInstance> party, int stage, Dungeon d,
                                              IReadOnlyList<string> trashRoster, string bossId, GameConfig cfg, Rng rng,
                                              double hpMult = 1.0, double dmgMult = 1.0)
        {
            var s = new CombatState { Stage = stage, Kind = EncounterKind.Dungeon };
            var arena = new DungeonArena(d, cfg.Balance.DungeonCorridorSight);
            s.Dungeon = arena;
            s.ArenaId = null; // the DungeonArena is the surface (ArenaOf returns it first)
            AddParty(s, party, cfg);

            // Party at the entrance room centre, fanned by the usual cluster offsets and Clamp'd onto the
            // walkable grid so nobody spawns in a wall/corner.
            var entrance = arena.EntranceCentre();
            foreach (var e in s.Entities)
                if (e.Team == Team.Party)
                {
                    var off = PartyStartPos(e.Slot == int.MaxValue ? 0 : e.Slot);
                    e.Pos = arena.Clamp(new Vec2(entrance.X + off.X, entrance.Y + off.Y));
                }

            SeedDungeonSpawns(s, d, trashRoster, bossId, cfg, rng, hpMult, dmgMult);

            // §7.3 door/key rules: remember the boss room, and require its key only when the floor
            // actually generated a Key room (short/legacy layouts stay key-free).
            foreach (var r in d.Rooms)
            {
                if (r.Type == RoomType.Boss) s.DungeonBossRoomId = r.Id;
                else if (r.Type == RoomType.Key) s.DungeonKeyRequired = true;
            }

            // §7.3 room-clear reward beat: a cleared room pays a gold burst worth
            // DungeonRoomClearMobEquiv average trash kills of THIS floor's roster, stage-scaled
            // like every kill payout (balances floor — display rounding rule).
            {
                double avgGold = 0; int rosterN = 0;
                foreach (var mid in trashRoster)
                    if (cfg.Monsters.TryGetValue(mid, out var md)) { avgGold += md.GoldReward; rosterN++; }
                if (rosterN > 0)
                    s.DungeonRoomClearGold = (long)Math.Floor(
                        avgGold / rosterN * cfg.Balance.KillRewardMult(stage) * cfg.Balance.DungeonRoomClearMobEquiv);
            }

            s.TimeMs = 0;
            s.SpawnTimerMs = 0;
            s.Status = CombatStatus.Running;
            return s;
        }

        /// <summary>Materialise the dungeon's authored spawns onto <paramref name="s"/> (shared by the
        /// init path). Deterministic — iterates d.Spawns in list order, cycling the trash roster by spawn
        /// index, and stamps each enemy's DungeonRoomId from its spawn.</summary>
        private static void SeedDungeonSpawns(CombatState s, Dungeon d, IReadOnlyList<string> trashRoster,
                                              string bossId, GameConfig cfg, Rng rng,
                                              double hpMult = 1.0, double dmgMult = 1.0)
        {
            // Stat scale mirrors a stage fight at the current stage — deeper players face tougher
            // floors. The optional multipliers stack the crypt's per-floor depth ramp on top
            // (Crypt.FloorHpMult / FloorDmgMult — the roguelite difficulty ladder).
            var rt = cfg.Stages.Find(r => r.Stage == s.Stage) ?? cfg.Stages[0];
            s.Loot = LootContext.ForStage(rt, cfg);
            double atkScale = StageScale(rt, cfg) * dmgMult, hpScale = HpScale(rt, cfg) * hpMult;

            int idx = 0;
            foreach (var sp in d.Spawns)
            {
                var pos = new Vec2(sp.X + 0.5, sp.Y + 0.5); // cell centre in world space
                if (sp.Tier == 2)
                {
                    if (cfg.Monsters.TryGetValue(bossId, out var bossDef))
                    {
                        var bossMob = MakeMonster(cfg, bossDef, "EBOSS", pos, atkScale, true,
                            hpScale * cfg.Balance.BossHpMult);
                        bossMob.DungeonRoomId = sp.RoomId;
                        s.Entities.Add(bossMob);
                    }
                    // A tier-2 spawn is a boss placement; it never counts toward the trash-index cycle.
                    continue;
                }

                // Tier 0/1 trash — cycle the roster (guard an empty roster by falling back to the config
                // trash resolver so a run always populates).
                MonsterDef mdef = trashRoster.Count > 0 && cfg.Monsters.TryGetValue(trashRoster[idx % trashRoster.Count], out var rm)
                    ? rm
                    : TrashDef(cfg, s.Stage, idx);
                var mob = MakeMonster(cfg, mdef, "E" + idx, pos, atkScale, false, hpScale);
                mob.DungeonRoomId = sp.RoomId;
                mob.Aggro = false;          // idle until a hero hits it (or it's woken by an in-room fight)
                mob.WanderTarget = pos;     // holds in place until the staggered repick
                mob.WanderCdMs = rng.RandRange(0, cfg.Balance.WanderMaxMs); // staggered — mirrors SpawnPack
                if (sp.Tier == 1) ApplyRank(mob, MonsterRank.Elite, cfg); // room-typed elite
                if (sp.Tier == 3)
                {
                    // §7.3 KEY BEARER: its death drops the Boss Key (HandleDeath flips the state).
                    // Elite rank gives it heft AND the existing rank tint/glow — the "marked" tell.
                    mob.DungeonKeyBearer = true;
                    ApplyRank(mob, MonsterRank.Elite, cfg);
                }
                // §7.3 wave phases: wave 0 stands ready; later waves wait fully built (stats and
                // rng rolled HERE in authored order, so releasing them later costs no draws and
                // determinism never depends on when a wave fires).
                mob.DungeonWave = sp.Wave;
                if (sp.Wave > 0) s.DungeonPendingWaves.Add(mob);
                else s.Entities.Add(mob);
                idx++;
            }
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
                stats = Tower.ApplyAccountBuffs(stats, save, cfg); // permanent Tower milestone buffs (account-wide)
                stats = Crypt.ApplyBoons(stats, save, cfg);        // permanent crypt boons (grave-dust track)
                double oldMax = e.MaxHp;
                double newMax = stats.Get(StatKey.Hp);

                e.Stats = stats;
                e.Skills = Skills.ActiveKit(hero, cfg); // newly-revealed kit actives apply live
                e.SkillRanks = new Dictionary<string, int>(hero.SkillRanks); // rank invests apply live
                e.MaxHp = newMax;
                e.AttackIntervalMs = AttackInterval(stats);
                if (e.Hp > 0) e.Hp = Math.Min(newMax, e.Hp + Math.Max(0.0, newMax - oldMax));
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
            s.LeaderRefId = save.LeaderHeroId; // keep the chosen formation leader in sync

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
                int idx = kv.Value;
                s.Entities.Add(new CombatEntity
                {
                    Id = "P" + idx + "_" + hero.Id,
                    Team = Team.Party,
                    Pos = PartyStartPos(idx),
                    Stats = stats,
                    Hp = hp,
                    MaxHp = hp,
                    AttackIntervalMs = AttackInterval(stats),
                    RefKind = "hero",
                    RefId = hero.Id,
                    Slot = idx,
                    RangedRole = IsRangedHero(hero, cfg),
                    BodyRadius = cfg.Balance.UnitRadius,
                    Skills = Skills.ActiveKit(hero, cfg),
                    SkillRanks = new Dictionary<string, int>(hero.SkillRanks),
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

        /// <summary>A follower's home behind the leader, role-aware: melee flank at the leader's
        /// shoulder (FormationMeleeBack), ranged park at casting distance (FormationRangedBack).
        /// Within a role, <paramref name="roleRank"/> 0 sits back-left, 1 back-right, then each
        /// further pair steps another <see cref="Balance.FormationBack"/> row back. <paramref
        /// name="heading"/> is the unit vector the leader faces (toward the pack), so the wing
        /// always trails behind it.</summary>
        private static Vec2 FormationHome(CombatEntity leader, Vec2 heading, bool ranged, int roleRank, GameConfig cfg,
                                          double? rangedBackOverride = null)
        {
            var perp = new Vec2(-heading.Y, heading.X);         // 90° to the heading = the wing axis
            double sideSign = (roleRank % 2 == 0) ? 1.0 : -1.0; // alternate left / right within the role
            double baseBack = ranged ? (rangedBackOverride ?? cfg.Balance.FormationRangedBack) : cfg.Balance.FormationMeleeBack;
            double back = baseBack + cfg.Balance.FormationBack * (roleRank / 2);
            double side = cfg.Balance.FormationSide;
            return new Vec2(
                leader.Pos.X - heading.X * back + perp.X * side * sideSign,
                leader.Pos.Y - heading.Y * back + perp.Y * side * sideSign);
        }

        /// <summary>True when the hero's def marks it as a ranged role — drives formation
        /// slotting (casting distance), fire-in-transit, and panic kiting. Unknown defs = melee.</summary>
        private static bool IsRangedHero(HeroInstance hero, GameConfig cfg)
            => cfg.Heroes.TryGetValue(hero.DefId, out var def) && def.Role == "ranged";

        private static void AddParty(CombatState s, IReadOnlyList<HeroInstance> party, GameConfig cfg)
        {
            int idx = 0;
            foreach (var hero in party)
            {
                var stats = Stats.ComputeHeroStats(hero, cfg);
                double hp = stats.Get(StatKey.Hp);
                s.Entities.Add(new CombatEntity
                {
                    Id = "P" + idx + "_" + hero.Id,
                    Team = Team.Party,
                    Pos = PartyStartPos(idx),
                    Stats = stats,
                    Hp = hp,
                    MaxHp = hp,
                    AttackIntervalMs = AttackInterval(stats),
                    RefKind = "hero",
                    RefId = hero.Id,
                    Slot = idx,
                    RangedRole = IsRangedHero(hero, cfg),
                    BodyRadius = cfg.Balance.UnitRadius,
                    Skills = Skills.ActiveKit(hero, cfg),
                    SkillRanks = new Dictionary<string, int>(hero.SkillRanks),
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
        private static void SpawnPack(CombatState s, StageDef rt, GameConfig cfg, Rng rng, int count, IArenaSurface? arena)
        {
            double w = cfg.Balance.MapHalfWidth - 1.0, d = cfg.Balance.MapHalfDepth - 1.0;
            var c = PartyCentroid(s);
            double ang = rng.RandRange(0, 2.0 * Math.PI);
            double rad = rng.RandRange(cfg.Balance.SpawnRingInner, cfg.Balance.SpawnRingOuter);
            var center = new Vec2(c.X + Math.Cos(ang) * rad, c.Y + Math.Sin(ang) * rad);
            double pr = cfg.Balance.PackRadius;

            for (int i = 0; i < count; i++)
            {
                var mdef = TrashDef(cfg, rt.Stage, s.SpawnCount);
                // Same rng draws in the same order as the open-plane path, THEN clamp: an arena
                // projects the pack point onto the walkable region; no arena keeps the rect clamp.
                double px = rng.RandRange(-pr, pr), py = rng.RandRange(-pr, pr);
                var pos = arena != null
                    ? arena.Clamp(new Vec2(center.X + px, center.Y + py))
                    : new Vec2(Math.Clamp(center.X + px, -w, w), Math.Clamp(center.Y + py, -d, d));
                var mob = MakeMonster(cfg, mdef, "E" + s.SpawnCount, pos, StageScale(rt, cfg), false, HpScale(rt, cfg));
                mob.Aggro = false;          // ambles until a hero hits it
                mob.WanderTarget = pos;     // idle in place until...
                mob.WanderCdMs = rng.RandRange(0, cfg.Balance.WanderMaxMs); // ...a staggered first repick
                ApplyRank(mob, RollRank(rng, cfg), cfg); // pack variety: maybe an elite/rare
                foreach (var m in s.ActiveModifiers)     // player's active modifiers (Lever 1)
                    ApplyModifier(mob, m.Def, m.Strength, m.Tuning, cfg, behaviorOnly: false);
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

        /// <summary>Roll a spawned trash mob's rank (pack variety): Rare first, then Elite,
        /// else Normal. One rng draw — deterministic.</summary>
        private static MonsterRank RollRank(Rng rng, GameConfig cfg)
        {
            double r = rng.Next();
            if (r < cfg.Balance.RareChance) return MonsterRank.Rare;
            if (r < cfg.Balance.RareChance + cfg.Balance.EliteChance) return MonsterRank.Elite;
            return MonsterRank.Normal;
        }

        /// <summary>Promote a mob to an elite/rare: scale HP + Atk, fatten the body (a visual
        /// tell + harder to surround), and tag the rank (drives rewards/loot in HandleDeath and
        /// the renderer's highlight). No-op for Normal. Mutates the entity.</summary>
        private static void ApplyRank(CombatEntity mob, MonsterRank rank, GameConfig cfg)
        {
            if (rank == MonsterRank.Normal) return;
            var b = cfg.Balance;
            double hpMult  = rank == MonsterRank.Rare ? b.RareHpMult   : b.EliteHpMult;
            double atkMult = rank == MonsterRank.Rare ? b.RareAtkMult  : b.EliteAtkMult;
            double bodyMult = rank == MonsterRank.Rare ? b.RareBodyMult : b.EliteBodyMult;

            mob.Stats[StatKey.Hp]  = mob.Stats.Get(StatKey.Hp) * hpMult;
            mob.Stats[StatKey.Atk] = mob.Stats.Get(StatKey.Atk) * atkMult;
            mob.MaxHp = mob.Stats.Get(StatKey.Hp);
            mob.Hp = mob.MaxHp;
            mob.BodyRadius *= bodyMult;
            mob.Rank = rank;
        }

        /// <summary>Per-kill XP/gold multiplier for a mob's rank (elites/rares pay out more).</summary>
        private static double RankRewardMult(MonsterRank rank, GameConfig cfg) => rank switch
        {
            MonsterRank.Rare  => cfg.Balance.RareRewardMult,
            MonsterRank.Elite => cfg.Balance.EliteRewardMult,
            _ => 1.0,
        };

        /// <summary>
        /// Apply a monster modifier (Lever 1) to an entity. Farm trash gets the full treatment:
        /// stat mults (×(1 + coeff·strength)), the per-hit behavior fraction (capped), and the
        /// thematic reward buff folded into the kill payout (so HandleDeath needs no save lookup).
        /// <paramref name="behaviorOnly"/> (bosses) sets ONLY the behavior + tag — a deep boss
        /// keeps its tuned HP so it stays killable inside the challenge timer. Mutates the entity;
        /// stacks (each call multiplies stats and adds to the reward buffs). Deterministic.
        /// </summary>
        private static void ApplyModifier(CombatEntity e, ModifierDef def, int strength, double tuning, GameConfig cfg, bool behaviorOnly)
        {
            // Shop tuning scales the mod's effective potency — BOTH danger and reward (floored at 1.0
            // in the reducer, so eff ≥ strength). Bosses/tower pass tuning 1.0 (not player-tuned).
            double eff = strength * tuning;

            if (!behaviorOnly)
            {
                foreach (var kv in def.StatPerStrength)
                    e.Stats[kv.Key] = e.Stats.Get(kv.Key) * (1.0 + kv.Value * eff);
                e.MaxHp = e.Stats.Get(StatKey.Hp);
                e.Hp = e.MaxHp;
                e.AttackIntervalMs = AttackInterval(e.Stats);

                foreach (var part in def.Rewards) // reward split (hybrid mods pay several channels)
                {
                    double reward = part.PerStrength * eff;
                    switch (part.Channel)
                    {
                        case ModifierReward.Gold:     e.GoldMult += reward; break;
                        case ModifierReward.Xp:       e.XpMult += reward; break;
                        case ModifierReward.DropRate: e.DropRateBonus += reward; break;
                    }
                }
            }

            if (def.Behavior == ModifierBehavior.Splash)
            {
                // Mechanical: grant the mob a splash radius (additive — base is 0, so a multiplier
                // couldn't bootstrap it) so its attacks hit the whole party. Uncapped — it's a
                // distance in tiles, not the 0..1 sustain/reflect fraction the cap guards.
                e.Stats[StatKey.SplashRadius] = e.Stats.Get(StatKey.SplashRadius) + def.BehaviorPerStrength * eff;
            }
            else if (def.Behavior == ModifierBehavior.Chain)
            {
                // Mechanical: grant the mob chain jumps (additive count; floored + clamped in combat).
                e.Stats[StatKey.ChainCount] = e.Stats.Get(StatKey.ChainCount) + def.BehaviorPerStrength * eff;
            }
            else
            {
                double frac = Math.Min(cfg.Balance.ModifierBehaviorCap, def.BehaviorPerStrength * eff);
                if (def.Behavior == ModifierBehavior.Vampiric) e.Lifesteal = Math.Max(e.Lifesteal, frac);
                else if (def.Behavior == ModifierBehavior.Thorns) e.ThornsReflect = Math.Max(e.ThornsReflect, frac);
            }

            e.ModTypes.Add(def.Id);
        }

        /// <summary>Make the stage's boss exhibit its inherent modifier (the source you fight +
        /// bank) — behavior-only, so the timer stays fair. No-op if the stage defines none.</summary>
        private static void ApplyBossModifier(CombatState s, int stage, GameConfig cfg)
        {
            string? typeId = cfg.ModifierTypeForStage(stage);
            if (typeId == null || !cfg.Modifiers.TryGetValue(typeId, out var def)) return;
            foreach (var e in s.Entities)
                if (e.IsBoss) ApplyModifier(e, def, stage, 1.0, cfg, behaviorOnly: true);
        }

        /// <summary>Advance the sim one fixed step. Mutates state; returns this step's events.</summary>
        public static List<CombatEvent> StepCombat(CombatState s, double dtMs, GameConfig cfg, Rng rng)
        {
            var events = new List<CombatEvent>();
            if (s.Status != CombatStatus.Running) return events;

            // The arena (or null = open plane) for this whole step — threaded to every movement,
            // spawn, formation-home, dash, and collision site so units stay on the walkable region.
            var arena = ArenaOf(s, cfg);

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
                        SpawnPack(s, rt, cfg, rng, n, arena);
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

                // Heal-over-time (priest Sanctify): buff-granted % of MaxHp per second.
                // EffectiveStat because the stat only ever comes from ActiveBuffs.
                double hotPct = e.EffectiveStat(StatKey.HpRegenPct);
                if (hotPct > 0) e.Hp = Math.Min(e.MaxHp, e.Hp + e.MaxHp * hotPct * dtMs / 1000.0);

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

            // Solo formation: the lowest-slot living hero LEADS toward the nearest pack; the
            // rest hold a triangle behind it (heading = leader→pack). Computed once per step.
            CombatEntity? leader = null;
            CombatEntity? leaderPack = null;
            Vec2 heading = new Vec2(0, 1);
            // Per-follower formation slot: role (ranged?) + rank WITHIN that role, in party order.
            Dictionary<string, (bool ranged, int roleRank)>? followerRank = null;
            // Dungeon room-sweep objective: the room the leader currently travels toward when it has no
            // reachable target (recomputed once per step from the living enemy set; deterministic). null
            // outside a dungeon or when no living enemy resolves a room.
            int? dungeonObjective = s.Dungeon != null ? DungeonObjectiveRoom(s) : null;
            // Ids of living RANGED party heroes — the allies a melee hero peels to defend. Built
            // once per step so the peel preference (below) is O(1) per candidate.
            HashSet<string>? rangedAllyIds = null;
            // TargetIds the living MELEE party heroes are on — the front line's focus. A ranged
            // follower assists this set instead of plinking its own nearest mob (which would WAKE a
            // fresh mob rather than pile onto an already-tanked one). Built once per step.
            HashSet<string>? meleeFocusIds = null;
            if (s.Tactic == PartyTactic.Solo)
            {
                var line = s.Entities.Where(e => e.Team == Team.Party && e.Alive)
                                     .OrderBy(e => e.Slot).ThenBy(e => e.Id, StringComparer.Ordinal)
                                     .ToList();
                if (line.Count > 0)
                {
                    // An explicit pick is ALWAYS honored (even a ranged hero). With no pick — or a
                    // pick that isn't on the field (dead/benched) — the default leader is the first
                    // MELEE hero, falling back to the lowest slot when the party is all-ranged.
                    leader = (s.LeaderRefId != null ? line.Find(e => e.RefId == s.LeaderRefId) : null)
                             ?? line.Find(e => !e.RangedRole) ?? line[0];
                    leaderPack = FindNearestEnemy(s, leader, cfg);
                    // Sticky heading: the raw leader→pack vector is ~1 unit (and thus wildly noisy)
                    // once the leader stands inside a pack — separation jitter + nearest-pack flips
                    // whip it side to side each step, teleporting the projected follower slots. So we
                    // only ADOPT a fresh candidate when the stored heading is uninitialized (first
                    // step) or the leader is still TRAVELING (pack beyond EngageRadius = the long,
                    // stable vector). While engaged the heading stays FROZEN.
                    if (leaderPack != null)
                    {
                        double hx = leaderPack.Pos.X - leader.Pos.X, hy = leaderPack.Pos.Y - leader.Pos.Y;
                        double hl = Math.Sqrt(hx * hx + hy * hy);
                        if (hl > 1e-6)
                        {
                            bool uninit = s.FormationHeading.X == 0 && s.FormationHeading.Y == 0;
                            if (uninit || hl > cfg.Balance.EngageRadius)
                                s.FormationHeading = new Vec2(hx / hl, hy / hl);
                        }
                    }
                    // leaderPack == null (field clear): keep the stored heading as-is.
                    if (!(s.FormationHeading.X == 0 && s.FormationHeading.Y == 0))
                        heading = s.FormationHeading;
                    // Rank followers within their own role group, preserving party order.
                    followerRank = new Dictionary<string, (bool, int)>();
                    int meleeRank = 0, rangedRank = 0;
                    foreach (var f in line)
                    {
                        if (ReferenceEquals(f, leader)) continue;
                        followerRank[f.Id] = f.RangedRole ? (true, rangedRank++) : (false, meleeRank++);
                    }
                    // Living ranged party heroes = the casters a melee hero peels to defend.
                    rangedAllyIds = new HashSet<string>();
                    foreach (var f in line)
                        if (f.RangedRole) rangedAllyIds.Add(f.Id);
                    // TargetIds of the living MELEE heroes (leader included). The leader's advance
                    // branch sets his TargetId to the pack he is WALKING TOWARD (below), so this set
                    // naturally covers both "fighting" and "walking towards". TargetIds persist on
                    // entities between steps, so this reads last step's choices during this step's
                    // loop — deterministic, and one step of lag is fine.
                    meleeFocusIds = new HashSet<string>();
                    foreach (var f in line)
                        if (!f.RangedRole && f.TargetId != null) meleeFocusIds.Add(f.TargetId);
                }
            }

            foreach (var e in actors)
            {
                if (!e.Alive) continue; // could have died earlier this step

                if (e.AttackCdMs > 0) e.AttackCdMs = Math.Max(0, e.AttackCdMs - dtMs);

                // Idle (non-aggro) trash just ambles randomly; it doesn't seek or attack the
                // party until something hits it (ApplyHit flips Aggro on).
                if (e.Team == Team.Enemy && !e.Aggro) { Wander(e, cfg, dtMs, rng, arena, s.Dungeon); continue; }

                // A ready skill replaces this step's basic attack/move (M11).
                if (TryCastSkill(s, e, cfg, rng, events, arena)) continue;

                CombatEntity? target;
                // Panic micro-kite: a ranged follower with an aggro'd enemy inside PanicRadius
                // backpedals this step (recomputed statelessly below). Set only in the follower
                // branch; drives the move section to replace its move with a retreat.
                CombatEntity? panicThreat = null;
                if (e.Team == Team.Party && s.Tactic == PartyTactic.Solo)
                {
                    bool isLeader = leader == null || ReferenceEquals(e, leader);
                    if (isLeader)
                    {
                        // The leader fights anything within its wide engage reach and otherwise
                        // advances on the nearest pack — it pulls the whole triangle along. A MELEE
                        // leader PREFERS (within that same reach) an enemy attacking a ranged ally,
                        // peeling for the caster the retreat has dragged into range.
                        var acquired = FindNearestEnemyWithin(s, e, cfg.Balance.EngageRadius,
                            e.RangedRole ? null : rangedAllyIds);
                        // Sticky target: re-acquiring "nearest" every step flapped between equidistant
                        // mobs and dragged the whole formation wing with it. Keep the foe we're already
                        // hitting (cur) if it's still alive and in reach — UNLESS a caster is under
                        // attack and cur isn't the attacker (peel must still win). Peel only applies to
                        // a melee leader (ranged has no peel preference; keep that symmetry).
                        CombatEntity? cur = null;
                        if (e.TargetId != null)
                        {
                            foreach (var o in s.Entities)
                                if (o.Id == e.TargetId) { cur = o; break; }
                            if (cur != null && (!cur.Alive || cur.Team == e.Team
                                                || Vec2.Distance(e.Pos, cur.Pos) > cfg.Balance.EngageRadius
                                                // Sticky keep must re-pass the room gate: acquisition is
                                                // gated, but a leader drifting across a doorway mid-fight
                                                // could otherwise keep swinging at a mob a wall now hides.
                                                || (s.Dungeon != null && !s.Dungeon.GateTargets(e.Pos, cur.Pos))))
                                cur = null;
                        }
                        bool IsPeel(CombatEntity? o) => !e.RangedRole && o != null && o.TargetId != null
                                                        && rangedAllyIds != null && rangedAllyIds.Contains(o.TargetId);
                        target = (cur != null && (IsPeel(cur) || !IsPeel(acquired))) ? cur : acquired;
                        if (target == null)
                        {
                            double ms = e.EffectiveStat(StatKey.MoveSpd);
                            if (ms <= 0) ms = MoveSpeedTilesPerSec;
                            // Dungeon leader travel: with no reachable target (room-gating hides packs
                            // behind walls), SWEEP toward the current objective room — the shallowest
                            // (entrance-nearest) room that still has living enemies — by descending that
                            // room's flow field. This routes the leader room-to-room in depth order rather
                            // than beelining the boss, so the crypt is cleared in sequence. The wing trails
                            // via the frozen heading (no visible pack ⇒ heading holds; re-snaps on the next
                            // pack). Step toward the next downhill cell centre at MoveSpd. When no objective
                            // resolves (shouldn't happen while enemies live), hold.
                            if (s.Dungeon != null)
                            {
                                e.TargetId = null;
                                // Sweep travel: with no enemy in engage reach, route via the flow field
                                // toward the current objective pack — the NEAREST living enemy of the
                                // shallowest living room, using ITS cell as the field seed (not the room
                                // CENTRE — a pack can spawn on corridor-tagged cells far from centre, where a
                                // centre-seeded field would strand the leader at the seed). The field threads
                                // corridors, so this reaches the pack room-to-room in depth order. Falls back
                                // to the room-centre field if the room somehow has no living enemy. The wing
                                // trails via the frozen heading.
                                if (dungeonObjective is int objRoom)
                                {
                                    var goal = NearestEnemyOfRoom(s, e, objRoom);
                                    var next = goal != null
                                        ? s.Dungeon.DownhillStepToCell(e.Pos, (int)Math.Floor(goal.Pos.X), (int)Math.Floor(goal.Pos.Y))
                                        : s.Dungeon.DownhillStep(e.Pos, objRoom);
                                    MoveToward(e, next, ms * dtMs / 1000.0, arena);
                                }
                                continue;
                            }
                            if (leaderPack == null) { e.TargetId = null; continue; } // field clear: idle
                            e.TargetId = leaderPack.Id;
                            MoveToward(e, leaderPack.Pos, ms * dtMs / 1000.0, arena);
                            continue;
                        }
                    }
                    else
                    {
                        // A follower is leashed to its triangle SLOT, not its own position: it
                        // only fights enemies near that slot and otherwise slides back to it. This
                        // stops fast melee (e.g. the Thief) from chain-chasing packs across the map.
                        bool ranged = e.RangedRole;
                        int roleRank = 0;
                        if (followerRank != null && followerRank.TryGetValue(e.Id, out var fr))
                        { ranged = fr.ranged; roleRank = fr.roleRank; }
                        Vec2 home = FormationHome(leader!, heading, ranged, roleRank, cfg,
                            // Dungeon corridors compress the ranged standoff: 4.6 behind the leader
                            // parks a caster in the PREVIOUS room, out of every fight (user-caught:
                            // "the ice mage is lagging behind too often to be of any use").
                            ranged && s.Dungeon != null ? cfg.Balance.DungeonRangedBack : (double?)null);
                        // An arena can place a slot off the walkable region (e.g. behind a leader on a
                        // shore) — degrade the home to the nearest walkable point so the deadzone/hustle
                        // logic terminates at a reachable spot instead of running in place at a cliff.
                        if (arena != null) home = arena.Clamp(home);
                        // A MELEE follower prefers (within the SAME slot radius) an enemy attacking
                        // a ranged ally — it peels off its slot to defend the caster. A RANGED
                        // follower instead ASSISTS the front line: within the same radius it prefers
                        // an enemy a melee ally is already on (meleeFocusIds), so it stacks onto a
                        // tanked mob rather than waking a fresh one. (Leaders keep their own sticky
                        // acquisition — assist is a follower-only override.)
                        target = FindNearestEnemyNear(s, home, cfg.Balance.FormationBreakRadius,
                            e.RangedRole ? null : rangedAllyIds, e.RangedRole ? meleeFocusIds : null);
                        // Fire-in-transit (ranged followers only): if the slot found nothing, a
                        // ranged follower still shoots the nearest enemy already within its OWN
                        // attack reach of its CURRENT spot — but it never MOVES toward it. Prefers a
                        // melee-tanked mob (assist) among what's in reach. If nothing's in reach,
                        // treat as no target (fall through to home).
                        // Room-scoped sight is judged from the BODY, not the slot: the slot sits by the
                        // leader inside the fight room while the follower may still be crossing the
                        // doorway — dropping the slot-acquired target here means a follower never shoots
                        // into a room it hasn't entered (user rule 2026-07-06); it keeps homing to its
                        // slot and re-acquires the moment it steps in.
                        if (target != null && s.Dungeon != null && !s.Dungeon.GateTargets(e.Pos, target.Pos))
                            target = null;
                        if (target == null && e.RangedRole)
                        {
                            var inReach = NearestEnemyInReach(s, e, meleeFocusIds);
                            if (inReach != null) target = inReach; // in reach => normal attack path fires in place
                        }
                        if (target == null)
                        {
                            e.TargetId = null;
                            if (Vec2.Distance(e.Pos, home) > cfg.Balance.FormationDeadzone)
                            {
                                double ms = e.EffectiveStat(StatKey.MoveSpd);
                                if (ms <= 0) ms = MoveSpeedTilesPerSec;
                                // Dungeon follow floor: homing followers move AT LEAST at the leader's
                                // speed — corridor marches are long and walled, so a slower caster
                                // falls a room behind and contributes nothing (user-caught). Overworld
                                // keeps per-hero speeds (an open feel verdict) — this is dungeon-only.
                                if (s.Dungeon != null)
                                {
                                    double flms = leader!.EffectiveStat(StatKey.MoveSpd);
                                    if (flms <= 0) flms = MoveSpeedTilesPerSec;
                                    ms = Math.Max(ms, flms);
                                }
                                // Regroup hustle: a follower stranded past the break radius sprints
                                // at max(own, leader) × RegroupHustleMult so a geared leader can't
                                // outrun an ungeared follower between packs. Within the radius it
                                // just eases into slot at its own speed.
                                if (Vec2.Distance(e.Pos, home) > cfg.Balance.FormationBreakRadius)
                                {
                                    double lms = leader!.EffectiveStat(StatKey.MoveSpd);
                                    if (lms <= 0) lms = MoveSpeedTilesPerSec;
                                    ms = Math.Max(ms, lms) * cfg.Balance.RegroupHustleMult;
                                }
                                // Maze rejoin routing: straight-line homing wedges a follower on wall
                                // corners the moment it falls a doorway behind (Play-caught: both
                                // followers stranded at the entrance while the leader soloed the crypt).
                                // When the straight segment to the slot is NOT walkable, descend the
                                // flow field toward the leader instead — the same string-pulled travel
                                // the leader uses — and resume exact slotting once back in line of walk.
                                if (s.Dungeon != null && !s.Dungeon.SegmentWalkable(e.Pos, home))
                                {
                                    int lr = s.Dungeon.RoomAt(leader!.Pos);
                                    home = lr >= 0
                                        ? s.Dungeon.DownhillStep(e.Pos, lr)
                                        : s.Dungeon.DownhillStepToCell(e.Pos,
                                            (int)Math.Floor(leader!.Pos.X), (int)Math.Floor(leader!.Pos.Y));
                                }
                                MoveToward(e, home, ms * dtMs / 1000.0, arena);
                            }
                            continue;
                        }
                        // Ranged follower HAS a target: if an aggro'd enemy is right on top of it,
                        // it backpedals this step (kites) instead of holding — attacks aren't gated
                        // on standing still, so it keeps firing at no DPS cost. Never for melee.
                        if (e.RangedRole)
                            panicThreat = NearestAggroEnemyWithin(s, e, cfg.Balance.PanicRadius);
                    }
                }
                else
                {
                    target = (e.Team == Team.Party && groupTarget != null && groupTarget.Alive)
                        ? groupTarget
                        : FindNearestEnemy(s, e, cfg);
                }
                e.TargetId = target?.Id;
                if (target == null) continue;

                double dist = Vec2.Distance(e.Pos, target.Pos);
                double baseRange = e.Stats.Get(StatKey.AttackRange);
                if (baseRange <= 0) baseRange = MeleeRange;
                bool isMelee = baseRange <= 2.0; // resolved base range this short => a melee attacker
                // Reach a target's body, not its centre — so a chunky boss the separation
                // pass holds at arm's length is still meleeable.
                double range = baseRange + target.BodyRadius;

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

                        // Chaining: the same swing arcs to up to ChainCount nearby enemies, hopping
                        // target → nearest-unhit → … within ChainRange (a moderate arc, not screen-wide).
                        int chains = Math.Min(cfg.Balance.MaxChainJumps, (int)Math.Floor(e.Stats.Get(StatKey.ChainCount)));
                        if (chains > 0)
                        {
                            var chained = new HashSet<string> { target.Id };
                            var from = target;
                            for (int c = 0; c < chains; c++)
                            {
                                var next = NearestEnemyWithin(s, from, target.Team, cfg.Balance.ChainRange, chained);
                                if (next == null) break;
                                ApplyHit(s, e, next, cfg, rng, events);
                                chained.Add(next.Id);
                                from = next;
                            }
                        }
                    }
                }
                else if (panicThreat == null)
                {
                    double moveSpd = e.EffectiveStat(StatKey.MoveSpd);
                    if (moveSpd <= 0) moveSpd = MoveSpeedTilesPerSec; // fallback for entities w/o the stat
                    // Surround ring (the stuck/orbit fix): a MELEE attacker approaches a personal
                    // CONTACT POINT on the target's rim instead of beelining to its centre, so a
                    // crowd fans out around the body rather than shoving/orbiting the same point.
                    // Ranged attackers keep centre-seeking (they stop at range anyway). Stateless —
                    // the angle folds the attacker's Id into a deterministic hash each step.
                    Vec2 dest = isMelee ? MeleeContactPoint(e, target) : target.Pos;
                    // Dungeon pathing: MoveToward has no pathfinding, so a straight approach jams whenever a
                    // wall sits between attacker and target — across a doorway (corridor-sight acquisition)
                    // OR inside a concave room (ellipse/octagon) where the chord clips the wall. A dungeon
                    // PARTY unit therefore routes its approach through the flow field toward the target's
                    // cell: the BFS descends a guaranteed-walkable path (and, in the open, descends straight
                    // — so convex/same-room cases behave as before, just without the jam). Only kicks in
                    // when genuinely out of reach and the field yields forward progress; otherwise it falls
                    // through to the straight approach (arrival cap intact). Monsters keep straight-line —
                    // they only chase what's already adjacent.
                    bool routed = false;
                    if (s.Dungeon != null && e.Team == Team.Party)
                    {
                        var next = s.Dungeon.DownhillStepToCell(e.Pos,
                            (int)Math.Floor(target.Pos.X), (int)Math.Floor(target.Pos.Y));
                        if (next.X != e.Pos.X || next.Y != e.Pos.Y) // field advanced (not at the seed cell)
                        {
                            MoveToward(e, next, moveSpd * dtMs / 1000.0, arena);
                            routed = true;
                        }
                    }
                    if (!routed)
                    {
                        // Arrival cap: stop just INSIDE reach (range - ArriveDepth) instead of striding to
                        // the rim contact point and getting shoved back out by ResolveCollisions next step
                        // (the in-place flap). Direction is unchanged — only this step's LENGTH is clamped.
                        double stepLen = Math.Min(moveSpd * dtMs / 1000.0, Math.Max(0.0, dist - (range - ArriveDepth)));
                        MoveToward(e, dest, stepLen, arena);
                    }
                }

                // Panic retreat: replaces this step's movement entirely (attacked or not) — a
                // panicked ranged follower runs TO the leader (not away from the threat, which
                // ran her off screen) and holds once within PanicHoldDist. Attacks stay ungated,
                // so she keeps firing the whole retreat. Stateless. If the leader is somehow gone,
                // hold position rather than fall back to the old unbounded away-vector.
                if (panicThreat != null && leader != null
                    && Vec2.Distance(e.Pos, leader.Pos) > cfg.Balance.PanicHoldDist)
                {
                    double retreatSpd = e.EffectiveStat(StatKey.MoveSpd);
                    if (retreatSpd <= 0) retreatSpd = MoveSpeedTilesPerSec;
                    MoveToward(e, leader.Pos, retreatSpd * dtMs / 1000.0, arena);
                }
            }

            // Soft-body separation: push overlapping units apart so they occupy space
            // instead of stacking. Runs AFTER movement/attacks (which already resolved at
            // their pre-push positions), so it only affects spacing, never hit outcomes.
            // In a dungeon the SWEEP LEADER is immovable in separation (its followers yield fully),
            // so a 3-body column can't pin the leader against a doorway/room-edge pinch point and
            // stall the flow-field sweep — the followers file in behind instead of shoving it back.
            ResolveCollisions(s, cfg, arena, s.Dungeon != null ? leader?.Id : null);

            // §7.3 sealed-door room progression — runs LAST so it sees final step positions and can
            // undo any crossing (movement, dash, collision push) with a deterministic clamp.
            if (s.Kind == EncounterKind.Dungeon && s.Dungeon != null)
                StepDungeonDoors(s, leader, events);

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
            else if (s.Kind == EncounterKind.Dungeon)
            {
                // FULL CLEAR: the run wins only when EVERY enemy is dead — the party sweeps the whole
                // crypt room by room, not just the boss room — and no wave still WAITS (§7.3 phases).
                // Lose on a full wipe or the failsafe timeout (a run that can't finish the sweep in
                // DungeonMaxRunSeconds).
                bool enemyAlive = s.Entities.Any(e => e.Team == Team.Enemy && e.Alive)
                                  || s.DungeonPendingWaves.Count > 0;
                if (!partyAlive) s.Status = CombatStatus.Lost;
                else if (!enemyAlive) s.Status = CombatStatus.Won;
                else if (s.TimeMs >= cfg.Balance.DungeonMaxRunSeconds * 1000.0) s.Status = CombatStatus.Lost;
                // Prune dead trash (same rationale as farm; keeps the entity list from growing).
                s.Entities.RemoveAll(e => e.Team == Team.Enemy && !e.Alive && !e.IsBoss);
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

            // Modifier behaviors (Lever 1), now team-agnostic so they work for BOTH a modified monster
            // (Lifesteal/ThornsReflect FIELD set in ApplyModifier) AND a hero whose imprinted gear grants
            // the matching EXCLUSIVE stat (Lifesteal/ThornsReflect read off Stats). Hits only ever cross
            // teams, so dropping the old team gates is safe. Capped so deep stacks can't reach 100%.
            double cap = cfg.Balance.ModifierBehaviorCap;

            // Vampiric / of Leeching: the attacker heals a fraction of damage dealt.
            double lifesteal = Math.Min(cap, attacker.Lifesteal + attacker.Stats.Get(StatKey.Lifesteal));
            if (lifesteal > 0)
                attacker.Hp = Math.Min(attacker.MaxHp, attacker.Hp + dmg * lifesteal);

            // Thorns / of Thorns: the victim reflects a fraction of damage taken back at the attacker.
            double thorns = Math.Min(cap, victim.ThornsReflect + victim.Stats.Get(StatKey.ThornsReflect));
            if (thorns > 0)
            {
                double reflect = dmg * thorns;
                attacker.Hp -= reflect;
                events.Add(new CombatEvent { Type = CombatEventType.Hit, SourceId = victim.Id, TargetId = attacker.Id, Amount = reflect });
                if (attacker.Hp <= 0) HandleDeath(s, attacker, cfg, rng, events);
            }

            if (victim.Hp <= 0) HandleDeath(s, victim, cfg, rng, events);
        }

        /// <summary>
        /// Try to cast the first ready skill in the entity's loadout (off cooldown, valid
        /// target). On success: set cooldown, apply the effect, emit a SkillCast event, and
        /// return true (the caller then skips this step's basic attack). Skills are gated on
        /// cooldown only — mana was removed. Deterministic — rng is only used inside ApplyHit.
        /// </summary>
        private static bool TryCastSkill(CombatState s, CombatEntity e, GameConfig cfg, Rng rng, List<CombatEvent> events, IArenaSurface? arena = null)
        {
            foreach (var id in e.Skills)
            {
                if (!cfg.Skills.TryGetValue(id, out var sk)) continue;            // unknown id
                if (e.SkillCdMs.TryGetValue(id, out var cd) && cd > 0) continue;  // on cooldown

                // Invested rank scales the skill's primary magnitude; at MaxRank the skill masters
                // and counts as rank + MasteryBonusRanks (§7.2). Rank 0 => 1.0 (= base).
                int rank = e.SkillRanks.TryGetValue(id, out var rk) ? rk : 0;
                double rankFactor = 1.0 + sk.EffectPerRank * Skills.EffectiveRank(rank, sk, cfg);

                switch (sk.Effect)
                {
                    case SkillEffectKind.Damage:
                    {
                        var target = PickDamageTarget(s, e, sk);
                        if (target == null) continue;
                        CastStart(e, sk, target.Id, events);
                        double mult = sk.DamageMult * rankFactor;
                        ApplyHit(s, e, target, cfg, rng, events, mult);
                        if (sk.AoeRadius > 0)
                        {
                            foreach (var o in s.Entities)
                                if (o.Team == target.Team && o.Alive && !ReferenceEquals(o, target)
                                    && Vec2.Distance(o.Pos, target.Pos) <= sk.AoeRadius)
                                    ApplyHit(s, e, o, cfg, rng, events, mult);
                        }
                        return true;
                    }
                    case SkillEffectKind.Heal:
                    {
                        var ally = PickHealTarget(s, e, sk);
                        if (ally == null) continue;
                        CastStart(e, sk, ally.Id, events);
                        double heal = Math.Max(1.0, e.EffectiveStat(StatKey.Atk) * sk.DamageMult * rankFactor);
                        ally.Hp = Math.Min(ally.MaxHp, ally.Hp + heal);
                        events.Add(new CombatEvent { Type = CombatEventType.Heal, SourceId = e.Id, TargetId = ally.Id, Amount = heal });
                        return true;
                    }
                    case SkillEffectKind.Buff:
                    {
                        if (!AnyEnemyAlive(s, e.Team)) continue; // don't waste buffs out of combat
                        // Party-wide buff (priest Sanctify): every living ally gets it. A
                        // heal-over-time is additionally gated on someone actually being hurt.
                        if (sk.Targeting == "party")
                        {
                            if (sk.BuffStat == StatKey.HpRegenPct && !AnyAllyInjured(s, e.Team)) continue;
                            CastStart(e, sk, e.Id, events);
                            foreach (var o in s.Entities)
                                if (o.Team == e.Team && o.Alive)
                                    o.Buffs.Add(new ActiveBuff { Stat = sk.BuffStat, Amount = sk.BuffAmount * rankFactor, RemainingMs = sk.BuffDurationMs });
                            return true;
                        }
                        CastStart(e, sk, e.Id, events);
                        e.Buffs.Add(new ActiveBuff { Stat = sk.BuffStat, Amount = sk.BuffAmount * rankFactor, RemainingMs = sk.BuffDurationMs });
                        return true;
                    }
                    case SkillEffectKind.Dash:
                    {
                        // Gap closer: leap to the nearest enemy within Range and strike on
                        // arrival. Skipped when already in melee contact — the cooldown is
                        // worth more than a zero-distance hop.
                        var target = PickDamageTarget(s, e, sk);
                        if (target == null) continue;
                        double gap = Vec2.Distance(e.Pos, target.Pos);
                        double contact = e.BodyRadius + target.BodyRadius + 0.15;
                        if (gap <= contact + 0.5) continue;
                        double inv = contact / gap; // land on the approach side, just touching
                        var landing = new Vec2(target.Pos.X - (target.Pos.X - e.Pos.X) * inv,
                                               target.Pos.Y - (target.Pos.Y - e.Pos.Y) * inv);
                        // Dungeon: the FLIGHT LINE must be walkable — a dash may never cut through a
                        // wall into a parallel corridor (user-caught in the maze layout). Blocked ⇒
                        // skip the skill this step (walk there like everyone else).
                        if (s.Dungeon != null && !s.Dungeon.SegmentWalkable(e.Pos, landing)) continue;
                        CastStart(e, sk, target.Id, events);
                        e.Pos = arena != null ? arena.Clamp(landing) : landing; // never dash off the walkable region
                        ApplyHit(s, e, target, cfg, rng, events, sk.DamageMult * rankFactor);
                        return true;
                    }
                }
            }
            return false;
        }

        private static void CastStart(CombatEntity e, SkillDef sk, string? targetId, List<CombatEvent> events)
        {
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

        /// <summary>Any living teammate missing health — gates the party heal-over-time
        /// so its cooldown isn't burned at full HP.</summary>
        private static bool AnyAllyInjured(CombatState s, Team team)
        {
            foreach (var o in s.Entities)
                if (o.Alive && o.Team == team && o.Hp < o.MaxHp - 0.5) return true;
            return false;
        }

        private static bool AnyEnemyAlive(CombatState s, Team team)
        {
            foreach (var o in s.Entities) if (o.Alive && o.Team != team) return true;
            return false;
        }

        /// <summary>Nearest alive entity on <paramref name="team"/> within <paramref name="range"/>
        /// of <paramref name="from"/>, excluding ids already hit. Stable Id tie-break. Drives the
        /// Chaining arc (target → nearest-unhit → …).</summary>
        private static CombatEntity? NearestEnemyWithin(CombatState s, CombatEntity from, Team team, double range, HashSet<string> exclude)
        {
            CombatEntity? best = null;
            double bestD = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team != team || exclude.Contains(o.Id)) continue;
                double d = Vec2.Distance(from.Pos, o.Pos);
                if (d > range) continue;
                if (d < bestD || (d == bestD && best != null && string.CompareOrdinal(o.Id, best.Id) < 0)) { bestD = d; best = o; }
            }
            return best;
        }

        /// <summary>Resolve a killed entity: party heroes are downed (respawn); monsters
        /// die and yield XP/gold/loot.</summary>
        private static void HandleDeath(CombatState s, CombatEntity target, GameConfig cfg, Rng rng, List<CombatEvent> events)
        {
            target.Hp = 0;
            events.Add(new CombatEvent { Type = CombatEventType.Death, EntityId = target.Id });

            if (target.Team == Team.Party)
            {
                // Farm: downed, not dead — respawns after the countdown. Boss challenge, Tower AND
                // Dungeon: NO respawn — a death there is for the whole run, so the fight is a real wall
                // (it fails once every hero is down). RespawnMs stays 0, leaving the hero plainly dead
                // rather than a frozen-timer "downed".
                if (s.Kind != EncounterKind.BossChallenge && s.Kind != EncounterKind.Tower
                    && s.Kind != EncounterKind.Dungeon)
                    target.RespawnMs = target.RespawnDurationMs;
                return;
            }

            if (target.IsBoss)
                events.Add(new CombatEvent { Type = CombatEventType.BossDefeated, Stage = s.Stage });

            // §7.3 key bearer down: the Boss Key drops (sim state — the boss-door gate reads it).
            if (target.DungeonKeyBearer && !s.DungeonBossKeyHeld)
            {
                s.DungeonBossKeyHeld = true;
                events.Add(new CombatEvent { Type = CombatEventType.BossKeyDrop, EntityId = target.Id });
            }

            // Tower grants NO farm income — the milestone account buffs are the reward, and a
            // grindable income source would undercut the one-clear-per-floor design.
            if (s.Kind == EncounterKind.Tower) return;

            // Loot + XP only from real monsters (guards synthetic test entities).
            if (target.RefKind == "monster" && cfg.Monsters.TryGetValue(target.RefId, out var mdef))
            {
                // Elites/rares pay out a multiple of a normal kill; active modifiers add their
                // thematic reward buff (gold/XP folded here, drop-rate into the loot context below).
                double mult = cfg.Balance.KillRewardMult(s.Stage) * RankRewardMult(target.Rank, cfg);
                s.PendingXp += (long)Math.Floor(mdef.XpReward * mult * target.XpMult);
                s.PendingGold += (long)Math.Floor(mdef.GoldReward * mult * target.GoldMult);

                var lootCtx = s.Loot;
                if (target.DropRateBonus > 0) lootCtx.DropRateMult *= (1.0 + target.DropRateBonus);

                // Bosses drop a guaranteed Unique+ bundle (+ ordinary extras),
                // sized by boss tier; elites/rares drop a boosted Rare-capped bundle; ordinary
                // trash uses the scarce per-kill chance.
                if (target.IsBoss)
                {
                    bool isMajor = (cfg.Stages.Find(st => st.Stage == s.Stage)?.IsMajorBoss) ?? false;
                    foreach (var drop in Loot.RollBossDrops(rng, lootCtx, cfg, isMajor))
                    {
                        s.PendingLoot.Add(drop);
                        events.Add(new CombatEvent { Type = CombatEventType.LootDrop, EntityId = target.Id, Item = drop });
                    }
                }
                else if (target.Rank != MonsterRank.Normal)
                {
                    int count = target.Rank == MonsterRank.Rare ? cfg.Balance.RareDropCount : cfg.Balance.EliteDropCount;
                    double rate = target.Rank == MonsterRank.Rare ? cfg.Balance.RareDropRateMult : cfg.Balance.EliteDropRateMult;
                    foreach (var drop in Loot.RollRankDrops(rng, lootCtx, cfg, count, rate))
                    {
                        Loot.ImprintDrop(rng, drop, target.ModTypes, s.ActiveModifiers, cfg); // mechanical-mod imprint
                        s.PendingLoot.Add(drop);
                        events.Add(new CombatEvent { Type = CombatEventType.LootDrop, EntityId = target.Id, Item = drop });
                    }
                }
                else
                {
                    var drop = Loot.RollDrop(rng, mdef, lootCtx, cfg);
                    if (drop != null)
                    {
                        Loot.ImprintDrop(rng, drop, target.ModTypes, s.ActiveModifiers, cfg); // mechanical-mod imprint
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

        /// <summary>True when any living enemy is ATTRIBUTED to <paramref name="roomId"/> (spawn-stamped
        /// DungeonRoomId — position-independent, so a mob nudged past the doorway still counts).</summary>
        private static bool RoomHasLivingEnemy(CombatState s, int roomId)
        {
            foreach (var e in s.Entities)
                if (e.Team == Team.Enemy && e.Alive && e.DungeonRoomId == roomId) return true;
            return false;
        }

        /// <summary>True when a wave of <paramref name="roomId"/> still waits in DungeonPendingWaves.</summary>
        private static bool RoomHasPendingWave(CombatState s, int roomId)
        {
            foreach (var p in s.DungeonPendingWaves)
                if (p.DungeonRoomId == roomId) return true;
            return false;
        }

        /// <summary>
        /// The §7.3 sealed-door pass (dungeon only, end of every step). In order:
        ///  1. CLEAR — a sealed room with no living attributed enemy unseals (RoomCleared: the
        ///     client's open-door fanfare beat) and joins DungeonClearedRooms.
        ///  2. SEAL — the leader standing in an uncleared room that still has living enemies slams
        ///     its doors (RoomSealed) and WAKES the whole room (the Mabinogi committed-arena beat).
        ///     The locked boss room never seals before the Boss Key is held — the gate below keeps
        ///     the party out of it entirely.
        ///  3. CONTAIN — while sealed, every living party hero AND the room's own mobs are clamped
        ///     back inside (any crossing this step — movement, dash, collision push — is undone), so
        ///     neither side can leave the fight and a stray mob can never soft-lock a clear. Without
        ///     the key, heroes are also clamped OUT of the boss room.
        /// Deterministic: clamps are pure ring searches; set iteration never drives logic.
        /// </summary>
        private static void StepDungeonDoors(CombatState s, CombatEntity? leader, List<CombatEvent> events)
        {
            var arena = s.Dungeon!;
            bool bossLocked = s.DungeonKeyRequired && !s.DungeonBossKeyHeld && s.DungeonBossRoomId >= 0;

            // 1 — the sealed room's fight phase ended: either the NEXT WAVE rises, or the room clears.
            if (s.DungeonSealedRoomId >= 0 && !RoomHasLivingEnemy(s, s.DungeonSealedRoomId))
            {
                int room = s.DungeonSealedRoomId;
                // Lowest pending wave of this room (authored list order = deterministic scan).
                int nextWave = int.MaxValue;
                foreach (var p in s.DungeonPendingWaves)
                    if (p.DungeonRoomId == room && p.DungeonWave < nextWave) nextWave = p.DungeonWave;

                if (nextWave != int.MaxValue)
                {
                    // Release the wave: pre-built mobs enter the fight already awake (the room is
                    // hot — the door stays sealed through the whole phase chain). Forward scan keeps
                    // the authored entity order in the live list.
                    foreach (var p in s.DungeonPendingWaves)
                    {
                        if (p.DungeonRoomId != room || p.DungeonWave != nextWave) continue;
                        p.Aggro = true;
                        s.Entities.Add(p);
                    }
                    s.DungeonPendingWaves.RemoveAll(p => p.DungeonRoomId == room && p.DungeonWave == nextWave);
                    events.Add(new CombatEvent { Type = CombatEventType.RoomWave, RoomId = room, Amount = nextWave });
                }
                else
                {
                    // §7.3 room-clear reward beat: doors open, a small gold burst pays out.
                    if (s.DungeonRoomClearGold > 0) s.PendingGold += s.DungeonRoomClearGold;
                    s.DungeonClearedRooms.Add(room);
                    events.Add(new CombatEvent
                    { Type = CombatEventType.RoomCleared, RoomId = room, Amount = s.DungeonRoomClearGold });
                    s.DungeonSealedRoomId = -1;
                }
            }

            // 2 — seal on entry.
            if (s.DungeonSealedRoomId < 0 && leader != null && leader.Alive)
            {
                int room = arena.RoomAt(leader.Pos);
                if (room >= 0 && !(bossLocked && room == s.DungeonBossRoomId)
                    && !s.DungeonClearedRooms.Contains(room)
                    // A pending wave alone also seals (a splash-emptied wave 0 must not strand its
                    // successors) — the clear check above then releases the next wave immediately.
                    && (RoomHasLivingEnemy(s, room) || RoomHasPendingWave(s, room)))
                {
                    s.DungeonSealedRoomId = room;
                    events.Add(new CombatEvent { Type = CombatEventType.RoomSealed, RoomId = room });
                    foreach (var e in s.Entities)
                        if (e.Team == Team.Enemy && e.Alive && e.DungeonRoomId == room) e.Aggro = true;
                }
            }

            // 3 — contain.
            int sealedRoom = s.DungeonSealedRoomId;
            foreach (var e in s.Entities)
            {
                if (!e.Alive) continue;
                if (e.Team == Team.Party)
                {
                    if (sealedRoom >= 0) e.Pos = arena.ClampToRoom(e.Pos, sealedRoom);
                    else if (bossLocked && arena.RoomAt(e.Pos) == s.DungeonBossRoomId)
                        e.Pos = arena.ClampOutsideRoom(e.Pos, s.DungeonBossRoomId);
                }
                else if (sealedRoom >= 0 && e.DungeonRoomId == sealedRoom)
                    e.Pos = arena.ClampToRoom(e.Pos, sealedRoom);
            }
        }

        /// <summary>
        /// The current room-sweep objective: among rooms that still hold a LIVING enemy, the one whose
        /// centre is SHALLOWEST from the entrance (smallest entrance-BFS depth), ties broken by lower room
        /// id. Each living enemy is attributed to its SPAWN room (DungeonRoomId — so a mob that chased into
        /// a corridor still counts toward its home room, not a moving cell lookup). Returns null when no
        /// living enemy resolves a valid room (e.g. all cleared). Deterministic — a pure function of the
        /// entity set + immutable dungeon; cheap (≤ a couple hundred entities).
        /// </summary>
        private static int? DungeonObjectiveRoom(CombatState s)
        {
            var dungeon = s.Dungeon;
            if (dungeon == null) return null;
            int? best = null;
            short bestDepth = short.MaxValue;
            void Consider(int room)
            {
                if (room < 0) return; // no home room recorded — can't route to it
                short depth = dungeon.RoomEntranceDepth(room);
                if (best == null || depth < bestDepth || (depth == bestDepth && room < best.Value))
                {
                    bestDepth = depth;
                    best = room;
                }
            }
            foreach (var e in s.Entities)
                if (e.Team == Team.Enemy && e.Alive) Consider(e.DungeonRoomId);
            // §7.3 wave phases: a room whose spawned mobs all died but whose later waves still WAIT
            // is unfinished business — the sweep must (re)enter it so the seal can release them.
            foreach (var p in s.DungeonPendingWaves) Consider(p.DungeonRoomId);
            return best;
        }

        /// <summary>The living enemy of <paramref name="roomId"/> (by spawn-stamped DungeonRoomId) nearest
        /// <paramref name="self"/> — the concrete pack cell the sweep routes its flow field onto. Stable Id
        /// tie-break; null if the room has no living enemy.</summary>
        private static CombatEntity? NearestEnemyOfRoom(CombatState s, CombatEntity self, int roomId)
        {
            CombatEntity? best = null;
            double bestD = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team == self.Team || o.DungeonRoomId != roomId) continue;
                double d = Vec2.Distance(self.Pos, o.Pos);
                if (d < bestD || (d == bestD && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                { bestD = d; best = o; }
            }
            return best;
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
                if (s.Dungeon != null && !s.Dungeon.GateTargets(centre, o.Pos)) continue; // room-gate
                double d = Vec2.Distance(centre, o.Pos);
                if (d < bestDist || (d == bestDist && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                {
                    bestDist = d;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The nearest enemy within <paramref name="radius"/> of <paramref name="self"/>,
        /// or null if none is that close (Solo party engages individually within range). Two optional
        /// preference buckets REORDER the in-radius candidates (never widen them): a candidate is
        /// "preferred" when EITHER its <c>TargetId</c> ∈ <paramref name="preferTargetingIds"/> (peel:
        /// an enemy attacking a ranged ally) OR its OWN <c>Id</c> ∈ <paramref name="preferIds"/>
        /// (assist: an enemy a melee ally is already on). Among the SAME candidate set, the nearest
        /// preferred one wins; otherwise it falls back to the plain nearest. Deterministic Id
        /// tie-breaks are identical in both buckets.</summary>
        private static CombatEntity? FindNearestEnemyWithin(CombatState s, CombatEntity self, double radius,
                                                            HashSet<string>? preferTargetingIds = null,
                                                            HashSet<string>? preferIds = null)
        {
            CombatEntity? best = null;      double bestDist = double.MaxValue;
            CombatEntity? bestDef = null;   double bestDefDist = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team == self.Team) continue;
                if (s.Dungeon != null && !s.Dungeon.GateTargets(self.Pos, o.Pos)) continue; // room-gate
                double d = Vec2.Distance(self.Pos, o.Pos);
                if (d > radius) continue;
                if (d < bestDist || (d == bestDist && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                { bestDist = d; best = o; }
                bool preferred = (preferTargetingIds != null && o.TargetId != null && preferTargetingIds.Contains(o.TargetId))
                                 || (preferIds != null && preferIds.Contains(o.Id));
                if (preferred
                    && (d < bestDefDist || (d == bestDefDist && bestDef != null && string.CompareOrdinal(o.Id, bestDef.Id) < 0)))
                { bestDefDist = d; bestDef = o; }
            }
            return bestDef ?? best;
        }

        /// <summary>The enemy nearest a world point but no farther than <paramref name="radius"/>,
        /// or null if none qualifies. Used to leash a follower's combat to its formation slot. The two
        /// preference buckets work exactly as in <see cref="FindNearestEnemyWithin"/>:
        /// <paramref name="preferTargetingIds"/> = peel (a melee follower toward an enemy attacking a
        /// ranged ally), <paramref name="preferIds"/> = assist (a ranged follower onto an enemy a
        /// melee ally is on). Preference only REORDERS the in-radius candidates, never widens them.</summary>
        private static CombatEntity? FindNearestEnemyNear(CombatState s, Vec2 point, double radius,
                                                          HashSet<string>? preferTargetingIds = null,
                                                          HashSet<string>? preferIds = null)
        {
            CombatEntity? best = null;      double bestDist = double.MaxValue;
            CombatEntity? bestDef = null;   double bestDefDist = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team != Team.Enemy) continue;
                if (s.Dungeon != null && !s.Dungeon.GateTargets(point, o.Pos)) continue; // room-gate (seeker = slot)
                double d = Vec2.Distance(point, o.Pos);
                if (d > radius) continue;
                if (d < bestDist || (d == bestDist && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                { bestDist = d; best = o; }
                bool preferred = (preferTargetingIds != null && o.TargetId != null && preferTargetingIds.Contains(o.TargetId))
                                 || (preferIds != null && preferIds.Contains(o.Id));
                if (preferred
                    && (d < bestDefDist || (d == bestDefDist && bestDef != null && string.CompareOrdinal(o.Id, bestDef.Id) < 0)))
                { bestDefDist = d; bestDef = o; }
            }
            return bestDef ?? best;
        }

        /// <summary>Nearest living enemy already within <paramref name="self"/>'s OWN attack reach
        /// (AttackRange stat + target BodyRadius, the same reach the in-range check uses). Used for
        /// ranged fire-in-transit: shoot what's on top of you mid-regroup without chasing it. Mirrors
        /// the acquisition helpers — no aggro filter (idle trash is a valid target once in reach).
        /// <paramref name="preferIds"/> = assist: among the SAME in-reach candidates, an enemy whose
        /// OWN Id ∈ the set (one a melee ally is on) is preferred; else falls back to plain nearest.
        /// Preference only REORDERS candidates within reach, never widens it; same Id tie-breaks.</summary>
        private static CombatEntity? NearestEnemyInReach(CombatState s, CombatEntity self,
                                                         HashSet<string>? preferIds = null)
        {
            double baseRange = self.Stats.Get(StatKey.AttackRange);
            if (baseRange <= 0) baseRange = MeleeRange;
            CombatEntity? best = null;      double bestDist = double.MaxValue;
            CombatEntity? bestPref = null;  double bestPrefDist = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team == self.Team) continue;
                if (s.Dungeon != null && !s.Dungeon.GateTargets(self.Pos, o.Pos)) continue; // room-gate
                double d = Vec2.Distance(self.Pos, o.Pos);
                if (d > baseRange + o.BodyRadius) continue; // must be genuinely in reach
                if (d < bestDist || (d == bestDist && best != null && string.CompareOrdinal(o.Id, best.Id) < 0))
                { bestDist = d; best = o; }
                if (preferIds != null && preferIds.Contains(o.Id)
                    && (d < bestPrefDist || (d == bestPrefDist && bestPref != null && string.CompareOrdinal(o.Id, bestPref.Id) < 0)))
                { bestPrefDist = d; bestPref = o; }
            }
            return bestPref ?? best;
        }

        /// <summary>Nearest living AGGRO'D enemy within <paramref name="radius"/> of <paramref
        /// name="self"/>'s position, or null. Drives the panic micro-kite: idle (non-aggro) trash
        /// standing nearby is no threat, so it doesn't trigger a backpedal.</summary>
        private static CombatEntity? NearestAggroEnemyWithin(CombatState s, CombatEntity self, double radius)
        {
            CombatEntity? best = null;
            double bestDist = double.MaxValue;
            foreach (var o in s.Entities)
            {
                if (!o.Alive || o.Team == self.Team || !o.Aggro) continue;
                if (s.Dungeon != null && !s.Dungeon.GateTargets(self.Pos, o.Pos)) continue; // room-gate
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

        private static CombatEntity? FindNearestEnemy(CombatState s, CombatEntity self, GameConfig cfg)
        {
            // Tank aggro bias applies ONLY on the monster acquisition path: a monster counts a
            // MELEE hero (RangedRole == false) as TankAggroBias tiles closer than it really is, so
            // tanks soak attention. Party heroes picking enemies use raw distance — the bias cannot
            // leak hero-side because it's gated on self being an enemy. Nearest-by-effective-distance
            // wins; Id tie-break for determinism (matches the newer acquisition helpers).
            bool monster = self.Team == Team.Enemy;
            double bias = monster ? cfg.Balance.TankAggroBias : 0.0;

            CombatEntity? best = null;
            double bestDist = double.MaxValue;
            foreach (var other in s.Entities)
            {
                if (!other.Alive || other.Team == self.Team) continue;
                if (s.Dungeon != null && !s.Dungeon.GateTargets(self.Pos, other.Pos)) continue; // room-gate (symmetric)
                double d = Vec2.Distance(self.Pos, other.Pos);
                if (monster && !other.RangedRole) d -= bias; // melee heroes read closer
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
        private static void ResolveCollisions(CombatState s, GameConfig cfg, IArenaSurface? arena = null,
                                              string? immovableId = null)
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
                        // Immovable body (dungeon sweep leader): the OTHER body absorbs the full overlap,
                        // so the leader is never shoved off its flow-field step by its own followers.
                        if (immovableId != null)
                        {
                            if (a.Id == immovableId) { aShare = 0.0; bShare = 1.0; }
                            else if (b.Id == immovableId) { aShare = 1.0; bShare = 0.0; }
                        }
                        // Pushed bodies stay on the walkable region (arena) or in the field (rect).
                        // Clamp short-circuits on Contains, so the arena branch stays cheap.
                        var aPush = new Vec2(a.Pos.X - nx * overlap * aShare, a.Pos.Y - ny * overlap * aShare);
                        var bPush = new Vec2(b.Pos.X + nx * overlap * bShare, b.Pos.Y + ny * overlap * bShare);
                        a.Pos = arena != null ? arena.Clamp(aPush)
                                              : new Vec2(Math.Clamp(aPush.X, -w, w), Math.Clamp(aPush.Y, -d, d));
                        b.Pos = arena != null ? arena.Clamp(bPush)
                                              : new Vec2(Math.Clamp(bPush.X, -w, w), Math.Clamp(bPush.Y, -d, d));
                    }
                }
            }
        }

        /// <summary>
        /// A melee attacker's personal contact point on the target's rim — the spot it walks to
        /// instead of the target's centre, so a crowd fans into a ring rather than piling on one
        /// point. Angle is STABLE per attacker (its Id hashed into one of 16 slots) and identical
        /// across runs/platforms (explicit FNV-1a, never GetHashCode). The offset radius
        /// (target.BodyRadius + own.BodyRadius*0.5) lands the attacker comfortably inside its reach
        /// (reach = range + target.BodyRadius, range ≥ MeleeRange = 1.0): e.g. two 0.45-radius
        /// bodies give offset 0.675 vs reach ≥ 1.45, so arrival is always within striking range.
        /// </summary>
        private static Vec2 MeleeContactPoint(CombatEntity e, CombatEntity target)
        {
            int slot = (int)(Fnv1a(e.Id) % 16);
            double theta = slot * (2.0 * Math.PI / 16.0);
            double r = target.BodyRadius + e.BodyRadius * 0.5;
            return new Vec2(target.Pos.X + Math.Cos(theta) * r,
                            target.Pos.Y + Math.Sin(theta) * r);
        }

        /// <summary>Explicit FNV-1a hash of a string's chars — deterministic across runtimes/
        /// platforms (unlike string.GetHashCode, which is randomized). Used to pick a stable
        /// surround-ring slot per attacker.</summary>
        private static uint Fnv1a(string sId)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < sId.Length; i++)
            {
                hash ^= sId[i];
                hash *= 16777619u;
            }
            return hash;
        }

        /// <summary>
        /// Step an entity toward <paramref name="dest"/> by at most <paramref name="maxStep"/>, with
        /// COLLIDE-AND-SLIDE against the arena. The candidate position is computed exactly as the
        /// legacy path; if there's no arena or the candidate is walkable it's accepted (bit-identical
        /// to pre-arena behavior). Otherwise the two axis-decomposed candidates are tried — the axis
        /// with the LARGER |delta| first (tie → X) — and the first walkable one is accepted, so units
        /// slide along a shore/wall around a shallow bay instead of jamming; if neither is walkable the
        /// entity holds position. There is no pathfinding — this only rounds convex boundaries.
        /// </summary>
        private static void MoveToward(CombatEntity e, Vec2 dest, double maxStep, IArenaSurface? arena = null)
        {
            double dx = dest.X - e.Pos.X;
            double dy = dest.Y - e.Pos.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            Vec2 candidate = (dist <= maxStep || dist == 0)
                ? new Vec2(dest.X, dest.Y)
                : new Vec2(e.Pos.X + dx / dist * maxStep, e.Pos.Y + dy / dist * maxStep);

            if (arena == null || arena.Contains(candidate)) { e.Pos = candidate; return; }

            // Blocked: slide along the boundary by keeping one axis of the move. Try the dominant
            // axis first so motion stays as close to the intended direction as possible.
            var slideX = new Vec2(candidate.X, e.Pos.Y);
            var slideY = new Vec2(e.Pos.X, candidate.Y);
            bool xFirst = Math.Abs(candidate.X - e.Pos.X) >= Math.Abs(candidate.Y - e.Pos.Y);
            var first = xFirst ? slideX : slideY;
            var second = xFirst ? slideY : slideX;
            if (arena.Contains(first)) e.Pos = first;
            else if (arena.Contains(second)) e.Pos = second;
            // neither axis walkable: stay put (no pathfinding).
        }

        /// <summary>Idle amble: stroll toward a random point within the field, repicking when
        /// reached or after a random interval. Deterministic (rng-driven). Clamped to the map
        /// so wanderers never drift out of bounds.</summary>
        private static void Wander(CombatEntity e, GameConfig cfg, double dtMs, Rng rng, IArenaSurface? arena = null,
                                   DungeonArena? dungeon = null)
        {
            // Repick a fresh destination only when the timer elapses (gated purely by the
            // staggered cooldown, so a batch of spawns doesn't all turn on the same frame).
            e.WanderCdMs -= dtMs;
            if (e.WanderCdMs <= 0)
            {
                double w = cfg.Balance.MapHalfWidth - 0.5, d = cfg.Balance.MapHalfDepth - 0.5, r = cfg.Balance.WanderRadius;
                // random local step; if it would leave the field, reflect inward instead of
                // clamping to the edge (clamping makes mobs pile along / trace the rectangle).
                // The rng DRAW ORDER is identical with or without an arena — the arena only projects
                // the already-reflected target onto the walkable region afterward.
                double ox = rng.RandRange(-r, r), oy = rng.RandRange(-r, r);
                double tx = e.Pos.X + ox; if (tx < -w || tx > w) tx = e.Pos.X - ox;
                double ty = e.Pos.Y + oy; if (ty < -d || ty > d) ty = e.Pos.Y - oy;
                var picked = new Vec2(Math.Clamp(tx, -w, w), Math.Clamp(ty, -d, d));
                var target = arena != null ? arena.Clamp(picked) : picked;
                // Dungeon: keep wanderers ROOM-BOUND — a target that clamps into a different room (or a
                // corridor) is rejected in favour of holding position, so idle packs don't drift out of
                // their room and pile into halls. Same rng draws as above, so determinism is unaffected.
                if (dungeon != null && dungeon.RoomAt(target) != dungeon.RoomAt(e.Pos))
                    target = e.Pos;
                e.WanderTarget = target;
                e.WanderCdMs = rng.RandRange(cfg.Balance.WanderMinMs, cfg.Balance.WanderMaxMs);
            }
            double speed = e.EffectiveStat(StatKey.MoveSpd);
            if (speed <= 0) speed = MoveSpeedTilesPerSec;
            MoveToward(e, e.WanderTarget, speed * cfg.Balance.WanderSpeedMult * dtMs / 1000.0, arena);
        }
    }
}
