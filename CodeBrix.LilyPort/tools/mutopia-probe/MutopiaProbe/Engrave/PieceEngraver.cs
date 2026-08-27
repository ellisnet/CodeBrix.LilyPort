// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CodeBrix.LilyPort;

namespace MutopiaProbe.Engrave;

/// <summary>
/// Runs one <c>.ly</c> through <see cref="BatchRunner"/> the way the regression harness's
/// BatchDriver does: from its own emptied scratch working directory (a <c>.ly</c> may WRITE, and
/// what it writes is named relative to the process working directory), with the engine told
/// about the change, and with anything the file left behind named rather than discarded.
/// </summary>
public static class PieceEngraver
{
    /// <summary>What one engrave produced.</summary>
    public sealed class Outcome
    {
        /// <summary>Gets or sets the status: OK, NOOUT, ERROR or TIMEOUT.</summary>
        public string Status { get; set; }

        /// <summary>Gets or sets the run result, or null when the run threw.</summary>
        public BatchRunResult Result { get; set; }

        /// <summary>Gets or sets the exception text when the run threw.</summary>
        public string Error { get; set; }

        /// <summary>Gets or sets the SVG pages, in order.</summary>
        public List<string> SvgPages { get; set; } = new List<string>();

        /// <summary>Gets or sets the MIDI files, in order.</summary>
        public List<string> MidiFiles { get; set; } = new List<string>();

        /// <summary>Gets or sets the files the run wrote into its working directory.</summary>
        public List<string> SideFiles { get; set; } = new List<string>();

        /// <summary>Gets or sets the wall-clock seconds the run took.</summary>
        public double Seconds { get; set; }
    }

    /// <summary>Engraves one file.</summary>
    /// <param name="lyPath">The (converted or raw) <c>.ly</c>.</param>
    /// <param name="outputDirectory">Where the SVG pages and MIDI files land.</param>
    /// <param name="outputBaseName">The output base name.</param>
    /// <param name="scratchDirectory">The per-file scratch working directory; emptied first.</param>
    /// <param name="log">Receives everything the engine prints for the run.</param>
    /// <param name="timeout">The cooperative time budget.</param>
    /// <returns>The outcome.</returns>
    public static Outcome Engrave(
        string lyPath, string outputDirectory, string outputBaseName, string scratchDirectory,
        TextWriter log, TimeSpan timeout)
    {
        Outcome outcome = new Outcome();
        string home = Directory.GetCurrentDirectory();
        if (Directory.Exists(scratchDirectory))
        {
            Directory.Delete(scratchDirectory, true);
        }

        Directory.CreateDirectory(scratchDirectory);
        Directory.CreateDirectory(outputDirectory);

        Stopwatch clock = Stopwatch.StartNew();
        using CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
        try
        {
            Directory.SetCurrentDirectory(scratchDirectory);
            BatchRunner.ReportWorkingDirectoryChange(scratchDirectory);
            BatchRunResult result = BatchRunner.RunFile(
                lyPath, outputDirectory, outputBaseName,
                new BatchRunOptions
                {
                    MessageWriter = log,
                    CancellationToken = cancellation.Token,
                    // The reference PDFs were built without textedit:// anchors, and an
                    // anchor is a bytes difference the PDF grade should never see.
                    PointAndClick = false,
                });
            outcome.Result = result;
            // The half BatchDriver's trap 1b names: a file can report a parse-error COUNT and
            // still produce pages, with the message printed nowhere. The collected diagnostics
            // are the only record, so they go into the log verbatim.
            if (result.Diagnostics.Count > 0 || result.ErrorCount > 0)
            {
                log.WriteLine();
                log.WriteLine("# parse errors reported: " + result.ErrorCount + "; diagnostics collected: " + result.Diagnostics.Count);
                foreach (string diagnostic in result.Diagnostics)
                {
                    log.WriteLine("# DIAG " + diagnostic);
                }
            }

            outcome.SvgPages.AddRange(result.SvgPaths);
            outcome.MidiFiles.AddRange(result.MidiPaths);
            outcome.Status = result.SvgPath != null ? "OK" : "NOOUT";
        }
        catch (OperationCanceledException)
        {
            outcome.Status = "TIMEOUT";
            outcome.Error = "cancelled after " + timeout.TotalSeconds + " s";
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            outcome.Status = "ERROR";
            outcome.Error = Describe(exception);
            log.WriteLine("ENGRAVE THREW: " + exception);
        }
        finally
        {
            Directory.SetCurrentDirectory(home);
            outcome.Seconds = clock.Elapsed.TotalSeconds;
            try
            {
                foreach (string artifact in Directory.GetFiles(scratchDirectory, "*", SearchOption.AllDirectories))
                {
                    outcome.SideFiles.Add(Path.GetRelativePath(scratchDirectory, artifact)
                        + " (" + new FileInfo(artifact).Length + " bytes)");
                }
            }
            catch (IOException)
            {
            }
        }

        return outcome;
    }

    /// <summary>Describes an exception on one line: type, first line of message, and the innermost cause.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The description.</returns>
    public static string Describe(Exception exception)
    {
        Exception inner = exception;
        while (inner.InnerException != null)
        {
            inner = inner.InnerException;
        }

        string text = exception.GetType().Name + ": " + FirstLine(exception.Message);
        if (!ReferenceEquals(inner, exception))
        {
            text += " <- " + inner.GetType().Name + ": " + FirstLine(inner.Message);
        }

        return text;
    }

    private static string FirstLine(string text)
    {
        if (text == null)
        {
            return string.Empty;
        }

        int end = text.IndexOfAny(new[] { '\r', '\n' });
        return end < 0 ? text : text.Substring(0, end);
    }
}
