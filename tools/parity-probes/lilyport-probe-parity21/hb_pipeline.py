#!/usr/bin/env python3
"""
D44 / R13 -- model the WHOLE upstream text-advance pipeline in integers and check
each stage against the authority that owns it.

PARITY 16 tried "Pango's two-stage quantization" and it fixed eight rows and
regressed eight.  This probe exists because the pipeline has FOUR integer stages,
not two, and the two that were missing are both upstream of the ones that were
tried:

  1. pango_size = lround(requested_size * PANGO_SCALE)          lily/font-select.cc:215
  2. pixel_size = pango_size / PANGO_SCALE * dpi / 72.0         pangofc-fontmap.c
     x_scale    = pango_units_from_double(pixel_size)           = floor(px*1024 + 0.5)
                = floor(pango_size * 1200/72 + 0.5)             pangofc-font.c
  3. adv_pu     = hb_font_get_glyph_h_advance(...)              harfbuzz em_mult
                = (units * ((x_scale<<16)//upem) + 32768) >> 16
     kerning adds a SEPARATELY scaled term (GPOS apply_value calls em_scale_x on
     the pair value), so it is em_mult(units) + em_mult(kern), NOT em_mult(sum).
  4. width_pu   = PANGO_UNITS_ROUND(adv_pu) = (adv_pu + 512) & ~1023   pango-shape.c

Stage 3 is the one that cannot be recalled, so it is MEASURED here against the
real libharfbuzz (HB_LIBRARY selects which one).  Stage 2's rounding of x_scale to
a whole Pango unit is the stage PARITY 16 did not have, and it is worth 0.5 Pango
units of scale error -- which is the size of the effect D44 reports.

Modules:
  emcheck [font.otf]   -- does em_mult reproduce hb over the scales the corpus
                          actually uses?  Prints every miss.
  scales               -- the x_scale values the corpus's own font sizes produce.
  advance FONT SIZE TEXT
                       -- run the whole pipeline for one string and print each
                          stage, so a port number can be diffed against it.
"""

import ctypes
import os
import sys

sys.path.insert(0, os.path.expanduser(
    "~/GitHome/CodeBrix.LilyPort/tools/parity-probes/lilyport-probe-parity17"))

PANGO_SCALE = 1024
PANGO_RESOLUTION = 1200
INCH_TO_BP = 72.0


# ---- the integer pipeline ---------------------------------------------------
def pango_size_of(requested_size):
    """lily/font-select.cc:215 -- lround, which is half AWAY FROM ZERO."""
    import math
    return int(math.floor(requested_size * PANGO_SCALE + 0.5)) if requested_size >= 0 \
        else -int(math.floor(-requested_size * PANGO_SCALE + 0.5))


def x_scale_of(pango_size):
    """pangofc-font.c -- pango_units_from_double(pixel_size), i.e. floor(x+0.5)."""
    import math
    pixel_size = (pango_size / float(PANGO_SCALE)) * PANGO_RESOLUTION / INCH_TO_BP
    return int(math.floor(pixel_size * PANGO_SCALE + 0.5))


def em_mult(v, x_scale, upem):
    """harfbuzz hb_font_t::em_mult over the truncated 16.16 multiplier."""
    mult = (x_scale << 16) // upem
    return (v * mult + 32768) >> 16


def pango_units_round(pu):
    """pango-shape.c -- PANGO_UNITS_ROUND, applied per glyph."""
    return (pu + (PANGO_SCALE >> 1)) & ~(PANGO_SCALE - 1)


# ---- measurement ------------------------------------------------------------
def corpus_scales():
    """The x_scale values LilyPond's own text sizes reach.

    Text sizes come from font-size steps on a base, so rather than guess the set,
    sweep every pango_size a plausible size band produces and de-duplicate.
    """
    out = []
    seen = set()
    # requested_size is LilyPond's internal length unit; the corpus's text runs sit
    # between roughly 1 and 40 of them.  Step by 1/1024 -- the lattice stage 1 puts
    # them on -- so every reachable pango_size is covered.
    for ps in range(1024, 40 * 1024):
        xs = x_scale_of(ps)
        if xs not in seen:
            seen.add(xs)
            out.append((ps, xs))
    return out


def emcheck(path):
    from hb_advance import Font
    from fontTools.ttLib import TTFont

    f = Font(path)
    tt = TTFont(path)
    hmtx = tt["hmtx"]
    order = tt.getGlyphOrder()
    cmap = tt.getBestCmap()

    chars = [c for c in
             "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,-()"
             if ord(c) in cmap]
    units = []
    for ch in chars:
        gid = f.glyph(ch)
        units.append((ch, gid, hmtx[order[gid]][0]))

    scales = corpus_scales()
    print("upem=%d  %d chars  %d distinct x_scale values" % (f.upem, len(units), len(scales)))

    misses = 0
    total = 0
    shown = 0
    for ps, xs in scales:
        for ch, gid, v in units:
            got = f.advance(gid, xs)
            mine = em_mult(v, xs, f.upem)
            total += 1
            if got != mine:
                misses += 1
                if shown < 20:
                    shown += 1
                    print("  MISS ch=%r units=%d pango_size=%d x_scale=%d hb=%d em_mult=%d"
                          % (ch, v, ps, xs, got, mine))
    print("em_mult vs hb: %d misses of %d samples" % (misses, total))
    return misses == 0


def advance(path, size, text):
    """The whole pipeline for one string, stage by stage."""
    from hb_advance import Font
    from fontTools.ttLib import TTFont

    f = Font(path)
    tt = TTFont(path)
    hmtx = tt["hmtx"]
    order = tt.getGlyphOrder()

    ps = pango_size_of(size)
    xs = x_scale_of(ps)
    print("requested_size=%.6f -> pango_size=%d -> x_scale=%d   (upem=%d)"
          % (size, ps, xs, f.upem))
    print("%-4s %6s %10s %10s %12s" % ("ch", "units", "hb_pu", "rounded_pu", "dots"))

    total_pu = 0
    for ch in text:
        gid = f.glyph(ch)
        v = hmtx[order[gid]][0]
        pu = f.advance(gid, xs)
        rounded = pango_units_round(pu)
        total_pu += rounded
        print("%-4s %6d %10d %10d %12.4f" % (ch, v, pu, rounded, rounded / 1024.0))

    print("TOTAL %d pango units = %.4f device dots" % (total_pu, total_pu / 1024.0))

    # what the port computes today: exact real dots per glyph, rounded half-up
    import math
    port = 0.0
    for ch in text:
        gid = f.glyph(ch)
        v = hmtx[order[gid]][0]
        exact_dots = v * ps / float(PANGO_SCALE * f.upem) * PANGO_RESOLUTION / INCH_TO_BP
        port += math.floor(exact_dots + 0.5)
    print("PORT (exact-real per glyph, no kerning) = %.4f device dots" % port)


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "emcheck"
    ORACLE_OTF = os.path.expanduser(
        "~/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/2.27.2/fonts/otf")
    if mode == "emcheck":
        target = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ORACLE_OTF, "C059-Roman.otf")
        sys.exit(0 if emcheck(target) else 1)
    elif mode == "scales":
        for ps, xs in corpus_scales()[:40]:
            print("pango_size=%6d  x_scale=%8d  exact=%.4f" % (ps, xs, ps * 1200.0 / 72.0))
    elif mode == "advance":
        advance(sys.argv[2], float(sys.argv[3]), sys.argv[4])
    else:
        print(__doc__)
        sys.exit(2)
