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
/// <c>check_value_type</c> over the REAL option table — the half
/// <c>ProgramOptionTypeCheckTests</c> cannot reach, because it declares its own options
/// to stay cheap.
/// <para>
/// What this class actually fences is that <c>scm/lily.scm</c>'s
/// <c>scheme-options-definitions</c> arrive with their declared types INTACT: a
/// predicate for <c>resolution</c>, a list for <c>anti-alias-factor</c>, a type symbol
/// for <c>font-export-dir</c>, and <c>boolean?</c> for the ones that name no
/// <c>#:type</c> at all. If the types were dropped on the way in, every case below would
/// pass its value straight through and the document would engrave with none of these
/// diagnostics.
/// </para>
/// <para>
/// The document is the one that was run against the pinned oracle
/// (<c>lilypond -dbackend=svg</c>) before any of this was written, and the expectations
/// are that run's own output, line for line — including the doubled quotation marks,
/// which are upstream's rendering of a string value and not a typo.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class ProgramOptionTypeCheckEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    /// <summary>
    /// The oracle's own input: one wrong value per declared SHAPE, one undeclared name,
    /// one type-symbol option that must pass unchecked, and a read-back of each.
    /// </summary>
    private const string OptionProbe = Version
        + "#(ly:set-option 'resolution \"abc\")\n"
        + "#(ly:set-option 'anti-alias-factor 9)\n"
        + "#(ly:set-option 'png-width 'notanindex)\n"
        + "#(ly:set-option 'nosuchoption #t)\n"
        + "#(ly:set-option 'font-export-dir \"somewhere\")\n"
        + "#(ly:message \"resolution is ~a\" (ly:get-option 'resolution))\n"
        + "#(ly:message \"aaf is ~a\" (ly:get-option 'anti-alias-factor))\n"
        + "#(ly:message \"png-width is ~a\" (ly:get-option 'png-width))\n"
        + "#(ly:message \"nosuchoption is ~a\" (ly:get-option 'nosuchoption))\n"
        + "#(ly:message \"font-export-dir is ~a\" (ly:get-option 'font-export-dir))\n"
        + "{ c'4 }\n";

    /// <summary>
    /// The CONTROL document: the same four options set to values their declared types
    /// ACCEPT. Every one of them must land, and nothing may be warned about — otherwise
    /// the unchanged values above would prove only that a set never happened.
    /// </summary>
    private const string ControlProbe = Version
        + "#(ly:set-option 'resolution 300)\n"
        + "#(ly:set-option 'anti-alias-factor 2)\n"
        + "#(ly:set-option 'png-width 500)\n"
        + "#(ly:message \"resolution is ~a\" (ly:get-option 'resolution))\n"
        + "#(ly:message \"aaf is ~a\" (ly:get-option 'anti-alias-factor))\n"
        + "#(ly:message \"png-width is ~a\" (ly:get-option 'png-width))\n"
        + "{ c'4 }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-optcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Engraves a document and answers everything it wrote to the log.</summary>
    /// <param name="text">The document.</param>
    /// <param name="name">The run's name.</param>
    /// <returns>The run's diagnostics and messages.</returns>
    private static string Messages(string text, string name)
    {
        StringWriter messages = new StringWriter();
        BatchRunner.RunText(
            text,
            name,
            null,
            ScratchDirectory(),
            new BatchRunOptions { MessageWriter = messages });

        return messages.ToString();
    }

    [Fact]
    public void the_real_option_table_checks_its_declared_types()
    {
        //Arrange & Act
        string log = Messages(OptionProbe, "option-type-check");

        //Assert
        // The three refusals, in upstream's wording. `resolution' is #:type
        // ,positive-number? (a predicate); `anti-alias-factor' is #:type (1 2 ... 8) (a
        // list, which names itself in the message); `png-width' is #:type ,index?.
        log.Should().Contain(
            "warning: ignoring option -dresolution=\"\"abc\"\": value has wrong type");
        log.Should().Contain("warning: ignoring option -danti-alias-factor=\"9\":");
        log.Should().Contain("invalid value; possible values are (1 2 3 4 5 6 7 8)");
        log.Should().Contain(
            "warning: ignoring option -dpng-width=\"notanindex\": value has wrong type");

        // Warned about and SET ANYWAY -- upstream keeps a name it does not know.
        log.Should().Contain("warning: no such program option: nosuchoption");
        log.Should().Contain("nosuchoption is #t");

        // The three refused values are the defaults from scheme-options-definitions,
        // unchanged: 101, 1 and 0.
        log.Should().Contain("resolution is 101");
        log.Should().Contain("aaf is 1");
        log.Should().Contain("png-width is 0");

        // #:type string-or-false says how to READ text, not what a value may be, so this
        // one is never checked and the string lands.
        log.Should().Contain("font-export-dir is somewhere");
        log.Should().NotContain("-dfont-export-dir");
    }

    [Fact]
    public void the_same_three_options_accept_values_their_types_allow()
    {
        //Arrange & Act
        string log = Messages(ControlProbe, "option-type-check-control");

        //Assert
        log.Should().Contain("resolution is 300");
        log.Should().Contain("aaf is 2");
        log.Should().Contain("png-width is 500");
        log.Should().NotContain("ignoring option");
        log.Should().NotContain("no such program option");
    }
}
