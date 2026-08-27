PARITY 17 PROBES
================================================================================

hb_advance.py -- D44, and the reason D44 did not land again
--------------------------------------------------------------------------------

WHAT IT IS.  A ctypes driver over the SYSTEM libharfbuzz, in the same shape as
PARITY 14's lilyport-probe-glyph-skyline/ft_decompose.py: it creates an
hb_face_t/hb_font_t over one of the pinned oracle's own .otf files, sets a scale,
and asks hb_font_get_glyph_h_advance for a glyph.  It exists because D44's rule is
HarfBuzz's integer arithmetic and plan rule 35a says a library rule is an ORACLE
and has to be READ, not recalled.  Two rules were REASONED at PARITY 16 and both
were refuted by full sweep; this is the instrument that stops a third guess.

RUN selfcheck FIRST (trap 32a).  At scale == upem, hb must reproduce the font's
own hmtx advance exactly.  Measured 2026-08-16: 65 glyphs, 0 mismatches, upem
1000, libharfbuzz 10.2.0.  Nothing else the probe says is believable until that
passes.

  python3 hb_advance.py selfcheck [font.otf]
  python3 hb_advance.py formula   [font.otf]
  python3 hb_advance.py cell      [font.otf]

WHAT IT MEASURED, AND WHAT IT REFUTES.  Over 285,940 (glyph, scale) samples on
five pinned faces:

    em_mult, truncated multiplier, +0x8000     414 misses  (0.145%)
    exact rational, round half away from zero 5883 misses
    float32 multiplier + roundf                984 misses / 56,347

so the answer is NEAR the classic em_mult form and is not any of the three.  A
THIRD candidate rule is therefore refuted, and it is refuted by measurement
rather than by a sweep, which is much cheaper than the two before it.

⚠ THE SHARP RESULT, and the one a later session should start from: hb's internal
multiplier fits NO simple closed form over the exact value.  Solving for it by
interval intersection (advance == floor((v*mult + 32768)/65536) constrains mult
from many glyphs at one scale) gives, for C059 at upem 1000:

    scale   mult must be in      exact (scale<<16)/upem   truncate  round
     4134   [270920, 270925]     270925.824               IN        OUT
     4347   [284885, 284886]     284884.992               OUT       IN

-- one case needs a multiplier BELOW the exact value and the other ABOVE it.
That is the same shape PARITY 16 reported from the sweep ("the two sets sit in
the same window and need OPPOSITE answers") reproduced in a single run against
the library itself, which is what makes it a property of hb rather than of the
corpus.  Note the interval solver ASSUMES the (v*mult + 32768) >> 16 form; where
its answer is far from the exact value (scale 27763 solved ~196 away) the form is
what is wrong, not hb, so do not read those rows as measurements of a multiplier.

WHAT IS STILL OPEN.  Whether hb 10.2.0 is the version the PINNED ORACLE links.
The oracle statically links harfbuzz, pango and freetype -- `ldd` on it lists
only libc, libm, libpthread, libdl and libresolv -- so the version could not be
read off the binary.  Trap 8b's lesson applies with the sign reversed: this probe
is honest about the SYSTEM library, and a claim about the ORACLE needs the
oracle's own build.  PARITY 14 hit the same limit with FreeType (system 2.13.3
against the oracle's 2.14.1) and its rule was: rest STRUCTURAL claims on the
system library, never sub-unit numeric ones.  The same rule applies here, and it
is why the session did not land a fix off these numbers.


drift.py, placements.py -- still PARITY 16's, still the first thing to reach for
--------------------------------------------------------------------------------

They live in ~/ClaudeHome/lilyport-probe-parity16 and PARITY 17 used drift.py on
all 53 PLACEMENT-DIFFERS rows at once, which took one run and re-ranked the whole
residue.  Trap 7a earned its place a fourth time:

    markup-font-select        worst 146.8012  ->  a uniform 0.05 y shift
    metronome-mark-formatter  worst  70.1160  ->  a uniform 0.04 y shift
    dead-notes                worst  16.9209  ->  dx <= 0.035 (one device dot)
                                                  plus a 0.07 y band

Read the honest map in the PARITY 17 STATUS file before believing any worst-delta
in the survivors list.
