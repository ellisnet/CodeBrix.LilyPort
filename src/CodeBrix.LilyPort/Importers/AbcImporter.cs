// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/abc2ly.py (the driver);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Reads ABC music notation and writes LilyPond source — the in-process equivalent of
/// LilyPond's own <c>abc2ly</c> script.
/// </summary>
/// <remarks>
/// Support for the ABC standard is INCOMPLETE, and deliberately as incomplete as
/// upstream's: multiple tunes in one file, block comments, PostScript commands and
/// several header fields are not handled, ABC line breaks are ignored, and lyrics are
/// not resynchronised by line breaks. Upstream's own list of limitations is the list
/// this carries, because the two are the same program.
/// <para>
/// See <see href="https://abcnotation.com/standard/abc_v1.6.txt">the ABC 1.6
/// standard</see> for what the input is.
/// </para>
/// </remarks>
public static class AbcImporter
{
    /// <summary>Converts an ABC document to LilyPond source.</summary>
    /// <param name="abcText">The ABC document.</param>
    /// <param name="options">What to ask of the converter, or <see langword="null"/>
    /// for its defaults.</param>
    /// <returns>The result.</returns>
    public static ImportResult Import(string abcText, AbcImportOptions options = null)
    {
        AbcImportOptions effective = options ?? new AbcImportOptions();
        ImportDiagnostics diagnostics = new ImportDiagnostics();
        AbcConverter converter = new AbcConverter(effective, diagnostics);

        string text = null;
        bool stopped = false;
        try
        {
            text = converter.Convert(abcText);
        }
        catch (ImportAbortedException aborted)
        {
            //Upstream ends the process here — under --strict deliberately, and on a
            //python exception because nothing catches it. Either way no file is
            //written, so there is no text to hand back.
            stopped = true;
            if (!aborted.AlreadyReported)
            {
                diagnostics.Write("abc2ly: " + aborted.Message + "\n");
                diagnostics.CountError();
            }
        }

        //ABC2LY'S OWN EXIT CODE, WHICH IS NOT A COUNT OF ITS COMPLAINTS.
        //scripts/abc2ly.py:98-101: error() writes the message and RETURNS, so a tune
        //holding one token the converter does not understand is still converted, still
        //written, and the script still exits ZERO. Only --strict turns error() into
        //sys.exit(1) — and that stop, like an uncaught python exception, arrives here as
        //ImportAbortedException with no text to hand back. So: the run stood unless it
        //was stopped.
        IReadOnlyList<string> messages = diagnostics.Close();
        return new ImportResult(text, messages, diagnostics.Errors, !stopped && text != null);
    }
}
