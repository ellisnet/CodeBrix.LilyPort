// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace MutopiaProbe.Compare;

/// <summary>
/// Counts the staves on a page by reading the SVG the engraver wrote, not by rasterising a PDF
/// and looking for dark rows. The number it returns is a property of the DOCUMENT — it does not
/// move with the grading resolution, and no change in a PDF library or a font can shift it.
/// <para>
/// WHY THIS EXISTS. The raster staff count (<see cref="PageBitmap.StaffCount"/>) was the sole
/// basis of half the PORT-GAP verdicts of the 2026-08-27 sweep and could not carry them:
/// re-graded at 200 dpi instead of 100, one checked pair REVERSED SIGN, and a font-coverage
/// change inside the PDF writer made four footer glyphs cross the 30 %-ink row threshold and
/// manufactured two PORT-GAP verdicts. See OBSERVATIONS_lilyport_mutopia_2026-08-27.txt L1/L4.
/// This rung replaces it in the verdict ladder; the raster count stays in results.tsv as
/// <c>raster_staves*</c>, reported and no longer believed.
/// </para>
/// <para>
/// WHAT A STAFF IS, exactly. Both sides emit LilyPond-style SVG (the port's SvgBackend is a port
/// of upstream's), in which every staff line is a <c>&lt;line&gt;</c> element inside translating
/// <c>&lt;g&gt;</c> groups. So:
/// </para>
/// <list type="number">
/// <item><description>every <c>&lt;line&gt;</c> is resolved to absolute user units by summing the
/// <c>translate(...)</c> of its ancestors (measured on the whole corpus: not one
/// <c>&lt;line&gt;</c> sits under a scale, rotate, matrix or skew — glyphs do, staff lines do
/// not — and a line that did would be dropped and counted in
/// <see cref="SvgPageStaves.LinesUnderNonTranslate"/> rather than mis-placed);</description></item>
/// <item><description>a line is HORIZONTAL when <c>|y1 − y2| ≤ 1e-4</c>;</description></item>
/// <item><description>horizontal lines are bucketed by their exact x-extent (rounded to 1e-3)
/// and the literal text of their <c>stroke-width</c> — the lines of one staff share both
/// exactly, and a bracket, a volta or an ottava rule never shares them with a staff line;</description></item>
/// <item><description>each bucket is sorted by y and cut into maximal runs of EQUALLY SPACED
/// lines (a gap counts as equal within <c>max(0.01, 5 %)</c> of the run's spacing);</description></item>
/// <item><description>a run is ONE STAFF when it holds <see cref="Rules.MinLines"/> to
/// <see cref="Rules.MaxLines"/> lines — 4 for a Gregorian staff, 5 for the usual one, 6 for
/// tablature — AND each line is at least <see cref="Rules.MinLengthInSpacings"/> times as long
/// as the spacing.</description></item>
/// </list>
/// <para>
/// The two rules are not arbitrary and are nowhere near their data. Measured over the 1962 SVG
/// pages of the 2026-08-27 sweep (227 rows, both sides): 122 902 horizontal lines form 23 391
/// runs of five, 64 of four, 12 of six, and 5 034 runs of one to three. The length-to-spacing
/// ratio of a 4-to-6 run is either ≤ 0.23 (stacked LEDGER LINES, which repeat at the staff-to-
/// staff pitch across a system and would otherwise count as a staff) or ≥ 3.72 (a real staff,
/// median 125) — the cut at 3.0 sits in an empty gap sixteen times wider than the noise. The
/// runs of two and three that a length rule alone would have accepted are all volta, ottava and
/// piano-pedal rules, which the line-count rule rejects on sight.
/// </para>
/// <para>
/// WHAT IT DOES NOT COUNT: a one-line percussion staff, which cannot be told from a stray
/// horizontal rule without guessing. None occurs in this corpus, and the omission would in any
/// case apply identically to both sides.
/// </para>
/// </summary>
public static class SvgStaves
{
    /// <summary>The staff-shape rules. Changing one changes what the rung measures; see the README's CALIBRATION section.</summary>
    public static class Rules
    {
        /// <summary>The fewest lines a run may hold and still be a staff: 4, for a Gregorian staff.</summary>
        public static int MinLines = 4;

        /// <summary>The most lines a run may hold and still be one staff: 6, for tablature.</summary>
        public static int MaxLines = 6;

        /// <summary>
        /// How many times its own line spacing a staff's lines must be long. Stacked ledger lines
        /// repeat at the staff-to-staff pitch and sit at ≤ 0.23; a real staff at ≥ 3.72.
        /// </summary>
        public static double MinLengthInSpacings = 3.0;

        /// <summary>How far from horizontal a line may be and still count as one, in user units.</summary>
        public static double FlatTolerance = 1e-4;

        /// <summary>The share of a run's spacing by which the next gap may differ and still continue it.</summary>
        public static double SpacingRelativeTolerance = 0.05;

        /// <summary>The floor under <see cref="SpacingRelativeTolerance"/>, in user units.</summary>
        public static double SpacingAbsoluteTolerance = 0.01;
    }

    /// <summary>Counts the staves on one SVG page.</summary>
    /// <param name="svgPath">The page.</param>
    /// <returns>The count and what it was read from.</returns>
    public static SvgPageStaves CountPage(string svgPath)
    {
        SvgPageStaves page = new SvgPageStaves();
        List<HorizontalLine> lines = new List<HorizontalLine>();
        try
        {
            ReadLines(svgPath, lines, page);
        }
        catch (Exception exception) when (exception is XmlException || exception is IOException || exception is UnauthorizedAccessException)
        {
            page.Error = exception.GetType().Name + ": " + exception.Message;
            return page;
        }

        page.HorizontalLines = lines.Count;
        Dictionary<(long Left, long Right, string Width), List<double>> buckets =
            new Dictionary<(long, long, string), List<double>>();
        foreach (HorizontalLine line in lines)
        {
            (long, long, string) key = ((long)Math.Round(line.Left * 1000.0), (long)Math.Round(line.Right * 1000.0), line.StrokeWidth);
            if (!buckets.TryGetValue(key, out List<double> ys))
            {
                ys = new List<double>();
                buckets[key] = ys;
            }

            ys.Add(line.Y);
        }

        int staves = 0;
        int consumed = 0;
        foreach (KeyValuePair<(long Left, long Right, string Width), List<double>> bucket in buckets)
        {
            List<double> ys = bucket.Value;
            ys.Sort();
            double length = (bucket.Key.Right - bucket.Key.Left) / 1000.0;
            int i = 0;
            while (i < ys.Count)
            {
                int j = i + 1;
                if (j >= ys.Count)
                {
                    break;
                }

                double spacing = ys[j] - ys[i];
                double tolerance = Math.Max(Rules.SpacingAbsoluteTolerance, Rules.SpacingRelativeTolerance * spacing);
                while (j + 1 < ys.Count && Math.Abs((ys[j + 1] - ys[j]) - spacing) <= tolerance)
                {
                    j++;
                }

                int count = j - i + 1;
                if (count >= Rules.MinLines && count <= Rules.MaxLines
                    && spacing > 0 && length >= Rules.MinLengthInSpacings * spacing)
                {
                    staves++;
                    consumed += count;
                }

                i = j + 1;
            }
        }

        page.Staves = staves;
        page.LinesInStaves = consumed;
        return page;
    }

    /// <summary>Counts the staves of every page of one side, in page order.</summary>
    /// <param name="svgPages">The pages, in order.</param>
    /// <returns>One entry per page.</returns>
    public static List<SvgPageStaves> CountPages(IReadOnlyList<string> svgPages)
    {
        List<SvgPageStaves> pages = new List<SvgPageStaves>();
        if (svgPages == null)
        {
            return pages;
        }

        foreach (string page in svgPages)
        {
            pages.Add(CountPage(page));
        }

        return pages;
    }

    /// <summary>
    /// Compares two sides page by page. PER PAGE deliberately: the raster rung summed its counts
    /// over the whole document, so one borderline page decided a whole row and a plus-one on page
    /// 3 could cancel a minus-one on page 9.
    /// </summary>
    /// <param name="portPages">The port's pages, in order.</param>
    /// <param name="oraclePages">The oracle's pages, in order.</param>
    /// <returns>The comparison.</returns>
    public static SvgStaffComparison Compare(IReadOnlyList<string> portPages, IReadOnlyList<string> oraclePages)
    {
        SvgStaffComparison comparison = new SvgStaffComparison();
        comparison.Port = CountPages(portPages);
        comparison.Oracle = CountPages(oraclePages);
        if (comparison.Port.Count == 0 || comparison.Oracle.Count == 0)
        {
            comparison.Verdict = SvgStaffComparison.Unavailable;
            return comparison;
        }

        foreach (SvgPageStaves page in comparison.Port)
        {
            if (page.Error != null)
            {
                comparison.Verdict = SvgStaffComparison.Unreadable;
                comparison.Note = "port page: " + page.Error;
                return comparison;
            }
        }

        foreach (SvgPageStaves page in comparison.Oracle)
        {
            if (page.Error != null)
            {
                comparison.Verdict = SvgStaffComparison.Unreadable;
                comparison.Note = "oracle page: " + page.Error;
                return comparison;
            }
        }

        int pages = Math.Min(comparison.Port.Count, comparison.Oracle.Count);
        comparison.ComparedPages = pages;
        StringBuilder differing = new StringBuilder();
        for (int page = 0; page < pages; page++)
        {
            int port = comparison.Port[page].Staves;
            int oracle = comparison.Oracle[page].Staves;
            if (port == oracle)
            {
                continue;
            }

            if (differing.Length > 0)
            {
                differing.Append(' ');
            }

            differing.Append(CultureInfo.InvariantCulture, $"p{page + 1}:{port}/{oracle}");
        }

        comparison.DifferingPages = differing.ToString();
        comparison.Verdict = differing.Length == 0 ? SvgStaffComparison.Equal : SvgStaffComparison.Differ;
        return comparison;
    }

    /// <summary>Renders a per-page count list as the TSV cell "9,11,9,10".</summary>
    /// <param name="pages">The pages.</param>
    /// <returns>The cell text.</returns>
    public static string PerPage(IReadOnlyList<SvgPageStaves> pages)
    {
        if (pages == null || pages.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder text = new StringBuilder();
        foreach (SvgPageStaves page in pages)
        {
            if (text.Length > 0)
            {
                text.Append(',');
            }

            text.Append(page.Error != null ? "?" : page.Staves.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>Sums a per-page count list, treating an unreadable page as zero.</summary>
    /// <param name="pages">The pages.</param>
    /// <returns>The total.</returns>
    public static int Total(IReadOnlyList<SvgPageStaves> pages)
    {
        int total = 0;
        if (pages == null)
        {
            return 0;
        }

        foreach (SvgPageStaves page in pages)
        {
            total += page.Staves;
        }

        return total;
    }

    private static void ReadLines(string svgPath, List<HorizontalLine> lines, SvgPageStaves page)
    {
        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        double[] x = new double[64];
        double[] y = new double[64];
        bool[] warped = new bool[64];
        using XmlReader reader = XmlReader.Create(svgPath, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            int depth = reader.Depth;
            if (depth >= x.Length)
            {
                Array.Resize(ref x, depth * 2);
                Array.Resize(ref y, depth * 2);
                Array.Resize(ref warped, depth * 2);
            }

            double parentX = depth == 0 ? 0.0 : x[depth - 1];
            double parentY = depth == 0 ? 0.0 : y[depth - 1];
            bool parentWarped = depth != 0 && warped[depth - 1];
            string transform = reader.GetAttribute("transform");
            if (transform != null)
            {
                AddTranslations(transform, ref parentX, ref parentY, ref parentWarped);
            }

            x[depth] = parentX;
            y[depth] = parentY;
            warped[depth] = parentWarped;

            if (!string.Equals(reader.LocalName, "line", StringComparison.Ordinal))
            {
                continue;
            }

            if (parentWarped)
            {
                page.LinesUnderNonTranslate++;
                continue;
            }

            double x1 = Number(reader.GetAttribute("x1"));
            double y1 = Number(reader.GetAttribute("y1"));
            double x2 = Number(reader.GetAttribute("x2"));
            double y2 = Number(reader.GetAttribute("y2"));
            if (Math.Abs(y1 - y2) > Rules.FlatTolerance)
            {
                continue;
            }

            lines.Add(new HorizontalLine
            {
                Left = parentX + Math.Min(x1, x2),
                Right = parentX + Math.Max(x1, x2),
                Y = parentY + y1,
                StrokeWidth = reader.GetAttribute("stroke-width") ?? string.Empty,
            });
        }
    }

    private static void AddTranslations(string transform, ref double tx, ref double ty, ref bool warped)
    {
        // A transform list is walked left to right, summing every translate(). Anything else in
        // it (scale, rotate, matrix, skew) marks the subtree WARPED, and a <line> found there is
        // dropped rather than placed wrongly. On the 2026-08-27 corpus that happened zero times.
        int at = 0;
        while (at < transform.Length)
        {
            while (at < transform.Length && (transform[at] == ' ' || transform[at] == ',' || transform[at] == '\t'))
            {
                at++;
            }

            int nameStart = at;
            while (at < transform.Length && transform[at] != '(')
            {
                at++;
            }

            if (at >= transform.Length)
            {
                return;
            }

            string name = transform.Substring(nameStart, at - nameStart).Trim();
            int open = at + 1;
            int close = transform.IndexOf(')', open);
            if (close < 0)
            {
                return;
            }

            string arguments = transform.Substring(open, close - open);
            at = close + 1;
            if (!string.Equals(name, "translate", StringComparison.Ordinal))
            {
                warped = true;
                continue;
            }

            string[] parts = arguments.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                tx += Number(parts[0]);
            }

            if (parts.Length > 1)
            {
                ty += Number(parts[1]);
            }
        }
    }

    private static double Number(string text)
        => text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.0;

    private struct HorizontalLine
    {
        public double Left;
        public double Right;
        public double Y;
        public string StrokeWidth;
    }
}

/// <summary>What one SVG page's staff lines say.</summary>
public sealed class SvgPageStaves
{
    /// <summary>Gets or sets the number of staves on the page.</summary>
    public int Staves { get; set; }

    /// <summary>Gets or sets how many horizontal lines the page holds in all.</summary>
    public int HorizontalLines { get; set; }

    /// <summary>Gets or sets how many of those lines were claimed by a staff.</summary>
    public int LinesInStaves { get; set; }

    /// <summary>Gets or sets how many lines were dropped for sitting under a scale, rotate, matrix or skew.</summary>
    public int LinesUnderNonTranslate { get; set; }

    /// <summary>Gets or sets why the page could not be read, or null.</summary>
    public string Error { get; set; }
}

/// <summary>The port's staves against the oracle's, page by page.</summary>
public sealed class SvgStaffComparison
{
    /// <summary>Every compared page holds the same number of staves on both sides.</summary>
    public const string Equal = "SVG-STAVES-EQUAL";

    /// <summary>At least one page holds a different number of staves. This is what a PORT-GAP on the staff rung now means.</summary>
    public const string Differ = "SVG-STAVES-DIFFER";

    /// <summary>One side wrote no SVG page, so there is nothing to compare.</summary>
    public const string Unavailable = "SVG-STAVES-UNAVAILABLE";

    /// <summary>A page would not parse. No evidence either way; never a verdict.</summary>
    public const string Unreadable = "SVG-STAVES-UNREADABLE";

    /// <summary>The oracle was not run.</summary>
    public const string NoOracle = "SVG-STAVES-NO-ORACLE";

    /// <summary>Gets or sets the port's pages.</summary>
    public List<SvgPageStaves> Port { get; set; } = new List<SvgPageStaves>();

    /// <summary>Gets or sets the oracle's pages.</summary>
    public List<SvgPageStaves> Oracle { get; set; } = new List<SvgPageStaves>();

    /// <summary>Gets or sets the verdict.</summary>
    public string Verdict { get; set; }

    /// <summary>Gets or sets the pages that differ, as "p3:12/11 p7:10/11", or empty.</summary>
    public string DifferingPages { get; set; } = string.Empty;

    /// <summary>Gets or sets how many page pairs were compared.</summary>
    public int ComparedPages { get; set; }

    /// <summary>Gets or sets a note (why the rung could not be read).</summary>
    public string Note { get; set; }
}
