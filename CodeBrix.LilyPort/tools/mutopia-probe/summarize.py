#!/usr/bin/env python3
"""summarize.py -- tally a mutopia-probe results.tsv and list its worst rows.

This file is part of CodeBrix.LilyPort.
Copyright (c) 2026 Jeremy Ellis and contributors

CodeBrix.LilyPort is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Usage:  summarize.py RESULTS_TSV [--worst N] [--by COLUMN]

Standard library only. Prints, per verdict column, the count of each verdict; then the
rows with the highest block_diff (the ink grade's number) and the rows that produced no
page at all, so the OBSERVATIONS document can be written from one screen of output.
"""

import argparse
import collections
import csv
import sys

VERDICT_COLUMNS = ["convert", "engraved_from", "engrave", "pdf", "page_count", "page_size",
                   "text", "ink", "midi", "midi_notes", "midi_pitches"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("results")
    parser.add_argument("--worst", type=int, default=15)
    parser.add_argument("--by", default=None, help="tally every verdict column split by this column (e.g. declared_version)")
    arguments = parser.parse_args()

    with open(arguments.results, encoding="utf-8", newline="") as handle:
        rows = list(csv.DictReader(handle, delimiter="\t"))

    print("%d row(s) in %s" % (len(rows), arguments.results))
    print()
    for column in VERDICT_COLUMNS:
        counts = collections.Counter(row.get(column, "") or "(blank)" for row in rows)
        print("%-14s %s" % (column, "  ".join("%s=%d" % (k, v) for k, v in sorted(counts.items()))))
    print()

    if arguments.by:
        groups = collections.defaultdict(list)
        for row in rows:
            groups[row.get(arguments.by, "")].append(row)
        for key in sorted(groups, key=version_key):
            group = rows_summary(groups[key])
            print("%-12s n=%-3d %s" % (key or "(blank)", len(groups[key]), group))
        print()

    def number(row, column):
        try:
            return float(row.get(column, "") or "nan")
        except ValueError:
            return float("nan")

    graded = [r for r in rows if r.get("ink") in ("SIMILAR", "LAYOUT-DIFFERS", "VERY-DIFFERENT")]
    graded.sort(key=lambda r: -number(r, "block_diff"))
    print("worst %d by block_diff:" % arguments.worst)
    for row in graded[:arguments.worst]:
        print("  %-6s %s/%s pages  text %-14s %-15s %s  %s" % (
            row["block_diff"], row["pages_port"], row["pages_ref"], row["text"], row["ink"], row["midi"], row["key"]))
    print()

    print("no page produced:")
    for row in rows:
        if row.get("engrave") != "OK":
            print("  %-8s %-14s %s  %s" % (row.get("engrave"), row.get("convert"), row["key"], (row.get("error") or "")[:100]))
    print()

    print("parse errors reported while still producing a page:")
    for row in rows:
        if row.get("engrave") == "OK" and (row.get("parse_errors") or "0") not in ("0", ""):
            print("  %s parse error(s)  %s" % (row["parse_errors"], row["key"]))
    print()

    print("midi first differences (EVENTS-DIFFER / TRACKS-DIFFER):")
    for row in rows:
        if row.get("midi") in ("EVENTS-DIFFER", "TRACKS-DIFFER"):
            print("  %-13s %-13s %s  %s" % (row.get("midi_notes"), row.get("midi_pitches"), row["key"], (row.get("midi_first_diff") or "")[:110]))


def rows_summary(group):
    parts = []
    for column in ("engrave", "page_count", "text", "ink", "midi"):
        counts = collections.Counter(r.get(column, "") or "-" for r in group)
        parts.append(column + "{" + ",".join("%s:%d" % (k, v) for k, v in sorted(counts.items())) + "}")
    return " ".join(parts)


def version_key(text):
    parts = []
    for piece in (text or "").replace("(", "").replace(")", "").split("."):
        try:
            parts.append(int(piece))
        except ValueError:
            parts.append(0)
    return parts


if __name__ == "__main__":
    sys.exit(main())
