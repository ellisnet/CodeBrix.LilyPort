// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.PdfRasterizer;

namespace MutopiaProbe.Compare;

/// <summary>
/// One PDF page as 8-bit grey through PDFium, and the measurements the ink grade reads off it.
/// A pixel is INK when it is darker than <see cref="InkThreshold"/> — deliberately generous,
/// because at the grading resolution a staff line is thinner than a pixel and renders as
/// mid-grey, and a threshold that missed staff lines would count no staves.
/// </summary>
public sealed class PageBitmap
{
    /// <summary>The grey level below which a pixel is ink.</summary>
    public const byte InkThreshold = 200;

    private PageBitmap(int width, int height, byte[] grey)
    {
        Width = width;
        Height = height;
        Grey = grey;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the grey levels, row-major.</summary>
    public byte[] Grey { get; }

    /// <summary>Rasterises one page.</summary>
    /// <param name="rasterizer">The rasterizer.</param>
    /// <param name="pdfBytes">The PDF.</param>
    /// <param name="pageNumber">The 1-based page.</param>
    /// <param name="dpi">The resolution.</param>
    /// <param name="pngPath">Where to also save the page as PNG, or null.</param>
    /// <returns>The bitmap.</returns>
    public static PageBitmap Rasterize(PageRasterizer rasterizer, byte[] pdfBytes, int pageNumber, int dpi, string pngPath)
    {
        using Image image = rasterizer.RasterizeToImage(pdfBytes, pageNumber, dpi).GetAwaiter().GetResult();
        if (pngPath != null)
        {
            image.SaveAsPng(pngPath);
        }

        using Image<L8> grey = image.CloneAs<L8>();
        byte[] data = new byte[grey.Width * grey.Height];
        grey.CopyPixelDataTo(data);
        return new PageBitmap(grey.Width, grey.Height, data);
    }

    /// <summary>Gets the fraction of pixels that are ink.</summary>
    /// <returns>The fraction in [0, 1].</returns>
    public double InkRatio()
    {
        long ink = 0;
        foreach (byte value in Grey)
        {
            if (value < InkThreshold)
            {
                ink++;
            }
        }

        return Grey.Length == 0 ? 0.0 : (double)ink / Grey.Length;
    }

    /// <summary>
    /// Counts the staves FROM THE RASTER. ⚠ INFORMATIONAL ONLY since 2026-08-28: this number
    /// moves with the grading resolution (a checked pair reversed sign between 100 and 200 dpi)
    /// and with the PDF writer's font coverage (four newly-drawn footer glyphs once added a
    /// phantom staff), so it decides no verdict any more. <see cref="SvgStaves"/> reads the count
    /// off the SVG structure instead. It stays here because it is the only staff signal available
    /// against MUTOPIA, which published a PDF and no SVG.
    /// <para>
    /// Rows more than <paramref name="rowFraction"/> ink are staff-line rows,
    /// adjacent line rows are one line, and lines closer together than 1.8× the median line gap
    /// belong to one staff. A staff is a group of four or more lines (five for a staff, six for
    /// tablature; four allows for one line lost to anti-aliasing).
    /// </para>
    /// </summary>
    /// <param name="rowFraction">The ink fraction a row needs to count as a staff line.</param>
    /// <returns>The staff count.</returns>
    public int StaffCount(double rowFraction)
    {
        List<int> lineRows = new List<int>();
        int threshold = (int)(Width * rowFraction);
        for (int y = 0; y < Height; y++)
        {
            int ink = 0;
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                if (Grey[row + x] < InkThreshold)
                {
                    ink++;
                }
            }

            if (ink >= threshold)
            {
                lineRows.Add(y);
            }
        }

        // Adjacent rows are one line.
        List<double> lines = new List<double>();
        int start = -1;
        int previous = -10;
        foreach (int y in lineRows)
        {
            if (y != previous + 1)
            {
                if (start >= 0)
                {
                    lines.Add((start + previous) / 2.0);
                }

                start = y;
            }

            previous = y;
        }

        if (start >= 0)
        {
            lines.Add((start + previous) / 2.0);
        }

        if (lines.Count < 4)
        {
            return 0;
        }

        List<double> gaps = new List<double>();
        for (int i = 1; i < lines.Count; i++)
        {
            gaps.Add(lines[i] - lines[i - 1]);
        }

        gaps.Sort();
        double median = gaps[gaps.Count / 2];
        double limit = median * 1.8;

        int staves = 0;
        int inGroup = 1;
        for (int i = 1; i < lines.Count; i++)
        {
            if (lines[i] - lines[i - 1] <= limit)
            {
                inGroup++;
            }
            else
            {
                if (inGroup >= 4)
                {
                    staves++;
                }

                inGroup = 1;
            }
        }

        if (inGroup >= 4)
        {
            staves++;
        }

        return staves;
    }

    /// <summary>
    /// Ink density on a grid of <paramref name="columns"/> cells across (rows follow the aspect
    /// ratio), so two pages of different pixel sizes can still be compared cell by cell.
    /// </summary>
    /// <param name="columns">Cells across.</param>
    /// <returns>The densities, row-major, with the grid's row count.</returns>
    public (double[] Cells, int Rows) Density(int columns)
    {
        int rows = Math.Max(1, (int)Math.Round((double)columns * Height / Width));
        double[] cells = new double[columns * rows];
        int[] counts = new int[columns * rows];
        for (int y = 0; y < Height; y++)
        {
            int cy = Math.Min(rows - 1, y * rows / Height);
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int cx = Math.Min(columns - 1, x * columns / Width);
                int index = cy * columns + cx;
                counts[index]++;
                if (Grey[row + x] < InkThreshold)
                {
                    cells[index] += 1.0;
                }
            }
        }

        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = counts[i] == 0 ? 0.0 : cells[i] / counts[i];
        }

        return (cells, rows);
    }

    /// <summary>
    /// The ink intersection-over-union of two same-sized pages: 1 when the ink coincides, 0 when
    /// none of it does. Null when the sizes differ.
    /// </summary>
    /// <param name="other">The other page.</param>
    /// <returns>The IoU, or null.</returns>
    public double? InkIoU(PageBitmap other)
    {
        if (other.Width != Width || other.Height != Height)
        {
            return null;
        }

        long union = 0;
        long intersection = 0;
        for (int i = 0; i < Grey.Length; i++)
        {
            bool a = Grey[i] < InkThreshold;
            bool b = other.Grey[i] < InkThreshold;
            if (a || b)
            {
                union++;
            }

            if (a && b)
            {
                intersection++;
            }
        }

        return union == 0 ? 1.0 : (double)intersection / union;
    }

    /// <summary>
    /// The cell-density difference of two pages: Σ|a − b| / max(Σa, Σb), 0 when the ink is
    /// distributed identically and about 1 when it is distributed entirely differently.
    /// </summary>
    /// <param name="other">The other page.</param>
    /// <param name="columns">Cells across.</param>
    /// <returns>The difference, clamped to [0, 1].</returns>
    public double BlockDifference(PageBitmap other, int columns)
    {
        (double[] a, int rowsA) = Density(columns);
        (double[] b, int rowsB) = other.Density(columns);
        int rows = Math.Min(rowsA, rowsB);
        double sumA = 0;
        double sumB = 0;
        double difference = 0;
        for (int r = 0; r < Math.Max(rowsA, rowsB); r++)
        {
            for (int c = 0; c < columns; c++)
            {
                double va = r < rowsA ? a[r * columns + c] : 0.0;
                double vb = r < rowsB ? b[r * columns + c] : 0.0;
                sumA += va;
                sumB += vb;
                difference += Math.Abs(va - vb);
            }
        }

        double scale = Math.Max(sumA, sumB);
        if (scale <= 0)
        {
            return 0.0;
        }

        return Math.Min(1.0, difference / scale);
    }
}
