// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;

namespace MutopiaProbe.Corpus;

/// <summary>
/// One row of the corpus's <c>ENTRY_POINTS.tsv</c>: a reference PDF (and, when Mutopia
/// published one, a reference MIDI) together with the <c>.ly</c> whose stem produced it.
/// </summary>
public sealed class EntryPoint
{
    /// <summary>Creates a row.</summary>
    /// <param name="piecePath">The piece's path under the corpus root, forward-slashed.</param>
    /// <param name="referencePdf">The reference PDF's file name within the piece directory.</param>
    /// <param name="referenceMidi">The reference MIDI's file name, or null when none was published.</param>
    /// <param name="sourceLy">The source <c>.ly</c>, relative to the piece directory, or null when the corpus could not match one.</param>
    public EntryPoint(string piecePath, string referencePdf, string referenceMidi, string sourceLy)
    {
        PiecePath = piecePath ?? throw new ArgumentNullException(nameof(piecePath));
        ReferencePdf = referencePdf ?? throw new ArgumentNullException(nameof(referencePdf));
        ReferenceMidi = string.IsNullOrEmpty(referenceMidi) ? null : referenceMidi;
        SourceLy = string.IsNullOrEmpty(sourceLy) ? null : sourceLy;
    }

    /// <summary>Gets the piece's path under the corpus root (for example <c>MozartWA/KV525/MozartWA-KV525</c>).</summary>
    public string PiecePath { get; }

    /// <summary>Gets the reference PDF's file name (for example <c>CanonInD-a4.pdf</c>).</summary>
    public string ReferencePdf { get; }

    /// <summary>Gets the reference MIDI's file name, or null.</summary>
    public string ReferenceMidi { get; }

    /// <summary>Gets the source <c>.ly</c> relative to the piece directory, or null when unmatched.</summary>
    public string SourceLy { get; }

    /// <summary>Gets the output stem: the reference PDF's name without its <c>-a4.pdf</c> suffix.</summary>
    public string Stem
    {
        get
        {
            string name = ReferencePdf;
            if (name.EndsWith("-a4.pdf", StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - "-a4.pdf".Length);
            }

            return Path.GetFileNameWithoutExtension(name);
        }
    }

    /// <summary>Gets the key that names this row in <c>results.tsv</c> and the output tree.</summary>
    public string Key => PiecePath + "/" + Stem;
}
