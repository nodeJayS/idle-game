using System;
using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Combat.StepAlong — the collide-and-slide movement geometry, and the freeze it used to hide.
    //
    // Reported by the user from a live stage-24 fight: "if the characters are blocked by this ledge /
    // corner, they stop moving and freeze. only the warrior attacks." Two distinct defects produced it,
    // both of them PERMANENT rather than transient, because the geometry is stateless — a unit that
    // holds this step recomputes the identical blocked move next step, forever:
    //   (a) a blocked move that is (near) axis aligned collapses one slide candidate onto the unit's
    //       OWN position, which is trivially walkable — a "successful" slide that never moves;
    //   (b) with both slides blocked the unit simply held, which strands it in the concave mouth of
    //       the perimeter bays the zone arenas author on purpose.
    // Measured on stage 24 (murkwater swamp) over 12k steps: the magician jammed 402 times and froze
    // for up to 412 consecutive steps; after the fix the party's worst frozen run is 0.
    public class MovementJamTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static ArenaShape Rect(double x, double y, double hw, double hd, int tier = 0)
            => new ArenaShape { Kind = ArenaShapeKind.Rect, X = x, Y = y, HalfW = hw, HalfD = hd, Tier = tier };
        private static ArenaShape Disc(double x, double y, double r, int tier = 0)
            => new ArenaShape { Kind = ArenaShapeKind.Disc, X = x, Y = y, HalfW = r, Tier = tier };

        private static HeroInstance[] Party() => new[]
        {
            new HeroInstance { Id = "h1", DefId = "warrior_basic",  Level = 55 },
            new HeroInstance { Id = "h2", DefId = "magician_basic", Level = 52 },
            new HeroInstance { Id = "h3", DefId = "thief_basic",    Level = 54 },
        };

        private static bool Moved(Vec2 a, Vec2 b) => Vec2.Distance(a, b) > 1e-9;

        // ---------------- the unblocked path is untouched ----------------

        [Fact]
        public void UnobstructedStepIsUnchangedWithAndWithoutAnArena()
        {
            var arena = new ArenaLayout { Id = "big", Shapes = { Disc(0, 0, 50) } };
            var from = new Vec2(3, -4);
            var dest = new Vec2(13, -4);

            var open = Combat.StepAlong(from, dest, 2.0, null);
            var onArena = Combat.StepAlong(from, dest, 2.0, arena);
            Assert.Equal(open.X, onArena.X, 12);
            Assert.Equal(open.Y, onArena.Y, 12);
            Assert.Equal(5.0, onArena.X, 9);   // stepped exactly maxStep along +X
            Assert.Equal(-4.0, onArena.Y, 9);
        }

        [Fact]
        public void ArrivalShorterThanTheStepLandsExactlyOnTheDestination()
        {
            var arena = new ArenaLayout { Id = "big", Shapes = { Disc(0, 0, 50) } };
            var got = Combat.StepAlong(new Vec2(0, 0), new Vec2(0.25, 0), 4.0, arena);
            Assert.Equal(0.25, got.X, 9);
            Assert.Equal(0, got.Y, 9);
        }

        // ---------------- (a) the degenerate axis-aligned slide ----------------

        [Fact]
        public void AxisAlignedBlockDoesNotReportAStationarySlideAsProgress()
        {
            // Walkable = a square. Stand ON the east edge and push due EAST: the move is pure +X, so
            // the Y-slide candidate collapses onto the unit's own position. That candidate is walkable
            // (it IS where the unit stands), and accepting it was the silent freeze.
            var arena = new ArenaLayout { Id = "square", Shapes = { Rect(0, 0, 10, 10) } };
            var from = new Vec2(10, 0);

            var got = Combat.StepAlong(from, new Vec2(30, 0), 0.5, arena);

            Assert.True(Moved(from, got), "unit stood still against a wall instead of grazing along it");
            Assert.True(arena.Contains(got), $"slid off the walkable region to {got.X},{got.Y}");
        }

        [Fact]
        public void AWallGrazeStaysPinnedToTheWallRatherThanDriftingOff()
        {
            // The escape may only turn as far as it must: against a straight N-S wall the unit ends up
            // travelling ALONG it (90° off the intended heading), never through it.
            var arena = new ArenaLayout { Id = "square", Shapes = { Rect(0, 0, 10, 10) } };
            var pos = new Vec2(10, 0);
            for (int i = 0; i < 40; i++)
            {
                pos = Combat.StepAlong(pos, new Vec2(30, 0), 0.5, arena);
                Assert.True(arena.Contains(pos), $"left the arena at step {i}: {pos.X},{pos.Y}");
                Assert.True(pos.X <= 10 + 1e-9, $"walked through the wall at step {i}: x={pos.X}");
            }
        }

        // ---------------- (b) the concave pocket ----------------

        [Fact]
        public void ConcaveCornerIsEscapedInsteadOfHeld()
        {
            // An L-shaped region: the notch at (0,0) is the inside corner. A unit tucked in the armpit
            // and asked to cross the notch diagonally has its direct step AND both axis slides blocked.
            var arena = new ArenaLayout
            {
                Id = "ell",
                Shapes = { Rect(-5, -2.5, 5, 2.5), Rect(-2.5, -5, 2.5, 5) } // x[-10,0]y[-5,0] + x[-5,0]y[-10,0]
            };
            var from = new Vec2(-0.0001, -0.0001); // hard into the corner, still walkable
            Assert.True(arena.Contains(from));

            var got = Combat.StepAlong(from, new Vec2(20, 20), 0.5, arena);

            Assert.True(Moved(from, got), "held position in a concave corner (the permanent freeze)");
            Assert.True(arena.Contains(got), $"escaped off the walkable region to {got.X},{got.Y}");
        }

        [Fact]
        public void SwampBayMouthReleasesTheUnitTheLiveFightFroze()
        {
            // The exact position a stage-24 probe caught the magician frozen at for 412 steps: on the
            // core disc's rim, at the mouth of the bay between the two southern banks.
            var arena = Cfg.Arenas["arena_murkwater_swamp"];
            var frozen = new Vec2(15.01, -29.38);
            Assert.True(arena.Contains(frozen), "probe position should be walkable");

            // Aim across the bay at the far southern bank — the destination the old slide could make no
            // progress toward, because the straight line there leaves the walkable union.
            var dest = new Vec2(30, -30);
            Assert.True(arena.Contains(dest));
            var pos = frozen;
            int arrivedAt = -1;
            for (int i = 0; i < 400 && arrivedAt < 0; i++)
            {
                var next = Combat.StepAlong(pos, dest, 0.15, arena);
                Assert.True(arena.Contains(next), $"stepped off the arena at {i}: {next.X},{next.Y}");
                Assert.True(Moved(pos, next), $"froze at step {i} at {pos.X},{pos.Y}");
                pos = next;
                if (Vec2.Distance(pos, dest) <= 1e-9) arrivedAt = i;
            }
            // It doesn't merely twitch free — it rounds the bay mouth and walks the whole way there.
            Assert.True(arrivedAt >= 0, $"never reached the far bank; stalled at {pos.X:F2},{pos.Y:F2}");
        }

        // ---------------- off-surface recovery ----------------

        [Fact]
        public void AUnitStandingOffTheSurfaceIsProjectedBackOn()
        {
            var arena = new ArenaLayout { Id = "disc", Shapes = { Disc(0, 0, 10) } };
            var stranded = new Vec2(40, 0);          // nowhere near walkable
            Assert.False(arena.Contains(stranded));

            var got = Combat.StepAlong(stranded, new Vec2(45, 0), 0.5, arena); // pushing further out
            Assert.True(arena.Contains(got), $"stayed stranded at {got.X},{got.Y}");
        }

        // ---------------- determinism ----------------

        [Fact]
        public void EscapeIsDeterministic()
        {
            var arena = Cfg.Arenas["arena_murkwater_swamp"];
            var from = new Vec2(15.01, -29.38);
            var dest = new Vec2(30, -30);
            var a = Combat.StepAlong(from, dest, 0.15, arena);
            for (int i = 0; i < 25; i++)
            {
                var b = Combat.StepAlong(from, dest, 0.15, arena);
                Assert.Equal(a.X, b.X, 15);
                Assert.Equal(a.Y, b.Y, 15);
            }
        }

        // ---------------- the whole fight, on the user's stage ----------------

        [Fact]
        public void NoPartyMemberFreezesThroughALongStage24Fight()
        {
            // The end-to-end guard: stage 24 is the murkwater swamp, the arena whose bay produced the
            // report. A hero may legitimately stand still (fighting in range, or parked at its
            // formation slot) — what must never happen again is standing still with NO target while
            // enemies are alive and unreached, which is what "they stop moving and freeze" looked like.
            var rng = new Rng(12345);
            var s = Combat.InitFarm(Party(), 24, Cfg, rng);
            Assert.Equal("arena_murkwater_swamp", s.ArenaId);
            var arena = Cfg.Arenas["arena_murkwater_swamp"];

            var last = new Dictionary<string, Vec2>();
            var run = new Dictionary<string, int>();
            const int limit = 240; // ~8s of wall clock at the fixed step — far past any legitimate pause

            for (int i = 0; i < 6000; i++)
            {
                Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, rng);
                foreach (var e in s.Entities.Where(x => x.Team == Team.Party && x.Alive))
                {
                    if (!last.TryGetValue(e.Id, out var prev)) { last[e.Id] = e.Pos; run[e.Id] = 0; continue; }
                    bool idle = !Moved(prev, e.Pos) && e.TargetId == null;
                    last[e.Id] = e.Pos;
                    run[e.Id] = idle ? run[e.Id] + 1 : 0;
                    Assert.True(run[e.Id] < limit,
                        $"{e.Id} stood still with no target for {run[e.Id]} steps at {e.Pos.X:F2},{e.Pos.Y:F2}");
                    Assert.True(arena.Contains(e.Pos), $"{e.Id} off-arena at {e.Pos.X:F2},{e.Pos.Y:F2}");
                }
            }
        }
    }
}
