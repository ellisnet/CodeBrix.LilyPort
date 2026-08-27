// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

//was previously: Fresco.Brix/src/Fresco.Brix.Core/MusicView/LilyPortScorePdfFonts.cs and the
//Families/Generics tables of Fresco.Brix/src/libs/Fresco.Brix.MusicView/LilyPortTypefaceResolver.cs
//(copied, not linked — this tool must not reference the Fresco.Brix application folder; trimmed
//of the SkiaSharp typeface cache, which the tool has no use for).

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Engine.Fonts;

namespace MutopiaProbe.Pdf;

/// <summary>
/// The engine's own text faces, extracted from its embedded assets for Html2Pdf to embed, and the
/// family map the SVG pages need: the SVG backend names the CSS generics (<c>serif</c>,
/// <c>sans</c>, <c>monospace</c>) or upstream's virtual names (<c>LilyPond Serif</c>, ...), and
/// Html2Pdf resolves a family by the name IN THE FONT FILE — C059, Nimbus Sans, Nimbus Mono PS,
/// TeX Gyre Schola. Without the rewrite every piece of text falls to Html2Pdf's own chain and the
/// PDF says out loud that it embedded Merriweather instead.
/// </summary>
public static class LilyPortTextFonts
{
    private static readonly Dictionary<string, string[]> Families
        = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["serif"] = new[] { "C059-Roman.otf", "C059-Bold.otf", "C059-Italic.otf", "C059-BdIta.otf" },
            ["sans"] = new[]
            {
                "NimbusSans-Regular.otf", "NimbusSans-Bold.otf",
                "NimbusSans-Italic.otf", "NimbusSans-BoldItalic.otf",
            },
            ["typewriter"] = new[]
            {
                "NimbusMonoPS-Regular.otf", "NimbusMonoPS-Bold.otf",
                "NimbusMonoPS-Italic.otf", "NimbusMonoPS-BoldItalic.otf",
            },
            ["unknown"] = new[]
            {
                "texgyreschola-regular.otf", "texgyreschola-bold.otf",
                "texgyreschola-italic.otf", "texgyreschola-bolditalic.otf",
            },
        };

    private static readonly Dictionary<string, string> Generics
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["serif"] = "serif",
            ["sans"] = "sans",
            ["sans-serif"] = "sans",
            ["monospace"] = "typewriter",
            ["LilyPond Serif"] = "serif",
            ["LilyPond Sans Serif"] = "sans",
            ["LilyPond Monospace"] = "typewriter",
            ["C059"] = "serif",
            ["Nimbus Sans"] = "sans",
            ["Nimbus Mono PS"] = "typewriter",
            ["TeX Gyre Schola"] = "unknown",
        };

    private static readonly Dictionary<string, string> ChainFamilies
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["serif"] = "C059",
            ["sans"] = "Nimbus Sans",
            ["typewriter"] = "Nimbus Mono PS",
            ["unknown"] = "TeX Gyre Schola",
        };

    /// <summary>Gets the file names of every face the table can hand out.</summary>
    public static IReadOnlyList<string> FaceFileNames
    {
        get
        {
            List<string> names = new List<string>();
            foreach (string[] faces in Families.Values)
            {
                names.AddRange(faces);
            }

            return names;
        }
    }

    /// <summary>Normalises a family name the SVG may carry to one of the four categories.</summary>
    /// <param name="familyName">The name as written in the SVG.</param>
    /// <returns><c>serif</c>, <c>sans</c>, <c>typewriter</c> or <c>unknown</c>.</returns>
    public static string Normalize(string familyName)
    {
        if (familyName != null && Generics.TryGetValue(familyName.Trim(), out string category))
        {
            return category;
        }

        return "unknown";
    }

    /// <summary>Maps a family name the SVG carries to the name Html2Pdf will find in the font file.</summary>
    /// <param name="familyName">The name as written in the SVG.</param>
    /// <returns>The embedded family's name.</returns>
    public static string MapFamily(string familyName) => ChainFamilies[Normalize(familyName)];

    /// <summary>
    /// Writes the engine's text faces into <paramref name="directory"/> (only when absent or of a
    /// different size) and returns their paths.
    /// </summary>
    /// <param name="directory">Where the files go.</param>
    /// <returns>The extracted files.</returns>
    public static IReadOnlyList<string> Extract(string directory)
    {
        Directory.CreateDirectory(directory);
        List<string> files = new List<string>();
        foreach (string fileName in FaceFileNames)
        {
            byte[] bytes = FontAssets.TextFont(fileName);
            if (bytes == null)
            {
                continue;
            }

            string path = Path.Combine(directory, fileName);
            FileInfo existing = new FileInfo(path);
            if (!existing.Exists || existing.Length != bytes.Length)
            {
                File.WriteAllBytes(path, bytes);
            }

            files.Add(path);
        }

        return files;
    }
}
