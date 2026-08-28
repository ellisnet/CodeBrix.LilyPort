// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Fences on <see cref="AllFontMetrics.FindOtfFont"/>'s not-found behaviour.
/// <para>
/// The behaviour these pin was a DIVERGENCE until 2026-08-26: the method warned and
/// answered <see langword="null"/> where upstream's <c>find_otf_font</c>
/// (<c>lily/all-font-metrics.cc:163-168</c>) has always gone straight to <c>error</c>.
/// Nothing recovered the null — <c>GetDefaultFont</c> propagated it and its call sites
/// dereference immediately — so a document naming a music font the port does not have
/// died with <c>wrong-type-arg ("ly:font-get-glyph" ...)</c>, an internal error naming a
/// procedure the user never wrote.
/// </para>
/// <para>
/// Reported from Fresco.Brix, whose document-font UI lets a user select a music font and
/// then remove it, which is what makes the state reachable by ordinary use.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class AllFontMetricsTests
{
    private const string MissingFontName = "no-such-font-42";
    private const string VendoredFontName = "emmentaler-20";

    [Fact]
    public void a_font_the_port_does_not_have_is_a_fatal_error()
    {
        //Arrange
        //Act
        LilyPondErrorException thrown = Assert.Throws<LilyPondErrorException>(
            () => AllFontMetrics.FindOtfFont(MissingFontName));

        //Assert
        // Upstream errors rather than answering; the whole point of the fix is that the
        // run stops HERE, where the font can still be named, rather than several frames
        // later inside a Scheme primitive.
        thrown.Should().NotBeNull();
    }

    [Fact]
    public void the_error_names_the_font_it_could_not_find()
    {
        //Arrange
        //Act
        LilyPondErrorException thrown = Assert.Throws<LilyPondErrorException>(
            () => AllFontMetrics.FindOtfFont(MissingFontName));

        //Assert
        // `cannot find font '%s' (search path: %s)' is upstream's own wording at
        // all-font-metrics.cc:166. The NAME is the half a user acts on.
        thrown.Message.Should().Contain(MissingFontName);
        thrown.Message.Should().Contain("cannot find font");
    }

    [Fact]
    public void the_error_names_where_it_looked()
    {
        //Arrange
        //Act
        LilyPondErrorException thrown = Assert.Throws<LilyPondErrorException>(
            () => AllFontMetrics.FindOtfFont(MissingFontName));

        //Assert
        // Ruling R18: the port reports its OWN font world rather than a FontConfig path
        // it does not have. With no override directories set, that world is the embedded
        // copies — and saying so is what tells a user their --fonts directory was not
        // picked up.
        thrown.Message.Should().Contain("search path:");
        thrown.Message.Should().Contain("CodeBrix.LilyPort.Engine");
    }

    [Fact]
    public void a_vendored_font_still_loads_and_is_cached()
    {
        //Arrange
        //Act
        OpenTypeFontMetric first = AllFontMetrics.FindOtfFont(VendoredFontName);
        OpenTypeFontMetric second = AllFontMetrics.FindOtfFont(VendoredFontName);

        //Assert
        // The control. Making the missing case fatal must not disturb the present case,
        // and the cache's identity guarantee is what the layout's scaled-font table and
        // the backend's named-glyph expressions are keyed on.
        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
    }
}
