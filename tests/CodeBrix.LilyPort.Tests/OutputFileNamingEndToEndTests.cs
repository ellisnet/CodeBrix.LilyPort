// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 12's fence for D37: what the output FILES are called.
/// <para>
/// Two rules, both read off upstream rather than off the port.
/// <c>scm/framework-svg.scm</c>'s <c>output-stencils</c> seeds its counter at
/// <c>(1- first-page-number)</c> and bumps it before each page, so a page's file suffix
/// is its PAGE NUMBER and a book that starts on page 3 has no <c>-1</c> or <c>-2</c> at
/// all. <c>scm/lily-library.scm</c>'s <c>get-outfile-name</c> gives a book the base name,
/// then <c>-&lt;output-suffix&gt;</c> when one is set, then <c>-&lt;n&gt;</c> for the n-th
/// book already printed under the SAME key — where the key is the base name and the
/// suffix TOGETHER.
/// </para>
/// <para>
/// The port numbered pages from one regardless, and carried a comment asserting that was
/// the oracle's rule (trap 26). Every file in the <c>page-turn-page-breaking</c> family
/// sets <c>auto-first-page-number</c>, which starts those books on page 2 to avoid a bad
/// turn — so every page of every one of them was named one too low, each family member's
/// LAST page read MISSING, and the pages before it were graded against the oracle's NEXT
/// page. It also numbered books by a running index, which named both halves of
/// <c>book-change-global-staffsize-abs-fonts</c> wrongly.
/// </para>
/// <para>
/// Each rule is stated as a PAIR that must come out differently, because a namer that
/// ignored the paper variable entirely would satisfy either half alone.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class OutputFileNamingEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-naming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The written files' base names, in the order the runner wrote them.</summary>
    private static List<string> WrittenNames(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPaths.Should().NotBeNull();
        return result.SvgPaths.Select(Path.GetFileName).ToList();
    }

    /// <summary>The MIDI files written, in the order the runner wrote them.</summary>
    private static List<string> WrittenMidiNames(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.MidiPaths.Should().NotBeNull();
        return result.MidiPaths.Select(Path.GetFileName).ToList();
    }

    /// <summary>A two-page book: the page break is forced, so the count is not a guess.</summary>
    private static string TwoPageBook(string paper)
        => Version
        + "\\book {\n"
        + "  \\paper { " + paper + " }\n"
        + "  \\score { { c'1 \\pageBreak c'1 } }\n"
        + "}\n";

    [Fact]
    public void pages_are_named_from_the_books_first_page_number()
    {
        //Arrange
        // first-page-number 3 over two pages: output-stencils names them 3 and 4. There
        // is deliberately no -1 and no -2 -- that absence IS the rule.
        string source = TwoPageBook("first-page-number = #3");

        //Act
        List<string> names = WrittenNames(source, "naming-from-three");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-from-three-3.svg", "naming-from-three-4.svg" });
    }

    [Fact]
    public void pages_are_named_from_one_when_the_book_does_not_say_otherwise()
    {
        //Arrange
        // The control for the fact above, and the reason the port's defect stayed
        // invisible for so long: with the default first-page-number the two rules agree,
        // so every ordinary file in the corpus was named correctly either way.
        string source = TwoPageBook(string.Empty);

        //Act
        List<string> names = WrittenNames(source, "naming-default");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-default-1.svg", "naming-default-2.svg" });
    }

    [Fact]
    public void output_suffix_names_the_book_and_the_counter_does_not_fire()
    {
        //Arrange
        // Two books printed under DIFFERENT keys, which is book-change's own shape: the
        // first carries a suffix, the second none. get-outfile-name's counter is keyed by
        // base name AND suffix, so neither book collides with the other and NEITHER gets a
        // number. A running book index would have named the second one "-1".
        string source =
            Version
            + "#(define output-suffix \"alpha\")\n"
            + "\\book { \\score { { c'1 } } }\n"
            + "#(define output-suffix #f)\n"
            + "\\book { \\score { { d'1 } } }\n";

        //Act
        List<string> names = WrittenNames(source, "naming-suffix");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-suffix-alpha.svg", "naming-suffix.svg" });
    }

    [Fact]
    public void the_counter_does_fire_when_two_books_share_one_key()
    {
        //Arrange
        // THE CONTROL that makes the pair mean something: with no suffix anywhere both
        // books land on the same key, so the second one DOES take the counter's "-1".
        // Without this half, a namer that had simply dropped the counter would pass.
        string source =
            Version
            + "\\book { \\score { { c'1 } } }\n"
            + "\\book { \\score { { d'1 } } }\n";

        //Act
        List<string> names = WrittenNames(source, "naming-twobooks");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-twobooks.svg", "naming-twobooks-1.svg" });
    }

    [Fact]
    public void midi_files_are_named_from_the_book_that_performed_them()
    {
        //Arrange
        // The Mendelssohn Octet's own shape, reduced: an explicit \book that only PRINTS,
        // then toplevel scores that only PERFORM and so build a SECOND book.
        // get-outfile-name gives that second book "-1", and upstream reaches
        // write-performances-midis once per book with the book's name -- so the two
        // performances are <base>-1.midi and <base>-1-1.midi, carrying the book counter
        // AND the performance counter. Named from the INPUT's base name with one running
        // counter they came out <base>.midi and <base>-1.midi, one book-suffix short, and
        // a comparator pairing by name matched the first movement against the second.
        string source =
            Version
            + "\\book { \\score { { c'1 } \\layout { } } }\n"
            + "\\score { { d'1 } \\midi { } }\n"
            + "\\score { { e'1 } \\midi { } }\n";

        //Act
        List<string> names = WrittenMidiNames(source, "naming-bookmidi");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-bookmidi-1.midi", "naming-bookmidi-1-1.midi" });
    }

    [Fact]
    public void midi_files_of_the_only_book_are_named_from_the_base_name()
    {
        //Arrange
        // THE CONTROL that makes the pair mean something, and the reason the defect above
        // stayed invisible: with a single book the book's name IS the base name, so the
        // two rules agree and every one-book file in the suite was named correctly either
        // way. Without this half, a namer that always appended "-1" would pass.
        string source =
            Version
            + "\\score { { d'1 } \\midi { } }\n"
            + "\\score { { e'1 } \\midi { } }\n";

        //Act
        List<string> names = WrittenMidiNames(source, "naming-onebookmidi");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-onebookmidi.midi", "naming-onebookmidi-1.midi" });
    }

    /// <summary>
    /// A file on disk, so that <see cref="BatchRunner.RunFile"/> — the overload that has
    /// an input file to take a name from — is the one under test.
    /// </summary>
    /// <param name="source">The LilyPond source.</param>
    /// <param name="inputBaseName">What to call the file, without extension.</param>
    /// <returns>The file's full path.</returns>
    private static string WriteInput(string source, string inputBaseName)
    {
        string path = Path.Combine(ScratchDirectory(), inputBaseName + ".ly");
        File.WriteAllText(path, source);
        return path;
    }

    /// <summary>
    /// A document that reports the two names the engine holds, and then makes an error
    /// so that a LOCATION is reported as well.
    /// </summary>
    private const string NameProbe =
        "#(ly:warning \"probe input-file-name is ~a\" (ly:parser-lookup 'input-file-name))\n"
        + "#(ly:warning \"probe output-name is ~a\" (ly:parser-output-name))\n"
        + "{ c'4 \\nosuchidentifier d'4 }\n";

    [Fact]
    public void renaming_the_output_does_not_rename_the_input()
    {
        //Arrange
        //⚠ EVERY EXPECTATION HERE WAS READ OFF THE PINNED 2.27.2 FIRST, not off the port:
        //`lilypond -o <dir>/renamed badname.ly' reports `Processing `…/badname.ly'', an
        //input-file-name of `…/badname.ly', an output-name of `renamed', an error located
        //in `…/badname.ly', and `failed files: "badname.ly"'. Upstream consults
        //output_name_global for what to WRITE and never for what was read.
        //⚠ The port names these BY BASENAME where upstream names them by full path — a
        //separate, older divergence that compare-diagnostics.py normalises away by
        //design (it reduces an absolute path in a message to its basename). This fence is
        //about WHICH FILE is named, not about how much of its path is printed.
        string path = WriteInput(Version + NameProbe, "originalname");
        StringWriter log = new StringWriter();

        //Act
        BatchRunResult renamed = BatchRunner.RunFile(
            path, ScratchDirectory(), "renamed",
            new BatchRunOptions { MessageWriter = log });

        //Assert
        string text = log.ToString();
        text.Should().Contain("Processing `");
        text.Should().Contain("originalname.ly");
        text.Should().Contain("probe input-file-name is originalname.ly");
        text.Should().NotContain("renamed.ly");

        //The CONTROL, and the half that must not have regressed: the OUTPUT name did
        //change, and it is what the file on disk is called.
        text.Should().Contain("probe output-name is renamed");
        renamed.SvgPaths.Select(Path.GetFileName).Should().Equal("renamed.svg");
    }

    [Fact]
    public void a_diagnostics_location_names_the_input_after_a_rename()
    {
        //Arrange
        //The location is the parse SOURCE name, which is the third place the output name
        //used to stand in for the input's — and the one a reader acts on, because a
        //warning that names a file nobody can open is worse than no location at all.
        string path = WriteInput(Version + NameProbe, "originalname");

        //Act
        BatchRunResult renamed = BatchRunner.RunFile(path, ScratchDirectory(), "renamed", null);
        BatchRunResult plain = BatchRunner.RunFile(path, ScratchDirectory(), null, null);

        //Assert
        //The CONTROL is the same file with no rename, whose locations must be identical:
        //what -o changes is the output, so the two runs' diagnostics must agree.
        //⚠ THE FILE AND THE LINE, NOT THE COLUMN. The oracle reports this error at 4:7
        //and the port at 4:23 — an older, separate difference in where a location points
        //within the line, which compare-diagnostics.py does not grade (location is not
        //part of its key) and which this fence has no business asserting either way.
        //What it asserts is WHICH FILE the location names.
        string located = renamed.Diagnostics.First(d => d.Contains("unknown command"));
        located.Should().StartWith("originalname.ly:4:");
        plain.Diagnostics.First(d => d.Contains("unknown command"))
            .Should().StartWith("originalname.ly:4:");
    }

    [Fact]
    public void the_input_name_belongs_to_the_run_and_not_to_the_callers_options()
    {
        //Arrange
        //A host may hold ONE options object and engrave several files through it. If the
        //runner wrote the input name into the caller's object, the second file would
        //report the first one's name.
        string first = WriteInput(Version + NameProbe, "firstfile");
        string second = WriteInput(Version + NameProbe, "secondfile");
        BatchRunOptions shared = new BatchRunOptions();
        StringWriter firstLog = new StringWriter();
        StringWriter secondLog = new StringWriter();

        //Act
        shared.MessageWriter = firstLog;
        BatchRunner.RunFile(first, ScratchDirectory(), "renamed", shared);
        shared.MessageWriter = secondLog;
        BatchRunner.RunFile(second, ScratchDirectory(), "renamed", shared);

        //Assert
        firstLog.ToString().Should().Contain("probe input-file-name is firstfile.ly");
        secondLog.ToString().Should().Contain("probe input-file-name is secondfile.ly");
        shared.InputName.Should().BeNull();
    }
}
