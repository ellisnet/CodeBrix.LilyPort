// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

//was previously: Fresco.Brix/src/libs/Fresco.Brix.MusicView/Export/ScorePdf.cs and the
//ReadPaperSize/ParseLength half of Fresco.Brix/src/libs/Fresco.Brix.MusicView/Pages/SvgPage.cs
//(copied, not linked — this tool must not reference the Fresco.Brix application folder; trimmed
//of ScorePage/CroppedPage/rotation, the SkiaSharp paper-colour parameter and the document-info
//post-pass, none of which a whole-page batch export needs).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CodeBrix.PdfDocCreate.Html2Pdf;
using CodeBrix.PdfDocCreate.Html2Pdf.Fonts;
using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.PdfDocuments.Pdf.IO;

namespace MutopiaProbe.Pdf;

/// <summary>
/// Turns the engine's SVG pages into one PDF: each page placed as VECTOR content on a PDF page
/// whose box is the SVG's own <c>width</c>/<c>height</c> (millimetres from the backend), the
/// font-family attributes rewritten to the engine's embedded faces first. Pages of one size are
/// rendered in a single Html2Pdf pass; mixed sizes are rendered one at a time and merged.
/// </summary>
public static class ScorePdfWriter
{
    /// <summary>Points per inch.</summary>
    public const double PointsPerInch = 72.0;

    private static readonly Regex FontFamilyAttribute = new Regex(
        "font-family=(\"[^\"]*\"|'[^']*')", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool fontsRegistered;

    /// <summary>Registers the engine's text faces with Html2Pdf once per process.</summary>
    /// <param name="fontDirectory">Where the faces are extracted to.</param>
    /// <returns>How many faces were registered.</returns>
    public static int RegisterFonts(string fontDirectory)
    {
        IReadOnlyList<string> files = LilyPortTextFonts.Extract(fontDirectory);
        if (!fontsRegistered)
        {
            Html2PdfFonts.AddFontFiles(files, false);
            fontsRegistered = true;
        }

        return files.Count;
    }

    /// <summary>Writes the PDF.</summary>
    /// <param name="fileName">The output path.</param>
    /// <param name="svgPages">The SVG pages, in order.</param>
    /// <param name="title">The document title, or null.</param>
    /// <param name="warnings">Receives Html2Pdf's warnings, or null.</param>
    /// <returns>The page count written, or 0 when there were no pages.</returns>
    public static int Write(string fileName, IReadOnlyList<string> svgPages, string title, IList<string> warnings)
    {
        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        byte[] bytes = ToBytes(svgPages, title, warnings);
        if (bytes == null)
        {
            return 0;
        }

        File.WriteAllBytes(fileName, bytes);
        return svgPages.Count;
    }

    /// <summary>Renders the PDF to bytes.</summary>
    /// <param name="svgPages">The SVG pages, in order.</param>
    /// <param name="title">The document title, or null.</param>
    /// <param name="warnings">Receives Html2Pdf's warnings, or null.</param>
    /// <returns>The PDF, or null when there were no pages.</returns>
    public static byte[] ToBytes(IReadOnlyList<string> svgPages, string title, IList<string> warnings)
    {
        if (svgPages == null)
        {
            throw new ArgumentNullException(nameof(svgPages));
        }

        List<PageSource> sources = new List<PageSource>();
        foreach (string page in svgPages)
        {
            if (page != null && File.Exists(page))
            {
                sources.Add(PageSource.For(page));
            }
        }

        if (sources.Count == 0)
        {
            return null;
        }

        string directory = Path.Combine(
            Path.GetTempPath(), "mutopia-probe-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].WriteSvg(Path.Combine(directory, "page-" + (i + 1) + ".svg"), LilyPortTextFonts.MapFamily);
            }

            return SameBox(sources)
                ? Render(sources, directory, sources[0].Width, sources[0].Height, title, warnings)
                : Merge(sources, directory, title, warnings);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Reads an SVG root's <c>width</c>/<c>height</c> as points; (0, 0) when absent.</summary>
    /// <param name="fileName">The SVG.</param>
    /// <returns>The size in points.</returns>
    public static (double Width, double Height) ReadPaperSizePoints(string fileName)
    {
        try
        {
            System.Xml.XmlReaderSettings settings = new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };
            using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(fileName, settings);
            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element)
                {
                    continue;
                }

                if (!string.Equals(reader.LocalName, "svg", StringComparison.Ordinal))
                {
                    return (0, 0);
                }

                return (ParseLengthPoints(reader.GetAttribute("width")), ParseLengthPoints(reader.GetAttribute("height")));
            }
        }
        catch (Exception exception) when (exception is IOException
            || exception is UnauthorizedAccessException || exception is System.Xml.XmlException)
        {
        }

        return (0, 0);
    }

    /// <summary>Parses an SVG length (mm, cm, in, pt, pc, px or unitless-as-px) to points.</summary>
    /// <param name="text">The attribute text.</param>
    /// <returns>Points, or 0 when unreadable.</returns>
    public static double ParseLengthPoints(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0.0;
        }

        string value = text.Trim();
        int end = value.Length;
        while (end > 0 && !(char.IsDigit(value[end - 1]) || value[end - 1] == '.'))
        {
            end--;
        }

        string unit = value.Substring(end).Trim().ToLowerInvariant();
        if (!double.TryParse(value.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return 0.0;
        }

        switch (unit)
        {
            case "mm": return number * PointsPerInch / 25.4;
            case "cm": return number * PointsPerInch / 2.54;
            case "in": return number * PointsPerInch;
            case "pt": return number;
            case "pc": return number * 12.0;
            case "px":
            case "": return number * PointsPerInch / 96.0;
            default: return 0.0;
        }
    }

    private static bool SameBox(List<PageSource> sources)
    {
        for (int i = 1; i < sources.Count; i++)
        {
            if (Math.Abs(sources[i].Width - sources[0].Width) > 0.01
                || Math.Abs(sources[i].Height - sources[0].Height) > 0.01)
            {
                return false;
            }
        }

        return true;
    }

    private static HtmlPdfRenderer Renderer(double width, double height, string title)
    {
        HtmlPdfRenderer renderer = new HtmlPdfRenderer();
        HtmlRenderOptions options = renderer.Options;
        options.PageWidthPoints = width;
        options.PageHeightPoints = height;
        options.Landscape = false;
        options.MarginTopPoints = 0;
        options.MarginRightPoints = 0;
        options.MarginBottomPoints = 0;
        options.MarginLeftPoints = 0;
        options.HeaderText = null;
        options.FooterText = null;
        options.GenerateOutline = false;
        options.DocumentTitle = string.IsNullOrEmpty(title) ? null : title;
        options.SvgPlacement = SvgPlacementMode.Vector;
        options.KeepUncoveredCharacters = true;
        options.CffSubsetMode = PdfCffSubsetMode.Sparse;
        return renderer;
    }

    private static string Html(IEnumerable<(string File, double Width, double Height)> images)
    {
        StringBuilder html = new StringBuilder();
        html.Append("<html><head><style>body{margin:0;padding:0}img{display:block;margin:0;padding:0}</style></head><body>");
        foreach ((string file, double width, double height) in images)
        {
            string size = "width:" + Points(width) + "pt;height:" + Points(height) + "pt";
            html.Append("<img src=\"").Append(file).Append("\" style=\"").Append(size).Append("\">");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private static byte[] Render(
        List<PageSource> sources, string directory, double width, double height, string title, IList<string> warnings)
    {
        List<(string, double, double)> images = new List<(string, double, double)>();
        for (int i = 0; i < sources.Count; i++)
        {
            images.Add(("page-" + (i + 1) + ".svg", sources[i].Width, sources[i].Height));
        }

        HtmlPdfRenderer renderer = Renderer(width, height, title);
        HtmlRenderResult result = renderer.RenderHtmlToBytes(Html(images), directory);
        Collect(result, warnings);
        return result.PdfBytes;
    }

    private static byte[] Merge(List<PageSource> sources, string directory, string title, IList<string> warnings)
    {
        using PdfDocument output = new PdfDocument();
        for (int i = 0; i < sources.Count; i++)
        {
            PageSource source = sources[i];
            HtmlPdfRenderer renderer = Renderer(source.Width, source.Height, title);
            (string, double, double)[] images = { ("page-" + (i + 1) + ".svg", source.Width, source.Height) };
            HtmlRenderResult result = renderer.RenderHtmlToBytes(Html(images), directory);
            Collect(result, warnings);
            using MemoryStream stream = new MemoryStream(result.PdfBytes);
            PdfDocument one = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            for (int p = 0; p < one.PageCount; p++)
            {
                output.AddPage(one.Pages[p]);
            }
        }

        using MemoryStream memory = new MemoryStream();
        output.Save(memory);
        return memory.ToArray();
    }

    private static void Collect(HtmlRenderResult result, IList<string> warnings)
    {
        if (warnings == null || result?.Warnings == null)
        {
            return;
        }

        foreach (RenderWarning warning in result.Warnings.Items)
        {
            warnings.Add(warning.Code + ": " + warning.Message
                + (warning.Occurrences > 1 ? " (x" + warning.Occurrences + ")" : string.Empty));
        }
    }

    private static string Points(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed class PageSource
    {
        private PageSource()
        {
        }

        public string FileName { get; private set; }

        public double Width { get; private set; }

        public double Height { get; private set; }

        public static PageSource For(string svgFile)
        {
            (double width, double height) = ReadPaperSizePoints(svgFile);
            if (width <= 0 || height <= 0)
            {
                // A4, the engine's default paper, when the root carries no usable size.
                width = 595.276;
                height = 841.89;
            }

            return new PageSource { FileName = svgFile, Width = width, Height = height };
        }

        public void WriteSvg(string path, Func<string, string> familyMapper)
        {
            string text = File.ReadAllText(FileName);
            if (familyMapper != null)
            {
                text = FontFamilyAttribute.Replace(text, match =>
                {
                    string quoted = match.Groups[1].Value;
                    string value = quoted.Substring(1, quoted.Length - 2);
                    string mapped = familyMapper(value);
                    return mapped == null ? match.Value : "font-family=\"" + mapped + "\"";
                });
            }

            File.WriteAllText(path, text);
        }
    }
}
