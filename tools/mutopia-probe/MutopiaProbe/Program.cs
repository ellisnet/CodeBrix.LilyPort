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
using CodeBrix.LilyPort;
using MutopiaProbe.Compare;
using MutopiaProbe.Convert;
using MutopiaProbe.Corpus;
using MutopiaProbe.Engrave;
using MutopiaProbe.Oracle;
using MutopiaProbe.Pdf;
using MutopiaProbe.Report;

namespace MutopiaProbe;

/// <summary>
/// The Mutopia probe: for every entry point of the corpus, convert, engrave, write a PDF, and
/// grade the PDF and MIDI against Mutopia's own. One process, one engine session, one row of
/// <c>results.tsv</c> per entry point, appended as it is produced so a killed run loses
/// nothing already graded and <c>--resume</c> picks up after it.
/// <para>
/// Usage: <c>MutopiaProbe CORPUS_PIECES_DIR OUT_DIR [--files a,b] [--limit N] [--resume]
/// [--retry-hung] [--timeout-seconds N] [--dpi N] [--no-ink] [--oracle [PATH]]
/// [--oracle-timeout-seconds N]</c>.
/// </para>
/// <para>
/// THIS IS NOT A FIDELITY ORACLE. Mutopia's PDFs and MIDIs were produced by the LilyPond named
/// in each piece's <c>\version</c> (2.4 to 2.19 in this corpus); the port is 2.27.2. A
/// difference is version drift, Mutopia's build environment, or a port gap, and the tool
/// cannot tell which — the OBSERVATIONS document written from its table has to.
/// </para>
/// <para>
/// <c>--oracle</c> is what makes it decidable. With it, the pinned upstream 2.27.2 renders the
/// SAME source the port was given, its pages go through the same SVG-to-PDF writer, and the row
/// carries a second grade — port against 2.27.2 — beside the first. The <c>verdict</c> column
/// then reads DRIFT when the port and 2.27.2 agree and only Mutopia differs, and PORT-GAP when
/// they do not. It roughly doubles the run time.
/// </para>
/// </summary>
public static class Program
{
    /// <summary>Where the harness's README tells a reader to unpack the pinned oracle; used when <c>--oracle</c> names no path.</summary>
    public static readonly string DefaultOracle = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "ClaudeHome", "oracle", "lilypond-2.27.2", "bin", "lilypond");

    /// <summary>Runs the sweep.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>0 when the sweep ran; 2 on usage errors.</returns>
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "usage: MutopiaProbe CORPUS_PIECES_DIR OUT_DIR [--files a,b] [--limit N] [--resume]"
                + " [--retry-hung] [--timeout-seconds N] [--dpi N] [--no-ink] [--oracle [PATH]]"
                + " [--oracle-timeout-seconds N]");
            return 2;
        }

        string corpusRoot = Path.GetFullPath(args[0]);
        string outputRoot = Path.GetFullPath(args[1]);
        int limit = int.MaxValue;
        HashSet<string> only = null;
        bool resume = false;
        bool retryHung = false;
        bool ink = true;
        int timeoutSeconds = 300;
        int oracleTimeoutSeconds = 0;
        string oracleBinary = null;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--limit" && i + 1 < args.Length)
            {
                limit = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--files" && i + 1 < args.Length)
            {
                only = new HashSet<string>(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            }
            else if (args[i] == "--resume")
            {
                resume = true;
            }
            else if (args[i] == "--retry-hung")
            {
                retryHung = true;
            }
            else if (args[i] == "--no-ink")
            {
                ink = false;
            }
            else if (args[i] == "--timeout-seconds" && i + 1 < args.Length)
            {
                timeoutSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--oracle")
            {
                // The path is optional: with the pinned install where the harness keeps it,
                // "--oracle" alone is the whole flag.
                oracleBinary = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i] : DefaultOracle;
            }
            else if (args[i] == "--oracle-timeout-seconds" && i + 1 < args.Length)
            {
                oracleTimeoutSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--dpi" && i + 1 < args.Length)
            {
                PdfComparison.Thresholds.Dpi = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else
            {
                Console.Error.WriteLine("unknown option: " + args[i]);
                return 2;
            }
        }

        Directory.CreateDirectory(outputRoot);
        List<EntryPoint> entries = EntryPointTable.Read(corpusRoot);
        string resultsPath = Path.Combine(outputRoot, "results.tsv");
        HashSet<string> done = resume ? ResultRow.ExistingKeys(resultsPath) : new HashSet<string>(StringComparer.Ordinal);
        if (!resume && File.Exists(resultsPath))
        {
            File.Delete(resultsPath);
        }

        using StreamWriter results = new StreamWriter(resultsPath, append: true);
        results.AutoFlush = true;
        if (new FileInfo(resultsPath).Length == 0)
        {
            results.WriteLine(ResultRow.Header());
        }

        // The rows the corpus could not match to a source are listed, not dropped.
        using (StreamWriter skipped = new StreamWriter(Path.Combine(outputRoot, "skipped-no-source.tsv"), false))
        {
            skipped.WriteLine("piece\tpdf\tmid");
            foreach (EntryPoint entry in entries.Where(e => e.SourceLy == null))
            {
                skipped.WriteLine(entry.PiecePath + "\t" + entry.ReferencePdf + "\t" + (entry.ReferenceMidi ?? string.Empty));
            }
        }

        List<EntryPoint> selected = entries
            .Where(e => e.SourceLy != null)
            .Where(e => only == null || only.Contains(e.Key) || only.Contains(e.Stem) || only.Any(o => e.Key.Contains(o, StringComparison.Ordinal)))
            .Where(e => !done.Contains(e.Key))
            .Take(limit)
            .ToList();

        int faces = ScorePdfWriter.RegisterFonts(Path.Combine(outputRoot, "fonts", "text"));
        string oracleVersion = null;
        string oracleFontConfig = null;
        if (oracleBinary != null)
        {
            oracleBinary = Path.GetFullPath(oracleBinary);
            if (!File.Exists(oracleBinary))
            {
                Console.Error.WriteLine("--oracle: not found: " + oracleBinary);
                return 2;
            }

            oracleVersion = OracleRunner.Version(oracleBinary);
            oracleFontConfig = OracleRunner.PinFonts(oracleBinary, Path.Combine(outputRoot, "fonts"));
            if (oracleFontConfig == null)
            {
                // Unpinned, the oracle would measure its text in the HOST's fonts and every row
                // would carry a text-metric difference that is this machine's, not the port's.
                Console.Error.WriteLine("--oracle: cannot find the oracle's own fonts under "
                    + Path.GetDirectoryName(Path.GetDirectoryName(oracleBinary)) + "/share/lilypond/*/fonts/otf;"
                    + " refusing to run it against the host's fonts");
                return 2;
            }

            if (!oracleVersion.Contains("2.27.2", StringComparison.Ordinal))
            {
                Console.WriteLine("# WARNING: the port targets 2.27.2; this oracle is \"" + oracleVersion
                    + "\", so the port-vs-oracle grade measures version drift too");
            }
        }

        if (oracleTimeoutSeconds <= 0)
        {
            oracleTimeoutSeconds = timeoutSeconds;
        }
        string scratchRoot = Path.Combine(Path.GetTempPath(), "mutopia-probe-scratch-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(scratchRoot);

        Console.WriteLine("# corpus: " + corpusRoot);
        Console.WriteLine("# output: " + outputRoot);
        Console.WriteLine("# entry points: " + entries.Count + " total, " + entries.Count(e => e.SourceLy == null)
            + " without a source (listed in skipped-no-source.tsv), " + done.Count + " already done, " + selected.Count + " to run");
        Console.WriteLine("# text faces registered with Html2Pdf: " + faces);
        Console.WriteLine("# per-file scratch working directories under " + scratchRoot);
        Console.WriteLine("# timeout " + timeoutSeconds + " s per file; ink grading " + (ink ? "at " + PdfComparison.Thresholds.Dpi + " dpi" : "OFF"));
        Console.WriteLine(oracleBinary == null
            ? "# oracle: OFF (--oracle to grade the port against upstream 2.27.2 as well as against Mutopia)"
            : "# oracle: " + oracleBinary + " (" + oracleVersion + "), " + oracleTimeoutSeconds
                + " s per file, fonts pinned to " + OracleRunner.FontDirectory + " via " + oracleFontConfig);

        Dictionary<string, PieceConverter.PieceOutcome> converted = new Dictionary<string, PieceConverter.PieceOutcome>(StringComparer.Ordinal);
        Stopwatch clock = Stopwatch.StartNew();
        int index = 0;
        Dictionary<string, int> tallies = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (EntryPoint entry in selected)
        {
            index++;
            ResultRow row = new ResultRow();
            row["key"] = entry.Key;
            row["piece"] = entry.PiecePath;
            row["stem"] = entry.Stem;
            row["source_ly"] = entry.SourceLy;

            string pieceDirectory = Path.Combine(corpusRoot, entry.PiecePath.Replace('/', Path.DirectorySeparatorChar));
            string pieceOutput = Path.Combine(outputRoot, "pieces", entry.PiecePath.Replace('/', Path.DirectorySeparatorChar));
            string entryOutput = Path.Combine(pieceOutput, entry.Stem);
            Directory.CreateDirectory(entryOutput);
            string started = Path.Combine(entryOutput, "STARTED");

            if (File.Exists(started) && !retryHung)
            {
                // A marker with no row means an earlier run never came back from this file.
                row["engrave"] = "HUNG";
                row["note"] = "STARTED marker from an earlier run and no result row; re-run with --retry-hung to try again";
                results.WriteLine(row.Line());
                Tally(tallies, "engrave:HUNG");
                Console.WriteLine(entry.Key + "\tHUNG\t(skipped)");
                continue;
            }

            File.WriteAllText(started, DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            Stopwatch fileClock = Stopwatch.StartNew();
            try
            {
                RunEntry(entry, row, pieceDirectory, pieceOutput, entryOutput, corpusRoot, outputRoot, scratchRoot,
                    converted, TimeSpan.FromSeconds(timeoutSeconds), ink, oracleBinary, TimeSpan.FromSeconds(oracleTimeoutSeconds));
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                row["error"] = PieceEngraver.Describe(exception);
                if (string.IsNullOrEmpty(row["engrave"]))
                {
                    row["engrave"] = "ERROR";
                }
            }

            row.Set("seconds", fileClock.Elapsed.TotalSeconds);
            results.WriteLine(row.Line());
            File.Delete(started);

            Tally(tallies, "engrave:" + row["engrave"]);
            Tally(tallies, "pages:" + row["page_count"]);
            Tally(tallies, "text:" + row["text"]);
            Tally(tallies, "ink:" + row["ink"]);
            Tally(tallies, "midi:" + row["midi"]);
            if (oracleBinary != null)
            {
                Tally(tallies, "oracle:" + row["oracle"]);
                Tally(tallies, "verdict:" + row["verdict"]);
                Tally(tallies, "verdict_pdf:" + row["verdict_pdf"]);
                Tally(tallies, "verdict_midi:" + row["verdict_midi"]);
            }
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[{0}/{1}] {2}\t{3}\tpages {4}/{5}\t{6}\t{7} {8}\tmidi {9} {10}\t{11}{12:0.0}s{13}",
                index, selected.Count, entry.Key, row["engrave"], row["pages_port"], row["pages_ref"],
                row["text"], row["ink"], row["block_diff"], row["midi"], row["midi_channel"] + " " + row["midi_notes"],
                oracleBinary == null ? string.Empty
                    : row["verdict"] + " (" + row["verdict_pdf"] + "/" + row["verdict_midi"] + " vs oracle "
                        + row["o_page_count"] + " " + row["o_ink"] + " " + row["o_block_diff"] + " " + row["o_midi_channel"] + ")\t",
                fileClock.Elapsed.TotalSeconds,
                string.IsNullOrEmpty(row["error"]) ? string.Empty : "\tERROR " + row["error"]));
        }

        Console.WriteLine();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "# {0} entry point(s) in {1:0.0}s", selected.Count, clock.Elapsed.TotalSeconds));
        foreach (KeyValuePair<string, int> tally in tallies.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("# " + tally.Key + "\t" + tally.Value);
        }

        Console.WriteLine("# results: " + resultsPath);
        return 0;
    }

    private static void RunEntry(
        EntryPoint entry, ResultRow row, string pieceDirectory, string pieceOutput, string entryOutput,
        string corpusRoot, string outputRoot, string scratchRoot,
        Dictionary<string, PieceConverter.PieceOutcome> converted, TimeSpan timeout, bool ink,
        string oracleBinary, TimeSpan oracleTimeout)
    {
        // 1. Convert the piece (once per piece; every entry point of it shares the copy).
        if (!converted.TryGetValue(entry.PiecePath, out PieceConverter.PieceOutcome conversion))
        {
            using StreamWriter convertLog = new StreamWriter(Path.Combine(pieceOutput, "convert.log"), false);
            conversion = PieceConverter.Convert(pieceDirectory, Path.Combine(pieceOutput, "converted"), convertLog);
            converted[entry.PiecePath] = conversion;
        }

        string sourceKey = entry.SourceLy.Replace('\\', '/');
        conversion.Files.TryGetValue(sourceKey, out PieceConverter.FileOutcome sourceOutcome);
        int pieceMessages = conversion.Files.Values.Sum(f => f.Messages.Count);
        int pieceErrors = conversion.Files.Values.Sum(f => f.Errors);
        if (sourceOutcome == null)
        {
            row["convert"] = "SOURCE-NOT-FOUND";
            row["engrave"] = "ERROR";
            row["error"] = "source " + entry.SourceLy + " is not in the converted copy";
            return;
        }

        row["declared_version"] = sourceOutcome.DeclaredVersion ?? "(none)";
        row["convert"] = sourceOutcome.Exception != null ? "CONVERT-THREW"
            : sourceOutcome.FromVersion == null ? "NO-VERSION"
            : pieceErrors > 0 ? "CONVERT-ERROR"
            : sourceOutcome.Changed ? "CONVERTED" : "UNCHANGED";
        row.Set("convert_rules", sourceOutcome.RulesApplied);
        row.Set("convert_messages", pieceMessages);

        // 2. Engrave the converted source; fall back to the raw source when that produced nothing
        //    and the conversion had touched (or failed on) the piece.
        string convertedSource = Path.Combine(conversion.ConvertedDirectory, entry.SourceLy.Replace('/', Path.DirectorySeparatorChar));
        string rawSource = Path.Combine(pieceDirectory, entry.SourceLy.Replace('/', Path.DirectorySeparatorChar));
        string scratch = Path.Combine(scratchRoot, entry.Stem);

        PieceEngraver.Outcome outcome;
        using (StreamWriter engraveLog = new StreamWriter(Path.Combine(entryOutput, "engrave.log"), false))
        {
            engraveLog.AutoFlush = true;
            engraveLog.WriteLine("# " + entry.Key + " from " + convertedSource);
            outcome = PieceEngraver.Engrave(convertedSource, entryOutput, entry.Stem, scratch, engraveLog, timeout);
            row["engraved_from"] = "converted";
            bool conversionTouched = conversion.Files.Values.Any(f => f.Changed || f.Errors > 0);
            if (outcome.Status != "OK" && outcome.Status != "TIMEOUT" && conversionTouched)
            {
                engraveLog.WriteLine();
                engraveLog.WriteLine("# converted source produced no page (" + outcome.Status + "); trying the RAW source " + rawSource);
                string rawOutput = Path.Combine(entryOutput, "raw");
                PieceEngraver.Outcome rawOutcome = PieceEngraver.Engrave(rawSource, rawOutput, entry.Stem, scratch, engraveLog, timeout);
                row["note"] = "converted: " + outcome.Status + (outcome.Error != null ? " (" + outcome.Error + ")" : string.Empty)
                    + "; raw: " + rawOutcome.Status;
                if (rawOutcome.Status == "OK")
                {
                    outcome = rawOutcome;
                    row["engraved_from"] = "raw";
                }
            }
        }

        row["engrave"] = outcome.Status;
        row["error"] = outcome.Error ?? string.Empty;
        row.Set("svg_pages", outcome.SvgPages.Count);
        row.Set("midi_files", outcome.MidiFiles.Count);
        row.Set("side_files", outcome.SideFiles.Count);
        if (outcome.Result != null)
        {
            row.Set("parse_errors", outcome.Result.ErrorCount);
            row.Set("systems", outcome.Result.SystemCount);
        }

        // 3. SVG pages -> PDF.
        string portPdf = null;
        if (outcome.SvgPages.Count > 0)
        {
            string pdfPath = Path.Combine(entryOutput, entry.Stem + ".pdf");
            List<string> warnings = new List<string>();
            try
            {
                int pages = ScorePdfWriter.Write(pdfPath, outcome.SvgPages, entry.Stem, warnings);
                row["pdf"] = pages > 0 ? "OK" : "NONE";
                portPdf = pages > 0 ? pdfPath : null;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                row["pdf"] = "PDF-ERROR";
                row["error"] = Append(row["error"], "pdf: " + PieceEngraver.Describe(exception));
            }

            row.Set("pdf_warnings", warnings.Count);
            if (warnings.Count > 0)
            {
                File.WriteAllLines(Path.Combine(entryOutput, "pdf-warnings.txt"), warnings);
            }
        }
        else
        {
            row["pdf"] = "NONE";
        }

        // 4. Grade the PDF.
        string referencePdf = Path.Combine(pieceDirectory, entry.ReferencePdf);
        PdfComparison pdf = PdfComparison.Grade(portPdf, referencePdf, ink ? Path.Combine(entryOutput, "compare") : null);
        row.Set("pages_port", pdf.PortPages);
        row.Set("pages_ref", pdf.ReferencePages);
        row["page_count"] = pdf.PageCountVerdict;
        row["size_port"] = pdf.PortPageSize;
        row["size_ref"] = pdf.ReferencePageSize;
        row["page_size"] = pdf.PageSizeVerdict;
        row["text"] = pdf.TextVerdict;
        row.Set("text_sim", pdf.TextSimilarity);
        row.Set("text_contain", pdf.TextContainment);
        row.Set("text_bag", pdf.TextBag);
        row.Set("tokens_port", pdf.PortTokens);
        row.Set("tokens_ref", pdf.ReferenceTokens);
        row["ink"] = ink ? pdf.InkVerdict : "INK-OFF";
        row.Set("block_diff", pdf.BlockDifference);
        row.Set("ink_iou", pdf.InkIoU);
        row.Set("ink_port", pdf.PortInk);
        row.Set("ink_ref", pdf.ReferenceInk);
        row["staves"] = pdf.StavesVerdict ?? string.Empty;
        row.Set("staves_port", pdf.PortStaves);
        row.Set("staves_ref", pdf.ReferenceStaves);
        row.Set("compared_pages", pdf.ComparedPages);
        row["note"] = Append(row["note"], pdf.Note);

        // 5. Grade the MIDI. The port names its first performance <stem>.midi.
        string portMidi = outcome.MidiFiles.FirstOrDefault(m =>
            string.Equals(Path.GetFileName(m), entry.Stem + ".midi", StringComparison.Ordinal)) ?? outcome.MidiFiles.FirstOrDefault();
        string referenceMidi = entry.ReferenceMidi == null ? null : Path.Combine(pieceDirectory, entry.ReferenceMidi);
        MidiComparison midi = MidiComparison.Grade(portMidi, referenceMidi);
        row["midi"] = midi.Verdict;
        row["midi_channel"] = midi.ChannelVerdict ?? string.Empty;
        row["midi_channel_first_diff"] = midi.ChannelFirstDifference ?? string.Empty;
        row["midi_notes"] = midi.NotesVerdict ?? string.Empty;
        row["midi_pitches"] = midi.PitchesVerdict ?? string.Empty;
        row.Set("midi_tracks_port", midi.PortTracks);
        row.Set("midi_tracks_ref", midi.ReferenceTracks);
        row.Set("midi_div_port", midi.PortDivision);
        row.Set("midi_div_ref", midi.ReferenceDivision);
        row.Set("midi_notes_port", midi.PortNotes);
        row.Set("midi_notes_ref", midi.ReferenceNotes);
        row.Set("midi_len_port", midi.PortLength);
        row.Set("midi_len_ref", midi.ReferenceLength);
        row.Set("midi_tempos_port", midi.PortTempos);
        row.Set("midi_tempos_ref", midi.ReferenceTempos);
        row.Set("midi_programs_port", midi.PortPrograms);
        row.Set("midi_programs_ref", midi.ReferencePrograms);
        row["midi_stamp_ref"] = midi.ReferenceStamp ?? string.Empty;
        row["midi_first_diff"] = midi.FirstDifference ?? string.Empty;
        if (outcome.MidiFiles.Count > 0 && referenceMidi == null)
        {
            row["note"] = Append(row["note"], "port wrote MIDI but Mutopia published none for this entry");
        }

        // 6. THE ORACLE. The pinned upstream 2.27.2 renders the SAME source the port was handed
        //    — the converted copy, or the raw one when that is what the port was graded from —
        //    and its pages go through the SAME SVG-to-PDF writer, so the PDF pipeline is not a
        //    variable here and what the second grade measures is engraving. This is what lets
        //    the verdict say DRIFT rather than "differs from a PDF built in 2009".
        PdfComparison oraclePdf = null;
        MidiComparison oracleMidi = null;
        bool oracleHasPages = false;
        bool oracleHasMidi = false;
        bool oracleFinished = false;
        if (oracleBinary != null)
        {
            string oracleOutput = Path.Combine(entryOutput, "oracle");
            string oracleScratch = Path.Combine(scratchRoot, entry.Stem + "-oracle");
            string gradedSource = row["engraved_from"] == "raw" ? rawSource : convertedSource;
            OracleRunner.Outcome oracle = OracleRunner.Run(
                oracleBinary, gradedSource, oracleOutput, entry.Stem, oracleScratch,
                Path.Combine(entryOutput, "oracle.log"), oracleTimeout);
            row["oracle"] = oracle.Status;
            oracleFinished = oracle.Status == "OK" || oracle.Status == "NOOUT" || oracle.Status == "FAIL";
            row.Set("oracle_seconds", oracle.Seconds);
            row.Set("oracle_errors", oracle.Errors);
            row.Set("oracle_warnings", oracle.Warnings);
            row.Set("oracle_pages", oracle.SvgPages.Count);
            row.Set("oracle_midi_files", oracle.MidiFiles.Count);
            if (oracle.Error != null)
            {
                row["note"] = Append(row["note"], "oracle: " + oracle.Error);
            }

            string oraclePdfPath = null;
            if (oracle.SvgPages.Count > 0)
            {
                oraclePdfPath = Path.Combine(oracleOutput, entry.Stem + ".pdf");
                try
                {
                    if (ScorePdfWriter.Write(oraclePdfPath, oracle.SvgPages, entry.Stem, null) == 0)
                    {
                        oraclePdfPath = null;
                    }
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    oraclePdfPath = null;
                    row["note"] = Append(row["note"], "oracle pdf: " + PieceEngraver.Describe(exception));
                }
            }

            oracleHasPages = oraclePdfPath != null;
            oraclePdf = PdfComparison.Grade(portPdf, oraclePdfPath, ink ? Path.Combine(entryOutput, "compare-oracle") : null);
            row["o_page_count"] = oraclePdf.PageCountVerdict;
            row["o_page_size"] = oraclePdf.PageSizeVerdict;
            row["o_text"] = oraclePdf.TextVerdict;
            row.Set("o_text_bag", oraclePdf.TextBag);
            row["o_ink"] = ink ? oraclePdf.InkVerdict : "INK-OFF";
            row.Set("o_block_diff", oraclePdf.BlockDifference);
            row["o_staves"] = oraclePdf.StavesVerdict ?? string.Empty;
            row.Set("o_staves_oracle", oraclePdf.ReferenceStaves);

            string oracleMidiPath = oracle.MidiFiles.FirstOrDefault(m =>
                string.Equals(Path.GetFileName(m), entry.Stem + ".midi", StringComparison.Ordinal)) ?? oracle.MidiFiles.FirstOrDefault();
            oracleHasMidi = oracleMidiPath != null;
            oracleMidi = MidiComparison.Grade(portMidi, oracleMidiPath);
            row["o_midi"] = oracleMidi.Verdict;
            row["o_midi_channel"] = oracleMidi.ChannelVerdict ?? string.Empty;
            row["o_midi_notes"] = oracleMidi.NotesVerdict ?? string.Empty;
            row["o_midi_pitches"] = oracleMidi.PitchesVerdict ?? string.Empty;
            row["o_midi_first_diff"] = oracleMidi.FirstDifference ?? string.Empty;
            if (oracle.MidiFiles.Count != outcome.MidiFiles.Count)
            {
                row["note"] = Append(row["note"], "port wrote " + outcome.MidiFiles.Count
                    + " MIDI file(s), the oracle " + oracle.MidiFiles.Count);
            }
        }
        else
        {
            row["oracle"] = "OFF";
        }

        row["verdict_pdf"] = DriftVerdict.ForPdf(pdf, oraclePdf, portPdf != null, oracleHasPages, oracleFinished);
        row["verdict_midi"] = DriftVerdict.ForMidi(midi, oracleMidi, portMidi != null, oracleHasMidi, oracleFinished);
        row["verdict"] = DriftVerdict.Worse(row["verdict_pdf"], row["verdict_midi"]);

        // 7. Copies for the harness's own comparator (compare-midi.py wants .midi on both sides).
        if (portMidi != null && referenceMidi != null && File.Exists(referenceMidi))
        {
            string flat = entry.Key.Replace('/', '_') + ".midi";
            string referenceDirectory = Path.Combine(outputRoot, "midi-crosscheck", "reference");
            string candidateDirectory = Path.Combine(outputRoot, "midi-crosscheck", "candidate");
            Directory.CreateDirectory(referenceDirectory);
            Directory.CreateDirectory(candidateDirectory);
            File.Copy(referenceMidi, Path.Combine(referenceDirectory, flat), true);
            File.Copy(portMidi, Path.Combine(candidateDirectory, flat), true);
        }

        if (outcome.SideFiles.Count > 0)
        {
            File.WriteAllLines(Path.Combine(entryOutput, "side-files.txt"), outcome.SideFiles);
        }
    }

    private static void Tally(Dictionary<string, int> tallies, string key)
    {
        tallies[key] = tallies.TryGetValue(key, out int n) ? n + 1 : 1;
    }

    private static string Append(string note, string more)
        => string.IsNullOrEmpty(more) ? note : string.IsNullOrEmpty(note) ? more : note + "; " + more;
}
