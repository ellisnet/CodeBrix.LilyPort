#!/usr/bin/env python3
"""Report a page's HONEST per-column drift, with the pairing artifact removed.

THE PROBLEM THIS SOLVES.  compare-output.py pairs marks of one name in a single
global order, so a horizontal drift re-sorts the list and pairs a mark on one staff
with a mark on another -- or, on these pages, a staff mark with the TAGLINE at the
bottom of the page.  That is where "worst 150.7218" comes from, and trap 7 says a
worst-delta read that way is not an attribution.  PARITY 14 attributed the chord grid
off exactly such a number and was wrong.

WHAT THIS DOES INSTEAD.  Marks are grouped by (name, y) and paired by x order WITHIN
the group.  A horizontal drift preserves y, so same-y pairing is the honest pairing;
a mark whose y really did move shows up as a count mismatch in its band rather than
as a giant delta.  Reported per file: the distinct dx values, the settled (modal) dx,
and how many bands did not pair.

Usage:
    drift.py <file.svg> [<file.svg> ...] [--kind rect] [--tol 0.01]
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


def bands(data, needle):
    out = collections.defaultdict(list)
    for mark, x, y in data["placements"]:
        if needle and needle not in mark:
            continue
        out[(mark, round(y, 2))].append(x)
    for key in out:
        out[key].sort()
    return out


def main():
    files = [a for a in sys.argv[1:] if not a.startswith("--")]
    needle = ""
    tolerance = 0.01
    for a in sys.argv[1:]:
        if a.startswith("--kind="):
            needle = a.split("=", 1)[1]
        if a.startswith("--tol="):
            tolerance = float(a.split("=", 1)[1])
    if not files:
        print(__doc__)
        return 2

    comparator = load_comparator()
    reference_dir = os.path.join(HARNESS, "reference/svg")
    candidate_dir = os.path.join(HARNESS, "candidate/svg")
    reference_names, candidate_names, note = comparator.resolve_sides(
        comparator.INDEX_PATH, reference_dir, candidate_dir, False)
    if reference_names is None:
        print("REFUSING TO REPORT: %s" % note)
        return 1

    for name in files:
        reference = comparator.parse_svg(
            os.path.join(reference_dir, name), reference_names)
        candidate = comparator.parse_svg(
            os.path.join(candidate_dir, name), candidate_names)
        if "error" in reference or "error" in candidate:
            print("%-52s UNPARSEABLE" % name)
            continue

        left = bands(reference, needle)
        right = bands(candidate, needle)

        deltas = []
        unpaired = 0
        moved_bands = []
        for key in sorted(set(left) | set(right)):
            a, b = left.get(key, []), right.get(key, [])
            if len(a) != len(b):
                unpaired += 1
                moved_bands.append((key, len(a), len(b)))
                continue
            for ax, bx in zip(a, b):
                deltas.append(round(bx - ax, 4))

        moving = [d for d in deltas if abs(d) > tolerance]
        counts = collections.Counter(moving)
        print("=== %s   kind=%s" % (name, needle or "ALL"))
        print("    marks paired %d, bands unpaired %d, moving %d"
              % (len(deltas), unpaired, len(moving)))
        if moving:
            print("    settled dx (most common): %s"
                  % ", ".join("%s x%d" % (d, n) for d, n in counts.most_common(6)))
            print("    dx range: %.4f .. %.4f" % (min(moving), max(moving)))
        if moved_bands:
            print("    y-bands whose COUNT differs (real vertical change):")
            for (mark, y), na, nb in moved_bands[:8]:
                print("        %-40s y=%9.4f ref %d cand %d" % (mark[:40], y, na, nb))
        print()

    return 0


if __name__ == "__main__":
    sys.exit(main())
