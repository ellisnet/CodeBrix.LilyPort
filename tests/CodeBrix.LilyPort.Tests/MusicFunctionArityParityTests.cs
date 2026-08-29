// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Linq;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// A music function whose body calls a procedure with the wrong number of arguments
/// REFUSES THE FILE, because that is what 2.27.2 does with it.
/// <para>
/// <c>unfold-repeats</c> gained a leading <c>types</c> argument upstream, and three
/// Mutopia pieces call it from embedded Scheme with the old arity — which convert-ly
/// does not rewrite, on either side. Measured on 2.27.2:
/// <c>\applyMusic #unfold-repeats { c'4 d' e' f' }</c> prints
/// <c>Wrong number of arguments to #&lt;procedure unfold-repeats (types music)&gt;</c>,
/// exits 1 and writes NOTHING — not the offending score's page, and not the page or the
/// <c>.midi</c> of a perfectly good score sitting beside it in the same file, because
/// Guile's error escapes the parse before any book is processed.
/// </para>
/// <para>
/// The port used to report <c>music function cannot return ##&lt;unspecified&gt;</c> and
/// CARRY ON, rendering five pages of the Chopin where upstream renders none. The probe
/// called that PORT-AHEAD; it is a fidelity divergence in the permissive direction, which
/// this project does not keep.
/// </para>
/// <para>
/// REFUSAL HAS ONE SHAPE AS OF THE 1.0.241.10 PIN. CodeBrix.LilyScheme up to
/// 1.0.238.240 bound a missing required parameter to <c>#&lt;unspecified&gt;</c> rather than
/// raising, so the port could only see the consequence one step later and
/// <c>BatchRunner</c> carried a proxy (<c>MusicFunctionReturnedUnspecified</c>) that
/// answered a result with no output. That proxy is GONE: from 1.0.241.10 the short call
/// raises Guile's <c>wrong-number-of-args</c> INSIDE the parse, and it escapes
/// <c>BatchRunner.RunText</c> as a <c>SchemeThrow</c> — the same way every other fatal
/// Scheme error does (the <c>cannot find music object: MarkEvent</c> case), and the way
/// hosts already handle one. So the throw is now REQUIRED rather than accepted as one of
/// two shapes: a test that still tolerated the proxy's shape would go on passing if the
/// arity check regressed.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class MusicFunctionArityParityTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-arity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void a_short_call_inside_a_music_function_refuses_the_whole_file()
    {
        //Arrange
        // The good score comes FIRST and asks for MIDI, so "refused" has to mean the whole
        // file and not just the score that went wrong: 2.27.2 writes neither its page nor
        // its .midi, having died during the parse.
        string source =
            Version
            + "\\score { { c'4 d' e' f' } \\layout { } \\midi { } }\n"
            + "\\score { { \\applyMusic #unfold-repeats { g'4 a' b' c'' } } \\layout { } }\n";

        //Act
        string scratch = ScratchDirectory();
        CodeBrix.LilyScheme.Runtime.SchemeThrow escaped = null;
        try
        {
            BatchRunner.RunText(source, "arity-refused", null, scratch);
        }
        catch (CodeBrix.LilyScheme.Runtime.SchemeThrow thrown)
        {
            escaped = thrown;
        }

        //Assert
        // The interpreter raises, as Guile does, and the error escapes the parse before any
        // book is processed. So the refusal IS the throw, and what it costs is asserted on
        // the DISK rather than on a result object that no longer comes back at all.
        escaped.Should().NotBeNull("2.27.2 dies inside the parse on this input");
        escaped.Key.Should().Be(CodeBrix.LilyScheme.Values.Symbol.Intern("wrong-number-of-args"));
        Directory.GetFiles(scratch, "*", SearchOption.AllDirectories).Should().BeEmpty(
            "upstream writes neither the offending score's page nor the good score's .midi");
    }

    [Fact]
    public void the_same_call_with_the_arity_the_procedure_declares_engraves()
    {
        //Arrange
        // THE CONTROL that makes the fact above mean something: \applyMusic itself, and
        // repeat unfolding through it, are untouched — it is the SHORT CALL that is fatal,
        // not the construct. unfold-repeats-fully takes the one argument this passes.
        string source =
            Version
            + "\\score { { c'4 d' e' f' } \\layout { } \\midi { } }\n"
            + "\\score {\n"
            + "  { \\applyMusic #unfold-repeats-fully { \\repeat unfold 2 { g'4 a' } } }\n"
            + "  \\layout { }\n"
            + "}\n";

        //Act
        BatchRunResult result = BatchRunner.RunText(
            source, "arity-accepted", null, ScratchDirectory());

        //Assert
        result.SvgPaths.Count.Should().Be(1);
        result.MidiPaths.Count.Should().Be(1);
        result.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void an_ordinary_music_function_is_unaffected()
    {
        //Arrange
        // The second control: a hand-written music function, called correctly, still
        // returns its music and still prints. Without this half a refusal that fired on
        // every music function would pass the pair above.
        string source =
            Version
            + "double =\n"
            + "#(define-music-function (music) (ly:music?)\n"
            + "   (make-sequential-music (list music (ly:music-deep-copy music))))\n"
            + "\\score { \\double { c'4 d' } \\layout { } }\n";

        //Act
        BatchRunResult result = BatchRunner.RunText(
            source, "arity-ordinary", null, ScratchDirectory());

        //Assert
        result.SvgPaths.Count.Should().Be(1);
        result.ErrorCount.Should().Be(0);
    }
}
