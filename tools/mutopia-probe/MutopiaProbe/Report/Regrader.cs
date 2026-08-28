// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using MutopiaProbe.Compare;
using MutopiaProbe.Corpus;
using MutopiaProbe.Oracle;

namespace MutopiaProbe.Report;

/// <summary>
/// Re-grades a sweep that has already run: it reads the SVG pages, PDFs and MIDIs an earlier run
/// left on disk and writes a fresh <c>results.tsv</c> into a NEW directory, without engraving
/// anything and without touching the run it read.
/// <para>
/// WHY. A full <c>--oracle</c> sweep of this corpus is 85 minutes, almost all of it the two
/// engravers. When only the GRADING changes — a threshold, a rung, a column — re-running the
/// engravers measures nothing new and risks measuring something else by accident. The regrade
/// re-computes every graded column from the artefacts and CARRIES FORWARD, cell for cell, every
/// column that describes the run itself: the conversion, the engrave status, the parse-error and
/// system counts, the page and MIDI file counts, the oracle's status, seconds, errors and
/// warnings. Those cannot be recovered from the artefacts and are not guesses — they are what the
/// original run recorded.
/// </para>
/// <para>
/// WHAT IT DOES NOT REPRODUCE: the comparison PNGs (the source run's are still where it left
/// them) and the midi-crosscheck copies. Everything the verdict is cut on is recomputed.
/// </para>
/// </summary>
public static class Regrader
{
    /// <summary>Re-grades an existing run.</summary>
    /// <param name="corpusRoot">The corpus's <c>pieces/</c> directory — Mutopia's own PDFs and MIDIs are read from it.</param>
    /// <param name="sourceRun">The run directory to read.</param>
    /// <param name="outputRoot">Where the fresh <c>results.tsv</c> goes. Must not be the source run.</param>
    /// <param name="ink">Whether to run the raster ink grade.</param>
    /// <returns>0 when the regrade ran; 2 on a usage error.</returns>
    public static int Run(string corpusRoot, string sourceRun, string outputRoot, bool ink)
    {
        string sourceResults = Path.Combine(sourceRun, "results.tsv");
        if (!File.Exists(sourceResults))
        {
            Console.Error.WriteLine("--regrade: no results.tsv in " + sourceRun);
            return 2;
        }

        if (string.Equals(Path.GetFullPath(sourceRun).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            Console.Error.WriteLine("--regrade: the output directory must not be the run being read (it is left untouched)");
            return 2;
        }

        Dictionary<string, EntryPoint> entries = new Dictionary<string, EntryPoint>(StringComparer.Ordinal);
        foreach (EntryPoint entry in EntryPointTable.Read(corpusRoot))
        {
            entries[entry.Key] = entry;
        }

        List<ResultRow> rows = ResultRow.Read(sourceResults);
        Directory.CreateDirectory(outputRoot);
        string resultsPath = Path.Combine(outputRoot, "results.tsv");
        using StreamWriter results = new StreamWriter(resultsPath, false);
        results.AutoFlush = true;
        results.WriteLine(ResultRow.Header());

        Console.WriteLine("# regrading " + sourceRun);
        Console.WriteLine("# corpus:   " + corpusRoot);
        Console.WriteLine("# output:   " + outputRoot + "  (the run above is not written to)");
        Console.WriteLine("# " + rows.Count + " row(s); ink grading " + (ink ? "at " + PdfComparison.Thresholds.Dpi + " dpi" : "OFF"));

        Stopwatch clock = Stopwatch.StartNew();
        Dictionary<string, int> tallies = new Dictionary<string, int>(StringComparer.Ordinal);
        int index = 0;
        int missing = 0;
        foreach (ResultRow row in rows)
        {
            index++;
            string key = row["key"];
            if (!entries.TryGetValue(key, out EntryPoint entry))
            {
                missing++;
                row["note"] = RowFiller.Append(row["note"], "regrade: no ENTRY_POINTS row for this key; graded columns carried forward unchanged");
                results.WriteLine(row.Line());
                continue;
            }

            try
            {
                RegradeRow(row, entry, corpusRoot, sourceRun, ink);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                row["note"] = RowFiller.Append(row["note"], "regrade threw: " + Engrave.PieceEngraver.Describe(exception));
            }

            results.WriteLine(row.Line());
            Tally(tallies, "verdict:" + row["verdict"]);
            Tally(tallies, "svg_staves:" + row["svg_staves"]);
            Tally(tallies, "raster_staves(oracle):" + row["o_raster_staves"]);
            if (index % 25 == 0 || index == rows.Count)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] {2:0.0}s", index, rows.Count, clock.Elapsed.TotalSeconds));
            }
        }

        Console.WriteLine();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "# {0} row(s) regraded in {1:0.0}s{2}",
            rows.Count, clock.Elapsed.TotalSeconds, missing == 0 ? string.Empty : "; " + missing + " had no corpus entry and were carried forward"));
        foreach (KeyValuePair<string, int> tally in tallies.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("# " + tally.Key + "\t" + tally.Value);
        }

        Console.WriteLine("# results: " + resultsPath);
        return 0;
    }

    private static void RegradeRow(ResultRow row, EntryPoint entry, string corpusRoot, string sourceRun, bool ink)
    {
        string pieceDirectory = Path.Combine(corpusRoot, entry.PiecePath.Replace('/', Path.DirectorySeparatorChar));
        string entryOutput = Path.Combine(sourceRun, "pieces", entry.PiecePath.Replace('/', Path.DirectorySeparatorChar), entry.Stem);

        // The sweep writes the port's PDF beside the entry directory whichever source was graded,
        // but the SVG pages and the MIDI of a RAW fallback live in raw/. engraved_from says which.
        string portPagesDirectory = string.Equals(row["engraved_from"], "raw", StringComparison.Ordinal)
            ? Path.Combine(entryOutput, "raw") : entryOutput;
        OracleRunner.CollectOutputs(portPagesDirectory, entry.Stem, out List<string> portSvg, out List<string> portMidiFiles);

        string portPdf = Path.Combine(entryOutput, entry.Stem + ".pdf");
        if (!File.Exists(portPdf))
        {
            portPdf = null;
        }

        string referencePdf = Path.Combine(pieceDirectory, entry.ReferencePdf);
        string referenceMidi = entry.ReferenceMidi == null ? null : Path.Combine(pieceDirectory, entry.ReferenceMidi);
        string portMidi = Pick(portMidiFiles, entry.Stem);

        PdfComparison pdf = PdfComparison.Grade(portPdf, referencePdf, null);
        RowFiller.FillMutopiaPdf(row, pdf, ink);
        MidiComparison midi = MidiComparison.Grade(portMidi, referenceMidi);
        RowFiller.FillMutopiaMidi(row, midi);

        string oracleStatus = row["oracle"];
        bool oracleRan = !string.IsNullOrEmpty(oracleStatus) && oracleStatus != "OFF";
        PdfComparison oraclePdf = null;
        MidiComparison oracleMidi = null;
        SvgStaffComparison svgStaves = null;
        bool oracleHasPages = false;
        bool oracleHasMidi = false;
        bool oracleFinished = false;
        if (oracleRan)
        {
            string oracleDirectory = Path.Combine(entryOutput, "oracle");
            OracleRunner.CollectOutputs(oracleDirectory, entry.Stem, out List<string> oracleSvg, out List<string> oracleMidiFiles);
            string oraclePdfPath = Path.Combine(oracleDirectory, entry.Stem + ".pdf");
            if (!File.Exists(oraclePdfPath))
            {
                oraclePdfPath = null;
            }

            // The same three statuses the sweep treats as "the oracle came back": a kill or a
            // failure to launch leaves a PARTIAL render, which is no evidence either way.
            oracleFinished = oracleStatus == "OK" || oracleStatus == "NOOUT" || oracleStatus == "FAIL";
            oracleHasPages = oraclePdfPath != null;
            oraclePdf = PdfComparison.Grade(portPdf, oraclePdfPath, null);
            svgStaves = SvgStaves.Compare(portSvg, oracleSvg);
            RowFiller.FillOraclePdf(row, oraclePdf, svgStaves, ink);

            string oracleMidiPath = MidiComparison.Counterpart(oracleMidiFiles, portMidi, entry.Stem);
            oracleHasMidi = oracleMidiPath != null;
            oracleMidi = MidiComparison.Grade(portMidi, oracleMidiPath);
            RowFiller.FillOracleMidi(row, oracleMidi);
        }

        row["verdict_pdf"] = DriftVerdict.ForPdf(pdf, oraclePdf, svgStaves, portPdf != null, oracleHasPages, oracleFinished);
        row["verdict_midi"] = DriftVerdict.ForMidi(midi, oracleMidi, portMidi != null, oracleHasMidi, oracleFinished);
        row["verdict"] = DriftVerdict.Worse(row["verdict_pdf"], row["verdict_midi"]);
    }

    private static string Pick(List<string> midiFiles, string stem)
        => midiFiles.FirstOrDefault(m => string.Equals(Path.GetFileName(m), stem + ".midi", StringComparison.Ordinal))
            ?? midiFiles.FirstOrDefault();

    private static void Tally(Dictionary<string, int> tallies, string key)
    {
        tallies[key] = tallies.TryGetValue(key, out int n) ? n + 1 : 1;
    }
}
