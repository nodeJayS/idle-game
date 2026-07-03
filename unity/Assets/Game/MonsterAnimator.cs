#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Procedural "life" for the rigid faceted monster models (art rule: monsters are ONE
    /// flat-shaded group, no rig — the smooth MS2 pipeline is heroes-only). With no bones to
    /// pose, we can't play clips; instead we animate a single BODY PIVOT the model hangs from,
    /// so a slime hops, a wolf strides, a wisp floats, a golem stomps — all by squashing/
    /// bobbing/tilting that one transform.
    ///
    /// The PIVOT TRICK is load-bearing: CombatView OWNS the root transform. SyncViews writes
    /// root.position EVERY frame (Lerp + lunge + knock offsets), the spawn anim scales the
    /// ROOT, and rank size bakes into the root's BaseScale; facing rotates the ROOT. So this
    /// component must never touch root position/scale, nor rotate the root about its origin.
    /// On <see cref="Init"/> it re-parents all of the prefab's children under a new "Body"
    /// child at local origin and animates ONLY that child (local Y bounce, local squash/
    /// stretch scale, local tilt). That composes cleanly with everything the root does.
    ///
    /// FAMILY is a CLIENT-SIDE cosmetic grouping — GameCore has no family field and must not
    /// gain one. The static <see cref="Families"/> table maps monster id (= MonsterDef.Id =
    /// the fbx name = CombatEntity.RefId) to a gait; unknown ids fall back to Stride.
    /// </summary>
    public sealed class MonsterAnimator : MonoBehaviour
    {
        public enum Family { Hop, Stride, Float, Stomp }

        // ---- family table (client-side cosmetic; keyed by monster id / fbx name) ----
        // Verified against the zone rosters in GameConfig.cs (Monsters[...] + Zones): all 28
        // ids are real, none missing. Grouping is by silhouette/flavour, not by sim role.
        private static readonly Dictionary<string, Family> Families = new()
        {
            // Hop — bouncy blobs that arc off the ground.
            { "slime", Family.Hop }, { "bog_toad", Family.Hop }, { "magma_imp", Family.Hop },
            { "ice_sprite", Family.Hop }, { "dust_scarab", Family.Hop }, { "chaos_spawn", Family.Hop },

            // Stride — legged walkers with a double-bounce gait and a bit of roll.
            { "goblin", Family.Stride }, { "goblin_king", Family.Stride }, { "bone_rattler", Family.Stride },
            { "grave_knight", Family.Stride }, { "frost_wolf", Family.Stride }, { "cinder_hound", Family.Stride },
            { "dune_stalker", Family.Stride }, { "tide_crab", Family.Stride }, { "tempest_naga", Family.Stride },
            { "riftwalker", Family.Stride },

            // Float — hover with no ground contact; bank into turns, never land-squash.
            { "marsh_wisp", Family.Float }, { "void_wisp", Family.Float }, { "gloom_shade", Family.Float },
            { "cave_bat", Family.Float }, { "storm_caller", Family.Float }, { "crown_seraph", Family.Float },

            // Stomp — slow heavies with a felt single-beat footfall.
            { "stone_sentry", Family.Stomp }, { "glacier_golem", Family.Stomp }, { "rune_construct", Family.Stomp },
            { "ash_tyrant", Family.Stomp }, { "bog_horror", Family.Stomp }, { "dune_wurm", Family.Stomp },
            { "nightmare_maw", Family.Stomp }, { "world_ender", Family.Stomp },
        };

        public static Family FamilyOf(string monsterId) =>
            Families.TryGetValue(monsterId, out var f) ? f : Family.Stride;

        // ---- tunables (subtle — cozy ortho 45° diorama, not a cartoon) ----
        private const float HopHeight = 0.35f;     // peak of a hop arc (world units, pre root-scale)
        private const float HopLength = 0.9f;      // ground distance one hop covers (sets cadence vs speed)
        private const float StrideBob = 0.06f;     // vertical bob amplitude of a stride
        private const float StrideRollDeg = 3f;    // z-tilt sway at the extremes of a stride
        private const float StridePitchDeg = 4f;   // forward lean while striding
        private const float FloatHover = 0.15f;    // idle hover sine amplitude (continuous)
        private const float FloatHz = 0.55f;       // hover frequency
        private const float FloatBankDeg = 8f;     // roll into a turn
        private const float StompBob = 0.10f;      // heavy single-beat lift height (slams to ground at the beat)
        private const float StompHz = 1.4f;        // footfalls per second while moving
        private const float StompPitchDeg = 3f;    // body pitch on the footfall
        private const float BreatheAmp = 0.03f;    // idle breathing scale (±%)
        private const float BreatheHz = 0.5f;      // idle breathing frequency
        private const float FlashSec = 0.12f;      // hit emission-flash duration
        private const float HitSquashSec = 0.16f;  // hit squash spring-back time
        private const float HopLandSec = 0.12f;    // final-touchdown squash spring-back (hop stop)

        // ---- state ----
        private Transform _body = null!;           // the animated pivot (children re-parented here)
        private Family _family;
        private float _phase;                      // per-instance offset so a pack doesn't bob in unison
        private float _t;                          // seconds alive (drives the continuous gaits)
        private bool _moving;
        private float _speed;                      // ground units/sec, sticky between sim steps
        private float _hopCycle;                   // hops elapsed; fraction = progress through the current arc
        private float _hopLandT;                   // final-touchdown squash timer (hop stop, item feel)
        private float _stridePhase;                // accumulated stride phase (radians) — accumulate, don't
                                                   // rescale _t, or a speed change jumps the phase
        private float _stompPhase;                 // accumulated stomp beats (same accumulation rule)
        private float _prevRootYaw;                // to detect turn rate for Float banking

        // Attack telegraph: a crouch during the lunge's anticipation, a stretch on punch-out.
        private float _atkT, _atkDur;

        // Hit squash: a quick y-flatten that springs back.
        private float _hitT;

        // Emission flash: cache each material's PRIOR emission (rank/mod tells set a persistent
        // _EmissionColor via MonsterModel.Tint) so we restore EXACTLY that, never black.
        private Material[] _mats = System.Array.Empty<Material>();
        private Color[] _emCache = System.Array.Empty<Color>();
        private bool _flashing;
        private float _flashT;

        // Death: owns the WHOLE object once the view is detached from the live set.
        private bool _dying;
        private float _dieT;
        private const float DieLife = 0.5f;        // corpse lingers long enough for a projectile to land
        private bool _toppleStyle;                 // topple (Stride/Stomp) vs poof (Hop/Float)
        private Vector3 _dieAxis;                  // world axis to pitch about (topple)
        private float _dieYaw;                     // yaw at death — cached ONCE; reading eulerAngles.y
                                                   // mid-topple is unstable near 90° pitch (gimbal)
        private Vector3 _rootBaseScale;            // root scale captured at death for the final shrink

        /// <summary>Wire the animator to a freshly-built model. <paramref name="phaseSeed"/> is a
        /// hash of the entity id so packs of the same monster don't pulse in lockstep.</summary>
        public void Init(string monsterId, int phaseSeed)
        {
            _family = FamilyOf(monsterId);
            // Avalanche the seed first: sequential entity ids ("E41","E43",…) hash near-
            // LINEARLY, so raw hash % 1000 handed a whole spawn wave phases ~0.01 rad apart
            // and the pack hovered in visible lockstep (caught in Play 2026-07-03). One
            // xorshift-multiply round decorrelates neighbours completely.
            uint h = (uint)phaseSeed;
            h ^= h >> 16; h *= 0x45d9f3bu; h ^= h >> 16;
            _phase = (h % 1000u) / 1000f * Mathf.PI * 2f;

            // Build the Body pivot at the root's origin and re-parent every existing child under
            // it. worldPositionStays:false keeps their local offsets, so the model looks identical
            // — we've just inserted one animatable joint between root and geometry.
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            var children = new List<Transform>();
            foreach (Transform c in transform) if (c != _body) children.Add(c);
            foreach (var c in children) c.SetParent(_body, false);

            // Cache materials + their current emission for the hit flash (see field comment).
            var mats = new List<Material>();
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    mats.Add(m);
            _mats = mats.ToArray();
            _emCache = new Color[_mats.Length];

            _prevRootYaw = transform.eulerAngles.y;
        }

        /// <summary>Every frame from SyncViews: whether the monster is walking. Mirrors
        /// IHeroAnim.SetMoving so both animator kinds share one feed shape.</summary>
        public void SetMoving(bool moving) => _moving = moving;

        /// <summary>Only on sim steps WHILE moving (mirrors IHeroAnim.SetMoveSpeed): the real
        /// ground speed (units/sec). STICKY between steps on purpose — the 60fps render frames
        /// between 30Hz sim steps must never see a 0, or hop cadence (speed/HopLength) and
        /// stride frequency whipsaw every other frame.</summary>
        public void SetMoveSpeed(float speed) => _speed = Mathf.Max(0f, speed);

        /// <summary>Anticipation for the capsule lunge: a quick crouch in the first ~30% of the
        /// lunge, then a stretch on the punch-out. Body language only — the positional lunge stays
        /// on the root, owned by CombatView. <paramref name="lungeDur"/> matches that lunge.</summary>
        public void TriggerAttack(float lungeDur)
        {
            _atkDur = Mathf.Max(0.06f, lungeDur);
            _atkT = _atkDur;
        }

        /// <summary>A hit landed: brief emission flash on every material + a quick squash that
        /// springs back. Never overrides an in-flight death.</summary>
        public void TriggerHit()
        {
            if (_dying) return;
            _hitT = HitSquashSec;
            // Snapshot the current emission ONLY when no flash is in flight — a second hit
            // inside the flash window (splash/chain, two heroes on one target) would otherwise
            // cache the mid-flash whitened colour, and the restore would latch the monster
            // permanently glowing. The retrigger still resets the timers below for the feel.
            if (!_flashing)
            {
                for (int i = 0; i < _mats.Length; i++)
                {
                    var m = _mats[i];
                    _emCache[i] = m != null && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
                }
                _flashing = true;
            }
            _flashT = FlashSec;
        }

        /// <summary>The killing blow: the animator now owns the whole detached GameObject and runs
        /// the death out, then self-destructs. Two styles by family: Stride/Stomp topple (pitch
        /// ~90° away from the hit), Hop/Float poof (inflate then shrink up to nothing).
        /// <paramref name="hitDir"/> is the world direction of the killing blow.</summary>
        public void Die(Vector3 hitDir)
        {
            if (_dying) return;
            _dying = true;
            _dieT = 0f;
            _rootBaseScale = transform.localScale;
            _dieYaw = transform.eulerAngles.y; // BEFORE any tumble — safe to decompose while upright
            _toppleStyle = _family == Family.Stride || _family == Family.Stomp;

            // Topple pitches the body FORWARD along the hit direction; the rotation axis is
            // perpendicular to it on the ground plane (right-hand rule → falls away from the blow).
            var flat = new Vector3(hitDir.x, 0f, hitDir.z);
            if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
            flat.Normalize();
            _dieAxis = Vector3.Cross(Vector3.up, flat); // horizontal axis to tumble about
            _flashing = false; // let any restore happen; the death owns the look now
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_dying) { UpdateDeath(dt); return; }

            _t += dt;
            UpdateFlash(dt);

            // Compose the body pose from gait + telegraph + hit squash. Start neutral each frame.
            float bobY = 0f;
            float pitch = 0f, roll = 0f;   // degrees
            var scale = Vector3.one;

            switch (_family)
            {
                case Family.Hop: PoseHop(ref bobY, ref scale); break;
                case Family.Stride: PoseStride(ref bobY, ref pitch, ref roll); break;
                case Family.Float: PoseFloat(ref bobY, ref roll); break;
                case Family.Stomp: PoseStomp(ref bobY, ref pitch); break;
            }

            // Idle breathing (a slow y-scale pulse) for the grounded gaits when standing still —
            // Float already hovers continuously, so it never "breathes".
            if (!_moving && _family != Family.Float)
            {
                float breathe = Mathf.Sin(_t * BreatheHz * Mathf.PI * 2f + _phase) * BreatheAmp;
                scale = Vector3.Scale(scale, new Vector3(1f - breathe * 0.5f, 1f + breathe, 1f - breathe * 0.5f));
            }

            // Attack telegraph: crouch (squash down) through the anticipation, snap to a stretch
            // on the punch-out. Layered as a multiplicative scale + a small dip.
            if (_atkT > 0f)
            {
                _atkT = Mathf.Max(0f, _atkT - dt);
                float p = 1f - (_atkDur > 0f ? _atkT / _atkDur : 1f); // 0 -> 1 over the lunge
                float squash;
                if (p < 0.3f) squash = Mathf.Lerp(1f, 0.85f, p / 0.3f);        // crouch down
                else squash = Mathf.Lerp(0.85f, 1.12f, (p - 0.3f) / 0.7f);      // stretch out
                scale = Vector3.Scale(scale, new Vector3(2f - squash, squash, 2f - squash));
                // sink a touch on the crouch — clamped so the crouch alone can never push the
                // body meaningfully underground (the gaits themselves never go below 0 now)
                bobY += Mathf.Max(-0.05f, (squash - 1f) * 0.15f);
            }

            // Hit squash: y flat / xz fat, springing back over HitSquashSec.
            if (_hitT > 0f)
            {
                _hitT = Mathf.Max(0f, _hitT - dt);
                float k = _hitT / HitSquashSec;                 // 1 -> 0
                float e = Mathf.Sin(k * Mathf.PI);              // 0 at the ends, 1 mid: a spring
                scale = Vector3.Scale(scale, new Vector3(1f + 0.15f * e, 1f - 0.2f * e, 1f + 0.15f * e));
            }

            _body.localPosition = new Vector3(0f, bobY, 0f);
            _body.localScale = scale;
            _body.localRotation = Quaternion.Euler(pitch, 0f, roll);
        }

        // ---- gaits (all Time-driven, phase-offset; amplitudes deliberately subtle) ----

        /// <summary>Hop: when moving, |sin| arcs whose frequency tracks speed (~speed/hopLength),
        /// with a landing squash at each touchdown. On a STOP, the current hop finishes its arc
        /// first (zeroing the cycle mid-air would teleport an apex slime straight to the ground),
        /// lands with a final squash, THEN rests (breathing handled above).</summary>
        private void PoseHop(ref float bobY, ref Vector3 scale)
        {
            float dt = Time.deltaTime;
            bool midHop = Mathf.Repeat(_hopCycle, 1f) > 0.0001f;
            if (_moving && _speed >= 0.05f)
            {
                _hopCycle += dt * (_speed / HopLength);
            }
            else if (midHop)
            {
                // stopped mid-air: keep the arc going at the last (sticky) cadence until the
                // next integer touchdown, then rest with a landing squash
                float next = Mathf.Ceil(_hopCycle);
                _hopCycle += dt * (Mathf.Max(_speed, 0.5f) / HopLength);
                if (_hopCycle >= next) { _hopCycle = 0f; _hopLandT = HopLandSec; }
            }
            else
            {
                _hopCycle = 0f;
                if (_hopLandT > 0f) // final-touchdown squash springing back
                {
                    _hopLandT = Mathf.Max(0f, _hopLandT - dt);
                    float e = _hopLandT / HopLandSec; // 1 -> 0
                    scale = new Vector3(1f + 0.18f * e, 1f - 0.22f * e, 1f + 0.18f * e);
                }
                return;
            }
            float arc = Mathf.Abs(Mathf.Sin(Mathf.Repeat(_hopCycle, 1f) * Mathf.PI)); // one arch per hop
            bobY = arc * HopHeight;
            // squash on the ground (arc near 0), stretch at the apex — reads as a springy bounce
            float land = 1f - arc;                              // 1 on touchdown, 0 at apex
            scale = new Vector3(1f + 0.18f * land, 1f - 0.22f * land, 1f + 0.18f * land);
        }

        /// <summary>Stride: a gentle double-bounce bob (2 beats/stride), alternating roll, and a
        /// small forward pitch while moving; standing rests (breathing above).</summary>
        private void PoseStride(ref float bobY, ref float pitch, ref float roll)
        {
            if (!_moving) return;
            float stridesPerSec = _speed / 1.4f;                // ~1.4u per stride at this scale
            // ACCUMULATE the phase: sPhase = _t * f would rescale ALL elapsed time whenever the
            // speed changes, jumping the pose discontinuously. Integrating bends the cadence.
            _stridePhase += Time.deltaTime * stridesPerSec * Mathf.PI * 2f;
            float sPhase = _stridePhase + _phase;
            bobY = Mathf.Abs(Mathf.Sin(sPhase)) * StrideBob;    // double-bounce: two lifts per stride
            roll = Mathf.Sin(sPhase) * StrideRollDeg;           // sway side to side, once per stride
            pitch = StridePitchDeg * Mathf.Clamp01(_speed / 3f); // lean into the walk, capped
        }

        /// <summary>Float: a continuous slow hover sine even when idle, and a gentle bank toward
        /// the turn direction. No landing squash EVER.</summary>
        private void PoseFloat(ref float bobY, ref float roll)
        {
            bobY = Mathf.Sin(_t * FloatHz * Mathf.PI * 2f + _phase) * FloatHover;
            // bank into turns: read the root's yaw rate (facing is on the root, owned by CombatView)
            float yaw = transform.eulerAngles.y;
            float delta = Mathf.DeltaAngle(_prevRootYaw, yaw);
            _prevRootYaw = yaw;
            float turnRate = Time.deltaTime > 0f ? delta / Time.deltaTime : 0f; // deg/sec
            roll = Mathf.Clamp(-turnRate / 90f, -1f, 1f) * FloatBankDeg;         // lean into the turn
        }

        /// <summary>Stomp: a slow, heavy single-beat footfall with a slight body pitch. GROUND IS
        /// THE FLOOR: the body lifts slowly between footfalls and slams back to 0 at the beat —
        /// full amplitude, zero floor clipping (a below-zero dip would sink the model into the
        /// ground AND get eaten by the crouch clamp). FELT, not bouncy: slow up, quick down.</summary>
        private void PoseStomp(ref float bobY, ref float pitch)
        {
            if (!_moving) return;
            _stompPhase += Time.deltaTime * StompHz; // accumulate (constant Hz today, but keep it jump-safe)
            float beat = Mathf.Repeat(_stompPhase + _phase / (Mathf.PI * 2f), 1f); // 0..1 per footfall
            float lift = beat < 0.8f ? Mathf.Sin(beat / 0.8f * Mathf.PI * 0.5f)              // slow rise
                                     : Mathf.Cos((beat - 0.8f) / 0.2f * Mathf.PI * 0.5f);     // quick slam down
            bobY = lift * StompBob;
            pitch = (1f - lift) * StompPitchDeg; // pitch spikes right at the footfall
        }

        // ---- hit flash ----

        private void UpdateFlash(float dt)
        {
            if (!_flashing) return;
            _flashT -= dt;
            float k = Mathf.Clamp01(_flashT / FlashSec); // 1 -> 0
            for (int i = 0; i < _mats.Length; i++)
            {
                var m = _mats[i];
                if (m == null || !m.HasProperty("_EmissionColor")) continue;
                // blend from a bright white flash back to the material's CACHED prior emission
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.Lerp(_emCache[i], Color.white, k));
            }
            if (_flashT <= 0f)
            {
                // restore EXACTLY the prior emission (never black — that would wipe a rank/mod tell)
                for (int i = 0; i < _mats.Length; i++)
                    if (_mats[i] != null && _mats[i].HasProperty("_EmissionColor"))
                        _mats[i].SetColor("_EmissionColor", _emCache[i]);
                _flashing = false;
            }
        }

        // ---- death ----

        private void UpdateDeath(float dt)
        {
            _dieT += dt;
            float a = Mathf.Clamp01(_dieT / DieLife);

            if (_toppleStyle)
            {
                // Topple: pitch ~90° away from the blow with an ease-out bounce, sink a little,
                // then a fast shrink at the very end so the corpse clears without a hard pop.
                float ease = 1f - (1f - a) * (1f - a);                  // ease-out
                float bounce = Mathf.Sin(a * Mathf.PI * 1.5f) * 6f * (1f - a); // small settle wobble
                float angle = 90f * ease + bounce;
                _body.localRotation = Quaternion.identity;              // reset local; tumble on root axis
                transform.rotation = Quaternion.AngleAxis(angle, _dieAxis) * Quaternion.Euler(0f, _dieYaw, 0f);
                _body.localPosition = Vector3.down * (0.15f * a);
                if (a > 0.7f)
                {
                    float shrink = (a - 0.7f) / 0.3f;                   // 0 -> 1 over the last 30%
                    transform.localScale = _rootBaseScale * (1f - shrink);
                }
            }
            else
            {
                // Poof: a brief 1.15x inflate, then shrink toward nothing with a small rise —
                // the floaty/blobby death (no corpse to tip over).
                float scale;
                if (a < 0.25f) scale = Mathf.Lerp(1f, 1.15f, a / 0.25f);     // inflate
                else scale = Mathf.Lerp(1.15f, 0.02f, (a - 0.25f) / 0.75f);  // shrink to ~nothing
                transform.localScale = _rootBaseScale * scale;
                _body.localPosition = Vector3.up * (0.6f * a);               // waft upward
            }

            if (a >= 1f) Destroy(gameObject);
        }
    }
}
