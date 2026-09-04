// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (the driver);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Reads MusicXML and writes LilyPond source — the in-process equivalent of LilyPond's
/// own <c>musicxml2ly</c> script.
/// </summary>
/// <remarks>
/// Support for MusicXML 4.0 is a SUBSET, and deliberately the same subset upstream
/// converts: MusicXML describes how to DRAW a score, and much of what it can say has no
/// semantic equivalent in LilyPond's input. Upstream's own list of what it does not
/// handle — every commented-out row of its notation and direction tables — is the list
/// this carries, because the two are the same program.
/// </remarks>
public static class MusicXmlImporter
{
    /// <summary>Converts a MusicXML document to LilyPond source.</summary>
    /// <param name="xmlText">The document.</param>
    /// <param name="options">
    /// What to ask of the converter, or <see langword="null"/> for its defaults.
    /// </param>
    /// <returns>The result.</returns>
    public static ImportResult Import(string xmlText, MusicXmlImportOptions options = null)
        => Run(options, (state) => MusicXmlReader.ReadXml(state, xmlText));

    /// <summary>Converts a compressed MusicXML container to LilyPond source.</summary>
    /// <param name="mxlData">The container, as a <c>.mxl</c> file's bytes.</param>
    /// <param name="options">
    /// What to ask of the converter, or <see langword="null"/> for its defaults.
    /// </param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// This is upstream's <c>-z</c>/<c>--compressed</c> option: the container's manifest
    /// is followed to the score it names.
    /// </remarks>
    public static ImportResult ImportCompressed(
        byte[] mxlData, MusicXmlImportOptions options = null)
        => Run(options, (state) => MusicXmlReader.ReadCompressed(state, mxlData));

    /// <summary>Runs one import over whatever the reader answers.</summary>
    /// <param name="options">What to ask of the converter.</param>
    /// <param name="read">How to read the document.</param>
    /// <returns>The result.</returns>
    private static ImportResult Run(
        MusicXmlImportOptions options, Func<MusicXmlImportState, MusicXmlNode> read)
    {
        MusicXmlImportOptions effective = options ?? new MusicXmlImportOptions();
        ImportDiagnostics diagnostics = new ImportDiagnostics();
        MusicXmlImportState state = new MusicXmlImportState(effective, diagnostics);
        MusicXmlConverter converter = new MusicXmlConverter(state);

        string text = null;
        bool stopped = false;
        try
        {
            converter.ApplyOptions();
            MusicXmlNode tree = read(state);
            if (tree == null)
            {
                throw new ImportAbortedException(
                    "the compressed container names no MusicXML score");
            }

            text = converter.Convert(tree, effective.SourceName ?? string.Empty);
        }
        catch (ImportAbortedException aborted)
        {
            //Upstream ends the process here, because nothing catches the exception. No
            //file is written, so there is no text to hand back.
            stopped = true;
            if (!aborted.AlreadyReported)
            {
                diagnostics.Write("musicxml2ly: " + aborted.Message + "\n");
                diagnostics.CountError();
            }
        }

        //MUSICXML2LY'S OWN EXIT CODE. Everything the converter cannot interpret goes
        //through ly.warning, which only prints (python/lilylib.py:82-87 — ly.error
        //prints too); the script's ONE diagnostic-driven stop is
        //scripts/musicxml2ly.py:5977-5978, `ly.error("Unable to find input file %s")'
        //followed by sys.exit(1), for a document it cannot open at all. That is the
        //shape the catch above carries — an unreadable document, or a container naming
        //no score — so again: the run stood unless it was stopped.
        IReadOnlyList<string> messages = diagnostics.Close();
        return new ImportResult(text, messages, diagnostics.Errors, !stopped && text != null);
    }
}
