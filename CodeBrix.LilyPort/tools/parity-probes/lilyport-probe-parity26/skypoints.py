#!/usr/bin/env python3
"""Reconstruct a -ddebug-skylines drawing's BUILDING list out of an SVG.

Grob::print draws a skyline with Lookup::points_to_line_stencil, which walks the
point list Skyline::to_points produced and connects EVERY consecutive pair.  So
building k occupies points[2k], points[2k+1] and is drawn by the line at odd
index 2k+1; the even-index lines are the connectors between buildings.

A ZERO-WIDTH building therefore draws as a fully degenerate line -- x1 == x2 AND
y1 == y2 -- which is what this tool counts and locates.  Nothing else in a
LilyPond SVG draws a zero-length line.

Usage:  skypoints.py FILE.svg [--group N] [--all]
"""
import re
import sys
from collections import Counter

LINE_RE = re.compile(
    r'<line[^>]*?x1="([-\d.e+]+)"\s+y1="([-\d.e+]+)"\s+'
    r'x2="([-\d.e+]+)"\s+y2="([-\d.e+]+)"')
GROUP_RE = re.compile(r'<g color="([^"]*)"')
XFORM_RE = re.compile(r'<g transform="translate\(([-\d.e+]+),\s*([-\d.e+]+)\)"')


def parse(path):
    """Yield (colour, tx, ty, x1, y1, x2, y2) for every skyline line, in order."""
    text = open(path, encoding="utf-8").read()
    colour = None
    tx = ty = 0.0
    out = []
    for m in re.finditer(r'<g color="[^"]*"|<g transform="translate\([^)]*\)"|<line[^>]*/>',
                         text):
        tok = m.group(0)
        if tok.startswith('<g color'):
            colour = GROUP_RE.match(tok).group(1)
        elif tok.startswith('<g transform'):
            g = XFORM_RE.match(tok)
            if g:
                tx, ty = float(g.group(1)), float(g.group(2))
        else:
            lm = LINE_RE.search(tok)
            if lm and colour is not None:
                x1, y1, x2, y2 = (float(v) for v in lm.groups())
                out.append((colour, tx, ty, x1, y1, x2, y2))
    return out


def main():
    path = sys.argv[1]
    show_all = "--all" in sys.argv
    lines = parse(path)
    print(f"{path}: {len(lines)} skyline lines")

    by_colour = {}
    for rec in lines:
        by_colour.setdefault(rec[0], []).append(rec)

    for colour, recs in by_colour.items():
        degen = [r for r in recs if r[3] == r[5] and r[4] == r[6]]
        print(f"\n  colour {colour}: {len(recs)} lines, {len(degen)} DEGENERATE")
        pts = Counter((round(r[1] + r[3], 4), round(r[2] + r[4], 4)) for r in degen)
        for pt, n in sorted(pts.items()):
            print(f"    degenerate x{n:<3} at ({pt[0]:.4f}, {pt[1]:.4f})")
        if show_all:
            for i, r in enumerate(recs):
                print(f"    [{i:4d}] ({r[1] + r[3]:9.4f},{r[2] + r[4]:9.4f}) -> "
                      f"({r[1] + r[5]:9.4f},{r[2] + r[6]:9.4f})")


if __name__ == "__main__":
    main()
