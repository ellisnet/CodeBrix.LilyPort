// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/abc2ly.py (get_option_parser);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// What may be asked of <see cref="AbcImporter"/>, named after <c>abc2ly</c>'s own long
/// options.
/// </summary>
/// <remarks>
/// Four kinds of upstream option are deliberately absent, and are not to be restored:
/// the driver's own (<c>-o</c>, <c>-h</c>, <c>--version</c>) belong to the application
/// calling this; the log-level switch (<c>-q</c>) is answered by filtering
/// <see cref="ImportResult.Messages"/>; there is no implementation to select; and the
/// input arrives as text rather than as a file name.
/// </remarks>
public sealed class AbcImportOptions
{
    /// <summary>
    /// Gets or sets whether to be strict about success — <c>abc2ly</c>'s
    /// <c>-s</c>/<c>--strict</c>.
    /// </summary>
    /// <remarks>
    /// Upstream exits on the first thing it does not understand rather than carrying on
    /// and writing a file with a hole in it; the import fails the same way, with
    /// <see cref="ImportResult.Text"/> null.
    /// </remarks>
    public bool Strict { get; set; }

    /// <summary>
    /// Gets or sets whether to preserve ABC's notion of beams — <c>abc2ly</c>'s
    /// <c>-b</c>/<c>--beams</c>.
    /// </summary>
    public bool Beams { get; set; }

    /// <summary>
    /// Gets or sets what to call the input in diagnostics.
    /// </summary>
    /// <remarks>
    /// ⚠ PORT-ONLY, and not a widening of the option surface. Upstream takes the file
    /// name from its command line and prints it in the one message that names a
    /// location ("<c>FILE: LINE: Huh?  Don't understand</c>"). A library is handed text
    /// rather than a path, so the caller supplies the name it wants those messages to
    /// carry; left unset, the message reads exactly as upstream's does for a file whose
    /// name is empty.
    /// </remarks>
    public string SourceName { get; set; } = string.Empty;
}
