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

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (LilyPondVoiceBuilder, VoiceData and extract_lyrics);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>The music of one voice, as it is being built up.</summary>
internal sealed class MusicXmlVoiceBuilder : LilyExpression
{
    /// <summary>Builds the builder.</summary>
    /// <param name="state">The import this voice belongs to.</param>
    internal MusicXmlVoiceBuilder(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The music built so far.</summary>
    internal List<LilyMusic> Elements { get; } = new List<LilyMusic>();

    /// <summary>The events waiting for the next note or rest.</summary>
    internal List<LilyMusic> PendingElements { get; private set; } = new List<LilyMusic>();

    /// <summary>The events waiting to be written just before the next note.</summary>
    internal List<LilyMusic> PendingLast { get; private set; } = new List<LilyMusic>();

    /// <summary>Where the music built so far ends.</summary>
    internal PythonFraction EndMoment { get; set; } = PythonFraction.Zero;

    /// <summary>Where the last thing added begins.</summary>
    internal PythonFraction BeginMoment { get; set; } = PythonFraction.Zero;

    /// <summary>Whether the next thing added sits at the start of a measure.</summary>
    internal bool AtMeasureStart { get; set; } = true;

    /// <summary>How long the current measure is.</summary>
    internal PythonFraction? MeasureLength { get; set; }

    /// <summary>How long the previous measure was.</summary>
    internal PythonFraction? PrevMeasureLength { get; set; }

    /// <summary>Whether the measure length has been written.</summary>
    internal bool SetMeasureLength { get; set; }

    /// <summary>Whether this voice is worth writing out at all.</summary>
    internal bool HasRelevantElements { get; set; }

    /// <summary>Which bar the builder has reached.</summary>
    internal int BarNumber { get; set; }

    /// <summary>How many measures the pending multi-measure rest covers.</summary>
    internal int MultiMeasureCount { get; set; }

    /// <summary>The pending multi-measure rest.</summary>
    internal LilyRestEvent MultiMeasureRest { get; set; }

    /// <summary>The chord holding the pending multi-measure rest.</summary>
    internal LilyChordEvent MultiMeasureEvChord { get; set; }

    /// <inheritdoc/>
    internal override bool Contains(LilyExpression element)
    {
        if (this == element)
        {
            return true;
        }

        foreach (LilyMusic e in Elements)
        {
            if (e.Contains(element))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ Upstream's <c>Base</c> declares no <c>print_ly</c>, so asking a voice builder to
    /// print itself raises <c>AttributeError</c>; nothing does. The port's base declares
    /// one, so this throws for the same reason.
    /// </remarks>
    internal override void PrintLy(LilyOutputPrinter printer)
        => throw new InvalidOperationException(
            "A voice builder is not a music expression and cannot print itself.");

    /// <summary>Writes out the pending multi-measure rest, if there is one.</summary>
    /// <param name="barCheck">Whether to add a bar check after it.</param>
    internal void EmitMultiMeasureRest(bool barCheck = true)
    {
        if (MultiMeasureRest == null)
        {
            return;
        }

        //Doing `R1^\markup{...}' would center the markup over the multi-measure rest,
        //which is most certainly not intended. Instead, do `<>^\markup{...} R1'. Since it
        //doesn't cause a problem, we do this for the remaining pending elements also.
        if (PendingElements.Count > 0)
        {
            AddPendingElements(true);
        }

        Elements.Add(MultiMeasureEvChord);
        MultiMeasureCount = 0;
        MultiMeasureRest = null;
        MultiMeasureEvChord = null;

        if (barCheck)
        {
            AddBarCheck();
        }
    }

    /// <summary>Records how long the last thing added lasts.</summary>
    /// <param name="duration">The length.</param>
    internal void SetDuration(PythonFraction duration) => EndMoment = BeginMoment + duration;

    /// <summary>How long the last thing added lasts.</summary>
    /// <returns>The length.</returns>
    internal PythonFraction CurrentDuration() => EndMoment - BeginMoment;

    /// <summary>Writes out the events that were waiting for a note.</summary>
    /// <param name="withEmptyChord">Whether to write an empty chord first.</param>
    /// <param name="toBarline">Whether the events belong to the following bar line.</param>
    /// <remarks>
    /// Elements right before a barline or before a measure end (in particular
    /// <c>&lt;direction&gt;</c>) are associated with the following bar line and not with
    /// the first note in the next bar.
    /// <para>
    /// TODO (upstream's): Right now, only the hairpin fully supports this attribute. Other
    /// types (dynamics, words, etc.) use it temporarily until a solution similar to
    /// handling <c>&lt;offset&gt;</c> gets implemented, namely by moving the event back in
    /// time a little bit.
    /// </para>
    /// </remarks>
    internal void AddPendingElements(bool withEmptyChord = false, bool toBarline = false)
    {
        if (withEmptyChord)
        {
            Elements.Add(new LilyEmptyChord(State));
        }

        if (toBarline)
        {
            foreach (LilyMusic e in PendingElements)
            {
                SetToBarline(e);
            }
        }

        Elements.AddRange(PendingElements);
        PendingElements = new List<LilyMusic>();
    }

    /// <summary>Marks an event as belonging to the following bar line.</summary>
    /// <param name="ev">The event.</param>
    /// <remarks>
    /// ⚠ Upstream assigns <c>to_barline</c> on every pending element, growing the
    /// attribute on the classes that do not declare it; only the three that declare it
    /// ever read it, so the port sets those and lets the rest pass.
    /// </remarks>
    private static void SetToBarline(LilyMusic ev)
    {
        switch (ev)
        {
            case LilyHairpinEvent hairpin:
                hairpin.ToBarline = true;
                break;
            case LilyDynamicsEvent dynamics:
                dynamics.ToBarline = true;
                break;
            case LilyTextEvent text:
                text.ToBarline = true;
                break;
        }
    }

    /// <summary>Writes out the events that were waiting for the next note.</summary>
    /// <remarks>
    /// The elements are separated from the previous chord with <c>&lt;&gt;</c>. We use
    /// this to implement support for <c>&lt;notation&gt;</c> elements that start and stop
    /// at the same time step (for example, a wavy line of a trill). Normally, such
    /// elements have different values of the <c>relative-x</c> property to indicate their
    /// horizontal positions, but <c>relative-x</c> gets ignored by <c>musicxml2ly</c>.
    /// <para>
    /// Since <c>&lt;&gt;</c> must be inserted into the output after any
    /// <c>&lt;direction&gt;</c> elements have been emitted, we handle this here and not
    /// while constructing the chord.
    /// </para>
    /// </remarks>
    internal void AddPendingLast()
    {
        Elements.Add(new LilyEmptyChord(State));
        Elements.AddRange(PendingLast);
        PendingLast = new List<LilyMusic>();
    }

    /// <summary>Adds music that takes up time.</summary>
    /// <param name="music">The music.</param>
    /// <param name="duration">How long it lasts.</param>
    /// <param name="relevant">Whether it makes the voice worth writing.</param>
    /// <param name="grace">The grace note this belongs to, when it is one.</param>
    internal void AddMusic(
        LilyMusic music, PythonFraction duration, bool relevant = true,
        MusicXmlNode grace = null)
    {
        HasRelevantElements = HasRelevantElements || relevant;

        bool isBarline = music is LilyBarLine;
        EmitMultiMeasureRest(!isBarline);

        //The elements in the pending-last list were added while processing the previous
        //`<note>' element and must be emitted before the current `<note>' element gets
        //handled.
        if (music is LilyChordEvent && PendingLast.Count > 0)
        {
            AddPendingLast();
        }

        Elements.Add(music);
        BeginMoment = EndMoment;
        SetDuration(duration);

        //Insert all pending dynamics right after the note or rest if it is not a grace
        //note or rest (which we handle separately).
        if (music is LilyChordEvent && grace == null)
        {
            if (PendingElements.Count > 0)
            {
                AddPendingElements();
            }
        }
    }

    /// <summary>Adds music that does not affect the position in the measure.</summary>
    /// <param name="command">The music.</param>
    /// <param name="relevant">Whether it makes the voice worth writing.</param>
    internal void AddCommand(LilyMusic command, bool relevant = true)
    {
        //Reset the measure length before emitting a bar line check.
        bool barCheck = !(command is LilyMeasureLengthEvent);
        EmitMultiMeasureRest(barCheck);

        HasRelevantElements = HasRelevantElements || relevant;
        Elements.Add(command);
    }

    /// <summary>Adds a bar line.</summary>
    /// <param name="barline">The bar line.</param>
    /// <param name="noBarNumber">Whether to leave the bar number off.</param>
    internal void AddBarline(LilyBarLine barline, bool noBarNumber = true)
    {
        if (PendingElements.Count > 0)
        {
            AddPendingElements(false, true);
        }

        //(Re)store the relevance flag so that a barline alone does not trigger output for
        //figured bass or chord names.
        bool hasRelevant = HasRelevantElements;

        int barNumber = noBarNumber ? 0 : BarNumber;

        //Ignore staff and page breaks together with volta-related repeat markers while
        //checking for a bar line right before.
        LilyBarLine prevBarline = null;
        LilyMusic elem = null;
        for (int i = Elements.Count - 1; i >= 0; i--)
        {
            elem = Elements[i];
            if (!(elem is LilyBreak || elem is MusicXmlMarker))
            {
                break;
            }
        }

        if (elem is LilyBarLine found)
        {
            prevBarline = found;
        }

        if (prevBarline != null && MultiMeasureRest == null)
        {
            //If we have an existing bar line object and no pending multi-measure rest, set
            //its bar number.
            prevBarline.BarNumber = barNumber;
        }
        else
        {
            //Otherwise add a new bar line object.
            barline.BarNumber = barNumber;
            AddMusic(barline, PythonFraction.Zero);
        }

        HasRelevantElements = hasRelevant;
    }

    /// <summary>Adds code without touching the relevance flag.</summary>
    /// <param name="command">The music.</param>
    internal void AddIrrelevant(LilyMusic command)
    {
        bool relevant = HasRelevantElements;
        AddCommand(command);
        HasRelevantElements = relevant;
    }

    /// <summary>Forgets the events that were waiting for a note.</summary>
    internal void ClearPendingElements() => PendingElements = new List<LilyMusic>();

    /// <summary>Stores a dynamic item until the next note or rest.</summary>
    /// <param name="dynamic">The item.</param>
    internal void AddDynamics(LilyMusic dynamic) => PendingElements.Add(dynamic);

    /// <summary>Stores an item that comes right before the next note or bar line.</summary>
    /// <param name="last">The item.</param>
    internal void AddLast(LilyMusic last) => PendingLast.Add(last);

    /// <summary>Adds a bar check, unless a multi-measure rest is pending.</summary>
    internal void AddBarCheck()
    {
        if (MultiMeasureRest == null)
        {
            AddBarline(new LilyBarLine(State), false);
        }
    }

    /// <summary>Skips forward to a moment, if it lies ahead.</summary>
    /// <param name="moment">The moment.</param>
    /// <param name="graceSkip">The grace-note skip to write first, if any.</param>
    internal void JumpForward(PythonFraction moment, LilyDuration graceSkip = null)
    {
        PythonFraction currentEnd = EndMoment;
        PythonFraction diff = moment - currentEnd;
        if (diff.Sign > 0)
        {
            //TODO (upstream's): Use the time signature for skips, too. Problem: The skip
            //might not start at a measure boundary!
            LilySkipEvent skip = new LilySkipEvent(State);
            skip.Duration.SetFromFraction(diff);
            skip.GraceSkip = graceSkip;

            LilyChordEvent evc = new LilyChordEvent(State);
            evc.When = currentEnd;
            evc.Elements.Add(skip);
            AddMusic(evc, diff, false);
        }
    }

    /// <summary>The chord at a moment, if the builder is already there.</summary>
    /// <param name="startingAt">The moment.</param>
    /// <returns>The chord, or null after skipping forward to that moment.</returns>
    internal LilyChordEvent LastEventChord(PythonFraction startingAt)
    {
        //If the position matches, find the last chord — do not cross a bar line!
        int at = Elements.Count - 1;
        while (at >= 0 && !(Elements[at] is LilyChordEvent || Elements[at] is LilyBarLine))
        {
            at -= 1;
        }

        if (Elements.Count > 0 && at >= 0 && Elements[at] is LilyChordEvent chord
            && BeginMoment == startingAt)
        {
            return chord;
        }

        JumpForward(startingAt);
        return null;
    }

    /// <summary>The chord a displaced direction element belongs to.</summary>
    /// <param name="dirIdx">Where the direction element sits.</param>
    /// <param name="dirPos">The moment the direction element is displaced to.</param>
    /// <param name="musicLength">How long the whole voice is.</param>
    /// <returns>The chord and the offset from it, or a null chord when there is none.</returns>
    internal (LilyChordEvent Chord, PythonFraction Offset) FindChordEventForOffset(
        int dirIdx, PythonFraction dirPos, PythonFraction musicLength)
    {
        //Ignore last dummy chord.
        int limit = Elements.Count - 1;
        int i = dirIdx;

        //Get a start element for the search.
        while (i < limit)
        {
            if (Elements[i] is LilyChordEvent)
            {
                break;
            }

            i += 1;
        }

        if (i == limit)
        {
            i = dirIdx - 1;
            while (i >= 0)
            {
                if (Elements[i] is LilyChordEvent)
                {
                    break;
                }

                i -= 1;
            }

            if (i == -1)
            {
                State.Warning(
                    "cannot apply <offset> because there are no notes or rests");
                return (null, PythonFraction.Zero);
            }
        }

        //Find the chord element that has the smallest non-negative offset to the
        //`<direction>' element with the applied `<offset>' value.
        int ceIdx = i;
        PythonFraction offset = dirPos - ((LilyChordEvent)Elements[ceIdx]).When;

        if (offset.Sign < 0)
        {
            i = dirIdx - 1;
            while (i >= 0)
            {
                if (Elements[i] is LilyChordEvent candidate)
                {
                    int newCeIdx = i;
                    PythonFraction newOffset = dirPos - candidate.When;
                    if (newOffset.Sign >= 0)
                    {
                        return ((LilyChordEvent)Elements[newCeIdx], newOffset);
                    }

                    ceIdx = newCeIdx;
                    offset = newOffset;
                }

                i -= 1;
            }

            State.Warning(
                "too large negative <offset> value; aligning to start of music instead");
            return ((LilyChordEvent)Elements[ceIdx], PythonFraction.Zero);
        }

        i = ceIdx + 1;
        while (i < limit)
        {
            if (Elements[i] is LilyChordEvent candidate)
            {
                int newCeIdx = i;
                PythonFraction newOffset = dirPos - candidate.When;
                if (newOffset.Sign < 0)
                {
                    return ((LilyChordEvent)Elements[ceIdx], offset);
                }

                ceIdx = newCeIdx;
                offset = newOffset;
            }

            i += 1;
        }

        LilyChordEvent ce = (LilyChordEvent)Elements[ceIdx];
        if (ce.When + offset >= musicLength)
        {
            State.Warning(
                "too large <offset> value; aligning to almost the end of music instead");
            //We use 1/32 before the end of music as an ad-hoc value to position the
            //`<direction>' element.
            offset = musicLength - ce.When - new PythonFraction(1, 32);
        }

        return (ce, offset);
    }

    /// <summary>Attaches every displaced direction element to the chord it belongs to.</summary>
    /// <param name="musicLength">How long the whole voice is.</param>
    /// <remarks>
    /// Before calling this function, the offset field of a direction element contains
    /// (absolute) moments. After calling, the field holds a positive time offset relative
    /// to the chord element to which the direction element gets added.
    /// </remarks>
    internal void LinkOffsetElements(PythonFraction musicLength)
    {
        for (int i = 0; i < Elements.Count; i++)
        {
            LilyMusic el = Elements[i];
            if (!(el is ILilyOffsetEvent offsetEvent) || offsetEvent.Offset.IsZero)
            {
                continue;
            }

            //`<offset>' elements are infrequent, and if they occur, their values are small
            //in the normal case. We thus don't get quadratic behaviour in searching for
            //the nearest chord element as implemented here.
            (LilyChordEvent ce, PythonFraction offset)
                = FindChordEventForOffset(i, offsetEvent.Offset, musicLength);
            ce.OffsetElements.Add((el, offset));
        }
    }
}

/// <summary>Everything one voice contributes to the document.</summary>
internal sealed class MusicXmlVoiceData
{
    /// <summary>The voice's LilyPond identifier.</summary>
    internal string VoiceName { get; set; }

    /// <summary>The input-side voice this was built from.</summary>
    internal MusicXmlVoice VoiceData { get; set; }

    /// <summary>The music itself, inside whatever wrappers the options ask for.</summary>
    internal LilyMusic LyVoice { get; set; }

    /// <summary>The figured bass, when the voice has one.</summary>
    internal LilyMusic FiguredBass { get; set; }

    /// <summary>The chord names, when the voice has any.</summary>
    internal LilyMusic ChordNames { get; set; }

    /// <summary>The fretboards, when the voice has any.</summary>
    internal LilyMusic FretBoards { get; set; }

    /// <summary>The lyric lines, by their number.</summary>
    internal Dictionary<string, LilyLyrics> LyricsDict { get; }
        = new Dictionary<string, LilyLyrics>(StringComparer.Ordinal);

    /// <summary>The order the lyric lines are written in.</summary>
    internal List<string> LyricsOrder { get; set; } = new List<string>();
}
