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

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (ChordEvent, ArpeggioChordEvent, Partial, MeasureLengthEvent, BarLine and ForBarline);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>Everything that sounds at one moment of one voice.</summary>
internal class LilyChordEvent : LilyNestedMusic
{
    /// <summary>Builds the chord.</summary>
    /// <param name="state">The import this chord belongs to.</param>
    internal LilyChordEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The grace notes written after this chord.</summary>
    internal LilySequentialMusic AfterGraceElements { get; set; }

    /// <summary>The grace notes written before this chord.</summary>
    internal LilySequentialMusic GraceElements { get; set; }

    /// <summary>Which kind of grace group precedes this chord.</summary>
    internal string GraceType { get; set; }

    /// <summary>The moment this chord sounds at.</summary>
    /// <remarks>For handling <c>&lt;direction&gt;</c> elements containing
    /// <c>&lt;offset&gt;</c>.</remarks>
    internal PythonFraction When { get; set; } = PythonFraction.Zero;

    /// <summary>The displaced events written before this chord, with their offsets.</summary>
    internal List<(LilyMusic Element, PythonFraction Offset)> OffsetElements { get; }
        = new List<(LilyMusic, PythonFraction)>();

    /// <summary>Which context a cross-staff or cross-voice arpeggio spans.</summary>
    internal string ArpeggioType { get; set; }

    /// <summary>Records a grace note written before this chord.</summary>
    /// <param name="element">The note.</param>
    internal void AppendGrace(LilyMusic element)
    {
        if (element != null)
        {
            if (GraceElements == null)
            {
                GraceElements = new LilySequentialMusic(State);
            }

            GraceElements.Append(element);
        }
    }

    /// <summary>Records a grace note written after this chord.</summary>
    /// <param name="element">The note.</param>
    internal void AppendAfterGrace(LilyMusic element)
    {
        if (element != null)
        {
            if (AfterGraceElements == null)
            {
                AfterGraceElements = new LilySequentialMusic(State);
            }

            AfterGraceElements.Append(element);
        }
    }

    /// <summary>Whether anything sounds in this chord.</summary>
    /// <returns>Whether it holds a note or a rest.</returns>
    internal bool HasElements()
        => Elements.Any(e => e is LilyNoteEvent || e is LilyRestEvent);

    /// <inheritdoc/>
    internal override PythonFraction GetLength(bool withFactor = true)
        => Elements.Count > 0
            ? Elements.Select(e => e.GetLength(withFactor)).Max()
            : PythonFraction.Zero;

    /// <summary>How long this chord lasts.</summary>
    /// <returns>The duration, or null when nothing in it sounds.</returns>
    internal LilyDuration GetDuration()
    {
        List<LilyRhythmicEvent> noteEvents = Elements
            .Where(e => e is LilyNoteEvent || e is LilyRestEvent)
            .Cast<LilyRhythmicEvent>()
            .ToList();
        return noteEvents.Count > 0 ? noteEvents[0].Duration : null;
    }

    /// <summary>What an arpeggiated chord writes before itself; nothing here.</summary>
    /// <param name="printer">Where to write.</param>
    internal virtual void ArpeggioPreChord(LilyOutputPrinter printer)
    {
    }

    /// <summary>What an arpeggiated chord writes after itself; nothing here.</summary>
    /// <param name="printer">Where to write.</param>
    internal virtual void ArpeggioPostChord(LilyOutputPrinter printer)
    {
    }

    /// <summary>Whether an associated event changes how the note head is drawn.</summary>
    /// <param name="noteEvent">The note.</param>
    /// <returns>Whether the head changed.</returns>
    private static bool NoteheadIsChanged(LilyNoteEvent noteEvent)
    {
        foreach (LilyExpression associated in noteEvent.AssociatedEvents)
        {
            if (associated is LilyNoteStyleEvent styleEvent)
            {
                if (!string.IsNullOrEmpty(styleEvent.Style) || styleEvent.Filled != null)
                {
                    return true;
                }
            }
            else if (associated is LilyParenthesizeEvent)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks for toe-heel substitutions and modifies the type fields in the elements
    /// accordingly.
    /// </summary>
    /// <param name="elements">The chord's non-rhythmic events.</param>
    /// <param name="direction">Which side of the staff is being collected.</param>
    private static void CollectToeHeels(List<LilyMusic> elements, int direction)
    {
        List<LilyArticulationWithSubstitutionEvent> toeHeelEvents
            = new List<LilyArticulationWithSubstitutionEvent>();
        bool haveSubstitution = false;
        string prefix = direction == 1 ? "r" : "l";

        foreach (LilyMusic element in elements)
        {
            if (!(element is LilyArticulationWithSubstitutionEvent articulation))
            {
                continue;
            }

            //  if not ev.visible:
            //      continue

            //We collect `<toe>' and `<heel>' elements without `placement' attribute
            //together with elements that have `placement="above"'.
            int forceDirection = articulation.ForceDirection ?? 1;
            if (forceDirection != direction)
            {
                continue;
            }

            //`\rtoe' and siblings ignore direction modifiers.
            articulation.ForceDirection = null;

            //We support a single toe-heel substitution; any other toe or heel element
            //afterwards is ignored.
            if (haveSubstitution)
            {
                articulation.Type = null;
                continue;
            }

            //This creates `\lheel', for example.
            articulation.Type = prefix + articulation.Type;
            toeHeelEvents.Add(articulation);

            if (articulation.Substitution)
            {
                haveSubstitution = true;
            }
        }

        if (toeHeelEvents.Count == 0)
        {
            return;
        }

        //Modify last two elements. Ignore substitution if we have only a single one.
        //
        //We don't support separate setting of color and font size of the right element.
        if (haveSubstitution && toeHeelEvents.Count > 1)
        {
            string left = toeHeelEvents[toeHeelEvents.Count - 2].Type;
            string right = toeHeelEvents[toeHeelEvents.Count - 1].Type.Substring(1);
            toeHeelEvents[toeHeelEvents.Count - 1].Type = null;
            //This creates `\rtoeheel', for example.
            toeHeelEvents[toeHeelEvents.Count - 2].Type = left + right;
        }
    }

    /// <summary>Whether every character of a string is an ASCII digit.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it reads as a number.</returns>
    /// <remarks>
    /// ⚠ python's <c>str.isdigit</c> also answers true for the characters whose Unicode
    /// numeric type is 'digit' without being in category Nd — superscripts, for
    /// instance. <c>char.IsDigit</c> is Nd alone. The difference can only show in a
    /// <c>&lt;fingering&gt;</c> written with such a character, which the corpus does not
    /// contain; the bound is recorded rather than approximated.
    /// </remarks>
    private static bool IsDigit(string text)
        => text.Length > 0 && text.All(char.IsDigit);

    /// <summary>
    /// Combines multiple <c>&lt;fingering&gt;</c> or <c>&lt;pluck&gt;</c> elements of a
    /// note into a single fingering instruction, with its elements concatenated
    /// horizontally.
    /// </summary>
    /// <param name="noteEvent">The note.</param>
    /// <param name="direction">Which side of the staff is being collected.</param>
    /// <param name="pluck">Whether plucking marks are being collected.</param>
    /// <remarks>
    /// An alternate fingering gets enclosed in parentheses, a substitution fingering is
    /// connected with an overtie to the previous fingering. The type fields are modified
    /// accordingly.
    /// </remarks>
    private void CollectFingerings(LilyNoteEvent noteEvent, int direction, bool pluck)
    {
        List<LilyFingeringEvent> fingeringEvents = new List<LilyFingeringEvent>();
        bool haveSubstitution = false;
        bool needMarkup = false;

        foreach (LilyExpression associated in noteEvent.AssociatedEvents)
        {
            if (!(associated is LilyFingeringEvent fingeringEvent))
            {
                continue;
            }

            if (fingeringEvent.IsPluck != pluck)
            {
                continue;
            }

            if (!fingeringEvent.Visible)
            {
                continue;
            }

            //We collect `<fingering>' elements without `placement' attribute together
            //with elements that have `placement="above"'. Dito for `<pluck>'.
            int forceDirection = fingeringEvent.ForceDirection ?? 1;
            if (forceDirection != direction)
            {
                continue;
            }

            //We support a single fingering substitution at the end; any other fingering
            //afterwards is ignored.
            if (haveSubstitution)
            {
                fingeringEvent.Type = null;
                continue;
            }

            fingeringEvents.Add(fingeringEvent);

            if (!string.IsNullOrEmpty(fingeringEvent.Color)
                || !string.IsNullOrEmpty(fingeringEvent.FontSize))
            {
                needMarkup = true;
            }

            if (fingeringEvent.Substitution)
            {
                haveSubstitution = true;
            }
        }

        if (fingeringEvents.Count == 0)
        {
            return;
        }

        List<string> fingerings = new List<string>();

        if (fingeringEvents.Count > 1 && pluck)
        {
            needMarkup = true;
        }

        if (needMarkup)
        {
            if (haveSubstitution && fingeringEvents.Count == 1)
            {
                fingerings.Add("\" \"");
            }

            foreach (LilyFingeringEvent fingeringEvent in fingeringEvents)
            {
                List<string> fingering = new List<string>();

                string color = LilyMarkup.ColorToLy(fingeringEvent.Color);
                if (color != null)
                {
                    fingering.Add("\\with-color " + color);
                }

                string fontSize = LilyMarkup.GetFontSize(State, fingeringEvent.FontSize, false);
                if (fontSize != null)
                {
                    fingering.Add("\\normalsize \\fontsize " + fontSize);
                }

                string text = fingeringEvent.Type;
                fingeringEvent.Type = null;
                if (fingeringEvent.Alternate)
                {
                    text = "(" + text + ")";
                }

                fingering.Add(MusicXmlUtilities.EscapeLyOutputString(text));

                fingerings.Add(string.Join(" ", fingering));
            }

            if (haveSubstitution)
            {
                string start = string.Join(" ", fingerings.Take(fingerings.Count - 2));
                string left = fingerings[fingerings.Count - 2];
                string right = fingerings[fingerings.Count - 1];
                fingeringEvents[0].Type =
                    "\\substFinger \\markup \\concat { " + start + " } "
                    + "\\markup " + left + " \\markup " + right;
            }
            else if (pluck)
            {
                fingeringEvents[0].Type =
                    "\\RH \\markup \\concat { "
                    + string.Join(" \\char ##x200A ", fingerings) + " }";
            }
            else
            {
                fingeringEvents[0].Type =
                    "\\finger \\markup \\concat { " + string.Join(" ", fingerings) + " }";
            }
        }
        else
        {
            if (haveSubstitution && fingeringEvents.Count == 1)
            {
                fingerings.Add(" ");
            }

            foreach (LilyFingeringEvent fingeringEvent in fingeringEvents)
            {
                string fingering = fingeringEvent.Type;
                fingeringEvent.Type = null;

                fingerings.Add(fingeringEvent.Alternate ? "(" + fingering + ")" : fingering);
            }

            if (haveSubstitution)
            {
                string start = MusicXmlUtilities.EscapeLyOutputString(
                    string.Concat(fingerings.Take(fingerings.Count - 2)));
                string left = MusicXmlUtilities.EscapeLyOutputString(
                    fingerings[fingerings.Count - 2]);
                string right = MusicXmlUtilities.EscapeLyOutputString(
                    fingerings[fingerings.Count - 1]);
                fingeringEvents[0].Type = "\\substFinger " + start + " " + left + " " + right;
            }
            else
            {
                string text = string.Concat(fingerings);

                if (pluck)
                {
                    fingeringEvents[0].Type = "\\RH \"" + text + "\"";
                    return;
                }

                //In the construction `<note>-<fingering>' the fingering is handled as an
                //unsigned integer, with leading zeroes stripped off, which we don't want.
                if (IsDigit(text))
                {
                    fingeringEvents[0].Type = text.Length > 1 && text[0] == '0'
                        ? "\\finger \"" + text + "\""
                        : text;
                }
                else
                {
                    fingeringEvents[0].Type =
                        "\\finger " + MusicXmlUtilities.EscapeLyOutputString(text);
                }
            }
        }
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        List<LilyStaffChange> staffChanges = Elements.OfType<LilyStaffChange>().ToList();

        List<LilyNoteEvent> noteEvents = Elements.OfType<LilyNoteEvent>().ToList();

        List<LilyRhythmicEvent> restEvents = Elements
            .OfType<LilyRhythmicEvent>()
            .Where(e => !(e is LilyNoteEvent))
            .ToList();

        List<LilyDelayedTurnEvent> delayedTurnEvents
            = Elements.OfType<LilyDelayedTurnEvent>().ToList();

        List<LilyMusic> otherEvents = Elements
            .Where(e => !(e is LilyRhythmicEvent || e is LilyStaffChange
                          || e is LilyDelayedTurnEvent))
            .ToList();

        List<LilyHarmonicNoteEvent> harmonicNoteEvents
            = noteEvents.OfType<LilyHarmonicNoteEvent>().ToList();

        CollectToeHeels(otherEvents, -1);
        CollectToeHeels(otherEvents, 1);

        foreach (LilyNoteEvent noteEvent in noteEvents)
        {
            CollectFingerings(noteEvent, -1, false);
            CollectFingerings(noteEvent, -1, true);
            CollectFingerings(noteEvent, 1, false);
            CollectFingerings(noteEvent, 1, true);
        }

        //Depending on the `<harmonic>' elements in a chord we provide default renderings
        //in case no attributes are set that change the appearance of note heads.
        //
        //We support the following combinations of harmonic-specific elements (for
        //everything else we simply use `\flageolet' symbols).
        //
        //  harmonic  natural   base-pitch touching-pitch sounding-pitch
        //  -----------------------------------------------------------------
        //     x         x          x            x                        [1]
        //     x         x                       x                        [2]
        //     x         x                       x               x        [3]
        //     x         x          x            x               x        [4]
        //
        //  harmonic artificial base-pitch touching-pitch sounding-pitch
        //  -----------------------------------------------------------------
        //     x         x          x            x                        [5]
        //     x         x          x            x               x        [4]
        //
        //[1] small black note in parentheses (base pitch)
        //    & hollow, diamond-shaped note (touching pitch)
        //[2] hollow, diamond-shaped note
        //[3] small hollow, diamond-shaped note in parentheses (touching pitch)
        //    & normal note with ring (sounding pitch)
        //[4] small black note (base pitch)
        //    & small hollow, diamond-shaped note (touching pitch)
        //    & normal note with ring (sounding pitch),
        //    & base and touching pitch are enclosed by a single pair of parentheses
        //[5] normal note (base pitch)
        //    & hollow, diamond-shaped note (touching pitch)
        //
        //Note that this algorithm fails if there are two or more harmonic combinations
        //in a single chord. Consequently, we don't consider such combinations.
        LilyHarmonicNoteEvent baseNote
            = harmonicNoteEvents.FirstOrDefault(e => e.HarmonicType == "base-pitch");
        if (baseNote != null
            && (baseNote.HarmonicVisible != true || NoteheadIsChanged(baseNote)))
        {
            baseNote = null;
        }

        LilyHarmonicNoteEvent touch
            = harmonicNoteEvents.FirstOrDefault(e => e.HarmonicType == "touching-pitch");
        if (touch != null && (touch.HarmonicVisible != true || NoteheadIsChanged(touch)))
        {
            touch = null;
        }

        LilyHarmonicNoteEvent sound
            = harmonicNoteEvents.FirstOrDefault(e => e.HarmonicType == "sounding-pitch");
        if (sound != null && (sound.HarmonicVisible != true || NoteheadIsChanged(sound)))
        {
            sound = null;
        }

        //`HarmonicVisible' is tested in the harmonic note's own post-note text to decide
        //whether to draw a flageolet symbol.
        if (noteEvents.Count == 3 && baseNote != null && touch != null && sound != null)
        {
            //Case 4.
            baseNote.HarmonicSteps = touch.Pitch.Steps() - baseNote.Pitch.Steps();
            baseNote.SetHarmonicTypes(new List<string> { "small" });
            baseNote.HarmonicVisible = null;
            touch.SetHarmonicTypes(new List<string> { "small", "diamond" });
            touch.HarmonicVisible = null;
        }
        else if (noteEvents.Count == 2)
        {
            if (baseNote != null)
            {
                if (baseNote.Harmonic == "artificial" && touch != null && sound == null)
                {
                    //Case 5.
                    baseNote.HarmonicVisible = null;
                    touch.SetHarmonicTypes(new List<string> { "diamond" });
                    touch.HarmonicVisible = null;
                }
                else if (baseNote.Harmonic == "natural" && touch != null && sound == null)
                {
                    //Case 1.
                    baseNote.SetHarmonicTypes(new List<string> { "small", "parentheses" });
                    baseNote.HarmonicVisible = null;
                    touch.SetHarmonicTypes(new List<string> { "diamond" });
                    touch.HarmonicVisible = null;
                }
            }
            else if (touch != null && touch.Harmonic == "natural" && sound != null)
            {
                //Case 3.
                touch.SetHarmonicTypes(
                    new List<string> { "small", "diamond", "parentheses" });
                touch.HarmonicVisible = null;
            }
        }
        else if (noteEvents.Count == 1 && touch != null && touch.Harmonic == "natural")
        {
            //Case 2.
            touch.SetHarmonicTypes(new List<string> { "diamond" });
            touch.HarmonicVisible = null;
        }

        //All preparations are done; we can now start with printing.

        if (AfterGraceElements != null)
        {
            printer.Dump("\\afterGrace {");
        }

        if (GraceElements != null && Elements.Count > 0)
        {
            //TODO (upstream's): Support slashed grace beams.
            printer.Dump(!string.IsNullOrEmpty(GraceType) ? "\\slashedGrace" : "\\grace");
            //Don't print a newline after a braced grace group.
            GraceElements.PrintLy(printer, false);
        }
        else if (GraceElements != null)
        {
            //No elements!
            State.Warning("Grace note with no following music: " + GraceElements);
            printer.Dump(
                !string.IsNullOrEmpty(GraceType) ? "\\" + GraceType : "\\grace");
            GraceElements.PrintLy(printer, false);
            printer.Dump("{}");
        }

        if (staffChanges.Count > 0)
        {
            staffChanges[0].PrintLy(printer);
        }

        foreach ((LilyMusic element, PythonFraction offset) in OffsetElements)
        {
            LilyDuration duration = LilyDuration.FromFraction(State, offset);
            //Also take care of tuplet timing.
            printer.Dump(
                "\\after " + duration.LyExpression(duration.Factor / printer.DurationFactor())
                + " ");
            //Temporarily reset the offset so that the event's normal printing routine
            //kicks in.
            ILilyOffsetEvent offsetEvent = (ILilyOffsetEvent)element;
            PythonFraction originalOffset = offsetEvent.Offset;
            offsetEvent.Offset = PythonFraction.Zero;
            element.PrintLy(printer);
            offsetEvent.Offset = originalOffset;
        }

        foreach (LilyDelayedTurnEvent delayedTurn in delayedTurnEvents)
        {
            delayedTurn.PrintLy(printer);
        }

        //Print all overrides and other settings for articulations or ornaments that need
        //to be inserted before the chord.
        foreach (LilyMusic other in otherEvents)
        {
            if (other is LilyEvent otherEvent)
            {
                otherEvent.PrintBeforeNote(printer);
            }
        }

        if (restEvents.Count > 0)
        {
            restEvents[0].PrintLy(printer);
        }
        else if (noteEvents.Count == 1 && string.IsNullOrEmpty(ArpeggioType))
        {
            //We don't print an arpeggio line or bracket for a single note if not part of
            //a cross-staff or cross-voice arpeggio.
            noteEvents[0].PrintLy(printer);
        }
        else if (noteEvents.Count > 0)
        {
            List<string> pitches = new List<string>();
            LilyPitch basePitch = null;
            LilyStemEvent stem = null;
            foreach (LilyNoteEvent noteEvent in noteEvents)
            {
                foreach (LilyExpression associated in noteEvent.AssociatedEvents)
                {
                    if (associated is LilyStemEvent stemEvent
                        && !string.IsNullOrEmpty(stemEvent.Value))
                    {
                        stem = stemEvent;
                    }
                }

                pitches.Add(noteEvent.ChordElementLy());
                if (basePitch == null)
                {
                    basePitch = State.PreviousPitch;
                }
            }

            if (stem != null)
            {
                printer.Dump(stem.LyExpression());
            }

            ArpeggioPreChord(printer);

            printer.Dump("<" + string.Join(" ", pitches) + ">");
            State.PreviousPitch = basePitch;
            LilyDuration duration = GetDuration();
            if (duration != null)
            {
                duration.PrintLy(printer);
            }

            ArpeggioPostChord(printer);
        }

        foreach (LilyMusic other in otherEvents)
        {
            other.PrintLy(printer);
        }

        foreach (LilyMusic other in otherEvents)
        {
            if (other is LilyEvent otherEvent)
            {
                otherEvent.PrintAfterNote(printer);
            }
        }

        if (AfterGraceElements != null)
        {
            printer.Dump("}");
            AfterGraceElements.PrintLy(printer, false);
        }

        PrintComment(printer);
    }
}

/// <summary>A chord that is arpeggiated or explicitly not arpeggiated.</summary>
internal sealed class LilyArpeggioChordEvent : LilyChordEvent
{
    /// <summary>Builds the arpeggiated chord from the plain one it replaces.</summary>
    /// <param name="chordEvent">The chord this one is built from.</param>
    /// <remarks>
    /// ⚠ Upstream provides no constructor here: <c>musicxml2ly.py</c> does not create an
    /// <c>ArpeggioChordEvent</c> but converts a <c>ChordEvent</c> by REBINDING its
    /// <c>__class__</c> and then calling <c>init()</c> by hand — upstream even documents
    /// why. C# has no such move, so the arpeggiated chord is built FROM the chord, once,
    /// carrying everything it had, and the caller goes on filling this object. Recorded
    /// in PORT-COVERAGE.
    /// </remarks>
    internal LilyArpeggioChordEvent(LilyChordEvent chordEvent)
        : base(chordEvent.State)
    {
        AfterGraceElements = chordEvent.AfterGraceElements;
        GraceElements = chordEvent.GraceElements;
        GraceType = chordEvent.GraceType;
        When = chordEvent.When;
        ArpeggioType = chordEvent.ArpeggioType;
        Comment = chordEvent.Comment;
        Identifier = chordEvent.Identifier;
        Color = chordEvent.Color;
        FontSize = chordEvent.FontSize;
        Parent = chordEvent.Parent;
        Start = chordEvent.Start;
        foreach ((LilyMusic Element, PythonFraction Offset) offsetElement
                 in chordEvent.OffsetElements)
        {
            OffsetElements.Add(offsetElement);
        }

        Elements = chordEvent.Elements;
        foreach (LilyMusic element in Elements)
        {
            if (element.Parent == chordEvent)
            {
                element.Parent = this;
            }
        }
    }

    /// <summary>Which kind of arpeggio the document asked for.</summary>
    internal string Arpeggio { get; set; }

    /// <summary>Which way the arrow points.</summary>
    internal string ArpeggioDir { get; set; }

    /// <summary>The colour the arpeggio is drawn in.</summary>
    internal string ArpeggioColor { get; set; }

    /// <summary>The lowest pitch the whole arpeggio reaches.</summary>
    internal double ArpeggioMinPitch { get; set; } = 1000;

    /// <summary>The highest pitch the whole arpeggio reaches.</summary>
    internal double ArpeggioMaxPitch { get; set; } = -1000;

    /// <summary>Writes how far the arpeggio reaches past this chord.</summary>
    /// <param name="printer">Where to write.</param>
    internal void PositionOffset(LilyOutputPrinter printer)
    {
        double minPitch = 1000;
        double maxPitch = -1000;
        foreach (LilyMusic element in Elements)
        {
            if (element is LilyNoteEvent noteEvent)
            {
                minPitch = Math.Min(minPitch, noteEvent.Pitch.Steps());
                maxPitch = Math.Max(maxPitch, noteEvent.Pitch.Steps());
            }
        }

        double minOffset = ArpeggioMinPitch - minPitch;
        double maxOffset = ArpeggioMaxPitch - maxPitch;
        if (minOffset != 0 || maxOffset != 0)
        {
            printer.Dump(
                "\\offset positions #'("
                + LilyOutputPrinter.FormatDouble(minOffset / 2) + " . "
                + LilyOutputPrinter.FormatDouble(maxOffset / 2) + ")");
        }
    }

    /// <inheritdoc/>
    internal override void ArpeggioPreChord(LilyOutputPrinter printer)
    {
        //For shorter command names.
        string cross = ArpeggioType switch
        {
            "PianoStaff" => "XX",
            "Staff" => "X",
            _ => string.Empty,
        };

        if (cross.Length > 0)
        {
            printer.Dump("\\arpeggio" + cross);
        }

        if (Arpeggio == "non-arpeggiate")
        {
            printer.Dump("\\arpeggioBracket" + cross);
        }
        else if (Arpeggio == "arpeggiate")
        {
            string direction = ArpeggioDir switch
            {
                "down" => "\\arpeggioArrowDown",
                "up" => "\\arpeggioArrowUp",
                _ => string.Empty,
            };
            if (direction.Length > 0)
            {
                printer.Dump(direction + cross);
            }
        }
    }

    /// <inheritdoc/>
    internal override void ArpeggioPostChord(LilyOutputPrinter printer)
    {
        if (Arpeggio != null)
        {
            string color = LilyMarkup.ColorToLy(ArpeggioColor);
            if (color != null)
            {
                printer.Dump("\\tweak color " + color);
            }

            PositionOffset(printer);

            printer.Dump("\\arpeggio");

            if (Arpeggio == "non-arpeggiate" || ArpeggioDir != null)
            {
                printer.Dump("\\arpeggioNormal");
            }
        }
    }
}

/// <summary>A pickup measure's length.</summary>
internal sealed class LilyPartial : LilyMusic
{
    /// <summary>Builds the pickup.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyPartial(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How long the pickup is.</summary>
    internal LilyDuration PartialDuration { get; set; }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (PartialDuration != null)
        {
            printer.Dump("\\partial " + PartialDuration.LyExpression());
        }
    }
}

/// <summary>A measure whose length the document sets by hand.</summary>
internal sealed class LilyMeasureLengthEvent : LilyMusic
{
    /// <summary>Builds the setting.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    /// <param name="length">The length, or a value at or below zero to reset it.</param>
    internal LilyMeasureLengthEvent(MusicXmlImportState state, PythonFraction length)
        : base(state)
        => Length = length;

    /// <summary>The length this measure is given.</summary>
    internal PythonFraction Length { get; }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
        => printer.Dump(
            Length > PythonFraction.Zero
                ? "\\measureLength #" + Length
                : "\\measureLengthReset");
}

/// <summary>A bar line.</summary>
internal sealed class LilyBarLine : LilyMusic
{
    private static readonly Dictionary<string, string> BarSymbols
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "dashed", "!" },
            { "dotted", ";" },
            { "heavy", "." },
            { "heavy-heavy", ".." },
            { "heavy-light", ".|" },
            { "light-heavy", "|." },
            { "light-light", "||" },
            { "none", string.Empty },
            { "regular", "|" },
            { "short", "," },
            { "tick", "'" },
            { "dots-heavy-heavy-dots", ":..:" },
        };

    /// <summary>Builds the bar line.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyBarLine(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The bar number of the bar to the right.</summary>
    internal int BarNumber { get; set; }

    /// <summary>Which bar line the document asked for.</summary>
    /// <remarks>
    /// ⚠ NAMING (rule 6). Upstream calls it <c>type</c>; the port says what kind of
    /// thing the value names, since a bar line has several other 'types' in its reach.
    /// </remarks>
    internal string BarType { get; set; }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        string barSymbol = BarType != null && BarSymbols.TryGetValue(BarType, out string mapped)
            ? mapped
            : null;

        List<string> val = new List<string>();
        if (barSymbol == null)
        {
            //This must be emitted before setting the color.
            val.Add("|");
        }

        if (Color != null)
        {
            //We can't use `\tweak' here.
            val.Add("\\once \\override Staff.BarLine.color = " + LilyMarkup.ColorToLy(Color));
        }

        if (barSymbol == ":..:")
        {
            val.Add("\\once \\set Score.doubleRepeatBarType = \"" + barSymbol + "\"");
        }
        else if (barSymbol != null)
        {
            val.Add("\\bar \"" + barSymbol + "\"");
        }

        foreach (string text in val)
        {
            printer.Dump(text);
        }

        //Emit a comment indicating the bar number to the left.
        if (BarNumber > 1)
        {
            printer.PrintVerbatim(
                " % " + (BarNumber - 1).ToString(CultureInfo.InvariantCulture));
            if (BarNumber % 10 == 0)
            {
                printer.PrintVerbatim("\n");
                printer.Newline();
                printer.Dump(
                    "\\barNumberCheck #" + BarNumber.ToString(CultureInfo.InvariantCulture));
            }
        }

        printer.Newline();
    }

    /// <inheritdoc/>
    internal override string LyExpression() => " | ";
}

/// <summary>The marker that pushes an event to the bar line.</summary>
internal sealed class LilyForBarline : LilyMusic
{
    /// <summary>Builds the marker.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyForBarline(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer) => printer.Dump("\\forBarLine");
}
