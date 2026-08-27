#!/usr/bin/env python3
"""
Reproduce upstream lily/freetype.cc's outline walk exactly, using the SAME
library the oracle uses: FT_Load_Glyph with FT_LOAD_NO_SCALE, then
FT_Outline_Decompose.

WHY THIS EXISTS
---------------
PARITY 14 is checking D43's stated cause -- "the port walks the outline with
its own CFF interpreter where upstream walks it with FT_Outline_Decompose, and
the two disagree slightly".  Before fixing the walker, establish whether the
walker is even the variable.  Trap 8a: measure, do not reason from the last
defect.

WHAT IT REPORTS
---------------
Per glyph, the decomposed command stream in FONT UNITS (FT_LOAD_NO_SCALE gives
integer font units), which is exactly what upstream's Path_interpreter
receives.

SELF-CHECK (trap 32a -- verify the tool before believing it)
------------------------------------------------------------
The FT_FaceRec / FT_GlyphSlotRec offsets below are hand-computed for LP64.  A
wrong offset would produce confident nonsense, so `selfcheck` compares the
decomposed point cloud's bounding box against fontTools' independent reading
of the same glyph in the same file.  Run it before trusting any output.

CAVEAT
------
The system libfreetype is 2.13.3; the pinned oracle statically links 2.14.1.
FT_Outline_Decompose's contour walk has been unchanged for many years and the
comparisons here are structural, but a numeric claim resting on this probe
alone should say so.
"""

import ctypes
import sys

FT_LOAD_NO_SCALE = 1 << 0

# --- LP64 struct offsets, hand-computed from freetype.h -------------------
# FT_FaceRec: 5 FT_Long, 2 char*, int+pad, ptr, int+pad, ptr, FT_Generic(16),
#             FT_BBox(32), 8 FT_Short(16) -> FT_GlyphSlot
FACE_GLYPH_OFFSET = 152
# FT_GlyphSlotRec: 3 ptr, uint+pad, FT_Generic(16), FT_Glyph_Metrics(64),
#                  2 FT_Fixed(16), FT_Vector(16), enum+pad(8), FT_Bitmap(40),
#                  2 int(8) -> FT_Outline
SLOT_OUTLINE_OFFSET = 200


class FT_Vector(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


class FT_Outline(ctypes.Structure):
    _fields_ = [("n_contours", ctypes.c_ushort),
                ("n_points", ctypes.c_ushort),
                ("points", ctypes.POINTER(FT_Vector)),
                ("tags", ctypes.c_char_p),
                ("contours", ctypes.POINTER(ctypes.c_ushort)),
                ("flags", ctypes.c_int)]


MOVE = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.POINTER(FT_Vector), ctypes.c_void_p)
LINE = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.POINTER(FT_Vector), ctypes.c_void_p)
CONIC = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.POINTER(FT_Vector),
                         ctypes.POINTER(FT_Vector), ctypes.c_void_p)
CUBIC = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.POINTER(FT_Vector),
                         ctypes.POINTER(FT_Vector), ctypes.POINTER(FT_Vector),
                         ctypes.c_void_p)


class FT_Outline_Funcs(ctypes.Structure):
    _fields_ = [("move_to", MOVE), ("line_to", LINE), ("conic_to", CONIC),
                ("cubic_to", CUBIC), ("shift", ctypes.c_int),
                ("delta", ctypes.c_long)]


_ft = ctypes.CDLL("libfreetype.so.6")
_ft.FT_Get_Name_Index.restype = ctypes.c_uint


class Face:
    """One FreeType face, opened once and reused."""

    def __init__(self, path):
        self.lib = ctypes.c_void_p()
        if _ft.FT_Init_FreeType(ctypes.byref(self.lib)):
            raise RuntimeError("FT_Init_FreeType failed")
        self.face = ctypes.c_void_p()
        err = _ft.FT_New_Face(self.lib, path.encode(), 0,
                              ctypes.byref(self.face))
        if err:
            raise RuntimeError("FT_New_Face failed: %d" % err)

    def index_of(self, name):
        return _ft.FT_Get_Name_Index(self.face, name.encode())

    def _outline_ptr(self):
        slot = ctypes.cast(self.face.value + FACE_GLYPH_OFFSET,
                           ctypes.POINTER(ctypes.c_void_p))[0]
        return ctypes.cast(slot + SLOT_OUTLINE_OFFSET,
                           ctypes.POINTER(FT_Outline))

    def load(self, index):
        err = _ft.FT_Load_Glyph(self.face, index, FT_LOAD_NO_SCALE)
        if err:
            raise RuntimeError("FT_Load_Glyph(%d) failed: %d" % (index, err))
        return self._outline_ptr()

    def raw_points(self, index):
        """The outline's point array, before decomposition."""
        o = self.load(index).contents
        return [(o.points[i].x, o.points[i].y) for i in range(o.n_points)]

    def decompose(self, index):
        """Upstream's command stream for one glyph, in font units."""
        outline = self.load(index)
        out = []

        def mv(to, _u):
            out.append(("move", to[0].x, to[0].y))
            return 0

        def ln(to, _u):
            out.append(("line", to[0].x, to[0].y))
            return 0

        def cn(c1, to, _u):
            out.append(("conic", c1[0].x, c1[0].y, to[0].x, to[0].y))
            return 0

        def cb(c1, c2, to, _u):
            out.append(("cubic", c1[0].x, c1[0].y, c2[0].x, c2[0].y,
                        to[0].x, to[0].y))
            return 0

        funcs = FT_Outline_Funcs(MOVE(mv), LINE(ln), CONIC(cn), CUBIC(cb), 0, 0)
        err = _ft.FT_Outline_Decompose(outline, ctypes.byref(funcs), None)
        if err:
            raise RuntimeError("FT_Outline_Decompose failed: %d" % err)
        return out


def selfcheck(path):
    """Confirms the struct offsets by cross-reading with fontTools."""
    from fontTools.ttLib import TTFont
    from fontTools.pens.recordingPen import RecordingPen

    tt = TTFont(path)
    gs = tt.getGlyphSet()
    face = Face(path)
    checked = failed = 0
    for name in list(gs.keys()):
        idx = face.index_of(name)
        if idx == 0 and name != ".notdef":
            continue
        pen = RecordingPen()
        gs[name].draw(pen)
        pts = [p for _, args in pen.value for p in args if p]
        pts = [p for grp in pts for p in (grp if isinstance(grp[0], tuple) else [grp])]
        if not pts:
            continue
        want = (min(p[0] for p in pts), min(p[1] for p in pts),
                max(p[0] for p in pts), max(p[1] for p in pts))
        raw = face.raw_points(idx)
        if not raw:
            continue
        got = (min(p[0] for p in raw), min(p[1] for p in raw),
               max(p[0] for p in raw), max(p[1] for p in raw))
        checked += 1
        if want != got:
            failed += 1
            if failed <= 5:
                print("  MISMATCH %-30s fontTools=%s freetype=%s"
                      % (name, want, got))
    print("selfcheck %s: %d glyphs compared, %d bbox mismatches"
          % (path.rsplit('/', 1)[-1], checked, failed))
    return failed == 0


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    if sys.argv[1] == "selfcheck":
        ok = True
        for p in sys.argv[2:]:
            ok = selfcheck(p) and ok
        return 0 if ok else 2
    face = Face(sys.argv[1])
    for name in sys.argv[2:]:
        idx = face.index_of(name)
        cmds = face.decompose(idx)
        print("%s  index=%d  commands=%d" % (name, idx, len(cmds)))
        for cmd in cmds:
            print("   ", cmd)
    return 0


if __name__ == "__main__":
    sys.exit(main())
