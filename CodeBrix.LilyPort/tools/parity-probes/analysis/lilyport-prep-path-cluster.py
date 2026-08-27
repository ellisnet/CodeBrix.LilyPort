#!/usr/bin/env python3
"""PARITY 13 PREP: attribute the sole-kind `path` GLYPHS-DIFFER rows.

The comparator grades a <path> by its command-letter signature whenever its
transform is NOT a pure scale() -- that is where the ex-D36 compound-transform
glyph runs land, so the detail row says only `missing path:Mccc...z`.  This tool
re-reads both sides' SVGs, takes every path the comparator graded that way, and
resolves its outline bytes against the committed glyph-identity index (each side
against ITS OWN half, per D29) -- turning an anonymous signature diff into a
NAMED glyph diff, then clustering the files by that diff.

Classification per file:
  ORDER-ONLY  -- both sides drew the same resolved multiset in a different order
  NAME-DIFF   -- the multisets differ; the missing/extra names ARE the cluster key
  UNRESOLVED  -- a differing path resolves on neither side (engraver-drawn shape:
                 slur, tie, beam, bracket); clustered by signature diff instead

Imports compare-output.py as a module rather than copying it (trap 32a-ii: a
copied tool loses the data its directory holds), and asserts both index halves
loaded before trusting any output.

Usage: python3 lilyport-prep-path-cluster.py VERDICTS_TSV REF_DIR CAND_DIR OUT_TSV
"""

import collections
import hashlib
import importlib.util
import os
import sys
from xml.etree import ElementTree

HARNESS = os.path.expanduser(
    "~/GitHome/CodeBrix.Samples.Gpl3/CodeBrix.LilyPort/tools/regression-harness")

spec = importlib.util.spec_from_file_location(
    "compare_output", os.path.join(HARNESS, "compare-output.py"))
co = importlib.util.module_from_spec(spec)
spec.loader.exec_module(co)

INDEX = co.load_glyph_index(os.path.join(HARNESS, "glyph-identity.tsv"))
if not INDEX or len(INDEX) < 2:
    raise SystemExit("glyph-identity index did not load both halves: %r" %
                     sorted(INDEX or {}))
SIDES = sorted(INDEX)  # expect ['candidate', 'reference']


def resolve(d, side):
    """Name for the outline if this side's index knows it, else sig(<letters>)."""
    normalized = co.WHITESPACE.sub(" ", (d or "").strip())
    digest = hashlib.sha256(normalized.encode("utf-8")).hexdigest()
    names = INDEX[side].get(digest)
    if names:
        return "+".join(sorted(names)), True
    letters = "".join(sorted(c for c in (d or "") if c.isalpha()))
    return "sig(%s)" % letters, False


def nonglyph_paths(svg_path, side):
    """Every path the comparator grades as `path:`, resolved, in document order."""
    tree = ElementTree.parse(svg_path)
    out = []

    def visit(element):
        if element.tag == co.SVG_NS + "path":
            transform = element.get("transform") or ""
            if not co.GLYPH_TRANSFORM.match(transform):
                out.append(resolve(element.get("d"), side))
        for child in element:
            visit(child)

    visit(tree.getroot())
    return out


def main():
    verdicts_tsv, ref_dir, cand_dir, out_tsv = sys.argv[1:5]

    files = []
    with open(verdicts_tsv) as handle:
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 3 and parts[1] == "GLYPHS-DIFFER":
                detail = parts[2]
                # sole-kind path rows: every missing/extra term is a path term
                terms = [t.strip() for chunk in detail.split(";")
                         for t in [chunk.strip()] if t]
                kinds = set()
                for term in terms:
                    body = term.split(None, 1)[1] if " " in term else term
                    kinds.add(body.split(":", 1)[0])
                if kinds == {"path"}:
                    files.append(parts[0])

    clusters = collections.defaultdict(list)
    rows = []
    for name in files:
        ref_path = os.path.join(ref_dir, name)
        cand_path = os.path.join(cand_dir, name)
        if not (os.path.exists(ref_path) and os.path.exists(cand_path)):
            rows.append((name, "NO-FILE", "", ""))
            continue
        ref_seq = nonglyph_paths(ref_path, "reference")
        cand_seq = nonglyph_paths(cand_path, "candidate")
        ref_names = [n for n, _ in ref_seq]
        cand_names = [n for n, _ in cand_seq]
        if collections.Counter(ref_names) == collections.Counter(cand_names):
            if ref_names == cand_names:
                kind, key = "SAME-SEQUENCE", "(comparator saw a diff this walk did not)"
            else:
                kind = "ORDER-ONLY"
                moved = sorted({n for i, n in enumerate(ref_names)
                                if i < len(cand_names) and cand_names[i] != n})
                key = "reordered: " + ", ".join(moved[:6])
        else:
            missing = collections.Counter(ref_names) - collections.Counter(cand_names)
            extra = collections.Counter(cand_names) - collections.Counter(ref_names)
            kind = "NAME-DIFF"
            if all(k.startswith("sig(") for k in list(missing) + list(extra)):
                kind = "UNRESOLVED"
            key = "missing[%s] extra[%s]" % (
                ", ".join("%s x%d" % (k, v) for k, v in sorted(missing.items())),
                ", ".join("%s x%d" % (k, v) for k, v in sorted(extra.items())))
        clusters[(kind, key)].append(name)
        rows.append((name, kind, key,
                     "ref:%s | cand:%s" % (" ".join(ref_names[:40]),
                                           " ".join(cand_names[:40]))))

    with open(out_tsv, "w") as handle:
        handle.write("# path-row attribution, one row per file\n")
        for row in rows:
            handle.write("\t".join(row) + "\n")

    print("%d sole-kind path files" % len(files))
    print()
    by_size = sorted(clusters.items(), key=lambda kv: -len(kv[1]))
    for (kind, key), members in by_size:
        print("%-11s %3d rows  %s" % (kind, len(members), key[:150]))
        for member in members[:4]:
            print("            e.g. %s" % member)
    # reconstruction check (trap 32a): every input file must appear in exactly one row
    if len(rows) != len(files):
        print("SELF-CHECK FAIL: %d files, %d rows" % (len(files), len(rows)))
    else:
        print("\nself-check clean: %d files -> %d attributed rows" % (len(files), len(rows)))


if __name__ == "__main__":
    main()
