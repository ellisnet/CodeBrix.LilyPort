lilyport-probe-parity26 -- the two remaining G1 rows, taken apart
================================================================
Created 2026-08-17 (PARITY 26).

WHAT THESE ANSWER.  `skyline-boxes-ellipses' and `skyline-grob-rotation' were the
last two non-matching rows against the GATE.  PARITY 25 left them described as
"no geometry, four hypotheses refuted".  These tools name the mechanism instead.

THE TOOLS
---------
skypoints.py FILE.svg [--all]
    Reconstructs a -ddebug-skylines drawing's BUILDING list out of an SVG.
    Grob::print draws a skyline with Lookup::points_to_line_stencil, which walks
    Skyline::to_points's output and connects EVERY consecutive pair, so building
    k occupies points[2k],[2k+1] and is drawn by the line at odd index 2k+1.  A
    ZERO-WIDTH building therefore draws as a fully degenerate line -- x1 == x2 AND
    y1 == y2 -- and nothing else in a LilyPond SVG draws a zero-length line.
    /!\ THE FILE ORDER IS THE REVERSE OF THE BUILDING ORDER.  Stencil::add_stencil
    PREPENDS to the combine-stencil list, so the last line drawn appears first.

skydiff.py REFERENCE.svg CANDIDATE.svg [--context N]
    Sequence-diffs two renderings' skyline drawings.  The comparator says WHICH
    marks differ; this says WHERE IN THE SEQUENCE, which is what names the
    building.  /!\ difflib mis-aligns long similar runs -- read the FIRST hunk and
    the DEGENERATE COUNTS in the header, not the hunk sizes.

replay.py DUMP.txt
    A LITERAL transcription of lily/skyline.cc's whole build path in Python
    (Building/precompute/intersection_x/above, non_overlapping_skyline,
    internal_merge_skyline, internal_build_skyline), plus BOTH candidate sort
    orders: 'stable' (what the port does) and 'libstdcxx' (std::sort's introsort,
    reproduced -- median-of-3 partition, heapsort at depth, final insertion sort).
    Feed it the port's OWN dumped inputs and compare with the port's OWN dumped
    outputs.  Python and C# both use IEEE doubles, so a difference is an
    IMPLEMENTATION difference and not an arithmetic one.
    /!\ THE DUMP MUST CARRY RAW (x1,y1,y2,x2), NOT Height(left)/Height(right).
    A building reconstructed from its own recomputed heights is not the same
    building: y_intercept = y1 - slope*left, so Height(left) is y1 only up to
    rounding.  The first version of this measurement was taken that way and
    reported six false differences.

adv/  -- two probes that run UNCHANGED on both engines and print numbers:
    adv-bb.ly   dumps the glyph-string expression's per-glyph (w h xo yo idx).
                /!\ ORACLE ONLY IN PRACTICE: the port's text stencil carries
                `glyph-outline' expressions, not `glyph-string', which is itself
                a recorded divergence.
    sky-num.ly  prints a TextScript's own vertical skyline NUMERICALLY through
                ly:skyline->points / ly:skyline-max-height.  This is the one that
                settles the question, because the -ddebug-skylines DRAWING rounds
                to four decimals and four decimals is exactly the precision the
                question is about.
    Both use `ly:message' rather than `display', because BatchDriver routes the
    Scheme output port elsewhere and `display' vanishes.

WHAT THEY ESTABLISHED
---------------------
1. THE PORT'S SKYLINE BUILD IS BIT-EXACT WITH UPSTREAM'S ALGORITHM.  replay.py
   agrees with the port's C# on all six skyline builds of a two-glyph markup, to
   the last bit of every left, right, slope and y-intercept, under BOTH sort
   orders.  That closes the SORT hypothesis properly (PARITY 25 had only shown
   that reversing tie order moves nothing, which is a weaker statement) and the
   MERGE hypothesis outright.

2. A SINGLE GLYPH IS EXACT.  Ten one-character markups (b o l n a e c i m x) all
   render skyline drawings BIT-IDENTICAL to the oracle's, line for line.

3. THE DIFFERENCE NEEDS TWO GLYPHS, and it is not the advance.  The port's
   second-glyph segments are the first glyph's plus 1.2291590551181102 exactly,
   and that is the oracle's own advance, read off adv-bb.ly.

4. THE MECHANISM IS THE TEXT SKYLINE'S METRIC SOURCE.  sky-num.ly, same file,
   both engines:

        quantity                 oracle                 port              delta
        max height (up)   -0.03300964259350392  -0.033001306825172244   8.3e-06
        max height (down)  1.6214403128075787    1.6214642086767959    -2.4e-05
        leftmost x         0.017605142716535432  0.017600696973425194   4.4e-06

   The oracle's numbers are Pango's: -0.03300964259350393 is exactly the `h'
   pair adv-bb.ly reads out of the glyph-string expression, i.e. Pango's INK
   RECTANGLE, quantized to whole Pango units and then scaled.  The port's come
   from the real CFF outline.  add_glyph_string_segments composes its per-glyph
   transform out of THREE differently-rounded boxes -- Pango's scaled ink rect,
   FreeType's unscaled metric box, and their length ratio as `scale_factor' --
   and upstream's own comment on that chain reads "FIXME: this looks extremely
   fishy."  The port has ONE source for both and its chain cancels EXACTLY
   (verified: (kx + s*bx) - s*bx == kx for the measured numbers).

   So the residue is ~1e-5 staff spaces = ~4.2e-05 mm, which is 1,200 times
   below the font ledger's own accepted ceiling of 0.05 mm -- far below anything
   the SVG's four decimals can print, EXCEPT where a coordinate lands on a
   rounding boundary (12.3500 vs 12.3501, 14.4000 vs 14.4001) and except where a
   skyline merge tie-break lands on exact equality and keeps or drops a
   degenerate zero-width building.  Only a -ddebug-skylines page can see either,
   because only such a page draws the partition itself.

WHAT WAS REFUTED HERE (do not re-run)
-------------------------------------
  * the sort            replay.py: stable and libstdc++ introsort give the SAME
                        answer on this input, and both equal the port's.
  * the merge           bit-exact against a literal transcription, six builds.
  * the transform chain upstream's four-step translate/translate/scale/translate
                        cancels EXACTLY in doubles for the measured numbers, so
                        the extra steps are not where the difference enters.
  * the advance         identical to the last bit.
  * the glyph outline   ten single-glyph markups are bit-identical.
Together with PARITY 25's four (contour closing, the CFF walk, the font assets,
stable-vs-std::sort as it was then tested) that is nine refuted hypotheses.
