#!/usr/bin/env python3
"""Rebuild the comparator residue histogram from a verdicts TSV.

Standing instruction in the Phase-4 plan (section 3): rebuild this at the START of
every parity session.  It is ~3 minutes of arithmetic on a file every sweep already
writes, and it RE-RANKS the work -- it is what named D0 at PARITY 3 and D10 at
PARITY 4.

Rank by measured ROW IMPACT, never by how loud a defect is in a log.

PARITY 8 extended it to GLYPHS-DIFFER.  Through PARITY 7 this tool read
PLACEMENT-DIFFERS only, which is why the plan could say GLYPHS-DIFFER "nothing has
ever attacked directly" while also saying the histogram re-ranks the work every
session -- the tool was structurally blind to the block holding the mass.  The
GLYPHS-DIFFER sections below cluster by ELEMENT KIND and, critically, isolate the
rows where a single kind is the ONLY difference: that is how D6's 53 rect-only rows
were found by hand, and it generalises to every other kind.

PARITY 11's opening rebuild fixed the second blind spot, and it is trap 32a again:
the term scanner SPLIT on ", ", but a text signature carries the raw CSS
font-family LIST, so "text:Linux Libertine O, serif:2.2000:X x1" was cut in half.
The head fragment carried no count and was DROPPED; the tail was read as a term of
a phantom kind "serif".  Seven rows -- the D31 tofu row and the CJK/Hebrew
one-line-breaking family -- were filed under a kind that does not exist while their
real kind, text, was undercounted.  Terms are now SCANNED against the closed kind
vocabulary, and any half that does not reconstruct exactly is reported.

KNOWN LIMIT OF THE DATA (not of this tool): compare-output.py emits at most
most_common(4) terms PER SIDE, so "sole kind" means sole among the top four of
each side.  A row with a fifth term of another kind reads as sole-kind here.

Usage:  python3 lilyport-residue-histogram.py /tmp/verdicts.tsv [-n TOP]
"""

import collections
import re
import sys

ROW = re.compile(r"^(?P<name>\S+)\t(?P<verdict>[A-Z-]+)\t(?P<detail>.*)$")
WORST = re.compile(r"worst (?P<delta>[0-9.]+) at (?P<elem>\S+)")

# The CLOSED set of element kinds, read off compare-output.py's own name
# construction (the authority, not a guess -- rule 35a).  A signature is one of
#   glyph:<names>@<scale> | glyph:<digest> | path:<letters> | use:<href>
#   line:<stroke-width>   | text:<font-family>:<font-size>:<content>
#   rect | polygon | circle | ellipse      (bare SVG tag names)
KINDS = r"(?:glyph|path|use|line|text|rect|polygon|circle|ellipse)"

# A term is "<signature> x<count>", terms are joined with ", ".  The signature
# is scanned, NEVER split on ", ", because two of its fields legitimately
# contain a comma-space: a text term carries the raw CSS font-family LIST
# ("Linux Libertine O, serif") and its CONTENT is arbitrary page text.  Splitting
# misfiled every such row under a phantom kind taken from the tail of the family
# list -- see the module docstring.
TERM_SCAN = re.compile(r"(?P<kind>" + KINDS + r"(?::.*?)?) x(?P<count>\d+)(?=, |$)")

# "missing ...", "extra ...", joined with "; ".
HALF_SCAN = re.compile(r"(?P<side>missing|extra) (?P<body>.*?)(?=; (?:missing|extra) |$)")


def parse_glyph_detail(detail, unconsumed=None):
    """Split a GLYPHS-DIFFER detail into (side, kind, count) triples.

    Detail grammar: "missing <term>, <term>; extra <term>, <term>", either half
    optional.  The scale suffix on a glyph term is dropped -- it is the font size
    the path was resolved at, not an identity -- but the glyph NAME is kept,
    because D29 makes the name the identity.

    ``unconsumed``, if given, receives any half whose terms do not reconstruct
    the half exactly.  That is the tool's own fence: a term the scanner drops is
    a row classified on partial evidence, which is how the phantom kinds got in.
    """
    triples = []
    for half in HALF_SCAN.finditer(detail):
        side, body = half.group("side"), half.group("body")
        matches = list(TERM_SCAN.finditer(body))
        if unconsumed is not None:
            rebuilt = ", ".join(m.group(0) for m in matches)
            if rebuilt != body:
                unconsumed.append((side, body, rebuilt))
        for match in matches:
            kind = match.group("kind")
            if kind.startswith("glyph:"):
                kind = "glyph:" + kind[len("glyph:"):].split("@")[0]
            triples.append((side, kind, int(match.group("count"))))
    return triples


def bare_kind(kind):
    """'glyph:accidentals.sharp' -> 'glyph'; 'rect' -> 'rect'."""
    return kind.split(":")[0]


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "/tmp/verdicts.tsv"
    top = int(sys.argv[sys.argv.index("-n") + 1]) if "-n" in sys.argv else 20

    verdicts = collections.Counter()
    by_delta = collections.defaultdict(list)     # exact worst delta -> [rows]
    by_element = collections.Counter()           # worst-offender element kind
    by_band = collections.Counter()

    # GLYPHS-DIFFER
    gd_rows = []                                 # (name, [(side, kind, count)])
    gd_kind_rows = collections.defaultdict(set)  # bare kind -> {rows}
    gd_sole_kind = collections.defaultdict(list) # bare kind -> [(name, detail)]
    gd_sole_named = collections.defaultdict(list)# exact kind -> [(name, count)]
    gd_side = collections.Counter()
    gd_unparsed = []
    gd_unconsumed = []                           # (row, side, body, rebuilt)

    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if line.startswith("#") or not line.strip():
                continue
            match = ROW.match(line)
            if not match:
                continue
            verdict = match.group("verdict")
            if verdict == "verdict":
                continue
            verdicts[verdict] += 1
            name = match.group("name")
            detail = match.group("detail")

            if verdict == "GLYPHS-DIFFER":
                leftovers = []
                triples = parse_glyph_detail(detail, leftovers)
                for side, body, rebuilt in leftovers:
                    gd_unconsumed.append((name, side, body, rebuilt))
                if not triples:
                    gd_unparsed.append((name, detail))
                    continue
                gd_rows.append((name, triples))
                kinds = {bare_kind(k) for _, k, _ in triples}
                for kind in kinds:
                    gd_kind_rows[kind].add(name)
                if len(kinds) == 1:
                    only = next(iter(kinds))
                    gd_sole_kind[only].append((name, detail))
                    exact = {k for _, k, _ in triples}
                    if len(exact) == 1:
                        total = sum(c for _, _, c in triples)
                        gd_sole_named[next(iter(exact))].append((name, total))
                sides = {s for s, _, _ in triples}
                gd_side["missing only" if sides == {"missing"} else
                        "extra only" if sides == {"extra"} else "both"] += 1
                continue

            if verdict != "PLACEMENT-DIFFERS":
                continue
            worst = WORST.search(detail)
            if not worst:
                continue
            delta = worst.group("delta")
            elem = worst.group("elem").split(":")[0]
            by_delta[delta].append(name)
            by_element[elem] += 1
            value = float(delta)
            if value < 0.02:
                by_band["(a) under 0.02"] += 1
            elif value < 0.1:
                by_band["(b) 0.02 - 0.1"] += 1
            elif value < 2:
                by_band["(c) 0.1 - 2"] += 1
            elif value < 10:
                by_band["(d) 2 - 10"] += 1
            else:
                by_band["(e) above 10"] += 1

    print("=" * 78)
    print("VERDICTS")
    total = sum(verdicts.values())
    for verdict, count in verdicts.most_common():
        print("  %-20s %5d   (%.1f%%)" % (verdict, count, 100.0 * count / total))
    print("  %-20s %5d" % ("TOTAL", total))

    print()
    print("=" * 78)
    print("PLACEMENT-DIFFERS -- CONSTANT CLUSTERS (identical worst delta = one")
    print("disabled rule, not accumulated drift; this is what named D0 and D10)")
    ranked = sorted(by_delta.items(), key=lambda kv: -len(kv[1]))
    for delta, rows in ranked[:top]:
        if len(rows) < 2:
            continue
        print("  %8s  %4d rows   e.g. %s" % (delta, len(rows), ", ".join(sorted(rows)[:3])))

    print()
    print("=" * 78)
    print("PLACEMENT-DIFFERS -- BY WORST-OFFENDER ELEMENT")
    for elem, count in by_element.most_common():
        print("  %-12s %5d" % (elem, count))

    print()
    print("=" * 78)
    print("PLACEMENT-DIFFERS -- BY MAGNITUDE BAND")
    for band, count in sorted(by_band.items()):
        print("  %-16s %5d" % (band, count))

    print()
    print("=" * 78)
    print("NEAR-MISS TAIL -- rows a small correction would flip to MATCH")
    under = [(float(d), len(r)) for d, r in by_delta.items() if float(d) < 0.05]
    under.sort()
    cumulative = 0
    for delta, count in under:
        cumulative += count
        print("  worst %-8s %4d rows   (cumulative %4d)" % (delta, count, cumulative))

    # ---------------- GLYPHS-DIFFER: the block holding the mass ----------------

    print()
    print("=" * 78)
    print("GLYPHS-DIFFER -- BY ELEMENT KIND (rows mentioning the kind AT ALL;")
    print("rows counted once per kind, so the column sums above the row total)")
    for kind, rows in sorted(gd_kind_rows.items(), key=lambda kv: -len(kv[1])):
        print("  %-12s %5d rows" % (kind, len(rows)))

    print()
    print("=" * 78)
    print("GLYPHS-DIFFER -- SOLE-KIND ROWS  <<< THE ENTRY POINTS")
    print("Rows where ONE element kind is the ONLY thing that differs.  A sole-kind")
    print("cluster is a single mechanism, the way D6's 53 rect-only rows were.")
    for kind, rows in sorted(gd_sole_kind.items(), key=lambda kv: -len(kv[1])):
        print("  %-12s %5d rows   e.g. %s" % (kind, len(rows),
                                              ", ".join(sorted(n for n, _ in rows)[:3])))

    print()
    print("=" * 78)
    print("GLYPHS-DIFFER -- SOLE *NAMED* KIND (one exact glyph name / element, top %d)" % top)
    print("The sharpest form: the whole page differs by ONE named thing.")
    ranked_named = sorted(gd_sole_named.items(), key=lambda kv: -len(kv[1]))
    for kind, rows in ranked_named[:top]:
        if len(rows) < 2:
            continue
        print("  %-46s %4d rows  (counts %s)" % (
            kind[:46], len(rows),
            ", ".join(str(c) for _, c in sorted(rows, key=lambda r: -r[1])[:4])))

    print()
    print("=" * 78)
    print("GLYPHS-DIFFER -- BY SIDE")
    for side, count in gd_side.most_common():
        print("  %-12s %5d rows" % (side, count))
    if gd_unparsed:
        print()
        print("  UNPARSED details: %d  e.g. %s" % (
            len(gd_unparsed), gd_unparsed[0][1][:60]))

    print()
    print("=" * 78)
    print("SCANNER SELF-CHECK -- halves whose terms do not reconstruct the half")
    print("(trap 32a: a tool's first answer is about the tool.  Nonzero here means")
    print("rows above are classified on partial evidence.)")
    if not gd_unconsumed:
        print("  CLEAN -- every term in every GLYPHS-DIFFER detail was consumed.")
    else:
        print("  %d half/halves did NOT reconstruct:" % len(gd_unconsumed))
        for row, side, body, rebuilt in gd_unconsumed[:10]:
            print("    %s [%s]" % (row, side))
            print("      got  %s" % body[:150])
            print("      read %s" % rebuilt[:150])


if __name__ == "__main__":
    main()
