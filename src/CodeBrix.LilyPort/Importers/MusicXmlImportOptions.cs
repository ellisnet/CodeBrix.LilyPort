// Copyright (c) 2026 Jeremy Ellis and contributors
//
// This file is part of CodeBrix.LilyPort, which is licensed under the
// GNU General Public License version 3 only.  See the LICENSE file in the
// repository root for the full text.

using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers;

/// <summary>How pitches are written in the converted document.</summary>
public enum MusicXmlPitchMode
{
    /// <summary>Each pitch is written against the one before it — <c>-r</c>/<c>--relative</c>.</summary>
    Relative,

    /// <summary>Each pitch names its own octave — <c>-a</c>/<c>--absolute</c>.</summary>
    Absolute,
}

/// <summary>What a MusicXML import may be told to do differently.</summary>
/// <remarks>
/// Every member mirrors one <c>musicxml2ly</c> command-line option, named after its LONG
/// spelling re-cased and keeping that spelling's sense — a member called
/// <c>NoPageLayout</c> is <see langword="false"/> by default because the option is
/// absent by default.
/// <para>
/// Four kinds of upstream option are deliberately ABSENT and are not a narrowing of what
/// the converter does: the driver's own (<c>-o</c>, <c>-h</c>, <c>--version</c>) belong
/// to the application calling this; the log-level switches (<c>-v</c>,
/// <c>--loglevel</c>) are answered by filtering <see cref="ImportResult.Messages"/>;
/// <c>--lxml</c> selects an implementation upstream itself no longer has; and
/// <c>-z</c>/<c>--compressed</c> names the INPUT KIND, which is
/// <see cref="MusicXmlImporter.ImportCompressed"/> rather than a flag.
/// <c>--sm</c>/<c>--shift-meter</c> is absent too: upstream declares it only to warn
/// that it is obsolete and ignored.
/// </para>
/// <para>
/// ⚠ <c>-r</c>/<c>--relative</c> and <c>-a</c>/<c>--absolute</c> are one option in
/// upstream's parser — the second is the first's <c>store_false</c> — so they are ONE
/// enum here (D58).
/// </para>
/// </remarks>
public sealed class MusicXmlImportOptions
{
    /// <summary>Gets or sets how pitches are written — <c>--relative</c>/<c>--absolute</c>.</summary>
    public MusicXmlPitchMode PitchMode { get; set; } = MusicXmlPitchMode.Relative;

    /// <summary>
    /// Gets or sets the note-name language — <c>-l</c>/<c>--language</c>, for example
    /// <c>deutsch</c>.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets whether <c>&lt;octave-shift&gt;</c> stops come before the note they
    /// belong to, as Finale writes them — <c>--oe</c>/<c>--ottavas-end-early</c>.
    /// </summary>
    /// <remarks>
    /// Kept as the text upstream reads rather than as a flag: it takes
    /// <c>t[rue]</c>/<c>f[alse]</c> and tests only the first character, and anything
    /// else reads as false.
    /// </remarks>
    public string OttavasEndEarly { get; set; }

    /// <summary>
    /// Gets or sets whether to leave out the <c>^</c>, <c>_</c> and <c>-</c> modifiers on
    /// articulations and dynamics — <c>--nd</c>/<c>--no-articulation-directions</c>.
    /// </summary>
    public bool NoArticulationDirections { get; set; }

    /// <summary>
    /// Gets or sets whether to leave out the exact vertical positions of rests —
    /// <c>--nrp</c>/<c>--no-rest-positions</c>.
    /// </summary>
    public bool NoRestPositions { get; set; }

    /// <summary>
    /// Gets or sets whether to ignore system breaks — <c>--nsb</c>/<c>--no-system-breaks</c>.
    /// </summary>
    public bool NoSystemBreaks { get; set; }

    /// <summary>
    /// Gets or sets whether to ignore page breaks — <c>--npb</c>/<c>--no-page-breaks</c>.
    /// </summary>
    public bool NoPageBreaks { get; set; }

    /// <summary>
    /// Gets or sets whether to ignore page margins — <c>--npm</c>/<c>--no-page-margins</c>.
    /// </summary>
    public bool NoPageMargins { get; set; }

    /// <summary>
    /// Gets or sets whether to leave out the exact page layout and its breaks —
    /// <c>--npl</c>/<c>--no-page-layout</c>, upstream's shortcut for the three above.
    /// </summary>
    public bool NoPageLayout { get; set; }

    /// <summary>
    /// Gets or sets whether to ignore the document's stem directions and let LilyPond
    /// choose — <c>--nsd</c>/<c>--no-stem-directions</c>.
    /// </summary>
    public bool NoStemDirections { get; set; }

    /// <summary>
    /// Gets or sets what to scale <c>&lt;dynamics&gt;</c> elements by —
    /// <c>--ds</c>/<c>--dynamics-scale</c>; zero means LilyPond's own size, and
    /// <see langword="null"/> means the document's own.
    /// </summary>
    public double? DynamicsScale { get; set; }

    /// <summary>
    /// Gets or sets whether markup font sizes are absolute rather than relative to the
    /// score's size — <c>--afs</c>/<c>--absolute-font-sizes</c>.
    /// </summary>
    public bool AbsoluteFontSizes { get; set; }

    /// <summary>
    /// Gets or sets whether to ignore the document's beaming and let LilyPond beam —
    /// <c>--nb</c>/<c>--no-beaming</c>.
    /// </summary>
    public bool NoBeaming { get; set; }

    /// <summary>Gets or sets whether to write a MIDI block — <c>-m</c>/<c>--midi</c>.</summary>
    public bool Midi { get; set; }

    /// <summary>
    /// Gets or sets which page's <c>&lt;credit&gt;</c> elements fill the header —
    /// <c>--cp</c>/<c>--credit-page</c>.
    /// </summary>
    public int CreditPage { get; set; } = 1;

    /// <summary>
    /// Gets or sets the pitch to transpose to — <c>--transpose</c>; the interval is the
    /// one between <c>c</c> and this pitch.
    /// </summary>
    public string Transpose { get; set; }

    /// <summary>
    /// Gets or sets how far to shift durations and time signatures —
    /// <c>--sd</c>/<c>--shift-durations</c>; -1 doubles every duration and 1 halves them.
    /// </summary>
    public int ShiftDurations { get; set; }

    /// <summary>
    /// Gets or sets which tablature clef to use — <c>--tc</c>/<c>--tab-clef</c>, either
    /// <c>tab</c> or <c>moderntab</c>.
    /// </summary>
    public string TabClef { get; set; }

    /// <summary>
    /// Gets or sets whether string numbers are written —
    /// <c>--sn</c>/<c>--string-numbers</c>, as <c>t[rue]</c> or <c>f[alse]</c>.
    /// </summary>
    /// <remarks>Kept as the text upstream reads, which tests only the first character.</remarks>
    public string StringNumbers { get; set; }

    /// <summary>
    /// Gets or sets whether <c>&lt;frame&gt;</c> events become a separate FretBoards
    /// voice rather than markups — <c>--fb</c>/<c>--fretboards</c>.
    /// </summary>
    public bool Fretboards { get; set; }

    /// <summary>
    /// Gets or sets whether the score is wrapped in a <c>\book</c> block — <c>--book</c>.
    /// </summary>
    public bool Book { get; set; }

    /// <summary>
    /// Gets or sets whether to leave out LilyPond's tag line —
    /// <c>--nt</c>/<c>--no-tagline</c>.
    /// </summary>
    public bool NoTagline { get; set; }

    /// <summary>Gets or sets what to call the input in the document's own header.</summary>
    /// <remarks>
    /// ⚠ PORT-ONLY, and not a widening of the option surface. <c>musicxml2ly</c> takes
    /// the file name from its command line and writes it into the preamble comment
    /// ("<c>% automatically converted by musicxml2ly from FILE</c>"). A library is handed
    /// text or bytes rather than a path, so the caller supplies the name that line should
    /// carry.
    /// </remarks>
    public string SourceName { get; set; } = string.Empty;
}
