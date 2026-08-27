#!/usr/bin/env python3
"""Read glyph advances and cmap mappings straight out of an OTF, as ground truth.

Written at PARITY 5 for D10.  Neither engine is trusted here: this parses the font
file itself so the port's numbers and the oracle's can both be checked against it.
"""

import struct
import sys


def tables(data):
    count = struct.unpack(">H", data[4:6])[0]
    out = {}
    for i in range(count):
        off = 12 + 16 * i
        tag = data[off:off + 4].decode("latin-1")
        start, length = struct.unpack(">II", data[off + 8:off + 16])
        out[tag] = (start, length)
    return out


def cmap_lookup(data, start):
    n = struct.unpack(">H", data[start + 2:start + 4])[0]
    best = None
    for i in range(n):
        pid, eid, off = struct.unpack(">HHI", data[start + 4 + 8 * i:start + 12 + 8 * i])
        sub = start + off
        fmt = struct.unpack(">H", data[sub:sub + 2])[0]
        if (pid, eid) in ((3, 10), (3, 1), (0, 3), (0, 4), (0, 6)) or best is None:
            best = (sub, fmt)
        if (pid, eid) == (3, 1):
            best = (sub, fmt)
    sub, fmt = best
    mapping = {}
    if fmt == 4:
        segx2 = struct.unpack(">H", data[sub + 6:sub + 8])[0]
        seg = segx2 // 2
        ends = struct.unpack(">%dH" % seg, data[sub + 14:sub + 14 + segx2])
        sstart = sub + 16 + segx2
        starts = struct.unpack(">%dH" % seg, data[sstart:sstart + segx2])
        dstart = sstart + segx2
        deltas = struct.unpack(">%dh" % seg, data[dstart:dstart + segx2])
        rstart = dstart + segx2
        ranges = struct.unpack(">%dH" % seg, data[rstart:rstart + segx2])
        for i in range(seg):
            for c in range(starts[i], min(ends[i], 0xFFFF) + 1):
                if ranges[i] == 0:
                    g = (c + deltas[i]) & 0xFFFF
                else:
                    gi = rstart + 2 * i + ranges[i] + 2 * (c - starts[i])
                    if gi + 2 > len(data):
                        continue
                    g = struct.unpack(">H", data[gi:gi + 2])[0]
                    if g:
                        g = (g + deltas[i]) & 0xFFFF
                if g:
                    mapping[c] = g
    elif fmt == 12:
        ngroups = struct.unpack(">I", data[sub + 12:sub + 16])[0]
        for i in range(ngroups):
            o = sub + 16 + 12 * i
            s, e, gs = struct.unpack(">III", data[o:o + 12])
            for c in range(s, e + 1):
                mapping[c] = gs + (c - s)
    return mapping, fmt


def main():
    path = sys.argv[1]
    text = sys.argv[2] if len(sys.argv) > 2 else "Hxg17o.i"
    data = open(path, "rb").read()
    tab = tables(data)
    head = tab["head"][0]
    upem = struct.unpack(">H", data[head + 18:head + 20])[0]
    hhea = tab["hhea"][0]
    num_h = struct.unpack(">H", data[hhea + 34:hhea + 36])[0]
    hmtx = tab["hmtx"][0]
    mapping, fmt = cmap_lookup(data, tab["cmap"][0])

    print("file      : %s" % path)
    print("unitsPerEm: %d   numberOfHMetrics: %d   cmap fmt: %d   tables: %s"
          % (upem, num_h, fmt, ",".join(sorted(tab))))
    print()
    print("  ch   cp   gid   advance(units)   adv/upem")
    for ch in text:
        cp = ord(ch)
        gid = mapping.get(cp)
        if gid is None:
            print("  %-4s %-4d  --   (not in cmap)" % (repr(ch), cp))
            continue
        idx = min(gid, num_h - 1)
        adv = struct.unpack(">H", data[hmtx + 4 * idx:hmtx + 4 * idx + 2])[0]
        print("  %-4s %-4d %4d   %6d           %.6f" % (ch, cp, gid, adv, adv / upem))


if __name__ == "__main__":
    main()
