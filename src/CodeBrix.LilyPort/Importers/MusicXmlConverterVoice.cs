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
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.ConvertLy;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (extract_lyrics and musicxml_voice_to_lily_voice);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

internal sealed partial class MusicXmlConverter
{
    /// <summary>Collects one numbered lyric line out of a voice.</summary>
    /// <param name="voice">The voice.</param>
    /// <param name="lyricKey">Which lyric line.</param>
    /// <param name="lyricsDict">Where to record the line.</param>
    internal void ExtractLyrics(
        MusicXmlVoice voice, string lyricKey,
        Dictionary<string, (List<string> Result, string StanzaId, string Placement)>
            lyricsDict)
    {
        List<string> result = new List<string>();

        string baseType = null;
        string action = "store";
        string text = string.Empty;
        string placement = null;
        bool placementWarning = false;

        foreach (MusicXmlNode elem in voice.Elements)
        {
            if (!(elem is MusicXmlNote))
            {
                continue;
            }

            List<MusicXmlNode> lyrics = elem.Has("lyric")
                ? elem.GetList("lyric")
                : new List<MusicXmlNode>();

            //There are three attributes that control the visibility of lyrics:
            //`print-object' of `<note>', `print-lyric' of `<note>', and `print-object' of
            //`<lyric>'. All attributes have 'yes' as the default value, and the
            //lower-level (i.e., nested) value overrides the higher-level value. If
            //`print-lyric' is set to 'no', the default of the `<lyric>'s `print-object'
            //attribute changes to 'no'.
            //
            //See https://github.com/w3c/musicxml/issues/592 for more.
            string notePrintObject = elem.Attribute("print-object", "yes");
            string notePrintLyric = elem.Attribute("print-lyric", notePrintObject);

            bool isRest = elem.Has("rest");
            string type;
            if (elem.Has("chord"))
            {
                type = isRest ? "chord rest" : "chord note";
            }
            else if (isRest)
            {
                type = "main rest";
                if (lyrics.Count > 0)
                {
                    State.Warning("rests with lyrics are not supported yet");
                    lyrics = new List<MusicXmlNode>();
                }
            }
            else
            {
                type = "main note";
            }

            if (baseType == "main rest")
            {
                if (type != "main note")
                {
                    action = "ignore";
                }
            }
            else if (baseType == "main note")
            {
                if (type == "main rest" || type == "main note")
                {
                    action = "emit";
                }
            }

            if (type == "main rest" || type == "main note")
            {
                baseType = type;
            }

            if (action == "ignore")
            {
                action = "store";
                continue;
            }

            if (action == "emit")
            {
                if (text.Length > 0)
                {
                    result.Add(text);
                    text = string.Empty;
                }
                else
                {
                    result.Add(" \\skip1 ");
                }
            }

            foreach (MusicXmlNode lyric in lyrics)
            {
                string printObject = lyric.Attribute("print-object", notePrintLyric);
                if (printObject != "yes")
                {
                    continue;
                }

                //If there is more than a single entry with the same `number' attribute,
                //the existing one gets overwritten. Note that we ignore the `name'
                //attribute.
                if (lyric.Attribute("number", "1") == lyricKey)
                {
                    //We set the vertical lyrics position based on the `placement'
                    //attribute of the very first `<lyric>' element. This is quite
                    //inflexible, unfortunately, but LilyPond doesn't have any real support
                    //for lyrics positioning that jumps between above and below a staff.
                    string placementAttr = lyric.Attribute("placement", "below");
                    if (placement == null)
                    {
                        placement = placementAttr;
                    }
                    else if (placement != placementAttr && !placementWarning)
                    {
                        State.Warning(
                            "cannot change vertical position of lyrics within a part");
                        placementWarning = true;
                    }

                    text = MusicXmlLyricsToText(lyric);
                }
            }

            action = "store";
        }

        if (action == "store" && baseType == "main note")
        {
            result.Add(text.Length > 0 ? text : " \\skip1 ");
        }

        //We apply a heuristic regular expression to get a stanza number from the first
        //syllable; we search for strings like '1.', '1.-3.', '2., 3.', and '1./3./5.',
        //with possible whitespace characters inbetween.
        string stanzaId = null;
        //⚠ python indexes result[0] unguarded; an empty list raises IndexError and the
        //script ends without writing a file.
        if (result.Count == 0)
        {
            throw new ImportAbortedException(
                "no lyric syllables were collected for line '" + lyricKey + "'");
        }

        Match match = PythonRegex.Search(
            @"(?xs)"
            + @"^ \s*"
            + @"( "" )?"
            + @"( (?: [0-9]+ \s* \. \s* [-/,]? \s* )+ ) \s*"
            + @"( [^-/,] .* )",
            result[0]);
        if (match.Success)
        {
            stanzaId = match.Groups[2].Value.Trim();
            if (!match.Groups[1].Success)
            {
                //⚠ python concatenates group(1) — None here — with a string, which raises
                //TypeError; the script ends without writing a file.
                throw new ImportAbortedException(
                    "a stanza number was found on an unquoted first syllable");
            }

            result[0] = match.Groups[1].Value + match.Groups[3].Value;
        }

        lyricsDict[lyricKey] = (result, stanzaId, placement);
    }

    /// <summary>Converts one voice into the music LilyPond writes for it.</summary>
    /// <param name="voice">The voice.</param>
    /// <param name="voiceNumber">Which numbered voice of its staff this is.</param>
    /// <param name="startingGraceSkip">
    /// The skip that keeps this voice aligned with a staff whose grace notes start
    /// earlier, or null.
    /// </param>
    /// <returns>Everything the voice contributes to the document.</returns>
    internal MusicXmlVoiceData MusicXmlVoiceToLilyVoice(
        MusicXmlVoice voice, int voiceNumber, LilyDuration startingGraceSkip)
    {
        List<(LilyChordEvent Chord, MusicXmlNode Tremolo)> tremoloEvents
            = new List<(LilyChordEvent, MusicXmlNode)>();
        List<(LilyChordEvent Chord, MusicXmlTuplet Tuplet,
              MusicXmlTimeModification TimeModification, bool Visible)> tupletEvents
            = new List<(LilyChordEvent, MusicXmlTuplet, MusicXmlTimeModification, bool)>();
        MusicXmlVoiceData returnValue = new MusicXmlVoiceData();
        returnValue.VoiceData = voice;

        bool clefVisible = true;
        bool keyVisible = true;
        bool noteVisible = true;

        bool timeSignatureAtStart = false;

        //For `<unpitched>' and pitched full-measure rests.
        LilyClefChange currClef = null;

        //Track pitch alterations for cautionary accidentals without parentheses (to be
        //realized with LilyPond's `!' pitch modifier) that are not represented with
        //`<accidental cautionary="yes" parentheses="no">'. Note that this might not work
        //correctly if there are multiple voices in a single staff.
        double[] alterations = new double[7];
        double[] currAlterations = new double[7];

        //First pitch needed for relative mode (if selected in command-line options).
        LilyPitch firstPitch = null;

        //For slur management.
        int slurCount = 0;

        //Needed for melismata detection (ignore lyrics on those notes!):
        //⚠ Upstream also keeps `is_beamed' and `ignore_lyrics' here and assigns them, but
        //reads neither again: the lyrics are collected by their own pass. The port drops
        //the two write-only locals rather than carrying values nothing consults.
        bool isTied = false;
        bool isChord = false;

        //For pedal marks.
        bool pedalIsLine = false;

        //Using the staff of a voice's first note is a heuristic guess. It might fail in
        //cross-staff situations.
        string currentStaff = voice.StartStaff;

        List<LilyMusic> pendingFiguredBass = new List<LilyMusic>();
        List<LilyChordNameEvent> pendingChordnames = new List<LilyChordNameEvent>();
        List<LilyFretBoardEvent> pendingFretboards = new List<LilyFretBoardEvent>();

        bool isSingleVoice = voiceNumber == 0;

        MusicXmlVoiceBuilder voiceBuilder = new MusicXmlVoiceBuilder(State);
        MusicXmlVoiceBuilder figuredBassBuilder = new MusicXmlVoiceBuilder(State);
        MusicXmlVoiceBuilder chordnamesBuilder = new MusicXmlVoiceBuilder(State);
        MusicXmlVoiceBuilder fretboardsBuilder = new MusicXmlVoiceBuilder(State);

        if (voice.CrossStaffChordVoice != null)
        {
            State.LayoutInformation.SetContextItem(
                "PianoStaff", "\\consists \"Span_stem_engraver\"");
        }

        //Make sure that the keys in the dictionary don't get reordered, since we need the
        //correct ordering of the lyrics stanzas.
        List<string> lyricsNumbers = voice.GetLyricsNumbers();
        returnValue.LyricsOrder = lyricsNumbers;
        Dictionary<string, (List<string> Result, string StanzaId, string Placement)> lyrics
            = new Dictionary<string, (List<string>, string, string)>(StringComparer.Ordinal);
        foreach (string number in lyricsNumbers)
        {
            ExtractLyrics(voice, number, lyrics);
        }

        int lastBarCheck = -1;

        foreach (MusicXmlNode n in voice.Elements)
        {
            bool tieStarted = false;
            bool isNote = n is MusicXmlNote;
            LilyDuration noteGraceSkip = null;
            LilyDuration skipGraceSkip = null;

            LilyStaffChange staffChange = null;

            MusicXmlMusicNode musicNode = n as MusicXmlMusicNode;
            voiceBuilder.AtMeasureStart = musicNode != null
                                          && musicNode.MeasurePosition.HasValue
                                          && musicNode.MeasurePosition.Value.IsZero;

            string staff = n is MusicXmlNote || n is MusicXmlDirection || n is MusicXmlHarmony
                ? n.Get("staff", "1") as string
                : voice.StartStaff;

            if (!string.IsNullOrEmpty(staff))
            {
                if ((currentStaff != null && staff != currentStaff)
                    || (currentStaff == null && staff != voice.StartStaff))
                {
                    staffChange = new LilyStaffChange(State, staff);
                    if (n is MusicXmlDirection)
                    {
                        //Check whether we are in 'grace mode'.
                        LilyChordEvent evcGrace = voiceBuilder.LastEventChord(
                            musicNode.When.Value);
                        if (evcGrace != null && evcGrace.Elements.Count == 0
                            && evcGrace.GraceElements != null)
                        {
                            evcGrace.AppendGrace(staffChange);
                            staffChange = null;
                        }
                    }

                    if (staffChange != null && !isNote)
                    {
                        //A check for `<note>' follows later.
                        voiceBuilder.AddCommand(staffChange);
                        staffChange = null;
                    }
                }

                currentStaff = staff;
            }

            if (n is MusicXmlMeasure measure)
            {
                int num = int.TryParse(
                    measure.Attribute("number"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int parsedNumber)
                    ? parsedNumber
                    : 0;

                voiceBuilder.BarNumber = num;
                figuredBassBuilder.BarNumber = num;
                chordnamesBuilder.BarNumber = num;
                fretboardsBuilder.BarNumber = num;

                //If the previous measure only contains a skip, we have to set the measure
                //length right now, also emitting the skip.
                if (voiceBuilder.SetMeasureLength)
                {
                    LilyMeasureLengthEvent a = new LilyMeasureLengthEvent(
                        State, voiceBuilder.MeasureLength.Value);
                    PythonFraction when
                        = voiceBuilder.EndMoment + voiceBuilder.MeasureLength.Value;

                    voiceBuilder.AddIrrelevant(a);
                    voiceBuilder.JumpForward(when);
                    voiceBuilder.SetMeasureLength = false;

                    figuredBassBuilder.AddIrrelevant(a);
                    figuredBassBuilder.JumpForward(when);
                    figuredBassBuilder.SetMeasureLength = false;

                    chordnamesBuilder.AddIrrelevant(a);
                    chordnamesBuilder.JumpForward(when);
                    chordnamesBuilder.SetMeasureLength = false;

                    if (Options.Fretboards)
                    {
                        fretboardsBuilder.AddIrrelevant(a);
                        fretboardsBuilder.JumpForward(when);
                        fretboardsBuilder.SetMeasureLength = false;
                    }
                }

                if (voiceBuilder.PendingElements.Count > 0)
                {
                    //Elements emitted after the last `<note>' of a measure are to be
                    //aligned at or near the following bar line.
                    voiceBuilder.AddPendingElements(false, true);
                }

                voiceBuilder.PrevMeasureLength = voiceBuilder.MeasureLength;

                if (voiceBuilder.PrevMeasureLength.HasValue)
                {
                    //Reset a measure length change.
                    LilyMeasureLengthEvent a = new LilyMeasureLengthEvent(
                        State, PythonFraction.Zero);
                    voiceBuilder.AddIrrelevant(a);
                    figuredBassBuilder.AddIrrelevant(a);
                    chordnamesBuilder.AddIrrelevant(a);
                    if (Options.Fretboards)
                    {
                        fretboardsBuilder.AddIrrelevant(a);
                    }
                }

                if (measure.RealLength != measure.Length)
                {
                    State.NeededAdditionalDefinitions.Add("measure-length");
                    voiceBuilder.MeasureLength = measure.RealLength;

                    //Setting the measure length must be delayed until a time signature (if
                    //any) is emitted.
                    voiceBuilder.SetMeasureLength = true;
                    figuredBassBuilder.SetMeasureLength = true;
                    chordnamesBuilder.SetMeasureLength = true;
                    if (Options.Fretboards)
                    {
                        fretboardsBuilder.SetMeasureLength = true;
                    }
                }
                else
                {
                    voiceBuilder.MeasureLength = null;
                }

                continue;
            }

            if (n is MusicXmlPartial partial)
            {
                LilyPartial a = MusicXmlPartialToLily(partial.PartialLength);
                if (a != null)
                {
                    voiceBuilder.AddIrrelevant(a);
                    figuredBassBuilder.AddIrrelevant(a);
                    chordnamesBuilder.AddIrrelevant(a);
                    fretboardsBuilder.AddIrrelevant(a);
                }

                continue;
            }

            isChord = n.Has("chord");
            bool isAfterGrace = isNote && ((MusicXmlNote)n).IsAfterGrace();

            //We have to check whether we must insert a grace skip for synchronization with
            //other staves.
            if (startingGraceSkip != null)
            {
                //Either a note at the beginning of the music ...
                if (musicNode.When.Value.IsZero)
                {
                    if (isNote)
                    {
                        noteGraceSkip = startingGraceSkip;
                        startingGraceSkip = null;
                    }
                }
                else
                {
                    //... or the staff starts with a skip.
                    skipGraceSkip = startingGraceSkip;
                    startingGraceSkip = null;
                }
            }

            if (!isChord && !isAfterGrace)
            {
                voiceBuilder.JumpForward(musicNode.When.Value, skipGraceSkip);
                figuredBassBuilder.JumpForward(musicNode.When.Value);
                chordnamesBuilder.JumpForward(musicNode.When.Value);
                fretboardsBuilder.JumpForward(musicNode.When.Value);
            }

            if (n is MusicXmlKeepAlive)
            {
                continue;
            }

            if (n is MusicXmlBarline barline)
            {
                LilyMusic lastElem = voiceBuilder.Elements[voiceBuilder.Elements.Count - 1];

                MusicXmlBarlineResult result = barline.ToLilyObject();
                List<LilyExpression> barlines = result.Markers;
                List<MusicXmlNode> fermatas = result.Fermatas;

                if (fermatas.Count > 0)
                {
                    State.NeededAdditionalDefinitions.Add("for-barline");

                    voiceBuilder.AddCommand(new LilyForBarline(State));
                    foreach (MusicXmlNode f in fermatas)
                    {
                        voiceBuilder.AddCommand(MusicXmlFermataToLilyEvent(f));
                    }
                }

                if (barlines.Count > 0 && lastElem is MusicXmlMarker lastMarker)
                {
                    //Catch non-standard sequences like the following (which are normally
                    //put into a single `<barline>' element):
                    //
                    //  <barline><repeat direction="backward"/></barline>
                    //  <barline><ending type="stop"/></barline>
                    MusicXmlMarker newLastElem = null;
                    if (lastMarker is MusicXmlRepeatMarker repeatLast
                        && repeatLast.Direction == 1
                        && barlines[0] is MusicXmlEndingMarker endingFirst
                        && endingFirst.Direction == 1)
                    {
                        newLastElem = new MusicXmlRepeatEndingMarker(
                            State, repeatLast, endingFirst);
                    }
                    else if (lastMarker is MusicXmlEndingMarker endingLast
                             && endingLast.Direction == -1
                             && barlines[0] is MusicXmlRepeatMarker repeatFirst
                             && repeatFirst.Direction == -1)
                    {
                        newLastElem = new MusicXmlRepeatEndingMarker(
                            State, repeatFirst, endingLast);
                    }

                    if (newLastElem != null)
                    {
                        voiceBuilder.Elements[voiceBuilder.Elements.Count - 1] = newLastElem;
                        figuredBassBuilder.Elements[figuredBassBuilder.Elements.Count - 1]
                            = newLastElem;
                        chordnamesBuilder.Elements[chordnamesBuilder.Elements.Count - 1]
                            = newLastElem;
                        fretboardsBuilder.Elements[fretboardsBuilder.Elements.Count - 1]
                            = newLastElem;
                        continue;
                    }
                }

                currAlterations = (double[])alterations.Clone();
                foreach (LilyExpression a in barlines)
                {
                    if (a is LilyBarLine barLineEvent)
                    {
                        voiceBuilder.AddBarline(barLineEvent);
                        figuredBassBuilder.AddBarline(barLineEvent);
                        chordnamesBuilder.AddBarline(barLineEvent);
                        fretboardsBuilder.AddBarline(barLineEvent);
                    }
                    else if (a is MusicXmlMarker marker)
                    {
                        voiceBuilder.AddCommand(marker);
                        figuredBassBuilder.AddCommand(marker, false);
                        chordnamesBuilder.AddCommand(marker, false);
                        fretboardsBuilder.AddCommand(marker, false);
                    }
                }

                continue;
            }

            if (n is MusicXmlPrint)
            {
                foreach (LilyMusic a in MusicXmlPrintToLily(musicNode))
                {
                    voiceBuilder.AddCommand(a, false);
                }

                continue;
            }

            //Finish the previous measure.
            //
            //The voice's first element is always a measure element that gets filtered out
            //above.
            if (musicNode.MeasurePosition.Value.IsZero && !ReferenceEquals(n, voice.Elements[1]))
            {
                currAlterations = (double[])alterations.Clone();

                //Print bar checks between measures. We do this after the jump-forward calls
                //so that a skip filling up the previous bar (if any) has already been
                //emitted. We want to emit a bar check *after* the measure, so we start with
                //a number of 2.
                int num = voiceBuilder.BarNumber;
                if (num > 1 && num > lastBarCheck)
                {
                    voiceBuilder.AddBarCheck();
                    figuredBassBuilder.AddBarCheck();
                    chordnamesBuilder.AddBarCheck();
                    fretboardsBuilder.AddBarCheck();
                    lastBarCheck = num;
                }
            }

            if (n is MusicXmlDirection direction)
            {
                //Check whether `<direction>' has already been converted in another voice,
                //or whether it is chained with another `<direction>' element.
                if (direction.Converted || direction.Previous != null)
                {
                    continue;
                }

                direction.Converted = true;

                voiceBuilder.EmitMultiMeasureRest();

                bool? newPedalIsLine = direction.PedalIsLine();
                if (newPedalIsLine.HasValue && pedalIsLine != newPedalIsLine.Value)
                {
                    string style = newPedalIsLine.Value ? "#'bracket" : "#'text";
                    voiceBuilder.AddCommand(
                        new LilySetEvent(State, "Staff.pedalSustainStyle", style));
                    pedalIsLine = newPedalIsLine.Value;
                }

                foreach (LilyMusic directionEvent in MusicXmlDirectionToLily(direction))
                {
                    //Don't wrap `<direction>' elements with a non-zero `<offset>' child
                    //into a chord element. We need them in the main list so that the
                    //offset-linking pass finds them easily.
                    if (((ILilyWaitForNote)directionEvent).WaitForNote()
                        && ((ILilyOffsetEvent)directionEvent).Offset.IsZero)
                    {
                        voiceBuilder.AddDynamics(directionEvent);
                    }
                    else
                    {
                        voiceBuilder.AddCommand(directionEvent);
                    }
                }

                continue;
            }

            if (n is MusicXmlHarmony)
            {
                if (voiceBuilder.AtMeasureStart && voiceBuilder.MeasureLength.HasValue)
                {
                    LilyMeasureLengthEvent a = new LilyMeasureLengthEvent(
                        State, voiceBuilder.MeasureLength.Value);
                    if (chordnamesBuilder.SetMeasureLength)
                    {
                        chordnamesBuilder.AddIrrelevant(a);
                        chordnamesBuilder.SetMeasureLength = false;
                    }

                    if (fretboardsBuilder.SetMeasureLength)
                    {
                        fretboardsBuilder.AddIrrelevant(a);
                        fretboardsBuilder.SetMeasureLength = false;
                    }

                    if (!Options.Fretboards)
                    {
                        voiceBuilder.AddIrrelevant(a);
                        voiceBuilder.SetMeasureLength = false;
                    }
                }

                if (Options.Fretboards)
                {
                    //Makes fretboard diagrams in a separate FretBoards voice.
                    foreach (LilyMusic a in MusicXmlHarmonyToLilyFretboards(n))
                    {
                        pendingFretboards.Add((LilyFretBoardEvent)a);
                    }
                }
                else
                {
                    //Makes markup fretboard-diagrams inside the voice.
                    foreach (LilyMusic a in MusicXmlHarmonyToLily(n))
                    {
                        if (((ILilyWaitForNote)a).WaitForNote())
                        {
                            voiceBuilder.AddDynamics(a);
                        }
                        else
                        {
                            voiceBuilder.AddCommand(a);
                        }
                    }
                }

                foreach (LilyMusic a in MusicXmlHarmonyToLilyChordName(n))
                {
                    pendingChordnames.Add((LilyChordNameEvent)a);
                }

                continue;
            }

            if (n is MusicXmlFiguredBass)
            {
                if (voiceBuilder.AtMeasureStart && figuredBassBuilder.SetMeasureLength)
                {
                    LilyMeasureLengthEvent a = new LilyMeasureLengthEvent(
                        State, voiceBuilder.MeasureLength.Value);
                    figuredBassBuilder.AddIrrelevant(a);
                    figuredBassBuilder.SetMeasureLength = false;
                }

                LilyFiguredBassEvent figured = MusicXmlFiguredBassToLily(n);
                if (figured != null)
                {
                    pendingFiguredBass.Add(figured);
                }

                continue;
            }

            if (n is MusicXmlAttributes attributes)
            {
                foreach (LilyMusic a in MusicXmlAttributesToLily(attributes))
                {
                    int mmCount = 0;

                    if (a is LilyKeySignatureChange keyChange)
                    {
                        alterations = keyChange.GetAlterations();

                        bool keyVisibleNew = keyChange.Visible;

                        if (keyVisible && !keyVisibleNew)
                        {
                            voiceBuilder.AddCommand(
                                new LilyOmitEvent(State, "Staff.KeySignature"));
                            voiceBuilder.AddCommand(
                                new LilyOmitEvent(State, "Staff.KeyCancellation"));
                        }
                        else if (!keyVisible && keyVisibleNew)
                        {
                            voiceBuilder.AddCommand(
                                new LilyOmitEvent(State, "Staff.KeySignature", true));
                            voiceBuilder.AddCommand(
                                new LilyOmitEvent(State, "Staff.KeyCancellation", true));
                        }

                        keyVisible = keyVisibleNew;
                    }
                    else if (a is LilyClefChange clefChange)
                    {
                        currClef = clefChange;

                        if (!string.IsNullOrEmpty(currentStaff)
                            && currentStaff != voice.StartStaff)
                        {
                            //`\change' must be emitted before `\clef'.
                            voiceBuilder.AddCommand(
                                new LilyStaffChange(State, voice.StartStaff));
                            currentStaff = voice.StartStaff;
                        }

                        bool clefVisibleNew = clefChange.Visible;

                        if (clefChange.Type == "none")
                        {
                            clefVisibleNew = false;
                        }

                        if (clefVisible && !clefVisibleNew)
                        {
                            voiceBuilder.AddCommand(new LilyOmitEvent(State, "Staff.Clef"));
                        }
                        else if (!clefVisible && clefVisibleNew)
                        {
                            voiceBuilder.AddCommand(
                                new LilyOmitEvent(State, "Staff.Clef", true));
                            voiceBuilder.AddCommand(
                                new LilySetEvent(State, "Staff.forceClef", "##t", true));
                        }

                        clefVisible = clefVisibleNew;
                    }
                    else if (a is LilyMeasureStyleEvent measureStyle)
                    {
                        mmCount = measureStyle.MultipleRestLength;

                        State.LayoutInformation.SetContextItem("Score", "skipBars = ##t");
                        State.LayoutInformation.SetContextItem(
                            "Staff", "\\override MultiMeasureRest.expand-limit = 1");
                    }
                    else if (a is LilyTimeSignatureChange && musicNode.When.Value.IsZero)
                    {
                        timeSignatureAtStart = true;
                    }

                    voiceBuilder.AddCommand(a);

                    if (mmCount != 0)
                    {
                        //Adding the command might emit a previous multi-measure rest, which
                        //also sets the count to zero, so we set this variable after the
                        //call.
                        voiceBuilder.MultiMeasureCount = mmCount;
                    }
                }

                continue;
            }

            if (!(n is MusicXmlNote note))
            {
                n.Message(
                    "unexpected " + n + "; expected Note or Attributes or Barline");
                continue;
            }

            //Suppress LilyPond's default time signature if there is no time signature on
            //the MusicXML side.
            if (musicNode.When.Value.IsZero && !isChord && !timeSignatureAtStart)
            {
                voiceBuilder.AddCommand(
                    new LilyOmitEvent(State, "Staff.TimeSignature", false, true));
            }

            if (voiceBuilder.AtMeasureStart && voiceBuilder.MeasureLength.HasValue)
            {
                //Emit delayed measure length changes.
                LilyMeasureLengthEvent a = new LilyMeasureLengthEvent(
                    State, voiceBuilder.MeasureLength.Value);
                if (chordnamesBuilder.SetMeasureLength)
                {
                    chordnamesBuilder.AddIrrelevant(a);
                    chordnamesBuilder.SetMeasureLength = false;
                }

                if (fretboardsBuilder.SetMeasureLength)
                {
                    fretboardsBuilder.AddIrrelevant(a);
                    fretboardsBuilder.SetMeasureLength = false;
                }

                if (figuredBassBuilder.SetMeasureLength)
                {
                    figuredBassBuilder.AddIrrelevant(a);
                    figuredBassBuilder.SetMeasureLength = false;
                }

                if (voiceBuilder.SetMeasureLength)
                {
                    voiceBuilder.AddIrrelevant(a);
                    voiceBuilder.SetMeasureLength = false;
                }
            }

            bool isDoubleNoteTremolo = false;

            LilyRhythmicEvent mainEvent = note.ToLilyObject(
                currClef,
                State.ConversionSettings.ConvertStemDirections,
                State.ConversionSettings.ConvertRestPositions);

            if (mainEvent is LilyNoteEvent || mainEvent is LilyRestEvent)
            {
                if (!mainEvent.Visible)
                {
                    noteVisible = false;
                    if (mainEvent.Spacing)
                    {
                        State.NeededAdditionalDefinitions.Add("hide-note");
                    }
                }
                else
                {
                    noteVisible = true;
                }

                //In cross-staff situations it can happen that the note's single-voice flag
                //is set while the voice number is zero, so we test for the latter, too.
                //
                //All notes in cross-staff chord voices have the single-voice flag set by
                //definition; the stem direction is set globally for such voices.
                if (voiceNumber != 0 && note.SingleVoice.HasValue
                    && voice.CrossStaffChordVoice == null)
                {
                    if (isSingleVoice != note.SingleVoice.Value)
                    {
                        isSingleVoice = note.SingleVoice.Value;

                        int vn = isSingleVoice ? 0 : voiceNumber;
                        voiceBuilder.AddCommand(new LilyVoiceSelector(State, vn));
                    }
                }
            }

            if (mainEvent is LilyNoteEvent noteEvent)
            {
                if (!(noteEvent.Cautionary || noteEvent.Editorial))
                {
                    double alteration = noteEvent.Pitch.Alteration;
                    int step = noteEvent.Pitch.Step;

                    if (currAlterations[step] == alteration)
                    {
                        //Do we need a forced accidental?
                        if (!string.IsNullOrEmpty(noteEvent.AccidentalValue))
                        {
                            noteEvent.ForcedAccidental = true;
                        }
                    }
                    else
                    {
                        currAlterations[step] = alteration;
                    }
                }
            }

            //Do we need bracketed accidentals?
            if (mainEvent is LilyNoteEvent editorialNote && editorialNote.Editorial)
            {
                State.NeededAdditionalDefinitions.Add("make-bracketed");
            }

            MusicXmlNode grace = note.Get("grace") as MusicXmlNode;
            List<MusicXmlNode> notationsChildren = note.GetList("notations");
            MusicXmlRest rest = note.Get("rest") as MusicXmlRest;
            bool isWholeMeasureRest = rest != null && rest.IsWholeMeasure();

            if (voiceBuilder.MultiMeasureRest != null)
            {
                if (isWholeMeasureRest && voiceBuilder.MultiMeasureCount != 0
                    && grace == null && notationsChildren.Count == 0)
                {
                    voiceBuilder.MultiMeasureRest.Duration.Repeat += 1;
                    voiceBuilder.MultiMeasureCount -= 1;

                    voiceBuilder.BeginMoment = voiceBuilder.EndMoment;
                    voiceBuilder.SetDuration(note.DurationValue.Value);
                    continue;
                }

                voiceBuilder.EmitMultiMeasureRest();
            }

            //At this point we don't have an active multi-measure rest.
            if (voiceBuilder.MultiMeasureCount != 0)
            {
                if (!isWholeMeasureRest)
                {
                    State.Warning("Not enough rests for multi-measure rest count");
                    voiceBuilder.MultiMeasureCount = 0;
                }
                else
                {
                    //We ignore the rest's `measure' attribute if we have an explicit
                    //multi-measure rest.
                    ((LilyRestEvent)mainEvent).FullMeasureGlyph = true;
                }
            }
            else
            {
                string fullMeasureGlyph = rest?.Attribute("measure");

                if (isWholeMeasureRest)
                {
                    voiceBuilder.MultiMeasureCount = 1;

                    if (fullMeasureGlyph != "no")
                    {
                        ((LilyRestEvent)mainEvent).FullMeasureGlyph = true;
                    }
                }
            }

            if (voiceBuilder.MultiMeasureCount != 0
                && ((LilyRestEvent)mainEvent).Pitch != null)
            {
                LilyRestEvent restEvent = (LilyRestEvent)mainEvent;
                restEvent.YOffset = (restEvent.Pitch.Steps() - currClef.Pitch.Steps()) / 2;

                //LilyPond uses a different mechanism to control the vertical position of a
                //multi-measure (or full-measure) rest; its (MusicXML) pitch thus must not
                //affect `\relative'.
                restEvent.Pitch = null;
            }

            if (mainEvent != null && firstPitch == null)
            {
                firstPitch = GetEventPitch(mainEvent);
            }

            //The chord starts as an empty chord object that gets filled with items related
            //to the current chord (notes, beams, etc.) while iterating over elements of the
            //voice.
            LilyChordEvent evChord = voiceBuilder.LastEventChord(musicNode.When.Value);
            if (evChord == null)
            {
                evChord = new LilyChordEvent(State);
                evChord.When = musicNode.When.Value;
                if (voiceBuilder.MultiMeasureCount != 0)
                {
                    voiceBuilder.MultiMeasureEvChord = evChord;
                    voiceBuilder.MultiMeasureRest = (LilyRestEvent)mainEvent;
                    voiceBuilder.BeginMoment = voiceBuilder.EndMoment;
                    voiceBuilder.SetDuration(note.DurationValue.Value);
                }
                else
                {
                    voiceBuilder.AddMusic(
                        evChord, note.DurationValue.Value, true, grace);
                }
            }
            else
            {
                //This catches '<grace note> <dynamics> <main note>'.
                if (voiceBuilder.PendingElements.Count > 0 && !note.Has("chord"))
                {
                    voiceBuilder.AddPendingElements();
                }
            }

            if (grace != null || noteGraceSkip != null)
            {
                LilyChordEvent graceChord = null;

                //After-graces and other graces use different lists; depending on whether we
                //have a chord or not, obtain either a new chord or the previous one to
                //create a chord.
                if (isAfterGrace)
                {
                    if (evChord.AfterGraceElements != null && isChord)
                    {
                        graceChord = evChord.AfterGraceElements.GetLastEventChord();
                    }

                    if (graceChord == null)
                    {
                        graceChord = new LilyChordEvent(State);
                        if (staffChange != null)
                        {
                            evChord.AppendAfterGrace(staffChange);
                            staffChange = null;
                        }

                        evChord.AppendAfterGrace(graceChord);
                        foreach (LilyMusic pd in voiceBuilder.PendingElements)
                        {
                            evChord.AppendAfterGrace(pd);
                        }

                        voiceBuilder.ClearPendingElements();
                    }
                }
                else
                {
                    if (evChord.GraceElements != null && isChord)
                    {
                        graceChord = evChord.GraceElements.GetLastEventChord();
                    }

                    if (graceChord == null)
                    {
                        graceChord = new LilyChordEvent(State);
                        if (staffChange != null)
                        {
                            evChord.AppendGrace(staffChange);
                            staffChange = null;
                        }

                        if (noteGraceSkip != null)
                        {
                            LilySkipEvent skip = new LilySkipEvent(State);
                            skip.Duration = noteGraceSkip;
                            evChord.AppendGrace(skip);
                        }

                        evChord.AppendGrace(graceChord);
                        foreach (LilyMusic pd in voiceBuilder.PendingElements)
                        {
                            evChord.AppendGrace(pd);
                        }

                        voiceBuilder.ClearPendingElements();
                    }
                }

                if (!isAfterGrace && grace != null)
                {
                    if (grace.Attribute("slash") == "yes")
                    {
                        evChord.GraceType = "slashed";
                    }
                }

                if (grace != null)
                {
                    //Now that we have inserted the chord into the grace music, insert
                    //everything into that chord instead of the outer one.
                    evChord = graceChord;
                }

                evChord.Append(mainEvent);
            }
            else
            {
                if (staffChange != null)
                {
                    evChord.Append(staffChange);
                    staffChange = null;
                }

                evChord.Append(mainEvent);

                if (voiceBuilder.MultiMeasureCount != 0)
                {
                    voiceBuilder.MultiMeasureCount -= 1;
                }
                else
                {
                    //When a note or chord has grace notes (which have no duration), the
                    //duration of the event chord is not yet known. However, the event chord
                    //was already added with duration 0, so we have to correct this when we
                    //process the main note.
                    if (voiceBuilder.CurrentDuration().IsZero
                        && note.DurationValue.Value.Sign > 0)
                    {
                        voiceBuilder.SetDuration(note.DurationValue.Value);
                    }
                }
            }

            //If we have a figured bass, set its voice builder to the correct position and
            //insert the pending figures.
            if (pendingFiguredBass.Count > 0)
            {
                figuredBassBuilder.JumpForward(musicNode.When.Value);

                foreach (LilyMusic fb in pendingFiguredBass)
                {
                    if (fb is LilyFiguredBassEvent figured)
                    {
                        //If a duration is given, use that, otherwise use the one of the
                        //associated note.
                        PythonFraction dur = figured.RealDuration;
                        if (dur.IsZero)
                        {
                            dur = evChord.GetLength();
                        }

                        if (figured.Duration == null)
                        {
                            figured.SetDuration(evChord.GetDuration());
                        }

                        figuredBassBuilder.AddMusic(figured, dur);
                    }
                    else
                    {
                        figuredBassBuilder.AddIrrelevant(fb);
                    }
                }

                pendingFiguredBass = new List<LilyMusic>();
            }

            if (pendingChordnames.Count > 0)
            {
                chordnamesBuilder.JumpForward(musicNode.When.Value);

                foreach (LilyChordNameEvent cn in pendingChordnames)
                {
                    //Assign the duration of the chord.
                    cn.Duration = evChord.GetDuration();
                    chordnamesBuilder.AddMusic(cn, evChord.GetLength());
                }

                pendingChordnames = new List<LilyChordNameEvent>();
            }

            if (pendingFretboards.Count > 0)
            {
                fretboardsBuilder.JumpForward(musicNode.When.Value);

                foreach (LilyFretBoardEvent fb in pendingFretboards)
                {
                    //Assign the duration of the chord.
                    fb.Duration = evChord.GetDuration().LyExpression();
                    fretboardsBuilder.AddMusic(fb, evChord.GetLength());
                }

                pendingFretboards = new List<LilyFretBoardEvent>();
            }

            string color = note.Attribute("color");
            string fontSize = note.Attribute("font-size");

            //The <notations> element can have the following children (+ means implemented,
            //~ partially, - not):
            //
            //  +tied | +slur | +tuplet | glissando | slide | ornaments | technical |
            //  articulations | dynamics | +fermata | arpeggiate | non-arpeggiate |
            //  accidental-mark | other-notation
            foreach (MusicXmlNode notationsNode in notationsChildren)
            {
                MusicXmlNotations notations = (MusicXmlNotations)notationsNode;
                foreach (MusicXmlTuplet tupletEvent in notations.GetTuplets())
                {
                    MusicXmlTimeModification timeMod
                        = note.GetMaybeExistTypedChild<MusicXmlTimeModification>();
                    tupletEvents.Add((evChord, tupletEvent, timeMod, noteVisible));
                }

                foreach (string arpeggiate in new[] { "arpeggiate", "non-arpeggiate" })
                {
                    foreach (MusicXmlNode a in notations.GetList(arpeggiate))
                    {
                        if (!(evChord is LilyArpeggioChordEvent))
                        {
                            //⚠ UPSTREAM REBINDS THE CHORD'S CLASS HERE
                            //(`ev_chord.__class__ = ArpeggioChordEvent', then
                            //`ev_chord.init()'), which C# cannot do. The port builds the
                            //arpeggiated chord FROM the chord and puts it back wherever the
                            //plain one was, which is the same object identity upstream ends
                            //up with, reached the only way this language allows.
                            LilyArpeggioChordEvent arpeggioChord
                                = new LilyArpeggioChordEvent(evChord);
                            ReplaceChord(
                                voiceBuilder, evChord, arpeggioChord, tupletEvents,
                                tremoloEvents);
                            evChord = arpeggioChord;

                            //Use first occurrence of the element to set attributes.
                            arpeggioChord.Arpeggio = arpeggiate;
                            arpeggioChord.ArpeggioType = note.ArpeggioType;
                            arpeggioChord.ArpeggioDir = a.Attribute("direction");
                            arpeggioChord.ArpeggioColor = a.Attribute("color", color);

                            if (note.ArpeggioType == "PianoStaff")
                            {
                                State.NeededAdditionalDefinitions.Add("arpeggioXX");
                            }
                            else if (note.ArpeggioType == "Staff")
                            {
                                State.NeededAdditionalDefinitions.Add("arpeggioX");
                                State.LayoutInformation.SetContextItem(
                                    "Staff", "\\consists \"Span_arpeggio_engraver\"");
                            }
                        }

                        //Setting a vertical minimum and a maximum position for the arpeggio
                        //is a subset of the theoretically possible chord configurations with
                        //`<arpeggiate>'. However, it is already very rare that an arpeggio
                        //covers only a part of a chord so we keep it simple.
                        //
                        //The same holds (more or less) for `<non-arpeggiate>'. We only
                        //support one arpeggio bracket per chord (having more can be
                        //problematic as discussed in
                        //https://github.com/w3c/musicxml/discussions/540).
                        LilyArpeggioChordEvent current = (LilyArpeggioChordEvent)evChord;
                        double steps = GetEventPitch(mainEvent).Steps();
                        current.ArpeggioMinPitch = Math.Min(current.ArpeggioMinPitch, steps);
                        current.ArpeggioMaxPitch = Math.Max(current.ArpeggioMaxPitch, steps);
                    }
                }

                List<MusicXmlNode> endslurs = notations.GetList("slur")
                    .Where(s => ((MusicXmlSpanner)s).GetSpannerType() == "stop")
                    .ToList();
                foreach (MusicXmlNode es in endslurs)
                {
                    if (slurCount == 0)
                    {
                        es.Message("Encountered closing slur, but no slur is open");
                    }
                    else
                    {
                        slurCount -= 1;
                    }

                    evChord.Append(MusicXmlSpannerToLilyEvent(es));
                }

                List<MusicXmlNode> startslurs = notations.GetList("slur")
                    .Where(s => ((MusicXmlSpanner)s).GetSpannerType() == "start")
                    .ToList();
                foreach (MusicXmlNode ss in startslurs)
                {
                    slurCount += 1;
                    LilySpanEvent lilyEv = MusicXmlSpannerToLilyEvent(ss);
                    lilyEv.Visible = noteVisible;
                    evChord.Append(lilyEv);
                }

                MusicXmlNode mxlTie = notations.GetTie();
                if (mxlTie != null
                    && (mxlTie.Attribute("type") == "start"
                        || mxlTie.Attribute("type") == "let-ring"))
                {
                    LilyTieEvent tie = new LilyTieEvent(State);
                    tie.Type = mxlTie.Attribute("type");
                    tie.Color = mxlTie.Attribute("color");
                    if (!Options.NoArticulationDirections)
                    {
                        string tieDir = mxlTie.Attribute("placement")
                                        ?? mxlTie.Attribute("orientation");
                        if (tieDir != null)
                        {
                            tie.ForceDirection = MusicXmlDirectionToIndicator(tieDir);
                        }
                    }

                    mainEvent.AddAssociatedEvent(tie);
                    if (tie.Type == "start")
                    {
                        if (grace == null)
                        {
                            isTied = true;
                        }

                        tieStarted = true;

                        State.LayoutInformation.SetContextItem(
                            "Score", "tieWaitForNote = ##t");
                    }
                }
                else
                {
                    isTied = false;
                }

                foreach (MusicXmlNode a in notations.GetAllChildren())
                {
                    List<LilyMusic> events = HandleNotationChild(
                        a, color, fontSize, evChord, mainEvent, voiceBuilder,
                        tremoloEvents, ref isDoubleNoteTremolo);
                    if (events == null)
                    {
                        continue;
                    }

                    foreach (LilyMusic e in events)
                    {
                        if (e == null)
                        {
                            continue;
                        }

                        if (e is LilySpanEvent spanEvent)
                        {
                            spanEvent.Visible = noteVisible;
                        }

                        if (e is LilyFingeringEvent)
                        {
                            mainEvent.AddAssociatedEvent(e);
                        }
                        else
                        {
                            evChord.Append(e);
                        }
                    }
                }
            }

            List<MusicXmlNode> mxlBeams = note.GetList("beam")
                .Where(b => (((MusicXmlBeam)b).GetSpannerType() == "begin"
                             || ((MusicXmlBeam)b).GetSpannerType() == "end")
                            && ((MusicXmlBeam)b).IsPrimary()
                            && !isDoubleNoteTremolo)
                .ToList();
            if (mxlBeams.Count > 0 && !State.ConversionSettings.IgnoreBeaming)
            {
                //⚠ Upstream also records here whether a beam — and thus a melisma — starts
                //or ends; nothing reads that flag afterwards.
                LilySpanEvent beamEv = MusicXmlSpannerToLilyEvent(mxlBeams[0]);
                if (beamEv != null)
                {
                    evChord.Append(beamEv);
                }
            }

            //Assume that a <tie> element only lasts for one note. This might not be correct
            //MusicXML interpretation, but works for most cases and fixes broken files, which
            //have the end tag missing.
            if (isTied && !tieStarted)
            {
                isTied = false;
            }
        }

        //For getting a correct value in the last bar check comment.
        voiceBuilder.BarNumber += 1;

        if (voiceBuilder.MultiMeasureRest == null && voiceBuilder.PendingElements.Count > 0)
        {
            //We have elements that are positioned after the last `<note>', i.e., after the
            //music has finished. Note that LilyPond skips items that actually need music in
            //the next measure (which we don't have at the very end); for example, a 'p'
            //dynamics gets discarded.
            voiceBuilder.AddPendingElements(false, true);
        }

        //Force trailing multi-measure rests and/or pending elements to be written out.
        voiceBuilder.AddMusic(new LilyChordEvent(State), PythonFraction.Zero);

        MusicXmlNode lastVoiceElem = voice.Elements[voice.Elements.Count - 1];
        MusicXmlMusicNode lastMusicNode = lastVoiceElem as MusicXmlMusicNode;
        PythonFraction musicLength = lastMusicNode.When.Value;
        if (lastMusicNode.DurationValue.HasValue)
        {
            musicLength += lastMusicNode.DurationValue.Value;
        }

        voiceBuilder.LinkOffsetElements(musicLength);
        chordnamesBuilder.LinkOffsetElements(musicLength);
        fretboardsBuilder.LinkOffsetElements(musicLength);

        List<LilyMusic> lyVoice = GroupTremolos(voiceBuilder.Elements, tremoloEvents);
        lyVoice = GroupTuplets(lyVoice, tupletEvents);
        lyVoice = GroupRepeats(lyVoice);

        LilySequentialMusic seqMusic = new LilySequentialMusic(State);
        seqMusic.Elements = lyVoice;

        foreach (KeyValuePair<
                     string, (List<string> Result, string StanzaId, string Placement)> entry
                 in lyrics)
        {
            LilyLyrics ev = new LilyLyrics(State);
            ev.LyricsSyllables.AddRange(entry.Value.Result);
            ev.StanzaId = entry.Value.StanzaId;
            ev.Placement = entry.Value.Placement;
            returnValue.LyricsDict[entry.Key] = ev;
        }

        LilyMusic voiceMusic = seqMusic;

        if (Options.ShiftDurations != 0)
        {
            LilyShiftDurations sd = new LilyShiftDurations(State);
            sd.Element = voiceMusic;
            voiceMusic = sd;
        }

        if (Options.PitchMode == MusicXmlPitchMode.Relative)
        {
            LilyRelativeMusic v = new LilyRelativeMusic(State);
            v.Element = voiceMusic;
            v.BasePitch = firstPitch;
            voiceMusic = v;
        }

        returnValue.LyVoice = voiceMusic;

        //Create \figuremode { figured bass elements }.
        if (figuredBassBuilder.HasRelevantElements)
        {
            LilySequentialMusic fbassMusic = new LilySequentialMusic(State);
            fbassMusic.Elements = GroupRepeats(figuredBassBuilder.Elements);
            LilyModeChangingMusicWrapper v = new LilyModeChangingMusicWrapper(State);
            v.Mode = "figuremode";
            v.Element = fbassMusic;
            returnValue.FiguredBass = ApplyShiftDurations(v);
        }

        //Create \chordmode { chords }.
        if (chordnamesBuilder.HasRelevantElements)
        {
            LilySequentialMusic cnameMusic = new LilySequentialMusic(State);
            cnameMusic.Elements = GroupRepeats(chordnamesBuilder.Elements);
            LilyModeChangingMusicWrapper v = new LilyModeChangingMusicWrapper(State);
            v.Mode = "chordmode";
            v.Element = cnameMusic;
            returnValue.ChordNames = ApplyShiftDurations(v);
        }

        //Create diagrams for the FretBoards engraver.
        if (fretboardsBuilder.HasRelevantElements)
        {
            LilySequentialMusic fboardMusic = new LilySequentialMusic(State);
            fboardMusic.Elements = GroupRepeats(fretboardsBuilder.Elements);
            LilyMusicWrapper v = new LilyMusicWrapper(State);
            v.Element = fboardMusic;
            returnValue.FretBoards = ApplyShiftDurations(v);
        }

        return returnValue;
    }

    /// <summary>Wraps a voice's music in the shift-durations wrapper, when asked for.</summary>
    /// <param name="music">The music.</param>
    /// <returns>The music, wrapped or not.</returns>
    private LilyMusic ApplyShiftDurations(LilyMusic music)
    {
        if (Options.ShiftDurations == 0)
        {
            return music;
        }

        LilyShiftDurations sd = new LilyShiftDurations(State);
        sd.Element = music;
        return sd;
    }
}

internal sealed partial class MusicXmlConverter
{
    /// <summary>Turns a pickup measure's length into an output-side event.</summary>
    /// <param name="partialLen">The length.</param>
    /// <returns>The event, or null when there is no pickup.</returns>
    internal LilyPartial MusicXmlPartialToLily(PythonFraction partialLen)
    {
        if (partialLen.Sign <= 0)
        {
            return null;
        }

        LilyPartial p = new LilyPartial(State);
        p.PartialDuration = LilyDuration.FromFraction(State, partialLen);
        return p;
    }

    /// <summary>The pitch a rhythmic event carries.</summary>
    /// <param name="ev">The event.</param>
    /// <returns>The pitch, or null.</returns>
    /// <remarks>
    /// ⚠ Upstream reads <c>main_event.pitch</c> off whichever of the two classes it built;
    /// a skip has none and would raise <c>AttributeError</c>, which nothing here reaches.
    /// </remarks>
    private static LilyPitch GetEventPitch(LilyRhythmicEvent ev)
        => ev switch
        {
            LilyNoteEvent note => note.Pitch,
            LilyRestEvent rest => rest.Pitch,
            _ => null,
        };

    /// <summary>
    /// Puts the arpeggiated chord wherever the plain one was — in the voice, in the
    /// registered tuplet and tremolo events, and inside any grace group.
    /// </summary>
    /// <param name="voiceBuilder">The voice being built.</param>
    /// <param name="oldChord">The chord being replaced.</param>
    /// <param name="newChord">The chord replacing it.</param>
    /// <param name="tupletEvents">The tuplet events registered so far.</param>
    /// <param name="tremoloEvents">The tremolo events registered so far.</param>
    /// <remarks>
    /// ⚠ Upstream needs none of this: rebinding <c>__class__</c> changes the object every
    /// reference already points at. C# has to find those references, and they are exactly
    /// these three: the voice's own element list, and the two event lists that were handed
    /// the chord earlier in this same note's processing.
    /// </remarks>
    private static void ReplaceChord(
        MusicXmlVoiceBuilder voiceBuilder, LilyChordEvent oldChord,
        LilyArpeggioChordEvent newChord,
        List<(LilyChordEvent Chord, MusicXmlTuplet Tuplet,
              MusicXmlTimeModification TimeModification, bool Visible)> tupletEvents,
        List<(LilyChordEvent Chord, MusicXmlNode Tremolo)> tremoloEvents)
    {
        for (int i = 0; i < voiceBuilder.Elements.Count; i++)
        {
            if (ReferenceEquals(voiceBuilder.Elements[i], oldChord))
            {
                voiceBuilder.Elements[i] = newChord;
            }
        }

        for (int i = 0; i < voiceBuilder.PendingElements.Count; i++)
        {
            if (ReferenceEquals(voiceBuilder.PendingElements[i], oldChord))
            {
                voiceBuilder.PendingElements[i] = newChord;
            }
        }

        if (ReferenceEquals(voiceBuilder.MultiMeasureEvChord, oldChord))
        {
            voiceBuilder.MultiMeasureEvChord = newChord;
        }

        for (int i = 0; i < tupletEvents.Count; i++)
        {
            if (ReferenceEquals(tupletEvents[i].Chord, oldChord))
            {
                tupletEvents[i] = (newChord, tupletEvents[i].Tuplet,
                    tupletEvents[i].TimeModification, tupletEvents[i].Visible);
            }
        }

        for (int i = 0; i < tremoloEvents.Count; i++)
        {
            if (ReferenceEquals(tremoloEvents[i].Chord, oldChord))
            {
                tremoloEvents[i] = (newChord, tremoloEvents[i].Tremolo);
            }
        }

        ReplaceInGraceGroup(oldChord.GraceElements, oldChord, newChord);
        ReplaceInGraceGroup(oldChord.AfterGraceElements, oldChord, newChord);
    }

    /// <summary>Replaces a chord inside one grace group.</summary>
    /// <param name="group">The group, or null.</param>
    /// <param name="oldChord">The chord being replaced.</param>
    /// <param name="newChord">The chord replacing it.</param>
    private static void ReplaceInGraceGroup(
        LilySequentialMusic group, LilyChordEvent oldChord, LilyChordEvent newChord)
    {
        if (group == null)
        {
            return;
        }

        for (int i = 0; i < group.Elements.Count; i++)
        {
            if (ReferenceEquals(group.Elements[i], oldChord))
            {
                group.Elements[i] = newChord;
            }
        }
    }

    /// <summary>Builds the events one child of a notations element asks for.</summary>
    /// <param name="a">The child.</param>
    /// <param name="color">The note's colour.</param>
    /// <param name="fontSize">The note's font size.</param>
    /// <param name="evChord">The chord being filled.</param>
    /// <param name="mainEvent">The note or rest itself.</param>
    /// <param name="voiceBuilder">The voice being built.</param>
    /// <param name="tremoloEvents">Where to register a double-note tremolo.</param>
    /// <param name="isDoubleNoteTremolo">Whether one was found.</param>
    /// <returns>The events, or null when the child is not handled.</returns>
    private List<LilyMusic> HandleNotationChild(
        MusicXmlNode a, string color, string fontSize, LilyChordEvent evChord,
        LilyRhythmicEvent mainEvent, MusicXmlVoiceBuilder voiceBuilder,
        List<(LilyChordEvent Chord, MusicXmlNode Tremolo)> tremoloEvents,
        ref bool isDoubleNoteTremolo)
    {
        switch (a.GetName())
        {
            case "accidental-mark":
            {
                (LilyMusic ev, string marker)
                    = MusicXmlArticulationToLilyEvent(a, color, fontSize);
                if (marker != null)
                {
                    //⚠ Upstream appends the MARKER STRING itself into the chord's element
                    //list here — the dispatch table reaches `musicxml_articulation_to_lily_event'
                    //directly and does not filter its two non-event answers — and the run
                    //then dies with an AttributeError when the chord tries to print that
                    //string. Reproduced as a failure rather than silently dropped.
                    throw new ImportAbortedException(
                        "an <accidental-mark> child of <notations> answered '" + marker
                        + "' rather than an event");
                }

                return new List<LilyMusic> { ev };
            }

            case "articulations":
            case "ornaments":
            case "technical":
                return ConvertAndAppendAllChildArticulations(
                    a, color, fontSize, evChord, voiceBuilder, tremoloEvents,
                    ref isDoubleNoteTremolo);

            case "dynamics":
                ConvertAndAppendAllChildDynamics(a, color, fontSize, evChord);
                return new List<LilyMusic> { null };

            case "fermata":
                return new List<LilyMusic>
                {
                    MusicXmlFermataToLilyEvent(a, color, fontSize),
                };

            case "glissando":
            case "slide":
                return new List<LilyMusic> { MusicXmlSpannerToLilyEvent(a) };

            default:
                return null;
        }
    }

    /// <summary>Builds the events every child of an ornament-like element asks for.</summary>
    /// <param name="mxlNode">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <param name="evChord">The chord being filled.</param>
    /// <param name="voiceBuilder">The voice being built.</param>
    /// <param name="tremoloEvents">Where to register a double-note tremolo.</param>
    /// <param name="isDoubleNoteTremolo">Whether one was found.</param>
    /// <returns>The events.</returns>
    private List<LilyMusic> ConvertAndAppendAllChildArticulations(
        MusicXmlNode mxlNode, string noteColor, string noteFontSize,
        LilyChordEvent evChord, MusicXmlVoiceBuilder voiceBuilder,
        List<(LilyChordEvent Chord, MusicXmlNode Tremolo)> tremoloEvents,
        ref bool isDoubleNoteTremolo)
    {
        List<LilyMusic> res = new List<LilyMusic>();

        //Mark trill spanners where `start' and `stop' elements (in that order) happen at
        //the same musical moment.
        List<string> wavyLineStarts = new List<string>();
        foreach (MusicXmlNode ch in mxlNode.GetNamedChildren("wavy-line"))
        {
            string id = ch.Attribute("number", "1");
            string type = ch.Attribute("type");
            if (type == "start")
            {
                wavyLineStarts.Add(id);
            }
            else if (type == "stop")
            {
                if (wavyLineStarts.Contains(id))
                {
                    ((MusicXmlWavyLine)ch).StartStop = true;
                }
            }
        }

        //Double-note tremolos.
        //
        //Note that LilyPond can't handle tremolo beams if a beam has already started
        //(issue #6706); the code output by `musicxml2ly' causes warnings and incorrect
        //rendering results that have to be resolved manually.
        foreach (MusicXmlNode ch in mxlNode.GetNamedChildren("tremolo"))
        {
            string type = ch.Attribute("type");
            if (type == "start" || type == "stop")
            {
                //No need to take care of `<time-modification>' elements; we always halve
                //the duration of the affected notes.
                tremoloEvents.Add((evChord, ch));
                isDoubleNoteTremolo = true;
            }
        }

        LilyMusic ev = null;
        string evMarker = null;
        bool haveEv = false;
        List<MusicXmlNode> delayedAccidentalMarks = new List<MusicXmlNode>();

        foreach (MusicXmlNode ch in mxlNode.GetAllChildren())
        {
            if (ch is MusicXmlHashText)
            {
                continue;
            }

            if (ch.GetName() == "accidental-mark")
            {
                if (haveEv && evMarker == "unsupported")
                {
                    //Silently ignore accidental marks attached to unhandled ornaments.
                    continue;
                }

                if (haveEv && evMarker == "delayed")
                {
                    delayedAccidentalMarks.Add(ch);
                    continue;
                }

                if (ev is LilyOrnamentEvent ornament)
                {
                    ornament.AccidentalMarks.Add(ch);
                    State.NeededAdditionalDefinitions.Add("accidental-marks");
                }
                else if (ev is LilyTextSpannerEvent trillSpanner)
                {
                    trillSpanner.AccidentalMarks.Add(ch);
                    State.NeededAdditionalDefinitions.Add("accidental-marks");
                }
                else
                {
                    State.Warning(
                        "ignoring <accidental-mark> not attached to proper <ornaments> "
                        + "child");
                }

                continue;
            }

            (ev, evMarker) = MusicXmlArticulationToLilyEvent(ch, noteColor, noteFontSize);
            haveEv = true;
            if (ev == null || evMarker != null)
            {
                continue;
            }

            if (delayedAccidentalMarks.Count > 0)
            {
                if (ev is LilyOrnamentEvent delayedOrnament)
                {
                    delayedOrnament.AccidentalMarks.AddRange(delayedAccidentalMarks);
                    State.NeededAdditionalDefinitions.Add("accidental-marks");
                }
                else if (ev is LilyTextSpannerEvent delayedSpanner)
                {
                    delayedSpanner.AccidentalMarks.AddRange(delayedAccidentalMarks);
                    State.NeededAdditionalDefinitions.Add("accidental-marks");
                }
            }

            if (ev is LilySpanEvent startStopEvent && startStopEvent.StartStop)
            {
                voiceBuilder.AddLast(ev);
                continue;
            }

            res.Add(ev);
        }

        return res;
    }

    /// <summary>Builds the dynamics one notations child asks for and adds it.</summary>
    /// <param name="mxlNode">The element.</param>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <param name="evChord">The chord being filled.</param>
    private void ConvertAndAppendAllChildDynamics(
        MusicXmlNode mxlNode, string noteColor, string noteFontSize, LilyChordEvent evChord)
    {
        LilyMarkupElement element = new LilyMarkupElement(
            mxlNode, LilyMarkupElement.CopyAttributes(mxlNode));
        LilyMusic ev = MusicXmlDynamicsToLilyEvent(
            new List<LilyMarkupElement> { element });
        if (ev != null)
        {
            if (!Options.NoArticulationDirections)
            {
                string dir = mxlNode.Attribute("placement");
                if (dir != null)
                {
                    ((LilyDynamicsEvent)ev).ForceDirection
                        = MusicXmlDirectionToIndicator(dir);
                }
            }

            evChord.Append(ev);
        }
    }
}
