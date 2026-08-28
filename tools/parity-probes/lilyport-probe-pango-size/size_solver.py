#!/usr/bin/env python3
"""
Predict, from the MEASURED Pango formatting rule alone, what font-size the SVG
backend must print -- and check that prediction against the committed reference
corpus, BEFORE any port code is changed.

WHY THIS EXISTS
---------------
pango_desc.py measures what pango_font_description_to_string emits.  This script
answers the question that measurement is FOR: does the measured rule account for
the four bounded-delta rows R10 accepted, or is the residue somewhere else?

THE MODEL, both halves, exactly as the code paths are
-----------------------------------------------------
Upstream: the size reaches the SVG as text.  A description carries an integer
number of Pango units q; to_string writes the SHORTEST decimal string that
round-trips back to q (pango_desc.py measures this); scm/output-svg.scm:154-156
pulls that token out with `[ -]([0-9.]+)$', divides by lily-unit-length (the
paper's output-scale, framework-svg.scm:93) and prints four decimals.

Port: TextFontMetric.DescriptionString formats q/1024 to THREE DECIMALS; the
same division and the same four-decimal print follow.

So both engines share q and L, and differ only in the string.  Neither q nor L is
recorded in the SVG, so this script SOLVES for them: every distinct font size in
one file shares one L, which over-determines it many times.

WHAT IT REPORTS
---------------
Per file: the solved L, and per distinct size the oracle's own value beside what
each of the two rules predicts.  A rule that predicts every oracle value in the
file is the rule upstream is using.

SELF-CHECK (trap 3 -- a tool's first answer is about the tool)
--------------------------------------------------------------
`selfcheck` runs the solver over a file whose sizes ALL agree between the two
engines (no R10 row in it).  There the two rules must be indistinguishable and
both must predict every value; a solver that cannot do that on the easy case is
not evidence about the hard one.
"""

import re
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pango_desc as P

PANGO_SCALE = 1024
SIZE_RE = re.compile(r'font-size="([0-9.]+)"')

_shortest_cache = {}


def shortest(q):
    """The string libpango writes for q Pango units (measured, then cached)."""
    if q not in _shortest_cache:
        _shortest_cache[q] = float(P.size_field(q))
    return _shortest_cache[q]


def three_decimals(q):
    return float("%.3f" % (q / PANGO_SCALE))


def sizes_in(path):
    """Distinct font-size attribute values, with their occurrence counts."""
    text = open(path, encoding="utf-8").read()
    counts = {}
    for value in SIZE_RE.findall(text):
        counts[value] = counts.get(value, 0) + 1
    return counts


def solve(oracle_sizes, lo=0.5, hi=4.0):
    """
    Solve for L using the LARGEST size as the anchor: for every integer q that
    could produce it, L is pinned, and the remaining sizes score the candidate.
    """
    anchor = max(float(v) for v in oracle_sizes)
    best = None
    q_lo = int(anchor * lo * PANGO_SCALE) - 2
    q_hi = int(anchor * hi * PANGO_SCALE) + 2
    for q in range(max(1, q_lo), q_hi + 1):
        L = shortest(q) / anchor
        if not (lo <= L <= hi):
            continue
        hits = 0
        for value in oracle_sizes:
            target = float(value)
            qi = int(round(target * L * PANGO_SCALE))
            for cand in (qi - 1, qi, qi + 1):
                if cand > 0 and "%.4f" % (shortest(cand) / L) == value:
                    hits += 1
                    break
        if best is None or hits > best[0]:
            best = (hits, L, q)
    return best


def report(path, label=None):
    counts = sizes_in(path)
    if not counts:
        print("%s: no font-size attributes" % (label or path))
        return None
    values = sorted(counts, key=float)
    hits, L, anchor_q = solve(values)
    print("%s  --  %d distinct sizes, solved L = %.8f (anchor q = %d), "
          "shortest-round-trip predicts %d of %d"
          % (label or os.path.basename(path), len(values), L, anchor_q, hits, len(values)))
    print("   %-10s %-6s %-10s %-10s %-10s %s"
          % ("oracle", "count", "q", "shortest", "3-decimal", "verdict"))
    agree = differ = unexplained = 0
    for value in values:
        target = float(value)
        qi = int(round(target * L * PANGO_SCALE))
        chosen = None
        for cand in (qi - 1, qi, qi + 1):
            if cand > 0 and "%.4f" % (shortest(cand) / L) == value:
                chosen = cand
                break
        if chosen is None:
            print("   %-10s %-6d %-10s %-10s %-10s %s"
                  % (value, counts[value], "?", "-", "-", "NOT REACHED by either rule"))
            unexplained += 1
            continue
        s_short = "%.4f" % (shortest(chosen) / L)
        s_three = "%.4f" % (three_decimals(chosen) / L)
        verdict = "identical" if s_short == s_three else "RULES DIFFER (port would print %s)" % s_three
        if s_short == s_three:
            agree += 1
        else:
            differ += 1
        print("   %-10s %-6d %-10d %-10s %-10s %s"
              % (value, counts[value], chosen, s_short, s_three, verdict))
    print("   -> %d sizes the two rules agree on, %d they differ on, %d unexplained"
          % (agree, differ, unexplained))
    return (agree, differ, unexplained)


def selfcheck(reference_dir):
    # A file with no R10 row: the two rules must be indistinguishable there.
    path = os.path.join(reference_dir, "page-layout-manual-position.svg")
    if not os.path.exists(path):
        path = os.path.join(reference_dir, "markup-note-sizes.svg")
    print("SELF-CHECK over %s" % os.path.basename(path))
    result = report(path)
    return result is not None and result[2] == 0


def main():
    argv = sys.argv[1:]
    if not argv:
        print(__doc__)
        print("usage: size_solver.py <reference-svg-dir> [file ...]")
        print("       size_solver.py selfcheck <reference-svg-dir>")
        return
    if argv[0] == "selfcheck":
        sys.exit(0 if selfcheck(argv[1]) else 1)
    ref = argv[0]
    names = argv[1:] or ["markup-note-sizes", "page-layout-bottom-padding",
                         "fret-diagrams-size", "tablature-full-notation"]
    for name in names:
        report(os.path.join(ref, name + ".svg"), name)
        print()


if __name__ == "__main__":
    main()
