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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (RhythmicEvent, RestEvent, SkipEvent, NoteEvent and HarmonicNoteEvent);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>An event that occupies time — a note, a rest or a skip.</summary>
internal class LilyRhythmicEvent : LilyEvent
{
    /// <summary>Builds the event.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyRhythmicEvent(MusicXmlImportState state)
        : base(state)
        => Duration = new LilyDuration(state);

    /// <summary>How long this event lasts.</summary>
    internal LilyDuration Duration { get; set; }

    /// <summary>The colour the augmentation dots are drawn in.</summary>
    internal string DotColor { get; set; }

    /// <summary>The size the augmentation dots are drawn at.</summary>
    internal string DotFontSize { get; set; }

    /// <summary>Whether the note or rest is drawn at all.</summary>
    /// <remarks>
    /// ⚠ Upstream declares <c>visible</c> on <c>RestEvent</c> and on <c>NoteEvent</c>
    /// but not on their shared base, and <c>SkipEvent</c> — the third subclass — never
    /// reads it. The two declarations are identical, so the port carries one.
    /// </remarks>
    internal bool Visible { get; set; } = true;

    /// <summary>Whether the note or rest takes up horizontal space.</summary>
    /// <remarks>See the remark on <see cref="Visible"/>: one field for two identical
    /// declarations.</remarks>
    internal bool Spacing { get; set; } = true;

    /// <summary>Whether a ledger line is drawn.</summary>
    /// <remarks>Upstream declares this on <c>NoteEvent</c> alone.</remarks>
    internal bool Ledger { get; set; } = true;

    /// <summary>Whether an augmentation dot is drawn.</summary>
    /// <remarks>
    /// See the remark on <see cref="Visible"/>: one field for two identical declarations.
    /// ⚠ NAMING (rule 6). Upstream calls it <c>dot</c>; the port says what the flag
    /// decides, since a rhythmic event also counts its dots.
    /// </remarks>
    internal bool PrintDot { get; set; } = true;

    /// <summary>
    /// The events tightly connected with this note or rest — stems, notehead styles,
    /// fingerings, ties and function wrappers.
    /// </summary>
    /// <remarks>
    /// Upstream's comment names the contract: each such class provides
    /// <c>pre_chord_ly</c>, <c>pre_note_ly</c> and <c>post_note_ly</c>, and asking one
    /// that does not raises <c>AttributeError</c>. The port's list holds expressions and
    /// casts to <see cref="ILilyAssociatedEvent"/>, so the cast is that AttributeError.
    /// </remarks>
    internal List<LilyExpression> AssociatedEvents { get; } = new List<LilyExpression>();

    /// <summary>Records one associated event.</summary>
    /// <param name="associatedEvent">The event.</param>
    internal void AddAssociatedEvent(LilyExpression associatedEvent)
    {
        if (associatedEvent != null)
        {
            AssociatedEvents.Add(associatedEvent);
        }
    }

    /// <summary>What every associated event puts before the chord.</summary>
    /// <returns>The texts.</returns>
    internal virtual List<string> PreChordLy()
        => AssociatedEvents
            .Select(e => ((ILilyAssociatedEvent)e).PreChordLy())
            .ToList();

    /// <summary>What every associated event puts before the note.</summary>
    /// <param name="isChordElement">Whether the note sits inside a chord.</param>
    /// <returns>The texts.</returns>
    internal virtual List<string> PreNoteLy(bool isChordElement)
        => AssociatedEvents
            .Select(e => ((ILilyAssociatedEvent)e).PreNoteLy(isChordElement))
            .ToList();

    /// <summary>What every associated event puts after the note.</summary>
    /// <param name="isChordElement">Whether the note sits inside a chord.</param>
    /// <returns>The texts.</returns>
    internal virtual List<string> PostNoteLy(bool isChordElement)
        => AssociatedEvents
            .Select(e => ((ILilyAssociatedEvent)e).PostNoteLy(isChordElement))
            .ToList();

    /// <summary>Everything the associated events put before the chord, joined.</summary>
    /// <returns>The text.</returns>
    internal string LyExpressionPreChord()
        => string.Join(" ", PreChordLy().Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Everything the associated events put before the note, joined.</summary>
    /// <param name="isChordElement">Whether the note sits inside a chord.</param>
    /// <returns>The text.</returns>
    internal string LyExpressionPreNote(bool isChordElement)
        => string.Join(" ", PreNoteLy(isChordElement).Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Everything the associated events put after the note, joined.</summary>
    /// <param name="isChordElement">Whether the note sits inside a chord.</param>
    /// <returns>The text.</returns>
    internal string LyExpressionPostNote(bool isChordElement)
        => string.Join(" ", PostNoteLy(isChordElement).Where(s => !string.IsNullOrEmpty(s)));

    /// <inheritdoc/>
    internal override PythonFraction GetLength(bool withFactor = true)
        => Duration.GetLength(withFactor);

    /// <summary>What this event contributes to a <c>make-music</c> expression.</summary>
    /// <returns>The properties.</returns>
    internal virtual string GetProperties() => "'duration " + Duration.LispExpression();
}

/// <summary>A rest.</summary>
internal sealed class LilyRestEvent : LilyRhythmicEvent
{
    /// <summary>Builds the rest.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyRestEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Where the rest sits, when the document places it.</summary>
    internal LilyPitch Pitch { get; set; }

    /// <summary>How far a pitched full-measure rest is moved vertically.</summary>
    internal double YOffset { get; set; }

    /// <summary>Whether the rest is drawn with a full-measure glyph.</summary>
    internal bool FullMeasureGlyph { get; set; }

    /// <inheritdoc/>
    internal override List<string> PreNoteLy(bool isChordElement)
    {
        List<string> elements = base.PreNoteLy(isChordElement);

        if (!Visible)
        {
            elements.Add("\\hideNote");
        }
        else
        {
            //We don't support the case
            //
            //  print-object="no" print-dot="yes"    (show only dots)
            //
            //since its practical use is questionable. Additionally, no other major
            //application handles this.
            if (!PrintDot)
            {
                elements.Add("\\tweak Dots.stencil ##f");
            }

            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                elements.Add("\\tweak color " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
            if (fontSize != null)
            {
                //See issue #6721 why we currently need this work-around.
                if (Duration.Dots != 0)
                {
                    elements.Insert(0, "\\once \\override Rest.font-size = " + fontSize);
                }
                else
                {
                    elements.Add("\\tweak font-size " + fontSize);
                }
            }

            string dotColor = LilyMarkup.ColorToLy(DotColor);
            if (dotColor != null)
            {
                elements.Add("\\tweak Dots.color " + dotColor);
            }

            string dotFontSize = LilyMarkup.GetFontSize(State, DotFontSize, false);
            if (dotFontSize != null)
            {
                //See issue #6721 why we currently need this work-around.
                //  res.append(r'\tweak Dots.font-size %s' % dot_font_size)
                //
                //⚠ AN UPSTREAM DEFECT, REPRODUCED (D64(a): a defect is proved by
                //MEASUREMENT against the oracle, never by reading the python). Upstream
                //writes the NOTE's font size here, not the DOT's — and writes the text
                //`None' when the note carries none. Recorded as a candidate in
                //tools/musicxml2lyprobe/DIVERGENCES.txt; the fix goes on top of a green
                //parity baseline, not into the port that establishes it.
                elements.Insert(
                    0, "\\once \\override Dots.font-size = " + (fontSize ?? "None"));
            }

            if (YOffset != 0)
            {
                elements.Add(
                    "\\tweak Y-offset #" + LilyOutputPrinter.FormatDouble(YOffset));
            }
        }

        return elements;
    }

    /// <inheritdoc/>
    internal override List<string> PostNoteLy(bool isChordElement)
        => base.PostNoteLy(isChordElement);

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (Duration == null)
        {
            return;
        }

        printer.Dump(LyExpressionPreChord());
        printer.Dump(LyExpressionPreNote(true));

        if (Pitch != null)
        {
            Pitch.PrintLy(printer);
            Duration.PrintLy(printer);
            printer.PrintVerbatim("\\rest");
        }
        else
        {
            printer.Dump(FullMeasureGlyph ? "R" : "r");
            Duration.PrintLy(printer);
        }

        printer.Dump(LyExpressionPostNote(true));
    }
}

/// <summary>A skip — time that passes with nothing drawn.</summary>
internal sealed class LilySkipEvent : LilyRhythmicEvent
{
    /// <summary>Builds the skip.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilySkipEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The grace-note skip that precedes this one, when there is one.</summary>
    internal LilyDuration GraceSkip { get; set; }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();

        if (GraceSkip != null)
        {
            result.Add("\\grace { s" + GraceSkip.LyExpression() + " }");
        }

        result.Add("s" + Duration.LyExpression());

        return string.Join(" ", result);
    }
}

/// <summary>A note.</summary>
internal class LilyNoteEvent : LilyRhythmicEvent
{
    /// <summary>Builds the note.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyNoteEvent(MusicXmlImportState state)
        : base(state)
        => Pitch = new LilyPitch(state);

    /// <summary>The pitch that sounds.</summary>
    internal LilyPitch Pitch { get; set; }

    /// <summary>Whether the accidental is a cautionary one.</summary>
    internal bool Cautionary { get; set; }

    /// <summary>Whether the accidental is an editorial one.</summary>
    internal bool Editorial { get; set; }

    /// <summary>Whether the accidental is forced.</summary>
    internal bool ForcedAccidental { get; set; }

    /// <summary>Which accidental the document named.</summary>
    internal string AccidentalValue { get; set; }

    /// <summary>The colour the accidental is drawn in.</summary>
    internal string AccidentalColor { get; set; }

    /// <summary>The size the accidental is drawn at.</summary>
    internal string AccidentalFontSize { get; set; }

    /// <inheritdoc/>
    internal override string GetProperties()
    {
        string result = base.GetProperties();

        if (Pitch != null)
        {
            result += Pitch.LispExpression();
        }

        return result;
    }

    /// <summary>The marks that force or query the accidental.</summary>
    /// <returns>The marks.</returns>
    internal string PitchMods()
    {
        string exclQuestion = string.Empty;
        if (Cautionary || Editorial)
        {
            exclQuestion += "?";
        }

        if (ForcedAccidental)
        {
            exclQuestion += "!";
        }

        return exclQuestion;
    }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        if (Pitch == null)
        {
            return null;
        }

        //Obtain all stuff that needs to be printed before and after the note.
        List<string> result = new List<string>();
        result.Add(LyExpressionPreNote(true));
        result.Add(Pitch.LyExpression() + PitchMods() + Duration.LyExpression());
        result.Add(LyExpressionPostNote(true));
        return string.Join(" ", result.Where(s => !string.IsNullOrEmpty(s)));
    }

    /// <summary>This note as one element of a chord.</summary>
    /// <returns>The text.</returns>
    internal string ChordElementLy()
    {
        if (Pitch == null)
        {
            return null;
        }

        //Obtain all stuff that needs to be printed before and after the note.
        List<string> result = new List<string>();
        result.Add(LyExpressionPreNote(true));
        result.Add(Pitch.LyExpression() + PitchMods());
        result.Add(LyExpressionPostNote(true));
        return string.Join(" ", result.Where(s => !string.IsNullOrEmpty(s)));
    }

    /// <summary>What a harmonic note adds before the note; nothing for a plain one.</summary>
    /// <param name="elements">What has been collected so far.</param>
    internal virtual void HarmonicPreNoteLy(List<string> elements)
    {
    }

    /// <summary>What a harmonic note adds after the note; nothing for a plain one.</summary>
    /// <param name="elements">What has been collected so far.</param>
    internal virtual void HarmonicPostNoteLy(List<string> elements)
    {
    }

    /// <inheritdoc/>
    internal override List<string> PreNoteLy(bool isChordElement)
    {
        List<string> elements = base.PreNoteLy(isChordElement);
        if (Editorial)
        {
            //We don't support both `editorial' and `cautionary' at the same time, letting
            //the former win.
            elements.Add("\\bracketAcc");
        }

        if (!Visible)
        {
            elements.Add("\\hideNote");
        }
        else
        {
            //We don't support the cases
            //
            //  print-object="no" print-leger="yes"  (show only ledger lines)
            //  print-object="no" print-dot="yes"    (show only dots)
            //
            //since their practical use is questionable. Additionally, no other major
            //application handles this.
            if (!Ledger)
            {
                elements.Add("\\tweak no-ledgers ##t");
            }

            if (!PrintDot)
            {
                elements.Add("\\tweak Dots.stencil ##f");
            }

            string accidentalColor = LilyMarkup.ColorToLy(AccidentalColor);
            if (accidentalColor != null)
            {
                elements.Add("\\tweak Accidental.color " + accidentalColor);
            }

            string accidentalFontSize = LilyMarkup.GetFontSize(
                State, AccidentalFontSize, false);
            if (accidentalFontSize != null)
            {
                elements.Add("\\tweak Accidental.font-size " + accidentalFontSize);
            }

            string dotColor = LilyMarkup.ColorToLy(DotColor);
            if (dotColor != null)
            {
                elements.Add("\\tweak Dots.color " + dotColor);
            }

            string dotFontSize = LilyMarkup.GetFontSize(State, DotFontSize, false);
            if (dotFontSize != null)
            {
                elements.Add("\\tweak Dots.font-size " + dotFontSize);
            }

            if (Duration.DurationLog == -3)
            {
                //`\maxima' doesn't work with normal note heads.
                elements.Add("\\tweak style #'baroque");
            }

            HarmonicPreNoteLy(elements);
        }

        return elements;
    }

    /// <inheritdoc/>
    internal override List<string> PostNoteLy(bool isChordElement)
    {
        List<string> elements = base.PostNoteLy(isChordElement);

        HarmonicPostNoteLy(elements);

        return elements;
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (Spacing)
        {
            LilyPitch pitch = Pitch;
            if (pitch != null)
            {
                printer.Dump(LyExpressionPreChord());
                printer.Dump(LyExpressionPreNote(true));
                pitch.PrintLy(printer, PitchMods());
            }
        }
        else
        {
            //We completely ignore objects without spacing; their purpose is not
            //typesetting but providing better MIDI support.
            printer.Dump("s");
        }

        Duration.PrintLy(printer);
        printer.Dump(LyExpressionPostNote(true));
    }
}

/// <summary>A note played as a harmonic.</summary>
internal sealed class LilyHarmonicNoteEvent : LilyNoteEvent
{
    /// <summary>Builds the harmonic note from the plain one it replaces.</summary>
    /// <param name="noteEvent">The note this one is built from.</param>
    /// <remarks>
    /// ⚠ Upstream provides no constructor here: in <c>musicxml.py</c> it does not create
    /// a <c>HarmonicNoteEvent</c> but converts a <c>NoteEvent</c> by REBINDING its
    /// <c>__class__</c> and then calling <c>init()</c> by hand. C# has no such move, so
    /// the harmonic note is built FROM the note event, once, carrying everything the
    /// note had; the caller then goes on filling this object instead. Recorded in
    /// PORT-COVERAGE.
    /// </remarks>
    internal LilyHarmonicNoteEvent(LilyNoteEvent noteEvent)
        : base(noteEvent.State)
    {
        Pitch = noteEvent.Pitch;
        Duration = noteEvent.Duration;
        DotColor = noteEvent.DotColor;
        DotFontSize = noteEvent.DotFontSize;
        Cautionary = noteEvent.Cautionary;
        Editorial = noteEvent.Editorial;
        ForcedAccidental = noteEvent.ForcedAccidental;
        AccidentalValue = noteEvent.AccidentalValue;
        AccidentalColor = noteEvent.AccidentalColor;
        AccidentalFontSize = noteEvent.AccidentalFontSize;
        Visible = noteEvent.Visible;
        Spacing = noteEvent.Spacing;
        Ledger = noteEvent.Ledger;
        PrintDot = noteEvent.PrintDot;
        Color = noteEvent.Color;
        FontSize = noteEvent.FontSize;
        Comment = noteEvent.Comment;
        Identifier = noteEvent.Identifier;
        Parent = noteEvent.Parent;
        Start = noteEvent.Start;
        BeforeNote = noteEvent.BeforeNote;
        AfterNote = noteEvent.AfterNote;
        foreach (LilyExpression associated in noteEvent.AssociatedEvents)
        {
            AssociatedEvents.Add(associated);
        }
    }

    /// <summary>Which kind of harmonic the document named.</summary>
    internal string Harmonic { get; set; }

    /// <summary>Which of the harmonic's pitches this note is.</summary>
    /// <remarks>
    /// The document's own reading is one of the three pitch names; the chord printer
    /// replaces it with the list of note-head treatments it decided on.
    /// </remarks>
    internal string HarmonicType { get; set; }

    /// <summary>Records the note-head treatments, replacing the pitch name.</summary>
    /// <param name="types">The treatments.</param>
    /// <remarks>
    /// Upstream ASSIGNS the list over the same attribute the pitch name was in, so the
    /// name is gone afterwards; the port drops it here for the same reason.
    /// </remarks>
    internal void SetHarmonicTypes(List<string> types)
    {
        HarmonicType = null;
        HarmonicTypes = types;
    }

    /// <summary>The note-head treatments the chord printer decided on.</summary>
    /// <remarks>
    /// ⚠ Upstream stores EITHER a pitch name OR a list of treatments in one attribute,
    /// and asks <c>isinstance(self.harmonic_type, list)</c> to tell them apart. C#
    /// cannot, so the list is its own member and the <c>isinstance</c> test is
    /// "this member is not null".
    /// </remarks>
    internal List<string> HarmonicTypes { get; set; }

    /// <summary>Whether a flageolet symbol is drawn.</summary>
    internal bool? HarmonicVisible { get; set; }

    /// <summary>The colour the harmonic symbol is drawn in.</summary>
    internal string HarmonicColor { get; set; }

    /// <summary>The size the harmonic symbol is drawn at.</summary>
    internal string HarmonicFontSize { get; set; }

    /// <summary>How many steps lie between the base pitch and the touching pitch.</summary>
    internal double HarmonicSteps { get; set; }

    /// <summary>
    /// A heuristic size and a vertical offset for Emmentaler's parentheses to enclose
    /// two notes in a chord with a separation given by the harmonic's steps.
    /// </summary>
    /// <returns>The size and the offset.</returns>
    internal (double Size, double Offset) HarmonicParenthesesTweaks()
        => (-3.8 + (System.Math.Abs(HarmonicSteps) * 1.4), HarmonicSteps / 4);

    /// <inheritdoc/>
    internal override void HarmonicPreNoteLy(List<string> elements)
    {
        if (HarmonicSteps != 0)
        {
            (double size, double offset) = HarmonicParenthesesTweaks();
            elements.Add(
                "\\harmonicParen " + size.ToString("F1", CultureInfo.InvariantCulture)
                + " " + offset.ToString("F1", CultureInfo.InvariantCulture));
        }

        if (HarmonicTypes != null)
        {
            if (HarmonicTypes.Contains("small"))
            {
                elements.Add("\\harmonicSmall");
            }

            if (HarmonicTypes.Contains("parentheses"))
            {
                elements.Add("\\parenthesize");
            }
        }
    }

    /// <inheritdoc/>
    internal override void HarmonicPostNoteLy(List<string> elements)
    {
        if (HarmonicTypes != null)
        {
            if (HarmonicTypes.Contains("diamond"))
            {
                elements.Add("\\harmonic");
            }
        }
        else if (HarmonicVisible == true)
        {
            //A circular harmonic symbol.
            string harmonicColor = LilyMarkup.ColorToLy(HarmonicColor);
            if (harmonicColor != null)
            {
                elements.Add("\\tweak color " + harmonicColor);
            }

            string harmonicFontSize = LilyMarkup.GetFontSize(State, HarmonicFontSize, false);
            if (harmonicFontSize != null)
            {
                elements.Add("\\tweak font-size " + harmonicFontSize);
            }

            elements.Add("\\flageolet");
        }
    }
}
