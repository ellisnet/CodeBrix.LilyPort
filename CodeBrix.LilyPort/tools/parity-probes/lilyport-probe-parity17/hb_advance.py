#!/usr/bin/env python3
"""
D44 -- read HarfBuzz's INTEGER advance arithmetic off the library itself.

Two rules were implemented and refuted at PARITY 16 (a two-stage Pango
quantization, and an epsilon at the tie).  Both were REASONED.  Plan rule 35a
says a library rule is an oracle and must be READ, so this probe drives the real
libharfbuzz through ctypes and asks it for advances directly, then checks the
candidate closed forms against what it answered.

  selfcheck   -- hb's unscaled advance must equal fontTools' hmtx advance.
                 Nothing below is believable until this passes (trap 32a).
  formula     -- fit hb's answers over a scale/glyph sweep against candidates.
  cell        -- the D44 cell itself: C059 'm' at the size font-name/header rows use.

Usage:  python3 hb_advance.py selfcheck|formula|cell [font.otf]
"""

import ctypes
import sys
import os

# R13 (PARITY 21) owed the probe this change: the library is a PARAMETER, so a
# version hunt can drive an UNPACKED libharfbuzz (dpkg-deb -x into a scratch dir)
# without installing anything.  HB_LIBRARY names the .so; absent, the system one
# answers, which is what every PARITY 17 measurement was taken against.
HB_LIBRARY = os.environ.get("HB_LIBRARY", "libharfbuzz.so.0")
HB = ctypes.CDLL(HB_LIBRARY)

ORACLE_OTF = os.path.expanduser(
    "~/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/2.27.2/fonts/otf")
C059 = os.path.join(ORACLE_OTF, "C059-Roman.otf")

# ---- ctypes prototypes ------------------------------------------------------
HB.hb_blob_create_from_file.restype = ctypes.c_void_p
HB.hb_blob_create_from_file.argtypes = [ctypes.c_char_p]
HB.hb_face_create.restype = ctypes.c_void_p
HB.hb_face_create.argtypes = [ctypes.c_void_p, ctypes.c_uint]
HB.hb_face_get_upem.restype = ctypes.c_uint
HB.hb_face_get_upem.argtypes = [ctypes.c_void_p]
HB.hb_font_create.restype = ctypes.c_void_p
HB.hb_font_create.argtypes = [ctypes.c_void_p]
HB.hb_font_set_scale.restype = None
HB.hb_font_set_scale.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
HB.hb_font_get_nominal_glyph.restype = ctypes.c_int
HB.hb_font_get_nominal_glyph.argtypes = [
    ctypes.c_void_p, ctypes.c_uint, ctypes.POINTER(ctypes.c_uint)]
HB.hb_font_get_glyph_h_advance.restype = ctypes.c_int
HB.hb_font_get_glyph_h_advance.argtypes = [ctypes.c_void_p, ctypes.c_uint]
HB.hb_version_string.restype = ctypes.c_char_p
HB.hb_version_string.argtypes = []


class Font(object):
    """A HarfBuzz face/font pair over one OTF file."""

    def __init__(self, path):
        blob = HB.hb_blob_create_from_file(path.encode("utf-8"))
        self.face = HB.hb_face_create(blob, 0)
        self.upem = HB.hb_face_get_upem(self.face)
        self.font = HB.hb_font_create(self.face)
        self.path = path

    def glyph(self, char):
        out = ctypes.c_uint(0)
        ok = HB.hb_font_get_nominal_glyph(
            self.font, ord(char), ctypes.byref(out))
        if not ok:
            raise KeyError("no glyph for %r in %s" % (char, self.path))
        return out.value

    def advance(self, glyph, scale):
        HB.hb_font_set_scale(self.font, scale, scale)
        return HB.hb_font_get_glyph_h_advance(self.font, glyph)


# ---- candidate closed forms -------------------------------------------------
def cand_truncate(v, scale, upem):
    """(v * ((scale<<16)/upem)) >> 16  -- em_mult with no rounding term."""
    mult = (scale << 16) // upem
    return (v * mult) >> 16


def cand_round(v, scale, upem):
    """(v * ((scale<<16)/upem) + 0x8000) >> 16  -- em_mult with the round term."""
    mult = (scale << 16) // upem
    return (v * mult + 0x8000) >> 16


def cand_real_round(v, scale, upem):
    """round(v * scale / upem) -- the exact-real rule the port uses today."""
    x = v * scale / float(upem)
    import math
    return int(math.floor(x + 0.5))


CANDIDATES = [
    ("em_mult truncate", cand_truncate),
    ("em_mult +0x8000 ", cand_round),
    ("exact-real round", cand_real_round),
]


def selfcheck(path):
    """hb at scale == upem must reproduce the hmtx advance exactly."""
    from fontTools.ttLib import TTFont

    f = Font(path)
    tt = TTFont(path)
    hmtx = tt["hmtx"]
    glyph_order = tt.getGlyphOrder()
    cmap = tt.getBestCmap()

    bad = 0
    checked = 0
    for ch in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,":
        cp = ord(ch)
        if cp not in cmap:
            continue
        gid = f.glyph(ch)
        name = glyph_order[gid]
        want = hmtx[name][0]
        got = f.advance(gid, f.upem)
        checked += 1
        if got != want:
            bad += 1
            print("  MISMATCH %r gid=%d hmtx=%d hb=%d" % (ch, gid, want, got))
    print("upem=%d  hb=%s" % (f.upem, HB.hb_version_string().decode()))
    print("selfcheck: %d glyphs, %d mismatches -- %s"
          % (checked, bad, "OK" if bad == 0 else "FAILED"))
    return bad == 0


def formula(path):
    """Fit hb's answers over many scales and glyphs against each candidate."""
    from fontTools.ttLib import TTFont

    f = Font(path)
    tt = TTFont(path)
    hmtx = tt["hmtx"]
    glyph_order = tt.getGlyphOrder()
    cmap = tt.getBestCmap()

    chars = [c for c in
             "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,-"
             if ord(c) in cmap]

    # Pango sets the hb scale from the font size in Pango units.  Sweep a wide
    # band of plausible scales rather than assuming one.
    scales = list(range(500, 40000, 137)) + [1024 * n for n in range(1, 60)]

    misses = dict((name, 0) for name, _ in CANDIDATES)
    total = 0
    first_bad = dict((name, None) for name, _ in CANDIDATES)

    for ch in chars:
        gid = f.glyph(ch)
        v = hmtx[glyph_order[gid]][0]
        for scale in scales:
            got = f.advance(gid, scale)
            total += 1
            for name, fn in CANDIDATES:
                if fn(v, scale, f.upem) != got:
                    misses[name] += 1
                    if first_bad[name] is None:
                        first_bad[name] = (ch, v, scale, got, fn(v, scale, f.upem))

    print("upem=%d  hb=%s" % (f.upem, HB.hb_version_string().decode()))
    print("%d (glyph, scale) samples\n" % total)
    for name, _ in CANDIDATES:
        note = ""
        if first_bad[name]:
            ch, v, scale, got, mine = first_bad[name]
            note = "   first miss: %r units=%d scale=%d hb=%d rule=%d" % (
                ch, v, scale, got, mine)
        print("  %-18s %6d misses%s" % (name, misses[name], note))


def cell(path):
    """The D44 cell: C059 'm', and the neighbourhood of sizes around it."""
    from fontTools.ttLib import TTFont

    f = Font(path)
    tt = TTFont(path)
    hmtx = tt["hmtx"]
    glyph_order = tt.getGlyphOrder()

    print("upem=%d  hb=%s\n" % (f.upem, HB.hb_version_string().decode()))
    print("%-4s %6s  %10s  %10s  %10s  %10s"
          % ("ch", "units", "scale", "hb", "trunc", "real-round"))
    for ch in "mMHxoi.":
        gid = f.glyph(ch)
        v = hmtx[glyph_order[gid]][0]
        for size_pt in (1.5552,):
            # Pango units for the size, then the 1200-dpi device scale.
            for scale in (int(round(size_pt * 1024)),
                          int(round(size_pt * 1024 * 1200 / 72.0))):
                got = f.advance(gid, scale)
                print("%-4s %6d  %10d  %10d  %10d  %10d"
                      % (ch, v, scale, got,
                         cand_truncate(v, scale, f.upem),
                         cand_real_round(v, scale, f.upem)))


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "selfcheck"
    target = sys.argv[2] if len(sys.argv) > 2 else C059
    if mode == "selfcheck":
        sys.exit(0 if selfcheck(target) else 1)
    elif mode == "formula":
        formula(target)
    elif mode == "cell":
        cell(target)
    else:
        print(__doc__)
        sys.exit(2)
