#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Code-built chibi hero puppets (Tunic art pivot) — big head, small chunky body, flat
    /// matte colours, assembled from primitives + a couple of tiny meshes. No rig/FBX/Mixamo:
    /// the joints (Head/Arm/Leg empties) are swung procedurally by <see cref="ChibiAnimator"/>.
    /// Built per class; returns null for an unknown defId so SpawnView falls back to a capsule.
    /// Uses URP/Lit (matte), NOT TunicSurface — its grass-on-top blend is for terrain, not heads;
    /// URP/Lit still receives the crisp key light + dappled cookie.
    /// </summary>
    public static class ChibiHero
    {
        // --- chibi metrics (world units; ~1.35 tall) ---
        private const float Hip = 0.42f;       // body pivot height = top of the legs
        private const float TorsoH = 0.5f;
        private const float HeadR = 0.27f;     // big head
        private const float ArmLen = 0.42f, ArmThick = 0.15f;
        private const float LegLen = 0.42f, LegThick = 0.17f;
        private const float ShoulderX = 0.30f, HipX = 0.13f;
        private static float ShoulderY => TorsoH * 0.80f;

        public static (GameObject root, ChibiAnimator anim)? Build(string defId, bool ranged)
        {
            // §7.2 pipeline: heroes are palette-varied builds on shared class bodies.
            bool iceMage = defId == "icemage_basic";
            bool mage = defId == "magician_basic" || iceMage || ranged;
            bool warrior = defId == "warrior_basic";
            bool thief = defId == "thief_basic";
            if (!mage && !warrior && !thief) return null;

            // Palette
            Color skin = new Color(0.95f, 0.80f, 0.66f);
            Color tunic = iceMage ? new Color(0.42f, 0.70f, 0.86f)  // pale frost robes
                        : mage ? new Color(0.66f, 0.13f, 0.15f)
                        : thief ? new Color(0.16f, 0.22f, 0.20f)   // dark hunter-green leathers
                        : new Color(0.20f, 0.40f, 0.78f);
            Color limb = iceMage ? new Color(0.32f, 0.56f, 0.72f)
                       : mage ? new Color(0.55f, 0.11f, 0.13f)
                       : thief ? new Color(0.12f, 0.16f, 0.15f)
                       : new Color(0.17f, 0.32f, 0.62f);
            Color boot = thief ? new Color(0.10f, 0.09f, 0.09f) : new Color(0.30f, 0.22f, 0.15f);
            Color orb = iceMage ? new Color(0.55f, 0.88f, 1f)       // frost orb vs fire orb
                                : new Color(1f, 0.5f, 0.12f);

            var root = new GameObject("chibi");
            var body = Joint("body", root.transform, new Vector3(0f, Hip, 0f));

            // Torso + head
            Part(PrimitiveType.Cube, body, new Vector3(0f, TorsoH * 0.5f, 0f), new Vector3(0.5f, TorsoH, 0.34f), Mat(tunic));
            var head = Joint("head", body, new Vector3(0f, TorsoH + HeadR * 0.85f, 0f));
            Part(PrimitiveType.Sphere, head, Vector3.zero, Vector3.one * (HeadR * 2f), Mat(skin));

            // Arms (joints at the shoulders; limb mesh hangs down so rotating the joint swings it)
            var armL = Joint("armL", body, new Vector3(-ShoulderX, ShoulderY, 0f));
            Limb(armL, ArmLen, ArmThick, Mat(limb));
            var armR = Joint("armR", body, new Vector3(ShoulderX, ShoulderY, 0f));
            Limb(armR, ArmLen, ArmThick, Mat(limb));

            // Legs (joints at the hips)
            var legL = Joint("legL", body, new Vector3(-HipX, 0f, 0f));
            Limb(legL, LegLen, LegThick, Mat(boot));
            var legR = Joint("legR", body, new Vector3(HipX, 0f, 0f));
            Limb(legR, LegLen, LegThick, Mat(boot));

            // The hand sits at the bottom of the weapon arm.
            var hand = Joint("hand", armR, new Vector3(0f, -ArmLen, 0f));
            if (warrior) BuildSword(hand);
            else if (thief) BuildDagger(hand);
            else BuildStaff(hand, orb);

            // The thief is a dual-dagger duelist: a second blade in the off hand.
            if (thief)
            {
                var handL = Joint("handL", armL, new Vector3(0f, -ArmLen, 0f));
                BuildDagger(handL);
            }

            if (mage) BuildHat(head, tunic);
            else if (thief) BuildHood(head, tunic);

            var anim = root.AddComponent<ChibiAnimator>();
            anim.Body = body; anim.Head = head;
            anim.ArmL = armL; anim.ArmR = armR; anim.LegL = legL; anim.LegR = legR;
            anim.Setup();
            return (root, anim);
        }

        // --- class kit -------------------------------------------------------

        private static void BuildSword(Transform hand)
        {
            var grip = Mat(new Color(0.32f, 0.22f, 0.14f));
            var steel = Mat(new Color(0.74f, 0.76f, 0.80f));
            var guard = Mat(new Color(0.36f, 0.36f, 0.40f));
            Part(PrimitiveType.Cube, hand, new Vector3(0f, -0.02f, 0f), new Vector3(0.05f, 0.14f, 0.05f), grip);
            Part(PrimitiveType.Cube, hand, new Vector3(0f, -0.10f, 0.0f), new Vector3(0.24f, 0.04f, 0.06f), guard);
            Part(PrimitiveType.Cube, hand, new Vector3(0f, -0.44f, 0f), new Vector3(0.08f, 0.62f, 0.03f), steel); // blade points down from the hand
        }

        private static void BuildStaff(Transform hand, Color orb)
        {
            var shaft = Mat(new Color(0.40f, 0.28f, 0.17f));
            Part(PrimitiveType.Cube, hand, new Vector3(0f, 0.22f, 0f), new Vector3(0.05f, 0.78f, 0.05f), shaft); // staff rises from the hand
            Part(PrimitiveType.Sphere, hand, new Vector3(0f, 0.64f, 0f), Vector3.one * 0.20f, Emissive(orb)); // element orb (fire / frost)
        }

        private static void BuildHat(Transform head, Color robe)
        {
            var hat = Mat(new Color(robe.r * 0.85f, robe.g * 0.85f, robe.b * 0.85f));
            PartMesh(Cone(7, 0.30f, 0.50f), head, new Vector3(0f, HeadR * 0.7f, 0f), Vector3.one, hat);
        }

        /// <summary>A short twin dagger: stubby blade off the warrior's full sword build.</summary>
        private static void BuildDagger(Transform hand)
        {
            var grip = Mat(new Color(0.20f, 0.16f, 0.12f));
            var steel = Mat(new Color(0.78f, 0.80f, 0.84f));
            var guard = Mat(new Color(0.30f, 0.30f, 0.34f));
            Part(PrimitiveType.Cube, hand, new Vector3(0f, -0.02f, 0f), new Vector3(0.045f, 0.11f, 0.045f), grip);
            Part(PrimitiveType.Cube, hand, new Vector3(0f, -0.08f, 0f), new Vector3(0.16f, 0.035f, 0.05f), guard);
            Part(PrimitiveType.Cube, hand, new Vector3(0f, -0.26f, 0f), new Vector3(0.06f, 0.34f, 0.025f), steel); // short blade
        }

        /// <summary>A pointed hood: a darker cone tilted back so the face still shows.</summary>
        private static void BuildHood(Transform head, Color cloth)
        {
            var hood = Mat(new Color(cloth.r * 0.7f, cloth.g * 0.7f, cloth.b * 0.7f));
            var t = PartMesh(Cone(7, HeadR * 1.12f, HeadR * 1.9f), head,
                             new Vector3(0f, -HeadR * 0.18f, -HeadR * 0.15f), Vector3.one, hood);
            t.localRotation = Quaternion.Euler(-18f, 0f, 0f); // lean the point back; leaves the face open
        }

        // --- builders / helpers ---------------------------------------------

        /// <summary>A blocky limb: a cube hung from the joint so its top is at the pivot and it
        /// extends straight down — rotating the joint swings the whole limb.</summary>
        private static void Limb(Transform joint, float len, float thick, Material mat)
            => Part(PrimitiveType.Cube, joint, new Vector3(0f, -len * 0.5f, 0f), new Vector3(thick, len, thick), mat);

        private static Transform Joint(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        private static Transform Part(PrimitiveType type, Transform parent, Vector3 lpos, Vector3 lscale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localScale = lscale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static Transform PartMesh(Mesh mesh, Transform parent, Vector3 lpos, Vector3 lscale, Material mat)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localScale = lscale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            Bootstrap.MakeMatte(m);
            return m;
        }

        private static Material Emissive(Color c)
        {
            var m = Mat(c);
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 2.5f); }
            return m;
        }

        /// <summary>A faceted cone (flat-shaded) for the wizard hat.</summary>
        private static Mesh Cone(int seg, float r, float h)
        {
            var v = new List<Vector3>();
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            int apex = v.Count; v.Add(new Vector3(0f, h, 0f));
            int center = v.Count; v.Add(Vector3.zero);
            var tris = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                int n = (i + 1) % seg;
                tris.Add(i); tris.Add(n); tris.Add(apex);     // side
                tris.Add(center); tris.Add(n); tris.Add(i);   // base cap
            }
            return FlatMesh(v.ToArray(), tris.ToArray());
        }

        /// <summary>Flat-shade: each tri gets its own verts (hard facet normals); faces oriented
        /// outward by the centroid so winding never matters.</summary>
        private static Mesh FlatMesh(Vector3[] verts, int[] tris)
        {
            Vector3 c = Vector3.zero;
            foreach (var p in verts) c += p;
            c /= verts.Length;

            var fv = new Vector3[tris.Length];
            var ft = new int[tris.Length];
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], d = verts[tris[i + 2]];
                Vector3 n = Vector3.Cross(b - a, d - a);
                bool outward = Vector3.Dot(n, (a + b + d) / 3f - c) >= 0f;
                fv[i] = a; fv[i + 1] = outward ? b : d; fv[i + 2] = outward ? d : b;
                ft[i] = i; ft[i + 1] = i + 1; ft[i + 2] = i + 2;
            }
            var m = new Mesh { vertices = fv, triangles = ft };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
