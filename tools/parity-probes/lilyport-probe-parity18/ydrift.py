#!/usr/bin/env python3
"""Report a page's HONEST per-row VERTICAL drift -- drift.py's mirror image.

WHY THIS EXISTS.  drift.py (PARITY 16) pairs marks by (name, y-band) and compares
x, which is the honest instrument for a HORIZONTAL drift.  PARITY 18's whole
residue is VERTICAL, and reading a vertical shift out of drift.py's "y-bands whose
COUNT differs" section gives you the bands, not the size of the move.  This pairs
by (name, x-band) and compares y, so a purely vertical drift preserves x and the
same-x pairing is the honest one.

Trap 7a applies in both directions: compare-output.py pairs marks of one name in a
single global order, so a vertical shift re-sorts the list and can pair a mark on
one staff with a mark on another, or with the page tagline.  A worst-delta is a
POINTER to a row, never a description of it.

TRAP 32a: A SWEEP TOOL'S FIRST ANSWER IS ABOUT THE TOOL.  Run --selftest first.
It grades a row the comparator calls MATCH and must report zero moving marks, and
it grades a row with a known shift and must report a non-zero one.

Usage:
    ydrift.py <file.svg> [<file.svg> ...] [--kind=<substring>] [--tol=0.01]
    ydrift.py --selftest
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
    """Group marks by (name, x rounded to 2dp); the values are their y coordinates.

    Two decimals, deliberately: drift.py's own first answer was about the tool
    because banding on four decimals split bands differing in the fourth place.
    """
    out = collections.defaultdict(list)
    for mark, x, y in data["placements"]:
        if needle and needle not in mark:
            continue
        out[(mark, round(x, 2))].append(y)
    for key in out:
        out[key].sort()
    return out


def report(comparator, reference_dir, candidate_dir, reference_names,
           candidate_names, name, needle, tolerance, quiet=False):
    reference = comparator.parse_svg(
        os.path.join(reference_dir, name), reference_names)
    candidate = comparator.parse_svg(
        os.path.join(candidate_dir, name), candidate_names)
    if "error" in reference or "error" in candidate:
        print("%-52s UNPARSEABLE" % name)
        return None

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
        for ay, by in zip(a, b):
            deltas.append(round(by - ay, 4))

    moving = [d for d in deltas if abs(d) > tolerance]
    counts = collections.Counter(moving)
    if not quiet:
        print("=== %s   kind=%s" % (name, needle or "ALL"))
        print("    marks paired %d, bands unpaired %d, moving %d"
              % (len(deltas), unpaired, len(moving)))
        if moving:
            print("    settled dy (most common): %s"
                  % ", ".join("%s x%d" % (d, n) for d, n in counts.most_common(8)))
            print("    dy range: %.4f .. %.4f" % (min(moving), max(moving)))
        if moved_bands:
            print("    x-bands whose COUNT differs (a real horizontal change):")
            for (mark, x), na, nb in moved_bands[:8]:
                print("        %-40s x=%9.4f ref %d cand %d" % (mark[:40], x, na, nb))
        print()
    return moving


def main():
    files = [a for a in sys.argv[1:] if not a.startswith("--")]
    needle = ""
    tolerance = 0.01
    selftest = "--selftest" in sys.argv
    for a in sys.argv[1:]:
        if a.startswith("--kind="):
            needle = a.split("=", 1)[1]
        if a.startswith("--tol="):
            tolerance = float(a.split("=", 1)[1])
    if not files and not selftest:
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

    if selftest:
        # The control: a row the comparator grades MATCH must show NO moving mark.
        # The case: a row with a known vertical shift must show one.
        #
        # THE CASE IS CORPUS-DEPENDENT AND GOES STALE WHEN THE ROW IS FIXED.  It was
        # `rest-dot-position.svg' at PARITY 18, and PARITY 18's OWN cycle 1 took that
        # row to MATCH later in the same session -- so the selftest failed at PARITY
        # 19's open reporting nothing about the tool (trap 32a in reverse: the tool
        # was fine and the SELFTEST had rotted).  When this fails, check the case
        # row's verdict before touching the tool, and repoint it at a row that still
        # drifts vertically.
        control, case = "clefs.svg", "caesura-style-comma.svg"
        ok = True
        moving = report(comparator, reference_dir, candidate_dir,
                        reference_names, candidate_names, control, "", tolerance,
                        quiet=True)
        print("selftest control %-28s moving=%s (expect 0)"
              % (control, "n/a" if moving is None else len(moving)))
        ok = ok and moving == []
        moving = report(comparator, reference_dir, candidate_dir,
                        reference_names, candidate_names, case, "", tolerance,
                        quiet=True)
        print("selftest case    %-28s moving=%s (expect >0)"
              % (case, "n/a" if moving is None else len(moving)))
        ok = ok and moving is not None and len(moving) > 0
        print("SELFTEST %s" % ("OK" if ok else "FAILED"))
        return 0 if ok else 1

    for name in files:
        report(comparator, reference_dir, candidate_dir, reference_names,
               candidate_names, name, needle, tolerance)
    return 0


if __name__ == "__main__":
    sys.exit(main())
