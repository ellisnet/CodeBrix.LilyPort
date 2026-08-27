#!/usr/bin/env python3
"""Vertical silhouette divergence between two builds of the same SVG font.

For every glyph present in both files, flattens each outline into a polyline and
computes the TOP and BOTTOM silhouette on a common x-grid -- taking, for each
x column, the min and max y over every SEGMENT that spans that column, not just
over segment endpoints. That distinction is the whole tool: a version that
sampled endpoints only reported up to 2000 font units of divergence on glyphs
whose real divergence is under 3, because two builds place their endpoints
differently and a column then catches the top contour in one and the bottom in
the other (trap 32a).

The silhouette is what a skyline reads, so the maximum over all glyphs bounds
what any layout decision can inherit from the choice of font build.

  usage: font_divergence.py <a.svg> <b.svg> [columns]
"""
import re
import sys


def flatten(d, steps=24):
    """Returns the outline as a list of (x0, y0, x1, y1) polyline segments."""
    toks = re.findall(r'[A-Za-z]|-?\d*\.?\d+(?:[eE][-+]?\d+)?', d)
    i = 0
    cur = (0.0, 0.0)
    start = cur
    prev_ctrl = None
    cmd = None
    segs = []

    def num():
        nonlocal i
        v = float(toks[i])
        i += 1
        return v

    def bez(p0, p1, p2, p3):
        pts = []
        for k in range(steps + 1):
            t = k / steps
            u = 1 - t
            pts.append((u * u * u * p0[0] + 3 * u * u * t * p1[0]
                        + 3 * u * t * t * p2[0] + t * t * t * p3[0],
                        u * u * u * p0[1] + 3 * u * u * t * p1[1]
                        + 3 * u * t * t * p2[1] + t * t * t * p3[1]))
        for a, b in zip(pts, pts[1:]):
            segs.append((a[0], a[1], b[0], b[1]))

    while i < len(toks):
        if re.match(r'[A-Za-z]', toks[i]):
            cmd = toks[i]
            i += 1
            if cmd in 'zZ':
                if cur != start:
                    segs.append((cur[0], cur[1], start[0], start[1]))
                cur = start
                prev_ctrl = None
                continue
        rel = cmd.islower()
        base = cur if rel else (0.0, 0.0)
        c = cmd.upper()
        if c == 'M':
            cur = start = (base[0] + num(), base[1] + num())
            prev_ctrl = None
            cmd = 'l' if rel else 'L'
        elif c == 'L':
            p = (base[0] + num(), base[1] + num())
            segs.append((cur[0], cur[1], p[0], p[1]))
            cur, prev_ctrl = p, None
        elif c == 'H':
            p = (base[0] + num(), cur[1])
            segs.append((cur[0], cur[1], p[0], p[1]))
            cur, prev_ctrl = p, None
        elif c == 'V':
            p = (cur[0], base[1] + num())
            segs.append((cur[0], cur[1], p[0], p[1]))
            cur, prev_ctrl = p, None
        elif c in 'CS':
            if c == 'S':
                c1 = ((2 * cur[0] - prev_ctrl[0], 2 * cur[1] - prev_ctrl[1])
                      if prev_ctrl else cur)
            else:
                c1 = (base[0] + num(), base[1] + num())
            c2 = (base[0] + num(), base[1] + num())
            e = (base[0] + num(), base[1] + num())
            bez(cur, c1, c2, e)
            cur, prev_ctrl = e, c2
        else:
            i += 1
    return segs


def silhouette(segs, lo, hi, columns):
    top = [None] * columns
    bot = [None] * columns
    if hi <= lo:
        return top, bot
    width = hi - lo
    for x0, y0, x1, y1 in segs:
        a, b = (x0, y0), (x1, y1)
        if a[0] > b[0]:
            a, b = b, a
        ja = max(0, min(columns - 1, int((a[0] - lo) / width * (columns - 1))))
        jb = max(0, min(columns - 1, int((b[0] - lo) / width * (columns - 1)) + 1))
        for j in range(ja, jb + 1):
            if j >= columns:
                break
            x = lo + width * j / (columns - 1)
            if x < a[0] - 1e-9 or x > b[0] + 1e-9:
                continue
            y = a[1] if b[0] - a[0] < 1e-12 else \
                a[1] + (b[1] - a[1]) * (x - a[0]) / (b[0] - a[0])
            lo_y = min(a[1], b[1]) if b[0] - a[0] < 1e-12 else y
            hi_y = max(a[1], b[1]) if b[0] - a[0] < 1e-12 else y
            top[j] = hi_y if top[j] is None else max(top[j], hi_y)
            bot[j] = lo_y if bot[j] is None else min(bot[j], lo_y)
    return top, bot


def glyphs(path):
    out = {}
    for m in re.finditer(r'<glyph([^>]*?)/?>', open(path).read()):
        name = re.search(r'glyph-name="([^"]*)"', m.group(1))
        d = re.search(r'\sd="([^"]*)"', m.group(1))
        if name and d and d.group(1).strip():
            out[name.group(1)] = d.group(1)
    return out


def main():
    a_path, b_path = sys.argv[1], sys.argv[2]
    columns = int(sys.argv[3]) if len(sys.argv) > 3 else 300
    A, B = glyphs(a_path), glyphs(b_path)
    shared = sorted(set(A) & set(B))
    same = [g for g in shared if A[g] == B[g]]
    worst = []
    for g in shared:
        if A[g] == B[g]:
            continue
        sa, sb = flatten(A[g]), flatten(B[g])
        if not sa or not sb:
            continue
        xs = [v for s in (sa + sb) for v in (s[0], s[2])]
        lo, hi = min(xs), max(xs)
        ta, ba = silhouette(sa, lo, hi, columns)
        tb, bb = silhouette(sb, lo, hi, columns)
        d = 0.0
        for j in range(columns):
            if ta[j] is None or tb[j] is None:
                continue
            d = max(d, abs(ta[j] - tb[j]), abs(ba[j] - bb[j]))
        worst.append((d, g))

    worst.sort(reverse=True)
    print(f"{a_path.split('/')[-1]}: {len(shared)} shared glyphs, "
          f"{len(same)} byte-identical outlines, {len(worst)} differing")
    if not worst:
        return
    vals = sorted(w for w, _ in worst)
    print("  silhouette divergence over the DIFFERING glyphs, font units "
          "(1000 units = 1 em = 4 staff-spaces):")
    for q, label in ((0.5, "median"), (0.9, "p90"), (0.99, "p99"), (1.0, "max")):
        print(f"    {label:7s} {vals[min(int(q * (len(vals) - 1)), len(vals) - 1)]:7.3f}")
    print("  worst glyphs:")
    for w, g in worst[:8]:
        print(f"    {g:42s} {w:7.3f}")


if __name__ == "__main__":
    main()
