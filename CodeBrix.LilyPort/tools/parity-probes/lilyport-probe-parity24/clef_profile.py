#!/usr/bin/env python3
"""Vertical silhouette thickness of one glyph outline, from an SVG font file.

Reads <glyph glyph-name="NAME" d="..."> out of an SVG font, flattens every
cubic to a polyline, and reports max over x of (top(x) - bottom(x)) -- the
quantity that decides how far apart two copies of the glyph must sit when a
skyline is the only thing separating them.

  usage: clef_profile.py <font.svg> <glyph-name> [samples]
"""
import re
import sys


def segments(d):
    toks = re.findall(r'[A-Za-z]|-?\d*\.?\d+', d)
    i = 0
    cur = (0.0, 0.0)
    start = cur
    prev_ctrl = None
    cmd = None
    out = []

    def num():
        nonlocal i
        v = float(toks[i])
        i += 1
        return v

    while i < len(toks):
        if re.match(r'[A-Za-z]', toks[i]):
            cmd = toks[i]
            i += 1
            if cmd in 'zZ':
                if cur != start:
                    out.append((cur, start))
                cur = start
                prev_ctrl = None
                continue
        rel = cmd.islower()
        base = cur if rel else (0.0, 0.0)
        c = cmd.upper()
        if c == 'M':
            p = (base[0] + num(), base[1] + num())
            cur = start = p
            prev_ctrl = None
            cmd = 'l' if rel else 'L'
            continue
        if c == 'L':
            p = (base[0] + num(), base[1] + num())
            out.append((cur, p))
            cur = p
            prev_ctrl = None
            continue
        if c == 'H':
            p = (base[0] + num(), cur[1])
            out.append((cur, p))
            cur = p
            prev_ctrl = None
            continue
        if c == 'V':
            p = (cur[0], base[1] + num())
            out.append((cur, p))
            cur = p
            prev_ctrl = None
            continue
        if c in 'CS':
            if c == 'S':
                c1 = ((2 * cur[0] - prev_ctrl[0], 2 * cur[1] - prev_ctrl[1])
                      if prev_ctrl else cur)
            else:
                c1 = (base[0] + num(), base[1] + num())
            c2 = (base[0] + num(), base[1] + num())
            e = (base[0] + num(), base[1] + num())
            out.extend(flatten(cur, c1, c2, e))
            prev_ctrl = c2
            cur = e
            continue
        i += 1
    return out


def flatten(p0, p1, p2, p3, n=64):
    pts = []
    for k in range(n + 1):
        t = k / n
        u = 1 - t
        x = (u * u * u * p0[0] + 3 * u * u * t * p1[0]
             + 3 * u * t * t * p2[0] + t * t * t * p3[0])
        y = (u * u * u * p0[1] + 3 * u * u * t * p1[1]
             + 3 * u * t * t * p2[1] + t * t * t * p3[1])
        pts.append((x, y))
    return list(zip(pts, pts[1:]))


def profile(segs, samples):
    xs = [p[0] for s in segs for p in s]
    lo, hi = min(xs), max(xs)
    top = [None] * samples
    bot = [None] * samples
    for a, b in segs:
        for p in (a, b):
            j = int(round((p[0] - lo) / (hi - lo) * (samples - 1)))
            if top[j] is None or p[1] > top[j]:
                top[j] = p[1]
            if bot[j] is None or p[1] < bot[j]:
                bot[j] = p[1]
    return lo, hi, top, bot


def main():
    path, name = sys.argv[1], sys.argv[2]
    samples = int(sys.argv[3]) if len(sys.argv) > 3 else 2000
    txt = open(path).read()
    m = re.search(r'<glyph[^>]*glyph-name="%s"[^>]*\sd="([^"]*)"' % re.escape(name), txt)
    if not m:
        sys.exit("glyph %s not found in %s" % (name, path))
    segs = segments(m.group(1))
    lo, hi, top, bot = profile(segs, samples)
    best = None
    for j in range(samples):
        if top[j] is None:
            continue
        t = top[j] - bot[j]
        if best is None or t > best[0]:
            best = (t, lo + (hi - lo) * j / (samples - 1))
    ys = [p[1] for s in segs for p in s]
    print("%-70s bbox x[%.1f %.1f] y[%.1f %.1f] max-thickness %.4f at x=%.2f"
          % (path.split('/')[-1] + ':' + name, lo, hi, min(ys), max(ys), best[0], best[1]))


if __name__ == "__main__":
    main()
