// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers;

/// <summary>
/// Everything <c>musicxml2ly</c> keeps at module scope, for the length of ONE import.
/// </summary>
/// <remarks>
/// The MusicXML converter is four python modules — <c>musicxml2ly</c>, <c>musicexp</c>,
/// <c>musicxml</c> and <c>musicxml2ly_globvars</c> — and each of them keeps part of its
/// world in module globals, relying on a run of the script being a run of the process.
/// abc2ly and midi2ly had one module each, so for those the faithful translation of
/// "the module" was "the converter instance" (see PORT-COVERAGE §2). Here there are
/// four, and they read each other's state, so the globals live in one object that all
/// four halves of the port are handed.
/// <para>
/// Two consequences worth stating, because neither is upstream's: TWO IMPORTS AT ONCE
/// CANNOT SEE EACH OTHER, which also lets the parity suite run in parallel where board
/// trap 25 says process-wide memo state would not have; and THE DIAGNOSTIC SINK IS
/// PER-IMPORT, for the same reason.
/// </para>
/// <para>
/// ⚠ AN UNSET OPTION IS NOT A FALSE ONE. Upstream reads most of these through a
/// <c>try: return x except NameError:</c> pair, so "never set" and "set to the default"
/// are the same answer by accident but not by construction. The port keeps them
/// nullable and answers the same default in the same place, so that a later reading of
/// either half stays honest.
/// </para>
/// </remarks>
internal sealed class MusicXmlImportState
{
    /// <summary>Builds the state for one import.</summary>
    /// <param name="options">What the caller asked for.</param>
    /// <param name="diagnostics">Where messages go.</param>
    internal MusicXmlImportState(MusicXmlImportOptions options, ImportDiagnostics diagnostics)
    {
        Options = options;
        Diagnostics = diagnostics;
        Paper = new LilyPaper(this);
        LayoutInformation = new LilyLayout(this);
        PitchGeneratingFunction = LilyPitchLanguages.PitchGeneral;
    }

    /// <summary>What the caller asked for.</summary>
    internal MusicXmlImportOptions Options { get; }

    /// <summary>Where messages go.</summary>
    internal ImportDiagnostics Diagnostics { get; }

    /// <summary>
    /// The program name every diagnostic carries, as <c>lilylib</c> takes it from
    /// <c>argv[0]</c>.
    /// </summary>
    private const string ProgramName = "musicxml2ly";

    /// <summary>Reports a warning, in upstream's own wording and shape.</summary>
    /// <param name="message">What to say.</param>
    internal void Warning(string message)
        => Diagnostics.Write(ProgramName + ": warning: " + message + "\n");

    /// <summary>Reports an error, in upstream's own wording and shape.</summary>
    /// <param name="message">What to say.</param>
    internal void Error(string message)
    {
        Diagnostics.Write(ProgramName + ": error: " + message + "\n");
        Diagnostics.CountError();
    }

    //--- musicxml.py -------------------------------------------------------------

    /// <summary>
    /// The musical lengths of grace-note sequences at the beginning of a voice.
    /// </summary>
    /// <remarks>
    /// Upstream keeps these in a module dictionary to work around LilyPond's issue #34.
    /// </remarks>
    internal Dictionary<(string PartId, string VoiceId), PythonFraction> StartingGraceLengths
    { get; } = new Dictionary<(string, string), PythonFraction>();

    /// <summary>The longest of those lengths.</summary>
    internal PythonFraction MaxStartingGraceLength { get; set; } = PythonFraction.Zero;

    /// <summary>Whether the document asked for any stem direction at all.</summary>
    internal bool HaveStemDirections { get; set; }

    //--- musicexp.py -------------------------------------------------------------

    /// <summary>The pitch a relative-mode pitch is measured against.</summary>
    internal LilyPitch PreviousPitch { get; set; }

    /// <summary>Whether pitches are written in relative mode.</summary>
    internal bool RelativePitches { get; set; }

    /// <summary>
    /// The duration logarithm the last duration wrote, for the tremolo event to read.
    /// </summary>
    /// <remarks>
    /// ⚠ A GLOBAL USED AS A RETURN CHANNEL. Upstream's comment says it plainly: "For
    /// communication between <c>Duration</c> and <c>TremoloEvent</c>". The tremolo
    /// reads whatever duration was written LAST, so the order of writing is part of the
    /// specification and must not be tidied.
    /// </remarks>
    internal int LyDur { get; set; }

    /// <summary>How the current language spells a pitch.</summary>
    internal Func<LilyPitch, string> PitchGeneratingFunction { get; set; }

    /// <summary>What <c>--shift-durations</c> asked for, or null when it was not given.</summary>
    internal int? ShiftDurationsOption { get; set; }

    /// <summary>What <c>--shift-durations</c> means here.</summary>
    internal int ShiftDurations => ShiftDurationsOption ?? 0;

    /// <summary>What <c>--midi</c> asked for, or null when it was not given.</summary>
    internal bool? MidiOption { get; set; }

    /// <summary>Whether a MIDI block is wanted.</summary>
    internal bool CreateMidi => MidiOption ?? false;

    /// <summary>What <c>--transpose</c> asked for, or null when it was not given.</summary>
    internal string TransposeOption { get; set; }

    /// <summary>The transposition, as the prefix a music expression carries.</summary>
    /// <returns>The prefix, or empty when no transposition was asked for.</returns>
    internal string GetTransposeString()
        => TransposeOption != null ? "\\transpose c " + TransposeOption : string.Empty;

    /// <summary>The transposition, in semitones.</summary>
    /// <returns>Always zero.</returns>
    /// <remarks>
    /// ⚠ UPSTREAM CANNOT ANSWER ANYTHING ELSE, and the port reproduces that rather than
    /// quietly repairing it. <c>get_transpose("integer")</c> calls
    /// <c>generic_tone_to_pitch</c>, whose first statement assigns
    /// <c>p.octave</c> BEFORE <c>p = Pitch()</c> binds <c>p</c> — an
    /// <c>UnboundLocalError</c> on every call. That exception derives from
    /// <c>NameError</c>, which is exactly what the caller's <c>except</c> clause names,
    /// so the failure is swallowed and the answer is 0. The two fret-diagram sites that
    /// ask are therefore never transposed by any release of upstream that carries this
    /// code. Kept as a REPRODUCED defect, not a fixed one: D64 requires a MEASUREMENT
    /// against the oracle before a converter defect is repaired, and repairing this one
    /// would move fixture output away from what the oracle produced. Recorded in
    /// tools/musicxml2lyprobe/DIVERGENCES.txt as a candidate.
    /// </remarks>
    internal int GetTransposeSemitones() => 0;

    /// <summary>What <c>--tab-clef</c> asked for, or null when it was not given.</summary>
    internal string TabClefOption { get; set; }

    /// <summary>Which tab clef to draw.</summary>
    internal string GetTabClef()
        => TabClefOption == "tab" || TabClefOption == "moderntab" ? TabClefOption : "tab";

    /// <summary>What <c>--ottavas-end-early</c> asked for, or null when it was not given.</summary>
    internal string OttavasEndEarlyOption { get; set; }

    /// <summary>Whether octave shifts end before their note.</summary>
    /// <returns>'t' or 'f'.</returns>
    internal string GetOttavasEndEarly()
        => !string.IsNullOrEmpty(OttavasEndEarlyOption) && OttavasEndEarlyOption[0] == 't'
            ? "t" : "f";

    /// <summary>What <c>--string-numbers</c> asked for, or null when it was not given.</summary>
    internal string StringNumbersOption { get; set; }

    /// <summary>Whether string numbers are drawn.</summary>
    /// <returns>'t' or 'f'.</returns>
    internal string GetStringNumbers()
        => !string.IsNullOrEmpty(StringNumbersOption) && StringNumbersOption[0] == 'f'
            ? "f" : "t";

    /// <summary>What <c>--book</c> asked for, or null when it was not given.</summary>
    internal bool? BookOption { get; set; }

    /// <summary>Whether the score goes inside a book block.</summary>
    internal bool GetBook() => BookOption ?? false;

    /// <summary>What <c>--no-tagline</c> asked for, or null when it was not given.</summary>
    internal bool? TaglineOption { get; set; }

    /// <summary>Whether a tagline is emitted.</summary>
    internal bool GetTagline() => TaglineOption ?? false;

    /// <summary>What <c>--absolute-font-sizes</c> asked for, or null when it was not given.</summary>
    internal bool? AbsoluteFontSizesOption { get; set; }

    /// <summary>Whether markup font sizes are absolute rather than score-relative.</summary>
    internal bool GetAbsoluteFontSizes() => AbsoluteFontSizesOption ?? false;

    //--- musicxml2ly_globvars.py -------------------------------------------------

    /// <summary>The settings that will go inside the layout block.</summary>
    internal LilyLayout LayoutInformation { get; }

    /// <summary>The settings that will go inside the paper block.</summary>
    internal LilyPaper Paper { get; }

    //--- musicxml2ly.py ----------------------------------------------------------

    /// <summary>What the conversion decided about how much of the document to carry.</summary>
    internal MusicXmlConversionSettings ConversionSettings { get; }
        = new MusicXmlConversionSettings();

    /// <summary>The named definitions the output has to carry for the score to work.</summary>
    internal List<string> NeededAdditionalDefinitions { get; } = new List<string>();

    /// <summary>
    /// The macros the converter builds as it goes, which might use the named
    /// definitions.
    /// </summary>
    internal Dictionary<string, string> AdditionalMacros { get; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The definitions this import added to upstream's own table.</summary>
    /// <remarks>
    /// ⚠ Upstream WRITES INTO <c>definitions.additional_definitions</c> at run time — the
    /// <c>--language</c> option adds an entry keyed by the language's own name — so the
    /// module-level table is per-run state. The port keeps upstream's table immutable and
    /// carries the run's additions here.
    /// </remarks>
    internal Dictionary<string, string> ExtraDefinitions { get; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The tuning of the strings a fret diagram is read against.
    /// </summary>
    /// <remarks>
    /// ⚠ MEMOISED ON FIRST USE, as upstream memoises it — and that memo is the cause of
    /// its one measured crash: a first diagram with four strings fixes the list at four
    /// entries, and a later six-string diagram then indexes past the end. See the
    /// divergence record.
    /// </remarks>
    internal List<LilyPitch> StringTunings { get; set; }

    /// <summary>What the document is called, for the one line that names it.</summary>
    internal string SourceName => Options.SourceName ?? string.Empty;
}

/// <summary>What the conversion decided about how much of the document to carry.</summary>
internal sealed class MusicXmlConversionSettings
{
    /// <summary>Whether the document's beaming is thrown away.</summary>
    internal bool IgnoreBeaming { get; set; }

    /// <summary>Whether the document's stem directions are carried.</summary>
    internal bool ConvertStemDirections { get; set; }

    /// <summary>Whether the document's exact rest placements are carried.</summary>
    internal bool ConvertRestPositions { get; set; } = true;

    /// <summary>Whether the document's page layout is carried.</summary>
    internal bool ConvertPageLayout { get; set; } = true;

    /// <summary>Whether the document's system breaks are carried.</summary>
    internal bool ConvertSystemBreaks { get; set; } = true;

    /// <summary>Whether the document's page breaks are carried.</summary>
    internal bool ConvertPageBreaks { get; set; } = true;

    /// <summary>Whether the document's page margins are carried.</summary>
    internal bool ConvertPageMargins { get; set; } = true;
}
