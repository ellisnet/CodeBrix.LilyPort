// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers;

/// <summary>
/// What an import produced: the LilyPond source, and everything the converter had to
/// say while it was writing it.
/// </summary>
/// <remarks>
/// Upstream's converters are command-line scripts that write their remarks to standard
/// error and their output to a file. Neither is what a library wants, so the text is
/// returned and the remarks are CAPTURED here — an editor offering File &gt; Import can
/// then show them beside the document instead of losing them to a console nobody read.
/// <para>
/// The shape mirrors <c>ConvertLy</c>'s <c>ConversionResult</c> deliberately, so a
/// caller handling one text transformer handles them all; the type is not shared,
/// because a conversion's six version members mean nothing to an importer.
/// </para>
/// </remarks>
public sealed class ImportResult
{
    internal ImportResult(
        string text, IReadOnlyList<string> messages, int errors, bool succeeded)
    {
        Text = text;
        Messages = messages;
        Errors = errors;
        Succeeded = succeeded;
    }

    /// <summary>
    /// Gets the LilyPond source, or <see langword="null"/> when the input could not be
    /// converted at all.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets what the converter had to say — upstream's warnings and errors, one message
    /// per line it would have written to standard error.
    /// </summary>
    /// <remarks>
    /// Progress and identification lines are NOT here. They are the command-line
    /// driver's output, governed upstream by a <c>--quiet</c> switch that this surface
    /// deliberately does not carry (the caller decides what to show); what remains is
    /// the material a user needs to see.
    /// </remarks>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Gets how many errors the converter reported.</summary>
    /// <remarks>
    /// A COUNT, not a verdict. Every one of these converters reports things it did not
    /// understand and carries on; see <see cref="Succeeded"/> for whether the conversion
    /// itself stood.
    /// </remarks>
    public int Errors { get; }

    /// <summary>
    /// Gets whether the conversion SUCCEEDED, in the sense the script this importer
    /// stands in for means by its exit code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Upstream's converters are command-line programs, and a caller of one — Frescobaldi
    /// among them — decides whether the import worked from the EXIT CODE, not from how
    /// much the program had to say on the way. All three of them convert what they
    /// understand, report what they do not, write the file and exit zero; each stops only
    /// where its own source stops, and the three do not agree about where that is:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>abc2ly</c> (<c>scripts/abc2ly.py:98-101</c>) — <c>error()</c>
    /// writes its message and RETURNS; only <c>--strict</c> makes it
    /// <c>sys.exit(1)</c>.</description></item>
    /// <item><description><c>midi2ly</c> — has no <c>error()</c> at all and no strict
    /// switch. Its only <c>sys.exit</c>s are command-line ones (<c>--warranty</c>, and no
    /// file named); a file it can read is always converted and the script exits
    /// zero.</description></item>
    /// <item><description><c>musicxml2ly</c> (<c>scripts/musicxml2ly.py:5977-5978</c>) —
    /// everything it cannot interpret is an <c>ly.warning</c>, which only prints; its one
    /// diagnostic-driven exit is <c>sys.exit(1)</c> for an input it cannot
    /// OPEN.</description></item>
    /// </list>
    /// <para>
    /// So this is false exactly where the script would have ended without writing a file,
    /// and true otherwise — including for a document the converter only partly understood,
    /// which is the case upstream opens and the count in <see cref="Errors"/> describes.
    /// </para>
    /// </remarks>
    public bool Succeeded { get; }
}
