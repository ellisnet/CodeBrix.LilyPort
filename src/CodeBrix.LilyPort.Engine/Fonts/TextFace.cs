// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// One text FACE — a single OTF file — reduced to what laying out a line of text needs:
/// which glyph a character maps to, how far the pen moves, and how tall the ink is.
/// <para>
/// New-in-family. Upstream asks Pango, which asks FreeType; the port reads the tables
/// directly and runs the charstrings (see <see cref="CffFont"/>) for the one figure no
/// table records.
/// </para>
/// </summary>
public sealed class TextFace
{
    // Pango's own sample string for the language `en-us', which is the language the
    // corpus's oracle runs report because the machine that made the reference has
    // LANG=en_US.UTF-8. See ApproximateCharWidth for why a string is baked in here at
    // all and what the alternative measures.
    private const string SampleString
        = "The wizard quickly jinxed the gnomes before they vaporized.";

    // pango_utf8_strwidth of the sample: one per character, because every character in it
    // is narrow. Spelled out rather than counted so the pairing with the string above is
    // visible at a glance.
    private const int SampleStringWidth = 59;

    private readonly SfntReader _reader;
    private readonly CffFont _cff;
    private readonly Dictionary<int, int> _cmap;
    private readonly double[] _advances;
    private readonly KerningTable _kerning;
    private readonly SubstitutionTable _substitutions;

    // Pango's approximate_char_width, in Pango units, keyed by the HarfBuzz scale the run
    // is shaped at. The figure is a per-(face, size) CONSTANT — it depends on nothing the
    // run carries — and computing it lays out a 59-character string, so it is cached.
    private readonly Dictionary<int, long> _approximateCharWidths = new Dictionary<int, long>();

    private TextFace(string fileName, SfntReader reader)
    {
        FileName = fileName;
        _reader = reader;
        UnitsPerEm = reader.UnitsPerEm;
        _cmap = reader.ReadCmap();
        _advances = reader.ReadAdvances();
        _kerning = KerningTable.Read(reader);
        _substitutions = SubstitutionTable.Read(reader);

        (int ascender, int descender) = reader.ReadHorizontalAscenderDescender();
        Ascender = ascender;
        Descender = descender;

        byte[] cff = reader.GetTable("CFF ");
        _cff = cff == null ? null : new CffFont(cff);

        (int xHeight, int capHeight) = reader.ReadXAndCapHeight();

        // PANGO'S SYNTHETIC SMALL CAPS ARE CAPITALS AT THE HEIGHT OF LOWERCASE, so the
        // scale is the face's own x-height over its own cap height — read off the font
        // (rule 35a), not chosen. C059-Roman answers 466/722 = 0.6454293628808865, which
        // is what the pinned oracle's `\fontCaps' measurements come out at.
        //
        // A face too old to carry the two fields gets 1.0, which reproduces what the
        // engine did before small caps were synthesised at all: nothing.
        SmallCapsScale = xHeight > 0 && capHeight > 0
            ? xHeight / (double)capHeight
            : 1.0;
    }

    /// <summary>Loads a vendored text face by file name, or returns null when absent.</summary>
    /// <param name="fileName">The file name, such as <c>C059-Roman.otf</c>.</param>
    /// <returns>The face.</returns>
    public static TextFace Load(string fileName)
    {
        byte[] bytes = FontAssets.TextFont(fileName);
        return bytes == null ? null : new TextFace(fileName, new SfntReader(bytes));
    }

    /// <summary>
    /// Loads a face from a PATH ON DISK — a font the DOCUMENT supplied rather than one
    /// the port ships.
    /// <para>
    /// Ruling R16: this is not the system-font fallback D23 forbids. Upstream implements
    /// <c>ly:font-config-add-font</c> as an <em>application</em> font
    /// (<c>all-font-metrics.cc</c> → <c>FcConfigAppFontAddFile</c>), the same set
    /// LilyPond's own bundled faces go into, and a document that carries its font files
    /// beside it renders the same on every machine — which is the whole point of the
    /// documented feature and the opposite of depending on the host.
    /// </para>
    /// </summary>
    /// <param name="path">The path to the font file.</param>
    /// <returns>The face, or <see langword="null"/> when it cannot be read.</returns>
    public static TextFace LoadFromPath(string path)
    {
        try
        {
            return new TextFace(System.IO.Path.GetFileName(path), SfntReader.FromFile(path))
            {
                SourcePath = System.IO.Path.GetFullPath(path),
            };
        }
        catch (System.IO.IOException)
        {
            return null;
        }
        catch (System.UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Gets the family name the face declares, or <see langword="null"/>.</summary>
    public string FamilyName => _reader.ReadFamilyName();

    /// <summary>Gets the file the face was read from.</summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the absolute path a DOCUMENT-supplied face was read from, or
    /// <see langword="null"/> for one of the vendored faces, which has no path at all —
    /// it is an embedded resource. <c>ly:font-config-get-font-file</c> (R18) answers
    /// with this for a document font and with a resource path for a vendored one.
    /// </summary>
    public string SourcePath { get; private init; }

    /// <summary>Gets the design units per em.</summary>
    public int UnitsPerEm { get; }

    /// <summary>
    /// Gets the face's <c>hhea</c> ascender, in design units — the ASCENT half of Pango's
    /// font metrics, and so the top of the box a character no face covers gets.
    /// </summary>
    /// <remarks>
    /// <c>pango_fc_font_create_base_metrics_for_context</c> takes
    /// <c>metrics-&gt;ascent</c> from <c>hb_font_get_extents_for_direction</c>, which for
    /// a horizontal direction is <c>hhea</c>'s ascender scaled by HarfBuzz's
    /// <c>em_scale_y</c>. See <see cref="SfntReader.ReadHorizontalAscenderDescender"/>
    /// for why the <c>OS/2</c> typographic pair is NOT this one.
    /// </remarks>
    public int Ascender { get; }

    /// <summary>
    /// Gets the face's <c>hhea</c> descender, in design units and normally NEGATIVE — the
    /// DESCENT half of Pango's font metrics.
    /// </summary>
    public int Descender { get; }

    /// <summary>
    /// Gets the factor a synthesised small capital is set at — the face's x-height over
    /// its cap height, or 1.0 when <c>OS/2</c> does not say.
    /// </summary>
    public double SmallCapsScale { get; }

    /// <summary>Gets the underlying container reader.</summary>
    public SfntReader Reader => _reader;

    /// <summary>Gets the face's GSUB substitutions, as the shaper applies them.</summary>
    /// <remarks>
    /// Exposed for the same reason <see cref="OpenTypeFont.Substitutions"/> is: which
    /// GSUB SCRIPT a face's features are read from is a decision the fences have to be
    /// able to check against the font, and for a text face the answer differs from the
    /// music font's — twelve of the vendored text faces name <c>liga</c> from
    /// <c>latn</c> and from no other script.
    /// </remarks>
    public SubstitutionTable Substitutions => _substitutions;

    /// <summary>Determines whether the face can draw a code point.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns><see langword="true"/> when it maps to a real glyph.</returns>
    public bool Covers(int codePoint) => _cmap.ContainsKey(codePoint);

    /// <summary>Returns a code point's glyph index, or 0 for <c>.notdef</c>.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The glyph index.</returns>
    public int GlyphIndex(int codePoint)
        => _cmap.TryGetValue(codePoint, out int glyph) ? glyph : 0;

    /// <summary>Returns a glyph's horizontal advance, in design units.</summary>
    /// <param name="glyph">The glyph index.</param>
    /// <returns>The advance.</returns>
    public double Advance(int glyph)
        => glyph >= 0 && glyph < _advances.Length ? _advances[glyph] : 0.0;

    /// <summary>
    /// Returns the kerning advance adjustment between two adjacent glyphs of one run,
    /// in design units. Zero when the face carries no kerning or the pair none.
    /// </summary>
    /// <param name="leftGlyph">The earlier glyph's index.</param>
    /// <param name="rightGlyph">The later glyph's index.</param>
    /// <returns>The adjustment; most kern pairs are negative.</returns>
    public double Kerning(int leftGlyph, int rightGlyph)
        => _kerning == null ? 0.0 : _kerning.Adjustment(leftGlyph, rightGlyph);

    /// <summary>
    /// Applies this face's GSUB substitutions to one run of its own glyphs, in place.
    /// Substitution runs BEFORE kerning, because kerning belongs to the pair the
    /// substituted run actually contains — the order HarfBuzz applies GSUB and GPOS in.
    /// </summary>
    /// <param name="glyphs">The run's glyph indices; rewritten in place.</param>
    /// <param name="features">The comma-separated feature string, possibly empty.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Substitute(List<int> glyphs, string features)
        => _substitutions != null && _substitutions.Apply(glyphs, features);

    /// <summary>Returns a glyph's ink bounding box, in design units.</summary>
    /// <param name="glyph">The glyph index.</param>
    /// <returns>The box.</returns>
    public Box GlyphBox(int glyph) => _cff == null ? default : _cff.GlyphBox(glyph);

    /// <summary>
    /// Returns Pango's <c>approximate_char_width</c> for this face at one HarfBuzz scale,
    /// in PANGO UNITS — the LOGICAL width a character no face covers occupies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>pango_fc_font_get_metrics</c> computes it by laying out the LANGUAGE'S SAMPLE
    /// STRING in the font and dividing the layout's logical width by
    /// <c>pango_utf8_strwidth</c> of that string — an INTEGER division, in Pango units.
    /// The layout is an ordinary one: it kerns, it substitutes, and
    /// <c>PANGO_SHAPE_ROUND_POSITIONS</c> rounds every glyph's advance to a whole device
    /// dot, so this is the same arithmetic <see cref="TextFontMetric.TextStencil(string, string, bool)"/> does
    /// for any other run. It carries NONE of the run's own <c>font-features</c>: the
    /// figure belongs to the font, not to the run.
    /// </para>
    /// <para>
    /// ⚠ THE SAMPLE STRING IS UPSTREAM'S ONE HOST DEPENDENCE IN THIS FIGURE, and the port
    /// pins it. <c>pango_language_get_sample_string (NULL)</c> asks
    /// <c>pango_language_get_default ()</c>, which reads the process's locale, and Pango
    /// 1.57's table answers the <c>en-us</c> entry above for <c>LANG=en_US.UTF-8</c> and
    /// its fallback pangram — "The quick brown fox jumps over the lazy dog." — for every
    /// other locale this was tried under (C, en-gb, de, fr, ja, ru: MEASURED, all seven
    /// runs of the same file through the pinned oracle). The two differ: at
    /// <c>\abs-fontsize #60</c> in C059-Roman the box is 166 device dots wide under
    /// <c>en-us</c> and 165 under the fallback. The port cannot be allowed to render a
    /// score differently because of a locale (D23's rule), so one of the two is baked in,
    /// and it is the one the corpus's reference and the calibration were MEASURED under
    /// (D66: the <c>en-us</c> string stays).
    /// A locale that changes what upstream draws is upstream's defect, not a contract to
    /// reproduce.
    /// </para>
    /// </remarks>
    /// <param name="xScale">The HarfBuzz scale the run is shaped at, in Pango units.</param>
    /// <returns>The approximate character width, in Pango units.</returns>
    public long ApproximateCharWidth(int xScale)
    {
        if (_approximateCharWidths.TryGetValue(xScale, out long cached))
        {
            return cached;
        }

        long multiplier = TextFontMetric.Multiplier(xScale, UnitsPerEm);

        List<int> glyphs = new List<int>(SampleString.Length);
        foreach (char character in SampleString)
        {
            glyphs.Add(GlyphIndex(character));
        }

        Substitute(glyphs, string.Empty);

        long total = 0;
        for (int i = 0; i < glyphs.Count; i++)
        {
            long step = TextFontMetric.EmMult(
                TextFontMetric.DesignUnits(Advance(glyphs[i])), multiplier);
            if (i + 1 < glyphs.Count)
            {
                step += TextFontMetric.EmMult(
                    TextFontMetric.DesignUnits(Kerning(glyphs[i], glyphs[i + 1])), multiplier);
            }

            total += TextFontMetric.PangoUnitsRound(step);
        }

        long width = total / SampleStringWidth;
        _approximateCharWidths[xScale] = width;
        return width;
    }

    /// <summary>Gets the face's charstring interpreter, or <see langword="null"/>.</summary>
    /// <remarks>
    /// The script machinery needs it to trace a text run's real outlines into a skyline,
    /// which is what closed the carried-forward text divergence.
    /// </remarks>
    public CffFont Cff => _cff;
}

/// <summary>
/// The ordered list of faces a text request may draw from — decision D23's fallback
/// chain, made concrete.
/// <para>
/// A chain is a family (serif, sans, typewriter) crossed with a style (bold, italic),
/// and it runs: the URW face LilyPond defaults to, then the TeX Gyre face upstream's
/// <c>00-lilypond-fonts.conf</c> names next, and then STOPS. Upstream continues into
/// DejaVu and Noto CJK, which it does not ship; the port stops at TeX Gyre and never
/// continues into a system font, so what a score looks like does not depend on what
/// happens to be installed on the machine that renders it. A code point no face in the
/// chain covers deliberately draws missing-glyph tofu.
/// </para>
/// <para>
/// A FAMILY NAME NOTHING ALIASES AND NO VENDORED FACE PROVIDES gets the <c>unknown</c>
/// chain, TeX Gyre Schola — which is not a port-side choice but a measurement. Upstream
/// asks fontconfig, and under the corpus's own pinning fontconfig best-matches such a
/// name to TeX Gyre Schola Regular over the bundled directory. The names that DO resolve
/// by category are enumerated in <see cref="Generics"/>, and they come from two
/// configurations rather than one. See <see cref="Normalize"/> and ruling R14.
/// </para>
/// </summary>
public static class TextFontChain
{
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, TextFace> Loaded
        = new Dictionary<string, TextFace>(StringComparer.Ordinal);

    // The DOCUMENT's own fonts, keyed by the family name each file declares. Ordinal-
    // ignore-case because a document types the family by hand and fontconfig's family
    // matching is case-insensitive.
    private static readonly Dictionary<string, TextFace> DocumentFonts
        = new Dictionary<string, TextFace>(StringComparer.OrdinalIgnoreCase);

    // VendoredFamilyLevels()' answer, built once and dropped by Reset() — the declared
    // names are read off the faces as they LOAD, so a change to FontAssets.SearchPaths
    // could put a different file, and a different declared family, behind a name.
    private static Dictionary<string, string[]> DeclaredLevels;

    // Each family lists its fallback levels, and each level its four faces indexed by
    // (bold ? 1 : 0) + (italic ? 2 : 0). Spelled out rather than generated from a
    // template because the three collections do not agree on how to name a face: URW
    // writes "Regular" and "BoldItalic", C059 writes "Roman" and "BdIta", and TeX Gyre
    // writes everything in lower case. A template silently produces a file name that
    // does not exist, and a missing face does not fail — it just drops out of the
    // chain, leaving text measured by the FALLBACK font.
    private static readonly Dictionary<string, string[][]> Families
        = new Dictionary<string, string[][]>(StringComparer.OrdinalIgnoreCase)
        {
            ["serif"] = new[]
            {
                new[]
                {
                    "C059-Roman.otf", "C059-Bold.otf", "C059-Italic.otf", "C059-BdIta.otf",
                },
                new[]
                {
                    "texgyreschola-regular.otf", "texgyreschola-bold.otf",
                    "texgyreschola-italic.otf", "texgyreschola-bolditalic.otf",
                },
            },
            ["sans"] = new[]
            {
                new[]
                {
                    "NimbusSans-Regular.otf", "NimbusSans-Bold.otf",
                    "NimbusSans-Italic.otf", "NimbusSans-BoldItalic.otf",
                },
                new[]
                {
                    "texgyreheros-regular.otf", "texgyreheros-bold.otf",
                    "texgyreheros-italic.otf", "texgyreheros-bolditalic.otf",
                },
            },
            ["typewriter"] = new[]
            {
                new[]
                {
                    "NimbusMonoPS-Regular.otf", "NimbusMonoPS-Bold.otf",
                    "NimbusMonoPS-Italic.otf", "NimbusMonoPS-BoldItalic.otf",
                },
                new[]
                {
                    "texgyrecursor-regular.otf", "texgyrecursor-bold.otf",
                    "texgyrecursor-italic.otf", "texgyrecursor-bolditalic.otf",
                },
            },

            // A family none of the 24 faces provides. ONE level, because this is not a
            // fallback chain at all: it is the single face fontconfig answers with, and
            // adding a second level would be inventing coverage upstream does not offer
            // for the same request. Ruling R14, MEASURED with fc-match under the
            // corpus's own pinning over eight unavailable names -- including "Arial" and
            // "Foo Bar Baz" -- which all answer TeX Gyre Schola, and at every style:
            // "DejaVu Sans:weight=bold" answers TeX Gyre Schola Bold.
            ["unknown"] = new[]
            {
                new[]
                {
                    "texgyreschola-regular.otf", "texgyreschola-bold.otf",
                    "texgyreschola-italic.otf", "texgyreschola-bolditalic.otf",
                },
            },
        };

    // The family names that resolve by CATEGORY rather than by best match, and the port
    // chain each one means. There are two groups and they come from two different
    // configurations, which is the whole reason this table is spelled out:
    //
    //   (1) THE CSS GENERICS. reference-fonts.conf.in aliases serif, sans, sans-serif
    //       and monospace; ly/paper-defaults-init.ly:170-181 makes LilyPond ask for
    //       "serif", "sans" and "monospace" under -dbackend=svg.
    //
    //   (2) LILYPOND'S OWN THREE VIRTUAL NAMES, which its shipped
    //       fonts/00-lilypond-fonts.conf aliases -- "LilyPond Serif" to C059 then TeX
    //       Gyre Schola, "LilyPond Sans Serif" to Nimbus Sans then TeX Gyre Heros,
    //       "LilyPond Monospace" to Nimbus Mono PS then TeX Gyre Cursor. That is D23's
    //       chain, face for face, because D23 was built from this file.
    //
    // /!\ GROUP (2) IS NOT REACHABLE ONLY THROUGH THE PAPER VARIABLE, and assuming it
    // was cost a corpus row. `markup-music-glyph.ly' sets font-name to "LilyPond Sans
    // Serif" DIRECTLY, which bypasses paper-defaults-init.ly's backend switch entirely.
    //
    // /!\ AND fc-match CANNOT MEASURE GROUP (2): LilyPond loads 00-lilypond-fonts.conf
    // into its own FcConfig at startup (lily/font-config.cc), so those three names are
    // aliased INSIDE the oracle's process even though FONTCONFIG_FILE has replaced the
    // system configuration. A shell fc-match answers TeX Gyre Schola for "LilyPond
    // Serif" and the oracle answers C059. Read group (2) off the conf, never off
    // fc-match.
    private static readonly Dictionary<string, string> Generics
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["serif"] = "serif",
            ["sans"] = "sans",
            ["sans-serif"] = "sans",
            ["monospace"] = "typewriter",
            ["LilyPond Serif"] = "serif",
            ["LilyPond Sans Serif"] = "sans",
            ["LilyPond Monospace"] = "typewriter",
        };

    /// <summary>
    /// Returns the faces to try, in order, for a family and style.
    /// </summary>
    /// <param name="family">
    /// The family requested: a generic name (<c>serif</c>, <c>sans</c>,
    /// <c>sans-serif</c>, <c>monospace</c>), the family name a VENDORED face declares
    /// (<c>C059</c>, <c>Nimbus Sans</c>, …), a comma-separated list of names, or any
    /// other family name — see <see cref="VendoredLevel"/> and <see cref="Normalize"/>.
    /// </param>
    /// <param name="bold">Whether bold was asked for.</param>
    /// <param name="italic">Whether italic was asked for.</param>
    /// <returns>The loaded faces, in fallback order; empty when nothing resolved.</returns>
    public static IReadOnlyList<TextFace> For(string family, bool bold, bool italic)
    {
        // A DOCUMENT-SUPPLIED FACE IS CONSULTED FIRST (R16), and it is an EXACT family
        // match on one entry of the CSS list, which is what fontconfig's best-match comes
        // to once the document's own files are in the application font set. It has to
        // come before Normalize, because a document font's family is by definition not
        // one of the three generic names and would otherwise fall into R14's
        // unknown → TeX Gyre Schola arm.
        //
        // ONE LEVEL, not a chain: the document named this face, and putting a vendored
        // fallback behind it would silently draw a different typeface for any code point
        // the document's own font happens not to cover.
        TextFace supplied = DocumentFont(family);
        if (supplied != null)
        {
            return new[] { supplied };
        }

        int style = (bold ? 1 : 0) + (italic ? 2 : 0);

        // A VENDORED FACE ANSWERS TO THE FAMILY NAME IT DECLARES, and it must, because
        // R18 has the port REPORT those names: ly:font-config-display-fonts lists the six
        // families the 24 embedded faces call themselves, and a listing a document cannot
        // then select from is a listing of nothing. Before this, every one of the six —
        // C059, TeX Gyre Schola, Nimbus Sans, TeX Gyre Heros, Nimbus Mono PS, TeX Gyre
        // Cursor — fell into R14's unknown arm, so asking for a real embedded family by
        // name engraved byte-for-byte the same as asking for a font that does not exist.
        //
        // It goes HERE, after the document's own registrations and before Normalize, for
        // the same reason the document arm does: none of these names is a generic, so
        // Normalize would send every one of them to `unknown'. Nothing a generic name
        // resolves to can change, because VendoredLevel gives way to a generic name and
        // the two name sets are disjoint.
        //
        // ONE LEVEL, on the same reasoning as the document arm: the request named a
        // FACE, and a second typeface behind it would be coverage nobody asked for.
        string[] declaredLevel = VendoredLevel(family);
        if (declaredLevel != null)
        {
            TextFace declared = Face(declaredLevel[style]);
            if (declared != null)
            {
                return new[] { declared };
            }

            // The style's file is not there — the same "drops out of the chain" case the
            // Families table warns about. Fall through, so the request is answered the
            // way an unavailable family is rather than with nothing at all.
        }

        string key = Normalize(family);
        if (!Families.TryGetValue(key, out string[][] levels))
        {
            levels = Families["unknown"];
        }

        List<TextFace> chain = new List<TextFace>();
        foreach (string[] level in levels)
        {
            TextFace face = Face(level[style]);
            if (face != null)
            {
                chain.Add(face);
            }
        }

        return chain;
    }

    /// <summary>
    /// The four-style level to draw from for the family a VENDORED face declares, or
    /// <see langword="null"/> when the request names no such family.
    /// </summary>
    /// <remarks>
    /// The list is walked IN ORDER and a generic name stops the walk, because that is
    /// what fontconfig does with a CSS family list: it takes the first entry it can
    /// satisfy. So <c>"Linux Libertine O,serif"</c> still reaches the serif chain through
    /// its second entry, and <c>"serif,Nimbus Sans"</c> still reaches it through its
    /// first — giving way to <see cref="Normalize"/>, which walks the same list from the
    /// start and finds the same generic.
    /// </remarks>
    /// <param name="family">The family or comma-separated family list requested.</param>
    /// <returns>The level, or <see langword="null"/>.</returns>
    private static string[] VendoredLevel(string family)
    {
        if (string.IsNullOrEmpty(family))
        {
            return null;
        }

        Dictionary<string, string[]> declared = VendoredFamilyLevels();
        foreach (string entry in family.Split(','))
        {
            string name = entry.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (Generics.ContainsKey(name))
            {
                return null;
            }

            if (declared.TryGetValue(name, out string[] level))
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>
    /// The family name each vendored face DECLARES, mapped to the four-style level it
    /// belongs to.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="Families"/> on first use rather than written out, so it can
    /// never disagree with the table it indexes, and read off each face's own name table
    /// rather than its file name, because those are the names R18 reports. Case-
    /// insensitive, as fontconfig's family matching is. TeX Gyre Schola names two levels
    /// (serif's second and <c>unknown</c>'s only) whose four files are the same, so
    /// whichever registers first is the same answer.
    /// </remarks>
    /// <returns>The map.</returns>
    private static Dictionary<string, string[]> VendoredFamilyLevels()
    {
        lock (Gate)
        {
            if (DeclaredLevels != null)
            {
                return DeclaredLevels;
            }

            Dictionary<string, string[]> levels
                = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string[][]> family in Families)
            {
                foreach (string[] level in family.Value)
                {
                    foreach (string fileName in level)
                    {
                        string name = Face(fileName)?.FamilyName;
                        if (!string.IsNullOrEmpty(name) && !levels.ContainsKey(name))
                        {
                            levels[name] = level;
                        }
                    }
                }
            }

            DeclaredLevels = levels;
            return levels;
        }
    }

    /// <summary>
    /// Lists every vendored face file the family table names, family by family and
    /// regular-first within each, with repeats left in for the caller to drop.
    /// </summary>
    /// <returns>The file names.</returns>
    private static IEnumerable<string> VendoredFaceFileNames()
    {
        foreach (KeyValuePair<string, string[][]> family in Families)
        {
            foreach (string[] level in family.Value)
            {
                foreach (string fileName in level)
                {
                    yield return fileName;
                }
            }
        }
    }

    /// <summary>Loads a face by file name, caching it.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The face, or <see langword="null"/> when there is no such file.</returns>
    public static TextFace Face(string fileName)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(fileName, out TextFace cached))
            {
                return cached;
            }

            TextFace face = TextFace.Load(fileName);
            Loaded[fileName] = face;
            return face;
        }
    }

    /// <summary>
    /// Lists the vendored text faces in the order the family table declares them, each
    /// with the family name the FILE declares and the location its bytes come from.
    /// </summary>
    /// <remarks>
    /// Ruling R18, and the ORDER is the family table's, not the manifest's: the table
    /// lists each family's four faces regular-bold-italic-bolditalic, which is what lets
    /// <see cref="VendoredFaceLocation"/> answer a bare family name with its REGULAR face,
    /// as fontconfig's best match does. Every one of the 24 faces appears in that table;
    /// TeX Gyre Schola appears twice, once as serif's second level and once as R14's
    /// unknown-family answer, and is listed once.
    /// </remarks>
    /// <returns>The faces, regular first within each family.</returns>
    public static IReadOnlyList<TextFace> VendoredFaces()
    {
        List<TextFace> faces = new List<TextFace>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string fileName in VendoredFaceFileNames())
        {
            if (!seen.Add(fileName))
            {
                continue;
            }

            TextFace face = Face(fileName);
            if (face != null)
            {
                faces.Add(face);
            }
        }

        return faces;
    }

    /// <summary>
    /// Answers where the vendored face for a family name lives, preferring the family's
    /// REGULAR face, or <see langword="null"/> when no vendored face declares that family.
    /// </summary>
    /// <remarks>
    /// The comparison is on the family name the FILE declares, not on the port's own
    /// generic keys: "serif" is a request the chain answers, not a family any face calls
    /// itself, and R18 is about naming a FACE. Case-insensitive, because fontconfig's
    /// family matching is.
    /// </remarks>
    /// <param name="family">The family name asked for.</param>
    /// <returns>The location, or <see langword="null"/>.</returns>
    public static string VendoredFaceLocation(string family)
    {
        if (string.IsNullOrEmpty(family))
        {
            return null;
        }

        foreach (TextFace face in VendoredFaces())
        {
            if (string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                return FontAssets.TextFontLocation(face.FileName);
            }
        }

        return null;
    }

    /// <summary>
    /// Lists the document-supplied registrations, family name and face, in the order the
    /// families sort.
    /// </summary>
    /// <returns>The registrations, empty when this document supplied no fonts.</returns>
    public static IReadOnlyList<KeyValuePair<string, TextFace>> DocumentFontRegistrations()
    {
        lock (Gate)
        {
            List<KeyValuePair<string, TextFace>> entries
                = new List<KeyValuePair<string, TextFace>>(DocumentFonts);
            entries.Sort((left, right)
                => string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));
            return entries;
        }
    }

    /// <summary>Discards every loaded face.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Loaded.Clear();
            DocumentFonts.Clear();
            DeclaredLevels = null;
        }
    }

    /// <summary>
    /// Registers one document-supplied font FILE, under the family name the file itself
    /// declares — upstream's <c>ly:font-config-add-font</c> (R16).
    /// </summary>
    /// <param name="path">The path to the font file.</param>
    /// <returns>Whether a face was registered.</returns>
    public static bool AddDocumentFont(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        TextFace face = TextFace.LoadFromPath(path);
        string family = face?.FamilyName;
        if (face == null || string.IsNullOrEmpty(family))
        {
            return false;
        }

        lock (Gate)
        {
            DocumentFonts[family] = face;
        }

        return true;
    }

    /// <summary>
    /// Registers every font file in a DIRECTORY — upstream's
    /// <c>ly:font-config-add-directory</c> (R16).
    /// </summary>
    /// <param name="directory">The directory to scan.</param>
    /// <returns>How many faces were registered.</returns>
    public static int AddDocumentFontDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
        {
            return 0;
        }

        int added = 0;
        foreach (string path in System.IO.Directory.GetFiles(directory))
        {
            if (AddDocumentFont(path))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Discards every document-supplied registration.
    /// <para>
    /// ⚠ THIS IS PER-FILE, and it is a leak of exactly the shape the other twelve had
    /// (trap 16). Upstream makes one fontconfig configuration per process and engraves
    /// one file per process; the port sweeps 2,146 files through one. A registration
    /// that outlived its file would let file N+1 resolve a family it never asked for —
    /// and <c>font-name-add-files.ly</c> DELETES its font files on the way out, so the
    /// leaked registration would point at a path that no longer exists.
    /// </para>
    /// </summary>
    public static void ResetDocumentFonts()
    {
        lock (Gate)
        {
            DocumentFonts.Clear();
        }
    }

    /// <summary>
    /// Returns the document-supplied face a family list names, or
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="family">The family or comma-separated family list requested.</param>
    /// <returns>The face, or <see langword="null"/>.</returns>
    public static TextFace DocumentFont(string family)
    {
        if (string.IsNullOrEmpty(family))
        {
            return null;
        }

        lock (Gate)
        {
            if (DocumentFonts.Count == 0)
            {
                return null;
            }

            foreach (string entry in family.Split(','))
            {
                string name = entry.Trim();
                if (name.Length != 0 && DocumentFonts.TryGetValue(name, out TextFace face))
                {
                    return face;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reduces a requested font family to the chain that serves it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A font family is a CSS family LIST, not one name — <c>kievan-notation.ly</c> asks
    /// for <c>"Linux Libertine O,serif"</c> — and fontconfig walks it, taking the first
    /// entry it can satisfy. So this walks it too, and matches a generic name EXACTLY
    /// within an entry.
    /// </para>
    /// <para>
    /// ⚠ IT USED TO SNIFF THE NAME instead: "contains mono" → typewriter, "contains
    /// sans" → sans, else serif. That has no upstream counterpart — upstream asks
    /// fontconfig and does not inspect family names anywhere — and it was wrong twice
    /// over. It sent <c>"DejaVu Sans Mono"</c>, a family the port does not have, to
    /// Nimbus Mono PS where the oracle answers TeX Gyre Schola; and a substring test
    /// over the WHOLE string would send <c>"Linux Libertine O,serif"</c> to Schola,
    /// where the oracle reaches C059 through the list's second entry. Ruling R14 (a),
    /// worth seven corpus rows; both halves MEASURED with <c>fc-match</c> under the
    /// corpus's own pinning (trap 8b).
    /// </para>
    /// <para>
    /// An empty entry is skipped rather than defaulted, and there is a real one to skip:
    /// <c>font-name = "Bitstream Vera Sans, Bold"</c> is a Pango description, so
    /// <c>FontInterface.ParseDescription</c> takes " Bold" off as a STYLE word and hands
    /// this the family <c>"Bitstream Vera Sans,"</c> — trailing comma included.
    /// </para>
    /// </remarks>
    /// <param name="family">The family or comma-separated family list requested.</param>
    /// <returns>The <see cref="Families"/> key to draw from.</returns>
    private static string Normalize(string family)
    {
        if (string.IsNullOrEmpty(family))
        {
            return "serif";
        }

        foreach (string entry in family.Split(','))
        {
            string name = entry.Trim();
            if (name.Length != 0 && Generics.TryGetValue(name, out string generic))
            {
                return generic;
            }
        }

        return "unknown";
    }
}
