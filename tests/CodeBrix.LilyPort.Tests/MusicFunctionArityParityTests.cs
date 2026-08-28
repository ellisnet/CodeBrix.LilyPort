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
/// ⚠ THE ARITY ITSELF IS NOT WHAT IS CHECKED, and the reason is recorded on
/// <c>BatchRunner</c>'s own helper: the Scheme interpreter under this port binds a missing
/// required parameter to <c>#&lt;unspecified&gt;</c> rather than raising, so the port never
/// sees the short call and can only see its consequence one step later. These tests are
/// written against the CONSEQUENCE — no output — rather than against the wording, so they
/// keep passing when the interpreter starts raising and the proxy retires.
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
        BatchRunResult result = BatchRunner.RunText(
            source, "arity-refused", null, ScratchDirectory());

        //Assert
        result.SvgPaths.Count.Should().Be(0);
        result.MidiPaths.Count.Should().Be(0);
        (result.ErrorCount > 0).Should().BeTrue("upstream exits 1 on this input");
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
