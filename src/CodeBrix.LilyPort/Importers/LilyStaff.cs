/*
   This file is part of LilyPond, the GNU music typesetter.

   Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>,
   Copyright (C) 2007--2026 Reinhold Kainhofer <reinhold@kainhofer.com>

   LilyPond is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.

   LilyPond is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.

   You should have received a copy of the GNU General Public License
   along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (StaffGroup, Staff and its three subclasses, Score, and the two module dictionaries beside them);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// One voice of one staff of one part, as the score setup names its four contexts.
/// </summary>
/// <remarks>
/// ⚠ Upstream carries these as six-member python lists built by <c>format_staff_info</c>
/// and unpacks them positionally at four sites, one of which ignores every member but
/// the fourth. C# cannot hold six unrelated types in a list, so each member is named.
/// </remarks>
internal sealed class LilyVoiceInfo
{
    /// <summary>Builds the entry.</summary>
    /// <param name="voiceName">The voice's LilyPond identifier.</param>
    /// <param name="lyrics">The lyric lines, each with its stanza and placement.</param>
    /// <param name="figuredBassName">The figured-bass identifier, or empty.</param>
    /// <param name="chordNamesName">The chord-names identifier, or empty.</param>
    /// <param name="fretBoardsName">The fretboards identifier, or empty.</param>
    /// <param name="crossStaffChordVoice">
    /// Which way a cross-staff chord's stems point, or null when it is not one.
    /// </param>
    internal LilyVoiceInfo(
        string voiceName,
        List<(string Name, string StanzaId, string Placement)> lyrics,
        string figuredBassName,
        string chordNamesName,
        string fretBoardsName,
        string crossStaffChordVoice)
    {
        VoiceName = voiceName;
        Lyrics = lyrics;
        FiguredBassName = figuredBassName;
        ChordNamesName = chordNamesName;
        FretBoardsName = fretBoardsName;
        CrossStaffChordVoice = crossStaffChordVoice;
    }

    /// <summary>The voice's LilyPond identifier.</summary>
    internal string VoiceName { get; }

    /// <summary>The lyric lines, each with its stanza number and placement.</summary>
    internal List<(string Name, string StanzaId, string Placement)> Lyrics { get; }

    /// <summary>The figured-bass identifier, or empty when there is none.</summary>
    internal string FiguredBassName { get; }

    /// <summary>The chord-names identifier, or empty when there is none.</summary>
    internal string ChordNamesName { get; }

    /// <summary>The fretboards identifier, or empty when there is none.</summary>
    internal string FretBoardsName { get; }

    /// <summary>Which way a cross-staff chord's stems point, or null.</summary>
    internal string CrossStaffChordVoice { get; }
}

/// <summary>One staff of one part, with the voices on it.</summary>
/// <remarks>Upstream's two-member list <c>[staff_id, voices]</c>.</remarks>
internal sealed class LilyStaffInfo
{
    /// <summary>Builds the entry.</summary>
    /// <param name="staffId">The staff's own identifier.</param>
    /// <param name="voices">The voices on it.</param>
    internal LilyStaffInfo(string staffId, List<LilyVoiceInfo> voices)
    {
        StaffId = staffId;
        Voices = voices;
    }

    /// <summary>The staff's own identifier.</summary>
    internal string StaffId { get; }

    /// <summary>The voices on it.</summary>
    internal List<LilyVoiceInfo> Voices { get; }
}

/// <summary>A group of staves.</summary>
internal class LilyStaffGroup : LilyExpression
{
    /// <summary>The grob each MusicXML group symbol asks for.</summary>
    internal static readonly Dictionary<string, string> SystemStartDict
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "brace", "SystemStartBrace" },
            //Default of `systemStartDelimiter' in `StaffGroup'.
            { "bracket", null },
            { "line", null },  //TODO (upstream's): Implement
            { "none", "SystemStartBar" },
            { "square", "SystemStartSquare" },
        };

    /// <summary>Builds the group.</summary>
    /// <param name="state">The import this group belongs to.</param>
    /// <param name="command">Which LilyPond context the group becomes.</param>
    internal LilyStaffGroup(MusicXmlImportState state, string command = "StaffGroup")
        : base(state)
        => StaffType = command;

    /// <summary>Which LilyPond context this group becomes.</summary>
    internal string StaffType { get; set; }

    /// <summary>The group's own identifier.</summary>
    internal string Id { get; set; }

    /// <summary>The instrument name written at the left.</summary>
    internal string InstrumentName { get; set; }

    /// <summary>Which MIDI instrument the group sounds with.</summary>
    internal string Sound { get; set; }

    /// <summary>The short instrument name written at the left.</summary>
    internal string ShortInstrumentName { get; set; }

    /// <summary>Which delimiter is drawn at the left.</summary>
    internal string Symbol { get; set; }

    /// <summary>The staves and groups inside this one.</summary>
    /// <remarks>
    /// ⚠ Typed as expressions rather than as groups because upstream appends whatever
    /// survives its grouping pass, which can include a <see cref="LilyPartGroupInfo"/>
    /// that never found its closing element. Upstream then asks that object for
    /// <c>is_group</c> and raises <c>AttributeError</c>; the cast below is that raise.
    /// </remarks>
    internal List<LilyExpression> Children { get; } = new List<LilyExpression>();

    /// <summary>Whether this is a group rather than a staff.</summary>
    internal virtual bool IsGroup => true;

    /// <summary>The context modifications this group carries.</summary>
    internal List<string> ContextModifications { get; } = new List<string>();

    /// <summary>The part's staves and their voices.</summary>
    /// <remarks>
    /// See the comment before <c>format_staff_info</c> together with
    /// <c>update_score_setup</c> (both in <c>musicxml2ly.py</c>) how entries look like.
    /// </remarks>
    internal List<LilyStaffInfo> PartInformation { get; set; }

    /// <summary>Whether the group this staff sits in names an instrument.</summary>
    internal bool HaveGroupInstrumentName { get; set; }

    /// <inheritdoc/>
    internal override bool Contains(LilyExpression element)
    {
        if (this == element)
        {
            return true;
        }

        foreach (LilyExpression child in Children)
        {
            if (child.Contains(element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Records one staff or group inside this one.</summary>
    /// <param name="staff">The staff or group.</param>
    internal void AppendStaff(LilyExpression staff) => Children.Add(staff);

    /// <summary>Finds the staff a part became.</summary>
    /// <param name="partId">The part's identifier.</param>
    /// <returns>The staff, or null.</returns>
    /// <remarks>
    /// TODO (upstream's): Instead of searching the tree, why not consult a score-level
    /// LUT {part_id: node} that is populated as nodes are added?
    /// </remarks>
    internal LilyStaffGroup FindPart(string partId)
    {
        if (partId == Id)
        {
            return this;
        }

        foreach (LilyExpression child in Children)
        {
            //⚠ Upstream guards with `getattr(c, "find_part", None)': a group-info object
            //that survived the grouping pass has no such method.
            LilyStaffGroup found = (child as LilyStaffGroup)?.FindPart(partId);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Records the part's staves and their voices.</summary>
    /// <param name="stavesInfo">The staves.</param>
    internal void SetPartInformation(List<LilyStaffInfo> stavesInfo)
        => PartInformation = stavesInfo;

    /// <summary>Records one context modification.</summary>
    /// <param name="modification">The modification.</param>
    internal void AddContextModification(string modification)
        => ContextModifications.Add(modification);

    /// <summary>Writes what is inside this group.</summary>
    /// <param name="printer">Where to write.</param>
    internal virtual void PrintLyContents(LilyOutputPrinter printer)
    {
        bool haveGroupInstrumentName = false;
        if (InstrumentName != null || ShortInstrumentName != null)
        {
            haveGroupInstrumentName = true;
        }

        foreach (LilyExpression child in Children)
        {
            if (child != null)
            {
                //⚠ Upstream reads `c.is_group' unconditionally, so anything that is not a
                //staff or a group raises AttributeError here; the cast is that raise.
                LilyStaffGroup group = (LilyStaffGroup)child;
                if (!group.IsGroup && haveGroupInstrumentName)
                {
                    group.HaveGroupInstrumentName = true;
                }

                group.PrintLy(printer);
            }
        }
    }

    /// <summary>Whether this group needs a <c>\with</c> block of its own.</summary>
    /// <returns>Whether it does.</returns>
    internal virtual bool NeedsWith()
    {
        bool needsWith = false;
        needsWith |= InstrumentName != null;
        needsWith |= ShortInstrumentName != null;
        needsWith |= Symbol != null && Symbol != "bracket";
        return needsWith;
    }

    /// <summary>Writes the context modifications this group's own settings ask for.</summary>
    /// <param name="printer">Where to write.</param>
    internal virtual void PrintLyContextMods(LilyOutputPrinter printer)
    {
        if (!string.IsNullOrEmpty(InstrumentName) || !string.IsNullOrEmpty(ShortInstrumentName))
        {
            printer.Dump("\\consists \"Instrument_name_engraver\"");
            printer.Newline();
        }

        string bracket = Symbol != null && SystemStartDict.TryGetValue(Symbol, out string named)
            ? named
            : null;
        if (bracket != null)
        {
            printer.Dump("systemStartDelimiter = #'" + bracket);
            printer.Newline();
        }
    }

    /// <summary>Writes this group's <c>\with</c> block, when it needs one.</summary>
    /// <param name="printer">Where to write.</param>
    internal virtual void PrintLyOverrides(LilyOutputPrinter printer)
    {
        bool needsWith = NeedsWith() | (ContextModifications.Count > 0);
        if (needsWith)
        {
            printer.Dump("\\with {");
            printer.Newline();
            PrintLyContextMods(printer);
            foreach (string modification in ContextModifications)
            {
                printer.Dump(modification);
                printer.Newline();
            }

            printer.Dump("}");
        }
    }

    /// <summary>Writes the chord-name contexts this group's voices ask for.</summary>
    /// <param name="printer">Where to write.</param>
    internal void PrintChords(LilyOutputPrinter printer)
    {
        //⚠ Upstream catches the TypeError raised by iterating None; the port asks.
        if (PartInformation == null)
        {
            return;
        }

        foreach (LilyStaffInfo staffInfo in PartInformation)
        {
            foreach (LilyVoiceInfo voice in staffInfo.Voices)
            {
                string chordNames = voice.ChordNamesName;
                if (!string.IsNullOrEmpty(chordNames))
                {
                    printer.Dump("\\context ChordNames = \"" + chordNames + "\"");
                    string transpose = State.GetTransposeString();
                    if (!string.IsNullOrEmpty(transpose))
                    {
                        printer.Dump(transpose);
                    }

                    printer.Dump("{");
                    printer.Newline();
                    printer.Dump("\\" + chordNames);
                    printer.Newline();
                    printer.Dump("}");
                    printer.Newline();
                }
            }
        }
    }

    /// <summary>Writes the fretboard contexts this group's voices ask for.</summary>
    /// <param name="printer">Where to write.</param>
    internal void PrintFretboards(LilyOutputPrinter printer)
    {
        //⚠ Upstream catches the TypeError raised by iterating None; the port asks.
        if (PartInformation == null)
        {
            return;
        }

        foreach (LilyStaffInfo staffInfo in PartInformation)
        {
            foreach (LilyVoiceInfo voice in staffInfo.Voices)
            {
                string fretboards = voice.FretBoardsName;
                if (!string.IsNullOrEmpty(fretboards))
                {
                    printer.Dump("\\context FretBoards = \"" + fretboards + "\"");
                    string transpose = State.GetTransposeString();
                    if (!string.IsNullOrEmpty(transpose))
                    {
                        printer.Dump(transpose);
                    }

                    printer.Dump("{");
                    printer.Newline();
                    printer.Dump("\\" + fretboards);
                    printer.Newline();
                    printer.Dump("}");
                    printer.Newline();
                }
            }
        }
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        PrintChords(printer);
        PrintFretboards(printer);
        if (!string.IsNullOrEmpty(StaffType))
        {
            printer.Dump(
                this is LilyStaff
                    ? "\\new " + StaffType + " = \"" + Id + "\""
                    : "\\new " + StaffType);
        }

        PrintLyOverrides(printer);
        if (!string.IsNullOrEmpty(StaffType))
        {
            printer.Dump("<<");
            printer.Newline();
            if (!string.IsNullOrEmpty(InstrumentName))
            {
                printer.Dump("\\set " + StaffType + ".instrumentName =");
                printer.Dump(LilyMarkup.EscapeInstrumentString(InstrumentName));
                printer.Newline();
            }

            if (!string.IsNullOrEmpty(ShortInstrumentName))
            {
                printer.Dump("\\set " + StaffType + ".shortInstrumentName =");
                printer.Dump(LilyMarkup.EscapeInstrumentString(ShortInstrumentName));
                printer.Newline();
            }

            if (!IsGroup && HaveGroupInstrumentName)
            {
                printer.Dump("\\override Staff.InstrumentName.self-alignment-X = #RIGHT");
                printer.Newline();
                printer.Dump("\\override Staff.InstrumentName.padding = #1");
                printer.Newline();
            }
        }

        if (!string.IsNullOrEmpty(Sound))
        {
            printer.Dump("\\set " + StaffType + ".midiInstrument = \"" + Sound + "\"");
            printer.Newline();
        }

        PrintLyContents(printer);
        if (!string.IsNullOrEmpty(StaffType))
        {
            printer.Dump(">>");
            printer.Newline();
        }
    }
}

/// <summary>One staff.</summary>
internal class LilyStaff : LilyStaffGroup
{
    /// <summary>The command that selects each numbered voice.</summary>
    internal static readonly Dictionary<int, string> VoiceTextDict
        = new Dictionary<int, string>
        {
            { 0, "\\oneVoice" },
            { 1, "\\voiceOne" },
            { 2, "\\voiceTwo" },
            { 3, "\\voiceThree" },
            { 4, "\\voiceFour" },
        };

    /// <summary>Builds the staff.</summary>
    /// <param name="state">The import this staff belongs to.</param>
    /// <param name="command">Which LilyPond context the staff becomes.</param>
    internal LilyStaff(MusicXmlImportState state, string command = "Staff")
        : base(state, command)
    {
    }

    /// <inheritdoc/>
    internal override bool IsGroup => false;

    /// <summary>The part this staff belongs to.</summary>
    internal MusicXmlNode Part { get; set; }

    /// <summary>Which delimiter a multi-staff part is drawn with.</summary>
    /// <remarks>Default of <c>systemStartDelimiter</c> in <c>PianoStaff</c>.</remarks>
    internal string PartSymbol { get; set; } = "brace";

    /// <summary>The first staff a group bar line spans.</summary>
    internal int BarlineTop { get; set; }

    /// <summary>The last staff a group bar line spans.</summary>
    internal int BarlineBottom { get; set; }

    /// <summary>Which LilyPond context each voice becomes.</summary>
    internal string VoiceCommand { get; set; } = "Voice";

    /// <summary>Which context each sub-staff becomes, for a multi-staff part.</summary>
    internal string SubStaffType { get; set; }

    /// <summary>Whether the part's own bar lines are drawn.</summary>
    internal bool? Barline { get; set; }

    /// <summary>Whether a span bar reaches the staff below.</summary>
    internal bool? SpanbarToStaffBelow { get; set; }

    /// <inheritdoc/>
    internal override bool NeedsWith() => false;

    /// <inheritdoc/>
    internal override void PrintLyContextMods(LilyOutputPrinter printer)
    {
    }

    /// <inheritdoc/>
    internal override void PrintLyContents(LilyOutputPrinter printer)
    {
        if (string.IsNullOrEmpty(Id) || PartInformation == null || PartInformation.Count == 0)
        {
            return;
        }

        int top = BarlineTop;
        int bottom = BarlineBottom;
        int partLen = PartInformation.Count;
        //Ignore invalid range.
        if (top > partLen || bottom > partLen || bottom < top)
        {
            top = 0;
            bottom = partLen + 1;
        }

        //Normalize.
        if (bottom == 0)
        {
            bottom = partLen + 1;
        }

        //Compute staff-to-staff bar line settings for sub-staves.
        List<(bool? Barline, bool? SpanBar)> barlines = new List<(bool?, bool?)>();
        //Start with index 1 to fit the next loop.
        barlines.Add((null, null));
        for (int i = 1; i <= partLen; i++)
        {
            if (top <= i && i <= bottom)
            {
                //Connect barlines.
                barlines.Add((Barline, true));
            }
            else
            {
                barlines.Add((Barline, SpanbarToStaffBelow));
            }
        }

        //Adjust bar line connections.
        if (bottom <= partLen)
        {
            barlines[bottom] = (barlines[bottom].Barline, SpanbarToStaffBelow);
        }

        barlines[partLen] = (barlines[partLen].Barline, SpanbarToStaffBelow);

        for (int index = 0; index < PartInformation.Count; index++)
        {
            int i = index + 1;
            LilyStaffInfo staffInfo = PartInformation[index];
            string staffId = staffInfo.StaffId;
            List<LilyVoiceInfo> voices = staffInfo.Voices;

            if (i == top)
            {
                printer.Dump("\\new PianoStaff");
                if (PartSymbol != "brace")
                {
                    string bracket
                        = PartSymbol != null
                          && SystemStartDict.TryGetValue(PartSymbol, out string named)
                            ? named
                            : null;
                    if (bracket != null)
                    {
                        printer.Dump("\\with {");
                        printer.Newline();
                        printer.Dump("systemStartDelimiter = #'" + bracket);
                        printer.Newline();
                        printer.Dump("}");
                    }
                }

                printer.Dump("<<");
                printer.Newline();
            }

            //Now comes the real definition of a part's staff (or staves).
            if (!string.IsNullOrEmpty(staffId) && !string.IsNullOrEmpty(SubStaffType))
            {
                printer.Dump("\\context " + SubStaffType + " = \"" + staffId + "\" <<");
            }
            else
            {
                printer.Dump("\\context " + StaffType + " <<");
            }

            printer.Newline();

            if (barlines[i].Barline != true)
            {
                printer.Dump("\\set Staff.measureBarType = \"-span|\"");
                printer.Newline();
            }

            if (barlines[i].SpanBar != true)
            {
                printer.Dump("\\override Staff.BarLine.allow-span-bar = ##f");
                printer.Newline();
            }

            printer.Dump("\\mergeDifferentlyDottedOn");
            printer.Newline();
            printer.Dump("\\mergeDifferentlyHeadedOn");
            printer.Newline();
            int n = 0;
            int nrVoices = voices.Count;

            bool voiceWarning = false;

            foreach (LilyVoiceInfo voice in voices)
            {
                n += 1;
                string voiceText = string.Empty;
                if (nrVoices > 1 || !string.IsNullOrEmpty(voice.CrossStaffChordVoice))
                {
                    //TODO (upstream's): Support more voices.
                    int voiceNumber = n;
                    //Cross-staff chord voices need the right stem direction, otherwise
                    //stems don't connect.
                    if ((voice.CrossStaffChordVoice == "U" && voiceNumber % 2 == 0)
                        || (voice.CrossStaffChordVoice == "D" && voiceNumber % 2 != 0))
                    {
                        voiceNumber += 1;
                    }

                    if (voiceNumber > 4)
                    {
                        if (!voiceWarning)
                        {
                            State.Warning(
                                "Only up to 4 voices per staff are supported; expect "
                                + "wrong stem directions and collisions");
                            voiceWarning = true;
                        }

                        voiceNumber = 4;
                    }

                    //TODO (upstream's): Voices might not appear in LilyPond order, i.e.,
                    //some voices might be missing! For example, if the MusicXML file
                    //contains only voices one, three, and four (in LilyPond order), this
                    //currently still results in `\voiceOne', `\voiceTwo', and
                    //`\voiceThree', causing wrong stem directions and possibly collisions.
                    //
                    //A solution might be to add some heuristics while mapping MusicXML
                    //voices to LilyPond voices, checking stem directions (irrespective of
                    //option `--no-stem-directions'). Unfortunately, this might fail
                    //spectacularly, especially in piano music with its ad-hoc polyphony,
                    //where there is no guarantee that the voice order stays the same
                    //during the whole piece.
                    //
                    //To better support piano music and the like a completely different
                    //paradigm would be necessary, also using ad-hoc polyphony on the
                    //LilyPond side (i.e., replacing global voices with local
                    //`<<...\\...>>' constructs).
                    voiceText = VoiceTextDict[voiceNumber] + " ";
                }

                printer.Dump("\\context " + VoiceCommand + " = \"" + voice.VoiceName + "\"");
                string transpose = State.GetTransposeString();
                if (!string.IsNullOrEmpty(transpose))
                {
                    printer.Dump(transpose);
                }

                printer.Dump("{");
                printer.Newline();
                printer.Dump(voiceText + "\\" + voice.VoiceName);
                printer.Newline();
                printer.Dump("}");
                printer.Newline();
                foreach ((string lyricName, string stanzaId, string placement) in voice.Lyrics)
                {
                    printer.Dump("\\new Lyrics");
                    if (placement == "above")
                    {
                        printer.Dump("\\with {");
                        printer.Newline();
                        string alignId = StaffType == "PianoStaff" ? staffId : Id;
                        printer.Dump("alignAboveContext = \"" + alignId + "\"");
                        printer.Newline();
                        printer.Dump("}");
                    }

                    printer.Dump("\\lyricsto \"" + voice.VoiceName + "\" {");
                    printer.Newline();
                    if (!string.IsNullOrEmpty(stanzaId))
                    {
                        printer.Dump("\\stanza \"" + stanzaId + "\"");
                    }

                    printer.Dump("\\" + lyricName);
                    printer.Newline();
                    printer.Dump("}");
                    printer.Newline();
                }

                if (!string.IsNullOrEmpty(voice.FiguredBassName))
                {
                    printer.Dump(
                        "\\context FiguredBass = \"" + voice.FiguredBassName + "\" \\"
                        + voice.FiguredBassName);
                }
            }

            printer.Dump(">>");
            printer.Newline();

            if (i == bottom)
            {
                printer.Dump(">>");
                printer.Newline();
            }
        }
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (PartInformation != null && PartInformation.Count > 1)
        {
            //The group delimiter of a multi-staff part is not controlled by
            //`<group-symbol>' but by `<part-symbol>'. The bar line is continuous within
            //the group delimiter if not overridden by `<group-barline>'.
            Symbol = BarlineTop != 0 || BarlineBottom != 0 ? "none" : PartSymbol;

            StaffType = "PianoStaff";
            SubStaffType = "Staff";
        }

        if (Symbol != "brace")
        {
            string bracket = Symbol != null && SystemStartDict.TryGetValue(Symbol, out string named)
                ? named
                : null;
            if (bracket != null)
            {
                AddContextModification("systemStartDelimiter = #'" + bracket);
            }
        }

        base.PrintLy(printer);
    }
}

/// <summary>A tablature staff.</summary>
internal sealed class LilyTabStaff : LilyStaff
{
    /// <summary>Builds the staff.</summary>
    /// <param name="state">The import this staff belongs to.</param>
    /// <param name="command">Which LilyPond context the staff becomes.</param>
    internal LilyTabStaff(MusicXmlImportState state, string command = "TabStaff")
        : base(state, command)
        => VoiceCommand = "TabVoice";

    /// <summary>How the instrument's strings are tuned.</summary>
    internal List<LilyPitch> StringTunings { get; set; } = new List<LilyPitch>();

    /// <summary>Which tablature format the staff is drawn with.</summary>
    internal string TablatureFormat { get; set; }

    /// <inheritdoc/>
    internal override void PrintLyOverrides(LilyOutputPrinter printer)
    {
        if ((StringTunings != null && StringTunings.Count > 0)
            || !string.IsNullOrEmpty(TablatureFormat))
        {
            printer.Dump("\\with {");
            printer.Newline();
            if (StringTunings != null && StringTunings.Count > 0)
            {
                printer.Dump("stringTunings = #`(");
                foreach (LilyPitch pitch in StringTunings)
                {
                    printer.Dump("," + pitch.LispExpression());
                }

                printer.Dump(")");
                printer.Newline();
            }

            if (!string.IsNullOrEmpty(TablatureFormat))
            {
                printer.Dump("tablatureFormat = #" + TablatureFormat);
                printer.Newline();
            }

            printer.Dump("}");
        }
    }
}

/// <summary>A drum staff.</summary>
internal sealed class LilyDrumStaff : LilyStaff
{
    /// <summary>Builds the staff.</summary>
    /// <param name="state">The import this staff belongs to.</param>
    /// <param name="command">Which LilyPond context the staff becomes.</param>
    internal LilyDrumStaff(MusicXmlImportState state, string command = "DrumStaff")
        : base(state, command)
        => VoiceCommand = "DrumVoice";

    /// <summary>Which drum style table the staff is drawn with.</summary>
    internal string DrumStyleTable { get; set; }

    /// <inheritdoc/>
    internal override void PrintLyOverrides(LilyOutputPrinter printer)
    {
        if (!string.IsNullOrEmpty(DrumStyleTable))
        {
            printer.Dump("\\with {");
            printer.Dump("drumStyleTable = #" + DrumStyleTable);
            printer.Dump("}");
        }
    }
}

/// <summary>A staff that carries rhythm alone.</summary>
internal sealed class LilyRhythmicStaff : LilyStaff
{
    /// <summary>Builds the staff.</summary>
    /// <param name="state">The import this staff belongs to.</param>
    /// <param name="command">Which LilyPond context the staff becomes.</param>
    internal LilyRhythmicStaff(MusicXmlImportState state, string command = "RhythmicStaff")
        : base(state, command)
    {
    }
}

/// <summary>The whole score.</summary>
internal sealed class LilyScore : LilyExpression
{
    /// <summary>Builds the score.</summary>
    /// <param name="state">The import this score belongs to.</param>
    internal LilyScore(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The staves and groups the score is made of.</summary>
    internal LilyStaffGroup Contents { get; private set; }

    /// <summary>Whether a MIDI block is written.</summary>
    internal bool CreateMidi { get; private set; }

    /// <summary>
    /// The tempo the MIDI block is written with, in beats per minute.
    /// </summary>
    /// <remarks>
    /// ⚠ Upstream declares this only in <c>set_tempo</c>, so a score whose tempo was
    /// never set raises <c>AttributeError</c> in <c>print_ly</c>. The converter always
    /// sets it; a null here reads the same way.
    /// </remarks>
    internal string Tempo { get; private set; }

    /// <inheritdoc/>
    internal override bool Contains(LilyExpression element)
    {
        if (this == element)
        {
            return true;
        }

        return Contents != null && Contents.Contains(element);
    }

    /// <summary>Records what the score is made of.</summary>
    /// <param name="contents">The staves and groups.</param>
    internal void SetContents(LilyStaffGroup contents) => Contents = contents;

    /// <summary>Finds the staff a part became.</summary>
    /// <param name="partId">The part's identifier.</param>
    /// <returns>The staff, or null.</returns>
    internal LilyStaffGroup FindPart(string partId) => Contents?.FindPart(partId);

    /// <summary>Records the tempo the MIDI block is written with.</summary>
    /// <param name="tempo">The value of the tempo, in beats per minute.</param>
    internal void SetTempo(string tempo) => Tempo = tempo;

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        CreateMidi = State.CreateMidi;
        if (State.GetBook())
        {
            printer.Dump("\\book {");
            printer.Newline();
        }

        printer.Dump("\\score {");
        printer.Newline();
        //Prints opening <<:
        printer.Dump("<<");
        printer.Newline();
        if (Contents != null)
        {
            Contents.PrintLy(printer);
        }

        printer.Dump(">>");
        printer.Newline();
        printer.Dump("\\layout {}");
        printer.Newline();
        //If the --midi option was not passed to musicxml2ly, that comments the "midi"
        //line.
        if (CreateMidi)
        {
            printer.Dump("}");
            printer.Newline();
            printer.Dump("\\score {");
            printer.Newline();
            printer.Dump("\\unfoldRepeats \\articulate {");
            printer.Newline();
            Contents.PrintLy(printer);
            printer.Dump("}");
            printer.Newline();
        }
        else
        {
            printer.Dump("% To create MIDI output, uncomment the following line:");
            printer.Newline();
            printer.Dump("%");
        }

        printer.Dump("\\midi { \\tempo 4 = " + Tempo + " }");
        printer.Newline();
        printer.Dump("}");
        printer.Newline();
        if (State.GetBook())
        {
            printer.Dump("}");
            printer.Newline();
        }
    }
}
