#!/usr/bin/env python3
"""Print the verdict transitions between the committed floor and a verdicts TSV.

Reads pass-manifest.tsv (the floor the ratchet just advanced to) and a run's
verdicts TSV, and reports every row whose verdict changed, grouped by
transition.  Used to READ THE EXPOSED ROWS BY NAME before advancing a floor,
which is what the M1 plan entry asks for.
"""
import sys, collections

def load(path, floor):
    out = {}
    for line in open(path):
        if line.startswith("#") or not line.strip():
            continue
        f = line.rstrip("\n").split("\t")
        if len(f) >= 2:
            out[f[0]] = f[1]
    return out

floor = load(sys.argv[1], True)
run = load(sys.argv[2], False)

groups = collections.defaultdict(list)
for name, before in floor.items():
    after = run.get(name)
    if after is not None and after != before:
        groups[(before, after)].append(name)

for (before, after), names in sorted(groups.items(), key=lambda kv: -len(kv[1])):
    print("%s -> %s : %d" % (before, after, len(names)))
    for n in sorted(names):
        print("    " + n)
