"""Full-fidelity Gamebryo 30.2.0.3 (MS2) NIF mesh importer.

Parses NiMesh blocks with their datastream semantics (verified layouts — see
docs/ms2-port-plan.md Phase 0):
  - INDEX (u16 triangles), POSITION/_BP, NORMAL/_BP (f32x3), TEXCOORD (f32x2),
    BLENDINDICES (u8x4), BLENDWEIGHT (f32x3, 4th derived), BONE_PALETTE (u16),
    BINORMAL/TANGENT (ignored).
  - _BP semantics = bind-pose (model space) — what skinned meshes carry.
  - NiSkinningMeshModifier: bone-ref list located by scan (u32 count followed by
    that many NiNode refs); BLENDINDICES index the mesh's BONE_PALETTE, which
    indexes this bone list.
  - Texture: NiMesh properties -> NiTexturingProperty -> NiSourceTexture -> .dds
    file-name string (byte-granular ref scan — u8 fields misalign the structs).

API:  load_nif(path) -> list of mesh dicts:
  { name, verts [(x,y,z)], tris [(a,b,c)], normals, uvs, texture (dds|None),
    alpha (bool), skinned (bool), bone_names [str], blend_indices [(4,)],
    blend_weights [(4,)] }
Pure stdlib — importable from Blender scripts.
"""
import math
import os
import struct
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from nif_skeleton import read_header  # noqa: E402

FMT_SIZE = {
    0x00010215: 2,   # uint16 x1 (index)
    0x00020436: 8,   # float32 x2
    0x00030437: 12,  # float32 x3
    0x00040438: 16,  # float32 x4
    0x00040108: 4,   # uint8 x4
    0x00010101: 1,
    0x00020102: 2,
    0x00040104: 4,
}


class Nif:
    def __init__(self, path):
        self.f = open(path, "rb")
        (self.types, self.tidx, self.sizes,
         self.strings, self.offs) = read_header(self.f)

    def tname(self, i):
        return self.types[self.tidx[i] & 0x7FFF]

    def blocks_of(self, t):
        return [i for i in range(len(self.offs)) if self.tname(i) == t]

    def raw(self, i):
        self.f.seek(self.offs[i])
        return self.f.read(self.sizes[i])

    def string(self, idx):
        return self.strings[idx] if 0 <= idx < len(self.strings) else None

    def node_name(self, i):
        idx = struct.unpack_from("<I", self.raw(i), 0)[0]
        return self.string(idx)


def parse_datastream(nif, i):
    """-> (formats, stride, nelem, data) or None."""
    raw = nif.raw(i)
    p = 0
    num_bytes, _, nregions = struct.unpack_from("<3I", raw, p); p += 12
    p += 8 * nregions
    ncomp = struct.unpack_from("<I", raw, p)[0]; p += 4
    formats = struct.unpack_from("<%dI" % ncomp, raw, p); p += 4 * ncomp
    if any(fmt not in FMT_SIZE for fmt in formats):
        return None
    stride = sum(FMT_SIZE[fmt] for fmt in formats)
    if stride == 0 or num_bytes % stride:
        return None
    return formats, stride, num_bytes // stride, raw[p:p + num_bytes]


def decode_component(fmt, data, stride, off, nelem):
    out = []
    for e in range(nelem):
        base = e * stride + off
        if fmt == 0x00030437:
            out.append(struct.unpack_from("<3f", data, base))
        elif fmt == 0x00020436:
            out.append(struct.unpack_from("<2f", data, base))
        elif fmt == 0x00040438:
            out.append(struct.unpack_from("<4f", data, base))
        elif fmt == 0x00040108:
            out.append(struct.unpack_from("<4B", data, base))
        elif fmt == 0x00010215:
            out.append(struct.unpack_from("<H", data, base)[0])
        else:
            out.append(data[base:base + FMT_SIZE[fmt]])
    return out


def parse_nimesh(nif, i):
    """-> (name, prop_refs, stream_list, modifier_refs); stream_list entries are
    (stream_block_ref, [semantic names])."""
    raw = nif.raw(i)
    p = 0
    name = nif.string(struct.unpack_from("<I", raw, p)[0]); p += 4
    ne = struct.unpack_from("<I", raw, p)[0]; p += 4
    extras = list(struct.unpack_from("<%di" % ne, raw, p)); p += 4 * ne + 4 + 2  # +ctrl+flags
    p += 12 + 36 + 4  # trs
    np_ = struct.unpack_from("<I", raw, p)[0]; p += 4
    props = list(struct.unpack_from("<%di" % np_, raw, p)); p += 4 * np_ + 4  # +collision
    nmat = struct.unpack_from("<I", raw, p)[0]; p += 4 + 8 * nmat + 5  # mats+active+update
    p += 4 + 2 + 1 + 16  # primitive, nsub, instancing, bound
    nds = struct.unpack_from("<I", raw, p)[0]; p += 4
    stream_list = []
    for _ in range(nds):
        ref = struct.unpack_from("<I", raw, p)[0]; p += 5  # +per_instance u8
        nsm = struct.unpack_from("<H", raw, p)[0]; p += 2 + 2 * nsm
        ncomp = struct.unpack_from("<I", raw, p)[0]; p += 4
        sems = []
        for _ in range(ncomp):
            sn, _sx = struct.unpack_from("<2I", raw, p); p += 8
            sems.append(nif.string(sn) or "?")
        stream_list.append((ref, sems))
    nmod = struct.unpack_from("<I", raw, p)[0]; p += 4
    mods = list(struct.unpack_from("<%di" % nmod, raw, p))
    return name, props, stream_list, mods, extras


def tint_of(nif, extras):
    """OverrideColor0 = MS2 customization tint (skin tone). Pure-primary values
    are mask-channel placeholders (face makeup system) — no flat tint there."""
    for er in extras:
        if er < 0 or nif.tname(er) != "NiColorExtraData":
            continue
        raw = nif.raw(er)
        if nif.string(struct.unpack_from("<I", raw, 0)[0]) != "OverrideColor0":
            continue
        r, g, b, _a = struct.unpack_from("<4f", raw, 4)
        if {r, g, b} <= {0.0, 1.0}:  # (1,0,0)-style mask placeholder
            return None
        return (r, g, b)
    return None


def skin_bones(nif, mod_ref):
    """NiSkinningMeshModifier -> bone names, by scanning for the count+refs run."""
    raw = nif.raw(mod_ref)
    node_ids = set(nif.blocks_of("NiNode"))
    for k in range(0, min(len(raw) - 8, 400), 2):
        n = struct.unpack_from("<I", raw, k)[0]
        if 1 <= n <= 100 and k + 4 + 4 * n <= len(raw):
            refs = struct.unpack_from("<%di" % n, raw, k + 4)
            if all(r in node_ids for r in refs):
                return [nif.node_name(r) for r in refs]
    return []


def texture_of(nif, prop_refs):
    """Follow props -> NiTexturingProperty -> NiSourceTexture -> .dds name.
    Byte-granular scans (u8 fields misalign both structs)."""
    src_ids = set(nif.blocks_of("NiSourceTexture"))
    for pr in prop_refs:
        if pr < 0 or nif.tname(pr) != "NiTexturingProperty":
            continue
        raw = nif.raw(pr)
        for k in range(len(raw) - 3):
            u = struct.unpack_from("<I", raw, k)[0]
            if u not in src_ids:
                continue
            sraw = nif.raw(u)
            for j in range(len(sraw) - 3):
                si = struct.unpack_from("<I", sraw, j)[0]
                name = nif.string(si)
                if name and name.lower().endswith(".dds"):
                    return name
    return None


def load_nif(path):
    nif = Nif(path)
    meshes = []
    for mi in nif.blocks_of("NiMesh"):
        name, props, stream_list, mods, extras = parse_nimesh(nif, mi)
        mesh = {"name": name, "verts": None, "tris": [], "normals": None,
                "uvs": None, "blend_indices": None, "blend_weights": None,
                "bone_palette": None, "bone_names": [], "texture": None,
                "alpha": False, "skinned": False, "tint": tint_of(nif, extras)}
        for ref, sems in stream_list:
            parsed = parse_datastream(nif, ref)
            if parsed is None:
                continue
            formats, stride, nelem, data = parsed
            if len(sems) != len(formats):
                continue
            off = 0
            for fmt, sem in zip(formats, sems):
                vals = decode_component(fmt, data, stride, off, nelem)
                off += FMT_SIZE[fmt]
                if sem == "INDEX":
                    mesh["tris"] = [(vals[j], vals[j + 1], vals[j + 2])
                                    for j in range(0, len(vals) - 2, 3)]
                elif sem in ("POSITION", "POSITION_BP"):
                    mesh["verts"] = vals
                elif sem in ("NORMAL", "NORMAL_BP"):
                    mesh["normals"] = vals
                elif sem == "TEXCOORD":
                    mesh["uvs"] = vals
                elif sem == "BLENDINDICES":
                    mesh["blend_indices"] = vals
                elif sem == "BLENDWEIGHT":
                    mesh["blend_weights"] = vals
                elif sem == "BONE_PALETTE":
                    mesh["bone_palette"] = vals
        for pr in props:
            if pr >= 0 and nif.tname(pr) == "NiAlphaProperty":
                mesh["alpha"] = True
        mesh["texture"] = texture_of(nif, props)
        for m in mods:
            if m >= 0 and nif.tname(m) == "NiSkinningMeshModifier":
                mesh["skinned"] = True
                mesh["bone_names"] = skin_bones(nif, m)
        if mesh["verts"]:
            meshes.append(mesh)
    return meshes


def _summary(path):
    for m in load_nif(path):
        w = "skinned(%d bones)" % len(m["bone_names"]) if m["skinned"] else "static"
        print("%-12s %5d v %5d tri  uv=%s n=%s bi=%s bw=%s pal=%s  %s alpha=%s tex=%s"
              % (m["name"], len(m["verts"]), len(m["tris"]),
                 m["uvs"] is not None, m["normals"] is not None,
                 m["blend_indices"] is not None, m["blend_weights"] is not None,
                 len(m["bone_palette"] or []), w, m["alpha"], m["texture"]))


if __name__ == "__main__":
    _summary(sys.argv[1])
