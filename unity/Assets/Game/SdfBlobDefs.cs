#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// The "JSON-ish" blob registry (ROADMAP 4, slice 3): maps a monster id to an authored SDF-blob
    /// visual — its primitives, gait family, health-bar height, and bounds padding. This is the
    /// CONTENT layer over the slice-1/2 tech (<see cref="SdfBlobRig"/> + <see cref="SdfBlobAnimator"/>):
    /// where <see cref="MonsterModel"/> loads a faceted FBX, the swamp blob family is authored here as
    /// overlapping spheres/capsules that the SDF shell fuses into one seamless critter. Client-only —
    /// GameCore has no idea it exists; keyed by monster id (= MonsterDef.Id = CombatEntity.RefId).
    ///
    /// PALETTE: murky swamp greens/teals chosen to POP slightly against the zone-3 ground
    /// (0.35,0.42,0.30) — a hair brighter/more saturated so a blob reads out of the muck. The
    /// chubby-limb lesson (SdfGaitTest) is load-bearing: limb/nub radii must be GENEROUS relative to
    /// the body or the smooth-min swallows them; blend K per prim tunes how melty each seam is.
    /// </summary>
    public static class SdfBlobDefs
    {
        /// <summary>One authored blob: its prims, gait family, health-bar height, and bounds slack.</summary>
        public sealed class Def
        {
            public List<SdfBlobRig.PrimitiveDef> Prims = new();
            public SdfBlobAnimator.Family Family;
            public float Height;         // health-bar anchor = TOP of the blob (see BuildDefs)
            public float BoundsPadding;  // world-space slack for the gait's peak excursion

            /// <summary>A per-instance deep copy of the authored prims. The registry is STATIC and
            /// shared, but BuildMesh writes PrimitiveDef.node — so every spawned blob must get its
            /// OWN def objects or two blobs of the same id would fight over the node pose.</summary>
            public List<SdfBlobRig.PrimitiveDef> ClonePrims()
            {
                var copy = new List<SdfBlobRig.PrimitiveDef>(Prims.Count);
                foreach (var p in Prims) copy.Add(p.Clone());
                return copy;
            }
        }

        // Built once, lazily. Keyed by monster id.
        private static Dictionary<string, Def>? _defs;
        private static Dictionary<string, Def> Defs => _defs ??= BuildDefs();

        /// <summary>Whether this monster id renders as an SDF blob (vs a faceted model / primitive).</summary>
        public static bool Has(string id) => Defs.ContainsKey(id);

        /// <summary>Fetch the authored def for an id, or null if it isn't a blob.</summary>
        public static Def? TryGet(string id) => Defs.TryGetValue(id, out var d) ? d : null;

        private static Dictionary<string, Def> BuildDefs()
        {
            var d = new Dictionary<string, Def>
            {
                { "mire_slime", MireSlime() },
                { "bog_shambler", BogShambler() },
                { "fen_spirit", FenSpirit() },
            };
            foreach (var def in d.Values) def.Height = TopOf(def.Prims);
            return d;
        }

        // ---- authored critters -----------------------------------------------------------------

        /// <summary>Mire Slime — the tank: a fat low body sphere with two side nubs, hopping. Murky
        /// green-teal that lifts out of the ground muck. Nubs kept chubby so the smin doesn't eat
        /// them; they ride the whole-blob hop arc (Hop translates every node).</summary>
        private static Def MireSlime()
        {
            Color body = new Color(0.28f, 0.55f, 0.40f); // murky green-teal
            Color nub  = new Color(0.34f, 0.62f, 0.48f); // a touch brighter so the nubs read
            return new Def
            {
                Family = SdfBlobAnimator.Family.Hop,
                // Pad for the hop height (~0.35 arc) plus the squash margin, like SdfGaitTest's hopper.
                BoundsPadding = 0.6f,
                // GROUND RULE (Play-verified 2026-07-05): CombatView roots monsters feet-at-ground
                // (root y = 0), so prims are authored with the blob RESTING ON y=0 — a body at y=0
                // buries its lower half (read as a pale crescent in the swamp). Body bottom kisses
                // the ground; the hop landing's center-compression sinks it a hair (reads as squash).
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "body", localPosition = new Vector3(0f, 0.54f, 0f),
                            radius = 0.52f, halfLength = 0f, color = body, blendK = 0.35f },
                    new() { name = "nub_L", localPosition = new Vector3(-0.42f, 0.42f, 0f),
                            radius = 0.22f, halfLength = 0f, color = nub, blendK = 0.24f },
                    new() { name = "nub_R", localPosition = new Vector3(0.42f, 0.42f, 0f),
                            radius = 0.22f, halfLength = 0f, color = nub, blendK = 0.24f },
                },
            };
        }

        /// <summary>Bog Shambler — the walker: body + head + two chubby legs (SdfGaitTest's walker
        /// shape), a mossy green/brown so it reads as a lumbering peat-thing. Legs swing about the
        /// hip; padding covers the ±LegSwing excursion.</summary>
        private static Def BogShambler()
        {
            Color body = new Color(0.32f, 0.46f, 0.30f); // mossy green
            Color head = new Color(0.44f, 0.50f, 0.28f); // olive head
            Color leg  = new Color(0.34f, 0.30f, 0.22f); // peat brown legs
            return new Def
            {
                Family = SdfBlobAnimator.Family.Walk,
                // A swinging leg reaches past its authored bounds; pad generously (SdfGaitTest lesson).
                BoundsPadding = 0.5f,
                // GROUND RULE (see MireSlime): authored resting on y=0 — leg tips (centre − halfLength
                // − radius) touch the ground, body/head stack above.
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "body", localPosition = new Vector3(0f, 0.89f, 0f),
                            radius = 0.50f, halfLength = 0f, color = body, blendK = 0.30f },
                    new() { name = "head", localPosition = new Vector3(0f, 1.49f, 0.06f),
                            radius = 0.30f, halfLength = 0f, color = head, blendK = 0.25f },
                    // Chubby legs (r 0.17): generous radius so the smin doesn't thin them mid-swing.
                    new() { name = "leg_L", localPosition = new Vector3(-0.24f, 0.43f, 0f),
                            radius = 0.17f, halfLength = 0.26f, color = leg, blendK = 0.16f },
                    new() { name = "leg_R", localPosition = new Vector3(0.24f, 0.43f, 0f),
                            radius = 0.17f, halfLength = 0.26f, color = leg, blendK = 0.16f },
                },
            };
        }

        /// <summary>Fen Spirit — the ranged floater: a body sphere + head + two small trailing wisp
        /// spheres, pale spectral blue-green. Hovers continuously (Float family). Glows are GENTLE
        /// — the diorama bloom washes bright emission to white, so the spectral tint stays in the
        /// albedo, not a hot emission (CLAUDE.md). Wisp radii kept generous so they fuse.</summary>
        private static Def FenSpirit()
        {
            Color body = new Color(0.45f, 0.72f, 0.70f); // pale spectral teal
            Color head = new Color(0.58f, 0.80f, 0.78f); // brighter crown
            Color wisp = new Color(0.40f, 0.66f, 0.66f); // trailing motes, a hair darker
            return new Def
            {
                Family = SdfBlobAnimator.Family.Float,
                // Continuous hover (±FloatHover 0.15) is small, but pad for it plus the death waft margin.
                BoundsPadding = 0.4f,
                // GROUND RULE (see MireSlime) + hover clearance: lowest point (wisp bottom) keeps
                // ~0.10 clearance at the hover's lowest dip (−FloatHover 0.15), so it never grounds.
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "body", localPosition = new Vector3(0f, 0.75f, 0f),
                            radius = 0.40f, halfLength = 0f, color = body, blendK = 0.32f },
                    new() { name = "head", localPosition = new Vector3(0f, 1.17f, 0f),
                            radius = 0.26f, halfLength = 0f, color = head, blendK = 0.26f },
                    // Two small trailing wisps below/behind — generous radius so they stay fused.
                    new() { name = "wisp_L", localPosition = new Vector3(-0.30f, 0.41f, -0.10f),
                            radius = 0.16f, halfLength = 0f, color = wisp, blendK = 0.22f },
                    new() { name = "wisp_R", localPosition = new Vector3(0.30f, 0.41f, -0.10f),
                            radius = 0.16f, halfLength = 0f, color = wisp, blendK = 0.22f },
                },
            };
        }

        // ---- helpers ---------------------------------------------------------------------------

        /// <summary>Health-bar anchor = the TOP of the fused blob: the max over prims of
        /// (localPosition.y + radius + halfLength). A capsule reaches radius+halfLength above its
        /// centre; a sphere just radius. Ignores blend melt (a small underestimate is fine — the
        /// bar floats a hair inside the shell rather than off in space).</summary>
        private static float TopOf(List<SdfBlobRig.PrimitiveDef> prims)
        {
            float top = 0f;
            foreach (var p in prims)
                top = Mathf.Max(top, p.localPosition.y + p.radius + p.halfLength);
            return top;
        }
    }
}
