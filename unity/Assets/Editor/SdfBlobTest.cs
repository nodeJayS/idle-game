#nullable enable
using IdleGame.Game;
using UnityEditor;
using UnityEngine;

namespace IdleGame.EditorTools
{
    /// <summary>
    /// Tools > SDF Blob Test (ROADMAP 4, slice 1). Spawns (or replaces) a "SdfBlobTest" GameObject
    /// in the open scene with a sample critter on the IdleGame/SdfBlendShell shader: a body sphere +
    /// an overlapping head, two arm capsules, and one thin antenna capsule with a small blend k
    /// (expected to dissolve a little — that's slice-1 data on how far the technique holds). Works in
    /// EDIT mode: no Play needed to see the fused body. The <see cref="SdfBlobWiggle"/> component
    /// bobs the head/arm/antenna transforms so the KILL CRITERION is observable: vertex-slide shimmer
    /// under faceted normals while parts move. Toggle it on the spawned object.
    /// </summary>
    public static class SdfBlobTest
    {
        [MenuItem("Tools/SDF Blob Test")]
        public static void Spawn()
        {
            var existing = GameObject.Find("SdfBlobTest");
            if (existing != null) Object.DestroyImmediate(existing);

            var go = new GameObject("SdfBlobTest");
            go.transform.position = new Vector3(0f, 1.2f, 0f); // lift it off the ground for the diorama cam

            var rig = go.AddComponent<SdfBlobRig>();
            rig.subdivisions = 9;
            rig.prims.Clear();

            // Palette: three distinct colours so proximity blending is visible where shapes overlap.
            Color bodyCol    = new Color(0.35f, 0.62f, 0.85f); // blue body
            Color headCol     = new Color(0.90f, 0.78f, 0.35f); // warm head (blends teal->gold in the neck)
            Color limbCol     = new Color(0.80f, 0.40f, 0.55f); // pink arms

            // Body sphere.
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "body", localPosition = new Vector3(0f, 0f, 0f),
                radius = 0.55f, halfLength = 0f, color = bodyCol, blendK = 0.30f });

            // Head sphere overlapping the body ~40% (centres 0.6 apart, radii 0.55 + 0.38).
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "head", localPosition = new Vector3(0f, 0.72f, 0f),
                radius = 0.38f, halfLength = 0f, color = headCol, blendK = 0.28f });

            // Two arm capsules angled out from the shoulders (axis = localEuler * up).
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "arm_L", localPosition = new Vector3(-0.45f, 0.12f, 0f),
                localEuler = new Vector3(0f, 0f, 55f),
                radius = 0.15f, halfLength = 0.30f, color = limbCol, blendK = 0.20f });
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "arm_R", localPosition = new Vector3(0.45f, 0.12f, 0f),
                localEuler = new Vector3(0f, 0f, -55f),
                radius = 0.15f, halfLength = 0.30f, color = limbCol, blendK = 0.20f });

            // Thin antenna capsule with a SMALL blend k — expected to dissolve a bit; slice-1 data.
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "antenna", localPosition = new Vector3(0f, 1.15f, 0f),
                radius = 0.05f, halfLength = 0.22f, color = headCol, blendK = 0.06f });

            rig.BuildMesh();

            // Material from the shader (create fresh; MPB carries the per-renderer primitive arrays).
            var mr = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("IdleGame/SdfBlendShell");
            if (shader == null)
            {
                Debug.LogError("[SdfBlobTest] Shader 'IdleGame/SdfBlendShell' not found — did it compile?");
            }
            else
            {
                var mat = new Material(shader);
                Bootstrap.MakeMatte(mat);
                mr.sharedMaterial = mat;
            }
            rig.SetPrimitivesDirty(); // ensure the MPB is pushed onto the freshly-assigned renderer

            var wiggle = go.AddComponent<SdfBlobWiggle>();
            wiggle.enabled = true;

            Selection.activeGameObject = go;
            Debug.Log("[SdfBlobTest] Spawned. Toggle SdfBlobWiggle to watch for vertex-slide shimmer "
                    + "(the KILL CRITERION). Antenna uses a small k — some dissolve is expected.");
        }
    }

    /// <summary>
    /// Sinusoidally bobs the head / arm / antenna child transforms of an <see cref="SdfBlobRig"/> so
    /// parts move WHILE the body stays fused — surfacing slice 1's kill criterion (does the faceted
    /// skin shimmer/vertex-slide as primitives move?). Editor-only test rig: [ExecuteAlways] runs it
    /// in edit mode too, so no Play is required. Disable the component to freeze the pose.
    /// </summary>
    [ExecuteAlways]
    public sealed class SdfBlobWiggle : MonoBehaviour
    {
        [Range(0f, 1f)] public float amplitude = 0.18f;
        [Range(0f, 4f)] public float speed = 1.5f;

        private Transform? _head, _armL, _armR, _antenna;
        private Vector3 _headBase, _armLBase, _armRBase, _antBase;
        private SdfBlobRig? _rig;

        private void OnEnable()
        {
            _rig = GetComponent<SdfBlobRig>();
            _head    = transform.Find("prim:head");
            _armL    = transform.Find("prim:arm_L");
            _armR    = transform.Find("prim:arm_R");
            _antenna = transform.Find("prim:antenna");
            if (_head != null) _headBase = _head.localPosition;
            if (_armL != null) _armLBase = _armL.localPosition;
            if (_armR != null) _armRBase = _armR.localPosition;
            if (_antenna != null) _antBase = _antenna.localPosition;
        }

        private void Update()
        {
            // Editor time (no Play): Time.time still advances in edit mode when repainting, but
            // realtimeSinceStartup is the reliable clock for [ExecuteAlways] wiggles.
            float t = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            float w = t * speed * Mathf.PI * 2f;

            if (_head != null)    _head.localPosition    = _headBase + new Vector3(0f, Mathf.Sin(w) * amplitude * 0.3f, 0f);
            if (_armL != null)    _armL.localPosition    = _armLBase + new Vector3(0f, Mathf.Sin(w + 1.0f) * amplitude, 0f);
            if (_armR != null)    _armR.localPosition    = _armRBase + new Vector3(0f, Mathf.Sin(w - 1.0f) * amplitude, 0f);
            if (_antenna != null) _antenna.localPosition = _antBase  + new Vector3(Mathf.Sin(w * 1.7f) * amplitude * 0.8f, 0f, 0f);

            // The rig pushes on LateUpdate, but edit-mode LateUpdate isn't guaranteed each repaint —
            // push explicitly so the moved children reflect in the MPB immediately.
            if (_rig != null) _rig.SetPrimitivesDirty();
        }
    }
}
