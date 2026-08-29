// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using System.Linq;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// What a character NO FACE IN THE CHAIN COVERS measures, and what it draws.
/// <para>
/// Upstream cannot draw it either, and neither engine puts a glyph on the page for it.
/// What upstream DOES do is reserve Pango's UNKNOWN-GLYPH BOX — a rectangle as wide as
/// the font's <c>approximate_char_width</c> and running from the font's ascent to its
/// descent, both inset by one device dot
/// (<c>pango_fc_font_get_glyph_extents</c>, the <c>PANGO_GLYPH_UNKNOWN_FLAG</c> branch).
/// The port used to hand the code point to <c>.notdef</c>, whose charstring in the URW
/// and TeX Gyre faces draws nothing at all, so the character measured ZERO HIGH. On the
/// Mutopia copyright footer — three U+01C0 dental clicks no bundled font covers — that
/// was 3.89 staff-spaces of height the page breaker got to spend on another system.
/// </para>
/// <para>
/// EVERY EXPECTED NUMBER BELOW IS READ OFF THE PINNED ORACLE, through
/// <c>ly:stencil-extent</c> on <c>interpret-markup</c> of the same markup, with the
/// oracle's fonts pinned to the faces this repository vendors. None of them is recorded
/// from the port's own output.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class UnknownGlyphBoxTests
{
    /// <summary>U+01C0 LATIN LETTER DENTAL CLICK — the Mutopia footer's character.</summary>
    private const string DentalClick = "ǀ";

    /// <summary>U+2F92 KANGXI RADICAL SEE — a second uncovered character, from another
    /// script, used as the control for "the box belongs to the font".</summary>
    private const string KangxiRadicalSee = "⾒";

    /// <summary>The layout's <c>output-scale</c> at the default staff height.</summary>
    private const double OutputScale = 1.7572990175729903;

    /// <summary>The point constant LilyPond converts a printer's point with.</summary>
    private const double PointInMillimetres = 25.4 / 72.27;

    private static TextFontMetric SerifAt(double points)
        => new TextFontMetric(
            "serif",
            false,
            false,
            false,
            FontInterface.QuantizeToPangoUnits(points * PointInMillimetres),
            OutputScale);

    [Fact]
    public void an_uncovered_character_measures_pangos_unknown_glyph_box()
    {
        //Arrange
        // \abs-fontsize #12 \char ##x01C0, measured on the pinned oracle:
        //   X = [0.00000, 1.12673]   Y = [-0.59711, 1.73474]
        TextFontMetric font = SerifAt(12.0);

        //Act
        Stencil stencil = font.TextStencil(DentalClick);

        //Assert
        stencil.Extent(Axis.X).Left.Should().BeApproximately(0.0, 1e-5);
        stencil.Extent(Axis.X).Right.Should().BeApproximately(1.126729, 1e-5);
        stencil.Extent(Axis.Y).Left.Should().BeApproximately(-0.597108, 1e-5);
        stencil.Extent(Axis.Y).Right.Should().BeApproximately(1.734740, 1e-5);
    }

    [Fact]
    public void the_width_is_the_faces_approximate_char_width_rounded_to_a_device_dot()
    {
        //Arrange
        // The same number as above, but derived rather than quoted: Pango's logical
        // rectangle for an unknown glyph is approximate_char_width exactly, and
        // pango_shape rounds every glyph's advance to a whole device dot before anything
        // reads the run.
        TextFontMetric font = SerifAt(12.0);
        TextFace face = font.Chain[0];

        //Act
        Stencil stencil = font.TextStencil(DentalClick);
        long rounded = TextFontMetric.PangoUnitsRound(face.ApproximateCharWidth(font.XScale));
        double expected = rounded * font.DevicePixel / 1024.0;

        //Assert
        stencil.Extent(Axis.X).Right.Should().BeApproximately(expected, 1e-12);

        // The control: the ROUNDING is load-bearing, not decorative. The unrounded width
        // is a different number, and the oracle's box is a whole number of dots at every
        // one of the 27 sizes from 6 pt to 600 pt this was measured over.
        double unrounded = face.ApproximateCharWidth(font.XScale) * font.DevicePixel / 1024.0;
        unrounded.Should().NotBe(expected);
    }

    [Fact]
    public void the_box_belongs_to_the_font_and_not_to_the_character()
    {
        //Arrange
        // MEASURED: at \abs-fontsize #12 the oracle gives U+2F92 the SAME box it gives
        // U+01C0, X [0, 1.12673] and Y [-0.59711, 1.73474]. A per-character rule --
        // .notdef's own advance, say -- cannot produce that.
        TextFontMetric font = SerifAt(12.0);

        //Act
        Stencil click = font.TextStencil(DentalClick);
        Stencil radical = font.TextStencil(KangxiRadicalSee);

        //Assert
        radical.Extent(Axis.X).Right.Should().BeApproximately(click.Extent(Axis.X).Right, 1e-12);
        radical.Extent(Axis.Y).Left.Should().BeApproximately(click.Extent(Axis.Y).Left, 1e-12);
        radical.Extent(Axis.Y).Right.Should().BeApproximately(click.Extent(Axis.Y).Right, 1e-12);
    }

    [Fact]
    public void a_covered_character_measures_exactly_what_it_always_did()
    {
        //Arrange
        // THE CONTROL THAT MATTERS MOST. \abs-fontsize #12 "M" on the pinned oracle:
        //   X = [0.00000, 2.25346]   Y = [0.00000, 1.73291]
        // A change that gave every glyph the unknown box would satisfy every assertion
        // above and would re-measure every word in the corpus.
        TextFontMetric font = SerifAt(12.0);

        //Act
        Stencil stencil = font.TextStencil("M");

        //Assert
        stencil.Extent(Axis.X).Left.Should().BeApproximately(0.0, 1e-5);
        stencil.Extent(Axis.X).Right.Should().BeApproximately(2.253458, 1e-5);
        stencil.Extent(Axis.Y).Left.Should().BeApproximately(0.0, 1e-5);
        stencil.Extent(Axis.Y).Right.Should().BeApproximately(1.732910, 1e-5);
    }

    [Fact]
    public void a_run_with_nothing_drawable_in_it_emits_no_text_at_all()
    {
        //Arrange
        // MEASURED on the pinned oracle's own SVG: `\markup \abs-fontsize #12 "ǀ"' and
        // `\markup \abs-fontsize #12 "ǀ⾒"' produce NO <text> element, while every
        // markup beside them does. Upstream's Pango_font::get_glyph_desc answers false
        // for a glyph carrying PANGO_GLYPH_UNKNOWN_FLAG and the run ends up with nothing
        // to draw -- so the stencil keeps its extents and carries an empty expression,
        // which is what ly:stencil-empty? then answers on and what scm/page.scm drops a
        // footer for.
        TextFontMetric font = SerifAt(12.0);

        //Act
        Stencil onlyUnknown = font.TextStencil(DentalClick);
        Stencil twoUnknown = font.TextStencil(DentalClick + KangxiRadicalSee);

        //Assert
        onlyUnknown.Expression.Should().Be(Nil.Instance);
        twoUnknown.Expression.Should().Be(Nil.Instance);
        onlyUnknown.IsEmpty.Should().BeTrue("an empty expression is what ly:stencil-empty? reads");

        // ... and the box survives it, which is the whole point: the footer is dropped
        // by page.scm, but a markup that carries a drawable character beside this one
        // still reserves the height. (The oracle's own extents, quoted above.)
        onlyUnknown.ExtentBox[Axis.Y].Right.Should().BeApproximately(1.734740, 1e-5);
    }

    [Fact]
    public void a_drawable_neighbour_keeps_the_whole_string_including_the_uncovered_character()
    {
        //Arrange
        // THE OTHER HALF, AND IT IS NOT SYMMETRICAL. The oracle writes
        // `<tspan>AǀB</tspan>' -- the WHOLE string, uncovered character included -- for
        // `\markup \abs-fontsize #12 "AǀB"', because the utf-8-string encapsulation
        // carries the ORIGINAL TEXT and upstream drops the GLYPH, not the character.
        // Only a run with nothing drawable in it disappears. MEASURED, both cases, in
        // one oracle run.
        TextFontMetric font = SerifAt(12.0);

        //Act
        Stencil mixed = font.TextStencil("A" + DentalClick + "B");

        //Assert
        mixed.Expression.Should().BeOfType<Pair>();
        Pair expression = (Pair)mixed.Expression;
        ((Symbol)expression.Car).Name.Should().Be("utf-8-string");
        Pair.ToList(expression.Cdr)[1].ToString().Should().Be("A" + DentalClick + "B");

        // The extents are the oracle's: X [0, 4.60935], Y bottom -0.59711 -- the unknown
        // box still sets the bottom, because "A" has no descender.
        mixed.Extent(Axis.X).Right.Should().BeApproximately(4.609353, 1e-4);
        mixed.Extent(Axis.Y).Left.Should().BeApproximately(-0.597108, 1e-5);
    }

    [Fact]
    public void the_missing_glyph_warning_still_fires_once_for_the_character()
    {
        //Arrange
        // D40's sentence must survive the fix: the diagnostics gate compares it against
        // the oracle's, word for word and count for count.
        TextFontMetric font = SerifAt(12.0);
        TextWriter savedOutput = Warn.Output;
        Warn.Output = TextWriter.Null;
        Warn.RecordMessages = true;
        Warn.ClearMessages();

        try
        {
            //Act
            font.TextStencil(DentalClick);

            //Assert
            Warn.Messages
                .Count(m => m.Contains(
                    "no glyph for character 'ǀ' (U+01C0 LATIN LETTER DENTAL CLICK)"))
                .Should().Be(1);
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
            Warn.Output = savedOutput;
        }
    }

    [Fact]
    public void the_ascent_and_descent_are_hhea_and_not_the_os2_typographic_pair()
    {
        //Arrange
        // The two disagree in TeX Gyre Schola, which is what makes this a test and not a
        // restatement: hhea says (1135, -332) and OS/2's typographic pair says
        // (798, -202). The oracle's box for
        // `\override #'(font-name . "TeX Gyre Schola") \abs-fontsize #12 \char ##x01C0'
        // is Y [-0.76269, 2.69002] -- 2.69 units up, which only 1135 can reach.
        TextFace schola = TextFace.Load("texgyreschola-regular.otf");

        //Act
        (int typoAscender, int typoDescender) = schola.Reader.ReadTypoAscenderDescender();

        //Assert
        schola.Ascender.Should().Be(1135);
        schola.Descender.Should().Be(-332);
        typoAscender.Should().Be(798);
        typoDescender.Should().Be(-202);
    }
}
