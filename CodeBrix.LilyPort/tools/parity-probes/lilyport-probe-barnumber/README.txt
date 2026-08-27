PROBES -- side-position placement and text metrics (written at PARITY 4, 2026-08-14)
====================================================================================

Every file here runs UNCHANGED on BOTH engines, which is the whole point: each one
prints its measurements to stdout, so the two runs can be diffed line for line.

    oracle:  ⚠ UNDER THE CORPUS'S FONT PINNING, ALWAYS.  Without it the oracle
             resolves "serif" through the HOST's fontconfig and answers Noto
             Serif, while the port uses C059 -- so the two runs are measuring
             DIFFERENT TYPEFACES and every number is nonsense.  PARITY 4's
             text-metric table was taken that way and had to be withdrawn.

             H=~/GitHome/CodeBrix.Samples.Gpl3/CodeBrix.LilyPort/tools/regression-harness
             D=~/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/2.27.2/fonts/otf
             sed "s|@FONTDIR@|$D|g" $H/reference-fonts.conf.in > /tmp/rf.conf
             cd <some scratch dir> && \
             FONTCONFIG_FILE=/tmp/rf.conf FONTCONFIG_PATH=/tmp \
             ~/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond -dbackend=svg \
                 -dpoint-and-click=#f <this dir>/<probe>.ly

             (generate-reference.sh does exactly this; a probe that skips it is
             not running the same experiment as the corpus.)

    port:    cd ~/GitHome/CodeBrix.Samples.Gpl3/CodeBrix.LilyPort && \
             dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
                 ~/ClaudeHome/lilyport-probe-barnumber <some out dir> \
                 --files <probe>.ly --keep-existing

    ⚠ Drop --no-build when the engine has just changed (trap 31), and never rebuild
      while a sweep is running -- the minute-stamped version rewrites the Engine DLL
      out from under it.

probe-two-systems.ly
    Bare two-system score. Nothing is instrumented; it exists so bar-number placement
    can be read straight out of the SVG.

probe-side-position-overlap.ly
    Prints the BarNumber's X extent and its StaffSymbol support's, in their common
    refpoint, at horizon-padding 0.05 and again at 5.0. This is what showed that
    aligned_side's whole support term depends on a 0.05-wide horizontal overlap that
    horizon-padding creates on purpose -- lose the overlap and Skyline::distance
    answers -infinity, which aligned_side silently turns into zero.

probe-skyline-vs-extent.ly
    Prints the BarNumber's own X extent, its stencil's, and the span of its stored
    vertical-skylines. On a faithful port all three agree, because
    simple-vertical-skylines-from-extents builds the skyline out of the extent. They
    did not agree before PARITY 4: the stored skyline had been translated by the
    grob's own X coordinate by an earlier reader that was handed an ALIAS of it.

probe-mark-extent.ly
    The first system of staff-ledger-positions, with the RehearsalMark's Y-offset
    wrapped: prints the mark's own Y-extent, its support's, and the offset
    side-position gives it. Shows the placement ARITHMETIC agreeing exactly between
    the engines while the markup's own extent does not.

probe-text-metrics.ly
    Four TextScript stencils -- a one-line markup, the same at \small, a two-digit
    number, and the two-line column staff-ledger-positions uses.

    ⚠ ITS MEASURED TABLE (2026-08-14, PARITY 4) IS WITHDRAWN.  It was taken with the
    oracle UNPINNED, so it compares Noto Serif with C059.  Everything it showed --
    an error on both axes, growing with the string, the port's "17" hanging below
    the baseline -- is an artifact of that.  Re-measured under the pinning, the Y
    extents agree to 2.4e-05 and only X was ever wrong.  See PARITY 5.

probe-text-perglyph.ly
    WRITTEN AT PARITY 5, and the one to start from for anything about text width.
    A single-character markup's X extent is (0 . advance) on BOTH engines, so a
    one-char line measures ONE glyph with nothing folded in; the repeated-character
    lines show whether an error grows per GLYPH or per RUN; the A/V/T lines isolate
    kerning. Under the pinning, in C059-Roman, the oracle's advances are EXACT
    INTEGER numbers of 1200-dpi device dots (one dot = 0.0341433 staff spaces at
    the default output-scale):

      H 54    x 35    g 35    1 36    7 36    o 32    . 18    i 20
      \tiny H 43   \small H 48   \large H 60   \huge H 68
      HH 108 (= 2*54, NOT round(2*53.676)=107)
      AV 87  (kern INSIDE the rounding; outside gives 88)
      AVAVAVAV 327 (= 7*40 + 47)

    Since PARITY 5 the port reproduces all 28 X extents BYTE-IDENTICALLY.

probe-origin-type.ly
    WRITTEN AT PARITY 5.  Asks ly:input-location? about a sequential music, the
    note inside it, an event chord and the chord's own notes, with a bare
    ly:make-music as the CONTROL that must answer N. Both engines now print
    Y Y Y Y / N.

probe-brace-glyph.ly
    WRITTEN AT PARITY 6, and the one to start from for anything about the brace font
    or, more generally, about a FONT'S SCALE.  It prints the whole input to
    \left-brace's glyph choice: the fetaBraces font's glyph COUNT, the layout's
    output-scale, ly:pt, the scaled-size handed to the search, the Y extent of a
    spread of brace glyphs, and the index binary-search actually returns.

    It found TWO defects at once, and neither was visible from the sweep, which only
    said "missing brace177, extra brace49" on 136 pages:

      (1) THE SCALE.  font-select.cc:164 runs fetaMusic, fetaBraces and fetaText
          through ONE call to best_rounded_design_size and then scales fetaMusic and
          fetaBraces alike by requested_size / actual_size.  The port's brace branch
          divided by the brace OTF's own design_size instead -- which that file
          records in MILLIMETRES -- so every brace glyph measured 2.84528x too tall.
          That number is exactly 1/ly:pt(1), which is what named the cause: the
          probe's ratio matched the pt->mm constant to six significant figures.

      (2) THE COUNT.  Open_type_font::count answers index_to_charcode_map_.size (),
          counting only glyphs a charcode reaches.  The port counted the CFF charset,
          which also holds .notdef -- one too many.  The tell is in the probe's own
          output: brace576 printed Yextent=(+inf.0 . -inf.0), an EMPTY extent, i.e. a
          glyph that does not exist, sitting at the top of a binary search.

    Oracle baseline, under the pinning (35 pt / 45 pt):
        glyph-count 575, output-scale 1.7572990175729903, ly:pt(35)=12.301093123010931
        brace0 2.0999999999999996   brace49 4.562399999999999
        brace177 13.0924            brace575 76.97479999999999
        binary-search 91 (35 pt), 121 (45 pt)
    Since PARITY 6 the port prints these BYTE-IDENTICALLY, every line.

    The general lesson, which is why this probe is worth keeping: a font whose SCALE
    is wrong by a constant does not look wrong on the page, because the glyph search
    compensates by picking a different glyph and the drawn size comes out about
    right.  It shows up only as a glyph-NAME difference.  Anything that selects a
    glyph by measuring it deserves this probe before it deserves a theory.
