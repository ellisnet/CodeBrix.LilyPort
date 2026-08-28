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
using CodeBrix.PdfRasterizer;

namespace MutopiaProbe.Compare;

/// <summary>
/// Grades the port's PDF against Mutopia's, coarse to fine, and reports EVERY rung rather than
/// stopping at the first: page count, page size, the words on the page, and the ink.
/// </summary>
public sealed class PdfComparison
{
    /// <summary>The ink thresholds, calibrated on the first pairs — see the README's CALIBRATION section.</summary>
    public static class Thresholds
    {
        /// <summary>Block difference at or below which two pages are SIMILAR (calibrated 2026-08-27, see the README).</summary>
        public static double Similar = 0.25;

        /// <summary>Block difference at or below which two pages are LAYOUT-DIFFERS; above is VERY-DIFFERENT.</summary>
        public static double LayoutDiffers = 0.60;

        /// <summary>Text similarity at or above which the words are NEAR.</summary>
        public static double TextNear = 0.90;

        /// <summary>The ink fraction of a row that makes it a staff-line row.</summary>
        public static double StaffRow = 0.30;

        /// <summary>The resolution pages are graded at.</summary>
        public static int Dpi = 100;

        /// <summary>
        /// Cells across for the block difference. EIGHT, not more: at 100 dpi an A4 page is
        /// 827 px wide, so a cell is ~103 px ≈ 26 mm — about three staves tall. A finer grid
        /// (24 across was tried first) is finer than the vertical drift between two
        /// engravings of the same systems, and reads a staff that moved a few millimetres
        /// as entirely different ink: Canon in D graded 1.00 at 24 columns and 0.48 at 8.
        /// </summary>
        public static int Columns = 8;

        /// <summary>How many page pairs are saved as PNG for eyeballing.</summary>
        public static int PngPages = 2;
    }

    /// <summary>Gets or sets the port PDF's page count, or -1 when unreadable.</summary>
    public int PortPages { get; set; } = -1;

    /// <summary>Gets or sets the reference PDF's page count, or -1 when unreadable.</summary>
    public int ReferencePages { get; set; } = -1;

    /// <summary>Gets or sets the page-count verdict: PAGES-EQUAL, PAGES-DIFFER, PORT-UNREADABLE, REF-UNREADABLE.</summary>
    public string PageCountVerdict { get; set; }

    /// <summary>Gets or sets the port's first-page size in points, as "WxH".</summary>
    public string PortPageSize { get; set; }

    /// <summary>Gets or sets the reference's first-page size in points, as "WxH".</summary>
    public string ReferencePageSize { get; set; }

    /// <summary>Gets or sets the page-size verdict: SIZE-EQUAL or SIZE-DIFFERS (within 1 pt).</summary>
    public string PageSizeVerdict { get; set; }

    /// <summary>Gets or sets the text verdict: TEXT-EQUAL, TEXT-NEAR, TEXT-DIFFERS, TEXT-NONE, TEXT-REF-EMPTY, TEXT-PORT-EMPTY, TEXT-UNAVAILABLE.</summary>
    public string TextVerdict { get; set; }

    /// <summary>Gets or sets the text similarity in [0, 1].</summary>
    public double TextSimilarity { get; set; }

    /// <summary>Gets or sets the port's token count.</summary>
    public int PortTokens { get; set; }

    /// <summary>Gets or sets the reference's token count.</summary>
    public int ReferenceTokens { get; set; }

    /// <summary>Gets or sets the ink verdict: SIMILAR, LAYOUT-DIFFERS, VERY-DIFFERENT, or INK-SKIPPED.</summary>
    public string InkVerdict { get; set; }

    /// <summary>Gets or sets the mean block difference over the compared pages.</summary>
    public double BlockDifference { get; set; }

    /// <summary>Gets or sets the mean ink IoU over the compared pages, or -1 when sizes differed.</summary>
    public double InkIoU { get; set; } = -1;

    /// <summary>Gets or sets the port's mean ink ratio.</summary>
    public double PortInk { get; set; }

    /// <summary>Gets or sets the reference's mean ink ratio.</summary>
    public double ReferenceInk { get; set; }

    /// <summary>Gets or sets the port's staff count summed over compared pages.</summary>
    public int PortStaves { get; set; }

    /// <summary>Gets or sets the reference's staff count summed over compared pages.</summary>
    public int ReferenceStaves { get; set; }

    /// <summary>Gets or sets the staff-count verdict: STAVES-EQUAL or STAVES-DIFFER over the compared pages.</summary>
    public string StavesVerdict { get; set; }

    /// <summary>Gets or sets the ordered text containment: LCS over the shorter side's length.</summary>
    public double TextContainment { get; set; }

    /// <summary>Gets or sets the bag-of-words containment — the number the verdict is cut on.</summary>
    public double TextBag { get; set; }

    /// <summary>Gets or sets how many page pairs were compared.</summary>
    public int ComparedPages { get; set; }

    /// <summary>Gets or sets a note (why something was skipped).</summary>
    public string Note { get; set; }

    /// <summary>Grades a pair.</summary>
    /// <param name="portPdf">The port's PDF, or null when none was produced.</param>
    /// <param name="referencePdf">Mutopia's PDF.</param>
    /// <param name="pngDirectory">Where page PNGs go, or null for none.</param>
    /// <returns>The comparison.</returns>
    public static PdfComparison Grade(string portPdf, string referencePdf, string pngDirectory)
    {
        PdfComparison comparison = new PdfComparison();
        using PageRasterizer rasterizer = new PageRasterizer();

        byte[] referenceBytes = SafeRead(referencePdf);
        byte[] portBytes = portPdf == null ? null : SafeRead(portPdf);

        comparison.ReferencePages = PageCount(rasterizer, referenceBytes);
        comparison.PortPages = portBytes == null ? -1 : PageCount(rasterizer, portBytes);

        if (comparison.ReferencePages < 0)
        {
            comparison.PageCountVerdict = "REF-UNREADABLE";
        }
        else if (comparison.PortPages < 0)
        {
            comparison.PageCountVerdict = portBytes == null ? "PORT-MISSING" : "PORT-UNREADABLE";
        }
        else
        {
            comparison.PageCountVerdict = comparison.PortPages == comparison.ReferencePages ? "PAGES-EQUAL" : "PAGES-DIFFER";
        }

        (double rw, double rh) = comparison.ReferencePages > 0 ? PageSize(rasterizer, referenceBytes) : (0, 0);
        (double pw, double ph) = comparison.PortPages > 0 ? PageSize(rasterizer, portBytes) : (0, 0);
        comparison.ReferencePageSize = Size(rw, rh);
        comparison.PortPageSize = Size(pw, ph);
        if (comparison.ReferencePages > 0 && comparison.PortPages > 0)
        {
            comparison.PageSizeVerdict = Math.Abs(rw - pw) <= 1.0 && Math.Abs(rh - ph) <= 1.0 ? "SIZE-EQUAL" : "SIZE-DIFFERS";
        }
        else
        {
            comparison.PageSizeVerdict = "SIZE-SKIPPED";
        }

        GradeText(comparison, portPdf, referencePdf);

        if (comparison.ReferencePages > 0 && comparison.PortPages > 0)
        {
            GradeInk(comparison, rasterizer, portBytes, referenceBytes, pngDirectory);
        }
        else
        {
            comparison.InkVerdict = "INK-SKIPPED";
        }

        return comparison;
    }

    private static void GradeText(PdfComparison comparison, string portPdf, string referencePdf)
    {
        if (comparison.ReferencePages <= 0 || comparison.PortPages <= 0)
        {
            comparison.TextVerdict = "TEXT-SKIPPED";
            return;
        }

        List<string> reference = PdfText.Tokens(referencePdf, out string referenceError);
        List<string> port = PdfText.Tokens(portPdf, out string portError);
        if (reference == null || port == null)
        {
            comparison.TextVerdict = "TEXT-UNAVAILABLE";
            comparison.Note = Append(comparison.Note, referenceError ?? portError);
            return;
        }

        comparison.ReferenceTokens = reference.Count;
        comparison.PortTokens = port.Count;
        if (reference.Count == 0 && port.Count == 0)
        {
            comparison.TextVerdict = "TEXT-NONE";
            comparison.TextSimilarity = 1.0;
            return;
        }

        if (reference.Count == 0)
        {
            // Pre-2.10 references embed their text as glyph outlines pdftotext cannot read.
            comparison.TextVerdict = "TEXT-REF-EMPTY";
            return;
        }

        if (port.Count == 0)
        {
            comparison.TextVerdict = "TEXT-PORT-EMPTY";
            return;
        }

        comparison.TextSimilarity = PdfText.Similarity(reference, port, 6000, out bool truncated, out double containment);
        comparison.TextContainment = containment;
        if (truncated)
        {
            comparison.Note = Append(comparison.Note, "text truncated to 6000 tokens per side");
        }

        // The verdict is cut on BAG CONTAINMENT, not symmetric similarity and not the ordered
        // LCS: Mutopia's references do not expose their footer to pdftotext (it comes out as
        // nothing) while the port's do, so the symmetric ratio is capped by that asymmetry on
        // every row; and pdftotext reads two verses of lyrics in whatever order their baselines
        // fall, so the ordered LCS punished every reflowed lyric page (guitar-duo: 0.64 ordered,
        // 0.99 as a bag, with 698 vs 711 words). The bag asks whether the words extractable on
        // BOTH sides agree; the other two numbers and the token counts stay beside it.
        comparison.TextBag = PdfText.BagContainment(reference, port);
        comparison.TextVerdict = comparison.TextSimilarity >= 0.9999 ? "TEXT-EQUAL"
            : comparison.TextBag >= Thresholds.TextNear ? "TEXT-NEAR"
            : "TEXT-DIFFERS";
    }

    private static void GradeInk(
        PdfComparison comparison, PageRasterizer rasterizer, byte[] portBytes, byte[] referenceBytes, string pngDirectory)
    {
        int pages = Math.Min(comparison.PortPages, comparison.ReferencePages);
        double blockSum = 0;
        double iouSum = 0;
        int iouCount = 0;
        double portInk = 0;
        double referenceInk = 0;
        if (pngDirectory != null)
        {
            Directory.CreateDirectory(pngDirectory);
        }

        for (int page = 1; page <= pages; page++)
        {
            string portPng = pngDirectory != null && page <= Thresholds.PngPages
                ? Path.Combine(pngDirectory, "port-" + page + ".png") : null;
            string referencePng = pngDirectory != null && page <= Thresholds.PngPages
                ? Path.Combine(pngDirectory, "ref-" + page + ".png") : null;
            PageBitmap port = PageBitmap.Rasterize(rasterizer, portBytes, page, Thresholds.Dpi, portPng);
            PageBitmap reference = PageBitmap.Rasterize(rasterizer, referenceBytes, page, Thresholds.Dpi, referencePng);
            blockSum += port.BlockDifference(reference, Thresholds.Columns);
            double? iou = port.InkIoU(reference);
            if (iou != null)
            {
                iouSum += iou.Value;
                iouCount++;
            }

            portInk += port.InkRatio();
            referenceInk += reference.InkRatio();
            comparison.PortStaves += port.StaffCount(Thresholds.StaffRow);
            comparison.ReferenceStaves += reference.StaffCount(Thresholds.StaffRow);
        }

        comparison.ComparedPages = pages;
        comparison.BlockDifference = pages == 0 ? 0 : blockSum / pages;
        comparison.InkIoU = iouCount == 0 ? -1 : iouSum / iouCount;
        comparison.PortInk = pages == 0 ? 0 : portInk / pages;
        comparison.ReferenceInk = pages == 0 ? 0 : referenceInk / pages;
        comparison.StavesVerdict = comparison.PortStaves == comparison.ReferenceStaves ? "STAVES-EQUAL" : "STAVES-DIFFER";
        comparison.InkVerdict = comparison.BlockDifference <= Thresholds.Similar ? "SIMILAR"
            : comparison.BlockDifference <= Thresholds.LayoutDiffers ? "LAYOUT-DIFFERS"
            : "VERY-DIFFERENT";
    }

    private static byte[] SafeRead(string path)
    {
        try
        {
            return path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static int PageCount(PageRasterizer rasterizer, byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return -1;
        }

        try
        {
            return rasterizer.GetPageCount(bytes).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static (double Width, double Height) PageSize(PageRasterizer rasterizer, byte[] bytes)
    {
        try
        {
            PdfPageDimensions dimensions = rasterizer.GetPageDimensions(bytes, pageNumber: 1).GetAwaiter().GetResult();
            return (dimensions.WidthInInches * 72.0, dimensions.HeightInInches * 72.0);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private static string Size(double width, double height)
        => width <= 0 || height <= 0
            ? "-"
            : width.ToString("0.#", CultureInfo.InvariantCulture) + "x" + height.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Append(string note, string more)
        => string.IsNullOrEmpty(more) ? note : string.IsNullOrEmpty(note) ? more : note + "; " + more;
}
