#!/usr/bin/env python3
"""Attribute sweep diagnostics to the input file that produced them.

The driver prints a file's RESULT line after running it, so every diagnostic
line since the previous result line belongs to the file named on the next one.
Only works on a merged (2>&1) sweep log.
"""
import re
import sys
from collections import Counter, defaultdict

RESULT = re.compile(r"^([A-Za-z0-9_.+-]+)\t(SVG|NOOUT|ERROR|MIDI|MIDI-FAIL|SIDE-FILE)\t")

CLASSES = {
    "baseline-skip":  re.compile(r"Type check for `baseline-skip' failed"),
    "staff-space":    re.compile(r"Type check for `staff-space' failed"),
    "not-breakable":  re.compile(r"bounds of this piece aren't breakable"),
    "spanner-invalid": re.compile(r"bounds of spanner are invalid"),
    "no-spacing":     re.compile(r"No spacing entry from (\w+) to `([\w-]+)'"),
}

pending = []
per_file = defaultdict(Counter)
pairs = defaultdict(Counter)
totals = Counter()

with open(sys.argv[1], errors="replace") as handle:
    for line in handle:
        match = RESULT.match(line)
        if match:
            name = match.group(1)
            for cls, count in pending:
                per_file[cls][name] += count
                totals[cls] += count
            # SIDE-FILE / MIDI lines are extra rows for the SAME file, so only a
            # terminal verdict closes the window.
            if match.group(2) in ("SVG", "NOOUT", "ERROR"):
                pending = []
            continue
        for cls, pattern in CLASSES.items():
            found = pattern.search(line)
            if found:
                pending.append((cls, 1))
                if cls == "no-spacing":
                    pairs[f"{found.group(1)} -> {found.group(2)}"][None] += 1
                break

for cls in ("baseline-skip", "staff-space", "not-breakable", "spanner-invalid", "no-spacing"):
    counter = per_file[cls]
    print(f"=== {cls}: {totals[cls]} occurrences across {len(counter)} file(s) ===")
    for name, count in counter.most_common(12):
        print(f"  {count:5d}  {name}")
    print()

print("=== no-spacing pairs ===")
for pair, counter in sorted(pairs.items(), key=lambda kv: -sum(kv[1].values())):
    print(f"  {sum(counter.values()):5d}  {pair}")
