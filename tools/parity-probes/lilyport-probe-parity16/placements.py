#!/usr/bin/env python3
"""Dump one page's marks in PAGE COORDINATES, both sides, aligned by column.

WHY THIS EXISTS.  compare-output.py reports a row's WORST delta, and trap 7 says a
worst-delta is not an attribution: the comparator pairs marks of the same name, so a
whole column of stems that drifted 2.0 to the right pairs the first reference stem
with the last candidate one and reports 150.  Reading "a rect displaced by 150" off
that number has cost this project two wrong attributions already.  What answers the
question is the COLUMN: every mark of one name, in page coordinates, on both sides,
in x order, with the per-column delta.

IT IMPORTS compare-output.py RATHER THAN COPYING IT.  Trap 32a's second instance was
a probe that copied the comparator to a scratchpad, where INDEX_PATH -- derived from
__file__ -- resolved to nothing, so it ran with no glyph index and reported 2,212
GLYPHS-DIFFER.  Importing from the harness directory keeps the index resolution the
comparator's own.

Usage:
    placements.py <file.svg> [name-substring] [--tol 0.01]
"""

import importlib.util
import os
import sys

HARNESS = os.path.expanduser(
    "~/GitHome/CodeBrix.LilyPort/tools/regression-harness")


def load_comparator():
    """Import compare-output.py from the harness, so its INDEX_PATH resolves."""
    path = os.path.join(HARNESS, "compare-output.py")
    spec = importlib.util.spec_from_file_location("compare_output", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    arguments = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not arguments:
        print(__doc__)
        return 2

    name = arguments[0]
    needle = arguments[1] if len(arguments) > 1 else ""
    tolerance = 0.01
    for a in sys.argv[1:]:
        if a.startswith("--tol"):
            tolerance = float(a.split("=", 1)[1])

    comparator = load_comparator()

    reference_dir = os.path.join(HARNESS, "reference/svg")
    candidate_dir = os.path.join(HARNESS, "candidate/svg")
    reference_names, candidate_names, note = comparator.resolve_sides(
        comparator.INDEX_PATH, reference_dir, candidate_dir, False)
    # Trap 32a(ii): check the tool's inputs resolved before reading its output.
    if reference_names is None or candidate_names is None:
        print("REFUSING TO REPORT: %s" % note)
        return 1
    print("# %s" % note)

    reference = comparator.parse_svg(os.path.join(reference_dir, name), reference_names)
    candidate = comparator.parse_svg(os.path.join(candidate_dir, name), candidate_names)

    for side, data in (("reference", reference), ("candidate", candidate)):
        if "error" in data:
            print("%s: %s" % (side, data["error"]))
            return 1

    # Group by mark name, each side, sorted by x then y -- the column order.
    def columns(data):
        out = {}
        for mark, x, y in data["placements"]:
            if needle and needle not in mark:
                continue
            out.setdefault(mark, []).append((x, y))
        for mark in out:
            out[mark].sort()
        return out

    left = columns(reference)
    right = columns(candidate)

    print("%-58s %5s %5s" % ("mark", "ref", "cand"))
    print("-" * 78)
    for mark in sorted(set(left) | set(right)):
        a = left.get(mark, [])
        b = right.get(mark, [])
        flag = "" if len(a) == len(b) else "   <-- COUNT DIFFERS"
        print("%-58s %5d %5d%s" % (mark[:58], len(a), len(b), flag))

    print()
    for mark in sorted(set(left) | set(right)):
        a = left.get(mark, [])
        b = right.get(mark, [])
        if len(a) != len(b):
            continue
        deltas = [(bx - ax, by - ay) for (ax, ay), (bx, by) in zip(a, b)]
        worst = max((abs(dx) + abs(dy), i) for i, (dx, dy) in enumerate(deltas))
        if worst[0] <= tolerance:
            continue
        print("=== %s   (%d marks, paired in x order)" % (mark, len(a)))
        distinct_dx = sorted({round(dx, 4) for dx, _ in deltas})
        distinct_dy = sorted({round(dy, 4) for _, dy in deltas})
        print("    dx values: %s" % distinct_dx[:12])
        print("    dy values: %s" % distinct_dy[:12])
        for i, ((ax, ay), (bx, by)) in enumerate(zip(a, b)):
            dx, dy = bx - ax, by - ay
            star = " *" if abs(dx) + abs(dy) > tolerance else ""
            print("    [%3d] ref (%10.4f,%9.4f)  cand (%10.4f,%9.4f)  d(%8.4f,%8.4f)%s"
                  % (i, ax, ay, bx, by, dx, dy, star))
        print()

    return 0


if __name__ == "__main__":
    sys.exit(main())
