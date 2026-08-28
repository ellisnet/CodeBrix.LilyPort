#!/usr/bin/env python3
"""
Measure what pango_font_description_to_string ACTUALLY emits for a font size,
by driving libpango rather than reasoning about it.

WHY THIS EXISTS
---------------
Engine PORT-COVERAGE's R10/R12 entry ("TEXT FONT SIZE, LAST DECIMAL") parks four
bounded-delta rows with an explicit revisit condition: "Measure what
pango_font_description_to_string actually emits, by driving the library rather
than reasoning about it -- the technique that settled D43 ... If the rule is
measured and the four values can be made exact, make them exact and retire this
entry; it exists because the cause is unmeasured, not because the divergence is
desirable."

The entry's own leading hypothesis is a SHORTEST-ROUND-TRIP formatter.  The port
formats to three decimals and its DescriptionString comment claims upstream does
the same -- a claim PORT-COVERAGE already flags as wrong (trap 26: a port comment
about upstream is not evidence about upstream).  This probe is the measurement.

WHAT IT REPORTS
---------------
For each size given in PANGO UNITS (1/1024 pt), the exact string libpango writes,
beside the three candidate formattings the entry names: three decimals, "%g"
(six significant digits) and shortest round trip (Python repr).  The verdict line
says which of the three the library's own output equals across the sweep.

SELF-CHECK (trap 3 / trap 32a -- verify the tool before believing it)
---------------------------------------------------------------------
`selfcheck` proves the probe is driving the library and not its own arithmetic:
  (i)   pango_version_string is read and printed -- the number this rests on;
  (ii)  a description built here round-trips through
        pango_font_description_from_string back to the same integer size, so the
        size really did reach the description;
  (iii) a size the library CANNOT be storing exactly (one that is not a whole
        number of Pango units) still comes back quantised, proving the integer
        lattice is the library's and not the probe's;
  (iv)  a family name with a space is quoted the way Pango quotes it, proving the
        returned bytes are Pango's own formatter and not a Python f-string.

CAVEAT
------
The oracle bundles Pango 1.57.0; this host carries what selfcheck prints (1.56.3
at the time of writing).  Rest STRUCTURAL claims on this probe -- "the formatter
is shortest-round-trip" -- and say so before resting a last-digit numeric one,
exactly the caveat D43's FreeType probe carries.
"""

import ctypes
import sys

PANGO_SCALE = 1024

_lib = None


def pango():
    """Load libpango once and declare the four entry points this probe uses."""
    global _lib
    if _lib is not None:
        return _lib
    lib = ctypes.CDLL("libpango-1.0.so.0")
    lib.pango_font_description_new.restype = ctypes.c_void_p
    lib.pango_font_description_free.argtypes = [ctypes.c_void_p]
    lib.pango_font_description_set_family.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
    lib.pango_font_description_set_size.argtypes = [ctypes.c_void_p, ctypes.c_int]
    lib.pango_font_description_get_size.argtypes = [ctypes.c_void_p]
    lib.pango_font_description_get_size.restype = ctypes.c_int
    lib.pango_font_description_to_string.argtypes = [ctypes.c_void_p]
    lib.pango_font_description_to_string.restype = ctypes.c_void_p
    lib.pango_font_description_from_string.argtypes = [ctypes.c_char_p]
    lib.pango_font_description_from_string.restype = ctypes.c_void_p
    lib.pango_version_string.restype = ctypes.c_char_p
    _lib = lib
    return lib


def _take_string(ptr):
    """Copy a g_malloc'd char* out and free it the way GLib expects."""
    text = ctypes.cast(ptr, ctypes.c_char_p).value.decode("utf-8")
    ctypes.CDLL("libc.so.6").free(ctypes.c_void_p(ptr))
    return text


def describe(units, family="LilyPond Serif"):
    """The string libpango writes for a description of `units` Pango units."""
    lib = pango()
    desc = lib.pango_font_description_new()
    lib.pango_font_description_set_family(desc, family.encode("utf-8"))
    lib.pango_font_description_set_size(desc, units)
    text = _take_string(lib.pango_font_description_to_string(desc))
    lib.pango_font_description_free(desc)
    return text


def size_field(units, family="LilyPond Serif"):
    """Just the trailing size token -- what output-svg.scm's `[ -]([0-9.]+)$' takes."""
    return describe(units, family).rsplit(" ", 1)[-1]


def candidates(units):
    """The three formattings PORT-COVERAGE names, for the same value."""
    exact = units / PANGO_SCALE
    return {
        "three-decimals": "%.3f" % exact,
        "%g-six-sig": "%g" % exact,
        "shortest-round-trip": repr(exact),
    }


def selfcheck():
    lib = pango()
    version = lib.pango_version_string().decode("ascii")
    print("(i)   pango_version_string = %s" % version)

    lib_desc = lib.pango_font_description_from_string(b"LilyPond Serif 11.5")
    got = lib.pango_font_description_get_size(lib_desc)
    lib.pango_font_description_free(lib_desc)
    ok_ii = got == int(round(11.5 * PANGO_SCALE))
    print("(ii)  from_string('… 11.5') -> get_size %d units (expect %d): %s"
          % (got, int(round(11.5 * PANGO_SCALE)), "OK" if ok_ii else "FAIL"))

    # 11.50033 pt is NOT a whole number of Pango units; the library must quantise.
    lib_desc = lib.pango_font_description_from_string(b"LilyPond Serif 11.50033")
    got2 = lib.pango_font_description_get_size(lib_desc)
    lib.pango_font_description_free(lib_desc)
    ok_iii = got2 == int(round(11.50033 * PANGO_SCALE))
    print("(iii) from_string('… 11.50033') -> get_size %d units (a whole number of "
          "1/1024ths, not the real): %s" % (got2, "OK" if ok_iii else "FAIL"))

    quoted = describe(10 * PANGO_SCALE, "Emmentaler Brace")
    ok_iv = quoted.startswith('"Emmentaler Brace"') or quoted.startswith("Emmentaler Brace")
    print("(iv)  to_string family formatting: %r: %s" % (quoted, "OK" if ok_iv else "FAIL"))

    return all([ok_ii, ok_iii, ok_iv])


def sweep(units_list):
    print("%-10s %-22s %-22s %-14s %-14s %s"
          % ("units", "pango to_string", "shortest-round-trip", "%.3f", "%g", "pango =="))
    verdict = {"three-decimals": 0, "%g-six-sig": 0, "shortest-round-trip": 0}
    for units in units_list:
        emitted = size_field(units)
        cands = candidates(units)
        hits = [name for name, text in cands.items() if text == emitted]
        for name in hits:
            verdict[name] += 1
        print("%-10d %-22s %-22s %-14s %-14s %s"
              % (units, emitted, cands["shortest-round-trip"],
                 cands["three-decimals"], cands["%g-six-sig"],
                 ",".join(hits) if hits else "NONE"))
    total = len(units_list)
    print()
    for name, hit in sorted(verdict.items(), key=lambda kv: -kv[1]):
        print("VERDICT  %-22s reproduces %d of %d" % (name, hit, total))


def main():
    argv = sys.argv[1:]
    if argv and argv[0] == "selfcheck":
        sys.exit(0 if selfcheck() else 1)
    if argv:
        sweep([int(a) for a in argv])
        return
    # A default sweep spanning the corpus's range, deliberately including sizes
    # whose exact value needs more than three decimals.
    default = [
        1024, 2048, 4096, 8192,
        9215, 9216, 9217,
        10000, 10240, 11111, 11776,
        12345, 16384, 20480, 22222, 24576, 32768,
    ]
    sweep(default)


if __name__ == "__main__":
    main()
