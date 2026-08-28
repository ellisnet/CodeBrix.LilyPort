// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Importers;
using Lily.Shell.Commands;
using SilverAssertions;
using Xunit;

namespace Lily.Shell.Core.Tests;

/// <summary>
/// What the shell's two text-converter commands accept — gated at the command LINE, the
/// way <see cref="DocsCommandLineTests"/> gates <c>docs</c>, because a path through the
/// command itself reads and writes files.
/// </summary>
public class TextConverterCommandLineTests
{
    [Fact]
    public void convert_ly_with_no_arguments_asks_which_file()
    {
        //Act
        var parsed = ConvertLyCommandLine.Parse([]);

        //Assert
        parsed.Error.Should().Be("which file?");
    }

    [Fact]
    public void convert_ly_takes_a_file_and_defaults_both_versions()
    {
        //Act
        var parsed = ConvertLyCommandLine.Parse(["old.ly"]);

        //Assert
        //Both null: the document's own \version, and the newest any rule targets.
        parsed.Error.Should().BeNull();
        parsed.InputPath.Should().EndWith("old.ly");
        parsed.From.Should().BeNull();
        parsed.To.Should().BeNull();
        parsed.OutputPath.Should().BeNull();
    }

    [Fact]
    public void convert_ly_reads_the_version_range()
    {
        //Act
        var parsed = ConvertLyCommandLine.Parse(
            ["old.ly", "--from", "2.12.0", "--to", "2.18.2"]);

        //Assert
        parsed.Error.Should().BeNull();
        parsed.From.Value.ToString().Should().Be("2.12.0");
        parsed.To.Value.ToString().Should().Be("2.18.2");
    }

    [Fact]
    public void convert_ly_refuses_a_version_that_is_not_one()
    {
        //Act
        var parsed = ConvertLyCommandLine.Parse(["old.ly", "--from", "banana"]);

        //Assert
        //The control is the same line with a real version, which parses.
        parsed.Error.Should().Be("'banana' is not a version");
        ConvertLyCommandLine.Parse(["old.ly", "--from", "2.12.0"]).Error.Should().BeNull();
    }

    [Fact]
    public void convert_ly_writes_in_place_only_when_told_where()
    {
        //Act
        var printed = ConvertLyCommandLine.Parse(["old.ly"]);
        var written = ConvertLyCommandLine.Parse(["old.ly", "-o", "new.ly"]);

        //Assert
        //⚠ There is deliberately no way to say "over the top of the input": upstream's
        //script rewrites in place and this one never does.
        printed.OutputPath.Should().BeNull();
        written.OutputPath.Should().EndWith("new.ly");
    }

    [Fact]
    public void convert_ly_refuses_a_second_file()
    {
        //Act
        var parsed = ConvertLyCommandLine.Parse(["one.ly", "two.ly"]);

        //Assert
        parsed.Error.Should().Contain("one file at a time");
    }

    [Fact]
    public void import_with_no_arguments_lists_the_formats()
    {
        //Act
        var parsed = ImportCommandLine.Parse([]);

        //Assert
        parsed.ListOnly.Should().BeTrue();
        parsed.Error.Should().BeNull();
    }

    [Fact]
    public void import_reads_the_abc_options_by_their_upstream_names()
    {
        //Act
        var parsed = ImportCommandLine.Parse(
            ["abc", "tune.abc", "--beams", "--strict"]);

        //Assert
        parsed.Error.Should().BeNull();
        parsed.Format.Should().Be(ImportFormat.Abc);
        parsed.AbcOptions.Beams.Should().BeTrue();
        parsed.AbcOptions.Strict.Should().BeTrue();
        parsed.MidiOptions.Should().BeNull();
    }

    [Fact]
    public void import_reads_the_midi_options_by_their_upstream_names()
    {
        //Act
        var parsed = ImportCommandLine.Parse(
        [
            "midi", "song.midi", "--absolute-pitches", "--explicit-durations",
            "--skip", "--text-lyrics", "--preview", "--key", "-2:1",
            "--duration-quant", "32", "--start-quant", "16",
            "--allow-tuplet", "4*2/3", "--allow-tuplet", "2*4/3",
        ]);

        //Assert
        parsed.Error.Should().BeNull();
        parsed.Format.Should().Be(ImportFormat.Midi);
        parsed.MidiOptions.AbsolutePitches.Should().BeTrue();
        parsed.MidiOptions.ExplicitDurations.Should().BeTrue();
        parsed.MidiOptions.Skip.Should().BeTrue();
        parsed.MidiOptions.TextLyrics.Should().BeTrue();
        parsed.MidiOptions.Preview.Should().BeTrue();
        parsed.MidiOptions.Key.Should().Be("-2:1");
        parsed.MidiOptions.DurationQuant.Should().Be(32);
        parsed.MidiOptions.StartQuant.Should().Be(16);
        parsed.MidiOptions.AllowTuplet.Should().HaveCount(2);
        parsed.AbcOptions.Should().BeNull();
    }

    [Fact]
    public void import_keeps_the_two_option_sets_apart()
    {
        //Act
        //--beams is abc's and --skip is midi's; neither belongs to the other.
        var abcWithMidiOption = ImportCommandLine.Parse(["abc", "t.abc", "--skip"]);
        var midiWithAbcOption = ImportCommandLine.Parse(["midi", "t.midi", "--beams"]);

        //Assert
        abcWithMidiOption.Error.Should().Be("unknown option '--skip' for abc");
        midiWithAbcOption.Error.Should().Be("unknown option '--beams' for midi");
    }

    [Fact]
    public void import_reads_musicxml_and_names_an_unknown_format()
    {
        //Act
        var parsed = ImportCommandLine.Parse(["musicxml", "score.xml"]);

        //Assert
        parsed.Error.Should().BeNull();
        parsed.Format.Should().Be(ImportFormat.MusicXml);
        parsed.MusicXmlOptions.Should().NotBeNull();
        parsed.MusicXmlOptions.SourceName.Should().Be("score.xml");
        ImportCommandLine.Parse(["banana", "x"]).Error.Should()
            .Be("unknown format 'banana' (abc, midi or musicxml)");
    }

    [Fact]
    public void import_musicxml_reads_the_long_option_of_every_switch()
    {
        //Act
        //One of each shape: a flag, an enum, a string, a float and an integer.
        var parsed = ImportCommandLine.Parse(
        [
            "musicxml", "score.xml",
            "--absolute",
            "--no-page-layout",
            "--fretboards",
            "--language", "deutsch",
            "--ottavas-end-early", "t",
            "--tab-clef", "moderntab",
            "--string-numbers", "f",
            "--transpose", "d",
            "--dynamics-scale", "2.5",
            "--credit-page", "2",
            "--shift-durations", "-1",
        ]);

        //Assert
        parsed.Error.Should().BeNull();
        parsed.MusicXmlOptions.PitchMode.Should().Be(MusicXmlPitchMode.Absolute);
        parsed.MusicXmlOptions.NoPageLayout.Should().BeTrue();
        parsed.MusicXmlOptions.Fretboards.Should().BeTrue();
        parsed.MusicXmlOptions.Language.Should().Be("deutsch");
        parsed.MusicXmlOptions.OttavasEndEarly.Should().Be("t");
        parsed.MusicXmlOptions.TabClef.Should().Be("moderntab");
        parsed.MusicXmlOptions.StringNumbers.Should().Be("f");
        parsed.MusicXmlOptions.Transpose.Should().Be("d");
        parsed.MusicXmlOptions.DynamicsScale.Should().Be(2.5);
        parsed.MusicXmlOptions.CreditPage.Should().Be(2);
        parsed.MusicXmlOptions.ShiftDurations.Should().Be(-1);

        //A control: the defaults are what upstream's are, so a switch that was not given
        //cannot be read as given.
        var plain = ImportCommandLine.Parse(["musicxml", "score.xml"]);
        plain.MusicXmlOptions.PitchMode.Should().Be(MusicXmlPitchMode.Relative);
        plain.MusicXmlOptions.NoPageLayout.Should().BeFalse();
        plain.MusicXmlOptions.CreditPage.Should().Be(1);
        plain.MusicXmlOptions.DynamicsScale.Should().BeNull();
    }

    [Fact]
    public void import_musicxml_refuses_another_formats_option()
    {
        //Act
        //--beams is abc's and --skip is midi's; neither belongs to musicxml.
        var withAbcOption = ImportCommandLine.Parse(["musicxml", "s.xml", "--beams"]);
        var withMidiOption = ImportCommandLine.Parse(["musicxml", "s.xml", "--skip"]);

        //Assert
        withAbcOption.Error.Should().Be("unknown option '--beams' for musicxml");
        withMidiOption.Error.Should().Be("unknown option '--skip' for musicxml");
    }

    [Fact]
    public void import_needs_a_file()
    {
        //Act
        var parsed = ImportCommandLine.Parse(["abc", "--beams"]);

        //Assert
        parsed.Error.Should().Be("which file?");
    }

    [Fact]
    public void import_option_that_needs_a_value_says_so()
    {
        //Act
        var parsed = ImportCommandLine.Parse(["midi", "t.midi", "--key"]);

        //Assert
        parsed.Error.Should().Be("--key needs a value");
    }

    [Fact]
    public void import_hands_the_converter_the_name_the_user_typed()
    {
        //Act
        var abc = ImportCommandLine.Parse(["abc", "tunes/reel.abc"]);
        var midi = ImportCommandLine.Parse(["midi", "songs/waltz.midi"]);

        //Assert
        //⚠ The NAME, not the resolved path: abc2ly prints it in its one
        //location-bearing message and midi2ly writes it into the document's first
        //line, so a resolved path would put this machine's layout in the user's score.
        abc.AbcOptions.SourceName.Should().Be("tunes/reel.abc");
        abc.InputPath.Should().NotBe("tunes/reel.abc");
        midi.MidiOptions.SourceName.Should().Be("songs/waltz.midi");
        midi.InputPath.Should().NotBe("songs/waltz.midi");
    }

    [Fact]
    public void import_refuses_a_quantisation_that_is_not_a_number()
    {
        //Act
        var parsed = ImportCommandLine.Parse(
            ["midi", "t.midi", "--duration-quant", "half"]);

        //Assert
        parsed.Error.Should().Be("'half' is not a number");
        ImportCommandLine.Parse(["midi", "t.midi", "--duration-quant", "32"])
            .Error.Should().BeNull();
    }
}
