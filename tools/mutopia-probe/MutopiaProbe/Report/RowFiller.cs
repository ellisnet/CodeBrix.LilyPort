// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using MutopiaProbe.Compare;

namespace MutopiaProbe.Report;

/// <summary>
/// Writes a graded comparison into a <see cref="ResultRow"/>. It exists so that the sweep and
/// <c>--regrade</c> cannot fill the same table in two subtly different ways: both call these
/// methods and nothing else touches the graded columns.
/// </summary>
public static class RowFiller
{
    /// <summary>Fills the port-against-Mutopia page columns.</summary>
    /// <param name="row">The row.</param>
    /// <param name="pdf">The grade.</param>
    /// <param name="ink">Whether the ink grade ran.</param>
    public static void FillMutopiaPdf(ResultRow row, PdfComparison pdf, bool ink)
    {
        row.Set("pages_port", pdf.PortPages);
        row.Set("pages_ref", pdf.ReferencePages);
        row["page_count"] = pdf.PageCountVerdict;
        row["size_port"] = pdf.PortPageSize;
        row["size_ref"] = pdf.ReferencePageSize;
        row["page_size"] = pdf.PageSizeVerdict;
        row["text"] = pdf.TextVerdict;
        row.Set("text_sim", pdf.TextSimilarity);
        row.Set("text_contain", pdf.TextContainment);
        row.Set("text_bag", pdf.TextBag);
        row.Set("tokens_port", pdf.PortTokens);
        row.Set("tokens_ref", pdf.ReferenceTokens);
        row["ink"] = ink ? pdf.InkVerdict : "INK-OFF";
        row.Set("block_diff", pdf.BlockDifference);
        row.Set("ink_iou", pdf.InkIoU);
        row.Set("ink_port", pdf.PortInk);
        row.Set("ink_ref", pdf.ReferenceInk);
        row["raster_staves"] = pdf.RasterStavesVerdict ?? string.Empty;
        row.Set("raster_staves_port", pdf.RasterPortStaves);
        row.Set("raster_staves_ref", pdf.RasterReferenceStaves);
        row.Set("compared_pages", pdf.ComparedPages);
        row["note"] = Append(row["note"], pdf.Note);
    }

    /// <summary>Fills the port-against-Mutopia performance columns.</summary>
    /// <param name="row">The row.</param>
    /// <param name="midi">The grade.</param>
    public static void FillMutopiaMidi(ResultRow row, MidiComparison midi)
    {
        row["midi"] = midi.Verdict;
        row["midi_channel"] = midi.ChannelVerdict ?? string.Empty;
        row["midi_channel_first_diff"] = midi.ChannelFirstDifference ?? string.Empty;
        row["midi_notes"] = midi.NotesVerdict ?? string.Empty;
        row["midi_pitches"] = midi.PitchesVerdict ?? string.Empty;
        row.Set("midi_tracks_port", midi.PortTracks);
        row.Set("midi_tracks_ref", midi.ReferenceTracks);
        row.Set("midi_div_port", midi.PortDivision);
        row.Set("midi_div_ref", midi.ReferenceDivision);
        row.Set("midi_notes_port", midi.PortNotes);
        row.Set("midi_notes_ref", midi.ReferenceNotes);
        row.Set("midi_len_port", midi.PortLength);
        row.Set("midi_len_ref", midi.ReferenceLength);
        row.Set("midi_tempos_port", midi.PortTempos);
        row.Set("midi_tempos_ref", midi.ReferenceTempos);
        row.Set("midi_programs_port", midi.PortPrograms);
        row.Set("midi_programs_ref", midi.ReferencePrograms);
        row["midi_stamp_ref"] = midi.ReferenceStamp ?? string.Empty;
        row["midi_first_diff"] = midi.FirstDifference ?? string.Empty;
    }

    /// <summary>
    /// Fills the port-against-oracle page columns, including BOTH staff rungs: the raster one,
    /// which is reported and decides nothing, and the SVG-structure one, which is the rung the
    /// verdict is cut on.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="oraclePdf">The PDF grade against the oracle.</param>
    /// <param name="svgStaves">The SVG staff rung, or null when the oracle did not run.</param>
    /// <param name="ink">Whether the ink grade ran.</param>
    public static void FillOraclePdf(ResultRow row, PdfComparison oraclePdf, SvgStaffComparison svgStaves, bool ink)
    {
        row["o_page_count"] = oraclePdf.PageCountVerdict;
        row["o_page_size"] = oraclePdf.PageSizeVerdict;
        row["o_text"] = oraclePdf.TextVerdict;
        row.Set("o_text_bag", oraclePdf.TextBag);
        row["o_ink"] = ink ? oraclePdf.InkVerdict : "INK-OFF";
        row.Set("o_block_diff", oraclePdf.BlockDifference);
        row["o_raster_staves"] = oraclePdf.RasterStavesVerdict ?? string.Empty;
        row.Set("o_raster_staves_port", oraclePdf.RasterPortStaves);
        row.Set("o_raster_staves_oracle", oraclePdf.RasterReferenceStaves);
        if (svgStaves == null)
        {
            row["svg_staves"] = SvgStaffComparison.NoOracle;
            return;
        }

        row["svg_staves"] = svgStaves.Verdict;
        row.Set("svg_staves_port", SvgStaves.Total(svgStaves.Port));
        row.Set("svg_staves_oracle", SvgStaves.Total(svgStaves.Oracle));
        row["svg_staves_by_page_port"] = SvgStaves.PerPage(svgStaves.Port);
        row["svg_staves_by_page_oracle"] = SvgStaves.PerPage(svgStaves.Oracle);
        row["svg_staves_diff_pages"] = svgStaves.DifferingPages ?? string.Empty;
        row["note"] = Append(row["note"], svgStaves.Note);
    }

    /// <summary>Fills the port-against-oracle performance columns.</summary>
    /// <param name="row">The row.</param>
    /// <param name="oracleMidi">The MIDI grade against the oracle.</param>
    public static void FillOracleMidi(ResultRow row, MidiComparison oracleMidi)
    {
        row["o_midi"] = oracleMidi.Verdict;
        row["o_midi_channel"] = oracleMidi.ChannelVerdict ?? string.Empty;
        row["o_midi_notes"] = oracleMidi.NotesVerdict ?? string.Empty;
        row["o_midi_pitches"] = oracleMidi.PitchesVerdict ?? string.Empty;
        row["o_midi_first_diff"] = oracleMidi.FirstDifference ?? string.Empty;
    }

    /// <summary>Appends one note to another, keeping both.</summary>
    /// <param name="note">What is already there.</param>
    /// <param name="more">What to add.</param>
    /// <returns>The joined note.</returns>
    public static string Append(string note, string more)
        => string.IsNullOrEmpty(more) ? note : string.IsNullOrEmpty(note) ? more : note + "; " + more;
}
