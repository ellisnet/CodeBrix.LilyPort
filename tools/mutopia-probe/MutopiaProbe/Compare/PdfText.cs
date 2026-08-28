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

namespace MutopiaProbe.Compare;

/// <summary>
/// The words on the page, from both PDFs alike, through the host's <c>pdftotext</c>
/// (poppler-utils). Titles, composer, lyrics, tempo marks and Mutopia's footer all survive
/// extraction on both sides, so an ordered-token comparison separates "the wrong words are on
/// the page" from "the same words, moved" — which the ink grade cannot do.
/// </summary>
public static class PdfText
{
    /// <summary>Extracts the tokens of a PDF, or null when <c>pdftotext</c> is unavailable or failed.</summary>
    /// <param name="pdfPath">The PDF.</param>
    /// <param name="error">Receives why extraction failed, or null.</param>
    /// <returns>The whitespace-separated tokens in reading order, or null.</returns>
    public static List<string> Tokens(string pdfPath, out string error)
    {
        error = null;
        try
        {
            ProcessStartInfo start = new ProcessStartInfo("pdftotext")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-enc");
            start.ArgumentList.Add("UTF-8");
            start.ArgumentList.Add(pdfPath);
            start.ArgumentList.Add("-");
            using Process process = Process.Start(start);
            string text = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                error = "pdftotext exit " + process.ExitCode + ": " + FirstLine(stderr);
                return null;
            }

            // A token is a maximal run of LETTERS. Two reasons, both measured on the first
            // pairs. Mutopia's references draw the notation glyphs with text operators
            // (Emmentaler through the PostScript backend), so pdftotext returns hundreds of
            // private-use "words" per page that the port's path-based PDF does not have —
            // letters only drops them, and the bar numbers with them (those follow the line
            // breaks, which are the ink grade's to report). And whether "www." and ".org"
            // come out as one token or three, or two lyric syllables at a line end as
            // "be-" "Gu-" or "beGu-", depends on nothing but horizontal spacing; letter runs
            // make both sides agree on what a word is.
            List<string> tokens = new List<string>();
            System.Text.StringBuilder run = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetter(c))
                {
                    run.Append(c);
                }
                else if (run.Length > 0)
                {
                    tokens.Add(run.ToString());
                    run.Clear();
                }
            }

            if (run.Length > 0)
            {
                tokens.Add(run.ToString());
            }

            return tokens;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is IOException)
        {
            error = "pdftotext unavailable: " + FirstLine(exception.Message);
            return null;
        }
    }

    /// <summary>
    /// The similarity of two token sequences: 2·LCS / (n + m), 1.0 when identical. Sequences over
    /// <paramref name="cap"/> tokens are truncated to the cap (the DP is quadratic) and the
    /// truncation is reported through <paramref name="truncated"/>.
    /// </summary>
    /// <param name="left">One sequence.</param>
    /// <param name="right">The other.</param>
    /// <param name="cap">The per-side token cap.</param>
    /// <param name="truncated">Receives whether either side was truncated.</param>
    /// <param name="containment">Receives LCS / min(n, m): how much of the shorter side the longer contains.</param>
    /// <returns>The similarity in [0, 1]; 1 when both are empty.</returns>
    public static double Similarity(List<string> left, List<string> right, int cap, out bool truncated, out double containment)
    {
        truncated = left.Count > cap || right.Count > cap;
        int n = Math.Min(left.Count, cap);
        int m = Math.Min(right.Count, cap);
        containment = 1.0;
        if (n == 0 && m == 0)
        {
            return 1.0;
        }

        if (n == 0 || m == 0)
        {
            containment = 0.0;
            return 0.0;
        }

        int[] previous = new int[m + 1];
        int[] current = new int[m + 1];
        for (int i = 1; i <= n; i++)
        {
            string a = left[i - 1];
            for (int j = 1; j <= m; j++)
            {
                current[j] = string.Equals(a, right[j - 1], StringComparison.Ordinal)
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }

            (previous, current) = (current, previous);
            Array.Clear(current, 0, current.Length);
        }

        int lcs = previous[m];
        containment = (double)lcs / Math.Min(n, m);
        return 2.0 * lcs / (n + m);
    }


    /// <summary>
    /// Bag-of-words containment: how much of the shorter side's token MULTISET the longer side
    /// holds, ignoring order. Order is a layout artefact here — pdftotext reads two verses of
    /// lyrics under a system in whatever order their baselines fall — so the ordered LCS
    /// punishes a reflowed page for words that are all present.
    /// </summary>
    /// <param name="left">One side.</param>
    /// <param name="right">The other.</param>
    /// <returns>|A ∩ B| / min(|A|, |B|); 1 when both are empty; 0 when only one is.</returns>
    public static double BagContainment(List<string> left, List<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 1.0;
        }

        if (left.Count == 0 || right.Count == 0)
        {
            return 0.0;
        }

        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string token in left)
        {
            counts[token] = counts.TryGetValue(token, out int n) ? n + 1 : 1;
        }

        int shared = 0;
        foreach (string token in right)
        {
            if (counts.TryGetValue(token, out int n) && n > 0)
            {
                shared++;
                counts[token] = n - 1;
            }
        }

        return (double)shared / Math.Min(left.Count, right.Count);
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
