PARITY 10 probe set (2026-08-15) — cross-staff spacing (D35), the brace search,
and music-identifier origins.

⚠ RUN EVERY ORACLE PROBE THROUGH ./run-oracle.sh, never the bare binary: it
applies the corpus's own font pinning out of reference-fonts.conf.in.  A probe
run without it resolves `serif' through the HOST's fontconfig and measures a
different typeface (trap 8b, which cost PARITY 4 a whole ruling).

The port side:
    cd ~/GitHome/CodeBrix.Samples.Gpl3/CodeBrix.LilyPort
    dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
        ~/ClaudeHome/lilyport-probe-crossstaff OUTDIR --files <probe>.ly

--------------------------------------------------------------------------------
D35 — \change Staff costs 10 staff-spaces of staff separation
--------------------------------------------------------------------------------

An A/B ON INPUT (trap 30b), read straight out of the SVG with no instrumentation:
each file changes ONE thing, and the staff-line Y positions are the measurement.

    probe-cross-v0-plain.ly          CONTROL: a PianoStaff with no \change Staff
    probe-cross-v1-changestaff.ly    the same, plus \change Staff inside a beam
    probe-cross-v2-switch.ly         v1 plus \showStaffSwitch
    probe-cross-v3-beamcross.ly      v1 with the beam removed
    probe-cross-v4-onenote.ly        ONE note moved, no beam, no chord
    probe-cross-v5-control.ly        CONTROL for v4

Measured 2026-08-15 (gap between the two staves' nearest staff lines):

    v0   oracle 5.0    port  5.0     <- agree
    v1   oracle 5.0    port 15.0
    v2   oracle 5.0*   port 15.0*
    v3   oracle 5.0    port 15.0

So the beam is not the axis and \showStaffSwitch is not the axis; \change Staff
is.  The brace-index residue M3 named is the SYMPTOM: the system-start delimiter
sizes itself from the staves it spans.

To read the staff-line positions the way the measurement above was made, use the
comparator's own parser rather than the raw SVG (it resolves the transforms):

    cd ~/GitHome/CodeBrix.Samples.Gpl3/CodeBrix.LilyPort/tools/regression-harness
    python3 -c "
    import importlib.util
    s=importlib.util.spec_from_file_location('co','compare-output.py')
    co=importlib.util.module_from_spec(s); s.loader.exec_module(co)
    d=co.parse_svg('PATH.svg')
    print(sorted({round(p[2],3) for p in d['placements'] if str(p[0]).startswith('line')})[:12])"

⚠ TWO PROBE APPROACHES THAT DO NOT WORK HERE, both learned the hard way:

  * Overriding Beam.after-line-breaking / Stem.after-line-breaking to report
    'cross-staff DISPLACES THE SPACING ON BOTH ENGINES — those properties have
    real defaults, and the override replaces them.  The numbers such a probe
    produces describe the probe.
  * \applyOutput walking a VerticalAxisGroup's `elements' and taking extents
    SEGFAULTS the pinned oracle at that point in the run.

What has been checked and is NOT the cause: the cross-staff guards in
AxisGroupInterface.RelativeGroupExtentOf, AxisGroupInterfaceVertical's skyline
path and its pure variants are all present and faithful, and every grob's
'cross-staff answer agrees with the oracle grob for grob.

--------------------------------------------------------------------------------
The brace search — why M3 is NOT a brace defect
--------------------------------------------------------------------------------

    probe-brace.ly       every INPUT to \left-brace's binary search: the glyph
                         count it is bounded by, output-scale, ly:pt, and eleven
                         brace glyph heights.  Runs as a MARKUP COMMAND so that
                         `layout' and `props' are the real ones -- a top-level
                         #(...) block cannot see output-scale and dies.
    probe-brace-size.ly  the SIZE in points that staff_brace hands to
                         \left-brace, by shadowing make-left-brace-markup.
    probe-brace-len.ly   the extent System_start_delimiter::print unites.
                         ⚠ Its after-line-breaking override reads the spacing
                         too early; prefer probe-brace-size.ly.

Measured 2026-08-15: every input agrees between the engines to four decimals
(glyph-count 575, output-scale 1.7572990175729903, brace177 13.0924, ...).

--------------------------------------------------------------------------------
Music-identifier origins (M7, closed)
--------------------------------------------------------------------------------

    probe-glide-origins.ly   prints the 'origin of every articulation on a note
                             carrying two \glide post-events.

Kept because it is the shape of a whole class: a music identifier's per-use
origin comes from the LEXER (Lily_lexer::scan_escaped_word), not the grammar,
and code that tells two post-events apart by origin depends on it.

--------------------------------------------------------------------------------
transitions.py — read the exposed rows BY NAME before advancing a floor
--------------------------------------------------------------------------------

    python3 transitions.py pass-manifest.tsv /tmp/verdicts.tsv

Groups every row whose verdict changed by transition.  `ratchet.py check' says
how many regressed; this says WHICH, and which way, which is what a change with
a wide blast radius needs before its floor is advanced.
