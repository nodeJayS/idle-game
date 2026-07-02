"""Extract the NiNode skeleton (names + world-space joint positions) from a
Gamebryo 30.2.0.3 NIF. Prints a name -> world position table; measurement
tool for rig authoring (see docs/ms2-hero-pipeline-plan.md — numbers only).

    python art/tools/nif_skeleton.py <file.nif> [--csv]
"""
import struct
import sys

def u32(f): return struct.unpack("<I", f.read(4))[0]
def u16(f): return struct.unpack("<H", f.read(2))[0]
def f32(f): return struct.unpack("<f", f.read(4))[0]

def sized_string(f):
    n = u32(f)
    return f.read(n).decode("latin-1")


def read_header(f):
    while True:
        c = f.read(1)
        if c == b"\n" or not c:
            break
    ver = u32(f)
    f.read(1)  # endian
    u32(f)     # user version
    num_blocks = u32(f)
    if ver >= 0x1E000002:
        f.read(u32(f))  # metadata blob
    num_block_types = u16(f)
    types = [sized_string(f) for _ in range(num_block_types)]
    type_index = [u16(f) for _ in range(num_blocks)]
    block_sizes = [u32(f) for _ in range(num_blocks)]
    num_strings = u32(f)
    u32(f)  # max string len
    strings = [sized_string(f) for _ in range(num_strings)]
    num_groups = u32(f)
    for _ in range(num_groups):
        u32(f)
    offsets = []
    off = f.tell()
    for i in range(num_blocks):
        offsets.append(off)
        off += block_sizes[i]
    return types, type_index, block_sizes, strings, offsets


def parse_ninode(f, off, size, strings, flags_bytes):
    """NiObjectNET + NiAVObject + NiNode for Gamebryo 30.x. Returns None if the
    layout doesn't validate (used to auto-pick the flags width)."""
    f.seek(off)
    end = off + size
    try:
        name_idx = u32(f)
        name = strings[name_idx] if name_idx < len(strings) else None
        if name is None:
            return None
        n_extra = u32(f)
        if n_extra > 50:
            return None
        for _ in range(n_extra):
            u32(f)
        u32(f)  # controller ref
        if flags_bytes == 2:
            u16(f)
        else:
            u32(f)
        trans = (f32(f), f32(f), f32(f))
        rot = [f32(f) for _ in range(9)]
        scale = f32(f)
        if not (0.5 < scale < 2.0):
            return None
        # rows should be ~unit length
        for r in range(3):
            m = rot[r * 3] ** 2 + rot[r * 3 + 1] ** 2 + rot[r * 3 + 2] ** 2
            if not (0.8 < m < 1.2):
                return None
        n_props = u32(f)
        if n_props > 50:
            return None
        for _ in range(n_props):
            u32(f)
        u32(f)  # collision ref
        n_children = u32(f)
        if n_children > 200 or f.tell() + 4 * n_children > end:
            return None
        children = [struct.unpack("<i", f.read(4))[0] for _ in range(n_children)]
        return name, trans, rot, scale, children
    except (struct.error, IndexError):
        return None


def load_nodes(path, verbose=False):
    """-> ({block_idx: (name, trans, rot, scale, children)}, parent map)."""
    f = open(path, "rb")
    types, type_index, sizes, strings, offsets = read_header(f)

    nodes = {}
    for flags_bytes in (4, 2):
        nodes = {}
        ok = True
        for i in range(len(offsets)):
            if types[type_index[i] & 0x7FFF] != "NiNode":
                continue
            r = parse_ninode(f, offsets[i], sizes[i], strings, flags_bytes)
            if r is None:
                ok = False
                break
            nodes[i] = r
        if ok and nodes:
            if verbose:
                print("# flags width: %d bytes; %d NiNodes" % (flags_bytes, len(nodes)))
            break
    f.close()
    if not nodes:
        raise ValueError("FAILED to parse NiNodes with either flags width: " + path)
    parent = {}
    for i, (_, _, _, _, children) in nodes.items():
        for c in children:
            if c in nodes:
                parent[c] = i
    return nodes, parent


def load_world_transforms(path):
    """-> {node_name: (pos (x,y,z), rot row-major 3x3)}. World-space."""
    nodes, parent = load_nodes(path)

    def matmul(a, b):
        return [sum(a[r * 3 + k] * b[k * 3 + c] for k in range(3))
                for r in range(3) for c in range(3)]

    def matvec(a, v):
        return tuple(sum(a[r * 3 + k] * v[k] for k in range(3)) for r in range(3))

    roots = [i for i in nodes if i not in parent]
    world_pos = {}
    world_rot = {}
    stack = list(roots)
    for r in roots:
        _, t, rot, _, _ = nodes[r]
        world_pos[r] = t
        world_rot[r] = rot
    while stack:
        i = stack.pop()
        for c in nodes[i][4]:
            if c not in nodes:
                continue
            _, t, rot, _, _ = nodes[c]
            off = matvec(world_rot[i], t)
            world_pos[c] = tuple(world_pos[i][d] + off[d] for d in range(3))
            world_rot[c] = matmul(world_rot[i], rot)
            stack.append(c)
    return {nodes[i][0]: (world_pos[i], world_rot[i]) for i in nodes}


def load_world_positions(path):
    """-> {node_name: (x, y, z) world}. The measurement API other tools import."""
    return {n: t[0] for n, t in load_world_transforms(path).items()}


def main():
    path = sys.argv[1]
    nodes, parent = load_nodes(path, verbose=True)
    world = load_world_positions(path)
    by_name = {nodes[i][0]: i for i in nodes}

    csv = "--csv" in sys.argv
    for name in sorted(world, key=lambda n: world[n][2]):
        x, y, z = world[name]
        i = by_name[name]
        pname = nodes[parent[i]][0] if i in parent else "-"
        if csv:
            print("%s,%s,%.4f,%.4f,%.4f" % (name, pname, x, y, z))
        else:
            print("%-24s parent=%-20s world=(%8.3f, %8.3f, %8.3f)" % (name, pname, x, y, z))
    return 0


if __name__ == "__main__":
    sys.exit(main())
