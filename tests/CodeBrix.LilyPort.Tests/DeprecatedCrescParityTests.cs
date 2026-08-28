// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Linq;
using CodeBrix.LilyPort.ConvertLy;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// <c>\deprecatedcresc</c> and its three siblings: the port is at PARITY with 2.27.2 on
/// them, and this fences that reading so it is not "fixed" later.
/// <para>
/// The Mendelssohn Octet's parts read as a defect — the port reports
/// <c>unknown command: `\deprecatedcresc'</c> dozens of times per part — but the
/// diagnostic is upstream's own. The chain, all of it read off LilyPond 2.27.2 rather
/// than off the port:
/// </para>
/// <para>
/// 1. <c>python/convertrules.py</c>'s rule for 2.13.20 (<c>\cresc etc. are now postfix
/// operators</c>) is what CREATES the token:
/// <c>re.sub(r'\\(cresc|dim|endcresc|enddim)\b', r'\\deprecated\1', s)</c>. It rewrites
/// the old spelling INTO <c>\deprecatedcresc</c>; nothing rewrites it back, and no later
/// rule in the table touches it.
/// </para>
/// <para>
/// 2. 2.27.2's own <c>ly/</c> and <c>scm/</c> define NO such command — the string
/// <c>deprecatedcresc</c> does not occur anywhere in a 2.27.2 installation except in
/// that one conversion rule. The commands existed only for the 2.13 series that
/// introduced the postfix operators, and were removed with it.
/// </para>
/// <para>
/// 3. So 2.27.2 rejects converted 2.12-era sources exactly as this port does, with the
/// same wording and — measured on the Octet — the same COUNT: 353 of these errors on the
/// full score from each side, 49 on Violin I from each side, 47 on Viola I from each
/// side, and each one followed by the same <c>string outside of text script or
/// \lyricmode</c>. Both sides then carry on and draw pages.
/// </para>
/// <para>
/// ⚠ THE FIX WOULD BE THE DIVERGENCE. Teaching the port's vendored <c>ly/</c> a
/// <c>\deprecatedcresc</c> that 2.27.2 does not have would make the port render a file
/// upstream refuses to render properly, which is the leniency this project's fidelity
/// principle rules out. The gap is in the corpus's sources, and upstream's own convert-ly
/// leaves it there.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class DeprecatedCrescParityTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-deprecated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void convert_ly_rewrites_cresc_to_deprecatedcresc_and_nothing_rewrites_it_back()
    {
        //Arrange
        // The Octet's own spelling, from a 2.12-era source: \cresc used as a PREFIX
        // command. Converted all the way to the current version, upstream's 2.13.20 rule
        // renames it and every rule after that leaves the new name alone.
        string source =
            "\\version \"2.12.0\"\n"
            + "{ c'4 \\cresc d'4 e'4 \\endcresc f'4 \\dim g'4 a'4 \\enddim b'4 }\n";

        //Act
        ConversionResult result = DocumentConverter.Convert(
            source, new ConversionVersion(2, 12, 0), DocumentConverter.LatestVersion);

        //Assert
        result.Text.Should().Contain("\\deprecatedcresc");
        result.Text.Should().Contain("\\deprecatedendcresc");
        result.Text.Should().Contain("\\deprecateddim");
        result.Text.Should().Contain("\\deprecatedenddim");
    }

    [Fact]
    public void deprecatedcresc_is_an_unknown_command_because_upstream_does_not_define_it()
    {
        //Arrange
        // What the converted Octet parts hand the engine, reduced to one bar.
        string source = Version + "{ c'4 \\deprecatedcresc d'4 e'4 f'4 }\n";

        //Act
        BatchRunResult result = BatchRunner.RunText(
            source, "deprecated-cresc", null, ScratchDirectory());

        //Assert
        result.Diagnostics.Any(d => d.Contains("unknown command: `\\deprecatedcresc'"))
            .Should().BeTrue(
                "2.27.2 reports the same error on the same input; defining the command"
                + " here would make the port more permissive than the version it targets");
    }

    [Fact]
    public void the_postfix_cresc_that_replaced_it_is_defined()
    {
        //Arrange
        // THE CONTROL that makes the pair mean something: the command 2.13.20 moved TO is
        // alive and well, so the fact above is about this one deleted spelling and not
        // about the port having lost its dynamics.
        string source = Version + "{ c'4\\cresc d'4 e'4 f'4\\! }\n";

        //Act
        BatchRunResult result = BatchRunner.RunText(
            source, "postfix-cresc", null, ScratchDirectory());

        //Assert
        result.Diagnostics.Any(d => d.Contains("unknown command")).Should().BeFalse();
        result.SvgPaths.Count.Should().Be(1);
    }
}
