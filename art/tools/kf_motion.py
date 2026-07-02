"""Decode MS2 .kf animations (Gamebryo 30.2.0.3 NiSequenceData) into per-bone
world-space rotation deltas + pelvis translation, sampled at 30 fps, written as
JSON for the Blender clip builder (art/skinned_body.py).

Channels are either constant (NiTransformEvaluator) or compressed uniform cubic
B-splines (NiBSplineCompTransformEvaluator: int16 control points scaled by
offset + half-range; handles index into NiBSplineData's short array).

World-space composition uses the rest skeleton from f_body.nif, so the output
deltas (anim_world * inverse(rest_world) per bone) retarget onto any rig whose
rest pose matches the measured joint table, regardless of bone-local axes.

    python art/tools/kf_motion.py <anim.kf> <out.json> [--summary]
"""
import json
import math
import os
import struct
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from nif_skeleton import read_header, load_world_positions  # noqa: E402

FBODY_NIF = r"C:\Games\MapleStory2\Extracted\Character\female\f_body.nif"  # default rest
FPS = 30
FLT_INVALID = -3.402823e38  # -FLT_MAX marks "channel not posed"

# Bip01 -> our rig bone names (art/skinned_body.py J table)
BONE_MAP = {
    "Bip01 Pelvis": "pelvis",
    "Bip01 Spine": "spine",
    "Bip01 Spine1": "spine1",
    "Bip01 Spine2": "spine2",
    "Bip01 Neck": "neck",
    "Bip01 Head": "head",
    "Bip01 L Clavicle": "clavL",
    "Bip01 L UpperArm": "uarmL",
    "Bip01 L Forearm": "forearmL",
    "Bip01 L Hand": "handL",
    "Bip01 R Clavicle": "clavR",
    "Bip01 R UpperArm": "uarmR",
    "Bip01 R Forearm": "forearmR",
    "Bip01 R Hand": "handR",
    "Bip01 L Thigh": "thighL",
    "Bip01 L Calf": "calfL",
    "Bip01 L Foot": "footL",
    "Bip01 R Thigh": "thighR",
    "Bip01 R Calf": "calfR",
    "Bip01 R Foot": "footR",
}


# --- tiny quaternion lib (w, x, y, z) -------------------------------------------

def qmul(a, b):
    aw, ax, ay, az = a
    bw, bx, by, bz = b
    return (aw * bw - ax * bx - ay * by - az * bz,
            aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw)


def qconj(q):
    return (q[0], -q[1], -q[2], -q[3])


def qnorm(q):
    m = math.sqrt(sum(c * c for c in q)) or 1.0
    return tuple(c / m for c in q)


def qrot(q, v):
    # rotate vector v by quaternion q
    p = qmul(qmul(q, (0.0, v[0], v[1], v[2])), qconj(q))
    return (p[1], p[2], p[3])


def mat_to_quat(m):
    # m = row-major 3x3
    t = m[0] + m[4] + m[8]
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        return qnorm((0.25 * s, (m[7] - m[5]) / s, (m[2] - m[6]) / s, (m[3] - m[1]) / s))
    if m[0] > m[4] and m[0] > m[8]:
        s = math.sqrt(1.0 + m[0] - m[4] - m[8]) * 2
        return qnorm(((m[7] - m[5]) / s, 0.25 * s, (m[1] + m[3]) / s, (m[2] + m[6]) / s))
    if m[4] > m[8]:
        s = math.sqrt(1.0 + m[4] - m[0] - m[8]) * 2
        return qnorm(((m[2] - m[6]) / s, (m[1] + m[3]) / s, 0.25 * s, (m[5] + m[7]) / s))
    s = math.sqrt(1.0 + m[8] - m[0] - m[4]) * 2
    return qnorm(((m[3] - m[1]) / s, (m[2] + m[6]) / s, (m[5] + m[7]) / s, 0.25 * s))


def angle_of(q):
    return math.degrees(2 * math.acos(max(-1.0, min(1.0, abs(q[0])))))


# --- rest skeleton from f_body.nif ----------------------------------------------

def parse_ninode_block(f, off, size, strings):
    f.seek(off)
    name_idx = struct.unpack("<I", f.read(4))[0]
    name = strings[name_idx] if name_idx < len(strings) else "?"
    n_extra = struct.unpack("<I", f.read(4))[0]
    f.read(4 * n_extra + 4 + 2)  # extra refs + controller + u16 flags
    trans = struct.unpack("<3f", f.read(12))
    rot = struct.unpack("<9f", f.read(36))
    f.read(4)  # scale
    n_props = struct.unpack("<I", f.read(4))[0]
    f.read(4 * n_props + 4)
    n_children = struct.unpack("<I", f.read(4))[0]
    children = struct.unpack("<%di" % n_children, f.read(4 * n_children))
    return name, trans, rot, children


def load_rest_skeleton(nif_path):
    """name -> (parent_name, local_trans, local_quat) + world composition."""
    f = open(nif_path, "rb")
    types, tidx, sizes, strings, offs = read_header(f)
    nodes = {}
    for i in range(len(offs)):
        if types[tidx[i] & 0x7FFF] != "NiNode":
            continue
        name, trans, rot, children = parse_ninode_block(f, offs[i], sizes[i], strings)
        nodes[i] = (name, trans, mat_to_quat(rot), children)
    f.close()
    parent = {}
    for i, (_, _, _, ch) in nodes.items():
        for c in ch:
            if c in nodes:
                parent[c] = i
    by_name, tree = {}, {}
    for i, (name, trans, quat, _) in nodes.items():
        pname = nodes[parent[i]][0] if i in parent else None
        by_name[name] = (pname, trans, quat)
    return by_name


# --- .kf parsing -----------------------------------------------------------------

def load_kf(path):
    f = open(path, "rb")
    types, tidx, sizes, strings, offs = read_header(f)

    duration = None
    textkeys = []
    const_pose = {}      # bone -> (trans|None, quat|None, data_ref)
    spline_chan = {}     # bone -> dict(trans_handle, rot_handle, offsets...)
    transform_data = {}  # block idx -> (rot_keys, trans_keys)
    spline_shorts = None
    spline_floats = None
    n_ctrl = None

    for i in range(len(offs)):
        t = types[tidx[i] & 0x7FFF]
        f.seek(offs[i])
        raw = f.read(sizes[i])
        if t == "NiSequenceData":
            ne = struct.unpack_from("<I", raw, 4)[0]
            duration = struct.unpack_from("<f", raw, 8 + 4 * ne + 4)[0]
        elif t == "NiTextKeyExtraData":
            nk = struct.unpack_from("<I", raw, 4)[0]
            p = 8
            for _ in range(nk):
                tt, si = struct.unpack_from("<fI", raw, p)
                p += 8
                textkeys.append((tt, strings[si] if si < len(strings) else "?"))
        elif t == "NiTransformEvaluator":
            name = strings[struct.unpack_from("<I", raw, 0)[0]]
            tr = struct.unpack_from("<3f", raw, 24)
            q = struct.unpack_from("<4f", raw, 36)
            data_ref = struct.unpack_from("<i", raw, 56)[0]
            const_pose[name] = (
                tr if all(v > FLT_INVALID / 2 for v in tr) else None,
                qnorm(q) if all(v > FLT_INVALID / 2 for v in q) else None,
                data_ref)
        elif t == "NiBSplineCompTransformEvaluator":
            name = strings[struct.unpack_from("<I", raw, 0)[0]]
            pose_tr = struct.unpack_from("<3f", raw, 40)
            pose_q = struct.unpack_from("<4f", raw, 52)
            th, rh, sh = struct.unpack_from("<3I", raw, 72)
            to, thr, ro, rhr, so, shr = struct.unpack_from("<6f", raw, 84)
            spline_chan[name] = {
                "pose_tr": pose_tr if all(v > FLT_INVALID / 2 for v in pose_tr) else None,
                "pose_q": qnorm(pose_q) if all(v > FLT_INVALID / 2 for v in pose_q) else None,
                "th": th, "rh": rh, "to": to, "thr": thr, "ro": ro, "rhr": rhr,
            }
        elif t == "NiTransformData":
            # raw keyframes: rotation quat keys + translation/scale key groups
            p = 0
            nrot = struct.unpack_from("<I", raw, p)[0]; p += 4
            rot_keys = []
            if nrot:
                rtype = struct.unpack_from("<I", raw, p)[0]; p += 4
                if rtype == 4:  # per-axis float keys — rare; skip via group walk
                    for _ in range(3):
                        ng = struct.unpack_from("<I", raw, p)[0]; p += 4
                        if ng:
                            gi = struct.unpack_from("<I", raw, p)[0]; p += 4
                            ksz = {1: 8, 2: 16, 3: 20}[gi]
                            p += ksz * ng
                else:
                    ksz = {1: 20, 2: 20, 3: 32}[rtype]
                    for _ in range(nrot):
                        tt = struct.unpack_from("<f", raw, p)[0]
                        q = struct.unpack_from("<4f", raw, p + 4)
                        rot_keys.append((tt, qnorm(q)))
                        p += ksz
            ntr = struct.unpack_from("<I", raw, p)[0]; p += 4
            tr_keys = []
            if ntr:
                itype = struct.unpack_from("<I", raw, p)[0]; p += 4
                ksz = {1: 16, 2: 40, 3: 28}[itype]
                for _ in range(ntr):
                    tt = struct.unpack_from("<f", raw, p)[0]
                    v = struct.unpack_from("<3f", raw, p + 4)
                    tr_keys.append((tt, v))
                    p += ksz
            transform_data[i] = (rot_keys, tr_keys)
        elif t == "NiBSplineData":
            nf = struct.unpack_from("<I", raw, 0)[0]
            spline_floats = struct.unpack_from("<%df" % nf, raw, 4)
            nsh = struct.unpack_from("<I", raw, 4 + 4 * nf)[0]
            spline_shorts = struct.unpack_from("<%dh" % nsh, raw, 8 + 4 * nf)
        elif t == "NiBSplineBasisData":
            n_ctrl = struct.unpack_from("<I", raw, 0)[0]
    f.close()
    return duration, textkeys, const_pose, spline_chan, spline_shorts, n_ctrl, transform_data


def bspline_sample(ctrl, t01):
    """Uniform cubic B-spline through control point rows (list of tuples)."""
    n = len(ctrl)
    x = t01 * (n - 3)
    k = min(int(x), n - 4)
    u = x - k
    b0 = (1 - u) ** 3 / 6.0
    b1 = (3 * u ** 3 - 6 * u ** 2 + 4) / 6.0
    b2 = (-3 * u ** 3 + 3 * u ** 2 + 3 * u + 1) / 6.0
    b3 = u ** 3 / 6.0
    dim = len(ctrl[0])
    return tuple(b0 * ctrl[k][d] + b1 * ctrl[k + 1][d] + b2 * ctrl[k + 2][d] + b3 * ctrl[k + 3][d]
                 for d in range(dim))


def decode_channels(spline_chan, shorts, n_ctrl, frames):
    """bone -> {quats: [q per frame] | None, trans: [v per frame] | None}"""
    out = {}
    for name, ch in spline_chan.items():
        rec = {"quats": None, "trans": None,
               "pose_q": ch["pose_q"], "pose_tr": ch["pose_tr"]}
        if ch["rh"] != 0xFFFFFFFF and ch["rh"] != 0xFFFF:
            cps = []
            for c in range(n_ctrl):
                q = tuple(shorts[ch["rh"] + c * 4 + d] / 32767.0 * ch["rhr"] + ch["ro"]
                          for d in range(4))
                cps.append(q)
            rec["quats"] = [qnorm(bspline_sample(cps, fr / max(1, frames - 1)))
                            for fr in range(frames)]
        if ch["th"] != 0xFFFFFFFF and ch["th"] != 0xFFFF:
            cps = []
            for c in range(n_ctrl):
                v = tuple(shorts[ch["th"] + c * 3 + d] / 32767.0 * ch["thr"] + ch["to"]
                          for d in range(3))
                cps.append(v)
            rec["trans"] = [bspline_sample(cps, fr / max(1, frames - 1))
                            for fr in range(frames)]
        out[name] = rec
    return out


# --- world composition + retarget deltas ----------------------------------------

def sample_keys(keys, t, lerp):
    if not keys:
        return None
    if t <= keys[0][0]:
        return keys[0][1]
    if t >= keys[-1][0]:
        return keys[-1][1]
    for i in range(len(keys) - 1):
        t0, v0 = keys[i]
        t1, v1 = keys[i + 1]
        if t0 <= t <= t1:
            a = (t - t0) / (t1 - t0) if t1 > t0 else 0.0
            return lerp(v0, v1, a)
    return keys[-1][1]


def qlerp(q0, q1, a):
    if sum(x * y for x, y in zip(q0, q1)) < 0:
        q1 = tuple(-c for c in q1)
    return qnorm(tuple(x + (y - x) * a for x, y in zip(q0, q1)))


def vlerp(v0, v1, a):
    return tuple(x + (y - x) * a for x, y in zip(v0, v1))


def main():
    kf_path, out_path = sys.argv[1], sys.argv[2]
    nif_path = sys.argv[sys.argv.index("--nif") + 1] if "--nif" in sys.argv else FBODY_NIF
    rest = load_rest_skeleton(nif_path)
    pelvis_world = load_world_positions(nif_path)["Bip01 Pelvis"]
    (duration, textkeys, const_pose, spline_chan,
     shorts, n_ctrl, transform_data) = load_kf(kf_path)
    frames = max(2, int(round(duration * FPS)) + 1)
    anim = decode_channels(spline_chan, shorts, n_ctrl, frames)

    # per-frame local pose: spline > raw keys > constant pose > rest local
    def local_pose(name, fr):
        pname, r_tr, r_q = rest[name]
        tr, q = r_tr, r_q
        t = duration * fr / max(1, frames - 1)
        if name in anim:
            a = anim[name]
            if a["quats"] is not None:
                q = a["quats"][fr]
            elif a["pose_q"] is not None:
                q = a["pose_q"]
            if a["trans"] is not None:
                tr = a["trans"][fr]
            elif a["pose_tr"] is not None:
                tr = a["pose_tr"]
        elif name in const_pose:
            ctr, cq, data_ref = const_pose[name]
            if data_ref is not None and data_ref >= 0 and data_ref in transform_data:
                rot_keys, tr_keys = transform_data[data_ref]
                kq = sample_keys(rot_keys, t, qlerp)
                ktr = sample_keys(tr_keys, t, vlerp)
                if kq is not None:
                    q = kq
                elif cq is not None:
                    q = cq
                if ktr is not None:
                    tr = ktr
                elif ctr is not None:
                    tr = ctr
            else:
                if cq is not None:
                    q = cq
                if ctr is not None:
                    tr = ctr
        return pname, tr, q

    # compose world orientation+position per frame for mapped bones
    def world_pose(name, fr, cache):
        if name in cache:
            return cache[name]
        pname, tr, q = local_pose(name, fr)
        if pname is None or pname not in rest:
            cache[name] = (q, tr)
            return cache[name]
        pq, ppos = world_pose(pname, fr, cache)
        wq = qmul(pq, q)
        wpos = tuple(p + o for p, o in zip(ppos, qrot(pq, tr)))
        cache[name] = (wq, wpos)
        return cache[name]

    rest_world = {}
    cache0 = {}
    for bip in BONE_MAP:
        # rest world orientation = compose rest-only chain (frame independent)
        def rest_wq(name):
            pname, _, q = rest[name]
            return q if pname not in rest else qmul(rest_wq(pname), q)
        rest_world[bip] = rest_wq(bip)

    bones = {BONE_MAP[b]: {"dq": []} for b in BONE_MAP}
    pelvis_dt = []
    for fr in range(frames):
        cache = {}
        for bip, ours in BONE_MAP.items():
            wq, wpos = world_pose(bip, fr, cache)
            dq = qmul(wq, qconj(rest_world[bip]))
            bones[ours]["dq"].append([round(c, 6) for c in qnorm(dq)])
            if bip == "Bip01 Pelvis":
                pelvis_dt.append([round((wpos[d] - pelvis_world[d]) * 0.01, 6)
                                  for d in range(3)])

    out = {"source": os.path.basename(kf_path), "duration": duration, "fps": FPS,
           "frames": frames, "textkeys": textkeys,
           "pelvis_dt": pelvis_dt, "bones": bones}
    with open(out_path, "w") as fh:
        json.dump(out, fh)
    print("wrote %s: %d frames, %.2fs, textkeys=%s" % (out_path, frames, duration,
                                                        [(round(t, 2), k) for t, k in textkeys]))

    if "--summary" in sys.argv:
        for ours in sorted(bones):
            arcs = [angle_of(tuple(q)) for q in bones[ours]["dq"]]
            print("  %-10s max delta %6.1f deg   mean %5.1f" %
                  (ours, max(arcs), sum(arcs) / len(arcs)))
        bob = [v[2] for v in pelvis_dt]
        print("  pelvis bob: %.3f .. %.3f units" % (min(bob), max(bob)))


if __name__ == "__main__":
    main()
