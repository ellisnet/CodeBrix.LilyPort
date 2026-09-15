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
/// THE DOCUMENT'S OWN TOPLEVEL BOOK HANDLER WINS, AND WHAT IT PRODUCES IS WRITTEN.
/// <para>
/// <c>ly/init.ly</c>'s epilogue hands the toplevel book to
/// <c>default-toplevel-book-handler</c> when that name is bound and to
/// <c>toplevel-book-handler</c> otherwise, and the parser hands an EXPLICIT
/// <c>\book</c> block to the second one the moment the block closes. Both are ordinary
/// definitions a document may replace, and
/// <c>ly/lilypond-book-preamble.ly</c> — which every lilypond-book document opens by
/// including — replaces both: the toplevel book goes to
/// <c>print-book-with-defaults-as-systems</c> (one file per SYSTEM) and an explicit
/// <c>\book</c> to <c>print-book-with-defaults</c> (one file per PAGE). The preamble's
/// own first comment says exactly that.
/// </para>
/// <para>
/// ⚠ THE PORT HONOURED THE HANDLER AND THEN DROPPED WHAT IT PRODUCED. Upstream's two
/// <c>ly:book-process</c> entry points WRITE the files; the port's compute the book and
/// hand the paper book back, because output is written by the batch runner. So a
/// document that installed its own handler engraved to nothing: "Completed
/// successfully", ErrorCount 0, no warning, and no file anywhere — measured on 2026-09-13
/// against the 2.27.2 oracle, which writes the SVG for the same six-line input.
/// </para>
/// <para>
/// Every expectation below is the ORACLE's, measured file by file with
/// <c>lilypond -dbackend=svg</c> before any of it was written. The pairs matter: the
/// systems path and the page path have to come out DIFFERENTLY from the same music, or a
/// runner that had simply kept its own path would pass every single-system case.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class BookHandlerEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    /// <summary>
    /// The include every lilypond-book document opens with, and the one line that makes
    /// all of this happen.
    /// </summary>
    private const string Preamble = "\\include \"lilypond-book-preamble.ly\"\n";

    /// <summary>
    /// Music that breaks into three systems, so that "one file per system" and "one file
    /// per page" cannot both be satisfied by the same answer.
    /// </summary>
    private const string ThreeSystems =
        "\\relative c' {\n"
        + "  c4 d e f | g a b c | \\break\n"
        + "  c,4 d e f | g a b c | \\break\n"
        + "  c,4 d e f | g a b c |\n"
        + "}\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-bookhandler-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Runs one source and answers the whole result.</summary>
    /// <param name="source">The LilyPond source.</param>
    /// <param name="name">The base name the run prints under.</param>
    /// <param name="messages">Where the run's log goes, or <see langword="null"/>.</param>
    /// <returns>The run's result.</returns>
    private static BatchRunResult Run(string source, string name, StringWriter messages)
        => BatchRunner.RunText(
            source,
            name,
            null,
            ScratchDirectory(),
            messages == null ? null : new BatchRunOptions { MessageWriter = messages });

    /// <summary>The written pages' bare file names, in the order the runner wrote them.</summary>
    /// <param name="source">The LilyPond source.</param>
    /// <param name="name">The base name the run prints under.</param>
    /// <returns>The file names.</returns>
    private static List<string> WrittenNames(string source, string name)
    {
        BatchRunResult result = Run(source, name, null);
        result.SvgPaths.Should().NotBeNull();
        return result.SvgPaths.Select(Path.GetFileName).ToList();
    }

    [Fact]
    public void the_preamble_writes_one_file_per_system()
    {
        //Arrange
        // MEASURED: the oracle writes musicfont-bach-1.svg and musicfont-bach-2.svg for
        // the two-system Goldberg sample, and here three systems give three files.
        // output-stencils numbers them from first-page-number, which is 1.
        string source = Version + Preamble + ThreeSystems;

        //Act
        List<string> names = WrittenNames(source, "bookhandler-systems");

        //Assert
        names.Should().Equal(new List<string>
        {
            "bookhandler-systems-1.svg",
            "bookhandler-systems-2.svg",
            "bookhandler-systems-3.svg",
        });
    }

    [Fact]
    public void the_preamble_writes_a_single_system_under_the_bare_base_name()
    {
        //Arrange
        // MEASURED on the FIXLIST's own six-line reproduction: the oracle writes
        // tinybook.svg, NOT tinybook-1.svg. framework-svg.scm's output-stencils suffixes
        // nothing when there is exactly one stencil, and the rule is the same one the
        // page path has always used here — which is why one writer serves both.
        string source = Version + Preamble + "\\relative c' { c4 d e f | g a b c }\n";

        //Act
        List<string> names = WrittenNames(source, "bookhandler-onesystem");

        //Assert
        names.Should().Equal(new List<string> { "bookhandler-onesystem.svg" });
    }

    [Fact]
    public void an_explicit_book_under_the_preamble_takes_the_page_path()
    {
        //Arrange
        // THE HALF THAT MAKES THE PAIR MEAN SOMETHING, and the preamble's own first
        // comment: "toplevel \book gets output per page, everything else gets output per
        // system/title". The preamble points `toplevel-book-handler' at
        // print-book-with-defaults, so the SAME three systems that give three files above
        // give ONE file here, because they fit on one page. MEASURED against the oracle.
        string source = Version + Preamble + "\\book {\n  \\score {\n" + ThreeSystems + "  }\n}\n";

        //Act
        List<string> names = WrittenNames(source, "bookhandler-explicitbook");

        //Assert
        names.Should().Equal(new List<string> { "bookhandler-explicitbook.svg" });
    }

    [Fact]
    public void an_ordinary_document_still_takes_the_page_path()
    {
        //Arrange
        // THE CONTROL. A document that leaves the toplevel handlers alone must be
        // untouched by any of this: the runner's own collector takes the book and the
        // page path writes it, exactly as it did before the fix.
        string source = Version + ThreeSystems;

        //Act
        List<string> names = WrittenNames(source, "bookhandler-ordinary");

        //Assert
        names.Should().Equal(new List<string> { "bookhandler-ordinary.svg" });
    }

    [Fact]
    public void the_systems_path_runs_the_line_breaker_and_not_the_page_breaker()
    {
        //Arrange
        // The log is the second half of the same divergence and was measured with it: the
        // oracle logs "Calculating line breaks..." for a preamble document and "Finding
        // the ideal number of pages..." for an ordinary one. The port logged the PAGE
        // lines for both, because its ly:book-process-to-systems forced the pages —
        // classic_output_aux forces the SYSTEMS and never runs a page breaker at all.
        StringWriter messages = new StringWriter();

        //Act
        Run(Version + Preamble + ThreeSystems, "bookhandler-systemslog", messages);

        //Assert
        string log = messages.ToString();
        log.Should().Contain("Calculating line breaks");
        log.Should().NotContain("Finding the ideal number of pages");
    }

    [Fact]
    public void an_ordinary_document_still_runs_the_page_breaker()
    {
        //Arrange
        // The control for the log pair, so that a runner which had simply stopped
        // printing the page lines could not pass the fact above.
        StringWriter messages = new StringWriter();

        //Act
        Run(Version + ThreeSystems, "bookhandler-pagelog", messages);

        //Assert
        messages.ToString().Should().Contain("Finding the ideal number of pages");
    }

    [Fact]
    public void the_two_paths_share_one_name_counter()
    {
        //Arrange
        // MEASURED: the oracle gives an explicit \book and a document-handled toplevel
        // book in ONE file the names twobooks.svg and twobooks-1.svg, because
        // get-outfile-name's counter-alist is one table for the whole run. Two counters —
        // one in the runner and one in the Scheme layer — would name both books the same
        // and let the second overwrite the first.
        string source =
            Version
            + "#(define default-toplevel-book-handler print-book-with-defaults-as-systems)\n"
            + "\\book { \\score { \\relative c' { c4 d e f } } }\n"
            + "\\relative c' { g'4 a b c }\n";

        //Act
        List<string> names = WrittenNames(source, "bookhandler-mixed");

        //Assert
        names.Should().Equal(new List<string>
        {
            "bookhandler-mixed.svg",
            "bookhandler-mixed-1.svg",
        });
    }

    [Fact]
    public void a_handler_that_drops_the_book_warns_instead_of_reporting_silent_success()
    {
        //Arrange
        // THE SAFETY NET. A handler that neither writes the book nor hands it back leaves
        // the runner nothing to write, and the run would otherwise report success with
        // zero errors and no explanation — which is the shape of the defect this class
        // exists for. It is a WARNING and not an error because the oracle is SILENT here:
        // the port is never stricter than 2.27.2, only louder.
        StringWriter messages = new StringWriter();
        string source =
            Version
            + "#(define default-toplevel-book-handler (lambda (book) #f))\n"
            + ThreeSystems;

        //Act
        BatchRunResult result = Run(source, "bookhandler-swallowed", messages);

        //Assert
        result.SvgPaths.Should().BeEmpty();
        result.ErrorCount.Should().Be(0);
        messages.ToString().Should().Contain("neither written nor handed back");
    }

    [Fact]
    public void a_book_with_no_scores_writes_nothing_and_says_nothing()
    {
        //Arrange
        // THE FIRST LEGITIMATE SILENCE, and the reason the net above is narrow. The
        // preamble sets output-empty-score-list, so a document with nothing in it still
        // builds a book and still reaches a handler — and the oracle writes no file and
        // emits no warning. MEASURED: `lilypond -dbackend=svg' on the include alone logs
        // "Parsing..." and nothing else.
        StringWriter messages = new StringWriter();

        //Act
        BatchRunResult result = Run(Version + Preamble, "bookhandler-emptybook", messages);

        //Assert
        result.SvgPaths.Should().BeEmpty();
        result.BookCount.Should().Be(1);
        messages.ToString().Should().NotContain("warning");
    }

    [Fact]
    public void a_book_that_only_performs_writes_its_midi_and_says_nothing()
    {
        //Arrange
        // THE SECOND LEGITIMATE SILENCE. classic_output_aux writes the performances
        // before it forces the systems, so a score with a \midi block and no \layout
        // produces a MIDI file and no page — MEASURED: the oracle writes midionly.midi
        // alone. The port wrote neither before the fix.
        StringWriter messages = new StringWriter();
        string source =
            Version
            + Preamble
            + "\\score { \\relative c' { c4 d e f } \\midi { } }\n";

        //Act
        BatchRunResult result = Run(source, "bookhandler-midionly", messages);

        //Assert
        result.SvgPaths.Should().BeEmpty();
        result.MidiPaths.Count.Should().Be(1);
        Path.GetFileName(result.MidiPaths[0]).Should().Be("bookhandler-midionly.midi");
        messages.ToString().Should().NotContain("warning");
    }

    [Fact]
    public void the_preamble_does_not_leak_its_handler_into_the_next_run()
    {
        //Arrange
        // The preamble REPLACES four toplevel handlers and sets two program options, and
        // the runner keeps one session for the whole batch. RestoreDefaults is what puts
        // them back; without it every file engraved after a lilypond-book document would
        // take the systems path too. Rule: a test that moves a toplevel binding from its
        // .ly input proves it moved back.
        WrittenNames(Version + Preamble + ThreeSystems, "bookhandler-leak-first");

        //Act
        List<string> names = WrittenNames(Version + ThreeSystems, "bookhandler-leak-second");

        //Assert
        names.Should().Equal(new List<string> { "bookhandler-leak-second.svg" });
    }
}
