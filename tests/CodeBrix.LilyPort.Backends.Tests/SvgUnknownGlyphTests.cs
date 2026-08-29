// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Backends.Tests;

/// <summary>
/// What the SVG backend writes for a character no face in the chain covers.
/// <para>
/// The rule is MEASURED, on the pinned oracle's own SVG, and it is not the obvious one:
/// a run with NOTHING drawable in it produces no <c>&lt;text&gt;</c> element at all,
/// while a run that has one drawable glyph beside the uncovered character writes the
/// WHOLE STRING, uncovered character included. Upstream's <c>utf-8-string</c>
/// encapsulation carries the original text and what it drops is the GLYPH.
/// </para>
/// <para>
/// The two markups and what 2.27.2 wrote for them, in one run:
/// <c>\markup \abs-fontsize #12 "ǀ"</c> → nothing;
/// <c>\markup \abs-fontsize #12 "AǀB"</c> → <c>&lt;tspan&gt;AǀB&lt;/tspan&gt;</c>.
/// </para>
/// </summary>
public class SvgUnknownGlyphTests
{
    /// <summary>U+01C0 LATIN LETTER DENTAL CLICK, which no bundled face covers.</summary>
    private const string DentalClick = "ǀ";

    private const double OutputScale = 1.7572990175729903;

    private static TextFontMetric SerifAtTwelvePoints()
        => new TextFontMetric(
            "serif",
            false,
            false,
            false,
            FontInterface.QuantizeToPangoUnits(12.0 * (25.4 / 72.27)),
            OutputScale);

    private static string Render(Stencil stencil)
    {
        SvgBackend backend = new SvgBackend();
        backend.Output(stencil.Expression);
        return backend.Body;
    }

    [Fact]
    public void an_uncovered_character_alone_writes_no_text_element()
    {
        //Arrange
        TextFontMetric font = SerifAtTwelvePoints();

        //Act
        string body = Render(font.TextStencil(DentalClick));

        //Assert
        body.Should().NotContain("<text");
        body.Should().NotContain(DentalClick);
    }

    [Fact]
    public void a_covered_character_is_still_written_which_is_the_control()
    {
        //Arrange
        // Without this the rule above could be "text fonts draw nothing", which would
        // take every word off every page in the corpus.
        TextFontMetric font = SerifAtTwelvePoints();

        //Act
        string body = Render(font.TextStencil("A"));

        //Assert
        body.Should().Contain("<text");
        body.Should().Contain("<tspan>A</tspan>");
    }

    [Fact]
    public void a_drawable_neighbour_keeps_the_uncovered_character_in_the_tspan()
    {
        //Arrange
        // Upstream's measured asymmetry. The port must not "clean" the string.
        TextFontMetric font = SerifAtTwelvePoints();

        //Act
        string body = Render(font.TextStencil("A" + DentalClick + "B"));

        //Assert
        body.Should().Contain("<tspan>A" + DentalClick + "B</tspan>");
    }
}
