"""Extract triangle meshes from Gamebryo 30.x (MS2) NIFs into a single PLY.

Layout (verified against f_body.nif):
  NiDataStream block body: num_bytes u32, cloning u32, num_regions u32,
  regions (start,count u32 pairs), num_components u32, formats u32[n],
  data[num_bytes], streamable u8. Usage is in the block TYPE string
  ('NiDataStream\x01<usage>\x01<access>'): 0=index, 1=vertex.
  Vertex streams are interleaved; component formats tell the stride.
  Positions = float3 components that are not unit-length (unit = normals etc).
  Index streams pair with the next vertex stream in block order.
"""
import math
import os
import struct
import sys

FMT_SIZE = {
    0x00010215: 2,   # uint16 x1 (index)
    0x00020436: 8,   # float32 x2
    0x00030437: 12,  # float32 x3
    0x00040438: 16,  # float32 x4
    0x00040108: 4,   # uint8/norm x4 (skin indices / weights / color)
    0x00010101: 1,   # uint8 x1
    0x00020102: 2,
    0x00040104: 4,
}

def u32(f): return struct.unpack("<I", f.read(4))[0]
def u16(f): return struct.unpack("<H", f.read(2))[0]

def sized_string(f):
    n = u32(f)
    return f.read(n).decode("latin-1")

def parse_file(path):
    f = open(path, "rb")
    line = b""
    while True:
        c = f.read(1)
        if c == b"\n" or not c:
            break
        line += c
    ver = u32(f)
    f.read(1)
    u32(f)
    num_blocks = u32(f)
    if ver >= 0x1E000002:
        f.read(u32(f))
    num_types = u16(f)
    types = [sized_string(f) for _ in range(num_types)]
    type_index = [u16(f) for _ in range(num_blocks)]
    sizes = [u32(f) for _ in range(num_blocks)]
    ns = u32(f)
    u32(f)
    [sized_string(f) for _ in range(ns)]
    gn = u32(f)
    [u32(f) for _ in range(gn)]

    streams = []  # (block_idx, usage, elements) elements = list of tuples per component
    for i in range(num_blocks):
        t = types[type_index[i] & 0x7FFF]
        if not t.startswith("NiDataStream"):
            f.seek(sizes[i], 1)
            continue
        usage = t.split("\x01")[1]
        start = f.tell()
        num_bytes = u32(f)
        u32(f)
        nregions = u32(f)
        f.read(8 * nregions)
        ncomp = u32(f)
        formats = [u32(f) for _ in range(ncomp)]
        data = f.read(num_bytes)
        f.seek(start + sizes[i])

        if any(fmt not in FMT_SIZE for fmt in formats):
            print("  ! unknown formats %s in block %d, skipped" % ([hex(x) for x in formats], i))
            continue
        stride = sum(FMT_SIZE[fmt] for fmt in formats)
        if stride == 0 or num_bytes % stride != 0:
            continue
        nelem = num_bytes // stride
        streams.append((i, usage, formats, stride, nelem, data))
    return streams

def positions_from_vertex_stream(formats, stride, nelem, data):
    """Pick the non-unit float3 component (positions, not normals/tangents)."""
    best = None
    off = 0
    for fmt in formats:
        if fmt == 0x00030437:
            pts = []
            for e in range(nelem):
                base = e * stride + off
                pts.append(struct.unpack_from("<3f", data, base))
            sample = pts[: min(400, len(pts))]
            unit = sum(1 for (x, y, z) in sample
                       if abs(math.sqrt(x * x + y * y + z * z) - 1.0) < 0.02) / max(1, len(sample))
            spread = max(max(p) for p in sample) - min(min(p) for p in sample)
            if unit < 0.5:
                score = spread
                if best is None or score > best[0]:
                    best = (score, pts)
        off += FMT_SIZE[fmt]
    return best[1] if best else None

def extract(path, verts_out, faces_out):
    streams = parse_file(path)
    pending_indices = None
    added = 0
    for (i, usage, formats, stride, nelem, data) in streams:
        if usage == "0" and formats == [0x00010215]:
            idx = struct.unpack("<%dH" % nelem, data)
            pending_indices = idx
        elif usage == "1":
            pts = positions_from_vertex_stream(formats, stride, nelem, data)
            if pts is None:
                continue
            base = len(verts_out)
            verts_out.extend(pts)
            added += len(pts)
            if pending_indices is not None and len(pending_indices) % 3 == 0 \
               and max(pending_indices) < len(pts):
                for j in range(0, len(pending_indices), 3):
                    faces_out.append((base + pending_indices[j],
                                      base + pending_indices[j + 1],
                                      base + pending_indices[j + 2]))
            pending_indices = None
    print("  %s: +%d verts" % (os.path.basename(path), added))

def main():
    out = sys.argv[-1]
    verts, faces = [], []
    for path in sys.argv[1:-1]:
        extract(path, verts, faces)
    print("total: %d verts, %d tris" % (len(verts), len(faces)))
    with open(out, "w") as w:
        w.write("ply\nformat ascii 1.0\nelement vertex %d\n" % len(verts))
        w.write("property float x\nproperty float y\nproperty float z\n")
        w.write("element face %d\nproperty list uchar int vertex_indices\nend_header\n" % len(faces))
        for (x, y, z) in verts:
            w.write("%.4f %.4f %.4f\n" % (x, y, z))
        for (a, b, c) in faces:
            w.write("3 %d %d %d\n" % (a, b, c))
    print("wrote", out)

main()
