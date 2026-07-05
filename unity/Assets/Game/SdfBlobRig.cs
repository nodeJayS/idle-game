#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Driver for the IdleGame/SdfBlendShell shader (ROADMAP 4, slice 1). Authors a critter as a
    /// list of primitive spheres/capsules, builds ONE low-poly source mesh around them all (each
    /// primitive's verts just need to sit NEAR it — the vertex shader snaps them onto the combined
    /// smooth-min surface), and every LateUpdate reads the per-primitive child transforms and pushes
    /// their WORLD-space pose into a MaterialPropertyBlock so animation can move primitives later.
    ///
    /// Client-only tech-art: GameCore has no idea this exists. Mirrors the faceted-monster idea
    /// (one flat-shaded group, no rig) — here the "rig" is just child transforms feeding uniforms.
    /// </summary>
    [ExecuteAlways]
    public sealed class SdfBlobRig : MonoBehaviour
    {
        public const int MaxPrims = 16; // must match MAX_PRIMS in SdfBlendShell.shader

        /// <summary>One authored primitive. A child Transform (created by the rig on Build) carries
        /// the live world pose so a future gait can bob/swing it; the rig reads it each frame.</summary>
        [System.Serializable]
        public sealed class PrimitiveDef
        {
            public string name = "prim";
            public Vector3 localPosition = Vector3.zero;
            public Vector3 localEuler = Vector3.zero;    // capsule axis = this rotation * up
            public float radius = 0.5f;
            public float halfLength = 0f;                // 0 => sphere, else capsule half-length
            public Color color = Color.white;
            public float blendK = 0.25f;                 // smooth-min radius: how melty this prim is

            [System.NonSerialized] public Transform? node; // live child transform (set by BuildMesh)

            /// <summary>A fresh copy carrying no live <see cref="node"/>. Load-bearing when authored
            /// defs are SHARED across instances (e.g. SdfBlobDefs' static registry): BuildMesh writes
            /// pd.node, so two blobs built from the SAME def object would stomp each other's node and
            /// push the wrong world pose. Clone per instance so each blob owns its own def objects.</summary>
            public PrimitiveDef Clone() => new PrimitiveDef
            {
                name = name, localPosition = localPosition, localEuler = localEuler,
                radius = radius, halfLength = halfLength, color = color, blendK = blendK,
            };
        }

        [Tooltip("Authored primitives. Overlap them and they blend into one seamless body.")]
        public List<PrimitiveDef> prims = new();

        [Tooltip("Latitude/longitude segments of each primitive's source mesh. ~8 keeps a few "
               + "hundred verts per primitive; the shader snaps them so exact placement is loose.")]
        [Range(4, 16)] public int subdivisions = 8;

        [Tooltip("Extra world-space slack added to every prim's bounds reach. Bounds bake from the "
               + "AUTHORED poses at BuildMesh; a gait that swings a node out past them frustum-pops "
               + "the blob. Set this to the largest expected swing/hop excursion (world units).")]
        public float boundsPadding = 0f;

        // ---- shader property ids (cached; string lookups every frame are wasteful) ----
        private static readonly int IdPosRad  = Shader.PropertyToID("_PrimPosRad");
        private static readonly int IdAxisLen = Shader.PropertyToID("_PrimAxisLen");
        private static readonly int IdColor   = Shader.PropertyToID("_PrimColor");
        private static readonly int IdCount   = Shader.PropertyToID("_PrimCount");
        private static readonly int IdTint    = Shader.PropertyToID("_BlobTint");
        private static readonly int IdEmit    = Shader.PropertyToID("_BlobEmit");

        // ---- per-renderer rank/mod tell (ROADMAP 4 slice 3), pushed via the SAME MPB as the prim
        // arrays. CombatView sets both once at spawn (rank/mod); the animator's hit-flash drives
        // SetEmission each frame and restores this field's prior value (via Emission) so a flash
        // never wipes a rank tell — same _emCache guard MonsterAnimator uses. Defaults are the
        // shader's no-op values (lean 0, emission black), so an un-tinted blob looks unchanged.
        private Color _tintColor = Color.white;
        private float _tintLean;              // 0 => no albedo lean (harmless default)
        private Color _emitColor = Color.black; // black => no additive glow (harmless default)

        /// <summary>Rank/mod albedo tell: lean the whole blob toward <paramref name="rgb"/> by
        /// <paramref name="lean"/> (0..1). Persistent until changed; written into the MPB every
        /// PushPrimitives so it survives a rebuild's MPB churn.</summary>
        public void SetTint(Color rgb, float lean)
        {
            _tintColor = rgb;
            _tintLean = Mathf.Clamp01(lean);
        }

        /// <summary>Additive glow tell (rank/mod aura, and the animator's transient hit-flash).
        /// Persistent until changed — the animator caches <see cref="Emission"/> before a flash and
        /// restores it after, so a rank/mod glow is never clobbered.</summary>
        public void SetEmission(Color rgb) => _emitColor = rgb;

        /// <summary>The current persistent emission (rank/mod glow). The animator snapshots this
        /// before a hit-flash so the restore lands EXACTLY on the prior tell, never black.</summary>
        public Color Emission => _emitColor;

        private MeshFilter _mf = null!;
        private MeshRenderer _mr = null!;
        private MaterialPropertyBlock? _mpb;

        // Reused scratch so the per-frame push allocates nothing.
        private readonly Vector4[] _posRad  = new Vector4[MaxPrims];
        private readonly Vector4[] _axisLen = new Vector4[MaxPrims];
        private readonly Vector4[] _colors  = new Vector4[MaxPrims];

        private void LateUpdate() => PushPrimitives();

        /// <summary>Re-push the MaterialPropertyBlock now (call after mutating prim poses off the
        /// normal update path — e.g. an editor tool tweaking a value).</summary>
        public void SetPrimitivesDirty() => PushPrimitives();

        /// <summary>(Re)build the combined source mesh and the per-primitive child transforms, then
        /// push the arrays. Safe to call in edit mode from a menu item.</summary>
        public void BuildMesh()
        {
            _mf ??= GetComponent<MeshFilter>();
            if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();
            _mr ??= GetComponent<MeshRenderer>();
            if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();

            // Rebuild the primitive child transforms from the defs (destroy any stale ones first).
            var stale = new List<Transform>();
            foreach (Transform c in transform)
                if (c.name.StartsWith("prim:")) stale.Add(c);
            foreach (var c in stale) DestroyImmediate(c.gameObject);

            int n = Mathf.Min(prims.Count, MaxPrims);
            var verts = new List<Vector3>(n * 256);
            var cols  = new List<Color>(n * 256);
            var tris  = new List<int>(n * 512);

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool haveBounds = false;

            for (int i = 0; i < n; i++)
            {
                var pd = prims[i];
                var node = new GameObject("prim:" + pd.name).transform;
                node.SetParent(transform, false);
                node.localPosition = pd.localPosition;
                node.localRotation = Quaternion.Euler(pd.localEuler);
                pd.node = node;

                // Source geometry in OBJECT space at the primitive's authored LOCAL pose. The verts
                // only need to be near the primitive — the shader does the real placement.
                AppendPrimitiveMesh(pd, verts, cols, tris);

                // Pad the bounds: verts get SNAPPED outward by up to (radius + blendK), so the mesh
                // bounds must cover the union of primitive reach or the whole blob frustum-pops.
                float reach = pd.radius + pd.halfLength + pd.blendK + boundsPadding;
                var b = new Bounds(pd.localPosition, Vector3.one * (reach * 2f));
                if (!haveBounds) { bounds = b; haveBounds = true; }
                else bounds.Encapsulate(b);
            }

            var mesh = new Mesh { name = "SdfBlob", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);   // unused by the shader (colour is SDF-derived) but harmless/debuggable
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();          // not used for shading, but keeps the asset valid
            mesh.bounds = haveBounds ? bounds : new Bounds(Vector3.zero, Vector3.one);
            _mf.sharedMesh = mesh;

            PushPrimitives();
        }

        /// <summary>Read every primitive child's world pose and write the shader arrays into the MPB.
        /// World-space so the snap math in the shader is done in world units (matches TransformObject
        /// ToWorld in the vertex stage).</summary>
        private void PushPrimitives()
        {
            if (_mr == null) { _mr = GetComponent<MeshRenderer>(); if (_mr == null) return; }
            _mpb ??= new MaterialPropertyBlock();
            _mr.GetPropertyBlock(_mpb);

            int n = Mathf.Min(prims.Count, MaxPrims);
            for (int i = 0; i < n; i++)
            {
                var pd = prims[i];
                Vector3 wpos = pd.node != null ? pd.node.position
                             : transform.TransformPoint(pd.localPosition);
                Quaternion wrot = pd.node != null ? pd.node.rotation
                             : transform.rotation * Quaternion.Euler(pd.localEuler);
                // Uniform-ish scale should scale radius/length so animation reads right. Prefer the
                // NODE's own lossyScale (an animator can squash/stretch a single prim by scaling its
                // node); fall back to the parent's scale when there's no live node yet. .x by the
                // existing convention — the SDF only carries a single radius, so scale is uniform-ish.
                float s = pd.node != null ? pd.node.lossyScale.x : transform.lossyScale.x;
                Vector3 axis = (wrot * Vector3.up).normalized;

                _posRad[i]  = new Vector4(wpos.x, wpos.y, wpos.z, pd.radius * s);
                _axisLen[i] = new Vector4(axis.x, axis.y, axis.z, pd.halfLength * s);
                // .linear: the project renders in LINEAR space and MPB floats get no colour-space
                // conversion, so raw sRGB-authored values render ~2x too bright (Play-verified
                // 2026-07-05: mid sea-green read as pale sage). Convert here so authored Colors
                // behave like inspector colours.
                var lc = pd.color.linear;
                _colors[i]  = new Vector4(lc.r, lc.g, lc.b, pd.blendK * s);
            }
            // Zero out unused slots so a shrunk list can't leave a stale primitive lit.
            for (int i = n; i < MaxPrims; i++)
            {
                _posRad[i] = Vector4.zero; _axisLen[i] = Vector4.zero; _colors[i] = Vector4.zero;
            }

            _mpb.SetVectorArray(IdPosRad, _posRad);
            _mpb.SetVectorArray(IdAxisLen, _axisLen);
            _mpb.SetVectorArray(IdColor, _colors);
            _mpb.SetFloat(IdCount, n); // shader-side _PrimCount is a float (MPB.SetInt IS SetFloat)
            // Rank/mod tell: rgb tint + lean amount in w, plus additive emission. Defaults
            // (lean 0 / emission black) are the shader's no-ops, so an un-tinted blob is unchanged.
            // Same .linear conversion as the prim colours (see above).
            var lt = _tintColor.linear; var le = _emitColor.linear;
            _mpb.SetVector(IdTint, new Vector4(lt.r, lt.g, lt.b, _tintLean));
            _mpb.SetVector(IdEmit, new Vector4(le.r, le.g, le.b, 0f));
            _mr.SetPropertyBlock(_mpb);
        }

        // ---- source-mesh generation --------------------------------------------------------

        /// <summary>Append one primitive's low-poly source mesh (object space, at its local pose).
        /// Sphere = UV-sphere; capsule = two hemisphere caps joined by a cylinder along local +Y.
        /// Vertex counts scale with <see cref="subdivisions"/>; a few hundred verts per primitive.</summary>
        private void AppendPrimitiveMesh(PrimitiveDef pd, List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            int rings = Mathf.Max(4, subdivisions);      // latitude bands (hemisphere gets rings/2)
            int seg = Mathf.Max(4, subdivisions);        // longitude segments
            var rot = Quaternion.Euler(pd.localEuler);
            Vector3 origin = pd.localPosition;
            float r = pd.radius;
            float hl = pd.halfLength;

            // Emit a vertex given (u in [0,1] around, v-latitude angle) plus an axial offset along
            // local +Y. Placement is deliberately approximate; the shader snaps it home.
            int Vert(float lon, float lat, float yOff)
            {
                float sinLat = Mathf.Sin(lat), cosLat = Mathf.Cos(lat);
                Vector3 local = new Vector3(
                    Mathf.Cos(lon) * sinLat * r,
                    cosLat * r + yOff,
                    Mathf.Sin(lon) * sinLat * r);
                int idx = verts.Count;
                verts.Add(origin + rot * local);
                cols.Add(pd.color);
                return idx;
            }

            int halfRings = Mathf.Max(2, rings / 2);

            // Grid of ring indices we can stitch: build top hemisphere (lat 0..pi/2, yOff +hl),
            // then bottom hemisphere (lat pi/2..pi, yOff -hl). For a sphere hl=0 so it's one sphere.
            var grid = new int[rings + 1, seg + 1];
            for (int ry = 0; ry <= rings; ry++)
            {
                // Split the latitude sweep so the seam sits at the equator; add the cap offset there.
                bool topHalf = ry <= halfRings;
                float lat;
                float yOff;
                if (topHalf)
                {
                    lat = (ry / (float)halfRings) * (Mathf.PI * 0.5f);   // 0..pi/2
                    yOff = hl;
                }
                else
                {
                    lat = Mathf.PI * 0.5f + ((ry - halfRings) / (float)(rings - halfRings)) * (Mathf.PI * 0.5f);
                    yOff = -hl;
                }
                for (int c = 0; c <= seg; c++)
                {
                    float lon = (c / (float)seg) * Mathf.PI * 2f;
                    grid[ry, c] = Vert(lon, lat, yOff);
                }
            }

            for (int ry = 0; ry < rings; ry++)
                for (int c = 0; c < seg; c++)
                {
                    int a = grid[ry, c], b = grid[ry, c + 1];
                    int d = grid[ry + 1, c], e = grid[ry + 1, c + 1];
                    // Two tris per quad, wound CCW for an outward face.
                    tris.Add(a); tris.Add(d); tris.Add(b);
                    tris.Add(b); tris.Add(d); tris.Add(e);
                }
        }
    }
}
