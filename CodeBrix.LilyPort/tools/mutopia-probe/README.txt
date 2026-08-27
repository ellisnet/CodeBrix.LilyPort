================================================================================
CodeBrix.LilyPort -- tools/mutopia-probe/
================================================================================

THE MUTOPIA PROBE. Opens every entry point of a locally downloaded Mutopia corpus with
the port, produces a PDF and a MIDI from each, and grades them against the PDF and MIDI
Mutopia published for the same source. One `dotnet run`, one row of results.tsv per
entry point. It ships nothing and is in no solution; it exists to produce the table an
OBSERVATIONS document is written from.

    MutopiaProbe/       the probe (net10.0 console; references CodeBrix.LilyPort in-repo,
                        plus the Html2Pdf and PdfRasterizer packages -- TOOL dependencies
                        under decision D52(a), never CodeBrix.LilyPort's)
    summarize.py        tallies a results.tsv and lists its worst rows (stdlib only)

--------------------------------------------------------------------------------
READ THIS FIRST: IT IS NOT A FIDELITY ORACLE
--------------------------------------------------------------------------------

The regression harness (tools/regression-harness) grades the port against the PINNED
LilyPond 2.27.2 rendering the SAME input, so a difference there is a port defect.

This tool cannot make that claim. Mutopia's PDF and MIDI for a piece were produced by
the LilyPond named in the piece's own \version -- 2.4 to 2.19 across the corpus, and
Ghostscript turned the PostScript into the PDF -- while the port is 2.27.2 and its
sources are first run through the port's convert-ly. A difference here is therefore
one of:

    (a) a PORT DEFECT       -- a crash, a parse error on converted input, no page, a
                               MIDI that is not an SMF, text missing from the page
    (b) a CONVERT-LY GAP    -- the conversion failed, or the converted file engraves
                               worse than the raw one did
    (c) VERSION DRIFT       -- spacing, page breaking, MIDI velocity/dynamics rules,
                               header layout: things upstream 2.27.2 would ALSO do
                               differently from the LilyPond that built the reference
    (d) INCONCLUSIVE

and the tool cannot tell which. Its job is to put every entry point on the same ladder
so a reader can. Sorting the rows into (a)-(d) is the OBSERVATIONS document's job.

--------------------------------------------------------------------------------
THE CORPUS
--------------------------------------------------------------------------------

A local download, NOT part of the repository and NEVER to be copied into it:

    ~/ClaudeHome/Mutopia/pieces/            100 pieces mirroring the server layout
    ~/ClaudeHome/Mutopia/pieces/ENTRY_POINTS.tsv
                                            path, pdf, mid, source_ly -- one row per
                                            reference PDF the corpus could match to
                                            the .ly whose stem produced it

The corpus root is a command-line argument; nothing about it is baked in. Every
scratch copy, engraved page, PDF, MIDI, PNG and the results table land under the OUTPUT
directory, which must also be outside the repository (the corpus's licences are
per-piece Public Domain / CC and its README says local testing only).

--------------------------------------------------------------------------------
RUNNING IT
--------------------------------------------------------------------------------

    cd tools/mutopia-probe/MutopiaProbe
    dotnet run -c Release -- ~/ClaudeHome/Mutopia/pieces ~/ClaudeHome/mutopia-probe-<date> \
        [--files key1,key2,...]      only entry points whose key contains one of these
        [--limit N]                  only the first N selected
        [--resume]                   skip keys already in results.tsv (append to it)
        [--retry-hung]               re-run entry points whose STARTED marker survived
        [--timeout-seconds N]        cooperative budget per file (default 300)
        [--dpi N]                    ink-grade resolution (default 100)
        [--no-ink]                   skip the raster grade entirely

    python3 ../summarize.py ~/ClaudeHome/mutopia-probe-<date>/results.tsv [--by declared_version]

The engine starts once (~20 s of Scheme boot) and every entry point runs in that one
session, exactly as BatchDriver does. Per-file times are seconds for a song and minutes
for an orchestral score.

A killed run loses nothing: results.tsv is appended and flushed per row, and --resume
continues after the last row. A file the engine never comes back from leaves its STARTED
marker behind; the next --resume run records it as HUNG and moves on (the cancellation
token is honoured only at the runner's own boundaries, so a runaway book cannot be
stopped from inside the process). --retry-hung tries such a file again.

Host requirement: pdftotext (Debian: poppler-utils) for the text grade. Without it the
text column reads TEXT-UNAVAILABLE and everything else still runs.

--------------------------------------------------------------------------------
WHAT ONE ENTRY POINT GOES THROUGH
--------------------------------------------------------------------------------

  1. CONVERT. The piece's directory is copied to <out>/pieces/<piece>/converted/ (sources
     only) and the port's DocumentConverter -- its convert-ly -- runs over every .ly/.ily
     in the copy, so relative \include lines keep resolving. An include file usually has
     no \version; it is converted FROM the highest version any file in the piece declares
     (upstream's convert-ly would refuse it without --from). convert.log records every
     file, rule count, "Not smart enough" message and the fallback used. Once per piece,
     shared by all its entry points.

  2. ENGRAVE. BatchRunner.RunFile on the converted source, from an emptied per-file
     scratch working directory (a .ly may write, relative to the cwd; the harness learnt
     that the hard way), point-and-click off, everything the engine prints captured to
     engrave.log. If that produced no page and the conversion had changed or failed on
     the piece, the RAW source is tried too and the row says which one was graded
     (engraved_from = converted | raw).

  3. PDF. The SVG pages become <stem>.pdf through Html2Pdf with SvgPlacement=Vector, each
     PDF page the SVG's own mm size, and the SVG's font-family attributes first rewritten
     to the engine's embedded text faces (C059 / Nimbus Sans / Nimbus Mono PS / TeX Gyre
     Schola), which are extracted from the engine's assets and registered with Html2Pdf.
     This is the route Fresco.Brix's PDF export uses, COPIED into Pdf/ (see the
     `//was previously:` lines) -- this tool must not reference the Fresco.Brix folder.

  4. GRADE THE PDF (every rung is reported, not just the first):
        page_count   PAGES-EQUAL | PAGES-DIFFER | PORT-MISSING | PORT-UNREADABLE | REF-UNREADABLE
        page_size    SIZE-EQUAL | SIZE-DIFFERS   (first page, within 1 pt; Mutopia's are A4)
        text         TEXT-EQUAL | TEXT-NEAR | TEXT-DIFFERS | TEXT-NONE | TEXT-REF-EMPTY |
                     TEXT-PORT-EMPTY | TEXT-UNAVAILABLE
                     -- pdftotext on both sides, ordered tokens, similarity = 2*LCS/(n+m)
        ink          SIMILAR | LAYOUT-DIFFERS | VERY-DIFFERENT | INK-SKIPPED
                     -- both PDFs rasterised by PDFium (page pairs up to the shorter
                     document); per page: ink ratio, staff count (rows of >= 30 % ink,
                     grouped into staves), ink IoU when the pixel sizes match, and the
                     BLOCK DIFFERENCE -- ink density on a 24-cell-wide grid, sum|a-b| /
                     max(sum a, sum b), 0 = identically distributed, ~1 = nothing in common.
                     block_diff is the number the verdict is cut on. The first two page
                     pairs are saved as compare/port-N.png and compare/ref-N.png.

  5. GRADE THE MIDI in compare-midi.py's vocabulary and with its four normalisations
     (absolute ticks, running status expanded, version stamp -> marker, end-of-track
     dropped; everything else exact, including same-tick order):
        midi         MATCH | EVENTS-DIFFER | TRACKS-DIFFER | MISSING | NOREF | UNPARSEABLE |
                     REF-UNPARSEABLE
     plus HOW they differ, because a 2.12-era reference differs from a 2.27 engine
     almost everywhere: midi_notes (NOTES-EQUAL when the multiset of (tick, pitch)
     note-ons is identical -- the notes, ignoring velocity and channel), midi_pitches
     (the multiset of pitches, ignoring time), track counts, division, last tick, note
     counts, tempo and program-change counts, the reference's version stamp, and the
     first differing event described on both sides.
     <out>/midi-crosscheck/{reference,candidate}/ hold .midi copies of every graded
     pair for the harness's own comparator:
        python3 tools/regression-harness/compare-midi.py <out>/midi-crosscheck/reference <out>/midi-crosscheck/candidate

--------------------------------------------------------------------------------
THE OUTPUT TREE
--------------------------------------------------------------------------------

    <out>/results.tsv                        one row per entry point (columns: Report/ResultRow.cs)
    <out>/skipped-no-source.tsv              ENTRY_POINTS rows with no source_ly (none in this corpus's table --
                                             its 29 unmatched PDFs were left out of the table altogether)
    <out>/fonts/text/                        the engine's text faces, extracted for Html2Pdf
    <out>/midi-crosscheck/                   see above
    <out>/pieces/<piece>/convert.log         the piece's conversion
    <out>/pieces/<piece>/converted/          the converted sources
    <out>/pieces/<piece>/<stem>/engrave.log  everything the engine printed
                                 <stem>.svg, <stem>-N.svg   the pages
                                 <stem>.midi                the performance(s)
                                 <stem>.pdf                 the PDF graded
                                 pdf-warnings.txt           Html2Pdf's warnings, when any
                                 compare/port-N.png, ref-N.png   the first page pairs
                                 side-files.txt             anything the file wrote to its cwd
                                 raw/                       the raw-source attempt, when one was made

--------------------------------------------------------------------------------
CALIBRATION
--------------------------------------------------------------------------------

Calibrated 2026-08-27 on 13 entry points chosen to span the corpus's \version range
(2.4.0 Thaxted, 2.6.0 lesgraces, 2.10.0 Wiegenlied x2, 2.11.34 Ave Maria, 2.12 Canon in D and
Toccata BWV 565, 2.14.2 WTK I/1 and amazing-mutopia, 2.16.1 Arban, 2.18.2 guitar-duo, 2.19.15
Hallelujah alto-score, 2.19.80 Mendelssohn Octet). The first run's numbers were WRONG on every
rung but page count, and each rule below is the correction, with the measurement that forced it.

THE INK GRADE: block_diff on an 8-COLUMN grid, SIMILAR <= 0.25, LAYOUT-DIFFERS <= 0.60, else
VERY-DIFFERENT (100 dpi, ink = grey < 200).

  The first grid was 24 columns (a cell ~8.7 mm, about one staff). Canon in D -- whose two
  pages are the same seven-bar-or-eight-bar music, the port breaking one system later -- graded
  1.00 (the clamp), because a system that moved a few millimetres vertically had left its
  cell entirely. The same pair against the same reference at coarser grids:

      columns      24     12      8      6      4
      CanonInD    1.00   0.93   0.48   0.30   0.18     same music, one line break moved
      wtk1-prel.  0.57   0.31   0.18   0.22   0.10     IDENTICAL layout to the eye
      Wiegenlied  0.91   0.61   0.55   0.54   0.41     same breaks, systems packed tighter
      Toccata p1  0.73   0.42   0.28   0.20   0.24     identical first page; 18 vs 11 pages later
      Arban       1.00   0.80   0.39   0.37   0.21

  Eight columns (a cell ~26 mm, three staves) is where the number first tracks the eye: an
  identical layout reads under 0.2, a moved line break about 0.5, a whole different page
  count over 0.6. Coarser than eight stops separating "one break moved" from "identical".
  Final calibration values at 8 columns: wtk1 0.18 and lesgraces 0.13 and guitar-duo 0.17
  (SIMILAR, and visually the same layout); Canon 0.51, Wiegenlied 0.49/0.54, Thaxted 0.47
  (LAYOUT-DIFFERS: same music, different breaks or spacing); Toccata 0.62 (VERY-DIFFERENT:
  the port produced 18 pages to the reference's 11). The ink IoU is reported but NOT cut on:
  it is 0.04-0.19 even for identical layouts, because a staff line is thinner than a pixel at
  this resolution and never lands on the same pixel twice.

  The STAFF COUNT is the shift-tolerant layout signal and agreed with the eye on every pair
  checked (Canon 6 vs 7 systems, wtk1 24 = 24). It is summed over the compared pages; the
  `staves` column says STAVES-EQUAL or STAVES-DIFFER.

THE TEXT GRADE: letter-run tokens, verdict on BAG containment >= 0.90.

  First run: every pair TEXT-DIFFERS, reference token counts 3-30x the port's. Mutopia's PDFs
  draw the notation glyphs with TEXT operators (Emmentaler through the PostScript backend), so
  pdftotext returns hundreds of private-use "words" per page that the port's path-based PDF
  does not have; the references also expose NO footer text at all, while the port's do. Then
  whether "www." ".org" is one token or three, and whether two lyric syllables at a line end
  come out as "be-" "Gu-" or "beGu-", depends on horizontal spacing alone. And pdftotext reads
  two verses under a system in whatever order their baselines fall, so an ordered LCS
  punished every reflowed lyric page (guitar-duo: 0.64 ordered, 0.99 as a bag, 698 vs 711
  words). Hence: tokens are maximal runs of LETTERS (numbers dropped -- bar numbers follow the
  line breaks, which is the ink grade's business), and the verdict is cut on the multiset
  containment of the shorter side in the longer. After the change: Canon 1.00, wtk1 1.00,
  lesgraces 1.00, guitar-duo 0.99, Wiegenlied 0.99, Ave Maria 0.98, alto-score 0.96 -- and
  Thaxted 0.88, correctly, because its title and composer really are missing from the port's
  page (see the OBSERVATIONS document: LilyPond 2.27.2 loses them too).
  Known residue in the port-only token lists: words run together across \markup \line items
  ("usingLilyPondby") -- the vector PDF places each word as its own text object and pdftotext
  does not always find the gap. A text-extraction artefact, not an engraving one.

THE MIDI GRADE: three verdicts, because one was useless.

  Every 2.10-2.12 reference is EVENTS-DIFFER at event 0 of track 0: the port names the control
  track after the piece's title where those releases wrote "control track", and the pre-2.6
  files carry a "creator:"/"at <date>" stamp. So `midi` alone said nothing. `midi_channel`
  (CHANNEL-EQUAL/DIFFER) sets every meta event aside and compares the performance -- notes
  with velocities, programs, controllers -- and `midi_notes` (NOTES-EQUAL/DIFFER) compares the
  multiset of (tick, pitch) note-ons, which is the notes themselves. Calibration: 9 of the 11
  pairs with a reference are NOTES-EQUAL; the CHANNEL-DIFFER first events are velocity 90 vs
  127 (2.10-era default), channel 0 vs 1, and program numbers -- release drift, all of them.
  The two NOTES-DIFFER are Thaxted (2.4.0, whose converted header fails to parse -- in 2.27.2
  upstream too) and lesgraces (2.6.0; the pitches match, the ticks do not).

  The pinned 2.27.2 oracle at ~/ClaudeHome/oracle/lilypond-2.27.2 turns "differs from Mutopia"
  into "differs from LilyPond": running it on the CONVERTED copy answered the three hardest
  calibration rows in minutes (the Octet's 706 errors and 39 pages are 2.27.2's own; Thaxted's
  lost header is 2.27.2's own; the Toccata's 18 pages against 2.27.2's 12 are the port's).
