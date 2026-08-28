// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using MutopiaProbe.Compare;

namespace MutopiaProbe.Report;

/// <summary>
/// Turns a pair of grades — the port against Mutopia, and the port against the pinned 2.27.2
/// oracle rendering the SAME source — into the one label the OBSERVATIONS document is sorted on.
/// <para>
/// Without the oracle, a row can only say "the port's page differs from the one Mutopia
/// published", which conflates three causes the README names. With it the question becomes
/// decidable: if the port agrees with 2.27.2, then whatever separates both of them from Mutopia
/// is version drift or Mutopia's build environment; if it does not, the difference is the
/// port's, whatever Mutopia happens to show.
/// </para>
/// </summary>
public static class DriftVerdict
{
    /// <summary>The port and the oracle agree, and so does Mutopia.</summary>
    public const string Clean = "CLEAN";

    /// <summary>The port and the oracle agree and Mutopia differs: upstream 2.27.2 differs from Mutopia the same way.</summary>
    public const string Drift = "DRIFT";

    /// <summary>The port differs from the oracle rendering the same source. This is the port's.</summary>
    public const string PortGap = "PORT-GAP";

    /// <summary>Neither the port nor the oracle produced anything: the converted source is refused by 2.27.2 too.</summary>
    public const string InputRefused = "INPUT-REFUSED";

    /// <summary>The port produced output where the oracle produced none. Not a defect, but not agreement either.</summary>
    public const string PortAhead = "PORT-AHEAD";

    /// <summary>The oracle was not run for this row.</summary>
    public const string NoOracle = "NO-ORACLE";

    /// <summary>
    /// The oracle did not finish, so what it wrote is a PARTIAL render and comparing against it
    /// would be meaningless. Measured: the Mendelssohn Octet's full score, where a 300 s kill
    /// left 31 pages of the 39 the oracle was still writing, and the row would otherwise have
    /// read PORT-GAP purely because the oracle was interrupted. Re-run such a row with a longer
    /// --oracle-timeout-seconds.
    /// </summary>
    public const string Inconclusive = "INCONCLUSIVE";

    /// <summary>Nothing to compare on this axis (neither side asked for it).</summary>
    public const string NotGraded = "NOT-GRADED";

    private static readonly string[] Severity =
    {
        PortGap, InputRefused, Inconclusive, PortAhead, Drift, Clean, NotGraded, NoOracle,
    };

    //was previously: ForPdf(mutopia, oracle, portHasPages, oracleHasPages, oracleFinished) — the
    //staff rung came from PdfAgrees, which read the RASTER count. Since 2026-08-28 the staff rung
    //is the SVG-structure count and arrives as its own argument.

    /// <summary>Grades the page axis.</summary>
    /// <param name="mutopia">The port against Mutopia's PDF.</param>
    /// <param name="oracle">The port against the oracle's PDF, or null when the oracle did not run.</param>
    /// <param name="svgStaves">The port's staves against the oracle's, read off the SVG pages, or null when the oracle did not run.</param>
    /// <param name="portHasPages">Whether the port produced a PDF at all.</param>
    /// <param name="oracleHasPages">Whether the oracle produced pages at all.</param>
    /// <param name="oracleFinished">Whether the oracle ran to completion; false when it was killed or would not launch.</param>
    /// <returns>The verdict.</returns>
    public static string ForPdf(
        PdfComparison mutopia, PdfComparison oracle, SvgStaffComparison svgStaves,
        bool portHasPages, bool oracleHasPages, bool oracleFinished)
    {
        if (oracle == null)
        {
            return NoOracle;
        }

        if (!oracleFinished)
        {
            // Whatever the oracle left behind is a partial render, so neither agreement nor
            // disagreement with it means anything.
            return Inconclusive;
        }

        if (!portHasPages && !oracleHasPages)
        {
            return InputRefused;
        }

        if (!portHasPages)
        {
            return PortGap;
        }

        if (!oracleHasPages)
        {
            return PortAhead;
        }

        if (!PdfAgrees(oracle) || StavesDiffer(svgStaves))
        {
            return PortGap;
        }

        return MutopiaAgrees(mutopia) ? Clean : Drift;
    }

    /// <summary>
    /// Whether the port agrees with Mutopia's PDF: <see cref="PdfAgrees"/> plus the RASTER staff
    /// rung, which this axis keeps. Mutopia published a PDF and no SVG, so the SVG-structure
    /// count that replaced the raster rung against the oracle cannot be taken here; and the
    /// Mutopia ladder was calibrated (see README, CALIBRATION) with the raster rung in place,
    /// where an agreeing pair sits at block_diff 0.13-0.18 and the rung is the shift-tolerant
    /// layout signal. Retiring it here would loosen the CLEAN bar without a measurement to
    /// justify it -- the 2026-08-28 re-grade measured exactly that: 28 rows to CLEAN. So the
    /// raster count still decides on THIS axis only, and decides nothing against the oracle.
    /// </summary>
    /// <param name="mutopia">The port against Mutopia's PDF.</param>
    /// <returns>True when the two documents agree as far as the Mutopia ladder can see.</returns>
    public static bool MutopiaAgrees(PdfComparison mutopia)
        => PdfAgrees(mutopia) && mutopia.RasterStavesVerdict != "STAVES-DIFFER";

    /// <summary>
    /// Whether the SVG-structure staff rung shows a real difference. Only
    /// <see cref="SvgStaffComparison.Differ"/> counts: an unavailable or unreadable rung is no
    /// evidence either way and must never produce a verdict, which is the mistake the raster
    /// rung it replaced kept making.
    /// </summary>
    /// <param name="svgStaves">The rung, or null when the oracle did not run.</param>
    /// <returns>True when at least one page holds a different number of staves.</returns>
    public static bool StavesDiffer(SvgStaffComparison svgStaves)
        => svgStaves != null && svgStaves.Verdict == SvgStaffComparison.Differ;

    /// <summary>Grades the performance axis.</summary>
    /// <param name="mutopia">The port against Mutopia's MIDI.</param>
    /// <param name="oracle">The port against the oracle's MIDI, or null when the oracle did not run.</param>
    /// <param name="portHasMidi">Whether the port wrote a MIDI file.</param>
    /// <param name="oracleHasMidi">Whether the oracle wrote a MIDI file.</param>
    /// <param name="oracleFinished">Whether the oracle ran to completion; false when it was killed or would not launch.</param>
    /// <returns>The verdict.</returns>
    public static string ForMidi(MidiComparison mutopia, MidiComparison oracle, bool portHasMidi, bool oracleHasMidi, bool oracleFinished)
    {
        if (oracle == null)
        {
            return NoOracle;
        }

        if (!oracleFinished)
        {
            return Inconclusive;
        }

        if (!portHasMidi && !oracleHasMidi)
        {
            // The source asks for no \midi, or neither side got far enough to write one. The
            // page axis already says which.
            return NotGraded;
        }

        if (!portHasMidi)
        {
            return PortGap;
        }

        if (!oracleHasMidi)
        {
            return PortAhead;
        }

        if (!MidiAgreesWithOracle(oracle))
        {
            return PortGap;
        }

        // Mutopia published no MIDI for plenty of entry points; with nothing to drift FROM, an
        // agreement with 2.27.2 is all this axis can say.
        return mutopia.Verdict == "NOREF" || MidiAgreesWithMutopia(mutopia) ? Clean : Drift;
    }

    /// <summary>Gets the worse of two verdicts.</summary>
    /// <param name="first">One verdict.</param>
    /// <param name="second">The other.</param>
    /// <returns>The worse one.</returns>
    public static string Worse(string first, string second)
        => Array.IndexOf(Severity, first) <= Array.IndexOf(Severity, second) ? first : second;

    /// <summary>
    /// Whether a PDF grade shows no evidence of a difference: the same number of pages, ink the
    /// calibrated grid calls SIMILAR, and no text the other side has that this one lost.
    /// <para>
    /// //was previously: this also required RasterStavesVerdict != "STAVES-DIFFER". Removed
    /// 2026-08-28. That rung was resolution-dependent and could be flipped by a font-coverage
    /// change inside the PDF library, and it was the sole basis of half the PORT-GAP verdicts of
    /// the 2026-08-27 sweep. Against the ORACLE the staff rung is now the SVG-structure count,
    /// applied by <see cref="ForPdf"/>. Against MUTOPIA the raster rung is kept, by
    /// <see cref="MutopiaAgrees"/>: that axis has no SVG to read and its ladder was calibrated
    /// with the rung in place, so it stays where it was calibrated.
    /// </para>
    /// </summary>
    /// <param name="comparison">The grade.</param>
    /// <returns>True when the two documents agree as far as the ladder can see.</returns>
    public static bool PdfAgrees(PdfComparison comparison)
    {
        if (comparison == null || comparison.PageCountVerdict != "PAGES-EQUAL")
        {
            return false;
        }

        if (comparison.InkVerdict == "LAYOUT-DIFFERS" || comparison.InkVerdict == "VERY-DIFFERENT")
        {
            return false;
        }

        // TEXT-REF-EMPTY is not a difference: the pre-2.10 references draw their text as glyph
        // outlines pdftotext cannot read, so there is nothing to compare against. TEXT-DIFFERS
        // and TEXT-PORT-EMPTY are — words the other side has and this one does not.
        return comparison.TextVerdict != "TEXT-DIFFERS" && comparison.TextVerdict != "TEXT-PORT-EMPTY";
    }

    /// <summary>Whether a MIDI grade against the ORACLE shows agreement: the same events, or the same performance under different meta text.</summary>
    /// <param name="comparison">The grade.</param>
    /// <returns>True when the performances agree.</returns>
    public static bool MidiAgreesWithOracle(MidiComparison comparison)
        => comparison != null && (comparison.Verdict == "MATCH" || comparison.ChannelVerdict == "CHANNEL-EQUAL");

    /// <summary>
    /// Whether a MIDI grade against MUTOPIA shows agreement. The bar is the notes — the multiset
    /// of (tick, pitch) note-ons — because velocity defaults, channel numbering and program
    /// numbers all changed between the releases that built the corpus and 2.27.2.
    /// </summary>
    /// <param name="comparison">The grade.</param>
    /// <returns>True when the notes agree.</returns>
    public static bool MidiAgreesWithMutopia(MidiComparison comparison)
        => comparison != null && (comparison.Verdict == "MATCH" || comparison.NotesVerdict == "NOTES-EQUAL");
}
