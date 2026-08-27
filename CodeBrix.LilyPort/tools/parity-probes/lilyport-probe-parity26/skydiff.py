#!/usr/bin/env python3
"""Sequence-diff the -ddebug-skylines drawings of two renderings of one file.

The comparator says WHICH marks differ; this says WHERE IN THE SEQUENCE, which is
what names the building.  Every skyline line is emitted in building order, so the
first divergence in the sequence is the first building the two engines disagree
about -- and everything after it is displacement, not evidence.

Usage:  skydiff.py REFERENCE.svg CANDIDATE.svg [--context N] [--all]
"""
import difflib
import re
import sys

LINE_RE = re.compile(
    r'<line[^>]*?x1="([-\d.e+]+)"\s+y1="([-\d.e+]+)"\s+'
    r'x2="([-\d.e+]+)"\s+y2="([-\d.e+]+)"')
GROUP_RE = re.compile(r'<g color="([^"]*)"')
XFORM_RE = re.compile(r'<g transform="translate\(([-\d.e+]+),\s*([-\d.e+]+)\)"')

COLOUR_NAME = {
    "rgba(100.0000%, 100.0000%, 0.0000%, 100.0000%)": "X-LEFT ",
    "rgba(0.0000%, 100.0000%, 0.0000%, 100.0000%)": "X-RIGHT",
    "rgba(0.0000%, 100.0000%, 100.0000%, 100.0000%)": "Y-DOWN ",
    "rgba(100.0000%, 0.0000%, 100.0000%, 100.0000%)": "Y-UP   ",
}


def rows(path):
    text = open(path, encoding="utf-8").read()
    colour = None
    tx = ty = 0.0
    out = []
    for m in re.finditer(
            r'<g color="[^"]*"|<g transform="translate\([^)]*\)"|<line[^>]*/>', text):
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
                kind = COLOUR_NAME.get(colour, colour[:18])
                degen = "DEGEN" if (x1 == x2 and y1 == y2) else "     "
                out.append(f"{kind} {degen} ({tx + x1:10.4f},{ty + y1:10.4f}) -> "
                           f"({tx + x2:10.4f},{ty + y2:10.4f})")
    return out


def main():
    a, b = rows(sys.argv[1]), rows(sys.argv[2])
    ctx = 2
    if "--context" in sys.argv:
        ctx = int(sys.argv[sys.argv.index("--context") + 1])
    print(f"reference: {len(a)} skyline lines ({sum('DEGEN' in r for r in a)} degenerate)")
    print(f"candidate: {len(b)} skyline lines ({sum('DEGEN' in r for r in b)} degenerate)")
    sm = difflib.SequenceMatcher(None, a, b, autojunk=False)
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == "equal":
            continue
        print(f"\n=== {tag.upper()}  ref[{i1}:{i2}]  cand[{j1}:{j2}] ===")
        for k in range(max(0, i1 - ctx), i1):
            print(f"   ctx  {a[k]}")
        for k in range(i1, i2):
            print(f"  -REF  {a[k]}")
        for k in range(j1, j2):
            print(f"  +CAN  {b[k]}")
        for k in range(i2, min(len(a), i2 + ctx)):
            print(f"   ctx  {a[k]}")


if __name__ == "__main__":
    main()
