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
using CodeBrix.LilyPort.ConvertLy;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (staff_attributes_to_string_tunings, staff_attributes_to_lily_staff, extract_instrument_sound, extract_score_structure, musicxml_id_to_lily and the naming helpers);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

internal sealed partial class MusicXmlConverter
{
    private static readonly string[] IdDigits
        = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };

    /// <summary>Reads a tablature staff's string tunings.</summary>
    /// <param name="mxlAttributes">The attributes in force at the start of the staff.</param>
    /// <returns>The tunings, lowest string last, as LilyPond wants them.</returns>
    internal List<LilyPitch> StaffAttributesToStringTunings(MusicXmlAttributes mxlAttributes)
    {
        MusicXmlNode details = mxlAttributes.GetMaybeExistNamedChild("staff-details");
        if (details == null)
        {
            return new List<LilyPitch>();
        }

        int lines = 6;
        MusicXmlNode staffLines = details.GetMaybeExistNamedChild("staff-lines");
        if (staffLines != null)
        {
            lines = int.Parse(staffLines.GetText().Trim(), CultureInfo.InvariantCulture);
        }

        //⚠ python's `[Pitch()] * lines' makes ONE object and repeats the REFERENCE, so
        //every unset line shares a single pitch. Reproduced: the reference is what
        //reaches the output when a document leaves a line untuned.
        LilyPitch shared = new LilyPitch(State);
        List<LilyPitch> tunings = Enumerable.Repeat(shared, lines).ToList();

        foreach (MusicXmlNode tuning in details.GetNamedChildren("staff-tuning"))
        {
            LilyPitch pitch = new LilyPitch(State);
            int line = 0;
            string lineText = tuning.Attribute("line");
            if (lineText != null
                && int.TryParse(
                    lineText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int parsedLine))
            {
                line = parsedLine - 1;
            }

            tunings[line] = pitch;

            string step = tuning.GetNamedChild("tuning-step").GetText().Trim();
            pitch.Step = MusicXmlConversion.MusicXmlStepToLily(step).Value;

            string octave = tuning.GetNamedChild("tuning-octave").GetText().Trim();
            pitch.Octave = int.Parse(octave, CultureInfo.InvariantCulture) - 4;

            MusicXmlNode alter = tuning.GetNamedChild("tuning-alter");
            if (alter != null)
            {
                pitch.Alteration = int.Parse(
                    alter.GetText().Trim(), CultureInfo.InvariantCulture);
            }
        }

        //lilypond seems to use the opposite ordering than MusicXML...
        tunings.Reverse();
        return tunings;
    }

    /// <summary>Builds the staff a part's first measure's attributes call for.</summary>
    /// <param name="mxlAttributes">The attributes by staff, or null.</param>
    /// <returns>The staff.</returns>
    /// <remarks>
    /// We only distinguish between <c>TabStaff</c> and <c>Staff</c>. Changing from a
    /// tablature to a normal staff (and vice versa) in the middle of a piece is allowed
    /// by MusicXML, but let's simplify the code by not supporting such peculiarities.
    /// <para>
    /// Note that we can't use <c>RhythmicStaff</c> or <c>DrumStaff</c>. For the former,
    /// there is no guarantee that notes are always placed on its single staff line
    /// (which <c>RhythmicStaff</c> enforces). For the latter, MusicXML doesn't provide a
    /// set of pre-defined 'drums' that could be used in <c>\drummode</c>, and
    /// <c>DrumStaff</c> also squeezes normal pitches to be positioned on its middle staff
    /// line.
    /// </para>
    /// </remarks>
    internal LilyStaff StaffAttributesToLilyStaff(
        PythonDictionary<string, MusicXmlAttributes> mxlAttributes)
    {
        if (mxlAttributes == null || mxlAttributes.Count == 0)
        {
            return new LilyStaff(State);
        }

        MusicXmlAttributes attributes = mxlAttributes.Items().First().Value;

        string clefSign = null;
        MusicXmlNode clef = attributes.GetMaybeExistNamedChild("clef");
        if (clef != null)
        {
            MusicXmlNode sign = clef.GetMaybeExistNamedChild("sign");
            if (sign != null)
            {
                clefSign = sign.GetText();
            }
        }

        LilyStaff staff;
        if (clefSign == "TAB")
        {
            LilyTabStaff tabStaff = new LilyTabStaff(State);
            tabStaff.StringTunings = StaffAttributesToStringTunings(attributes);
            staff = tabStaff;
        }
        else
        {
            staff = new LilyStaff(State);
        }

        //We support `<part-symbol>' only at the beginning of a part.
        MusicXmlNode partSymbol = attributes.GetMaybeExistNamedChild("part-symbol");
        if (partSymbol != null)
        {
            staff.PartSymbol = partSymbol.GetText();
            staff.BarlineTop = int.Parse(
                partSymbol.Attribute("top-staff", "0"), CultureInfo.InvariantCulture);
            staff.BarlineBottom = int.Parse(
                partSymbol.Attribute("bottom-staff", "0"), CultureInfo.InvariantCulture);
        }

        return staff;
    }

    /// <summary>Which MIDI instrument a part sounds with.</summary>
    /// <param name="scorePart">The part's entry in the part list.</param>
    /// <returns>The instrument, or null.</returns>
    internal static string ExtractInstrumentSound(MusicXmlNode scorePart)
    {
        MusicXmlNode scoreInstrument
            = scorePart.GetMaybeExistNamedChild("score-instrument");
        if (scoreInstrument == null)
        {
            return null;
        }

        MusicXmlNode sound = scoreInstrument.GetMaybeExistNamedChild("instrument-sound");
        return sound != null
            ? MusicXmlUtilities.MusicXmlSoundToLilyPondMidiInstrument(sound.GetText())
            : null;
    }

    /// <summary>Builds the score's staves and groups out of the part list.</summary>
    /// <param name="partList">The part list.</param>
    /// <param name="staffInfo">The attributes in force at each part's start.</param>
    /// <returns>The score.</returns>
    /// <remarks>
    /// ⚠ Upstream returns the STRUCTURE rather than the score when the part list is
    /// empty, and the score otherwise; every caller uses the answer as a score. The port
    /// answers the score in both cases and records the difference, because a
    /// <c>StaffGroup</c> where a <c>Score</c> is expected is a crash upstream reaches
    /// only for a document with no part list at all — which the schema forbids.
    /// </remarks>
    internal LilyScore ExtractScoreStructure(
        MusicXmlNode partList,
        Dictionary<string, PythonDictionary<string, MusicXmlAttributes>> staffInfo)
    {
        LilyScore score = new LilyScore(State);
        LilyStaffGroup structure = new LilyStaffGroup(State, null);
        score.SetContents(structure);

        if (partList == null)
        {
            return score;
        }

        LilyStaff ReadScorePart(MusicXmlNode element)
        {
            if (!(element is MusicXmlScorePart))
            {
                return null;
            }

            //Depending on the attributes of the first measure we create different types
            //of staves (`Staff' or `TabStaff').
            string partId = element.Attribute("id");
            PythonDictionary<string, MusicXmlAttributes> attributes
                = partId != null && staffInfo.TryGetValue(partId, out var found) ? found : null;
            LilyStaff staff = StaffAttributesToLilyStaff(attributes);
            if (staff == null)
            {
                return null;
            }

            staff.Id = partId;

            string instrumentNameText = string.Empty;

            MusicXmlNode partName = element.GetMaybeExistNamedChild("part-name");
            //Finale gives unnamed parts the name "MusicXML Part" automatically!
            if (partName != null && partName.GetText() != "MusicXML Part")
            {
                instrumentNameText = partName.GetText();
                staff.InstrumentName = LilyMarkup.TextToLy(
                    State,
                    new List<LilyMarkupElement>
                    {
                        new LilyMarkupElement(
                            partName, LilyMarkupElement.CopyAttributes(partName)),
                    });
            }

            //`<part-name-display>' overrides `<part-name>'.
            partName = element.GetMaybeExistNamedChild("part-name-display");
            if (partName != null)
            {
                if (partName.Attribute("print-object", "yes") == "yes")
                {
                    instrumentNameText = ExtractDisplayText(partName);
                    staff.InstrumentName = ExtractDisplayMarkup(partName);
                }
                else
                {
                    staff.InstrumentName = null;
                }
            }

            if (Options.Midi)
            {
                staff.Sound = ExtractInstrumentSound(element);
            }

            //TODO (upstream's): Replace this very rough estimate with the first
            //per-system left margin value:
            //
            //  <print> -> <system-layout> -> <left-margin>
            if (!string.IsNullOrEmpty(instrumentNameText))
            {
                State.Paper.Indent = Math.Max(
                    State.Paper.Indent, MusicXmlUtilities.PythonLength(instrumentNameText));
                State.Paper.InstrumentNames.Add(instrumentNameText);
            }

            string shortInstrumentNameText = string.Empty;

            MusicXmlNode partShort = element.GetMaybeExistNamedChild("part-abbreviation");
            if (partShort != null)
            {
                shortInstrumentNameText = partShort.GetText();
                staff.ShortInstrumentName = LilyMarkup.TextToLy(
                    State,
                    new List<LilyMarkupElement>
                    {
                        new LilyMarkupElement(
                            partShort, LilyMarkupElement.CopyAttributes(partShort)),
                    });
            }

            //`<part-abbreviation-display>' overrides `<part-abbreviation>'
            partShort = element.GetMaybeExistNamedChild("part-abbreviation-display");
            if (partShort != null)
            {
                if (partShort.Attribute("print-object", "yes") == "yes")
                {
                    shortInstrumentNameText = ExtractDisplayText(partShort);
                    staff.ShortInstrumentName = ExtractDisplayMarkup(partShort);
                }
                else
                {
                    staff.ShortInstrumentName = null;
                }
            }

            //TODO (upstream's): Read in the MIDI device / instrument

            //TODO (upstream's): Replace this very rough estimate with the global left
            //margin value:
            //
            //  <defaults> -> <system-layout> -> <left-margin>
            if (!string.IsNullOrEmpty(shortInstrumentNameText))
            {
                State.Paper.ShortIndent = Math.Max(
                    State.Paper.ShortIndent,
                    MusicXmlUtilities.PythonLength(shortInstrumentNameText));
            }

            return staff;
        }

        LilyStaffGroup ReadScoreGroup(MusicXmlNode element)
        {
            if (!(element is MusicXmlPartGroup))
            {
                return null;
            }

            LilyStaffGroup group = new LilyStaffGroup(State);
            string groupId = element.Attribute("number");
            if (groupId != null)
            {
                group.Id = groupId;
            }

            MusicXmlNode groupName = element.GetMaybeExistNamedChild("group-name");
            if (groupName != null)
            {
                group.InstrumentName = LilyMarkup.TextToLy(
                    State,
                    new List<LilyMarkupElement>
                    {
                        new LilyMarkupElement(
                            groupName, LilyMarkupElement.CopyAttributes(groupName)),
                    });
            }

            //`<group-name-display>' overrides `<group-name>'.
            groupName = element.GetMaybeExistNamedChild("group-name-display");
            if (groupName != null)
            {
                group.InstrumentName = groupName.Attribute("print-object", "yes") == "yes"
                    ? ExtractDisplayMarkup(groupName)
                    : null;
            }

            MusicXmlNode groupShort = element.GetMaybeExistNamedChild("group-abbreviation");
            if (groupShort != null)
            {
                group.ShortInstrumentName = LilyMarkup.TextToLy(
                    State,
                    new List<LilyMarkupElement>
                    {
                        new LilyMarkupElement(
                            groupShort, LilyMarkupElement.CopyAttributes(groupShort)),
                    });
            }

            //`<group-abbreviation-display>' overrides `<group-abbreviation>'.
            groupShort = element.GetMaybeExistNamedChild("group-abbreviation-display");
            if (groupShort != null)
            {
                group.ShortInstrumentName
                    = groupShort.Attribute("print-object", "yes") == "yes"
                        ? ExtractDisplayMarkup(groupShort)
                        : null;
            }

            MusicXmlNode groupSymbol = element.GetMaybeExistNamedChild("group-symbol");
            if (groupSymbol != null)
            {
                group.Symbol = groupSymbol.GetText();
            }

            return group;
        }

        List<MusicXmlNode> partGroups = partList.GetAllChildren();

        //`<part-group>' elements are not nested; they describe ranges instead: consider
        //them as commands that switch on a feature while being active.
        //
        //* For staff delimiters this implies a cumulative effect. However, LilyPond only
        //  supports nested delimiters, which means that some (hypothetical) MusicXML
        //  layouts cannot be realized. For example, group 1 can contain a bracket from
        //  staff 1 to 5, while group 2 holds a brace from staff 2 to 6. In LilyPond, the
        //  brace can only span staff 2 to 5.
        //
        //* For bar lines this implies physical overwriting: A continuous bar line always
        //  overwrites Mensurstriche. In LilyPond, we implement bar line handling by
        //  setting them staff by staff.

        //1) Replace all `Score_part' objects by their corresponding `Staff' objects,
        //   collect all start and stop points of groups that contain `<group-symbol>',
        //   `<group-name>', etc., and put this data into one `PartGroupInfo' object.
        //   However, exclude `<group-barline>' and collect this information separately in
        //   a local array.
        List<LilyExpression> staves = new List<LilyExpression>();
        int staffIdx = 0;

        //Elements: [type, start_staff, end_staff].
        List<(string Type, int StartStaff, int? EndStaff)> barlines
            = new List<(string, int, int?)>();
        int barlineIdx = 0;
        //Mapping group IDs to `barlines' indices.
        Dictionary<string, int> barlineIds = new Dictionary<string, int>(StringComparer.Ordinal);

        HashSet<string> symbolIds = new HashSet<string>(StringComparer.Ordinal);

        LilyPartGroupInfo groupInfo = new LilyPartGroupInfo(State);
        foreach (MusicXmlNode element in partGroups)
        {
            if (element is MusicXmlScorePart)
            {
                if (!groupInfo.IsEmpty())
                {
                    staves.Add(groupInfo);
                    groupInfo = new LilyPartGroupInfo(State);
                }

                LilyStaff staff = ReadScorePart(element);
                if (staff != null)
                {
                    staves.Add(staff);
                }

                staffIdx += 1;
            }
            else if (element is MusicXmlPartGroup)
            {
                string numberAttr = element.Attribute("number", "1");

                if (element.Attribute("type") == "start")
                {
                    if (element.GetAllChildren().Any(
                            c => c is MusicXmlGroupName || c is MusicXmlGroupNameDisplay
                                 || c is MusicXmlGroupAbbreviation
                                 || c is MusicXmlGroupAbbreviationDisplay
                                 || c is MusicXmlGroupSymbol))
                    {
                        groupInfo.AddStart(element);
                        symbolIds.Add(numberAttr);
                    }

                    MusicXmlNode groupBarline
                        = element.GetMaybeExistNamedChild("group-barline");
                    string barlineType = null;
                    if (groupBarline != null)
                    {
                        barlineType = groupBarline.GetText();
                    }

                    if (!string.IsNullOrEmpty(barlineType))
                    {
                        barlines.Add((barlineType, staffIdx, null));
                        barlineIds[numberAttr] = barlineIdx;
                        barlineIdx += 1;
                    }
                }
                else if (element.Attribute("type") == "stop")
                {
                    if (symbolIds.Contains(numberAttr))
                    {
                        groupInfo.AddEnd(element);
                        symbolIds.Remove(numberAttr);
                    }

                    if (barlineIds.TryGetValue(numberAttr, out int idx))
                    {
                        barlines[idx] = (barlines[idx].Type, barlines[idx].StartStaff, staffIdx);
                        barlineIds.Remove(numberAttr);
                    }
                }
            }
        }

        if (!groupInfo.IsEmpty())
        {
            staves.Add(groupInfo);
        }

        //2) Apply bar line settings to all `Staff' objects and convert them to
        //   staff-to-staff values.
        List<LilyStaff> realStaves = staves.OfType<LilyStaff>().ToList();
        foreach ((string type, int startStaff, int? endStaff) in barlines)
        {
            if (type == "Mensurstrich")
            {
                for (int idx = startStaff; idx < endStaff.Value - 1; idx++)
                {
                    if (realStaves[idx].Barline != true)
                    {
                        realStaves[idx].Barline = false;
                    }

                    realStaves[idx].SpanbarToStaffBelow = true;
                }

                if (realStaves[endStaff.Value - 1].Barline != true)
                {
                    realStaves[endStaff.Value - 1].Barline = false;
                }
            }
            else if (type == "yes")
            {
                for (int idx = startStaff; idx < endStaff.Value - 1; idx++)
                {
                    realStaves[idx].Barline = true;
                    realStaves[idx].SpanbarToStaffBelow = true;
                }

                realStaves[endStaff.Value - 1].Barline = true;
            }
        }

        //Normalize unset values to barline type 'no'.
        foreach (LilyStaff realStaff in realStaves)
        {
            if (!realStaff.Barline.HasValue)
            {
                realStaff.Barline = true;
            }

            if (!realStaff.SpanbarToStaffBelow.HasValue)
            {
                realStaff.SpanbarToStaffBelow = false;
            }
        }

        //3) Detect staff delimiter groups.
        List<int> groupStarts = new List<int>();
        int pos = 0;
        while (pos < staves.Count)
        {
            LilyExpression element = staves[pos];
            if (element is LilyPartGroupInfo info)
            {
                int prevStart = 0;
                if (groupStarts.Count > 0)
                {
                    prevStart = groupStarts[groupStarts.Count - 1];
                }
                else if (info.End.Count > 0)
                {
                    //No group to end here.
                    info.End = new PythonDictionary<string, MusicXmlNode>();
                }

                if (info.End.Count > 0)
                {
                    //Closes an existing group.
                    LilyPartGroupInfo previous = (LilyPartGroupInfo)staves[prevStart];
                    List<string> ends = info.End.Keys.ToList();
                    List<string> prevStarted = previous.Start.Keys.ToList();
                    List<string> intersection = prevStarted.Where(ends.Contains).ToList();
                    string grpId;
                    if (intersection.Count > 0)
                    {
                        grpId = intersection[0];
                    }
                    else
                    {
                        //Close the last started group.
                        grpId = previous.Start.Keys[0];
                        //Find the corresponding closing tag and remove it!
                        int j = pos + 1;
                        bool foundClosing = false;
                        while (j < staves.Count && !foundClosing)
                        {
                            if (staves[j] is LilyPartGroupInfo other
                                && other.End.ContainsKey(grpId))
                            {
                                foundClosing = true;
                                other.End.Remove(grpId);
                                if (other.IsEmpty())
                                {
                                    staves.RemoveAt(j);
                                }
                            }

                            j += 1;
                        }
                    }

                    MusicXmlNode grpObj = previous.Start[grpId];
                    LilyStaffGroup group = ReadScoreGroup(grpObj);
                    //Remove the id from both the start and end.
                    if (info.End.ContainsKey(grpId))
                    {
                        info.End.Remove(grpId);
                    }

                    previous.Start.Remove(grpId);
                    if (info.IsEmpty())
                    {
                        staves.RemoveAt(pos);
                    }

                    //Replace the staves with the whole group.
                    for (int j = prevStart + 1; j < pos; j++)
                    {
                        group.AppendStaff(staves[j]);
                    }

                    staves.RemoveRange(prevStart + 1, pos - (prevStart + 1));
                    staves.Insert(prevStart + 1, group);
                    //Reset pos so that we continue at the correct position.
                    pos = prevStart;
                    //Remove an empty start group.
                    if (((LilyPartGroupInfo)staves[prevStart]).IsEmpty())
                    {
                        staves.RemoveAt(prevStart);
                        groupStarts.Remove(prevStart);
                        pos -= 1;
                    }
                }
                else if (info.Start.Count > 0)
                {
                    //Starts new part groups.
                    groupStarts.Add(pos);
                }
            }

            pos += 1;
        }

        foreach (LilyExpression staff in staves)
        {
            structure.AppendStaff(staff);
        }

        return score;
    }

    /// <summary>Turns a MusicXML identifier into one LilyPond accepts.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The LilyPond identifier.</returns>
    internal static string MusicXmlIdToLily(string id)
    {
        for (int d = 0; d < IdDigits.Length; d++)
        {
            id = PythonRegex.Sub(
                d.ToString(CultureInfo.InvariantCulture), IdDigits[d], id);
        }

        return PythonRegex.Sub("[^a-zA-Z]", "X", id);
    }

    /// <summary>The LilyPond identifier one voice of one part is written under.</summary>
    /// <param name="partId">The part.</param>
    /// <param name="name">The voice.</param>
    /// <returns>The identifier.</returns>
    internal static string MusicXmlVoiceNameToLilyName(string partId, string name)
        => MusicXmlIdToLily("Part" + partId + "Voice" + name);

    /// <summary>The LilyPond identifier one lyric line is written under.</summary>
    /// <param name="partId">The part.</param>
    /// <param name="name">The voice.</param>
    /// <param name="lyricsNumber">Which lyric line.</param>
    /// <returns>The identifier.</returns>
    internal static string MusicXmlLyricsNameToLilyName(
        string partId, string name, string lyricsNumber)
        => MusicXmlIdToLily("Part" + partId + "Voice" + name + "Lyrics" + lyricsNumber);

    /// <summary>The LilyPond identifier one figured-bass line is written under.</summary>
    /// <param name="partId">The part.</param>
    /// <param name="voiceName">The voice.</param>
    /// <returns>The identifier.</returns>
    internal static string MusicXmlFiguredBassNameToLilyName(string partId, string voiceName)
        => MusicXmlIdToLily("Part" + partId + "Voice" + voiceName + "FiguredBass");

    /// <summary>The LilyPond identifier one chord-names line is written under.</summary>
    /// <param name="partId">The part.</param>
    /// <param name="voiceName">The voice.</param>
    /// <returns>The identifier.</returns>
    internal static string MusicXmlChordNamesNameToLilyName(string partId, string voiceName)
        => MusicXmlIdToLily("Part" + partId + "Voice" + voiceName + "Chords");

    /// <summary>The LilyPond identifier one fretboards line is written under.</summary>
    /// <param name="partId">The part.</param>
    /// <param name="voiceName">The voice.</param>
    /// <returns>The identifier.</returns>
    internal static string MusicXmlFretBoardsNameToLilyName(string partId, string voiceName)
        => MusicXmlIdToLily("Part" + partId + "Voice" + voiceName + "FretBoards");
}
