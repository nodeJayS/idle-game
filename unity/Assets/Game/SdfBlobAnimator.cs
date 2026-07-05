#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Procedural gait for an SDF blob (ROADMAP 4, slice 2). Where <see cref="MonsterAnimator"/>
    /// hangs a rigid faceted model off ONE body pivot, this drives the individual prim:* child
    /// nodes of a sibling <see cref="SdfBlobRig"/> — so a strider actually swings its legs and a
    /// blob squashes on landing, all inside the fused SDF shell. Client-only tech-art; GameCore has
    /// no idea this exists.
    ///
    /// The PIVOT CONTRACT (copied from MonsterAnimator) is load-bearing: a future CombatView OWNS
    /// this component's ROOT transform (position for the Lerp/lunge/knock, rotation for facing,
    /// scale for rank/spawn). So this component NEVER touches its own root transform — it poses ONLY
    /// the prim:* child nodes, each RELATIVE TO ITS CAPTURED REST POSE. The public API
    /// (<see cref="SetMoving"/> / <see cref="SetMoveSpeed"/>) mirrors MonsterAnimator/IHeroAnim so
    /// CombatView can feed both animator kinds through one shape in slice 3.
    ///
    /// REBIND caveat: <see cref="SdfBlobRig.BuildMesh"/> DESTROYS AND RECREATES the prim nodes, so
    /// cached Transforms go stale across a rebuild. We re-resolve them by name (transform.Find
    /// "prim:<name>") and re-capture rest pose in <see cref="OnEnable"/>, and expose
    /// <see cref="Rebind"/> so the editor tool can refresh after a BuildMesh.
    ///
    /// [ExecuteAlways] so the editor gait test runs without entering Play (edit-mode clock trick
    /// from SdfBlobWiggle: Application.isPlaying ? Time.time : Time.realtimeSinceStartup).
    /// </summary>
    [ExecuteAlways]
    public sealed class SdfBlobAnimator : MonoBehaviour, IMonsterAnim
    {
        public enum Family { Walk, Hop, Float }

        // ---- tunables (subtle — cozy ortho 45° diorama, mirrors MonsterAnimator amplitudes) ----
        private const float StrideBob      = 0.05f;  // body/head double-bounce bob amplitude (world units)
        private const float StridePitchDeg = 4f;     // forward body lean while walking
        private const float LegSwingDeg    = 30f;    // peak fore/aft leg swing about the hip
        private const float HopHeight      = 0.35f;  // peak of a hop arc (world units, pre root-scale)
        private const float HopLength      = 0.9f;   // ground distance one hop covers (sets cadence vs speed)
        private const float HopLandSec     = 0.12f;  // final-touchdown squash spring-back (hop stop)
        private const float HopSquashScale = 0.10f;  // uniform node shrink at touchdown (radius dip)
        private const float HopSquashY     = 0.25f;  // node local-Y compression toward the base at touchdown
        private const float BreatheAmp     = 0.02f;  // idle breathing y-position pulse of body/head (world units)
        private const float BreatheHz      = 0.5f;   // idle breathing frequency
        private const float FloatHover     = 0.15f;  // continuous whole-blob hover amplitude (world units)
        private const float FloatHz        = 0.55f;  // hover frequency (mirrors MonsterAnimator.PoseFloat)

        // Attack telegraph (crouch then punch-out) and hit reaction (squash spring-back). A single
        // per-prim radius squash is impossible (the SDF carries one radius) — see MonsterAnimator's
        // note; we FAKE both via NODE local-Y instead of a per-axis scale.
        private const float AtkCrouchY   = 0.18f;  // body/head local-Y compression at the anticipation low
        private const float AtkStretchY  = 0.14f;  // body/head local-Y lift at the punch-out peak
        private const float HitSquashSec = 0.16f;  // hit squash spring-back time (mirrors MonsterAnimator)
        private const float HitSquashY   = 0.20f;  // peak body/head local-Y compression on a hit
        private const float FlashSec     = 0.12f;  // hit emission-flash duration (mirrors MonsterAnimator)

        // Death: the POOF (blobs never topple). Mirrors MonsterAnimator's Hop/Float death branch,
        // INCLUDING the ownership flip — after Die the animator owns the whole detached object and
        // may drive ROOT scale/position (CombatView has detached the view from the live set).
        private const float DieLife    = 0.5f;   // corpse lingers long enough for a projectile to land
        private const float DieInflate = 1.15f;  // brief inflate before the shrink
        private const float DieWaft    = 0.6f;   // upward drift over the death (world units, pre root-scale)

        // ---- one animated prim node + its captured rest pose ----
        // Rest pose is the AUTHORED local transform; every frame we pose relative to it and never
        // drift. halfLength comes from the matching PrimitiveDef (a node alone can't report it).
        private sealed class Node
        {
            public Transform t = null!;
            public Vector3 restPos;
            public Quaternion restRot;
            public Vector3 restScale;
            public float halfLength;   // capsule half-length from the def (0 for spheres)
        }

        private SdfBlobRig? _rig;
        private readonly List<Node> _bodyNodes = new(); // "body"/"head" — bob + breathe
        private readonly List<Node> _legNodes  = new(); // names starting "leg" — swing about the hip
        private readonly List<Node> _allNodes  = new(); // every prim node — Hop translates ALL of them

        [SerializeField] private Family _family = Family.Walk;

        private float _phase;        // per-instance offset so packs don't gait in lockstep
        private float _t;            // seconds alive (drives the continuous gaits)
        private bool _moving;
        private float _speed;        // ground units/sec, sticky between sim steps
        private float _stridePhase;  // accumulated stride phase (radians) — accumulate, don't rescale _t
        private float _hopCycle;     // hops elapsed; fraction = progress through the current arc
        private float _hopLandT;     // final-touchdown squash timer (hop stop, item feel)

        // Attack telegraph: a crouch during the lunge's anticipation, a stretch on the punch-out.
        private float _atkT, _atkDur;
        // Hit squash: a quick body/head local-Y flatten that springs back.
        private float _hitT;

        // Emission flash THROUGH THE RIG: cache the rig's PRIOR persistent emission (a rank/mod
        // tell set by CombatView) so we restore EXACTLY that after the flash, never black. Only
        // snapshot when no flash is in flight — a second hit inside the flash window would otherwise
        // cache the mid-flash whitened colour and latch the blob permanently glowing (same bug
        // MonsterAnimator's _emCache guards).
        private bool _flashing;
        private float _flashT;
        private Color _emCache = Color.black;

        // Death: the animator owns the WHOLE detached object once CombatView calls Die, runs the
        // poof out on the ROOT transform, then self-destructs.
        private bool _dying;
        private float _dieT;
        private Vector3 _rootBaseScale = Vector3.one; // root scale captured at death for the final shrink
        private Vector3 _rootBasePos;                 // root position captured at death (for the upward waft)

        /// <summary>Wire the animator to a family + phase seed. <paramref name="phaseSeed"/> is a
        /// hash of the entity id so packs of the same critter don't gait in lockstep. The seed is
        /// AVALANCHED exactly like MonsterAnimator.Init: sequential ids ("E41","E43",…) hash near-
        /// linearly, so raw hash % 1000 leaves a whole spawn wave phases ~0.01 rad apart (visible
        /// lockstep). One xorshift-multiply round decorrelates neighbours completely.</summary>
        public void Init(Family family, int phaseSeed)
        {
            _family = family;
            uint h = (uint)phaseSeed;
            h ^= h >> 16; h *= 0x45d9f3bu; h ^= h >> 16;
            _phase = (h % 1000u) / 1000f * Mathf.PI * 2f;
            Rebind();
        }

        /// <summary>Every frame from CombatView: whether the critter is walking. Mirrors
        /// IHeroAnim/MonsterAnimator.SetMoving so both animator kinds share one feed shape.</summary>
        public void SetMoving(bool moving) => _moving = moving;

        /// <summary>Only on sim steps WHILE moving (mirrors MonsterAnimator.SetMoveSpeed): the real
        /// ground speed (units/sec). STICKY between steps on purpose — the render frames between sim
        /// steps must never see a 0, or hop cadence (speed/HopLength) and stride frequency whipsaw
        /// every other frame.</summary>
        public void SetMoveSpeed(float speed) => _speed = Mathf.Max(0f, speed);

        /// <summary>Anticipation for the capsule lunge (mirrors MonsterAnimator.TriggerAttack): a
        /// quick crouch in the first ~30% of the lunge, then a stretch on the punch-out. Because a
        /// per-axis prim squash is impossible, this poses body/head NODE local-Y (crouch = compress
        /// toward the base, stretch = lift) rather than scaling. Never overrides an in-flight death.
        /// <paramref name="lungeDur"/> matches CombatView's positional lunge.</summary>
        public void TriggerAttack(float lungeDur)
        {
            if (_dying) return;
            _atkDur = Mathf.Max(0.06f, lungeDur);
            _atkT = _atkDur;
        }

        /// <summary>A hit landed (mirrors MonsterAnimator.TriggerHit): a quick body/head squash that
        /// springs back + an emission flash pushed THROUGH THE RIG. Never overrides a death. Snapshot
        /// the rig's prior emission ONLY when no flash is in flight, so a second hit inside the window
        /// (splash/chain) can't cache the whitened mid-flash colour and latch the blob glowing.</summary>
        public void TriggerHit()
        {
            if (_dying || _rig == null) return;
            _hitT = HitSquashSec;
            if (!_flashing)
            {
                _emCache = _rig.Emission; // the persistent rank/mod tell to restore to
                _flashing = true;
            }
            _flashT = FlashSec;
        }

        /// <summary>The killing blow (mirrors MonsterAnimator's Hop/Float poof branch): the animator
        /// now owns the whole detached GameObject — CombatView has removed the view from the live set,
        /// so driving ROOT scale/position is safe here (and NOWHERE else). Blobs always POOF (inflate
        /// then shrink to nothing with a small upward waft), then Destroy(gameObject).
        /// <paramref name="hitDir"/> is unused — a blob has no corpse to tip toward the blow.</summary>
        public void Die(Vector3 hitDir)
        {
            if (_dying) return;
            _dying = true;
            _dieT = 0f;
            _rootBaseScale = transform.localScale; // rank size scale is baked in here; poof scales it
            _rootBasePos = transform.position;
            _flashing = false; // the death owns the look now; let any restore drop
        }

        private void OnEnable()
        {
            Rebind();
            // Baseline the clock NOW or the first Update's dt spans everything since editor/game
            // start (a giant one-frame phase jump). Same pattern as SdfPacer.OnEnable.
            _t = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        }

        /// <summary>(Re)resolve the prim:* nodes and re-capture their rest pose. Call after
        /// <see cref="SdfBlobRig.BuildMesh"/> (which destroys+recreates the nodes) so cached
        /// Transforms don't dangle. Roles are found BY NAME, never by index: "body"/"head" bob,
        /// names starting "leg" swing, anything else just rides along (Hop translates all).</summary>
        public void Rebind()
        {
            _rig = GetComponent<SdfBlobRig>();
            _bodyNodes.Clear();
            _legNodes.Clear();
            _allNodes.Clear();
            if (_rig == null) return;

            foreach (var pd in _rig.prims)
            {
                var t = transform.Find("prim:" + pd.name);
                if (t == null) continue;
                // Rest pose from the AUTHORED DEF, never the live node: OnEnable re-runs on every
                // domain reload (= every compile) and the scene nodes keep whatever mid-gait pose
                // they were left at — capturing the node would bake a lifted/swung pose in as
                // "rest" forever. BuildMesh creates nodes at exactly this def pose with unit scale.
                var node = new Node
                {
                    t = t,
                    restPos = pd.localPosition,
                    restRot = Quaternion.Euler(pd.localEuler),
                    restScale = Vector3.one,
                    halfLength = pd.halfLength,
                };
                _allNodes.Add(node);

                string nm = pd.name;
                if (nm == "body" || nm == "head") _bodyNodes.Add(node);
                else if (nm.StartsWith("leg")) _legNodes.Add(node);
                // Unknown prims (nubs/antennae) keep rest pose in Walk; in Hop they still ride the
                // whole-blob arc because _allNodes holds every node.
            }
        }

        private void Update()
        {
            if (_rig == null) return;
            // Edit-mode clock trick: realtimeSinceStartup is the reliable [ExecuteAlways] clock when
            // not in Play (Time.deltaTime can be 0 between edit-mode repaints).
            float now = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            float dt = now - _t <= 0f ? 0f : now - _t; // guard against a non-monotonic edit clock
            _t = now;

            if (_dying) { UpdateDeath(dt); return; }

            UpdateFlash(dt);

            switch (_family)
            {
                case Family.Walk:  PoseWalk(dt);  break;
                case Family.Hop:   PoseHop(dt);   break;
                case Family.Float: PoseFloat();   break;
            }

            // Attack telegraph + hit squash layer ON TOP of the gait pose, both faked via body/head
            // NODE local-Y (a per-axis prim squash is impossible). Applied after the gait set the
            // rest-relative pose this frame, so they compose as an additional Y offset.
            LayerBodySquash(dt);

            // The rig pushes on LateUpdate, but edit-mode LateUpdate isn't guaranteed each repaint —
            // push explicitly so the posed children reflect in the MPB immediately (SdfBlobWiggle
            // does the same).
            _rig.SetPrimitivesDirty();
        }

        // ---- Float: continuous whole-blob hover -----------------------------------------------

        /// <summary>Float: a continuous slow hover of the WHOLE blob (every prim node lifts by the
        /// same y sine, phase-offset) even when idle — mirrors MonsterAnimator.PoseFloat. It never
        /// rests, so NO landing squash and NO breathing ever. Banking is skipped: a gentle roll on
        /// sphere prims can't read (the SDF carries no orientation), so we don't fake it.</summary>
        private void PoseFloat()
        {
            float lift = Mathf.Sin(_t * FloatHz * Mathf.PI * 2f + _phase) * FloatHover;
            foreach (var n in _allNodes)
            {
                var p = n.restPos;
                p.y += lift;
                n.t.localPosition = p;
                n.t.localRotation = n.restRot;
                n.t.localScale = n.restScale;
            }
        }

        // ---- attack / hit squash (layered onto the gait) --------------------------------------

        /// <summary>Layer the attack crouch/stretch and the hit squash onto the body/head nodes as
        /// an extra local-Y offset. Both fake a per-axis flatten the single-radius SDF can't do:
        /// crouch/hit COMPRESS local Y toward the base, the punch-out LIFTS it. Timers decay here so
        /// they run regardless of which gait posed the frame.</summary>
        private void LayerBodySquash(float dt)
        {
            float dy = 0f;

            if (_atkT > 0f)
            {
                _atkT = Mathf.Max(0f, _atkT - dt);
                float p = 1f - (_atkDur > 0f ? _atkT / _atkDur : 1f); // 0 -> 1 over the lunge
                // Match MonsterAnimator's 0.3/0.7 phase split: crouch for the first 30%, stretch out.
                dy += p < 0.3f ? Mathf.Lerp(0f, -AtkCrouchY, p / 0.3f)
                               : Mathf.Lerp(-AtkCrouchY, AtkStretchY, (p - 0.3f) / 0.7f);
            }

            if (_hitT > 0f)
            {
                _hitT = Mathf.Max(0f, _hitT - dt);
                float k = _hitT / HitSquashSec;        // 1 -> 0
                float e = Mathf.Sin(k * Mathf.PI);     // 0 at the ends, 1 mid: a spring
                dy += -HitSquashY * e;
            }

            if (dy == 0f) return;
            foreach (var n in _bodyNodes)
            {
                var p = n.t.localPosition; // compose on whatever the gait already set this frame
                p.y += dy;
                n.t.localPosition = p;
            }
        }

        // ---- hit emission flash (through the rig) ---------------------------------------------

        /// <summary>Drive the hit flash to white then back to the rig's CACHED prior emission (a
        /// rank/mod tell), all via SdfBlobRig.SetEmission — same restore-to-prior contract as
        /// MonsterAnimator.UpdateFlash, so a flash never wipes a rank glow.</summary>
        private void UpdateFlash(float dt)
        {
            if (!_flashing || _rig == null) return;
            _flashT -= dt;
            float k = Mathf.Clamp01(_flashT / FlashSec); // 1 -> 0
            _rig.SetEmission(Color.Lerp(_emCache, Color.white, k));
            if (_flashT <= 0f)
            {
                _rig.SetEmission(_emCache); // restore EXACTLY the prior tell (never black)
                _flashing = false;
            }
        }

        // ---- death (poof) — owns the ROOT once CombatView detaches the view --------------------

        /// <summary>Poof: a brief inflate then a shrink toward nothing with a small upward waft,
        /// driven on the ROOT transform (safe now — the view is detached). Mirrors MonsterAnimator's
        /// Hop/Float death branch; self-destructs when the linger elapses.</summary>
        private void UpdateDeath(float dt)
        {
            _dieT += dt;
            float a = Mathf.Clamp01(_dieT / DieLife);
            float scale = a < 0.25f ? Mathf.Lerp(1f, DieInflate, a / 0.25f)       // inflate
                                    : Mathf.Lerp(DieInflate, 0.02f, (a - 0.25f) / 0.75f); // shrink to ~nothing
            transform.localScale = _rootBaseScale * scale;
            transform.position = _rootBasePos + Vector3.up * (DieWaft * a);        // waft upward
            if (_rig != null) _rig.SetPrimitivesDirty();
            if (a >= 1f) Destroy(gameObject);
        }

        // ---- Walk: 2-legged strider ------------------------------------------------------------

        /// <summary>Walk: legs swing fore/aft about the hip (phase-opposed), body+head double-bounce
        /// bob with a small forward pitch on the body while moving. Phase ACCUMULATES from speed
        /// (stridesPerSec = speed/1.4 like MonsterAnimator) — never rescale total elapsed time, or a
        /// speed change would jump the pose. Idle → gentle breathing (below).</summary>
        private void PoseWalk(float dt)
        {
            if (!_moving)
            {
                foreach (var n in _legNodes) RestNode(n);
                Breathe();
                return;
            }

            float stridesPerSec = _speed / 1.4f;                 // ~1.4u per stride at this scale
            _stridePhase += dt * stridesPerSec * Mathf.PI * 2f;  // ACCUMULATE (see summary)
            float sPhase = _stridePhase + _phase;

            // Legs: swing about the HIP (the top of the capsule), not the node centre. The authored
            // node position is the capsule CENTRE, so a bare rotation spins the leg about its middle
            // and the top would slide out of the hip socket. Fix: keep the hip fixed and orbit the
            // centre below it. Rest hip = restPos + restRot*up*halfLength (capsule axis = rot*up).
            // After a swing rotation, the new axis is (swing*restRot)*up, so the centre that keeps
            // the hip planted is hip + (swing*restRot)*down*halfLength.
            for (int i = 0; i < _legNodes.Count; i++)
            {
                var n = _legNodes[i];
                float legSign = (i % 2 == 0) ? 0f : Mathf.PI;    // legs phase-opposed (L vs R)
                float swingDeg = Mathf.Sin(sPhase + legSign) * LegSwingDeg;
                var swing = Quaternion.Euler(swingDeg, 0f, 0f);

                Vector3 hip = n.restPos + n.restRot * (Vector3.up * n.halfLength);
                Quaternion newRot = swing * n.restRot;
                n.t.localRotation = newRot;
                n.t.localPosition = hip + newRot * (Vector3.down * n.halfLength);
                n.t.localScale = n.restScale;
            }

            // Body + head: double-bounce bob (|sin(2·phase)| — two lifts per stride) plus a small
            // forward pitch on the BODY node while moving, mirroring PoseStride's amplitudes.
            float bob = Mathf.Abs(Mathf.Sin(sPhase)) * StrideBob;
            float pitch = StridePitchDeg * Mathf.Clamp01(_speed / 3f);
            foreach (var n in _bodyNodes)
            {
                n.t.localPosition = n.restPos + new Vector3(0f, bob, 0f);
                bool isBody = n.t.name == "prim:body";
                n.t.localRotation = isBody ? Quaternion.Euler(pitch, 0f, 0f) * n.restRot : n.restRot;
                n.t.localScale = n.restScale;
            }
        }

        // ---- Hop: no-legged blob ---------------------------------------------------------------

        /// <summary>Hop: the WHOLE blob arcs off the ground — every prim node translates up by the
        /// same arc, so nubs/antennae hop with the body and stay fused. Cadence = speed/HopLength
        /// exactly like MonsterAnimator.PoseHop, INCLUDING the stop-mid-air rule (finish the arc at
        /// the sticky cadence, land, then rest) and the landing squash.
        ///
        /// SQUASH TRADEOFF: the SDF only carries a single radius per prim, so a true per-axis sphere
        /// squash is impossible. We FAKE the flatten with (a) a slight uniform node shrink into the
        /// landing (radius dip) AND (b) compressing each node's local Y toward the blob base — the
        /// position compression is what actually reads as "the blob flattens on impact".</summary>
        private void PoseHop(float dt)
        {
            bool midHop = Mathf.Repeat(_hopCycle, 1f) > 0.0001f;
            float arc;   // 0 on the ground, 1 at apex
            float land;  // 1 on touchdown, 0 at apex — drives the squash

            if (_moving && _speed >= 0.05f)
            {
                _hopCycle += dt * (_speed / HopLength);
                arc = Mathf.Abs(Mathf.Sin(Mathf.Repeat(_hopCycle, 1f) * Mathf.PI));
                land = 1f - arc;
            }
            else if (midHop)
            {
                // stopped mid-air: keep the arc going at the last (sticky) cadence until the next
                // integer touchdown, then rest with a landing squash (never teleport an apex blob
                // straight to the ground).
                float next = Mathf.Ceil(_hopCycle);
                _hopCycle += dt * (Mathf.Max(_speed, 0.5f) / HopLength);
                if (_hopCycle >= next) { _hopCycle = 0f; _hopLandT = HopLandSec; }
                arc = Mathf.Abs(Mathf.Sin(Mathf.Repeat(_hopCycle, 1f) * Mathf.PI));
                land = 1f - arc;
            }
            else
            {
                // Grounded + resting: run out the final-touchdown squash, then breathe.
                _hopCycle = 0f;
                if (_hopLandT > 0f)
                {
                    _hopLandT = Mathf.Max(0f, _hopLandT - dt);
                    float e = _hopLandT / HopLandSec; // 1 -> 0
                    ApplyHop(0f, e);
                    return;
                }
                Breathe();
                return;
            }

            ApplyHop(arc, land);
        }

        /// <summary>Translate every node up by the shared arc and apply the faked squash on landing.
        /// arc = 0..1 (height), land = 0..1 (squash strength).</summary>
        private void ApplyHop(float arc, float land)
        {
            float lift = arc * HopHeight;
            float yCompress = 1f - HopSquashY * land;   // pull local Y toward the base
            float shrink = 1f - HopSquashScale * land;  // slight uniform radius dip
            foreach (var n in _allNodes)
            {
                Vector3 p = n.restPos;
                p.y = p.y * yCompress + lift;           // compress toward base, then lift the whole blob
                n.t.localPosition = p;
                n.t.localRotation = n.restRot;
                n.t.localScale = n.restScale * shrink;
            }
        }

        // ---- shared helpers --------------------------------------------------------------------

        /// <summary>Idle breathing for BOTH families when standing still: a slow y-POSITION pulse of
        /// the body/head nodes (phase-offset), NOT a scale change — scaling a node changes its SDF
        /// radius, which would visibly inflate/deflate the blob rather than breathe it.</summary>
        private void Breathe()
        {
            float w = _t * BreatheHz * Mathf.PI * 2f + _phase;
            for (int i = 0; i < _bodyNodes.Count; i++)
            {
                var n = _bodyNodes[i];
                float off = Mathf.Sin(w + i * 0.6f) * BreatheAmp; // phase-offset body vs head
                n.t.localPosition = n.restPos + new Vector3(0f, off, 0f);
                n.t.localRotation = n.restRot;
                n.t.localScale = n.restScale;
            }
            // Non-body/head nodes (legs, nubs) rest while idle.
            foreach (var n in _legNodes) RestNode(n);
        }

        private static void RestNode(Node n)
        {
            n.t.localPosition = n.restPos;
            n.t.localRotation = n.restRot;
            n.t.localScale = n.restScale;
        }
    }
}
