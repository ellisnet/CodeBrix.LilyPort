// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/midi2ly.py (the driver);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Reads a Standard MIDI File and writes LilyPond source — the in-process equivalent of
/// LilyPond's own <c>midi2ly</c> script.
/// </summary>
/// <remarks>
/// What comes back is a transcription, not a score: MIDI carries no beams, no slurs, no
/// spelling of an accidental and no idea where a voice begins. midi2ly guesses the
/// clef from the average pitch, splits overlapping notes into voices by when they
/// sound, and leans on LilyPond's completion engravers to make bars out of whatever
/// lengths it found — which is why the document it writes opens by installing them.
/// </remarks>
public static class MidiImporter
{
    /// <summary>Converts a MIDI file to LilyPond source.</summary>
    /// <param name="midiData">The file's bytes.</param>
    /// <param name="options">What to ask of the converter, or <see langword="null"/>
    /// for its defaults.</param>
    /// <returns>The result.</returns>
    public static ImportResult Import(byte[] midiData, MidiImportOptions options = null)
    {
        MidiImportOptions effective = options ?? new MidiImportOptions();
        ImportDiagnostics diagnostics = new ImportDiagnostics();
        MidiConverter converter = new MidiConverter(effective, diagnostics);

        string text = null;
        bool stopped = false;
        try
        {
            text = converter.Convert(midiData);
        }
        catch (MidiFormatException badFile)
        {
            //python/midi.py's own error, which midi2ly does not catch: the script ends
            //with a traceback and writes nothing.
            stopped = true;
            diagnostics.Write(
                "midi2ly: error: " + badFile.Message + "\n");
            diagnostics.CountError();
        }
        catch (ImportAbortedException aborted)
        {
            stopped = true;
            if (!aborted.AlreadyReported)
            {
                diagnostics.Write("midi2ly: error: " + aborted.Message + "\n");
                diagnostics.CountError();
            }
        }

        //MIDI2LY'S OWN EXIT CODE, AND IT IS THE LOOSEST OF THE THREE. The script has no
        //error() and no --strict: `ly.error' (scripts/midi2ly.py:821, "BUG: time skew")
        //only prints, and its only sys.exit calls are command-line ones — 0 for
        //--warranty, 2 when no file was named — neither of which a library has. So a
        //file it can read ALWAYS converts and always exits zero, and the only way the
        //script ends without writing is the traceback the two catches above stand for.
        IReadOnlyList<string> messages = diagnostics.Close();
        return new ImportResult(text, messages, diagnostics.Errors, !stopped && text != null);
    }
}
