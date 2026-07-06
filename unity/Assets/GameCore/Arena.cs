#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>Whether an <see cref="ArenaShape"/> is an axis-aligned rectangle or a disc.</summary>
    public enum ArenaShapeKind { Rect, Disc }

    /// <summary>
    /// One convex piece of a per-stage arena (M8 slice 1). A layout's walkable region is the UNION
    /// of its shapes; a shape is either an axis-aligned rectangle (HalfW × HalfD half-extents) or a
    /// disc (HalfW = radius, HalfD ignored). <see cref="Tier"/> is COSMETIC height data this slice —
    /// no combat rule reads it yet; the client renders world Y = Tier * Balance.TerrainTierHeight so
    /// higher-tier shapes read as raised platforms. High-ground combat mechanics are deferred.
    /// Pure geometry, no rng — every method is deterministic.
    /// </summary>
    public sealed class ArenaShape
    {
        public ArenaShapeKind Kind = ArenaShapeKind.Rect;
        public double X, Y;          // centre
        public double HalfW, HalfD;  // Rect half-extents; a Disc uses HalfW as its radius (HalfD ignored)
        public int Tier;             // height tier, 0 = base plain

        /// <summary>True when point <paramref name="p"/> lies inside this shape (edge inclusive).</summary>
        public bool Contains(Vec2 p)
        {
            if (Kind == ArenaShapeKind.Disc)
            {
                double dx = p.X - X, dy = p.Y - Y;
                return dx * dx + dy * dy <= HalfW * HalfW;
            }
            return p.X >= X - HalfW && p.X <= X + HalfW
                && p.Y >= Y - HalfD && p.Y <= Y + HalfD;
        }

        // Disc projections aim a hair INSIDE the rim: center + dx*(r/len) can round to a point
        // whose squared distance exceeds r² by ~1e-15, making Contains(Nearest(p)) false — live
        // probing showed entities parked "outside" the union by 7e-15 after a shore clamp. The
        // inset is nanometre-scale (invisible, deterministic) and makes the invariant exact.
        private const double RimInset = 1e-9;

        /// <summary>Nearest point of this shape to <paramref name="p"/> — returns <paramref name="p"/>
        /// itself when contained, and always a point <see cref="Contains"/> accepts. Rect: per-axis
        /// clamp (exact bounds). Disc: radial projection a hair inside the rim (see RimInset); the
        /// degenerate centre case (only reachable when the radius is ~0, since p==centre is contained
        /// for any positive radius) resolves deterministically to centre + (radius, 0).</summary>
        public Vec2 Nearest(Vec2 p)
        {
            if (Contains(p)) return p;
            if (Kind == ArenaShapeKind.Disc)
            {
                double r = Math.Max(0.0, HalfW - RimInset);
                double dx = p.X - X, dy = p.Y - Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= 1e-12) return new Vec2(X + r, Y); // p == centre: pick a fixed radial
                double s = r / len;
                return new Vec2(X + dx * s, Y + dy * s);
            }
            return new Vec2(Math.Clamp(p.X, X - HalfW, X + HalfW),
                            Math.Clamp(p.Y, Y - HalfD, Y + HalfD));
        }
    }

    /// <summary>
    /// A per-stage arena: the walkable region is the UNION of <see cref="Shapes"/>, with per-shape
    /// height tiers the client renders as raised platforms (this slice: cosmetic data only). Movement
    /// is straight-line + collide-and-slide (no pathfinding), so layouts obey AUTHORING RULES that the
    /// content tests enforce (they are a design contract, not enforced by this code):
    /// <list type="bullet">
    /// <item>The union must be CONNECTED and roughly star-shaped around the origin.</item>
    /// <item>The base plain (tier 0) must cover the disc r≈32 around the origin — the party spawns at
    /// the centre and packs ring the spawn ring (outer 26) + wander 5 + pad, so the whole engagement
    /// bubble is on solid tier-0 ground.</item>
    /// <item>Every Tier&gt;0 shape must OVERLAP at least one lower-tier shape by a band ≥ ~2 tiles wide —
    /// that overlap IS the ramp/stairs the client draws up onto the platform.</item>
    /// <item>Unwalkable gaps (future water/void) only as shallow PERIMETER bays (depth ≤ ~6, mouth ≥ ~8),
    /// never deep concave pockets or interior holes — collide-and-slide can round a shallow bay but
    /// gets stuck in a deep pocket.</item>
    /// </list>
    /// Pure geometry, no rng — every method is deterministic.
    /// </summary>
    public sealed class ArenaLayout
    {
        public string Id = "";
        public List<ArenaShape> Shapes = new List<ArenaShape>(); // walkable region = the UNION of shapes

        /// <summary>True when <paramref name="p"/> lies inside ANY shape (the walkable union).</summary>
        public bool Contains(Vec2 p)
        {
            foreach (var sh in Shapes) if (sh.Contains(p)) return true;
            return false;
        }

        /// <summary>Max <see cref="ArenaShape.Tier"/> over the shapes containing <paramref name="p"/>;
        /// 0 when <paramref name="p"/> is outside the union (the base plain).</summary>
        public int TierAt(Vec2 p)
        {
            int tier = 0;
            foreach (var sh in Shapes)
                if (sh.Contains(p) && sh.Tier > tier) tier = sh.Tier;
            return tier;
        }

        /// <summary><paramref name="p"/> when contained, else the nearest point across all shapes.
        /// Distances compare strict-&lt; in shape order, so the LOWEST index wins any tie — deterministic,
        /// no rng.</summary>
        public Vec2 Clamp(Vec2 p)
        {
            if (Contains(p)) return p;
            Vec2 best = p;
            double bestD2 = double.MaxValue;
            foreach (var sh in Shapes)
            {
                var q = sh.Nearest(p);
                double dx = q.X - p.X, dy = q.Y - p.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = q; }
            }
            return best;
        }
    }
}
