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
    internal ImportResult(string text, IReadOnlyList<string> messages, int errors)
    {
        Text = text;
        Messages = messages;
        Errors = errors;
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
    public int Errors { get; }

    /// <summary>Gets whether the input converted.</summary>
    public bool Succeeded => Text != null && Errors == 0;
}
