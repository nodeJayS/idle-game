"""Extract MP3 samples from FMOD FSB5 banks (MS2's MS2Sound_*.fsb are all
MPEG-codec, so each sample's data is a raw MP3 stream — no transcoding).

    python art/tools/fsb_extract.py <bank.fsb> <outDir> [name-filter]
    python art/tools/fsb_extract.py <bank.fsb> --list [name-filter]

Pure stdlib. Layout: 60-byte header (magic FSB5, version 1, numSamples,
sampleHeadersSize, nameTableSize, dataSize, mode), then packed u64 sample
headers (+ optional extra chunks), a name table, then sample data.
"""
import os
import struct
import sys


def load_fsb(path):
    d = open(path, "rb").read()
    magic, version, nsamp, sh_size, nt_size, data_size, mode = \
        struct.unpack_from("<4s6I", d, 0)
    assert magic == b"FSB5", "not FSB5"
    assert mode == 11, "not MPEG codec (%d)" % mode
    base = 60

    # sample headers
    p = base
    samples = []  # (data_offset, freq, channels)
    FREQ = {1: 8000, 2: 11000, 3: 11025, 4: 16000, 5: 22050, 6: 24000,
            7: 32000, 8: 44100, 9: 48000, 10: 96000}
    for _ in range(nsamp):
        raw, = struct.unpack_from("<Q", d, p)
        p += 8
        has_chunk = raw & 1
        freq_id = (raw >> 1) & 0xF
        channels = ((raw >> 5) & 0x1) + 1
        data_offset = ((raw >> 6) & ((1 << 28) - 1)) * 16
        while has_chunk:
            chunk, = struct.unpack_from("<I", d, p)
            p += 4
            has_chunk = chunk & 1
            csize = (chunk >> 1) & 0xFFFFFF
            p += csize
        samples.append((data_offset, FREQ.get(freq_id, 44100), channels))

    # name table
    names = []
    nt_base = base + sh_size
    if nt_size:
        offs = struct.unpack_from("<%dI" % nsamp, d, nt_base)
        for o in offs:
            q = nt_base + o
            end = d.index(b"\x00", q)
            names.append(d[q:end].decode("latin-1"))
    else:
        names = ["sample%04d" % i for i in range(nsamp)]

    data_base = base + sh_size + nt_size
    out = []
    for i in range(nsamp):
        start = data_base + samples[i][0]
        end = data_base + samples[i + 1][0] if i + 1 < nsamp else data_base + data_size
        out.append((names[i], d[start:min(end, len(d))]))
    return out


def main():
    listing = "--list" in sys.argv
    rest = [a for a in sys.argv[2:] if a != "--list"]
    if len(sys.argv) < 2 or (not listing and not rest):
        print("usage: fsb_extract.py <bank.fsb> <out_dir> [filter]\n"
              "       fsb_extract.py <bank.fsb> --list [filter]")
        return
    bank = sys.argv[1]
    out_dir = None if listing else rest[0]
    filt = (rest[1] if not listing and len(rest) > 1 else
            rest[0] if listing and rest else "").lower()

    samples = load_fsb(bank)
    n = 0
    for name, data in samples:
        if filt and filt not in name.lower():
            continue
        n += 1
        if listing:
            print("%-48s %7.1f KB" % (name, len(data) / 1024))
        else:
            os.makedirs(out_dir, exist_ok=True)
            with open(os.path.join(out_dir, name + ".mp3"), "wb") as fh:
                fh.write(data)
    print("%s %d/%d samples%s" % ("listed" if listing else "extracted",
                                  n, len(samples),
                                  "" if listing else " -> " + out_dir))


if __name__ == "__main__":
    main()
