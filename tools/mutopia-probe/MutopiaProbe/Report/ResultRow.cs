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

namespace MutopiaProbe.Report;

/// <summary>
/// One line of <c>results.tsv</c>: an ordered list of named cells, so the header and the rows
/// cannot drift apart and a reader can add a column without renumbering anything.
/// </summary>
public sealed class ResultRow
{
    private readonly List<KeyValuePair<string, string>> cells = new List<KeyValuePair<string, string>>();

    /// <summary>The column names, in order.</summary>
    public static readonly string[] Columns =
    {
        "key", "piece", "stem", "source_ly", "declared_version", "convert", "convert_rules", "convert_messages",
        "engraved_from", "engrave", "parse_errors", "systems", "svg_pages", "midi_files", "side_files",
        "pdf", "pdf_warnings", "pages_port", "pages_ref", "page_count", "size_port", "size_ref", "page_size",
        "text", "text_bag", "text_sim", "text_contain", "tokens_port", "tokens_ref",
        //was previously: "staves", "staves_port", "staves_ref" — renamed 2026-08-28 so that no
        //reader can mistake the RASTER count, which now decides nothing, for the SVG-structure
        //count in the svg_staves* columns, which is the rung the verdict is cut on.
        "ink", "block_diff", "ink_iou", "ink_port", "ink_ref",
        "raster_staves", "raster_staves_port", "raster_staves_ref", "compared_pages",
        "midi", "midi_channel", "midi_notes", "midi_pitches", "midi_tracks_port", "midi_tracks_ref", "midi_div_port", "midi_div_ref",
        "midi_notes_port", "midi_notes_ref", "midi_len_port", "midi_len_ref", "midi_tempos_port", "midi_tempos_ref",
        "midi_programs_port", "midi_programs_ref", "midi_stamp_ref", "midi_first_diff", "midi_channel_first_diff",
        "oracle", "oracle_seconds", "oracle_errors", "oracle_warnings", "oracle_pages", "oracle_midi_files",
        "o_page_count", "o_page_size", "o_text", "o_text_bag", "o_ink", "o_block_diff",
        //was previously: "o_staves", "o_staves_oracle". Renamed, and o_raster_staves_port added:
        //the old table held the port's raster count only for the MUTOPIA comparison (over
        //min(port, Mutopia) pages) while o_staves_oracle counted the ORACLE over min(port,
        //oracle) pages, so the two numbers a reader naturally paired were measured over
        //different page sets. That is what made Mendelssohn_Octet_-_Viola_1 look like a
        //fifteen-staff disagreement (106 over 10 pages against 121 over 11).
        "o_raster_staves", "o_raster_staves_port", "o_raster_staves_oracle",
        // The staff rung the verdict is cut on: counted from the SVG structure, per page.
        "svg_staves", "svg_staves_port", "svg_staves_oracle",
        "svg_staves_by_page_port", "svg_staves_by_page_oracle", "svg_staves_diff_pages",
        "o_midi", "o_midi_channel", "o_midi_notes", "o_midi_pitches", "o_midi_first_diff",
        "verdict", "verdict_pdf", "verdict_midi",
        "seconds", "error", "note",
    };

    /// <summary>Creates a row with every column blank.</summary>
    public ResultRow()
    {
        foreach (string column in Columns)
        {
            cells.Add(new KeyValuePair<string, string>(column, string.Empty));
        }
    }

    /// <summary>Gets or sets a cell by column name.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The cell text.</returns>
    public string this[string column]
    {
        get
        {
            int index = IndexOf(column);
            return cells[index].Value;
        }

        set
        {
            int index = IndexOf(column);
            cells[index] = new KeyValuePair<string, string>(column, Clean(value));
        }
    }

    /// <summary>Sets a numeric cell.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value.</param>
    public void Set(string column, int value) => this[column] = value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Sets a numeric cell.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value.</param>
    public void Set(string column, long value) => this[column] = value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Sets a numeric cell to three decimals.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value.</param>
    public void Set(string column, double value) => this[column] = value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Gets the header line.</summary>
    /// <returns>The tab-separated header.</returns>
    public static string Header() => string.Join("\t", Columns);

    /// <summary>Gets the row as a line.</summary>
    /// <returns>The tab-separated row.</returns>
    public string Line()
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\t');
            }

            builder.Append(cells[i].Value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads an existing <c>results.tsv</c> onto fresh rows, matching cells BY COLUMN NAME. A
    /// column the file has and this build does not is dropped, and a column this build has and
    /// the file does not stays blank — which is what lets <c>--regrade</c> read a table written
    /// before a column was renamed and re-fill the columns it recomputes.
    /// </summary>
    /// <param name="path">The results file.</param>
    /// <returns>The rows, in file order.</returns>
    public static List<ResultRow> Read(string path)
    {
        List<ResultRow> rows = new List<ResultRow>();
        string[] header = null;
        foreach (string line in File.ReadLines(path))
        {
            string[] cells = line.Split('\t');
            if (header == null)
            {
                header = cells;
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            ResultRow row = new ResultRow();
            for (int i = 0; i < header.Length && i < cells.Length; i++)
            {
                if (Array.IndexOf(Columns, header[i]) >= 0)
                {
                    row[header[i]] = cells[i];
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Reads the keys already present in a results file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The keys.</returns>
    public static HashSet<string> ExistingKeys(string path)
    {
        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return keys;
        }

        bool first = true;
        foreach (string line in File.ReadLines(path))
        {
            if (first)
            {
                first = false;
                continue;
            }

            int tab = line.IndexOf('\t');
            if (tab > 0)
            {
                keys.Add(line.Substring(0, tab));
            }
        }

        return keys;
    }

    private static int IndexOf(string column)
    {
        int index = Array.IndexOf(Columns, column);
        if (index < 0)
        {
            throw new ArgumentException("no column named " + column, nameof(column));
        }

        return index;
    }

    private static string Clean(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
