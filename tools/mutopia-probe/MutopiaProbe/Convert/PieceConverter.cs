// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.LilyPort.ConvertLy;

namespace MutopiaProbe.Convert;

/// <summary>
/// Copies one piece directory into the run and runs the port's convert-ly over every
/// <c>.ly</c>/<c>.ily</c> in the copy, so the engraver reads 2.27-syntax sources with every
/// relative <c>\include</c> still resolving. The corpus itself is never written to.
/// <para>
/// An include file usually declares NO <c>\version</c>; upstream's convert-ly would refuse it
/// without <c>--from</c>. Here the fallback "from" is the HIGHEST version any file in the piece
/// declares, on the reasoning that the includes were written for the same LilyPond as the
/// score that includes them — recorded in the piece's <c>convert.log</c> so a wrong guess can
/// be seen.
/// </para>
/// </summary>
public static class PieceConverter
{
    /// <summary>The per-file outcome of a conversion.</summary>
    public sealed class FileOutcome
    {
        /// <summary>Gets or sets the file's path relative to the piece directory.</summary>
        public string RelativePath { get; set; }

        /// <summary>Gets or sets the <c>\version</c> the file declared, or null.</summary>
        public string DeclaredVersion { get; set; }

        /// <summary>Gets or sets the version the conversion started from, or null when unknown.</summary>
        public string FromVersion { get; set; }

        /// <summary>Gets or sets whether the fallback "from" was used.</summary>
        public bool UsedFallbackFrom { get; set; }

        /// <summary>Gets or sets how many rules were applied.</summary>
        public int RulesApplied { get; set; }

        /// <summary>Gets or sets whether the text changed.</summary>
        public bool Changed { get; set; }

        /// <summary>Gets or sets the converter's error count.</summary>
        public int Errors { get; set; }

        /// <summary>Gets or sets the converter's messages (its "Not smart enough" warnings and the like).</summary>
        public List<string> Messages { get; set; } = new List<string>();

        /// <summary>Gets or sets the version the converted file was stamped with, or null.</summary>
        public string StampedVersion { get; set; }

        /// <summary>Gets or sets an exception message when the converter threw.</summary>
        public string Exception { get; set; }
    }

    /// <summary>The outcome of converting a whole piece.</summary>
    public sealed class PieceOutcome
    {
        /// <summary>Gets or sets the converted copy's directory.</summary>
        public string ConvertedDirectory { get; set; }

        /// <summary>Gets or sets the fallback "from" version used for version-less files, or null.</summary>
        public string FallbackFrom { get; set; }

        /// <summary>Gets the per-file outcomes, keyed by relative path (forward-slashed).</summary>
        public Dictionary<string, FileOutcome> Files { get; } = new Dictionary<string, FileOutcome>(StringComparer.Ordinal);
    }

    /// <summary>Copies and converts one piece.</summary>
    /// <param name="pieceDirectory">The piece's directory in the corpus.</param>
    /// <param name="convertedDirectory">Where the converted copy goes; emptied first.</param>
    /// <param name="log">Receives the piece's convert log text.</param>
    /// <returns>The outcome.</returns>
    public static PieceOutcome Convert(string pieceDirectory, string convertedDirectory, TextWriter log)
    {
        if (Directory.Exists(convertedDirectory))
        {
            Directory.Delete(convertedDirectory, true);
        }

        CopyTree(pieceDirectory, convertedDirectory);

        PieceOutcome outcome = new PieceOutcome { ConvertedDirectory = convertedDirectory };

        List<string> sources = new List<string>();
        foreach (string file in Directory.GetFiles(convertedDirectory, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);
            if (string.Equals(extension, ".ly", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ily", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".lyi", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(file);
            }
        }

        sources.Sort(StringComparer.Ordinal);

        // Pass 1: the highest declared version in the piece, for the version-less includes.
        ConversionVersion? highest = null;
        Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in sources)
        {
            string text = ReadText(file);
            texts[file] = text;
            if (DocumentConverter.TryReadDeclaredVersion(text, out ConversionVersion declared))
            {
                if (highest == null || declared > highest.Value)
                {
                    highest = declared;
                }
            }
        }

        outcome.FallbackFrom = highest?.ToString();
        log.WriteLine("piece: " + pieceDirectory);
        log.WriteLine("converted copy: " + convertedDirectory);
        log.WriteLine("fallback --from for version-less files: " + (outcome.FallbackFrom ?? "(none: no file declares a version)"));
        log.WriteLine("target: " + DocumentConverter.LatestVersion);
        log.WriteLine();

        foreach (string file in sources)
        {
            string relative = Path.GetRelativePath(convertedDirectory, file).Replace('\\', '/');
            FileOutcome fileOutcome = new FileOutcome { RelativePath = relative };
            outcome.Files[relative] = fileOutcome;
            string text = texts[file];

            bool hasOwn = DocumentConverter.TryReadDeclaredVersion(text, out ConversionVersion own);
            fileOutcome.DeclaredVersion = hasOwn ? own.ToString() : null;
            ConversionVersion? from = hasOwn ? own : highest;
            fileOutcome.UsedFallbackFrom = !hasOwn && highest != null;
            fileOutcome.FromVersion = from?.ToString();

            log.WriteLine("== " + relative + "  declared=" + (fileOutcome.DeclaredVersion ?? "-")
                + (fileOutcome.UsedFallbackFrom ? "  from=" + fileOutcome.FromVersion + " (fallback)" : string.Empty));

            if (from == null)
            {
                log.WriteLine("   SKIPPED: no version anywhere to convert from");
                continue;
            }

            try
            {
                ConversionResult result = DocumentConverter.Convert(text, from, null);
                fileOutcome.RulesApplied = result.AppliedRules.Count;
                fileOutcome.Changed = result.Changed;
                fileOutcome.Errors = result.Errors;
                fileOutcome.StampedVersion = result.StampedVersion?.ToString();
                foreach (string message in result.Messages)
                {
                    fileOutcome.Messages.Add(message);
                    log.WriteLine("   " + message.TrimEnd());
                }

                log.WriteLine("   rules=" + result.AppliedRules.Count + " changed=" + result.Changed
                    + " errors=" + result.Errors + " stamped=" + (fileOutcome.StampedVersion ?? "-"));

                if (result.Changed)
                {
                    File.WriteAllText(file, result.Text, new UTF8Encoding(false));
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                fileOutcome.Exception = exception.GetType().Name + ": " + FirstLine(exception.Message);
                fileOutcome.Errors++;
                log.WriteLine("   THREW " + fileOutcome.Exception);
            }
        }

        return outcome;
    }

    /// <summary>
    /// Reads a source as UTF-8, falling back to Latin-1 when the bytes are not valid UTF-8 —
    /// the pre-2.6 files in the corpus predate LilyPond's UTF-8 requirement.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>The text.</returns>
    public static string ReadText(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);
            // Reference outputs and archives are not sources; the scratch copy does not need them.
            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".midi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".rdf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
        }
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
