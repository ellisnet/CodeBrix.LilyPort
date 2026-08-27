#!/usr/bin/env python3
"""Print EVERY moving mark of one file: its name, x, both y values and dy.

drift.py and residue.py settle a page down to a modal delta and a count.  When
the modal delta is not the whole story -- several distinct values, or a handful
of marks that move where the rest do not -- this prints the marks themselves so
the pattern is readable rather than summarised.

Pairing is by (name, x-band) and then along y, which is ydrift.py's pairing; pass
--axis x to pair by (name, y-band) and read x instead.

Usage:
    ybands.py <file.svg> [--candidate DIR] [--axis y] [--tol 0.01] [--kind SUBSTR]
"""

import collections
import importlib.util
import os
import sys

HARNESS = os.path.expanduser(
    "~/GitHome/CodeBrix.Samples.Gpl3/CodeBrix.LilyPort/tools/regression-harness")


def load_comparator():
    path = os.path.join(HARNESS, "compare-output.py")
    spec = importlib.util.spec_from_file_location("compare_output", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def bands(data, axis, needle):
    """Key on the axis we are NOT reading, so a pure drift preserves the key."""
    out = collections.defaultdict(list)
    for mark, x, y in data["placements"]:
        if needle and needle not in mark:
            continue
        key = (mark, round(x, 2)) if axis == "y" else (mark, round(y, 2))
        out[key].append((y, x) if axis == "y" else (x, y))
    for key in out:
        out[key].sort()
    return out


def main():
    args = sys.argv[1:]
    positional = [a for a in args if not a.startswith("--")]
    candidate_dir = os.path.join(HARNESS, "candidate/svg")
    axis, tolerance, needle = "y", 0.01, ""
    for a in args:
        if a.startswith("--candidate="):
            candidate_dir = os.path.expanduser(a.split("=", 1)[1])
        if a.startswith("--axis="):
            axis = a.split("=", 1)[1]
        if a.startswith("--tol="):
            tolerance = float(a.split("=", 1)[1])
        if a.startswith("--kind="):
            needle = a.split("=", 1)[1]
    if not positional:
        print(__doc__)
        return 2

    comparator = load_comparator()
    reference_dir = os.path.join(HARNESS, "reference/svg")
    reference_names, candidate_names, note = comparator.resolve_sides(
        comparator.INDEX_PATH, reference_dir, candidate_dir, False)
    if reference_names is None:
        print("REFUSING TO REPORT: %s" % note)
        return 1

    other = "x" if axis == "y" else "y"
    for name in positional:
        reference = comparator.parse_svg(os.path.join(reference_dir, name), reference_names)
        candidate = comparator.parse_svg(os.path.join(candidate_dir, name), candidate_names)
        if "error" in reference or "error" in candidate:
            print("%s UNPARSEABLE" % name)
            continue
        left, right = bands(reference, axis, needle), bands(candidate, axis, needle)

        print("=== %s   axis=d%s   candidate=%s" % (name, axis, candidate_dir))
        print("    %-40s %10s %12s %12s %10s"
              % ("MARK", other.upper(), "REF " + axis.upper(),
                 "CAND " + axis.upper(), "D" + axis.upper()))
        moving = 0
        for key in sorted(set(left) | set(right)):
            a, b = left.get(key, []), right.get(key, [])
            if len(a) != len(b):
                print("    %-40s %10.3f   COUNT DIFFERS  ref %d cand %d"
                      % (key[0][:40], key[1], len(a), len(b)))
                continue
            for (av, ao), (bv, bo) in zip(a, b):
                delta = bv - av
                if abs(delta) <= tolerance:
                    continue
                moving += 1
                print("    %-40s %10.3f %12.4f %12.4f %10.4f"
                      % (key[0][:40], ao, av, bv, delta))
        print("    moving marks: %d" % moving)
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
