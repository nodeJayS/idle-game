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
                // Crypt-native trio (10.10a) — dark palettes, GENTLE lift (crypt floors are dim
                // grey-blue; brightness comes from albedo contrast, never hot emission).
                { "grave_ooze", GraveOoze() },
                { "bone_amalgam", BoneAmalgam() },
                { "crypt_wraith", CryptWraith() },
                // Molten + frost tier critters (10.10b) — same desaturation rule as the crypt trio.
                { "magma_blob", MagmaBlob() },
                { "frost_drifter", FrostDrifter() },
                // Slither's first user (10.10c) — and the shape rehearsal for the (d) boss.
                { "bone_serpent", BoneSerpent() },
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

        /// <summary>Grave Ooze — the crypt tank: a fat low body with two side nubs (the mire_slime
        /// silhouette) plus a half-swallowed BONE poking out of the mass — the "grave" tell. Sickly
        /// necrotic green a hair brighter than the crypt's grey-blue floor so it wells up readable.
        /// SpawnStyle "rise" (GameCore) does the emergence; Hop carries it after.</summary>
        private static Def GraveOoze()
        {
            // Play-tuned 2026-07-12 (two passes): the crypt's dim warm-grey light DESATURATES —
            // the first passes read as a mossy boulder. Green must dominate its channels outright
            // to survive; equal-ish R/B reads grey on the lavender floors.
            Color body = new Color(0.32f, 0.58f, 0.34f);
            Color nub  = new Color(0.40f, 0.68f, 0.42f);
            Color bone = new Color(0.78f, 0.75f, 0.62f); // the jutting femur — ivory, no glow
            return new Def
            {
                Family = SdfBlobAnimator.Family.Hop,
                BoundsPadding = 0.6f, // hop arc + squash margin (the MireSlime numbers)
                // GROUND RULE (see MireSlime): authored resting on y=0.
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "body", localPosition = new Vector3(0f, 0.52f, 0f),
                            radius = 0.50f, halfLength = 0f, color = body, blendK = 0.35f },
                    new() { name = "nub_L", localPosition = new Vector3(-0.40f, 0.40f, 0f),
                            radius = 0.21f, halfLength = 0f, color = nub, blendK = 0.24f },
                    new() { name = "nub_R", localPosition = new Vector3(0.40f, 0.40f, 0f),
                            radius = 0.21f, halfLength = 0f, color = nub, blendK = 0.24f },
                    // The bone: a thin capsule BREAKING THE SILHOUETTE at a jaunty angle — Play-caught
                    // that a shy bone drowns in the mass; it must clear the body top from any camera
                    // yaw. Small blendK so it stays crisp (a fused bone reads as a lump).
                    new() { name = "bone", localPosition = new Vector3(0.24f, 0.90f, -0.10f),
                            localEuler = new Vector3(0f, 0f, -38f),
                            radius = 0.10f, halfLength = 0.26f, color = bone, blendK = 0.10f },
                },
            };
        }

        /// <summary>Bone Amalgam — the crypt walker: the BogShambler frame (body + head + two chubby
        /// legs) in grave-bone ivory with dark joint-gristle legs, plus a shoulder spur so the
        /// silhouette says "knot of bones", not "pale shambler".</summary>
        private static Def BoneAmalgam()
        {
            // Play-tuned 2026-07-12: the first-pass ivory read as muddy "mud golem" brown in the
            // crypt light — pushed toward real bleached bone so the fantasy lands.
            Color mass = new Color(0.68f, 0.64f, 0.54f);
            Color skull = new Color(0.80f, 0.76f, 0.64f);
            Color joint = new Color(0.42f, 0.38f, 0.31f); // dark gristle legs
            return new Def
            {
                Family = SdfBlobAnimator.Family.Walk,
                BoundsPadding = 0.5f, // ±LegSwing excursion (the BogShambler numbers)
                // GROUND RULE (see MireSlime): leg tips touch y=0, body/head stack above.
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "body", localPosition = new Vector3(0f, 0.88f, 0f),
                            radius = 0.48f, halfLength = 0f, color = mass, blendK = 0.30f },
                    new() { name = "head", localPosition = new Vector3(0f, 1.46f, 0.08f),
                            radius = 0.28f, halfLength = 0f, color = skull, blendK = 0.25f },
                    // A jutting shoulder spur — crisp (low blendK) like the ooze's bone.
                    new() { name = "spur", localPosition = new Vector3(-0.34f, 1.22f, -0.04f),
                            localEuler = new Vector3(0f, 0f, 42f),
                            radius = 0.10f, halfLength = 0.20f, color = skull, blendK = 0.12f },
                    new() { name = "leg_L", localPosition = new Vector3(-0.24f, 0.43f, 0f),
                            radius = 0.17f, halfLength = 0.26f, color = joint, blendK = 0.16f },
                    new() { name = "leg_R", localPosition = new Vector3(0.24f, 0.43f, 0f),
                            radius = 0.17f, halfLength = 0.26f, color = joint, blendK = 0.16f },
                },
            };
        }

        /// <summary>Crypt Wraith — the ranged floater: the FenSpirit frame in dark spectral purple
        /// (the crypt's gloom accent), wisps trailing lower and further back so it drifts like torn
        /// cloth. All tint in the albedo — the diorama bloom would wash any hot emission white.</summary>
        private static Def CryptWraith()
        {
            Color shroud = new Color(0.40f, 0.34f, 0.54f); // dark spectral purple
            Color crown  = new Color(0.52f, 0.46f, 0.68f); // brighter hood/crown
            Color wisp   = new Color(0.33f, 0.28f, 0.45f); // trailing rags, darker
            return new Def
            {
                Family = SdfBlobAnimator.Family.Float,
                BoundsPadding = 0.4f, // hover ±0.15 + death-waft margin (the FenSpirit numbers)
                // GROUND RULE + hover clearance (see FenSpirit): lowest wisp keeps ~0.10 at the dip.
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "body", localPosition = new Vector3(0f, 0.78f, 0f),
                            radius = 0.38f, halfLength = 0f, color = shroud, blendK = 0.32f },
                    new() { name = "head", localPosition = new Vector3(0f, 1.20f, 0.02f),
                            radius = 0.25f, halfLength = 0f, color = crown, blendK = 0.26f },
                    new() { name = "wisp_L", localPosition = new Vector3(-0.26f, 0.42f, -0.16f),
                            radius = 0.15f, halfLength = 0f, color = wisp, blendK = 0.22f },
                    new() { name = "wisp_R", localPosition = new Vector3(0.26f, 0.42f, -0.16f),
                            radius = 0.15f, halfLength = 0f, color = wisp, blendK = 0.22f },
                    // A third, lower tail-wisp so the drift reads as torn cloth, not a tripod.
                    new() { name = "wisp_T", localPosition = new Vector3(0f, 0.34f, -0.26f),
                            radius = 0.13f, halfLength = 0f, color = wisp, blendK = 0.22f },
                },
            };
        }

        /// <summary>Magma Blob — the molten tier's BEATING heart: dark basalt mass with EMBER nubs —
        /// the orange dominates its channels outright (the 10.10a desaturation lesson) so it glows
        /// against the lava floors without any hot emission. Pulse gait (10.10c): the mass swells
        /// rhythmically — exactly the critter the family was named for.</summary>
        private static Def MagmaBlob()
        {
            // Play-tuned 2026-07-12: pass one's "cooled crust" brown was near-IDENTICAL to the
            // molten floor (0.35,0.25,0.22) — a brown boulder. Obsidian-black mass + big bright
            // ember masses is the contrast pair that survives; one ember faces the camera south.
            Color basalt = new Color(0.15f, 0.13f, 0.14f);
            Color ember  = new Color(0.92f, 0.46f, 0.14f);
            Color core   = new Color(1.00f, 0.66f, 0.24f); // the top vent, brightest
            return new Def
            {
                Family = SdfBlobAnimator.Family.Pulse,
                BoundsPadding = 0.6f, // beat swell + travel bounce fit inside the old hop margin
                // GROUND RULE (see MireSlime): authored resting on y=0.
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "body", localPosition = new Vector3(0f, 0.50f, 0f),
                            radius = 0.48f, halfLength = 0f, color = basalt, blendK = 0.34f },
                    new() { name = "ember_F", localPosition = new Vector3(-0.20f, 0.34f, 0.34f),
                            radius = 0.24f, halfLength = 0f, color = ember, blendK = 0.20f },
                    new() { name = "ember_R", localPosition = new Vector3(0.40f, 0.42f, -0.04f),
                            radius = 0.22f, halfLength = 0f, color = ember, blendK = 0.20f },
                    // The vent: a bright core cresting the mass — crisp, breaking the silhouette
                    // (the GraveOoze femur lesson).
                    new() { name = "vent", localPosition = new Vector3(0.06f, 0.90f, 0f),
                            radius = 0.19f, halfLength = 0f, color = core, blendK = 0.14f },
                },
            };
        }

        /// <summary>Frost Drifter — the frost tier's ranged floater: deep glacial blue-teal (the
        /// frost floors are LIGHT, so contrast comes from going DARKER — the BoneAmalgam lesson
        /// inverted) with a paler crystal crown breaking the top.</summary>
        private static Def FrostDrifter()
        {
            Color ice    = new Color(0.30f, 0.48f, 0.66f); // deep glacial blue — blue dominates
            Color crown  = new Color(0.52f, 0.72f, 0.88f); // pale crystal cap
            Color shard  = new Color(0.26f, 0.40f, 0.56f); // trailing shards, darker
            return new Def
            {
                Family = SdfBlobAnimator.Family.Float,
                BoundsPadding = 0.4f, // hover ±0.15 + waft margin (the FenSpirit numbers)
                // GROUND RULE + hover clearance (see FenSpirit).
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "body", localPosition = new Vector3(0f, 0.76f, 0f),
                            radius = 0.36f, halfLength = 0f, color = ice, blendK = 0.30f },
                    // The crown: a crisp up-thrust crystal capsule cresting the body.
                    new() { name = "crown", localPosition = new Vector3(0f, 1.16f, 0f),
                            localEuler = new Vector3(0f, 0f, 12f),
                            radius = 0.14f, halfLength = 0.18f, color = crown, blendK = 0.14f },
                    new() { name = "shard_L", localPosition = new Vector3(-0.26f, 0.44f, -0.14f),
                            radius = 0.14f, halfLength = 0f, color = shard, blendK = 0.22f },
                    new() { name = "shard_R", localPosition = new Vector3(0.26f, 0.44f, -0.14f),
                            radius = 0.14f, halfLength = 0f, color = shard, blendK = 0.22f },
                },
            };
        }

        /// <summary>Bone Serpent — Slither's first user: a ground-hugging chain of five vertebrae
        /// (seg0 = the skull, authored head-to-tail in prims order — the animator's chain order IS
        /// the authoring order). Radii and ivory both taper toward the tail so the whip reads even
        /// in stills; each segment rests ON y=0 (y = its radius, the ground rule). blendK generous
        /// so the chain fuses into one worm, not a bead string. Head faces +Z (CombatView's facing
        /// axis); the wave offsets local X.</summary>
        private static Def BoneSerpent()
        {
            // The amalgam's bleached-bone family, tapering dark toward the tail (desaturation rule:
            // ivory bright enough to survive the crypt's dim light).
            Color skull = new Color(0.80f, 0.76f, 0.64f);
            Color mid   = new Color(0.68f, 0.64f, 0.54f);
            Color tail  = new Color(0.50f, 0.46f, 0.39f);
            return new Def
            {
                Family = SdfBlobAnimator.Family.Slither,
                BoundsPadding = 0.45f, // lateral whip (±SlitherAmp) + margin
                Prims = new List<SdfBlobRig.PrimitiveDef>
                {
                    new() { name = "seg0", localPosition = new Vector3(0f, 0.24f, 0.55f),
                            radius = 0.24f, halfLength = 0f, color = skull, blendK = 0.26f },
                    new() { name = "seg1", localPosition = new Vector3(0f, 0.20f, 0.18f),
                            radius = 0.20f, halfLength = 0f, color = mid, blendK = 0.28f },
                    new() { name = "seg2", localPosition = new Vector3(0f, 0.17f, -0.16f),
                            radius = 0.17f, halfLength = 0f, color = mid, blendK = 0.28f },
                    new() { name = "seg3", localPosition = new Vector3(0f, 0.14f, -0.47f),
                            radius = 0.14f, halfLength = 0f, color = tail, blendK = 0.28f },
                    new() { name = "seg4", localPosition = new Vector3(0f, 0.11f, -0.74f),
                            radius = 0.11f, halfLength = 0f, color = tail, blendK = 0.26f },
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
