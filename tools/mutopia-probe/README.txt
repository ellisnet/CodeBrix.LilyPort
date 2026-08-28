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

UNLESS --oracle IS PASSED. Then the pinned 2.27.2 renders the SAME source the port was
handed and the row carries a SECOND grade beside the first, and the question becomes
decidable: if the port agrees with 2.27.2, whatever separates BOTH of them from Mutopia
is (c); if it does not, the difference is the port's and is (a). See THE ORACLE MODE
below. It roughly doubles the run time.

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
        [--oracle [PATH]]            ALSO run the pinned upstream LilyPond on the same
                                     source and grade port-vs-oracle (see below). PATH
                                     defaults to
                                     ~/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond
        [--oracle-timeout-seconds N] the oracle's budget (default: the port's). Unlike
                                     the port's cooperative token this one is a real
                                     kill, process tree and all.

    python3 ../summarize.py ~/ClaudeHome/mutopia-probe-<date>/results.tsv [--by declared_version]

RE-GRADING A SWEEP THAT HAS ALREADY RUN, without engraving anything:

    dotnet run -c Release -- ~/ClaudeHome/Mutopia/pieces ~/ClaudeHome/mutopia-probe-<newdate> \
        --regrade ~/ClaudeHome/mutopia-probe-<olddate>  [--dpi N] [--no-ink]

    Reads the OLD run's artefacts -- its SVG pages, PDFs and MIDIs, both sides -- re-runs every
    grade over them, and writes a fresh results.tsv into the NEW directory. The old run is opened
    read-only and is not written to. Neither engraver runs, so what would be an 85-minute sweep
    is 106 seconds (measured, 227 rows).

    Every GRADED column is recomputed. Every column that describes the RUN -- convert, engrave,
    parse_errors, systems, svg_pages, midi_files, pdf, oracle, oracle_seconds, oracle_errors,
    oracle_warnings, oracle_pages, seconds -- is carried forward from the old table cell for cell,
    because it cannot be recovered from the artefacts and is not something to guess at. Cells are
    matched BY COLUMN NAME, so a table written before a column was renamed still reads.
    Not reproduced: the comparison PNGs and the midi-crosscheck copies; the old run still has them.

    Use it when the GRADING changed and the engraving did not. When the engine changed, re-run
    the sweep: a regrade would grade the old pages.

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
                     document); per page: ink ratio, ink IoU when the pixel sizes match, and
                     the BLOCK DIFFERENCE -- ink density on a 24-cell-wide grid, sum|a-b| /
                     max(sum a, sum b), 0 = identically distributed, ~1 = nothing in common.
                     block_diff is the number the verdict is cut on. The first two page
                     pairs are saved as compare/port-N.png and compare/ref-N.png.
        raster_staves
                     STAVES-EQUAL | STAVES-DIFFER  -- ⚠ REPORTED, NOT BELIEVED. Rows of
                     >= 30 % ink, grouped into staves, summed over the compared pages. It
                     decides nothing (see A NOTE ON THE STAFF RUNG); it is kept because it
                     is the only staff signal available against MUTOPIA, which published a
                     PDF and no SVG.

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

  6. THE ORACLE, when --oracle was passed. See the next section.

--------------------------------------------------------------------------------
THE ORACLE MODE (--oracle)
--------------------------------------------------------------------------------

The pinned upstream LilyPond renders the SAME .ly the port was given -- the converted
copy, or the raw one on the rows where that is what the port was graded from -- and the
row gets a second set of columns holding the port-vs-oracle grade, plus the verdict the
whole mode exists for.

WHAT IS HELD IDENTICAL, so that what the second grade measures is ENGRAVING and nothing
else:

    the source         byte for byte the file the port read (engraved_from says which)
    the backend        --formats=svg -dbackend=svg, the same pages the port produces
    point and click    -dno-point-and-click on both sides
    the working dir    a fresh emptied scratch directory per file, as for the port
    the PDF route      the oracle's SVG pages go through THIS TOOL'S ScorePdfWriter,
                       the same one the port's went through -- not through LilyPond's
                       own PDF backend and not through Ghostscript. So the PDF writer,
                       the page box rule and the font embedding are common-mode and
                       cancel out.
    the fonts          under -dbackend=svg, ly/paper-defaults-init.ly makes LilyPond
                       name the CSS GENERIC families, so Pango would resolve
                       serif/sans/monospace through the HOST's fontconfig and measure
                       the oracle's text in whatever this machine has installed. The
                       tool writes <out>/fonts/oracle-fonts.conf -- mirroring
                       tools/regression-harness/reference-fonts.conf.in -- pinning them
                       to the oracle's OWN bundled faces, which are the same files the
                       port vendors, with no system directory in scope. If those fonts
                       cannot be found the tool REFUSES to run the oracle rather than
                       record the host's.

    --silent is NOT passed: the oracle's own warnings and errors on the converted source
    are half of what the mode is for (oracle_errors, oracle_warnings, oracle.log).

A NOTE ON --oracle-timeout-seconds. The default is the port's budget (300 s). The
Mendelssohn Octet's full score needs 395 s of oracle time, and below that the oracle is
killed mid-render and the row reads INCONCLUSIVE (see the verdict ladder). For a full
corpus sweep pass --oracle-timeout-seconds 900.

WHAT THIS MODE FOUND, 2026-08-27. Besides sizing the port's real gaps, the sweep exposed
a defect in the engine's per-file isolation: a file whose parse died inside a \layout,
\midi, \paper or \with block left its scope on the stack, and from then on every later
file's toplevel assignments landed in that orphan instead of the base scope -- where
RestoreToplevelScope could not see them to remove them. One corpus file rebinds \< for
its own use, and the rebinding escaped into every later file. Fixed in
LilyParserSession.RestoreToplevelScope (the scope stack is now trimmed to its snapshot
depth) and reported per run by BatchRunner. 27 of 227 rows changed when the sweep was
re-run with the fix. See OBSERVATIONS_lilyport_mutopia_2026-08-27.txt section U1.

THE VERDICT LADDER (Report/DriftVerdict.cs). Two axes, pages and performance, each
graded on the same rungs as the Mutopia comparison, then the worse of the two:

    CLEAN          port = oracle, and Mutopia agrees too
    DRIFT          port = oracle, Mutopia differs -- upstream 2.27.2 differs from
                   Mutopia the same way. Bucket (c).
    PORT-GAP       port /= oracle on the same source. The port's, whatever Mutopia
                   shows. Bucket (a).
    INPUT-REFUSED  neither produced anything: 2.27.2 refuses the converted source too.
                   Bucket (b) -- convert-ly, or a source that needs something absent.
    PORT-AHEAD     the port produced output where 2.27.2 produced none. Not a defect;
                   not agreement either. Bucket (d).
    INCONCLUSIVE   the oracle did not FINISH (killed by --oracle-timeout-seconds, or it
                   would not launch), so what it left behind is a partial render and
                   neither agreement nor disagreement with it means anything. Re-run
                   the row with a bigger budget. Measured 2026-08-27: the Mendelssohn
                   Octet's full score needed 394 s and, killed at 300, had written 31
                   of its 39 pages -- the row read PORT-GAP "39 vs 31 pages" purely
                   because of the kill, and read PAGES-EQUAL at 0.001 once re-run.
    NOT-GRADED     that axis has nothing on either side (no \midi in the source).
    NO-ORACLE      --oracle was not passed.

"Agrees" on the page axis means PAGES-EQUAL and ink not LAYOUT-DIFFERS/VERY-DIFFERENT and
text not TEXT-DIFFERS/TEXT-PORT-EMPTY -- and, against the ORACLE only, SVG-STAVES-EQUAL. On
the performance axis it means MATCH or CHANNEL-EQUAL against the oracle -- a stricter bar
than the NOTES-EQUAL used against Mutopia, because velocity defaults, channel numbering
and program numbers changed between the corpus's releases and 2.27.2 but must not
change between the port and the version it targets.

A NOTE ON THE STAFF RUNG.  ** Replaced 2026-08-28; the raster count decides nothing now. **

WHAT IT USED TO BE. Both PDFs were rasterised and a page row holding >= 30 % ink counted as
a staff line; the staves were summed over the whole document and a difference in that sum was
enough, on its own, for PORT-GAP. It was the sole basis of 36 of the 72 PORT-GAP verdicts of
the 2026-08-27 sweep, and two measurements showed it could not carry them: re-graded at 200 dpi
instead of 100, one checked pair REVERSED SIGN (9/8 became 8/9 at o_block_diff 0.002), and when
the Html2Pdf pin moved -- a change that altered no page geometry at all -- four newly-drawn
footer glyphs put enough ink in one raster row to count as a staff and manufactured two PORT-GAP
verdicts. A rung a font-coverage change can flip has no business deciding anything.

WHAT IT IS NOW (Compare/SvgStaves.cs). The staves are counted from the SVG the engraver wrote,
which is a property of the DOCUMENT: no resolution, no PDF library and no font can move it. Both
sides emit LilyPond-style SVG -- the port's SvgBackend is a port of upstream's -- so ONE
algorithm reads both. Every <line> is placed by summing its ancestors' translate(); a line is
horizontal when |y1-y2| <= 1e-4; horizontal lines are bucketed by their exact x-extent and the
literal text of their stroke-width; each bucket is cut into maximal runs of EQUALLY SPACED lines;
and a run is ONE STAFF when it holds 4 to 6 lines (4 Gregorian, 5 usual, 6 tablature) and each
line is at least 3x as long as the spacing.

    COUNTED PER PAGE AND COMPARED PER PAGE, so one borderline page cannot decide a whole row and
    a plus-one on page 3 cannot cancel a minus-one on page 9. svg_staves_diff_pages names the
    pages that differ, as "p4:16/15 p7:16/17".

    This rung exists only against the ORACLE. Mutopia published PDFs and no SVG, so on that axis
    the RASTER rung stays in force (DriftVerdict.MutopiaAgrees): the Mutopia ladder was
    calibrated with it in place, an agreeing pair there sits at block_diff 0.13-0.18 where the
    raster count is the shift-tolerant layout signal, and retiring it would have moved 28 rows
    to CLEAN on no measurement -- the 2026-08-28 re-grade tried exactly that and the number was
    put back. So: against the oracle the SVG count decides and the raster count decides
    nothing; against Mutopia the raster count decides as it always did.

    NOT COUNTED: a one-line percussion staff, which cannot be told from a stray horizontal rule
    without guessing. None occurs in this corpus, and the omission would apply to both sides.

    NOT DELIVERED: a SYSTEM count. The SVG carries no system grouping -- no id, no class, no
    wrapper element -- and neither vertical proximity nor document order recovers one. Measured on
    StraussJJ/blue_danube, one page: the gap INSIDE a two-staff system is 5.00 to 6.74 units and
    the gap BETWEEN systems 8.35 to 9.53, ranges close enough that any fixed cut is a guess, and a
    largest-gap split would invent systems on a single-staff part where every gap is a break.
    Staves per page is what this rung reports.

See OBSERVATIONS_lilyport_mutopia_2026-08-27.txt sections L1 and L4 for what forced the change,
and REPORT_lilyport_L1_svg_staves_2026-08-28.txt for the re-grade it produced.

READING THE NUMBERS. The port-vs-oracle ink number is not on the same scale as the
Mutopia one: an agreeing pair sits at 0.001-0.002 (measured), where an agreeing pair
against Mutopia sits at 0.13-0.18. The calibrated SIMILAR cut of 0.25 is therefore very
loose on this axis and is left alone rather than split in two -- summarize.py instead
prints the o_block_diff tail, so a row at, say, 0.2 is read even though the ladder still
calls it SIMILAR.

COLUMNS: oracle, oracle_seconds, oracle_errors, oracle_warnings, oracle_pages,
oracle_midi_files, then o_page_count, o_page_size, o_text, o_text_bag, o_ink,
o_block_diff, o_raster_staves, o_raster_staves_port, o_raster_staves_oracle,
svg_staves, svg_staves_port, svg_staves_oracle, svg_staves_by_page_port,
svg_staves_by_page_oracle, svg_staves_diff_pages, o_midi, o_midi_channel, o_midi_notes,
o_midi_pitches, o_midi_first_diff, then verdict, verdict_pdf, verdict_midi.

    RENAMED 2026-08-28: staves -> raster_staves, staves_port -> raster_staves_port,
    staves_ref -> raster_staves_ref, o_staves -> o_raster_staves, o_staves_oracle ->
    o_raster_staves_oracle. The svg_staves* columns are the rung the verdict is cut on; the
    raster_staves* ones are reported and decide nothing, and the names now say which is which.

    o_raster_staves_port IS NEW, and it closes a trap the old table set. The port's raster staff
    count was recorded only for the MUTOPIA comparison, over min(port, Mutopia) pages, while
    o_staves_oracle counted the ORACLE over min(port, oracle) pages -- so the two numbers a reader
    naturally paired were measured over DIFFERENT PAGE SETS. That is the whole of
    Mendelssohn_Octet_-_Viola_1's "unexplained fifteen-staff difference": 106 was ten pages
    against Mutopia and 121 was eleven pages against the oracle. On the same eleven pages the
    raster reads 122 against 121, and the SVG reads 122 against 122, page for page.

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
                                 oracle.log                 everything upstream 2.27.2 printed  (--oracle)
                                 oracle/                    its SVG pages, MIDI and <stem>.pdf  (--oracle)
                                 compare-oracle/            port-N.png / ref-N.png against it   (--oracle)
    <out>/fonts/oracle-fonts.conf            the fontconfig the oracle is run under   (--oracle)

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

  THE RASTER STAFF COUNT was the shift-tolerant layout signal and agreed with the eye on every
  pair checked (Canon 6 vs 7 systems, wtk1 24 = 24). It is summed over the compared pages; the
  `raster_staves` column says STAVES-EQUAL or STAVES-DIFFER.

  ⚠ IT DECIDES NOTHING SINCE 2026-08-28, and two measurements say why. Re-graded at 200 dpi
  instead of 100, one checked pair REVERSED SIGN (9/8 became 8/9 at o_block_diff 0.002) while
  three others kept their delta exactly. And when the Html2Pdf pin moved to 1.0.240.106 -- a
  change that altered NO page geometry, ink identical on all 227 rows -- two rows gained a
  staff on the port side alone (blue_danube and AugenhaltenM, both 8/8 -> 9/8 at unchanged
  o_block_diff) because newly-covered footer glyphs put enough ink in one raster row to cross
  the 30 % threshold. Two PORT-GAP verdicts were manufactured by four footer characters
  becoming visible. See OBSERVATIONS_lilyport_mutopia_2026-08-27.txt sections L1 and L4.

THE SVG STAFF COUNT, which replaced it on the oracle axis. Calibrated on the 1962 SVG pages the
2026-08-27 sweep left on disk -- 227 rows, both sides -- and defined in full under A NOTE ON THE
STAFF RUNG above. The two rules it turns on are nowhere near their data:

      run length (lines)         1: 4392   2: 549   3: 43   4: 64   5: 23391   6: 12
      length/spacing of a 4-6 run   <= 0.23  (stacked LEDGER LINES, which repeat at the
                                             staff-to-staff pitch across a system)
                                    >= 3.72  (a real staff; median 125)
                                    the cut at 3.0 sits in an empty gap sixteen times the noise

  The runs of two and three that a length rule alone would have accepted are all volta, ottava
  and piano-pedal rules, told apart by their own stroke-width: on DvorakA/O95/Sym9 Mvt3 page 16,
  65 lines at width 0.1219 are the thirteen staves and 6 at 0.1950 are the brackets.

  VALIDATED before it was wired in. On the 28 rows the authoritative run calls CLEAN, the port
  and the oracle agree on every page (28/28, no exceptions). On the three rows known to be raster
  artefacts -- MussorgskyM/promenade-2 (the 200 dpi sign reversal) and StraussJJ/blue_danube and
  VolkmannR/AugenhaltenM (the two the Html2Pdf bump manufactured) -- all three read 8 staves
  against 8. And on ClementiM/O36/sonatina-1, which survives as a real difference, the counter
  says 16 against 14 on page 1 and the saved PNGs show eight systems against seven.

  RE-GRADE, 2026-08-28 (~/ClaudeHome/mutopia-probe-L1-regrade-2026-08-28/, produced by --regrade
  from the authoritative run):

      verdict          before   after
      DRIFT              117      120
      PORT-GAP            72       41
      CLEAN               28       56
      INPUT-REFUSED        7        7
      PORT-AHEAD           3        3

  Of the 36 PORT-GAP rows that failed on the staff rung and nothing else, 31 were artefacts of
  the raster count and 5 are real per-page differences.

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
  That hand check became --oracle, and re-running those rows through it reproduced every one:
  Canon in D, Thaxted and lesgraces DRIFT; wtk1-prelude1 CLEAN; the Toccata PORT-GAP. The
  agreeing rows came back at o_block_diff 0.001-0.002 with TEXT-EQUAL and MIDI MATCH -- which
  is also the evidence that the mode holds everything but engraving identical, since a
  difference in the PDF route or the font resolution could not have landed there.
