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

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (group_repeats, musicxml_tuplet_to_lily, group_tuplets and group_tremolos);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>One entry of the repeat parser's marker list.</summary>
/// <remarks>
/// ⚠ Upstream keeps these as two-member python lists whose SECOND member is a position,
/// a start-and-stop pair, or a list of such pairs, depending on what the FIRST member
/// says. C# cannot, so all three readings are named and <see cref="Kind"/> — upstream's
/// own first member — says which is filled.
/// </remarks>
internal sealed class MusicXmlRepeatMarkerEntry
{
    /// <summary>Builds an entry naming one position.</summary>
    /// <param name="kind">Which marker or parser state this is.</param>
    /// <param name="position">Where it sits in the music list.</param>
    internal MusicXmlRepeatMarkerEntry(string kind, int position)
    {
        Kind = kind;
        Position = position;
    }

    /// <summary>Builds an entry naming one ending's range.</summary>
    /// <param name="kind">Which marker or parser state this is.</param>
    /// <param name="range">The ending's start and stop.</param>
    internal MusicXmlRepeatMarkerEntry(string kind, (int Start, int Stop) range)
    {
        Kind = kind;
        Range = range;
    }

    /// <summary>Builds an entry naming several endings' ranges.</summary>
    /// <param name="kind">Which marker or parser state this is.</param>
    /// <param name="ranges">The endings' starts and stops.</param>
    internal MusicXmlRepeatMarkerEntry(string kind, List<(int Start, int Stop)> ranges)
    {
        Kind = kind;
        Ranges = ranges;
    }

    /// <summary>Which marker or parser state this entry is.</summary>
    internal string Kind { get; set; }

    /// <summary>Where the marker sits in the music list.</summary>
    internal int Position { get; set; }

    /// <summary>The ending's start and stop.</summary>
    internal (int Start, int Stop) Range { get; set; }

    /// <summary>The endings' starts and stops.</summary>
    internal List<(int Start, int Stop)> Ranges { get; set; }
}

internal sealed partial class MusicXmlConverter
{
    private const string MarkerRepeatForwardEndingStart = "REPEAT FORWARD & ENDING START";
    private const string MarkerEndingStopRepeatBackward = "ENDING STOP & REPEAT BACKWARD";
    private const string MarkerRepeatForward = "REPEAT FORWARD";
    private const string MarkerRepeatBackward = "REPEAT BACKWARD";
    private const string MarkerEndingStart = "ENDING START";
    private const string MarkerEndingStop = "ENDING STOP";
    private const string MarkerEndOfInput = "$";
    private const string StateBeginOfInput = "^";
    private const string StateLastEnding = "last ending";
    private const string StateEndings = "endings";
    private const string StateEndingsWithRepeat = "endings with repeat";
    private const string StateNestedEndingStart = "repeat forward & nested ending start";

    private static readonly Dictionary<string, string> MarkerIdDict
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {
                MarkerRepeatForwardEndingStart,
                "combination <repeat type=\"forward\"> with <ending type=\"start\">"
            },
            {
                MarkerEndingStopRepeatBackward,
                "combination <ending type=\"stop\"> with <repeat type=\"backward\">"
            },
            { MarkerRepeatForward, "element <repeat type=\"forward\">" },
            { MarkerRepeatBackward, "element <repeat type=\"backward\">" },
            { MarkerEndingStart, "element <ending type=\"start\">" },
            { MarkerEndingStop, "element <ending type=\"stop\">" },
            { MarkerEndOfInput, "end of input" },
        };

    /// <summary>python's <c>list[index]</c>, where a negative index counts from the end.</summary>
    /// <typeparam name="T">What the list holds.</typeparam>
    /// <param name="list">The list.</param>
    /// <param name="index">The index, which may be negative.</param>
    /// <returns>The element.</returns>
    /// <remarks>
    /// ⚠ LOAD-BEARING in the repeat parser. An implicit repeat start is recorded at
    /// position -1 — a position no element has — and upstream then reads
    /// <c>music_list[-1]</c>, which is the LAST element rather than an error. Four of the
    /// corpus's repeat cases reach that read.
    /// </remarks>
    private static T PyAt<T>(List<T> list, int index)
        => list[index < 0 ? list.Count + index : index];

    /// <summary>python's <c>del list[index]</c>, where a negative index counts from the end.</summary>
    /// <typeparam name="T">What the list holds.</typeparam>
    /// <param name="list">The list.</param>
    /// <param name="index">The index, which may be negative.</param>
    private static void PyRemoveAt<T>(List<T> list, int index)
        => list.RemoveAt(index < 0 ? list.Count + index : index);

    /// <summary>Which marker an element and a direction name.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The marker, or null when the element is not one.</returns>
    /// <remarks>
    /// ⚠ Upstream keys this on <c>type(el)</c>, so a repeat-and-ending marker does NOT
    /// match the plain repeat marker's entries; the port asks for the exact type too.
    /// </remarks>
    private static string GetMarkerKind(LilyMusic element)
    {
        if (!(element is MusicXmlMarker marker))
        {
            return null;
        }

        Type type = marker.GetType();
        int direction = marker.Direction;

        if (type == typeof(MusicXmlRepeatEndingMarker))
        {
            return direction switch
            {
                -1 => MarkerRepeatForwardEndingStart,
                1 => MarkerEndingStopRepeatBackward,
                _ => null,
            };
        }

        if (type == typeof(MusicXmlRepeatMarker))
        {
            return direction switch
            {
                -1 => MarkerRepeatForward,
                1 => MarkerRepeatBackward,
                _ => null,
            };
        }

        if (type == typeof(MusicXmlEndingMarker))
        {
            return direction switch
            {
                -1 => MarkerEndingStart,
                1 => MarkerEndingStop,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// Detects repeats and alternative endings and converts them to the corresponding
    /// output-side objects, containing nested music.
    /// </summary>
    /// <param name="musicList">The music, which is modified in place.</param>
    /// <returns>The music.</returns>
    internal List<LilyMusic> GroupRepeats(List<LilyMusic> musicList)
    {
        int musicListPos = 0;
        List<MusicXmlRepeatMarkerEntry> markers = new List<MusicXmlRepeatMarkerEntry>();
        int markersPos = 0;

        (int Start, int Stop) ClampRange(int start, int stop)
        {
            if (start < 0)
            {
                start = 0;
            }

            if (stop > musicList.Count)
            {
                stop = musicList.Count;
            }

            return (start, stop);
        }

        //Wrap elements in `musicList' between the two markers.
        void WrapRepeat(int start, int stop)
        {
            State.LayoutInformation.SetContextItem(
                "Score", "doubleRepeatBarType = \":|.|:\"");

            LilyMusic startMarker = PyAt(musicList, start);
            if (startMarker is MusicXmlRepeatMarker repeatMarker && repeatMarker.AtStart)
            {
                //We need special LilyPond support for this case.
                State.LayoutInformation.SetContextItem(
                    "Score", "printInitialRepeatBar = ##t");
            }

            object times = 2;
            if (stop < musicList.Count)
            {
                LilyMusic stopMarker = musicList[stop];
                if (stopMarker.GetType() == typeof(MusicXmlRepeatMarker))
                {
                    times = ((MusicXmlRepeatMarker)stopMarker).Times;
                    if (times is int intTimes && intTimes == 1)
                    {
                        //We need special LilyPond support for this case.
                        State.LayoutInformation.SetContextItem(
                            "Score", "printTrivialVoltaRepeats = ##t");
                    }
                }
            }

            LilyRepeatedMusic repeated = new LilyRepeatedMusic(State);
            repeated.RepeatCount = times;
            repeated.SetMusic(musicList.GetRange(start + 1, Math.Max(0, stop - (start + 1))));

            (int m, int n) = ClampRange(start, stop + 1);
            musicList.RemoveRange(m, Math.Max(0, n - m));

            musicList.Insert(m, repeated);

            //We have to adjust the music-list position if we modify the beginning of the
            //music list.
            if (markers[0].Position <= musicListPos)
            {
                musicListPos = markers[0].Position + 1;
            }
        }

        //Construct a `\repeat' block with alternatives as specified by the endings.
        void WrapRepeatWithEndings(int start, List<(int Start, int Stop)> endings)
        {
            State.LayoutInformation.SetContextItem(
                "Score", "doubleRepeatBarType = \":|.|:\"");

            int stop = endings[0].Start;
            LilyRepeatedMusic repeated = new LilyRepeatedMusic(State);
            repeated.SetMusic(musicList.GetRange(start + 1, Math.Max(0, stop - (start + 1))));

            int repeatCount = 0;
            foreach ((int i, int j) in endings)
            {
                //We have to prepend the data from `<ending>' start elements to set
                //properties for the `VoltaSpanner' grobs. Note that MusicXML doesn't
                //provide a separate `color' attribute for the volta number text.
                MusicXmlNode elementStart = GetMarkerEvent(musicList[i]);
                Dictionary<string, object> attributes
                    = LilyMarkupElement.CopyAttributes(elementStart);
                attributes.Remove("color");

                LilyVoltaStyleEvent volta = new LilyVoltaStyleEvent(State);
                volta.Element = new LilyMarkupElement(elementStart, attributes);

                //Check the `number' attribute of the start and stop `<ending>' elements.
                List<int> volteStart = GetMarkerVolte(musicList[i]);
                List<int> volteStop = GetMarkerVolte(musicList[j]);
                List<int> volte;
                if (volteStart != null && volteStart.Count > 0)
                {
                    volte = volteStart;
                }
                else if (volteStop != null && volteStop.Count > 0)
                {
                    volte = volteStop;
                }
                else
                {
                    //Handle an empty `volte' list (i.e., MusicXML tells us that the volta
                    //number is undetermined, whatever this means). Since LilyPond doesn't
                    //print a right-open volta bracket if both the first and the second
                    //ending have the same volta number, we use values '1' and '2'.
                    MusicXmlNode elementStop = GetMarkerEvent(musicList[j]);
                    volte = elementStop != null && elementStop.Attribute("type") == "discontinue"
                        ? new List<int> { 2 }
                        : new List<int> { 1 };
                }

                repeatCount += volte.Count;

                if (elementStart != null
                    && elementStart.Attribute("print-object", "yes") == "no")
                {
                    volta.Visible = false;
                }

                volta.Color = elementStart?.Attribute("color");

                LilySequentialMusic sequential = new LilySequentialMusic(State);
                sequential.Elements.Add(volta);
                sequential.Elements.AddRange(
                    musicList.GetRange(i + 1, Math.Max(0, j - (i + 1))));

                repeated.AddEnding(volte, sequential);
            }

            repeated.RepeatCount = repeatCount;

            //Deleting elements from the music list in a loop works if we do it in reverse
            //order. We also delete the repeat markers around the actual data.
            for (int index = endings.Count - 1; index >= 0; index--)
            {
                (int i, int j) = endings[index];
                (int m2, int n2) = ClampRange(i, j + 1);
                musicList.RemoveRange(m2, Math.Max(0, n2 - m2));
            }

            //There is no repeat marker after the element at the stop position.
            (int m, int n) = ClampRange(start, stop);
            musicList.RemoveRange(m, Math.Max(0, n - m));

            musicList.Insert(m, repeated);

            //We have to adjust the music-list position if we modify the beginning of the
            //music list.
            if (markers[0].Position <= musicListPos)
            {
                musicListPos = markers[0].Position + 1;
            }
        }

        //Marker objects from the music list are stored in the `markers' array. We only
        //access the music list if `markers' is exhausted.
        string GetMarker()
        {
            if (markersPos < markers.Count)
            {
                string found = markers[markersPos].Kind;
                markersPos += 1;
                return found;
            }

            int musicListEnd = musicList.Count;

            while (musicListPos < musicListEnd)
            {
                LilyMusic element = PyAt(musicList, musicListPos);
                string marker = GetMarkerKind(element);

                if (marker != null)
                {
                    markers.Add(new MusicXmlRepeatMarkerEntry(marker, musicListPos));
                    musicListPos += 1;
                    markersPos += 1;
                    return marker;
                }

                musicListPos += 1;
            }

            //End of input.
            return MarkerEndOfInput;
        }

        //Parse, rearrange, and transform the repeat structure data list. The function
        //answers true if the list should be scanned again and false if the list should be
        //regenerated or if we are done.
        bool ParseMarkers()
        {
            //Begin of input.
            string state = StateBeginOfInput;

            while (true)
            {
                //If we answer false there is no need to adjust the `markers' array
                //(except for what we need for wrapping) since it gets regenerated in the
                //next call of the loop.
                string marker = GetMarker();
                string markerId = MarkerIdDict.TryGetValue(marker, out string named)
                    ? named
                    : null;

                int curr = markersPos - 1;
                int prev = markersPos - 2;

                if (state == StateBeginOfInput)
                {
                    if (marker == MarkerEndingStart || marker == MarkerRepeatBackward)
                    {
                        //We have an implicit repeat start.
                        markers.Insert(
                            0, new MusicXmlRepeatMarkerEntry(MarkerRepeatForward, -1));
                        return true;
                    }

                    if (marker == MarkerEndingStop
                        || marker == MarkerEndingStopRepeatBackward
                        || marker == MarkerRepeatForwardEndingStart)
                    {
                        State.Warning("ignoring unexpected " + markerId);
                        PyRemoveAt(musicList, markers[curr].Position);
                        return false;
                    }

                    if (marker == MarkerEndOfInput)
                    {
                        return false;
                    }
                }
                else if (state == MarkerEndingStart)
                {
                    if (marker == MarkerEndingStop)
                    {
                        int start = PyAt(markers, prev).Position;
                        int stop = markers[curr].Position;
                        markers[prev < 0 ? markers.Count + prev : prev] = new MusicXmlRepeatMarkerEntry(
                            StateLastEnding, (start, stop));
                        markers.RemoveAt(curr);
                        return true;
                    }

                    if (marker == MarkerEndOfInput)
                    {
                        //We have an implicit ending stop.
                        int start = markers[curr].Position;
                        int stop = musicList.Count;

                        MusicXmlEndingMarker newElement = new MusicXmlEndingMarker(State);
                        newElement.Direction = 1;

                        LilyMusic element = musicList[stop - 1];
                        if (element is LilyChordEvent chord && chord.Elements.Count == 0)
                        {
                            //Ignore empty chord at the end of input.
                            stop -= 1;
                        }

                        musicList.Insert(stop, newElement);
                        markers[curr] = new MusicXmlRepeatMarkerEntry(
                            StateLastEnding, (start, stop));
                        return true;
                    }

                    if (marker == MarkerEndingStopRepeatBackward)
                    {
                        int start = PyAt(markers, prev).Position;
                        int stop = markers[curr].Position;
                        markers[prev < 0 ? markers.Count + prev : prev] = new MusicXmlRepeatMarkerEntry(
                            StateEndingsWithRepeat,
                            new List<(int, int)> { (start, stop) });
                        markers.RemoveAt(curr);
                        return true;
                    }

                    if (marker == MarkerEndingStart)
                    {
                        State.Warning("ignoring unexpected " + markerId);
                        PyRemoveAt(musicList, markers[curr].Position);
                        return false;
                    }
                }
                else if (state == MarkerRepeatForward)
                {
                    if (marker == StateEndings)
                    {
                        int start = PyAt(markers, prev).Position;
                        List<(int Start, int Stop)> endings = markers[curr].Ranges;
                        WrapRepeatWithEndings(start, endings);
                        return false;
                    }

                    if (marker == MarkerRepeatBackward)
                    {
                        int start = PyAt(markers, prev).Position;
                        int stop = markers[curr].Position;
                        WrapRepeat(start, stop);
                        return false;
                    }

                    if (marker == MarkerEndOfInput)
                    {
                        //We have an implicit backward repeat.
                        int start = PyAt(markers, prev).Position;
                        int stop = musicList.Count;

                        //Similar to explicit repeats we ignore the final bar line's type
                        //(if set).
                        LilyMusic element = musicList[stop - 1];
                        if (element is LilyChordEvent chord && chord.Elements.Count == 0)
                        {
                            //Ignore empty chord at the end of input.
                            element = musicList[stop - 2];
                        }

                        if (element is LilyBarLine barLine)
                        {
                            barLine.BarType = null;
                        }

                        WrapRepeat(start, stop);
                        return false;
                    }
                }
                else if (state == MarkerRepeatForwardEndingStart)
                {
                    if (marker == MarkerEndingStop)
                    {
                        //TODO (upstream's): We ignore the ending position of the volta
                        //bracket and only use the position of the 'repeat backward &
                        //ending start' element, letting LilyPond handle the volta bracket
                        //automatically. Find a way to control the length of the final
                        //volta bracket.
                        PyAt(markers, prev).Kind = StateNestedEndingStart;
                        markers.RemoveAt(curr);
                        musicListPos -= 1;
                        musicList.RemoveAt(musicListPos);
                        //Continue with parsing; don't reset the music-list position for
                        //this case.
                        return true;
                    }

                    if (marker == MarkerRepeatBackward)
                    {
                        //A nested repeat that starts at the same position as the volta
                        //bracket but ends within the volta bracket.
                        int start = PyAt(markers, prev).Position;
                        int stop = markers[curr].Position;

                        LilyMusic element = musicList[start];
                        MusicXmlEndingMarker newElement = new MusicXmlEndingMarker(State);
                        newElement.Direction = ((MusicXmlMarker)element).Direction;
                        newElement.MxlEvent = GetMarkerEvent(element);
                        newElement.Volte = GetMarkerVolte(element);
                        WrapRepeat(start, stop);
                        musicList.Insert(start, newElement);

                        return false;
                    }

                    if (marker == MarkerEndOfInput)
                    {
                        //We have an implicit ending and backward repeat.
                        int start = PyAt(markers, prev).Position;

                        LilyMusic element = musicList[start];
                        MusicXmlEndingMarker newElement = new MusicXmlEndingMarker(State);
                        newElement.Direction = ((MusicXmlMarker)element).Direction;
                        newElement.MxlEvent = GetMarkerEvent(element);
                        newElement.Volte = GetMarkerVolte(element);
                        WrapRepeat(start, musicList.Count);
                        musicList.Insert(start, newElement);

                        return false;
                    }

                    if (marker == MarkerRepeatForwardEndingStart || marker == MarkerEndingStart)
                    {
                        State.Warning("ignoring unexpected " + markerId);
                        PyRemoveAt(musicList, markers[curr].Position);
                        return false;
                    }
                }
                else if (state == StateNestedEndingStart)
                {
                    if (marker == MarkerRepeatBackward)
                    {
                        int start = PyAt(markers, prev).Position;
                        int stop = markers[curr].Position;

                        //Emit the nested repeat and put it into a last ending.
                        MusicXmlEndingMarker newAfter = new MusicXmlEndingMarker(State);
                        newAfter.Direction = 1;

                        MusicXmlEndingMarker newBefore = new MusicXmlEndingMarker(State);
                        newBefore.Direction = -1;
                        newBefore.MxlEvent = GetMarkerEvent(musicList[start]);
                        newBefore.Volte = GetMarkerVolte(musicList[start]);

                        WrapRepeat(start, stop);
                        musicList.Insert(start + 1, newAfter);
                        musicList.Insert(start, newBefore);

                        //Adjust the music-list position for proper re-parsing.
                        if (markers[0].Position < musicListPos)
                        {
                            musicListPos = markers[0].Position;
                        }

                        return false;
                    }
                }
                else if (state == StateEndingsWithRepeat)
                {
                    if (marker == StateEndingsWithRepeat)
                    {
                        PyAt(markers, prev).Ranges.AddRange(markers[curr].Ranges);
                        markers.RemoveAt(curr);
                        return true;
                    }

                    if (marker == StateLastEnding)
                    {
                        PyAt(markers, prev).Kind = StateEndings;
                        PyAt(markers, prev).Ranges.Add(markers[curr].Range);
                        markers.RemoveAt(curr);
                        return true;
                    }
                }
                else if (state == StateLastEnding)
                {
                    //This catches the unusual situation where a prima volta bracket is
                    //ended before the repeat bar.
                    if (marker == MarkerRepeatBackward)
                    {
                        int endingStop = PyAt(markers, prev).Range.Stop;
                        int repeat = markers[curr].Position;

                        LilyMusic endingStopEl = musicList[endingStop];
                        LilyMusic repeatEl = musicList[repeat];

                        //We move the volta bracket end to the repeat bar.
                        musicList[repeat] = new MusicXmlRepeatEndingMarker(
                            State, (MusicXmlRepeatMarker)repeatEl,
                            (MusicXmlEndingMarker)endingStopEl);
                        musicList.RemoveAt(endingStop);

                        //Adjust the music-list position for proper re-parsing.
                        musicListPos = markers[0].Position;
                        return false;
                    }

                    if (marker == MarkerEndOfInput)
                    {
                        State.Warning("unexpected " + markerId);
                        PyRemoveAt(musicList, markers[curr].Range.Start);
                        PyRemoveAt(musicList, markers[curr].Range.Stop);

                        //Adjust the music-list position for proper re-parsing.
                        musicListPos = markers[0].Position;
                        return false;
                    }

                    State.Warning("adding repeat barline to lone " + markerId);

                    int lastEndingStop = PyAt(markers, prev).Range.Stop;
                    LilyMusic lastEndingStopEl = musicList[lastEndingStop];

                    musicList[lastEndingStop] = new MusicXmlRepeatEndingMarker(
                        State, new MusicXmlRepeatMarker(State),
                        (MusicXmlEndingMarker)lastEndingStopEl);

                    PyAt(markers, prev).Kind = StateEndingsWithRepeat;
                    PyAt(markers, prev).Ranges = new List<(int, int)> { PyAt(markers, prev).Range };
                    return true;
                }

                state = marker;
            }
        }

        //Try to identify larger structures that can be wrapped with repeated or
        //sequential music objects. If we have a hit, do this wrapping and start again.
        //
        //The music list is modified in place.
        while (true)
        {
            //`markers' is modified in place.
            markers = new List<MusicXmlRepeatMarkerEntry>();
            markersPos = 0;
            while (ParseMarkers())
            {
                markersPos = 0;
            }

            if (musicListPos == musicList.Count)
            {
                break;
            }
        }

        return musicList;
    }

    /// <summary>The element a marker came from, whichever kind of marker it is.</summary>
    /// <param name="element">The marker.</param>
    /// <returns>The element, or null.</returns>
    /// <remarks>
    /// ⚠ Upstream reads <c>.mxl_event</c> off a marker that may be an ending marker or a
    /// repeat-and-ending marker; C# cannot inherit from both, so the port asks each.
    /// </remarks>
    private static MusicXmlNode GetMarkerEvent(LilyMusic element)
        => element switch
        {
            MusicXmlEndingMarker ending => ending.MxlEvent,
            MusicXmlRepeatEndingMarker repeatEnding => repeatEnding.MxlEvent,
            _ => null,
        };

    /// <summary>Which times through a marker's ending applies to.</summary>
    /// <param name="element">The marker.</param>
    /// <returns>The volta numbers, or null.</returns>
    private static List<int> GetMarkerVolte(LilyMusic element)
        => element switch
        {
            MusicXmlEndingMarker ending => ending.Volte,
            MusicXmlRepeatEndingMarker repeatEnding => repeatEnding.Volte,
            _ => null,
        };

    /// <summary>
    /// Extracts the settings for tuplets from a notations-tuplet and a time-modification
    /// element.
    /// </summary>
    /// <param name="tupletElement">The tuplet element.</param>
    /// <param name="timeModification">The time-modification element, or null.</param>
    /// <returns>The wrapper.</returns>
    internal LilyTimeScaledMusic MusicXmlTupletToLily(
        MusicXmlTuplet tupletElement, MusicXmlTimeModification timeModification)
    {
        LilyTimeScaledMusic tsm = new LilyTimeScaledMusic(State);
        (int Normal, int Actual) fraction = (1, 1);
        if (timeModification != null)
        {
            fraction = timeModification.GetFraction();
        }

        tsm.Numerator = fraction.Normal;
        tsm.Denominator = fraction.Actual;

        (tsm.Color, tsm.FontSize) = tupletElement.GetTupletNumberAttributes();

        (int Log, int Dots)? normalType = tupletElement.GetNormalType();
        if (!normalType.HasValue && timeModification != null)
        {
            normalType = timeModification.GetNormalType();
        }

        if (!normalType.HasValue && timeModification != null)
        {
            MusicXmlNode note = timeModification.Parent;
            if (note != null)
            {
                normalType = ((MusicXmlNote)note).GetDurationInfo();
            }
        }

        if (normalType.HasValue)
        {
            LilyDuration normalNote = new LilyDuration(State);
            normalNote.DurationLog = normalType.Value.Log;
            normalNote.Dots = normalType.Value.Dots;
            tsm.NormalType = normalNote;
        }

        (int Log, int Dots)? actualType = tupletElement.GetActualType();
        if (actualType.HasValue)
        {
            LilyDuration actualNote = new LilyDuration(State);
            actualNote.DurationLog = actualType.Value.Log;
            actualNote.Dots = actualType.Value.Dots;
            tsm.ActualType = actualNote;
        }

        //Obtain non-default nrs of notes from the tuplet object!
        tsm.DisplayNumerator = tupletElement.GetNormalNr();
        tsm.DisplayDenominator = tupletElement.GetActualNr();

        if (tupletElement.Attribute("bracket") == "no")
        {
            tsm.DisplayBracket = null;
        }
        else if (tupletElement.Attribute("line-shape") == "curved")
        {
            tsm.DisplayBracket = "curved";
        }
        else
        {
            tsm.DisplayBracket = "bracket";
        }

        string showNumber = tupletElement.Attribute("show-number");
        if (showNumber != null)
        {
            tsm.DisplayNumber = DisplayValues.TryGetValue(showNumber, out string number)
                ? number
                : "actual";
        }

        string showType = tupletElement.Attribute("show-type");
        if (showType != null)
        {
            tsm.DisplayType = DisplayValues.TryGetValue(showType, out string type)
                ? type
                : null;
        }

        if (!Options.NoArticulationDirections)
        {
            string direction = tupletElement.Attribute("placement");
            if (direction != null)
            {
                tsm.ForceDirection = MusicXmlDirectionToIndicator(direction);
            }
        }

        return tsm;
    }

    private static readonly Dictionary<string, string> DisplayValues
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "none", null },
            { "actual", "actual" },
            { "both", "both" },
        };

    /// <summary>
    /// Collects music from the music list demarcated by the events into time-scaled
    /// wrappers.
    /// </summary>
    /// <param name="musicList">The music.</param>
    /// <param name="events">The tuplet events, in the order they were registered.</param>
    /// <returns>The regrouped music.</returns>
    internal List<LilyMusic> GroupTuplets(
        List<LilyMusic> musicList,
        List<(LilyChordEvent Chord, MusicXmlTuplet Tuplet,
              MusicXmlTimeModification TimeModification, bool Visible)> events)
    {
        List<(int Start, int Stop, LilyTimeScaledMusic Tsm)> indices
            = new List<(int, int, LilyTimeScaledMusic)>();
        Dictionary<string, int> brackets = new Dictionary<string, int>(StringComparer.Ordinal);

        int j = 0;
        foreach ((LilyChordEvent evChord, MusicXmlTuplet tupletElement,
                  MusicXmlTimeModification timeModification, bool visible) in events)
        {
            while (j < musicList.Count)
            {
                //Since its registration in `events' the chord object might be meanwhile
                //wrapped into a two-stem tremolo or tuplet.
                if (musicList[j].Contains(evChord))
                {
                    break;
                }

                j += 1;
            }

            string nr = tupletElement.Attribute("number", "0");
            if (tupletElement.Attribute("type") == "start")
            {
                LilyTimeScaledMusic tupletObject
                    = MusicXmlTupletToLily(tupletElement, timeModification);
                tupletObject.Visible = visible;

                bool haveActualNormal = tupletObject.ActualType != null
                                        && tupletObject.NormalType != null
                                        && tupletObject.DisplayNumerator.HasValue
                                        && tupletObject.DisplayNumerator.Value != 0
                                        && tupletObject.DisplayDenominator.HasValue
                                        && tupletObject.DisplayDenominator.Value != 0;

                if (musicList[j] is LilyRepeatedMusic repeated
                    && repeated.RepeatType == "tremolo")
                {
                    if (haveActualNormal)
                    {
                        PythonFraction factor =
                            tupletObject.NormalType.GetLength()
                            * PythonFraction.FromLong(tupletObject.DisplayNumerator.Value)
                            / (tupletObject.ActualType.GetLength()
                               * PythonFraction.FromLong(
                                   tupletObject.DisplayDenominator.Value));
                        tupletObject.Numerator = (int)factor.Numerator;
                        tupletObject.Denominator = (int)factor.Denominator;
                    }
                    else
                    {
                        //There are no explicitly specified numerator and denumerator
                        //values, so adjust the time modification for the double-note
                        //tremolo.
                        tupletObject.Numerator *= 2;
                    }
                }
                else if (haveActualNormal)
                {
                    tupletObject.Numerator = tupletObject.DisplayNumerator.Value;
                    tupletObject.Denominator = tupletObject.DisplayDenominator.Value;
                }

                indices.Add((j, -1, tupletObject));
                brackets[nr] = indices.Count - 1;
            }
            else if (tupletElement.Attribute("type") == "stop")
            {
                //Ignore tuplet ends without corresponding starts.
                if (brackets.TryGetValue(nr, out int bracketIndex))
                {
                    //Set the ending position to j.
                    indices[bracketIndex] = (indices[bracketIndex].Start, j,
                        indices[bracketIndex].Tsm);
                    brackets.Remove(nr);
                }
            }

            //We don't increase `j' before the next loop since the element at that position
            //might contain the end of a tuplet, too.
        }

        //Sort `indices' by ascending start values as the primary and descending end values
        //as the secondary key. This allows us to walk the list from the start, processing
        //the indices in linear order while popping off completed tuplet groups.
        //
        //⚠ python's `list.sort' is STABLE, and upstream sorts twice to build the compound
        //key; the port does the same with a stable ordering rather than one comparison,
        //because the two are not the same sort when keys tie.
        indices = indices
            .OrderByDescending(entry => entry.Stop)
            .ToList();
        indices = indices
            .OrderBy(entry => entry.Start)
            .ToList();

        List<LilyMusic> newList = new List<LilyMusic>();
        List<List<LilyMusic>> outStack = new List<List<LilyMusic>>();
        outStack.Add(newList);
        List<LilyMusic> outList = outStack[outStack.Count - 1];
        int last = 0;
        int idx = 0;
        while (indices.Count > 0)
        {
            (int i1, int i2, LilyTimeScaledMusic tsm) = indices[idx];
            if (i1 > i2)
            {
                //Ignore tuplet starts without corresponding ends.
                if (outStack.Count > 1)
                {
                    outStack.RemoveAt(outStack.Count - 1);
                }

                outList = outStack[outStack.Count - 1];
                indices.RemoveAt(idx);
                if (idx > 0)
                {
                    idx -= 1;
                }

                continue;
            }

            if (last <= i1)
            {
                //We have a new tuplet.
                outList.AddRange(musicList.GetRange(last, Math.Max(0, i1 - last)));
                outStack.Add(new List<LilyMusic>());
                outList = outStack[outStack.Count - 1];
                last = i1;
            }

            if (idx + 1 < indices.Count && i2 > indices[idx + 1].Start)
            {
                //We have a nested tuplet.
                idx += 1;
                continue;
            }

            i2 += 1;
            //At this point, the range encompasses all (remaining) notes of the current
            //tuplet. There might be dynamics following this range, however, which apply
            //to the last note of the tuplet. Advance the end to include them in the range.
            while (i2 < musicList.Count && musicList[i2] is LilyDynamicsEvent)
            {
                i2 += 1;
            }

            if (last < i2)
            {
                outList.AddRange(musicList.GetRange(last, Math.Max(0, i2 - last)));
            }

            LilySequentialMusic seq = new LilySequentialMusic(State);
            seq.Elements = outList;
            tsm.Element = seq;

            outStack.RemoveAt(outStack.Count - 1);
            outList = outStack[outStack.Count - 1];

            outList.Add(tsm);

            last = i2;
            indices.RemoveAt(idx);
            if (idx > 0)
            {
                idx -= 1;
            }
        }

        newList.AddRange(musicList.GetRange(last, Math.Max(0, musicList.Count - last)));
        return newList;
    }

    /// <summary>Collects double-note tremolos into repeated-music wrappers.</summary>
    /// <param name="musicList">The music.</param>
    /// <param name="events">The tremolo events, in the order they were registered.</param>
    /// <returns>The regrouped music.</returns>
    internal List<LilyMusic> GroupTremolos(
        List<LilyMusic> musicList,
        List<(LilyChordEvent Chord, MusicXmlNode Tremolo)> events)
    {
        int? leftIdx = null;
        int? rightIdx = null;

        int numBeams = 0;
        int numStrokes = 0;
        string tremoloColor = null;

        List<LilyMusic> newList = new List<LilyMusic>();

        int last = 0;
        int j = 0;
        foreach ((LilyChordEvent evChord, MusicXmlNode tremoloElement) in events)
        {
            while (j < musicList.Count)
            {
                if (ReferenceEquals(musicList[j], evChord))
                {
                    break;
                }

                j += 1;
            }

            MusicXmlNode note = tremoloElement.Parent.Parent.Parent;

            if (tremoloElement.Attribute("type") == "start")
            {
                if (leftIdx.HasValue)
                {
                    State.Warning("Ignoring double-note tremolo without end");
                }

                leftIdx = j;

                List<MusicXmlNode> beams = note.GetList("beam")
                    .Where(b => ((MusicXmlBeam)b).GetSpannerType() == "begin"
                                || ((MusicXmlBeam)b).GetSpannerType() == "continue")
                    .ToList();
                numBeams = beams.Count > 0
                    ? beams.Max(
                        b => int.Parse(b.Attribute("number"), CultureInfo.InvariantCulture))
                    : 0;
                numStrokes = int.Parse(
                    tremoloElement.GetText().Trim(), CultureInfo.InvariantCulture);
                //The stroke count must be in the range [0;8].
                numStrokes = Math.Max(0, Math.Min(numStrokes, 8));

                //LilyPond can't set beam and tremolo stroke colors separately. We first
                //check for beam color, then for tremolo color.
                //⚠ NOT reset per tremolo: upstream initialises this once, before the loop,
                //so a later tremolo whose beams and element name no colour inherits the
                //one an earlier tremolo found. Reproduced.
                foreach (MusicXmlNode beam in beams)
                {
                    string color = beam.Attribute("color");
                    if (color != null)
                    {
                        tremoloColor = color;
                        break;
                    }
                }

                if (tremoloColor == null)
                {
                    string color = tremoloElement.Attribute("color");
                    if (color != null)
                    {
                        tremoloColor = color;
                    }
                }

                continue;
            }

            if (!leftIdx.HasValue)
            {
                State.Warning("Ignoring double-note tremolo without start");
                continue;
            }

            //We take all information on a double-stem tremolo from its left element.
            rightIdx = j;

            //We found a double-note tremolo.
            //
            //Compute the values of `count' and `dur' as used in
            //
            //  \repeat tremolo <count> { left<dur> right<dur> }
            int dur = 1 << (2 + numBeams + numStrokes);

            //We need the duration without the factor (i.e., without the possible scaling
            //caused by tuplets).
            PythonFraction length = musicList[leftIdx.Value].GetLength(false)
                                    * PythonFraction.FromLong(dur)
                                    / PythonFraction.FromLong(2);
            if (length.Denominator > 1)
            {
                State.Warning("Strange tremolo note length encountered");
            }

            object count = (int)length.Numerator;

            //Add the factor again since the time-scaled wrapper compensates it while
            //emitting durations.
            PythonFraction factor = musicList[leftIdx.Value].GetLength(true)
                                    * PythonFraction.FromLong(dur) / length;
            LilyDuration duration = LilyDuration.FromFraction(
                State, new PythonFraction(1, dur));
            duration.Factor = factor;

            //Adjust duration of chord notes.
            foreach (int i in new[] { leftIdx.Value, rightIdx.Value })
            {
                LilyChordEvent chord = (LilyChordEvent)musicList[i];
                foreach (LilyNoteEvent noteEvent in chord.Elements.OfType<LilyNoteEvent>())
                {
                    noteEvent.Duration = duration;
                }
            }

            //At this point, the range encompasses the two notes of the double-note
            //tremolo. There might be dynamics following this range, however, which apply
            //to the right note of the tremolo (this doesn't make any sense under normal
            //circumstances, but who knows what the dynamics get used for). Advance the
            //right index to include them in the range.
            while (rightIdx.Value < musicList.Count
                   && musicList[rightIdx.Value] is LilyDynamicsEvent)
            {
                rightIdx = rightIdx.Value + 1;
            }

            newList.AddRange(
                musicList.GetRange(last, Math.Max(0, leftIdx.Value - last)));

            LilyRepeatedMusic repeated = new LilyRepeatedMusic(State);
            repeated.RepeatType = "tremolo";
            repeated.RepeatCount = count;
            repeated.TremoloStrokes = numStrokes;
            repeated.Color = tremoloColor;
            repeated.SetMusic(
                musicList.GetRange(
                    leftIdx.Value, Math.Max(0, rightIdx.Value + 1 - leftIdx.Value)));

            newList.Add(repeated);

            last = rightIdx.Value + 1;
            leftIdx = null;
            rightIdx = null;
        }

        newList.AddRange(musicList.GetRange(last, Math.Max(0, musicList.Count - last)));
        return newList;
    }
}
