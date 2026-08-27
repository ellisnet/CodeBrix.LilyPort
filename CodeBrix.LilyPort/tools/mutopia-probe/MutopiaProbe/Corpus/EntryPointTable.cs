// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;

namespace MutopiaProbe.Corpus;

/// <summary>
/// Reads the corpus's <c>ENTRY_POINTS.tsv</c> (columns: <c>path</c>, <c>pdf</c>, <c>mid</c>,
/// <c>source_ly</c>). Rows whose <c>source_ly</c> is blank are kept so the sweep can LIST them
/// as skipped rather than silently drop them — the corpus README says 29 reference PDFs have
/// no stem match, and a sweep that reported "all done" over 199 rows would hide that.
/// </summary>
public static class EntryPointTable
{
    /// <summary>Reads the table.</summary>
    /// <param name="corpusRoot">The <c>pieces/</c> directory of the corpus.</param>
    /// <returns>The rows, in file order.</returns>
    public static List<EntryPoint> Read(string corpusRoot)
    {
        string path = Path.Combine(corpusRoot, "ENTRY_POINTS.tsv");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The corpus has no ENTRY_POINTS.tsv.", path);
        }

        List<EntryPoint> rows = new List<EntryPoint>();
        bool first = true;
        foreach (string line in File.ReadLines(path))
        {
            if (first)
            {
                first = false;
                if (line.StartsWith("path\t", StringComparison.Ordinal))
                {
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] cells = line.Split('\t');
            if (cells.Length < 2)
            {
                continue;
            }

            string mid = cells.Length > 2 ? cells[2].Trim() : null;
            string source = cells.Length > 3 ? cells[3].Trim() : null;
            rows.Add(new EntryPoint(cells[0].Trim(), cells[1].Trim(), mid, source));
        }

        return rows;
    }
}
