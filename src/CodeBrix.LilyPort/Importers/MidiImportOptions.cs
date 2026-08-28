// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/midi2ly.py (get_option_parser);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// What may be asked of <see cref="MidiImporter"/>, named after <c>midi2ly</c>'s own
/// long options.
/// </summary>
/// <remarks>
/// Four kinds of upstream option are deliberately absent, and are not to be restored:
/// the driver's own (<c>-o</c>, <c>-h</c>, <c>--version</c>, <c>-w</c>) belong to the
/// application calling this; the log-level switches (<c>-q</c>, <c>-V</c>, <c>-D</c>)
/// are answered by filtering <see cref="ImportResult.Messages"/>; there is no
/// implementation to select; and the input arrives as bytes rather than as a file name.
/// </remarks>
public sealed class MidiImportOptions
{
    /// <summary>
    /// Gets or sets whether to print absolute pitches — <c>-a</c>/<c>--absolute-pitches</c>.
    /// </summary>
    public bool AbsolutePitches { get; set; }

    /// <summary>
    /// Gets or sets the duration to quantise note durations on —
    /// <c>-d</c>/<c>--duration-quant</c>, or <see langword="null"/> not to.
    /// </summary>
    public int? DurationQuant { get; set; }

    /// <summary>
    /// Gets or sets whether to print explicit durations —
    /// <c>-e</c>/<c>--explicit-durations</c>.
    /// </summary>
    public bool ExplicitDurations { get; set; }

    /// <summary>
    /// Gets the files to prepend to the output — <c>-i</c>/<c>--include-header</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ A LIST, because upstream's option is <c>action='append'</c> and may be given
    /// more than once; each entry is a path, and each file is READ and copied in
    /// between a comment naming it and a <c>% end</c> line, exactly as upstream does.
    /// </remarks>
    public IList<string> IncludeHeader { get; } = new List<string>();

    /// <summary>
    /// Gets or sets the key to assume — <c>-k</c>/<c>--key</c>, spelled
    /// <c>ALT[:MINOR]</c> where ALT is +sharps or -flats and MINOR is 1.
    /// </summary>
    /// <remarks>
    /// Kept as upstream spells it rather than as two numbers: the string is what a user
    /// types, and a key signature the file itself carries at time zero overrides it
    /// only when this is unset.
    /// </remarks>
    public string Key { get; set; }

    /// <summary>
    /// Gets or sets whether to convert only the first four bars —
    /// <c>-p</c>/<c>--preview</c>.
    /// </summary>
    public bool Preview { get; set; }

    /// <summary>
    /// Gets or sets whether to use <c>s</c> instead of <c>r</c> for rests —
    /// <c>-S</c>/<c>--skip</c>.
    /// </summary>
    public bool Skip { get; set; }

    /// <summary>
    /// Gets or sets the duration to quantise note starts on —
    /// <c>-s</c>/<c>--start-quant</c>, or <see langword="null"/> not to.
    /// </summary>
    public int? StartQuant { get; set; }

    /// <summary>
    /// Gets the tuplet durations to allow — <c>-t</c>/<c>--allow-tuplet</c>, each
    /// spelled <c>DUR*NUM/DEN</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ A LIST, because upstream's option is <c>action='append'</c>: a run may allow
    /// several, and <c>--allow-tuplet=4*2/3 --allow-tuplet=2*4/3</c> is upstream's own
    /// worked example.
    /// </remarks>
    public IList<string> AllowTuplet { get; } = new List<string>();

    /// <summary>
    /// Gets or sets whether to treat every text event as a lyric —
    /// <c>-x</c>/<c>--text-lyrics</c>.
    /// </summary>
    public bool TextLyrics { get; set; }

    /// <summary>Gets or sets what to call the input in the output's own tag line.</summary>
    /// <remarks>
    /// ⚠ PORT-ONLY, and not a widening of the option surface. midi2ly takes the file
    /// name from its command line and writes it into the first line of the document it
    /// produces ("<c>% Lily was here -- automatically converted by midi2ly from
    /// FILE</c>"). A library is handed bytes rather than a path, so the caller supplies
    /// the name that line should carry.
    /// </remarks>
    public string SourceName { get; set; } = string.Empty;
}
