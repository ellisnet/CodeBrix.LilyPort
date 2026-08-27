PROBES -- outside-staff placement and measure length (written at PARITY 9, 2026-08-15)
======================================================================================

Every file here runs UNCHANGED on BOTH engines.  Two of them print; the rest are read
straight out of the SVG, which is the honest way to ask a placement question because it
instruments nothing and so perturbs nothing.

    oracle:  ./run-oracle.sh <probe>.ly [out-dir]

             The script applies the CORPUS'S FONT PINNING, which is not optional:
             without it the oracle resolves "serif" through the host's fontconfig and
             answers Noto Serif while the port uses C059, so the two runs measure
             different typefaces and every number is nonsense (trap 8b).  PARITY 4's
             text-metric table was taken that way and had to be withdrawn.

    port:    cd ~/GitHome/CodeBrix.Samples.Gpl3/CodeBrix.LilyPort && \
             dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
                 ~/ClaudeHome/lilyport-probe-volta <out dir> --files <probe>.ly

    ⚠ Drop --no-build when the engine has just changed (trap 31), and never build while
      a sweep is running (trap 31a).

    ⚠ PROBES REPORT THROUGH ly:warning, NEVER ly:message.  The port prints nothing at all
      for ly:message -- PARITY 7 opened the Scheme-layer sink for ly:warning and
      ly:programming-error and did not cover ly:message.  A probe that reports through
      ly:message is readable on the ORACLE ONLY and cannot be diffed.  This cost PARITY 9
      two probe iterations before it was noticed.

--------------------------------------------------------------------------------------
D32 -- the outside-staff A/B set.  Each changes exactly ONE input from the one before.
--------------------------------------------------------------------------------------

probe-volta-v0-baseline.ly        the bar-line-built-in fixture, reduced to one system
probe-volta-v1-no-barnumber.ly    v0 with the bar number omitted
probe-volta-v3-no-edge-height.ly  v1 with the bracket's 2.0 edge hooks removed

Measure the top staff line and the bracket line out of the SVG and subtract.  What the
ORACLE answers, and what it decomposes the gap into:

    v0   top staff line 11.6474   bracket 6.7706   gap 4.8768
    v1   top staff line  9.9006   bracket 6.7706   gap 3.1300   bar number worth 1.7468
    v3   top staff line  9.6906   bracket 8.5606   gap 1.1300   edge hooks worth 2.0000

Note what v0 -> v1 shows and how easily it is misread: the bracket does NOT move when the
bar number goes, the STAFF does.  PARITY 8's note had suspected the bracket was being
stacked over the bar number; it is not, and one render said so.

Before the D32 fix the PORT answered gap = -1.0 in ALL THREE -- the bracket 1.0 BELOW the
top staff line, unmoved by either input.  A grob that ignores every input is not
mispositioned, it is ABSENT from the computation, and that reading is what pointed at
`SkylinesFromElementStencils' answering an empty skyline rather than at any volta code.
After the fix all three agree with the oracle exactly.

--------------------------------------------------------------------------------------
D6 -- the measure-length matrix
--------------------------------------------------------------------------------------

probe-measure-length-matrix.ly    four scores in one file, reporting through
                                  \contextPropertyCheck (a WARNING, so both engines print
                                  it), each asserting that measureLength is 1/2 inside a
                                  \measureRemainder:

    A  Timing per staff, wrapped music changes context   (the failing fixture's shape)
    B  Timing per staff, no context change
    C  Timing at Score,  wrapped music changes context
    D  Timing at Score,  no context change

The oracle is SILENT in all four.  Before the fix the port warned in all four, which is
what killed the reading the FIXTURES THEMSELVES suggest -- irregular-measure-initial-
context says in its own comments that it exists to expose an engine issuing the timing
event in Score before descending into \context Staff.  It was neither per-staff timing
nor context descent: \measureRemainder never set measureLength at all.

The pattern generalises.  When a fixture's comment tells you what it is designed to
expose, that is a hypothesis worth CROSSING against the other candidate, not one worth
believing.

probe-measure-length.ly and probe-measure-length-check.ly are the earlier single-case
forms, kept because probe-measure-length.ly is the one that revealed the ly:message gap.
probe-message-check.ly is the control that pinned it: a top-level ly:message and an
\applyContext one, both printed by the oracle and neither by the port.
