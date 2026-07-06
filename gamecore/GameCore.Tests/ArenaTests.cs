using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Arena layouts (ROADMAP 8, slice 1): per-stage walkable regions (union of rect/disc shapes)
    // with cosmetic height tiers, plus the collide-and-slide movement clamp. These pin the geometry
    // (Contains/Nearest/Clamp/TierAt), the slide behavior, the combat integration (spawns + party
    // stay walkable across farm/tower/boss), determinism, the null-arena open-plane regression, and
    // the GameConfig.Default() content contract.
    public class ArenaTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static ArenaShape Rect(double x, double y, double hw, double hd, int tier = 0)
            => new ArenaShape { Kind = ArenaShapeKind.Rect, X = x, Y = y, HalfW = hw, HalfD = hd, Tier = tier };
        private static ArenaShape Disc(double x, double y, double r, int tier = 0)
            => new ArenaShape { Kind = ArenaShapeKind.Disc, X = x, Y = y, HalfW = r, Tier = tier };

        // ---------------- Shape geometry ----------------

        [Fact]
        public void RectContainsInsideOutsideEdge()
        {
            var r = Rect(0, 0, 4, 2);
            Assert.True(r.Contains(new Vec2(0, 0)));    // centre
            Assert.True(r.Contains(new Vec2(4, 2)));    // corner (edge inclusive)
            Assert.True(r.Contains(new Vec2(-4, 0)));   // edge
            Assert.False(r.Contains(new Vec2(4.01, 0)));
            Assert.False(r.Contains(new Vec2(0, 2.01)));
        }

        [Fact]
        public void DiscContainsInsideOutsideEdge()
        {
            var d = Disc(1, 1, 3);
            Assert.True(d.Contains(new Vec2(1, 1)));    // centre
            Assert.True(d.Contains(new Vec2(4, 1)));    // on the rim
            Assert.False(d.Contains(new Vec2(4.001, 1)));
            Assert.False(d.Contains(new Vec2(1 + 2.13, 1 + 2.13))); // ~3.01 out at 45°
        }

        [Fact]
        public void RectNearestClampsPerAxisAndKeepsContainedPoint()
        {
            var r = Rect(0, 0, 4, 2);
            Assert.Equal(new Vec2(1, 1), r.Nearest(new Vec2(1, 1)));   // contained => itself
            var side = r.Nearest(new Vec2(10, 0));                     // clamp X
            Assert.Equal(4, side.X, 9); Assert.Equal(0, side.Y, 9);
            var corner = r.Nearest(new Vec2(10, 10));                  // clamp to corner
            Assert.Equal(4, corner.X, 9); Assert.Equal(2, corner.Y, 9);
        }

        [Fact]
        public void DiscNearestProjectsRadiallyAndKeepsContainedPoint()
        {
            // Projections land a hair INSIDE the rim (RimInset 1e-9) so Contains always accepts them.
            var d = Disc(0, 0, 5);
            Assert.Equal(new Vec2(1, 1), d.Nearest(new Vec2(1, 1)));   // contained => itself
            var q = d.Nearest(new Vec2(10, 0));
            Assert.Equal(5, q.X, 6); Assert.Equal(0, q.Y, 9);
            Assert.True(d.Contains(q));
            var diag = d.Nearest(new Vec2(10, 10));                    // radial at 45°
            double dist = Vec2.Distance(new Vec2(0, 0), diag);
            Assert.True(dist <= 5.0 && dist > 5.0 - 1e-6, $"diag dist {dist:R} not just inside the rim");
            Assert.Equal(diag.X, diag.Y, 9);
            Assert.True(d.Contains(diag));
        }

        [Fact]
        public void DiscNearestDegenerateCentreIsDeterministic()
        {
            // Only reachable for a ~0 radius (p==centre is contained for any positive radius).
            var d = Disc(3, 7, 0);
            var q = d.Nearest(new Vec2(3, 7));
            Assert.Equal(new Vec2(3, 7), q); // radius 0 => centre + (0,0)
        }

        [Fact]
        public void ClampedPointsAreAlwaysContained()
        {
            // The exact invariant a live probe caught: a disc's radial projection could round to a
            // point Contains rejects by ~1e-15 (entities parked epsilon-off-shore). Nearest now aims
            // a hair inside the rim, so Contains(Clamp(p)) holds EXACTLY for any outside point.
            var a = new ArenaLayout
            {
                Id = "t",
                Shapes = { Disc(0, 0, 33), Rect(-30, -30, 12, 8), Disc(20.5, 16.25, 9.1) }
            };
            for (int k = 0; k < 96; k++)
            {
                double th = k * (System.Math.PI * 2 / 96);
                for (double r = 33.5; r <= 80; r += 7.3)
                {
                    var p = new Vec2(System.Math.Cos(th) * r, System.Math.Sin(th) * r);
                    if (a.Contains(p)) continue;
                    var q = a.Clamp(p);
                    Assert.True(a.Contains(q), $"Clamp({p.X:F2},{p.Y:F2}) => ({q.X:R},{q.Y:R}) not contained");
                }
            }
        }

        // ---------------- Layout: Clamp / TierAt / Contains ----------------

        [Fact]
        public void LayoutClampReturnsContainedPointUnchanged()
        {
            var a = new ArenaLayout { Id = "t", Shapes = { Rect(0, 0, 10, 10), Disc(20, 0, 5) } };
            Assert.Equal(new Vec2(3, -4), a.Clamp(new Vec2(3, -4)));
        }

        [Fact]
        public void LayoutClampPicksNearestAcrossShapes()
        {
            var a = new ArenaLayout { Id = "t", Shapes = { Rect(0, 0, 5, 5), Disc(30, 0, 5) } };
            // A point just right of the rect clamps to the rect edge, not the far disc.
            var q = a.Clamp(new Vec2(8, 0));
            Assert.Equal(5, q.X, 9); Assert.Equal(0, q.Y, 9);
        }

        [Fact]
        public void LayoutClampEquidistantTieFavorsLowestIndex()
        {
            // Two identical rects mirrored about x=0; a point on the axis is equidistant to both.
            var a = new ArenaLayout { Id = "t", Shapes = { Rect(-10, 0, 4, 4), Rect(10, 0, 4, 4) } };
            var q = a.Clamp(new Vec2(0, 0)); // 6 to each rect's inner edge
            // Lowest index (the left rect at x=-10, inner edge x=-6) wins the strict-< compare.
            Assert.Equal(-6, q.X, 9);
            Assert.Equal(0, q.Y, 9);
        }

        [Fact]
        public void TierAtIsMaxOverContainersAndZeroOutside()
        {
            var a = new ArenaLayout
            {
                Id = "t",
                Shapes = { Rect(0, 0, 10, 10, 0), Rect(5, 0, 4, 4, 1), Rect(5, 0, 2, 2, 2) }
            };
            Assert.Equal(2, a.TierAt(new Vec2(5, 0)));   // inside all three => max tier
            Assert.Equal(1, a.TierAt(new Vec2(8, 0)));   // inside base + tier-1
            Assert.Equal(0, a.TierAt(new Vec2(-8, 0)));  // base only
            Assert.Equal(0, a.TierAt(new Vec2(100, 0))); // outside union => 0
        }

        [Fact]
        public void LayoutContainsIsUnion()
        {
            var a = new ArenaLayout { Id = "t", Shapes = { Rect(0, 0, 5, 5), Disc(20, 0, 5) } };
            Assert.True(a.Contains(new Vec2(0, 0)));
            Assert.True(a.Contains(new Vec2(20, 0)));
            Assert.False(a.Contains(new Vec2(12, 0))); // in the gap between the two shapes
        }

        // ---------------- Slide movement ----------------

        // An L-shaped arena: a horizontal arm + a vertical arm sharing the corner near origin.
        private static ArenaLayout LArena() => new ArenaLayout
        {
            Id = "L",
            Shapes =
            {
                Rect(0, 0, 10, 2, 0),   // horizontal arm along +/-x
                Rect(-8, 8, 2, 8, 0),   // vertical arm rising from the left, tier 0
            }
        };

        private static CombatEntity Walker(double x, double y) => new CombatEntity
        {
            Id = "W", Team = Team.Party, Pos = new Vec2(x, y), BodyRadius = 0,
            Stats = new StatBlock { [StatKey.MoveSpd] = 3.0 },
        };

        [Fact]
        public void SlideReachesTargetAroundTheCornerStayingWalkable()
        {
            var arena = LArena();
            var e = Walker(8, 0);                 // far end of the horizontal arm
            var target = new Vec2(-8, 14);        // top of the vertical arm
            Assert.True(arena.Contains(e.Pos));

            int steps = 0;
            const int cap = 400;
            while (Vec2.Distance(e.Pos, target) > 0.3 && steps < cap)
            {
                Combat_MoveToward(e, target, 0.2, arena);
                Assert.True(arena.Contains(e.Pos), $"left walkable region at {e.Pos.X},{e.Pos.Y}");
                steps++;
            }
            Assert.True(steps < cap, "walker never reached the target around the corner");
        }

        [Fact]
        public void SlideAlongWallMovesButStaysWalkable()
        {
            var arena = new ArenaLayout { Id = "wall", Shapes = { Rect(0, 0, 10, 4, 0) } };
            var e = Walker(0, 3.5);               // near the top wall
            var before = e.Pos;
            // Aim up-and-right into the wall: the Y move is blocked, the X slide is taken.
            Combat_MoveToward(e, new Vec2(9, 20), 1.0, arena);
            Assert.True(arena.Contains(e.Pos));
            Assert.True(e.Pos.X > before.X, "did not slide along the wall");
            Assert.True(e.Pos.Y <= 4.0 + 1e-9, "climbed past the wall");
        }

        // Reflection shim onto the private Combat.MoveToward(e, dest, maxStep, arena).
        private static void Combat_MoveToward(CombatEntity e, Vec2 dest, double maxStep, ArenaLayout arena)
        {
            var mi = typeof(Combat).GetMethod("MoveToward",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            mi!.Invoke(null, new object?[] { e, dest, maxStep, arena });
        }

        // ---------------- Farm integration ----------------

        private static HeroInstance[] Party() => new[]
        {
            new HeroInstance { Id = "h1", DefId = "warrior_basic",  Level = 5 },
            new HeroInstance { Id = "h2", DefId = "magician_basic", Level = 5 },
            new HeroInstance { Id = "h3", DefId = "thief_basic",    Level = 5 },
        };

        [Fact]
        public void FarmKeepsAllEntitiesAndSpawnsInsideTheArena()
        {
            var arena = Cfg.ArenaForStage(1)!;   // verdant woods
            var s = Combat.InitFarm(Party(), 1, Cfg, new Rng(7));
            Assert.Equal("arena_verdant_woods", s.ArenaId);

            // initial spawn pack is walkable
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy))
                Assert.True(arena.Contains(e.Pos), $"spawned enemy off-arena at {e.Pos.X},{e.Pos.Y}");

            for (int i = 0; i < 400; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(7));

            foreach (var e in s.Entities)
                Assert.True(arena.Contains(e.Pos), $"{e.Id} ({e.Team}) drifted off-arena at {e.Pos.X},{e.Pos.Y}");
        }

        [Fact]
        public void FormationHomeDegradesToWalkableSoFollowerSettles()
        {
            // Tiny arena so a ranged follower's slot behind the leader projects off the walkable
            // region — the clamp must let it settle at the nearest walkable point (no run-in-place).
            var cfg = GameConfig.Default();
            var arena = new ArenaLayout { Id = "small", Shapes = { Disc(0, 0, 4, 0) } };
            cfg.Arenas["small"] = arena;

            var s = Combat.InitFarm(Party(), 1, cfg, new Rng(1));
            s.ArenaId = "small";
            s.Tactic = PartyTactic.Solo;
            s.Entities.RemoveAll(e => e.Team == Team.Enemy);
            s.SpawnTimerMs = double.MaxValue; // freeze spawns
            foreach (var e in s.Entities) e.Pos = arena.Clamp(e.Pos);

            // Let it settle, then confirm positions stabilize AND stay walkable.
            for (int i = 0; i < 200; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            var snap = s.Entities.Where(e => e.Team == Team.Party).ToDictionary(e => e.Id, e => e.Pos);
            for (int i = 0; i < 60; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));

            foreach (var e in s.Entities.Where(e => e.Team == Team.Party))
            {
                Assert.True(arena.Contains(e.Pos), $"{e.Id} off-arena at {e.Pos.X},{e.Pos.Y}");
                Assert.True(Vec2.Distance(snap[e.Id], e.Pos) < 0.05,
                    $"{e.Id} still moving (delta {Vec2.Distance(snap[e.Id], e.Pos):F3}) — not settled");
            }
        }

        // ---------------- Tower / boss transitions ----------------

        [Fact]
        public void InitTowerPlacesEveryoneOnTheFloorsArena()
        {
            // Fresh-init (mode isolation, 2026-07-06): a tower floor builds its own state, so the
            // old "stranded party clamped in" scenario can't occur — the invariant is simply that
            // everything the init places (party cluster + pack + guardian) is on the floor's arena.
            var s = Combat.InitTower(Party(), 11, Cfg, new Rng(3)); // zone 2 arena
            var arena = Cfg.ArenaForStage(11)!;
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party))
                Assert.True(arena.Contains(e.Pos), $"party off-arena: {e.Pos.X},{e.Pos.Y}");
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy))
                Assert.True(arena.Contains(e.Pos), $"tower spawn off-arena: {e.Pos.X},{e.Pos.Y}");
        }

        [Fact]
        public void EnterBossChallengeSpawnsBossOnTheArena()
        {
            var s = Combat.InitFarm(Party(), 1, Cfg, new Rng(5));
            // Push the party to the map edge so the +BossSpawnDistance offset would land off-arena.
            foreach (var e in s.Entities.Where(e => e.Team == Team.Party)) e.Pos = new Vec2(39, 0);

            Combat.EnterBossChallenge(s, Cfg);
            var arena = Cfg.ArenaForStage(1)!;
            var boss = s.Entities.Single(e => e.IsBoss);
            Assert.True(arena.Contains(boss.Pos), $"boss spawned off-arena at {boss.Pos.X},{boss.Pos.Y}");
        }

        // ---------------- Dash ----------------

        [Fact]
        public void DashLandingIsClampedInside()
        {
            // Assassin (thief) has a Dash active; place a target across a boundary so the naive
            // landing would sit off-arena, and confirm the clamp keeps the dasher walkable.
            var cfg = GameConfig.Default();
            var arena = new ArenaLayout { Id = "strip", Shapes = { Rect(0, 0, 8, 2, 0) } };
            cfg.Arenas["strip"] = arena;

            var s = Combat.InitFarm(
                new[] { new HeroInstance { Id = "a1", DefId = "thief_basic", Level = 10 } }, 1, cfg, new Rng(1));
            s.ArenaId = "strip";
            s.Entities.RemoveAll(e => e.Team == Team.Enemy);
            s.SpawnTimerMs = double.MaxValue;

            var hero = s.Entities.First(e => e.Team == Team.Party);
            hero.Pos = new Vec2(-6, 0);
            // A target off the walkable strip (high Y) within dash range — the dasher would leap up
            // toward it, which the arena must clamp back onto the strip.
            s.Entities.Add(new CombatEntity
            {
                Id = "Z", Team = Team.Enemy, Pos = new Vec2(-3, 6), BodyRadius = 0.45,
                Stats = new StatBlock { [StatKey.Hp] = 500, [StatKey.Atk] = 1, [StatKey.Def] = 0 },
                Hp = 500, MaxHp = 500, RefKind = "test", RefId = "Z",
            });

            for (int i = 0; i < 60; i++) Combat.StepCombat(s, Combat.DefaultStepMs, cfg, new Rng(1));
            Assert.True(arena.Contains(hero.Pos), $"dasher landed off-arena at {hero.Pos.X},{hero.Pos.Y}");
        }

        // ---------------- Determinism ----------------

        [Fact]
        public void SameSeedAndArenaProduceIdenticalPositions()
        {
            List<(string, double, double)> Run()
            {
                var s = Combat.InitFarm(Party(), 1, Cfg, new Rng(42));
                for (int i = 0; i < 300; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(42));
                return s.Entities.OrderBy(e => e.Id, System.StringComparer.Ordinal)
                                 .Select(e => (e.Id, e.Pos.X, e.Pos.Y)).ToList();
            }
            Assert.Equal(Run(), Run());
        }

        // ---------------- Null arena = open plane regression ----------------

        [Fact]
        public void NullArenaLeavesMovementOnTheOpenPlane()
        {
            // A hand-built CombatState has ArenaId == null: entities roam the open plane, unaffected
            // by any walkable clamp. Compare against a small reference region the hero clearly exits.
            var region = new ArenaLayout { Id = "ref", Shapes = { Disc(0, 0, 3, 0) } };
            var e = new CombatEntity
            {
                Id = "P", Team = Team.Party, Pos = new Vec2(0, 0), BodyRadius = 0,
                Stats = new StatBlock { [StatKey.Hp] = 100, [StatKey.Atk] = 1, [StatKey.MoveSpd] = 3.0 },
                Hp = 100, MaxHp = 100, RefKind = "test", RefId = "P",
            };
            var far = new CombatEntity
            {
                Id = "Q", Team = Team.Enemy, Pos = new Vec2(120, 60), BodyRadius = 0,
                Stats = new StatBlock { [StatKey.Hp] = 100, [StatKey.Atk] = 1, [StatKey.MoveSpd] = 3.0 },
                Hp = 100, MaxHp = 100, RefKind = "test", RefId = "Q",
            };
            var s = new CombatState();
            s.Entities.Add(e);
            s.Entities.Add(far);

            Assert.Null(s.ArenaId);
            for (int i = 0; i < 200; i++) Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(1));
            // The party hero chases the far enemy well past the r=3 reference — proof the clamp is off.
            Assert.False(region.Contains(e.Pos), "open-plane hero did not move freely");
        }

        // ---------------- Content: GameConfig.Default() ----------------

        [Fact]
        public void EveryZoneArenaIdResolvesIntoArenas()
        {
            for (int i = 0; i < Cfg.Zones.Count; i++)
            {
                var z = Cfg.Zones[i];
                Assert.False(string.IsNullOrEmpty(z.ArenaId), $"zone {z.Id} has no ArenaId");
                Assert.True(Cfg.Arenas.ContainsKey(z.ArenaId), $"zone {z.Id} arena {z.ArenaId} missing");
                // A stage in this zone's band resolves to the same layout the zone names.
                int stage = i * 10 + 1;
                Assert.Same(Cfg.Zones[i], Cfg.ZoneForStage(stage));
                Assert.Same(Cfg.Arenas[z.ArenaId], Cfg.ArenaForStage(stage));
            }
        }

        [Fact]
        public void EveryLayoutCoversOriginAndPartyStartCluster()
        {
            var pts = new[]
            {
                new Vec2(0, 0),
                new Vec2(0.7, 0.7), new Vec2(-0.7, 0.7),
                new Vec2(0.7, -0.7), new Vec2(-0.7, -0.7),
            };
            foreach (var a in Cfg.Arenas.Values)
                foreach (var p in pts)
                    Assert.True(a.Contains(p), $"arena {a.Id} misses party point {p.X},{p.Y}");
        }

        [Fact]
        public void EveryLayoutCoversTheSpawnBubble()
        {
            // The tier-0 base must cover the r≈32 engagement bubble (spawn ring 26 + wander 5 + pad).
            // Sample a fine ring/grid inside r=30 and require every point walkable.
            foreach (var a in Cfg.Arenas.Values)
            {
                for (double r = 4; r <= 30; r += 2)
                    for (int k = 0; k < 24; k++)
                    {
                        double th = k * (System.Math.PI * 2 / 24);
                        var p = new Vec2(System.Math.Cos(th) * r, System.Math.Sin(th) * r);
                        Assert.True(a.Contains(p), $"arena {a.Id} gap at r={r} ({p.X:F1},{p.Y:F1})");
                    }
            }
        }

        [Fact]
        public void EveryRaisedShapeOverlapsALowerTierShape()
        {
            // The ramp rule: sample each Tier>0 shape's own area on a 0.5 grid and require some
            // sample to also lie in a strictly-lower-tier shape (that overlap band = the ramp).
            foreach (var a in Cfg.Arenas.Values)
            {
                foreach (var sh in a.Shapes)
                {
                    if (sh.Tier <= 0) continue;
                    bool foundRamp = false;
                    double ext = sh.Kind == ArenaShapeKind.Disc ? sh.HalfW : System.Math.Max(sh.HalfW, sh.HalfD);
                    for (double dx = -ext; dx <= ext && !foundRamp; dx += 0.5)
                        for (double dy = -ext; dy <= ext && !foundRamp; dy += 0.5)
                        {
                            var p = new Vec2(sh.X + dx, sh.Y + dy);
                            if (!sh.Contains(p)) continue;
                            foreach (var other in a.Shapes)
                                if (other.Tier < sh.Tier && other.Contains(p)) { foundRamp = true; break; }
                        }
                    Assert.True(foundRamp, $"arena {a.Id}: tier-{sh.Tier} shape at ({sh.X},{sh.Y}) has no lower-tier ramp");
                }
            }
        }

        [Fact]
        public void EveryShapeFitsInsideTheLegacyMapRect()
        {
            double w = Cfg.Balance.MapHalfWidth, d = Cfg.Balance.MapHalfDepth;
            foreach (var a in Cfg.Arenas.Values)
                foreach (var sh in a.Shapes)
                {
                    double hw = sh.Kind == ArenaShapeKind.Disc ? sh.HalfW : sh.HalfW;
                    double hd = sh.Kind == ArenaShapeKind.Disc ? sh.HalfW : sh.HalfD;
                    Assert.True(sh.X - hw >= -w && sh.X + hw <= w, $"arena {a.Id} shape exceeds map width");
                    Assert.True(sh.Y - hd >= -d && sh.Y + hd <= d, $"arena {a.Id} shape exceeds map depth");
                }
        }

        [Fact]
        public void EveryArenaHasAtMostTwelveShapes()
        {
            foreach (var a in Cfg.Arenas.Values)
                Assert.True(a.Shapes.Count <= 12, $"arena {a.Id} has {a.Shapes.Count} shapes (> 12)");
        }
    }
}
