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

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (voices_in_part, get_all_voices, print_voice_definitions, format_staff_info, update_score_setup, update_layout_information, the preamble writers and convert);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

internal sealed partial class MusicXmlConverter
{
    /// <summary>Splits one part into its voices.</summary>
    /// <param name="part">The part.</param>
    /// <returns>The voices and the attributes in force at each staff's start.</returns>
    internal (List<KeyValuePair<string, MusicXmlVoice>> Voices,
              PythonDictionary<string, MusicXmlAttributes> PartInfo)
        VoicesInPart(MusicXmlPart part)
    {
        part.Interpret();
        part.TagSingleVoices();
        part.ExtractVoices();
        return (part.GetVoices(), part.GetStaffAttributes());
    }

    /// <summary>Splits every part into its voices.</summary>
    /// <param name="parts">The parts.</param>
    /// <returns>The voices and staff attributes, by part.</returns>
    internal PythonDictionary<string,
            (List<KeyValuePair<string, MusicXmlVoice>> Voices,
             PythonDictionary<string, MusicXmlAttributes> PartInfo)>
        VoicesInPartInParts(List<MusicXmlPart> parts)
    {
        PythonDictionary<string,
                (List<KeyValuePair<string, MusicXmlVoice>>,
                 PythonDictionary<string, MusicXmlAttributes>)> dictionary
            = new PythonDictionary<string,
                (List<KeyValuePair<string, MusicXmlVoice>>,
                 PythonDictionary<string, MusicXmlAttributes>)>();
        foreach (MusicXmlPart p in parts)
        {
            //Don't crash if the part doesn't have an id (that's invalid MusicXML, but such
            //files are out in the wild!)
            //TODO (upstream's): extract correct part id from other sources
            dictionary[p.Attribute("id")] = VoicesInPart(p);
        }

        if (State.StartingGraceLengths.Count > 0)
        {
            State.MaxStartingGraceLength = State.StartingGraceLengths.Values.Max();
        }

        return dictionary;
    }

    /// <summary>Converts every voice of every part.</summary>
    /// <param name="parts">The parts.</param>
    /// <returns>The converted voices and the staff attributes, by part.</returns>
    internal (PythonDictionary<string, PythonDictionary<string, MusicXmlVoiceData>> Voices,
              Dictionary<string, PythonDictionary<string, MusicXmlAttributes>> StaffInfo)
        GetAllVoices(List<MusicXmlPart> parts)
    {
        PythonDictionary<string,
                (List<KeyValuePair<string, MusicXmlVoice>> Voices,
                 PythonDictionary<string, MusicXmlAttributes> PartInfo)> allVoices
            = VoicesInPartInParts(parts);

        PythonDictionary<string, PythonDictionary<string, MusicXmlVoiceData>> allLyVoices
            = new PythonDictionary<string, PythonDictionary<string, MusicXmlVoiceData>>();
        Dictionary<string, PythonDictionary<string, MusicXmlAttributes>> allLyStaffInfo
            = new Dictionary<string, PythonDictionary<string, MusicXmlAttributes>>(
                StringComparer.Ordinal);

        foreach ((string p,
                  (List<KeyValuePair<string, MusicXmlVoice>> nameVoice,
                   PythonDictionary<string, MusicXmlAttributes> staffInfo)) in allVoices.Items())
        {
            int numVoices = nameVoice.Count;
            PythonDictionary<string, MusicXmlVoiceData> partLyVoices
                = new PythonDictionary<string, MusicXmlVoiceData>();

            Dictionary<string, int> voicesInStavesCounter
                = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> stavesCounter
                = new Dictionary<string, int>(StringComparer.Ordinal);
            if (numVoices != 0)
            {
                foreach (KeyValuePair<string, MusicXmlVoice> entry in nameVoice)
                {
                    string staff = entry.Value.StartStaff;
                    string key = staff ?? string.Empty;
                    stavesCounter[key] = stavesCounter.TryGetValue(key, out int count)
                        ? count + 1
                        : 1;
                }
            }

            foreach (KeyValuePair<string, MusicXmlVoice> entry in nameVoice)
            {
                string n = entry.Key;
                MusicXmlVoice v = entry.Value;

                PythonFraction startingGraceLength
                    = State.StartingGraceLengths.TryGetValue(
                        (p, n), out PythonFraction found)
                        ? found
                        : PythonFraction.Zero;
                PythonFraction length = State.MaxStartingGraceLength - startingGraceLength;
                LilyDuration startingGraceSkip = length.IsZero
                    ? null
                    : LilyDuration.FromFraction(State, length);

                int voiceInStaff;
                if (numVoices > 1)
                {
                    //The code to get the voice number in a staff should stay in sync with
                    //the score-setup pass.
                    string staff = v.StartStaff;
                    string key = staff ?? string.Empty;
                    if (stavesCounter[key] > 1)
                    {
                        voicesInStavesCounter[key]
                            = voicesInStavesCounter.TryGetValue(key, out int count)
                                ? count + 1
                                : 1;
                        voiceInStaff = voicesInStavesCounter[key];
                    }
                    else
                    {
                        voiceInStaff = 0;
                    }
                }
                else
                {
                    voiceInStaff = 0;
                }

                partLyVoices[n] = MusicXmlVoiceToLilyVoice(v, voiceInStaff, startingGraceSkip);
            }

            allLyVoices[p] = partLyVoices;
            allLyStaffInfo[p ?? string.Empty] = staffInfo;
        }

        return (allLyVoices, allLyStaffInfo);
    }

    /// <summary>Writes every voice, lyric line, chord-name and fretboard definition.</summary>
    /// <param name="printer">Where to write.</param>
    /// <param name="partList">The part list.</param>
    /// <param name="voices">The converted voices, by part.</param>
    internal void PrintVoiceDefinitions(
        LilyOutputPrinter printer, List<MusicXmlNode> partList,
        PythonDictionary<string, PythonDictionary<string, MusicXmlVoiceData>> voices)
    {
        foreach (MusicXmlNode part in partList)
        {
            string partId = part.Attribute("id");
            PythonDictionary<string, MusicXmlVoiceData> nvDict
                = voices.GetOrDefault(partId)
                  ?? new PythonDictionary<string, MusicXmlVoiceData>();
            foreach ((string name, MusicXmlVoiceData voice) in nvDict.Items())
            {
                string k = MusicXmlVoiceNameToLilyName(partId, name);
                printer.Dump(k + " =");
                if (voice.VoiceData.CrossStaffChordVoice != null)
                {
                    printer.Dump("\\crossStaff");
                }

                voice.LyVoice.PrintLy(printer);
                printer.Newline();
                if (voice.ChordNames != null)
                {
                    string cnname = MusicXmlChordNamesNameToLilyName(partId, name);
                    printer.Dump(cnname + " =");
                    voice.ChordNames.PrintLy(printer);
                    printer.Newline();
                }

                foreach (string l in voice.LyricsOrder)
                {
                    string lname = MusicXmlLyricsNameToLilyName(partId, name, l);
                    printer.Dump(lname + " =");
                    voice.LyricsDict[l].PrintLy(printer);
                    printer.Newline();
                }

                if (voice.FiguredBass != null)
                {
                    string fbname = MusicXmlFiguredBassNameToLilyName(partId, name);
                    printer.Dump(fbname + " =");
                    voice.FiguredBass.PrintLy(printer);
                    printer.Newline();
                }

                if (voice.FretBoards != null)
                {
                    string fbdname = MusicXmlFretBoardsNameToLilyName(partId, name);
                    printer.Dump(fbdname + " =");
                    voice.FretBoards.PrintLy(printer);
                    printer.Newline();
                }
            }
        }
    }

    /// <summary>Names the four contexts one staff's voices need.</summary>
    /// <param name="partId">The part.</param>
    /// <param name="staffId">The staff, or null when the part has only one.</param>
    /// <param name="rawVoices">The voices and what each carries.</param>
    /// <returns>The staff's entry.</returns>
    internal static LilyStaffInfo FormatStaffInfo(
        string partId, string staffId,
        List<(string VoiceName,
              List<(string Name, string StanzaId, string Placement)> LyricsIds,
              LilyMusic FiguredBass, LilyMusic ChordNames, LilyMusic FretBoards,
              string CrossStaffChordVoice)> rawVoices)
    {
        List<LilyVoiceInfo> voices = new List<LilyVoiceInfo>();
        foreach ((string v,
                  List<(string Name, string StanzaId, string Placement)> lyricsIds,
                  LilyMusic figuredBass, LilyMusic chordNames, LilyMusic fretBoards,
                  string cscVoice) in rawVoices)
        {
            string voiceName = MusicXmlVoiceNameToLilyName(partId, v);
            List<(string Name, string StanzaId, string Placement)> voiceLyrics
                = lyricsIds
                    .Select(entry => (
                        MusicXmlLyricsNameToLilyName(partId, v, entry.Name),
                        entry.StanzaId,
                        entry.Placement))
                    .ToList();
            string figuredBassName = figuredBass != null
                ? MusicXmlFiguredBassNameToLilyName(partId, v)
                : string.Empty;
            string chordNamesName = chordNames != null
                ? MusicXmlChordNamesNameToLilyName(partId, v)
                : string.Empty;
            string fretBoardsName = fretBoards != null
                ? MusicXmlFretBoardsNameToLilyName(partId, v)
                : string.Empty;
            voices.Add(
                new LilyVoiceInfo(
                    voiceName, voiceLyrics, figuredBassName, chordNamesName,
                    fretBoardsName, cscVoice));
        }

        return new LilyStaffInfo(staffId, voices);
    }

    /// <summary>Gives every staff of the score the voices it holds, and the tempo.</summary>
    /// <param name="scoreStructure">The score.</param>
    /// <param name="partList">The part list.</param>
    /// <param name="voices">The converted voices, by part.</param>
    /// <param name="parts">The parts.</param>
    internal void UpdateScoreSetup(
        LilyScore scoreStructure, List<MusicXmlNode> partList,
        PythonDictionary<string, PythonDictionary<string, MusicXmlVoiceData>> voices,
        List<MusicXmlPart> parts)
    {
        foreach (MusicXmlNode partDefinition in partList)
        {
            string partId = partDefinition.Attribute("id");
            PythonDictionary<string, MusicXmlVoiceData> nvDict = voices.GetOrDefault(partId);
            if (nvDict == null || nvDict.Count == 0)
            {
                if (partList.Count == 1 && voices.Count == 1)
                {
                    //If there is only one part, infer the ID.
                    nvDict = voices.Items().First().Value;
                    voices[partId] = nvDict;
                }
                else
                {
                    State.Warning("unknown part in part-list: " + partId);
                    continue;
                }
            }

            List<string> staves = new List<string>();
            foreach ((string _, MusicXmlVoiceData voice) in nvDict.Items())
            {
                staves.AddRange(voice.VoiceData.Staves.Keys);
            }

            List<LilyStaffInfo> stavesInfo = new List<LilyStaffInfo>();
            if (staves.Count > 1)
            {
                foreach (string s in staves.Distinct().OrderBy(x => x, StringComparer.Ordinal))
                {
                    stavesInfo.Add(
                        FormatStaffInfo(partId, s, CollectRawVoices(nvDict, s)));
                }
            }
            else
            {
                stavesInfo.Add(
                    FormatStaffInfo(partId, null, CollectRawVoices(nvDict, null)));
            }

            LilyStaffGroup part = scoreStructure.FindPart(partId);
            if (part != null)
            {
                part.SetPartInformation(stavesInfo);
            }
        }

        List<MusicXmlNode> sounds = new List<MusicXmlNode>();
        foreach (MusicXmlPart part in parts)
        {
            foreach (MusicXmlMeasure measure in part.GetTypedChildren<MusicXmlMeasure>())
            {
                sounds.AddRange(measure.GetTypedChildren<MusicXmlSound>());
                foreach (MusicXmlDirection direction
                         in measure.GetTypedChildren<MusicXmlDirection>())
                {
                    sounds.AddRange(direction.GetTypedChildren<MusicXmlSound>());
                }
            }
        }

        scoreStructure.SetTempo("100");
        foreach (MusicXmlNode sound in sounds)
        {
            string tempo = ((MusicXmlSound)sound).GetTempo();
            if (!string.IsNullOrEmpty(tempo))
            {
                scoreStructure.SetTempo(tempo);
                break;
            }
        }
    }

    /// <summary>Collects one staff's voices in the shape the staff formatter wants.</summary>
    /// <param name="nvDict">The part's voices.</param>
    /// <param name="staff">Which staff, or null for all of them.</param>
    /// <returns>The voices.</returns>
    private static List<(string VoiceName,
                         List<(string Name, string StanzaId, string Placement)> LyricsIds,
                         LilyMusic FiguredBass, LilyMusic ChordNames, LilyMusic FretBoards,
                         string CrossStaffChordVoice)>
        CollectRawVoices(PythonDictionary<string, MusicXmlVoiceData> nvDict, string staff)
    {
        List<(string, List<(string, string, string)>, LilyMusic, LilyMusic, LilyMusic,
              string)> thisStaffRawVoices
            = new List<(string, List<(string, string, string)>, LilyMusic, LilyMusic,
                LilyMusic, string)>();
        foreach ((string voiceName, MusicXmlVoiceData voice) in nvDict.Items())
        {
            if (staff != null && voice.VoiceData.StartStaff != staff)
            {
                continue;
            }

            List<(string, string, string)> order = new List<(string, string, string)>();
            foreach (string i in voice.LyricsOrder)
            {
                order.Add((i, voice.LyricsDict[i].StanzaId, voice.LyricsDict[i].Placement));
            }

            thisStaffRawVoices.Add(
                (voiceName, order, voice.FiguredBass, voice.ChordNames, voice.FretBoards,
                    voice.VoiceData.CrossStaffChordVoice));
        }

        return thisStaffRawVoices;
    }

    /// <summary>Sets global values in the layout block, like auto-beaming.</summary>
    internal void UpdateLayoutInformation()
    {
        if (!State.ConversionSettings.IgnoreBeaming)
        {
            State.LayoutInformation.SetContextItem("Score", "autoBeaming = ##f");
        }

        if (State.GetStringNumbers() == "f")
        {
            State.LayoutInformation.SetContextItem(
                "Score", "\\override StringNumber #'stencil = ##f");
        }
    }

    /// <summary>Writes the document's opening lines.</summary>
    /// <param name="printer">Where to write.</param>
    /// <param name="filename">What to call the input.</param>
    internal void PrintLyPreamble(LilyOutputPrinter printer, string filename)
    {
        printer.DumpVersion(LilyPortInfo.CompatibleWithVersion);
        printer.PrintVerbatim(
            "% automatically converted by musicxml2ly from " + filename);
        printer.Newline();
        printer.Dump("\\pointAndClickOff");
        printer.Newline();
        if (Options.Midi)
        {
            printer.Newline();
            printer.Dump("\\include \"articulate.ly\"");
            printer.Newline();
        }

        printer.Newline();
    }

    /// <summary>Writes the definitions the score needs.</summary>
    /// <param name="printer">Where to write.</param>
    internal void PrintLyAdditionalDefinitions(LilyOutputPrinter printer)
    {
        if (State.NeededAdditionalDefinitions.Count > 0)
        {
            printer.PrintVerbatim("%% additional definitions required by the score:");
            printer.Newline();
        }

        foreach (string a in State.NeededAdditionalDefinitions
                     .Distinct()
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            printer.PrintVerbatim(
                State.ExtraDefinitions.TryGetValue(a, out string extra)
                    ? extra
                    : MusicXmlDefinitions.Get(a));
            printer.Newline();
        }

        if (State.NeededAdditionalDefinitions.Count > 0)
        {
            printer.Newline();
        }
    }

    /// <summary>Writes the macros the score needs.</summary>
    /// <param name="printer">Where to write.</param>
    internal void PrintLyAdditionalMacros(LilyOutputPrinter printer)
    {
        if (State.AdditionalMacros.Count > 0)
        {
            printer.PrintVerbatim("%% additional macros required by the score:");
            printer.Newline();
        }

        foreach (string a in State.AdditionalMacros.Keys
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            printer.PrintVerbatim(State.AdditionalMacros[a]);
            printer.Newline();
        }

        if (State.AdditionalMacros.Count > 0)
        {
            printer.Newline();
        }
    }

    /// <summary>Applies the options, the way upstream's driver does before converting.</summary>
    /// <remarks>
    /// ⚠ ORDER MATTERS: the dynamics-scale warning is the first thing upstream can say
    /// about a run, before the document is even read, and the corpus records it in that
    /// position.
    /// </remarks>
    internal void ApplyOptions()
    {
        //Support historical note name aliases similar to LilyPond.
        string language = Options.Language switch
        {
            "catalan" => "catal\u00e0",
            "espanol" => "espa\u00f1ol",
            "portugues" => "portugu\u00eas",
            _ => Options.Language,
        };

        //midi-block option
        if (Options.Midi)
        {
            State.MidiOption = true;
        }

        //ottavas end early option
        if (!string.IsNullOrEmpty(Options.OttavasEndEarly))
        {
            State.OttavasEndEarlyOption = Options.OttavasEndEarly;
        }

        //transpose function
        if (!string.IsNullOrEmpty(Options.Transpose))
        {
            State.TransposeOption = Options.Transpose;
        }

        //duration shift function
        if (Options.ShiftDurations != 0)
        {
            State.ShiftDurationsOption = Options.ShiftDurations;
        }

        //dynamics scale function
        if (Options.DynamicsScale.HasValue && Options.DynamicsScale.Value < 0)
        {
            State.Warning(
                "requested dynamics scale factor must be non-negative, setting to 0");
            Options.DynamicsScale = 0;
        }

        //tab clef option
        if (!string.IsNullOrEmpty(Options.TabClef))
        {
            State.TabClefOption = Options.TabClef;
        }

        //string numbers option
        if (!string.IsNullOrEmpty(Options.StringNumbers))
        {
            State.StringNumbersOption = Options.StringNumbers;
        }

        //book option
        if (Options.Book)
        {
            State.BookOption = true;
        }

        //no-tagline option
        if (!Options.NoTagline)
        {
            State.TaglineOption = true;
        }

        if (Options.AbsoluteFontSizes)
        {
            State.AbsoluteFontSizesOption = true;
        }

        if (!string.IsNullOrEmpty(language))
        {
            LilyPitchLanguages.SetPitchLanguage(State, language);
            State.NeededAdditionalDefinitions.Add(language);
            State.ExtraDefinitions[language] = "\\language \"" + language + "\"\n";
        }

        State.ConversionSettings.IgnoreBeaming = Options.NoBeaming;
        State.ConversionSettings.ConvertPageLayout = !Options.NoPageLayout;
        if (State.ConversionSettings.ConvertPageLayout)
        {
            State.ConversionSettings.ConvertSystemBreaks = !Options.NoSystemBreaks;
            State.ConversionSettings.ConvertPageBreaks = !Options.NoPageBreaks;
            State.ConversionSettings.ConvertPageMargins = !Options.NoPageMargins;
        }
        else
        {
            State.ConversionSettings.ConvertSystemBreaks = false;
            State.ConversionSettings.ConvertPageBreaks = false;
            State.ConversionSettings.ConvertPageMargins = false;
        }

        State.ConversionSettings.ConvertStemDirections = !Options.NoStemDirections;
        State.ConversionSettings.ConvertRestPositions = !Options.NoRestPositions;
    }

    /// <summary>Converts one document.</summary>
    /// <param name="tree">The document, already demarshalled.</param>
    /// <param name="filename">What to call the input in the preamble comment.</param>
    /// <returns>The LilyPond source.</returns>
    internal string Convert(MusicXmlNode tree, string filename)
    {
        LilyHeader scoreInformation = ExtractScoreInformation(tree);
        LilyPaper paperInformation = ExtractPaperInformation(tree);

        List<MusicXmlPart> parts = tree.GetTypedChildren<MusicXmlPart>();
        (PythonDictionary<string, PythonDictionary<string, MusicXmlVoiceData>> voices,
            Dictionary<string, PythonDictionary<string, MusicXmlAttributes>> staffInfo)
            = GetAllVoices(parts);

        if (State.HaveStemDirections)
        {
            State.NeededAdditionalDefinitions.Add("stem-directions");
        }

        LilyScore score = null;
        List<MusicXmlNode> partList = null;
        MusicXmlPartList mxlPl = tree.GetMaybeExistTypedChild<MusicXmlPartList>();
        if (mxlPl != null)
        {
            score = ExtractScoreStructure(mxlPl, staffInfo);
            partList = mxlPl.GetNamedChildren("score-part");
        }

        //Score information is contained in the <work>, <identification> or
        //<movement-title> tags.
        UpdateScoreSetup(score, partList, voices, parts);
        //After the conversion, update the list of settings for the \layout block.
        UpdateLayoutInformation();

        LilyOutputPrinter printer = new LilyOutputPrinter();

        PrintLyPreamble(printer, filename);
        PrintLyAdditionalDefinitions(printer);
        PrintLyAdditionalMacros(printer);
        if (scoreInformation != null)
        {
            scoreInformation.PrintLy(printer);
        }

        if (paperInformation != null && State.ConversionSettings.ConvertPageLayout)
        {
            paperInformation.PrintLy(printer);
        }

        State.LayoutInformation.PrintLy(printer);
        PrintVoiceDefinitions(printer, partList, voices);

        printer.Newline();
        printer.Dump("% The score definition");
        printer.Newline();
        score.PrintLy(printer);
        printer.Newline();

        return printer.GetText();
    }
}
