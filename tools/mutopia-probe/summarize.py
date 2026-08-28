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

When the results were produced with --oracle, the oracle columns are tallied too and three
further sections appear: every PORT-GAP row with what actually differs from upstream 2.27.2,
the tail of o_block_diff -- because the port-vs-oracle ink number sits around 0.001 for
an agreeing pair, so a row at 0.2 is worth reading even though the calibrated ladder still
calls it SIMILAR -- and every row whose SVG staff count differs, with the pages it differs on.
"""

import argparse
import collections
import csv
import sys

VERDICT_COLUMNS = ["convert", "engraved_from", "engrave", "pdf", "page_count", "page_size",
                   "text", "ink", "midi", "midi_notes", "midi_pitches"]

# Only tallied when the sweep was run with --oracle; "verdict" is the column the OBSERVATIONS
# document is sorted on.
# was previously: ... "o_staves" ... -- renamed 2026-08-28, and svg_staves added beside it. The
# raster staff count is reported and decides nothing; svg_staves, counted off the SVG structure
# per page, is the rung the verdict is cut on. See the README's A NOTE ON THE STAFF RUNG.
ORACLE_COLUMNS = ["oracle", "verdict", "verdict_pdf", "verdict_midi",
                  "o_page_count", "o_text", "o_ink", "svg_staves", "o_raster_staves",
                  "o_midi", "o_midi_channel"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("results")
    parser.add_argument("--worst", type=int, default=15)
    parser.add_argument("--by", default=None, help="tally every verdict column split by this column (e.g. declared_version)")
    arguments = parser.parse_args()

    with open(arguments.results, encoding="utf-8", newline="") as handle:
        rows = list(csv.DictReader(handle, delimiter="\t"))

    with_oracle = any(row.get("oracle") not in (None, "", "OFF") for row in rows)

    print("%d row(s) in %s%s" % (len(rows), arguments.results,
                                 "" if with_oracle else "   (no --oracle columns)"))
    print()
    for column in VERDICT_COLUMNS + (ORACLE_COLUMNS if with_oracle else []):
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

    if not with_oracle:
        return

    print()
    print("PORT-GAP rows -- the port differs from upstream 2.27.2 rendering the SAME source:")
    gaps = [r for r in rows if r.get("verdict") == "PORT-GAP"]
    gaps.sort(key=lambda r: -number(r, "o_block_diff"))
    for row in gaps:
        print("  %-42s %s" % (row["key"][-42:], why_port_gap(row)))
    if not gaps:
        print("  (none)")
    print()

    print("PORT-AHEAD / INPUT-REFUSED rows:")
    for row in rows:
        if row.get("verdict") in ("PORT-AHEAD", "INPUT-REFUSED"):
            print("  %-14s %-42s oracle %-8s err=%-4s %s" % (
                row["verdict"], row["key"][-42:], row.get("oracle"), row.get("oracle_errors"),
                (row.get("note") or "")[:70]))
    print()

    print("worst %d by o_block_diff (port vs oracle; an agreeing pair sits near 0.001):" % arguments.worst)
    against_oracle = [r for r in rows if r.get("o_ink") in ("SIMILAR", "LAYOUT-DIFFERS", "VERY-DIFFERENT")]
    against_oracle.sort(key=lambda r: -number(r, "o_block_diff"))
    for row in against_oracle[:arguments.worst]:
        print("  %-6s %-9s %s/%s pages vs oracle  %-14s %-18s %s" % (
            row["o_block_diff"], row["verdict"], row["pages_port"], row["oracle_pages"],
            row["o_ink"], row.get("svg_staves", ""), row["key"]))
    print()

    print("SVG staff differences per page (port/oracle), the layout rung the verdict is cut on:")
    differing = [r for r in rows if r.get("svg_staves") == "SVG-STAVES-DIFFER"]
    differing.sort(key=lambda r: r["key"])
    for row in differing:
        print("  %-9s %-42s %s" % (row["verdict"], row["key"][-42:], row.get("svg_staves_diff_pages")))
    if not differing:
        print("  (none)")
    print()

    print("oracle diagnostics on the converted source (its own errors, not the port's):")
    noisy = [r for r in rows if (r.get("oracle_errors") or "0") not in ("0", "")]
    noisy.sort(key=lambda r: -number(r, "oracle_errors"))
    for row in noisy[:arguments.worst]:
        print("  %-5s error(s)  port %-4s parse error(s)  %-9s %s" % (
            row["oracle_errors"], row.get("parse_errors"), row.get("verdict"), row["key"]))
    if not noisy:
        print("  (none)")


def why_port_gap(row):
    """One line naming which rungs of the port-vs-oracle ladder actually failed."""
    reasons = []
    if row.get("o_page_count") != "PAGES-EQUAL":
        reasons.append("pages %s vs %s (%s)" % (row.get("pages_port"), row.get("oracle_pages"), row.get("o_page_count")))
    # was previously: cut on o_staves, pairing staves_port (measured against MUTOPIA, over
    # min(port, Mutopia) pages) with o_staves_oracle (measured over min(port, oracle) pages) --
    # two counts over different page sets. The staff rung is now the SVG one, per page.
    if row.get("svg_staves") == "SVG-STAVES-DIFFER":
        reasons.append("staves %s vs %s on %s" % (
            row.get("svg_staves_port"), row.get("svg_staves_oracle"), row.get("svg_staves_diff_pages")))
    if row.get("o_ink") in ("LAYOUT-DIFFERS", "VERY-DIFFERENT"):
        reasons.append("ink %s (%s)" % (row.get("o_ink"), row.get("o_block_diff")))
    if row.get("o_text") in ("TEXT-DIFFERS", "TEXT-PORT-EMPTY"):
        reasons.append("text %s (bag %s)" % (row.get("o_text"), row.get("o_text_bag")))
    if row.get("verdict_midi") == "PORT-GAP":
        reasons.append("midi %s %s %s" % (row.get("o_midi"), row.get("o_midi_channel"), (row.get("o_midi_first_diff") or "")[:60]))
    if row.get("oracle") not in ("OK", "", None):
        reasons.append("oracle %s" % row.get("oracle"))
    return "; ".join(reasons) or "(see the row)"


def number(row, column):
    try:
        return float(row.get(column, "") or "nan")
    except ValueError:
        return float("nan")


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
