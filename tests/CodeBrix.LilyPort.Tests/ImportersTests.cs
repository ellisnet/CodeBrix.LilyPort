// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Importers;
using SilverAssertions;
using System;
using System.Text;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// The importers' own surface: what the public entry points answer, and the handful of
/// mechanisms the parity corpus cannot reach because upstream's own corpus never
/// contains them.
/// </summary>
/// <remarks>
/// The corpus in <see cref="AbcImporterParityTests"/> and
/// <see cref="MidiImporterParityTests"/> is the real fence; this file covers the shape
/// of the surface and the failure paths. Every expected value here is either read off
/// upstream's own expression or recorded from python's own run — never from this port —
/// and each is paired with a control that must come out differently.
/// </remarks>
public class ImportersTests
{
    [Fact]
    public void abc_import_with_no_options_reads_a_tune()
    {
        //Arrange
        string abc = "X: 1\nT: Control\nL: 1/8\nK: C\nCDEF |\n";

        //Act
        ImportResult result = AbcImporter.Import(abc);

        //Assert
        result.Succeeded.Should().BeTrue();
        result.Errors.Should().Be(0);
        //D63: the \version an importer emits is the release upstream FROZE -- the last
        //one whose output syntax was verified -- not the release being ported. The
        //tagline below is the other case and does read the ported release.
        result.Text.Should().StartWith("\\version \"2.24.0\"\n");
        result.Text.Should().NotStartWith(
            "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n");
        result.Text.Should().Contain(
            "LilyPond " + LilyPortInfo.CompatibleWithVersion + " was here");
        result.Text.Should().Contain("title = \"Control\"");
    }

    [Fact]
    public void abc_import_of_nothing_still_writes_a_document()
    {
        //Arrange + Act
        //Upstream runs its dumps whatever the input was, so an empty file produces the
        //skeleton and not a failure. The control below is the tune that DOES have
        //notes, which must differ.
        ImportResult empty = AbcImporter.Import(string.Empty);
        ImportResult tune = AbcImporter.Import("X: 1\nK: C\nC |\n");

        //Assert
        empty.Succeeded.Should().BeTrue();
        empty.Text.Should().Contain("\\score {");
        empty.Text.Should().NotBe(tune.Text);
    }

    [Fact]
    public void abc_strict_mode_gives_up_where_the_default_carries_on()
    {
        //Arrange
        //A line abc2ly cannot finish nibbling -- verified against the pinned oracle,
        //which reports and keeps going (exit 0, file written) without --strict, and
        //exits 1 writing nothing with it. ⚠ Under --strict the follow-up lines that
        //echo the offending text are never reached, because sys.exit happens inside
        //error() before them; the message count below is that difference.
        string abc = "X: 1\nK: C\nC @ |\n";

        //Act
        ImportResult lenient = AbcImporter.Import(
            abc, new AbcImportOptions { SourceName = "t.abc" });
        ImportResult strict = AbcImporter.Import(
            abc, new AbcImportOptions { Strict = true, SourceName = "t.abc" });

        //Assert
        lenient.Text.Should().NotBeNull();
        lenient.Errors.Should().Be(1);
        lenient.Succeeded.Should().BeFalse();
        //Four, not three: the last line upstream writes carries the offending text
        //AND its own newline, so the stream ends with a blank line and the blank is a
        //message. The recorded corpus shows the same shape.
        lenient.Messages.Should().HaveCount(4);
        strict.Text.Should().BeNull();
        strict.Errors.Should().Be(1);
        strict.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void abc_beams_option_changes_the_output()
    {
        //Arrange
        string abc = "X: 1\nT: Beams\nM: 4/4\nL: 1/8\nK: C\nCDEF GABc |\n";

        //Act
        ImportResult plain = AbcImporter.Import(abc);
        ImportResult beamed = AbcImporter.Import(
            abc, new AbcImportOptions { Beams = true });

        //Assert
        //--beams both brackets the notes and installs \autoBeamOff in the layout.
        beamed.Text.Should().NotBe(plain.Text);
        beamed.Text.Should().Contain("\\autoBeamOff");
        plain.Text.Should().NotContain("\\autoBeamOff");
    }

    [Fact]
    public void abc_source_name_is_what_the_location_message_carries()
    {
        //Arrange
        string abc = "X: 1\nK: C\nC @ |\n";

        //Act
        ImportResult named = AbcImporter.Import(
            abc, new AbcImportOptions { SourceName = "chosen.abc" });
        ImportResult unnamed = AbcImporter.Import(abc);

        //Assert
        named.Messages.Should().Contain(
            m => m.StartsWith("chosen.abc: 3: Huh?", StringComparison.Ordinal));
        unnamed.Messages.Should().Contain(
            m => m.StartsWith(": 3: Huh?", StringComparison.Ordinal));
    }

    [Fact]
    public void abc_backslash_k_converts_where_upstream_dies()
    {
        //Arrange
        //⚠ A DECLARED DIVERGENCE (abc-backslash-k-crash). abc2ly's try_parse_escape
        //calls compute_key() with NO argument -- a TypeError that takes the script down
        //and leaves no output file at all, MEASURED against the pinned 2.27.2. The
        //result it discards means even the intended call did nothing, so the port
        //consumes the escape and carries on. The control is the same tune without it,
        //which must come out identical apart from the escape.
        string withEscape = "X: 1\nK: C\nC \\K D |\n";
        string control = "X: 1\nK: C\nC D |\n";

        //Act
        ImportResult converted = AbcImporter.Import(withEscape);
        ImportResult fine = AbcImporter.Import(control);

        //Assert
        converted.Succeeded.Should().BeTrue();
        converted.Errors.Should().Be(0);
        converted.Text.Should().Be(fine.Text);
    }

    [Fact]
    public void midi_import_of_something_that_is_not_midi_fails_the_way_upstream_does()
    {
        //Arrange
        byte[] notMidi = Encoding.ASCII.GetBytes("this is not a MIDI file at all");

        //Act
        ImportResult result = MidiImporter.Import(notMidi);

        //Assert
        //python/midi.py's own wording: expected b'MThd', got <what was there>.
        result.Text.Should().BeNull();
        result.Errors.Should().Be(1);
        result.Succeeded.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("expected b'MThd'"));
    }

    [Fact]
    public void midi_import_of_a_truncated_file_says_so()
    {
        //Arrange
        //A header that claims one track, with nothing after it.
        byte[] truncated =
        {
            (byte)'M', (byte)'T', (byte)'h', (byte)'d',
            0, 0, 0, 6,
            0, 0, 0, 1, 0, 96,
        };

        //Act
        ImportResult result = MidiImporter.Import(truncated);

        //Assert
        result.Text.Should().BeNull();
        result.Messages.Should().Contain(m => m.Contains("expected b'MTrk'"));
    }

    [Fact]
    public void midi_import_of_an_empty_file_is_not_a_crash()
    {
        //Arrange + Act
        ImportResult empty = MidiImporter.Import(Array.Empty<byte>());
        ImportResult none = MidiImporter.Import(null);

        //Assert
        empty.Text.Should().BeNull();
        empty.Errors.Should().Be(1);
        none.Text.Should().BeNull();
        none.Errors.Should().Be(1);
    }

    [Fact]
    public void midi_allow_tuplet_that_is_not_a_triple_is_upstreams_unpacking_error()
    {
        //Arrange
        //Upstream unpacks each --allow-tuplet into three names; "4*2" gives two.
        MidiImportOptions broken = new MidiImportOptions();
        broken.AllowTuplet.Add("4*2");
        MidiImportOptions control = new MidiImportOptions();
        control.AllowTuplet.Add("4*2/3");

        //Act
        ImportResult failed = MidiImporter.Import(MinimalMidi(), broken);
        ImportResult fine = MidiImporter.Import(MinimalMidi(), control);

        //Assert
        failed.Text.Should().BeNull();
        failed.Messages.Should().Contain(m => m.Contains("not enough values to unpack"));
        fine.Text.Should().NotBeNull();
    }

    [Fact]
    public void midi_source_name_is_what_the_tag_line_carries()
    {
        //Arrange + Act
        ImportResult named = MidiImporter.Import(
            MinimalMidi(), new MidiImportOptions { SourceName = "chosen.midi" });
        ImportResult unnamed = MidiImporter.Import(MinimalMidi());

        //Assert
        named.Text.Should().StartWith(
            "% Lily was here -- automatically converted by midi2ly from chosen.midi\n");
        unnamed.Text.Should().StartWith(
            "% Lily was here -- automatically converted by midi2ly from \n");
    }

    [Theory]
    //Recorded from python's own `'%g' % value', which is what midi2ly formats a
    //fractional tempo's comment with. .NET's own "G" format answers differently for
    //four of these, which is why the port carries its own.
    [InlineData(120.0, "120")]
    [InlineData(3.0, "3")]
    [InlineData(0.0, "0")]
    [InlineData(0.5, "0.5")]
    [InlineData(2.5, "2.5")]
    [InlineData(133.33333333333334, "133.333")]
    [InlineData(96.66666666666667, "96.6667")]
    [InlineData(63.999999, "64")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(1e-05, "1e-05")]
    [InlineData(1.2345678e-05, "1.23457e-05")]
    [InlineData(100000.0, "100000")]
    [InlineData(1000000.0, "1e+06")]
    [InlineData(1234567.0, "1.23457e+06")]
    public void the_g_format_answers_what_python_answers(double value, string expected)
    {
        //Arrange + Act
        string formatted = MidiFraction.FormatG(value);

        //Assert
        formatted.Should().Be(expected);
    }

    [Fact]
    public void a_fraction_knows_whether_its_rounded_decimal_is_exact()
    {
        //Arrange
        //Recorded from python: Fraction(400,3) != Decimal('133.333'), while
        //Fraction(1205,10) == Decimal('120.5'). midi2ly writes the approximation mark
        //on exactly this difference.
        //Act + Assert
        IsExactlyItsRoundedDecimal(400, 3).Should().BeFalse();
        IsExactlyItsRoundedDecimal(1205, 10).Should().BeTrue();
        IsExactlyItsRoundedDecimal(240, 1).Should().BeTrue();
        IsExactlyItsRoundedDecimal(500, 4).Should().BeTrue();
    }

    /// <summary>
    /// midi2ly's own question: is the six-digit decimal it would print the same number
    /// as the fraction it printed it from?
    /// </summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator.</param>
    /// <returns>Whether the two are the same number.</returns>
    private static bool IsExactlyItsRoundedDecimal(long numerator, long denominator)
    {
        MidiFraction f = new MidiFraction(numerator, denominator);
        return f.EqualsDecimalText(MidiFraction.FormatG(f.ToDouble()));
    }

    /// <summary>The smallest MIDI file the reader accepts: a header and one empty track.</summary>
    /// <returns>The bytes.</returns>
    private static byte[] MinimalMidi()
        => new byte[]
        {
            (byte)'M', (byte)'T', (byte)'h', (byte)'d',
            0, 0, 0, 6,
            0, 0, 0, 1, 0, 96,
            (byte)'M', (byte)'T', (byte)'r', (byte)'k',
            0, 0, 0, 0,
        };
}
