#!/usr/bin/env python3
"""Measure BOTH axes for every non-matching row of a verdicts TSV, in one pass.

WHY THIS EXISTS.  A worst-delta is the comparator's own pairing and not a
measurement (trap 7a), and it also hides HOW MUCH of the page moved, which is the
more useful number (trap 7b).  Six rows moving a single mark out of 705 is not
drift and must not be chased as drift; one row moving 333 marks by a hair is.

WHAT IT DOES.  For each non-MATCH row it pairs marks TWICE -- once grouped by
(name, y-band) and read along x, once grouped by (name, x-band) and read along y --
and prints the settled (modal) delta on each axis with the count of marks that
carry it, plus the number of bands that failed to pair at all.

Usage:
    residue.py <verdicts.tsv> [--candidate DIR] [--tol 0.01] [--only SUBSTR]
"""

import collections
import importlib.util
import os
import sys

HARNESS = os.path.expanduser(
    "~/GitHome/CodeBrix.LilyPort/tools/regression-harness")


def load_comparator():
    path = os.path.join(HARNESS, "compare-output.py")
    spec = importlib.util.spec_from_file_location("compare_output", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def bands(data, axis):
    """Group marks for pairing.  axis='x' bands on y and reads x; 'y' is the mirror."""
    out = collections.defaultdict(list)
    for mark, x, y in data["placements"]:
        key = (mark, round(y, 2)) if axis == "x" else (mark, round(x, 2))
        out[key].append(x if axis == "x" else y)
    for key in out:
        out[key].sort()
    return out


def measure(reference, candidate, axis, tolerance):
    left, right = bands(reference, axis), bands(candidate, axis)
    deltas, unpaired = [], 0
    for key in sorted(set(left) | set(right)):
        a, b = left.get(key, []), right.get(key, [])
        if len(a) != len(b):
            unpaired += 1
            continue
        for av, bv in zip(a, b):
            deltas.append(round(bv - av, 4))
    moving = [d for d in deltas if abs(d) > tolerance]
    return len(deltas), unpaired, moving


def main():
    args = sys.argv[1:]
    positional = [a for a in args if not a.startswith("--")]
    candidate_dir = os.path.join(HARNESS, "candidate/svg")
    tolerance, only = 0.01, ""
    for a in args:
        if a.startswith("--candidate="):
            candidate_dir = os.path.expanduser(a.split("=", 1)[1])
        if a.startswith("--tol="):
            tolerance = float(a.split("=", 1)[1])
        if a.startswith("--only="):
            only = a.split("=", 1)[1]
    if not positional:
        print(__doc__)
        return 2

    rows = []
    with open(positional[0]) as handle:
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if line.startswith("#") or len(parts) < 2 or parts[0] in ("file", "FILE"):
                continue
            if parts[1] == "MATCH":
                continue
            if only and only not in parts[0]:
                continue
            rows.append((parts[0], parts[1]))

    comparator = load_comparator()
    reference_dir = os.path.join(HARNESS, "reference/svg")
    reference_names, candidate_names, note = comparator.resolve_sides(
        comparator.INDEX_PATH, reference_dir, candidate_dir, False)
    if reference_names is None:
        print("REFUSING TO REPORT: %s" % note)
        return 1

    print("candidate: %s" % candidate_dir)
    print("%-46s %-18s %7s %7s  %s" % ("FILE", "VERDICT", "PAIRED", "MOVING", "SETTLED"))
    for name, verdict in rows:
        reference = comparator.parse_svg(os.path.join(reference_dir, name), reference_names)
        candidate = comparator.parse_svg(os.path.join(candidate_dir, name), candidate_names)
        if "error" in reference or "error" in candidate:
            print("%-46s %-18s  UNPARSEABLE" % (name[:46], verdict))
            continue
        for axis in ("x", "y"):
            paired, unpaired, moving = measure(reference, candidate, axis, tolerance)
            counts = collections.Counter(moving)
            settled = ", ".join("%s x%d" % (d, n) for d, n in counts.most_common(4)) or "-"
            print("%-46s %-18s %7d %7d  d%s %s%s"
                  % (name[:46] if axis == "x" else "", verdict if axis == "x" else "",
                     paired, len(moving), axis, settled,
                     ("   [%d bands unpaired]" % unpaired) if unpaired else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
