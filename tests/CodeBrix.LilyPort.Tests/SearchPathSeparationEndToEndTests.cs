// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// The two search paths upstream keeps apart, and the extension list that separates
/// <c>ly:parse-file</c> from <c>ly:parse-init</c>.
/// <para>
/// Upstream has TWO lists and this port had one. <c>Sources</c>' <c>cur_dir</c> — the
/// directory of the file on top of the include stack — is what the lexer's
/// <c>\include</c> resolves against, and <c>global_path</c> is what <c>ly:find-file</c>,
/// <c>ly:parse-file</c> and <c>ly:parse-init</c> search. The batch runner used to push the
/// entry directory onto the port's <c>global_path</c>, which put it in front of all three
/// primitives and made the port find files upstream cannot.
/// </para>
/// <para>
/// EVERY EXPECTATION BELOW WAS READ OFF THE PINNED 2.27.2 FIRST, from probes under
/// <c>~/ClaudeHome/l18-probes/</c>, run with the working directory somewhere OTHER than
/// the input's directory — which is the arrangement a library call always has:
/// </para>
/// <list type="bullet">
/// <item><description><c>#(ly:message "~a" (ly:find-file "asset.txt"))</c> with
/// <c>asset.txt</c> beside the input answers <c>#f</c>.</description></item>
/// <item><description>with BOTH <c>sub</c> and <c>sub.ly</c> in the working directory,
/// <c>(ly:parse-file "sub")</c> opens <c>sub.ly</c> — the extension loop is the outer one,
/// from <c>lily-parser-scheme.cc</c>'s <c>input_extensions</c>.</description></item>
/// <item><description>with the same two files, <c>(ly:parse-init "sub")</c> opens
/// <c>sub</c>, because that entry point passes NO extension list.</description></item>
/// </list>
/// <para>
/// ⚠ One upstream behaviour is deliberately NOT reproduced: when <c>ly:parse-init</c>
/// resolves nothing, 2.27.2 goes on to parse the empty name and DUMPS CORE (measured).
/// The port raises <c>ly-file-failed</c> instead, which is what the rest of this entry
/// point already did.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class SearchPathSeparationEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private static string NewDirectory(string tag)
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-searchpath-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Engraves a document and answers everything it wrote to the log.</summary>
    /// <param name="text">The document.</param>
    /// <param name="name">The run's name.</param>
    /// <param name="includeDirectory">The directory the document came from, or null.</param>
    /// <returns>The run's diagnostics and messages.</returns>
    private static string Messages(string text, string name, string includeDirectory)
        => Run(text, name, includeDirectory, out _);

    /// <summary>
    /// Engraves a document, answering the log and handing back the run's result.
    /// <para>⚠ A Scheme throw reaches the RESULT's diagnostics, not the message writer:
    /// the writer carries the progress and warning stream the way upstream's stderr does,
    /// and the batch driver prints the two separately.</para>
    /// </summary>
    /// <param name="text">The document.</param>
    /// <param name="name">The run's name.</param>
    /// <param name="includeDirectory">The directory the document came from, or null.</param>
    /// <param name="result">Receives the run's result.</param>
    /// <returns>The run's messages.</returns>
    private static string Run(
        string text, string name, string includeDirectory, out BatchRunResult result)
    {
        StringWriter messages = new StringWriter();
        result = BatchRunner.RunText(
            text,
            name,
            includeDirectory,
            NewDirectory("out"),
            new BatchRunOptions { MessageWriter = messages });

        return messages.ToString();
    }

    [Fact]
    public void the_entry_directory_serves_include_and_is_invisible_to_find_file()
    {
        //Arrange
        // ONE directory, TWO mechanisms, and upstream's two OPPOSITE answers in a single
        // run — which is why this needs no control document. \include must reach
        // `sibling.ily' (upstream's cur_dir); ly:find-file must NOT reach `asset.txt'
        // sitting right beside it (upstream's global_path, measured as #f).
        string root = NewDirectory("entry");
        try
        {
            File.WriteAllText(Path.Combine(root, "sibling.ily"), "siblingMarker = #7\n");
            File.WriteAllText(Path.Combine(root, "asset.txt"), "HELLO-FROM-ASSET\n");

            string document = Version
                + "\\include \"sibling.ily\"\n"
                + "#(ly:message \"find-file says ~a\" (ly:find-file \"asset.txt\"))\n"
                + "#(ly:message \"include says ~a\" siblingMarker)\n"
                + "{ c'4 }\n";

            //Act
            string log = Messages(document, "entry-directory", root);

            //Assert
            log.Should().Contain("include says 7");
            log.Should().Contain("find-file says #f");
            log.Should().NotContain("find-file says " + root);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void parse_file_appends_the_ly_extension_and_parse_init_does_not()
    {
        //Arrange
        // BOTH `sub' and `sub.ly' exist, side by side, and they say different things. That
        // is what makes this an assertion about the ORDER rather than about mere
        // reachability: a resolver that tried the name as written first would find `sub'
        // and never look for the extension, and it would still "work". MEASURED on the
        // pinned 2.27.2 with both files in the working directory: ly:parse-file opens
        // `sub.ly' and ly:parse-init opens `sub'.
        string root = NewDirectory("extension");
        string previous = Directory.GetCurrentDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "sub"), "#(ly:message \"OPENED-BARE-SUB\")\n");
            File.WriteAllText(
                Path.Combine(root, "sub.ly"), "#(ly:message \"OPENED-SUB-DOT-LY\")\n");
            Directory.SetCurrentDirectory(root);

            //Act
            string parseFile = Run(
                Version + "#(ly:parse-file \"sub\")\n{ c'4 }\n", "parse-file-bare", null,
                out BatchRunResult parseFileResult);
            string parseInit = Run(
                Version + "#(ly:parse-init \"sub\")\n{ c'4 }\n", "parse-init-bare", null,
                out BatchRunResult parseInitResult);

            //Assert
            // ly:parse-file carries input_extensions, and the extension loop is the OUTER
            // one, so `sub.ly' wins over the file whose name was actually written.
            parseFile.Should().Contain("OPENED-SUB-DOT-LY");
            parseFile.Should().NotContain("OPENED-BARE-SUB");
            parseFileResult.ErrorCount.Should().Be(0);

            // THE CONTROL, and it must come out the OTHER way round: ly:parse-init passes
            // no extension list, so the same name in the same directory opens the other
            // file. Two primitives, two files, one run apiece — neither answer can be an
            // accident of what happened to be on disk.
            parseInit.Should().Contain("OPENED-BARE-SUB");
            parseInit.Should().NotContain("OPENED-SUB-DOT-LY");
            parseInitResult.ErrorCount.Should().Be(0);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Delete(root);
        }
    }
}
