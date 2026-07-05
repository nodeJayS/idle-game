#nullable enable
using IdleGame.Game;
using UnityEditor;
using UnityEngine;

namespace IdleGame.EditorTools
{
    /// <summary>
    /// Tools > SDF Gait Test (ROADMAP 4, slice 2). Spawns (or replaces) a "SdfGaitTest" object with
    /// TWO critters side by side, each an <see cref="SdfBlobRig"/> + <see cref="SdfBlobAnimator"/>:
    /// a two-legged WALKER (swings its legs about the hip) and a no-legged HOPPER (whole-blob arc +
    /// squash). A tiny editor-only <see cref="SdfPacer"/> owns each ROOT and paces it back and forth
    /// so the gaits play without Play mode — this pacer-owns-root / animator-owns-prims split
    /// rehearses the slice-3 CombatView contract.
    ///
    /// WHAT TO WATCH: the walker's legs staying fused at the hip through the full swing (no gap
    /// tearing open at the socket), the hopper staying fused through its landing squash, and NEITHER
    /// critter frustum-popping at the pace extremes (bounds padding must cover the excursions).
    /// </summary>
    public static class SdfGaitTest
    {
        [MenuItem("Tools/SDF Gait Test")]
        public static void Spawn()
        {
            var existing = GameObject.Find("SdfGaitTest");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("SdfGaitTest");

            var shader = Shader.Find("IdleGame/SdfBlendShell");
            if (shader == null)
                Debug.LogError("[SdfGaitTest] Shader 'IdleGame/SdfBlendShell' not found — did it compile?");

            BuildWalker(root.transform, shader);
            BuildHopper(root.transform, shader);

            Selection.activeGameObject = root;
            Debug.Log("[SdfGaitTest] Spawned walker (x -1.5) + hopper (x +1.5). Watch: legs staying "
                    + "fused at the hip through the swing, the hopper staying fused through its "
                    + "landing squash, and no frustum-pop at the pace extremes.");
        }

        // ---- WALKER: two-legged strider --------------------------------------------------------
        private static void BuildWalker(Transform parent, Shader? shader)
        {
            var go = new GameObject("Walker");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-1.5f, 1.2f, 0f); // lift for the diorama cam

            var rig = go.AddComponent<SdfBlobRig>();
            rig.subdivisions = 9;
            // Slice-1 lesson: a swinging limb reaches well past its authored bounds; pad generously
            // for the ±LegSwing excursion or the blob frustum-pops mid-stride.
            rig.boundsPadding = 0.5f;
            rig.prims.Clear();

            Color bodyCol = new Color(0.35f, 0.62f, 0.85f); // blue body
            Color headCol = new Color(0.90f, 0.78f, 0.35f); // warm head
            Color legCol  = new Color(0.55f, 0.45f, 0.70f); // muted violet legs

            // Body sphere.
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "body", localPosition = new Vector3(0f, 0f, 0f),
                radius = 0.5f, halfLength = 0f, color = bodyCol, blendK = 0.30f });

            // Head sphere sitting on the body.
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "head", localPosition = new Vector3(0f, 0.62f, 0f),
                radius = 0.30f, halfLength = 0f, color = headCol, blendK = 0.25f });

            // Two leg capsules. Deliberately CHUBBIER than slice-1's arms (r 0.16 vs 0.15) — the
            // slice-1 lesson was that limbs need generous radius relative to the body or the smin
            // swallows them; a swinging leg that thins is worse. Node position = capsule CENTRE.
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "leg_L", localPosition = new Vector3(-0.24f, -0.45f, 0f),
                radius = 0.16f, halfLength = 0.26f, color = legCol, blendK = 0.16f });
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "leg_R", localPosition = new Vector3(0.24f, -0.45f, 0f),
                radius = 0.16f, halfLength = 0.26f, color = legCol, blendK = 0.16f });

            rig.BuildMesh();
            ApplyMaterial(go, shader);
            rig.SetPrimitivesDirty();

            var anim = go.AddComponent<SdfBlobAnimator>();
            anim.Init(SdfBlobAnimator.Family.Walk, go.GetInstanceID());

            var pacer = go.AddComponent<SdfPacer>();
            pacer.speed = 1.2f; // units/sec
        }

        // ---- HOPPER: no-legged blob ------------------------------------------------------------
        private static void BuildHopper(Transform parent, Shader? shader)
        {
            var go = new GameObject("Hopper");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(1.5f, 1.2f, 0f);

            var rig = go.AddComponent<SdfBlobRig>();
            rig.subdivisions = 9;
            // Pad for the hop height (0.35 * arc-lift) plus the squash margin.
            rig.boundsPadding = 0.6f;
            rig.prims.Clear();

            Color bodyCol = new Color(0.85f, 0.45f, 0.40f); // warm coral body
            Color nubCol  = new Color(0.90f, 0.70f, 0.35f); // gold side nubs

            // One fat body sphere.
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "body", localPosition = new Vector3(0f, 0f, 0f),
                radius = 0.5f, halfLength = 0f, color = bodyCol, blendK = 0.35f });

            // Two small side nubs. Names DON'T match body/head/leg roles, so the animator leaves them
            // at rest in Walk — but Hop translates ALL nodes, so here they hop with the body and stay
            // fused under the whole-blob arc (that's the point: verify fusion through translation).
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "nub_L", localPosition = new Vector3(-0.42f, 0f, 0f),
                radius = 0.18f, halfLength = 0f, color = nubCol, blendK = 0.22f });
            rig.prims.Add(new SdfBlobRig.PrimitiveDef {
                name = "nub_R", localPosition = new Vector3(0.42f, 0f, 0f),
                radius = 0.18f, halfLength = 0f, color = nubCol, blendK = 0.22f });

            rig.BuildMesh();
            ApplyMaterial(go, shader);
            rig.SetPrimitivesDirty();

            var anim = go.AddComponent<SdfBlobAnimator>();
            anim.Init(SdfBlobAnimator.Family.Hop, go.GetInstanceID());

            var pacer = go.AddComponent<SdfPacer>();
            pacer.speed = 1.6f; // units/sec
        }

        /// <summary>Material setup identical to SdfBlobTest: fresh material off the SDF shader, made
        /// matte, assigned as sharedMaterial (the MPB carries the per-renderer primitive arrays).</summary>
        private static void ApplyMaterial(GameObject go, Shader? shader)
        {
            if (shader == null) return;
            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(shader);
            Bootstrap.MakeMatte(mat);
            mr.sharedMaterial = mat;
        }
    }

    /// <summary>
    /// Editor-only pacer (ROADMAP 4, slice 2). Owns the critter's ROOT — paces it back and forth
    /// along x over ±1.2 at <see cref="speed"/>, rotates the root to face its travel direction, and
    /// feeds the sibling <see cref="SdfBlobAnimator"/> every frame (SetMoving + SetMoveSpeed). At
    /// each turnaround it drops SetMoving(false) for a beat so the STOP behaviours show (hopper
    /// finishing its arc + landing squash, walker settling to rest/breathe). This deliberately
    /// mirrors the CombatView contract: the pacer owns the root, the animator owns the prims.
    /// [ExecuteAlways] + the edit-mode clock trick so it runs without Play.
    /// </summary>
    [ExecuteAlways]
    public sealed class SdfPacer : MonoBehaviour
    {
        public float speed = 1.2f;    // units/sec along x
        public float range = 1.2f;    // ± travel from the spawn x
        public float pauseSec = 0.6f; // stop for a beat at each turnaround (shows stop behaviours)

        private SdfBlobAnimator? _anim;
        private Vector3 _center;
        private float _lastT;
        private float _travel;        // signed offset from center
        private int _dir = 1;
        private float _pauseT;

        private void OnEnable()
        {
            _anim = GetComponent<SdfBlobAnimator>();
            _center = transform.position;
            _lastT = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

            // Edit-mode smoothness: [ExecuteAlways] Update only runs when the editor repaints, and
            // edit-mode repaints are EVENT-driven (mouse over a view etc.) — so the gait advances in
            // irregular bursts and reads choppy. EditorApplication.update ticks continuously; pump
            // the player loop + scene view from it so the test animates at editor framerate. Play
            // mode has a real loop and needs none of this.
            if (!Application.isPlaying) EditorApplication.update += EditorTick;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) EditorApplication.update -= EditorTick;
        }

        private static void EditorTick()
        {
            EditorApplication.QueuePlayerLoopUpdate(); // runs Update/LateUpdate on ExecuteAlways scripts
            SceneView.RepaintAll();                    // and actually redraw what they posed
        }

        private void Update()
        {
            float now = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            float dt = now - _lastT;
            if (dt < 0f) dt = 0f; // guard a non-monotonic edit clock
            _lastT = now;

            if (_pauseT > 0f)
            {
                // Turnaround beat: hold still so the animator's stop behaviours play out.
                _pauseT = Mathf.Max(0f, _pauseT - dt);
                if (_anim != null) { _anim.SetMoving(false); _anim.SetMoveSpeed(speed); }
                return;
            }

            _travel += _dir * speed * dt;
            if (_travel >= range)  { _travel = range;  _dir = -1; _pauseT = pauseSec; }
            if (_travel <= -range) { _travel = -range; _dir = 1;  _pauseT = pauseSec; }

            transform.position = _center + new Vector3(_travel, 0f, 0f);
            // Face travel direction: +x dir looks +x (yaw +90), -x dir looks -x (yaw -90).
            transform.rotation = Quaternion.Euler(0f, _dir > 0 ? 90f : -90f, 0f);

            if (_anim != null) { _anim.SetMoving(true); _anim.SetMoveSpeed(speed); }
        }
    }
}
