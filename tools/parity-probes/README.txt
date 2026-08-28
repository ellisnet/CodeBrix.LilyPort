CodeBrix.LilyPort -- tools/parity-probes
================================================================================

WHAT THIS IS

The measurement tools the port's RULINGS rest on.  When a decision in
PORT-COVERAGE or on the LilyPort board says a value was "measured", these are
what measured it: the probe that isolated the behaviour, the script that paired
the marks, the driver that ran both engines over the same input.

Vendored here on 2026-08-27 by Jeremy's ruling (board item L8, audit §2.10).
They had lived only in ~/ClaudeHome -- outside version control, on one machine,
in a scratch-adjacent folder.  That was recorded as deliberate FOR ONE SESSION
in the PARITY-13 prep and nothing revisited it, and in the meantime the set grew
from 12 directories to 17.  It is 18 as of 2026-08-27: lilyport-probe-pango-size
was written straight into the repository, which is what vendoring the rest was
for.  The precedent for acting was already on the record:
the api-spec's own measurement scripts (freevars.py, cat.py) are GONE, so that
spec's headline counts are no longer re-derivable by anyone.  A ruling nobody
can reproduce the measurement for is a ruling on trust.


THE DIRECTORY NAMES ARE DELIBERATELY UNCHANGED

Every directory keeps the `lilyport-probe-` prefix it was cited under.  This is
not tidiness lost by accident: the traps and rulings name these paths in prose
-- standing trap 28 cites `lilyport-probe-parity16/drift.py`,
`parity18/ydrift.py` and `parity25/residue.py --candidate=/tmp/gate` by name --
and renaming would silently break every one of those citations.  A reader who
follows a trap to a path must land on the file.


WHAT IS HERE

  lilyport-probe-barnumber/       bar-number placement
  lilyport-probe-break-align.ly   break-alignment probe (a loose file, as it was)
  lilyport-probe-chordgrid/       chord grids
  lilyport-probe-crossstaff/      cross-staff spanners
  lilyport-probe-glyph-skyline/   glyph skyline extraction (R9/R20's ground)
  lilyport-probe-jump-mark/       jump marks
  lilyport-probe-ledger/          ledger lines; carries its oracle-out /
                                  port-fixed / port-prefix captures
  lilyport-probe-pango-size/      what pango_font_description_to_string ACTUALLY
                                  writes for a size (R10/R12's ground).  Written
                                  here rather than in ~/ClaudeHome, which is the
                                  point of this directory existing.  pango_desc.py
                                  drives libpango through ctypes and has a
                                  selfcheck; size_solver.py predicts the corpus's
                                  font-size values from the measured rule
  lilyport-probe-parity16..26/    the PARITY waves' own probes, one per wave;
                                  parity16 drift.py, parity18 ydrift.py and
                                  parity25 residue.py are the y-band mark
                                  pairers standing trap 28 sends you to
  lilyport-probe-volta/           volta / measure-length (D6)

  analysis/lilyport-residue-histogram.py            residue distribution
  analysis/lilyport-prep-path-cluster.py            path clustering prep
  analysis/lilyport-readwrite-sweep.py              property read/write sweep
  analysis/lilyport-otf-metrics.py                  OTF metric extraction
  analysis/lilyport-attribute-sweep-diagnostics.py  diagnostics attribution


HOW TO RUN THEM

They are historical instruments, not a suite: each was written to answer ONE
question, and several hard-code a path into ~/ClaudeHome or /tmp from the
session that produced them.  Read the probe before running it.  Nothing here is
wired into the build, into any test project, or into the regression harness, and
nothing here runs on its own.

Two standing rules still bind anything you run from this directory:

  * Rule 7 -- ~/GitHome/lilypond and ~/GitHome/guile are READ-ONLY reference.  A
    probe reads the checkout; no probe writes to it.

  * Standing trap 29 -- a probe that overrides a property with a default changes
    what it measures on BOTH engines, and a probe's environment must match the
    corpus's.  Use ly:message, not display: BatchDriver routes the output port
    elsewhere.


PROVENANCE AND LICENSING

The `.ly` probes here are the PORT'S OWN, written to isolate a behaviour -- they
carry their own comments naming the decision each was cut for.  They are not
copies of upstream's regression corpus; where a probe was derived from a corpus
file, its own header says so.

The captured outputs (`oracle-out`, `port-fixed`, `port-prefix`, the .svg and
metric dumps) are PROGRAM OUTPUT, which is rule 17's case (a): a fixture
recording what a program produced does not inherit that program's licence.

Nothing in this directory is packed into CodeBrix.LilyPort.GplLicenseForever --
`tools/` is not in the package at all, verified 2026-08-27 against the packing
rules in src/CodeBrix.LilyPort/CodeBrix.LilyPort.csproj.
