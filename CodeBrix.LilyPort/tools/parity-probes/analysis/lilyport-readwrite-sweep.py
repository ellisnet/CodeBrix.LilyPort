#!/usr/bin/env python3
"""TRAP 17a SWEEP -- for every property / grob-array the port READS, find the
site that WRITES it.

PARITY 7 earned this sweep and did not run it.  `bounded-by-me` was READ
faithfully by `PaperColumn.IsUsed` -- upstream's order, upstream's comment copied
-- and written by NOTHING, because `Spanner.SetBound` never made the
`internal_set_as_bound_of_spanner` call that produces it.  A missing CALL leaves
no stub, no throw, and no ledger row, so nothing in the project could notice.
Cost: 403 + 228 diagnostics and 50 comparator rows, misread for four sessions as
a defect in `BreakIntoPieces`, which was faithful all along.

METHOD.  Properties are reached through interned symbols held in per-file
`private static readonly Symbol XSymbol = Symbol.Intern("x")` fields.  For each
.cs file we map field name -> interned string, then classify every accessor call
as a READ or a WRITE of that string.  The Scheme layer is scanned too, because a
property the C# side only reads may legitimately be written by vendored Scheme --
that is NOT a defect, and leaving it out would bury the real findings in noise.

A finding is: READ by the port, WRITTEN NOWHERE in the port.  That is the
`bounded-by-me` shape.  Each one is then a question for upstream: does upstream
write it?  If yes, it is a defect of exactly PARITY 7's kind.

Usage:  python3 lilyport-readwrite-sweep.py [--all] [--quiet]
"""

import collections
import os
import re
import sys

PORT = os.path.expanduser(
    "~/GitHome/CodeBrix.Samples.Gpl3/CodeBrix.LilyPort/src")

# field name -> interned symbol string
INTERN = re.compile(r'(\w+)\s*=\s*Symbol\.Intern\s*\(\s*"([^"]+)"\s*\)')
# a directly-inlined Symbol.Intern("x") inside a call
INLINE = re.compile(r'Symbol\.Intern\s*\(\s*"([^"]+)"\s*\)')

# call name -> which argument carries the symbol.  This is NOT uniform and getting
# it wrong silently inverts the result: `grob.GetProperty(Sym)` takes the symbol
# first, but the static `PointerGroupInterface.AddGrob(grob, Sym, x)` takes it
# SECOND, so reading argument 0 there records the GROB variable's name as the
# property and the real write disappears.  63 writes and 181 reads were lost to
# exactly that before this table existed.
READ_CALLS = {
    "GetProperty": 0, "GetObject": 0, "GetPropertyData": 0,
    "ExtractGrobSet": 1, "GetGrobArray": 1, "Count": 1,
}
WRITE_CALLS = {
    "SetProperty": 0, "SetObject": 0, "SetPropertyIfUnset": 0,
    "AddGrob": 1, "AddUnorderedGrob": 1, "SetOrdered": 1,
}

# Scheme-layer writers.  `(set! (ly:grob-property g 'sym) v)` and the bang forms.
SCM_WRITE = [
    re.compile(r"ly:grob-set-property!\s*[^)]*?'([\w:.<>=?!-]+)"),
    re.compile(r"ly:grob-set-object!\s*[^)]*?'([\w:.<>=?!-]+)"),
    re.compile(r"ly:context-set-property!\s*[^)]*?'([\w:.<>=?!-]+)"),
    re.compile(r"set!\s*\(\s*ly:grob-property\s+\S+\s+'([\w:.<>=?!-]+)"),
    re.compile(r"set!\s*\(\s*ly:grob-object\s+\S+\s+'([\w:.<>=?!-]+)"),
    re.compile(r"grob-set-property!\s*[^)]*?'([\w:.<>=?!-]+)"),
]
# A property NAMED in a .scm definition list (grob descriptions, interfaces)
# counts as written-by-data: the backend materialises it from the alist.
SCM_DEFINE = re.compile(r"\(\s*([a-z][\w.-]*)\s*\.\s")


def call_symbol_args(text, call, index):
    """Yield (argument `index`, offset) of each `call(` occurrence in `text`.

    Scans the WHOLE FILE, not a line at a time.  The port routinely wraps a call
    so that the symbol lands on the following line --

        prevPrimitive.SetProperty(
            FlexaIntervalSymbol, SchemeConvert.FromInt(pitch - prevPitch));

    -- and a line-based scan sees `SetProperty(` with an empty argument list, so
    the write vanishes and the property is reported as read-but-never-written.
    `flexa-interval` was a false finding for exactly this reason.
    """
    for match in re.finditer(r"\b" + re.escape(call) + r"\s*\(", text):
        depth = 0
        args = [[]]
        for i in range(match.end(), min(len(text), match.end() + 400)):
            char = text[i]
            if char in "([":
                depth += 1
            elif char in ")]":
                if depth == 0:
                    break
                depth -= 1
            elif char == "," and depth == 0:
                args.append([])
                continue
            args[-1].append(char)
        if len(args) > index:
            yield " ".join("".join(args[index]).split()), match.start()


def csharp_files():
    for root, _dirs, files in os.walk(PORT):
        if "/bin/" in root or "/obj/" in root:
            continue
        for name in sorted(files):
            if name.endswith(".cs"):
                yield os.path.join(root, name)


def global_symbol_table():
    """field name -> interned string, across the WHOLE port.

    A symbol constant is frequently declared in one file and used in another --
    `MaybeLoose` is declared in SpacingSpanner.cs and used to WRITE the property
    in SpacingDetermineLooseColumns.cs -- so a per-file table alone loses the
    write and reports the property as never written.  Names that map to two
    different strings anywhere in the tree are dropped rather than guessed.
    """
    table = {}
    conflicted = set()
    for path in csharp_files():
        with open(path, encoding="utf-8", errors="replace") as handle:
            for field, sym in INTERN.findall(handle.read()):
                if field in table and table[field] != sym:
                    conflicted.add(field)
                table[field] = sym
    for field in conflicted:
        table.pop(field, None)
    return table, conflicted


def scan_csharp(global_table):
    reads = collections.defaultdict(list)
    writes = collections.defaultdict(list)
    if True:
        for path in csharp_files():
            with open(path, encoding="utf-8", errors="replace") as handle:
                text = handle.read()
            table = dict(global_table)
            table.update(INTERN.findall(text))
            rel = os.path.relpath(path, PORT)
            for calls, sink in ((READ_CALLS, reads), (WRITE_CALLS, writes)):
                for call, index in calls.items():
                    if call not in text:
                        continue
                    for arg, offset in call_symbol_args(text, call, index):
                        sym = None
                        # A bare string literal: `SetProperty("is-title", true)`.
                        # The port uses both spellings interchangeably, and missing
                        # this one reports properties as unwritten that are written
                        # on the very next line of the same file.
                        literal = re.match(r'^"([^"]+)"$', arg)
                        inline = INLINE.search(arg)
                        if literal:
                            sym = literal.group(1)
                        elif inline:
                            sym = inline.group(1)
                        else:
                            # first identifier in the arg, possibly qualified
                            ident = re.match(r"[\w.]+", arg)
                            if ident:
                                sym = table.get(ident.group(0).split(".")[-1])
                        if sym:
                            lineno = text.count("\n", 0, offset) + 1
                            sink[sym].append("%s:%d" % (rel, lineno))
    return reads, writes


def scan_scheme():
    written = collections.defaultdict(list)
    defined = set()
    for root, _dirs, files in os.walk(PORT):
        if "/bin/" in root or "/obj/" in root:
            continue
        for name in files:
            if not name.endswith((".scm", ".ly")):
                continue
            path = os.path.join(root, name)
            with open(path, encoding="utf-8", errors="replace") as handle:
                text = handle.read()
            rel = os.path.relpath(path, PORT)
            for lineno, line in enumerate(text.splitlines(), 1):
                for pattern in SCM_WRITE:
                    for sym in pattern.findall(line):
                        written[sym].append("%s:%d" % (rel, lineno))
                for sym in SCM_DEFINE.findall(line):
                    defined.add(sym)
    return written, defined


UPSTREAM = os.path.expanduser("~/GitHome/lilypond/lily")

# Upstream's own write spellings.  These are the DECISIVE filter: a property the
# port reads and never writes is only a defect if UPSTREAM writes it.  Properties
# that come from grob descriptions or from the user's .ly are read-only on both
# sides and are not findings.
UP_WRITE = [
    re.compile(r'\bset_property\s*\([^;()]*?,\s*"([^"]+)"'),
    re.compile(r'\bset_object\s*\([^;()]*?,\s*"([^"]+)"'),
    re.compile(r'add_grob\s*\([^;]{0,120}?ly_symbol2scm\s*\(\s*"([^"]+)"', re.S),
    re.compile(r'set_property\s*\([^;()]*?,\s*ly_symbol2scm\s*\(\s*"([^"]+)"'),
]


def scan_upstream():
    """Symbols upstream WRITES from C++, with their sites."""
    written = collections.defaultdict(list)
    if not os.path.isdir(UPSTREAM):
        return written
    for name in sorted(os.listdir(UPSTREAM)):
        if not name.endswith((".cc", ".hh")):
            continue
        path = os.path.join(UPSTREAM, name)
        with open(path, encoding="utf-8", errors="replace") as handle:
            text = handle.read()
        for pattern in UP_WRITE:
            for match in pattern.finditer(text):
                sym = match.group(1)
                line = text.count("\n", 0, match.start()) + 1
                written[sym].append("%s:%d" % (name, line))
    return written


def main():
    show_all = "--all" in sys.argv
    global_table, conflicted = global_symbol_table()
    reads, writes = scan_csharp(global_table)
    scm_writes, scm_defined = scan_scheme()
    up_writes = scan_upstream()

    print("=" * 78)
    print("TRAP 17a SWEEP -- read sites without a write site")
    print("  C# properties READ:      %5d distinct" % len(reads))
    print("  C# properties WRITTEN:   %5d distinct" % len(writes))
    print("  Scheme-layer WRITTEN:    %5d distinct" % len(scm_writes))
    print("  Scheme-layer DEFINED:    %5d distinct (alist data)" % len(scm_defined))
    print("  UPSTREAM C++ WRITTEN:    %5d distinct" % len(up_writes))
    print("  symbol constants:        %5d resolved, %d name-conflicted (dropped)"
          % (len(global_table), len(conflicted)))

    unwritten = []
    for sym, sites in reads.items():
        if sym in writes or sym in scm_writes:
            continue
        unwritten.append((len(sites), sym, sites))
    unwritten.sort(reverse=True)

    # THE FINDINGS: read by the port, written nowhere in the port, and written by
    # upstream C++.  That is exactly the bounded-by-me shape.
    findings = [(c, s, sites) for c, s, sites in unwritten if s in up_writes]

    print()
    print("=" * 78)
    print("READ BUT NEVER WRITTEN, ANYWHERE IN THE PORT:  %d symbols" % len(unwritten))
    print("  of those, UPSTREAM WRITES:                   %d   <<< THE FINDINGS" % len(findings))
    print("  (the rest are read-only on both sides -- grob-description data or")
    print("   user .ly input -- and are not defects)")

    print()
    print("=" * 78)
    print("FINDINGS -- the bounded-by-me shape, ranked by port read sites")
    for count, sym, sites in findings:
        tag = " [data]" if sym in scm_defined else ""
        shown = ", ".join(sites[:3])
        if len(sites) > 3:
            shown += ", +%d more" % (len(sites) - 3)
        print("  %-34s %3d port read(s)%s" % (sym, count, tag))
        print("      port:     %s" % shown)
        up = up_writes[sym]
        up_shown = ", ".join(up[:3])
        if len(up) > 3:
            up_shown += ", +%d more" % (len(up) - 3)
        print("      upstream: %s" % up_shown)

    if show_all:
        print()
        print("=" * 78)
        print("READ/WRITE RATIO -- written, but far more read than written")
        ratio = []
        for sym, sites in reads.items():
            if sym in writes and len(writes[sym]) * 8 <= len(sites):
                ratio.append((len(sites), len(writes[sym]), sym))
        ratio.sort(reverse=True)
        for rcount, wcount, sym in ratio[:30]:
            print("  %-34s %3d read / %d write" % (sym, rcount, wcount))


if __name__ == "__main__":
    main()
