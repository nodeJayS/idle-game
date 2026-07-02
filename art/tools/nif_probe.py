"""Probe a Gamebryo NIF header: version, block types, block sizes, string table."""
import struct
import sys

def u32(f): return struct.unpack("<I", f.read(4))[0]
def u16(f): return struct.unpack("<H", f.read(2))[0]

def sized_string(f):
    n = u32(f)
    return f.read(n).decode("latin-1")

path = sys.argv[1]
f = open(path, "rb")

header_line = b""
while True:
    c = f.read(1)
    if c == b"\n" or not c:
        break
    header_line += c
print("header:", header_line.decode("latin-1"))

ver = u32(f)
print("binary version: %08X (%d.%d.%d.%d)" % (ver, ver >> 24, (ver >> 16) & 0xFF, (ver >> 8) & 0xFF, ver & 0xFF))
endian = f.read(1)[0]
print("endian:", endian)
user_ver = u32(f)
print("user version:", user_ver)
num_blocks = u32(f)
print("num blocks:", num_blocks)

if ver >= 0x1E000002:  # 30.0.0.2+: header metadata blob
    meta_len = u32(f)
    f.read(meta_len)
    print("skipped metadata blob:", meta_len, "bytes")

# Gamebryo 20.2.0.7+ vanilla: metadata only if user version >= ... (Bethesda adds BS header).
num_block_types = u16(f)
types = [sized_string(f) for _ in range(num_block_types)]
print("block types:")
for i, t in enumerate(types):
    print("  [%d] %r" % (i, t))

type_index = [u16(f) for _ in range(num_blocks)]
block_sizes = [u32(f) for _ in range(num_blocks)]

num_strings = u32(f)
max_string_len = u32(f)
strings = [sized_string(f) for _ in range(num_strings)]
print("strings (%d):" % num_strings)
for i, s in enumerate(strings):
    print("  {%d} %r" % (i, s))

num_groups = u32(f)
groups = [u32(f) for _ in range(num_groups)]
print("groups:", groups)

data_start = f.tell()
print("block data starts at offset:", data_start)
print("blocks:")
off = data_start
for i in range(num_blocks):
    t = types[type_index[i] & 0x7FFF]
    print("  #%-3d %-38s size=%-8d @%d" % (i, t, block_sizes[i], off))
    off += block_sizes[i]

import os
print("computed end:", off, " file size:", os.path.getsize(path))
