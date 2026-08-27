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
using System.Linq;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (Event, SpanEvent and the spanner and direction events below them);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// An expression that can be asked whether it must be attached to a following note.
/// </summary>
/// <remarks>
/// ⚠ Upstream declares <c>wait_for_note</c> on the SEVEN classes that answer it and
/// nowhere else, so asking any other expression raises <c>AttributeError</c>. The port
/// keeps that shape rather than giving <see cref="LilyMusic"/> a default: a default
/// would silently answer for an expression upstream refuses to answer for, which is
/// the "defensive guard the original does not have" defect. A cast that fails here is
/// the AttributeError.
/// </remarks>
internal interface ILilyWaitForNote
{
    /// <summary>Whether this expression has to be written before a note.</summary>
    /// <returns>Whether it waits.</returns>
    bool WaitForNote();
}

/// <summary>
/// An expression that a rhythmic event carries alongside itself — a stem, a notehead
/// style, a fingering, a tie or a function wrapper.
/// </summary>
/// <remarks>
/// Upstream's comment at <c>RhythmicEvent.associated_events</c> names the contract:
/// three functions returning what goes before a chord, right before a note, and right
/// after a note. Only five classes provide them, and upstream raises
/// <c>AttributeError</c> for anything else in the list.
/// </remarks>
internal interface ILilyAssociatedEvent
{
    /// <summary>What to write before the chord this belongs to.</summary>
    /// <returns>The text.</returns>
    string PreChordLy();

    /// <summary>What to write immediately before the note.</summary>
    /// <param name="isChordElement">Whether the note sits inside a chord.</param>
    /// <returns>The text.</returns>
    string PreNoteLy(bool isChordElement);

    /// <summary>What to write immediately after the note.</summary>
    /// <param name="isChordElement">Whether the note sits inside a chord.</param>
    /// <returns>The text.</returns>
    string PostNoteLy(bool isChordElement);
}

/// <summary>An event that a <c>&lt;direction&gt;</c> element may displace in time.</summary>
/// <remarks>
/// Upstream declares <c>offset</c> on each such class and the chord printer sets it to
/// zero and back around the event's own printing, so the port needs one name for that
/// move. A class without it is one upstream would raise <c>AttributeError</c> for.
/// </remarks>
internal interface ILilyOffsetEvent
{
    /// <summary>How far into the measure the event is displaced.</summary>
    PythonFraction Offset { get; set; }
}

/// <summary>An event, which is a music expression attached to a note.</summary>
internal class LilyEvent : LilyMusic
{
    /// <summary>Builds the event.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Text to write before the note this event is attached to.</summary>
    /// <remarks>Ignored for notes themselves.</remarks>
    internal string BeforeNote { get; set; }

    /// <summary>Text to write after the note this event is attached to.</summary>
    internal string AfterNote { get; set; }

    /// <summary>Writes what goes before the note — an override, for instance.</summary>
    /// <param name="printer">Where to write.</param>
    internal virtual void PrintBeforeNote(LilyOutputPrinter printer)
    {
        if (!string.IsNullOrEmpty(BeforeNote))
        {
            printer.Dump(BeforeNote);
        }
    }

    /// <summary>Writes what goes after the note — a reset, for instance.</summary>
    /// <param name="printer">Where to write.</param>
    internal virtual void PrintAfterNote(LilyOutputPrinter printer)
    {
        if (!string.IsNullOrEmpty(AfterNote))
        {
            printer.Dump(AfterNote);
        }
    }
}

/// <summary>An event that spans a stretch of music, written at each end.</summary>
internal class LilySpanEvent : LilyEvent, ILilyWaitForNote
{
    /// <summary>Builds the event.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilySpanEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The element this event came from, for reaching the other end.</summary>
    internal MusicXmlSpanner MxlEvent { get; set; }

    /// <summary>The attributes this event was built with, when it carries its own.</summary>
    internal Dictionary<string, string> MxlAttributes { get; set; }

    /// <summary>Which end of the span this is: -1 to start, 1 to stop.</summary>
    internal int SpanDirection { get; set; }

    /// <summary>How the span's line is drawn.</summary>
    internal string LineType { get; set; } = "solid";

    /// <summary>What kind of span this is — crescendo, octave up and so on.</summary>
    internal int SpanType { get; set; }

    /// <summary>How big the span is, for an octave shift.</summary>
    internal int Size { get; set; }

    /// <summary>Which side of the staff LilyPond's modifier asks for.</summary>
    /// <remarks>
    /// ⚠ Nullable because <c>musicxml_direction_to_lily</c> assigns the direction it read
    /// off the <c>&lt;direction&gt;</c> element — which is <c>None</c> when the element
    /// carries no <c>placement</c> — straight onto a starting spanner. Every reader is a
    /// dictionary lookup that answers its default for <c>None</c>, which is what an unset
    /// value here does.
    /// </remarks>
    internal int? ForceDirection { get; set; }

    /// <summary>Whether the span is drawn at all.</summary>
    internal bool Visible { get; set; } = true;

    /// <summary>Whether this spanner starts and stops on one note.</summary>
    /// <remarks>
    /// ⚠ Upstream declares this on the TEXT spanner alone but assigns it on whatever
    /// spanner it just built, growing the attribute on the others; only the text spanner
    /// ever reads it. The port carries one field on the base, which is the same thing
    /// without the growth.
    /// </remarks>
    internal bool StartStop { get; set; }

    /// <inheritdoc/>
    public virtual bool WaitForNote() => true;

    /// <summary>What this event contributes to a <c>make-music</c> expression.</summary>
    /// <returns>The properties.</returns>
    internal string GetProperties()
        => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "'span-direction {0}", SpanDirection);

    /// <summary>Records what kind of span this is.</summary>
    /// <param name="type">The kind, as the document spells it.</param>
    internal virtual void SetSpanType(string type) => SpanType = ParseSpanType(type);

    /// <summary>Reads a span type the way the base class does.</summary>
    /// <param name="type">The kind, as the document spells it.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// ⚠ Upstream's base <c>set_span_type</c> assigns the STRING it was handed to an
    /// attribute otherwise holding an integer. Only the two subclasses that override
    /// it are ever handed a value the base would have to keep, so the port reads the
    /// integer it can and leaves zero otherwise, which is what every consumer of
    /// <c>span_type</c> compares against.
    /// </remarks>
    private static int ParseSpanType(string type)
        => int.TryParse(type, out int value) ? value : 0;

    /// <summary>The tweak that hides an invisible span.</summary>
    /// <returns>The tweak, or empty when the span is drawn.</returns>
    internal string NotVisible() => Visible ? string.Empty : "\\tweak transparent ##t ";

    /// <summary>Reads an attribute off this event's own element.</summary>
    /// <param name="attribute">The attribute name.</param>
    /// <param name="defaultValue">What to answer when it is absent.</param>
    /// <returns>The value, or the default.</returns>
    internal string GetMxlEventAttribute(string attribute, string defaultValue)
    {
        string result = defaultValue;
        if (MxlAttributes != null)
        {
            result = MxlAttributes.TryGetValue(attribute, out string value)
                ? value
                : defaultValue;
        }
        else if (MxlEvent != null)
        {
            result = MxlEvent.Attribute(attribute, defaultValue);
        }

        return result;
    }

    /// <summary>Reads an attribute off the element at the other end of the span.</summary>
    /// <param name="attribute">The attribute name.</param>
    /// <param name="defaultValue">What to answer when it is absent.</param>
    /// <returns>The value, or the default.</returns>
    internal string GetPairedMxlEventAttribute(string attribute, string defaultValue)
    {
        string result = defaultValue;
        if (MxlEvent != null && MxlEvent.PairedWith != null)
        {
            LilySpanEvent paired = (LilySpanEvent)MxlEvent.PairedWith.SpannerEvent;
            if (paired != null)
            {
                if (paired.MxlAttributes != null)
                {
                    result = paired.MxlAttributes.TryGetValue(attribute, out string value)
                        ? value
                        : defaultValue;
                }
                else if (paired.MxlEvent != null)
                {
                    result = paired.MxlEvent.Attribute(attribute, defaultValue);
                }
            }
        }

        return result;
    }

    /// <summary>The event at the other end of the span.</summary>
    /// <returns>The event, or null.</returns>
    internal LilySpanEvent GetPairedEvent()
    {
        LilySpanEvent result = null;
        if (MxlEvent != null && MxlEvent.PairedWith != null)
        {
            result = (LilySpanEvent)MxlEvent.PairedWith.SpannerEvent;
        }

        return result;
    }

    /// <summary>The text elements the other end of the span carries.</summary>
    /// <returns>The elements, or null.</returns>
    internal List<LilyMarkupElement> GetPairedTextElements()
    {
        List<LilyMarkupElement> result = null;
        if (MxlEvent != null && MxlEvent.PairedWith != null)
        {
            LilySpanEvent pairedEvent = (LilySpanEvent)MxlEvent.PairedWith.SpannerEvent;
            if (pairedEvent != null)
            {
                result = pairedEvent.TextElements;
            }
        }

        return result;
    }

    /// <summary>The text this event draws, when it draws any.</summary>
    /// <remarks>
    /// ⚠ Upstream declares <c>text_elements</c> only on the two spanner classes that
    /// carry text, and <c>get_paired_text_elements</c> reads it off whichever spanner
    /// is at the other end. Since either kind can be the partner, the field sits on
    /// the base here, exactly as that method's reach requires.
    /// </remarks>
    internal List<LilyMarkupElement> TextElements { get; set; }
}

/// <summary>A breath mark.</summary>
internal sealed class LilyBreatheEvent : LilyEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this event belongs to.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="fontSize">The size to draw it at.</param>
    internal LilyBreatheEvent(MusicXmlImportState state, string color, string fontSize)
        : base(state)
    {
        Color = color;
        FontSize = fontSize;

        List<string> afterNote = new List<string>();
        string clr = LilyMarkup.ColorToLy(color);
        if (clr != null)
        {
            afterNote.Add("\\tweak color " + clr);
        }

        string size = LilyMarkup.GetFontSize(state, fontSize, false);
        if (size != null)
        {
            afterNote.Add("\\tweak font-size " + size);
        }

        afterNote.Add("\\breathe");

        AfterNote = string.Join(" ", afterNote);
    }

    /// <inheritdoc/>
    internal override string LyExpression() => string.Empty;
}

/// <summary>A caesura.</summary>
internal sealed class LilyCaesuraEvent : LilyEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this event belongs to.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="fontSize">The size to draw it at.</param>
    internal LilyCaesuraEvent(MusicXmlImportState state, string color, string fontSize)
        : base(state)
    {
        Color = color;
        FontSize = fontSize;

        List<string> afterNote = new List<string>();
        string clr = LilyMarkup.ColorToLy(color);
        if (clr != null)
        {
            afterNote.Add("\\tweak color " + clr);
        }

        string size = LilyMarkup.GetFontSize(state, fontSize, false);
        if (size != null)
        {
            afterNote.Add("\\tweak font-size " + size);
        }

        afterNote.Add("\\caesura");

        AfterNote = string.Join(" ", afterNote);
    }

    /// <inheritdoc/>
    internal override string LyExpression() => string.Empty;
}

/// <summary>A slur.</summary>
internal sealed class LilySlurEvent : LilySpanEvent
{
    /// <summary>Builds the slur.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilySlurEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which of the overlapping slurs this is.</summary>
    internal int Number { get; set; } = 1;

    /// <inheritdoc/>
    internal override void PrintBeforeNote(LilyOutputPrinter printer)
    {
        string command = LineType switch
        {
            "dotted" => "\\slurDotted",
            "dashed" => "\\slurDashed",
            _ => string.Empty,
        };
        if (command.Length > 0 && SpanDirection == -1)
        {
            printer.Dump(command);
        }
    }

    /// <inheritdoc/>
    internal override void PrintAfterNote(LilyOutputPrinter printer)
    {
        //Reset non-solid slur types!
        string command = LineType switch
        {
            "dotted" => "\\slurSolid",
            "dashed" => "\\slurSolid",
            _ => string.Empty,
        };
        if (command.Length > 0 && SpanDirection == -1)
        {
            printer.Dump(command);
        }
    }

    /// <summary>The modifier that puts the slur on one side of the staff.</summary>
    /// <returns>The modifier.</returns>
    internal string DirectionMod()
        => ForceDirection switch { 1 => "^", -1 => "_", _ => string.Empty };

    /// <summary>This slur's own mark.</summary>
    /// <returns>The mark.</returns>
    internal string SlurToLy()
    {
        string result = SpanDirection switch { -1 => "(", 1 => ")", _ => string.Empty };
        if (result.Length > 0 && Number != 1)
        {
            result = "\\=" + Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                     + result;
        }

        return result;
    }

    /// <inheritdoc/>
    internal override string LyExpression() => SlurToLy();

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        string val = SlurToLy();
        if (val.Length == 0)
        {
            return;
        }

        if (SpanDirection == -1)
        {
            if (Visible)
            {
                string color = LilyMarkup.ColorToLy(Color);
                if (color != null)
                {
                    printer.Dump("-\\tweak color " + color);
                }

                printer.Dump(DirectionMod() + val);
            }
            else
            {
                printer.Dump(NotVisible() + DirectionMod() + val);
            }
        }
        else
        {
            printer.Dump(val);
        }
    }
}

/// <summary>A beam.</summary>
internal sealed class LilyBeamEvent : LilySpanEvent
{
    /// <summary>Builds the beam.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyBeamEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();
        if (SpanDirection == -1)
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("\\tweak color " + color);
            }

            result.Add("[");
        }
        else if (SpanDirection == 1)
        {
            result.Add("]");
        }

        return string.Join(" ", result);
    }
}

/// <summary>A sustain-pedal mark.</summary>
internal sealed class LilyPedalEvent : LilySpanEvent, ILilyOffsetEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyPedalEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    public override bool WaitForNote() => SpanDirection != 1;

    /// <inheritdoc/>
    /// <remarks>
    /// LilyPond's support for positioning pedal marks above or below a staff is
    /// limited: if there is a series of <c>\sustainOn</c> and <c>\sustainOff</c>
    /// commands without any intermediate stop, all of them are positioned with a single
    /// <c>SustainPedalLineSpanner</c> grob (which can span over multiple systems). In
    /// other words, positioning of single pedal marks is not possible in general. For
    /// this reason we ignore the 'placement' attribute.
    /// </remarks>
    internal override string LyExpression()
    {
        if (!Offset.IsZero)
        {
            return string.Empty;
        }

        List<string> result = new List<string>();

        string color = LilyMarkup.ColorToLy(Color);
        string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);

        if (SpanDirection == 1)
        {
            result.Add("<>");
            if (color != null)
            {
                result.Add("\\tweak color " + color);
            }

            if (fontSize != null)
            {
                result.Add("\\tweak font-size " + fontSize);
            }
        }

        if (SpanDirection == 0 || SpanDirection == 1)
        {
            result.Add("\\sustainOff");
        }

        if (SpanDirection == 0 || SpanDirection == -1)
        {
            if (color != null)
            {
                result.Add("\\tweak color " + color);
            }

            if (fontSize != null)
            {
                result.Add("\\tweak font-size " + fontSize);
            }

            result.Add("\\sustainOn");
        }

        return string.Join(" ", result);
    }
}

/// <summary>A text spanner — a trill line, a dashed line or a wavy line.</summary>
internal sealed class LilyTextSpannerEvent : LilySpanEvent, ILilyOffsetEvent
{
    /// <summary>Builds the spanner.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyTextSpannerEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The ornament element this spanner decorates, when it has one.</summary>
    internal MusicXmlNode MxlOrnament { get; set; }

    /// <summary>The accidental marks a trill spanner carries.</summary>
    internal List<MusicXmlNode> AccidentalMarks { get; set; } = new List<MusicXmlNode>();

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <summary>How the spanner is drawn — 'wave', 'dashes', 'stop' or a trill.</summary>
    /// <remarks>
    /// ⚠ python grows this attribute on the object at run time (in
    /// <c>musicxml_direction_to_lily</c>); C# cannot, so it is a field here.
    /// </remarks>
    internal string Style { get; set; }

    /// <summary>The modifier that puts the spanner on one side of the staff.</summary>
    /// <returns>The modifier.</returns>
    internal string DirectionMod()
        => ForceDirection switch { 1 => "^", -1 => "_", _ => string.Empty };

    /// <summary>This spanner's tweaks and its own mark.</summary>
    /// <returns>The tweaks and the mark.</returns>
    internal (List<string> Tweaks, string Value) TextSpannerToLy()
    {
        List<string> tweaks = new List<string>();

        if (SpanDirection == 0)
        {
            if (AccidentalMarks.Count > 0)
            {
                //A trill pitch change. Being part of a spanner, we don't make it inherit
                //attributes from `<note>'.
                LilyOrnamentEvent ornamentEvent = new LilyOrnamentEvent(State);
                ornamentEvent.OrnamentType = (string.Empty, string.Empty);
                ornamentEvent.AccidentalMarks = AccidentalMarks;

                //TODO (upstream's): Find a possibility to 'attach' a trill pitch change
                //accidental mark to the spanner so that we can actually obey its
                //`placement' attribute. LilyPond doesn't support this currently; see
                //issue #6724. Here we heuristically implement the most common case,
                //which means an accidental above a trill spanner above a staff.
                string above = "^";
                State.Warning(
                    "Correct vertical positioning of trill pitch change accidental "
                    + "marks might fail and need manual fixing.");

                (_, string trillCommand, string trillArgs) = ornamentEvent.OrnamentToLy();
                trillCommand = above + trillCommand;
                return (new List<string>(), string.Join(" ", new[] { trillCommand, trillArgs }));
            }

            return (new List<string>(), string.Empty);
        }

        MusicXmlNode ornament = null;
        if (MxlOrnament != null)
        {
            ornament = MxlOrnament;
        }
        else if (((LilyTextSpannerEvent)GetPairedEvent()).MxlOrnament != null)
        {
            ornament = ((LilyTextSpannerEvent)GetPairedEvent()).MxlOrnament;
        }

        string val = string.Empty;

        if (SpanDirection == -1)
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                tweaks.Add("\\tweak color " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
            if (fontSize != null)
            {
                tweaks.Add("\\tweak font-size " + fontSize);
            }
        }

        if (Style == "wave")
        {
            if (SpanDirection == -1)
            {
                val = "\\startTextSpan";
                tweaks.Add("\\tweak style #'trill");
            }
        }
        else if (Style == "dashes")
        {
            if (SpanDirection == -1)
            {
                val = "\\startTextSpan";
                tweaks.Add("\\tweak style #'dashed-line");

                string startMarkup = LilyMarkup.TextToLy(State, TextElements, "\\normal-text");
                if (startMarkup.Length > 0)
                {
                    tweaks.Add("\\tweak bound-details.left.text " + "\\markup " + startMarkup);
                }

                string stopMarkup = LilyMarkup.TextToLy(
                    State, GetPairedTextElements(), "\\normal-text");
                if (stopMarkup.Length > 0)
                {
                    tweaks.Add("\\tweak bound-details.right.text " + "\\markup " + stopMarkup);
                }
            }
            else if (SpanDirection == 1)
            {
                val = GetPairedEvent() is LilyDynamicsSpannerEvent
                    ? "\\!"
                    : "\\stopTextSpan";
            }
        }
        else if (Style == "stop" && ornament == null)
        {
            val = "\\stopTextSpan";
        }
        else
        {
            if (SpanDirection == -1)
            {
                val = "\\startTrillSpan";

                string spannerColorAttribute = Color ?? "#000000";
                string trillColorAttribute = MxlOrnament == null
                    ? "#000000"
                    : MxlOrnament.Attribute("color", "#000000");
                string trillColor = string.Empty;
                if (spannerColorAttribute != trillColorAttribute)
                {
                    trillColor = LilyMarkup.ColorToLy(trillColorAttribute, true);
                }

                string trillFontSizeAttribute = MxlOrnament?.Attribute("font-size");
                string trillFontSize = LilyMarkup.GetFontSize(
                    State, trillFontSizeAttribute, false);

                string generalCase = "optional";
                if (!string.IsNullOrEmpty(trillColor) || !string.IsNullOrEmpty(trillFontSize))
                {
                    generalCase = "mandatory";
                }

                string accidentalMarksCommand = null;
                string accidentalMarksArgs = string.Empty;
                if (AccidentalMarks.Count > 0)
                {
                    //Being part of a spanner, we don't make a trill pitch accidental mark
                    //inherit attributes from `<note>'.
                    LilyOrnamentEvent ornamentEvent = new LilyOrnamentEvent(State);
                    ornamentEvent.OrnamentType = ("scripts.trill", "trill");
                    ornamentEvent.AccidentalMarks = AccidentalMarks;
                    ornamentEvent.ForceDirection = 0;
                    (_, accidentalMarksCommand, accidentalMarksArgs)
                        = ornamentEvent.OrnamentToLy(generalCase);
                }

                if (accidentalMarksCommand == null)
                {
                    if (!string.IsNullOrEmpty(trillColor) || !string.IsNullOrEmpty(trillFontSize))
                    {
                        tweaks.Add("\\tweak bound-details.left.text \\markup");
                        if (!string.IsNullOrEmpty(trillColor))
                        {
                            tweaks.Add("\\with-color " + trillColor);
                        }

                        if (!string.IsNullOrEmpty(trillFontSize))
                        {
                            tweaks.Add("\\normalsize \\fontsize " + trillFontSize);
                        }

                        tweaks.Add(
                            "\\with-true-dimension #X \\musicglyph \"scripts.trill\"");
                    }
                }
                else if (accidentalMarksCommand == "\\accTrill")
                {
                    val = "\\trillTweak " + accidentalMarksArgs + " " + val;
                }
                else
                {
                    tweaks.Add("\\tweak bound-details.left.text \\markup");
                    if (!string.IsNullOrEmpty(trillColor))
                    {
                        tweaks.Add("\\with-color " + trillColor);
                    }

                    if (!string.IsNullOrEmpty(trillFontSize))
                    {
                        tweaks.Add("\\normalsize \\fontsize " + trillFontSize);
                    }

                    tweaks.Add("\\accs-ornament " + accidentalMarksArgs);
                }
            }
            else
            {
                val = "\\stopTrillSpan";
            }
        }

        return (tweaks, val);
    }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        (List<string> tweaks, string val) = TextSpannerToLy();

        string result = string.Empty;
        if (val.Length > 0)
        {
            string notVisible = NotVisible();
            result = tweaks.Count > 0
                ? notVisible + string.Join(" ", tweaks) + " " + val
                : notVisible + val;
        }

        return result;
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (!Offset.IsZero)
        {
            return;
        }

        (List<string> tweaks, string val) = TextSpannerToLy();

        if (val.Length == 0)
        {
            return;
        }

        if (SpanDirection == -1)
        {
            string notVisible = NotVisible();
            if (tweaks.Count > 0)
            {
                printer.Dump(notVisible + tweaks[0]);
                for (int i = 1; i < tweaks.Count; i++)
                {
                    printer.Dump(tweaks[i]);
                }

                printer.Dump(DirectionMod() + val);
            }
            else
            {
                printer.Dump(notVisible + DirectionMod() + val);
            }
        }
        else
        {
            printer.Dump(val);
        }
    }
}

/// <summary>A dynamics spanner — a written-out crescendo or decrescendo.</summary>
internal sealed class LilyDynamicsSpannerEvent : LilySpanEvent, ILilyOffsetEvent
{
    /// <summary>Builds the spanner.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyDynamicsSpannerEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The ornament element this spanner decorates, when it has one.</summary>
    internal MusicXmlNode MxlOrnament { get; set; }

    /// <summary>Which of the two kinds of spanner this is.</summary>
    internal string Type { get; set; }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    public override bool WaitForNote() => SpanDirection == -1;

    /// <summary>The modifier that puts the spanner on one side of the staff.</summary>
    /// <returns>The modifier.</returns>
    internal string DirectionMod()
        => ForceDirection switch { 1 => "^", -1 => "_", _ => string.Empty };

    /// <summary>This spanner's tweaks and its own mark.</summary>
    /// <returns>The tweaks and the mark.</returns>
    internal (List<string> Tweaks, string Value) DynamicsSpannerToLy()
    {
        List<string> initMarkup = new List<string>();
        initMarkup.Add("\\normal-text");

        string spannerColorAttribute = Color ?? "#000000";
        string dynamicsColorAttribute = TextElements[0].Get("color", "#000000");

        if (spannerColorAttribute != dynamicsColorAttribute)
        {
            initMarkup.Add("\\with-color " + LilyMarkup.ColorToLy(dynamicsColorAttribute, true));
        }

        string textMarkup = LilyMarkup.TextToLy(
            State, TextElements, string.Join(" ", initMarkup));

        List<string> tweaks = new List<string>();

        string val = Type == "cresc" ? "\\Cresc" : "\\Decresc";

        string color = LilyMarkup.ColorToLy(Color);
        if (color != null)
        {
            tweaks.Add("\\tweak color " + color);
        }

        string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
        if (fontSize != null)
        {
            tweaks.Add("\\tweak font-size " + fontSize);
        }

        if (textMarkup.Length > 0)
        {
            tweaks.Add("\\tweak text \\markup " + textMarkup);
        }

        return (tweaks, val);
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (!Offset.IsZero)
        {
            return;
        }

        if (SpanDirection == -1)
        {
            (List<string> tweaks, string val) = DynamicsSpannerToLy();
            string notVisible = NotVisible();

            if (tweaks.Count > 0)
            {
                printer.Dump(notVisible + tweaks[0]);
                for (int i = 1; i < tweaks.Count; i++)
                {
                    printer.Dump(tweaks[i]);
                }

                printer.Dump(DirectionMod() + val);
            }
            else
            {
                printer.Dump(notVisible + DirectionMod() + val);
            }
        }
        else if (SpanDirection == 1)
        {
            printer.Dump("<>\\!");
        }
    }
}

/// <summary>A ligature bracket.</summary>
internal sealed class LilyBracketSpannerEvent : LilySpanEvent, ILilyOffsetEvent
{
    /// <summary>Builds the bracket.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyBracketSpannerEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    /// <remarks>Ligature brackets use prefix notation for the start.</remarks>
    public override bool WaitForNote() => SpanDirection != -1;

    /// <summary>This bracket's own mark.</summary>
    /// <returns>The mark.</returns>
    internal string BracketToLy()
        => SpanDirection switch { 1 => "\\]", -1 => "\\[", _ => string.Empty };

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (!Offset.IsZero)
        {
            return;
        }

        string val = BracketToLy();
        if (val.Length == 0)
        {
            return;
        }

        if (SpanDirection == -1)
        {
            string style = LineType switch
            {
                "dashed" => "dashed-line",
                "dotted" => "dotted-line",
                "wavy" => "trill",
                _ => null,
            };
            if (style != null)
            {
                printer.Dump("\\tweak style #'" + style);
            }

            string lineEndAtStart = GetMxlEventAttribute("line-end", "none");
            string lineEndAtStop = GetPairedMxlEventAttribute("line-end", "none");
            printer.Dump(
                "\\tweak edge-height #(make-edge-height '" + lineEndAtStart
                + " '" + lineEndAtStop + ")");

            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                printer.Dump("\\tweak color " + color);
            }

            string direction = ForceDirection switch
            {
                1 => "#UP",
                -1 => "#DOWN",
                _ => string.Empty,
            };
            if (direction.Length > 0)
            {
                printer.Dump("\\tweak direction " + direction);
            }
        }

        printer.Dump(val);
    }
}

/// <summary>An octave shift.</summary>
internal sealed class LilyOctaveShiftEvent : LilySpanEvent, ILilyOffsetEvent
{
    /// <summary>Builds the shift.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyOctaveShiftEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Upstream's comment: intentionally set to be always zero. It declares the field all
    /// the same, and the voice loop reads it off every direction event it built, so the
    /// port declares it too and no code path ever writes it.
    /// </remarks>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    public override bool WaitForNote()
        => SpanDirection == 1 && State.GetOttavasEndEarly() == "t";

    /// <inheritdoc/>
    internal override void SetSpanType(string type)
        => SpanType = type switch { "up" => 1, "down" => -1, _ => 0 };

    /// <summary>How far LilyPond shifts, in octaves.</summary>
    /// <returns>The shift.</returns>
    internal int LyOctaveShiftIndicator()
    {
        //Convert 8/15 to lilypond indicators (+-1/+-2)
        int value;
        if (Size == 8)
        {
            value = 1;
        }
        else if (Size == 15)
        {
            value = 2;
        }
        else
        {
            State.Warning(
                "Invalid octave shift size found: "
                + Size.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ". Using no shift.");
            value = 0;
        }

        //Negative values go up!
        value *= -1 * SpanType;
        return value;
    }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        //Intentionally no code to handle the offset.
        List<string> value = new List<string>();

        int direction = LyOctaveShiftIndicator();
        if (direction != 0)
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                value.Add("\\tweak color " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
            if (fontSize != null)
            {
                value.Add("\\tweak font-size " + fontSize);
            }

            value.Add(
                "\\ottava #"
                + direction.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return SpanDirection switch
        {
            -1 => string.Join(" ", value),
            1 => "\\ottava #0",
            _ => string.Empty,
        };
    }
}

/// <summary>A glissando or a portamento.</summary>
internal sealed class LilyGlissandoEvent : LilySpanEvent
{
    /// <summary>Builds the glissando.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyGlissandoEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    internal override void PrintBeforeNote(LilyOutputPrinter printer)
    {
        if (SpanDirection != -1)
        {
            return;
        }

        string style = LineType switch
        {
            "dashed" => "dashed-line",
            "dotted" => "dotted-line",
            "wavy" => "trill",
            _ => null,
        };
        if (style != null)
        {
            printer.Dump("\\once \\override Glissando.style = #'" + style);
        }

        if (Visible)
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                printer.Dump("\\once \\override Glissando.color = " + color);
            }
        }
    }

    /// <inheritdoc/>
    internal override string LyExpression()
        => SpanDirection switch { -1 => "\\glissando", 1 => string.Empty, _ => string.Empty };

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        string val = LyExpression();
        if (val.Length == 0)
        {
            return;
        }

        if (SpanDirection == -1)
        {
            printer.Dump(NotVisible() + val);
        }
        else
        {
            printer.Dump(val);
        }
    }
}

/// <summary>A tie.</summary>
internal sealed class LilyTieEvent : LilyEvent, ILilyAssociatedEvent
{
    /// <summary>Builds the tie.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyTieEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which side of the staff the tie is drawn on.</summary>
    internal int ForceDirection { get; set; }

    /// <summary>Which kind of tie this is.</summary>
    internal string Type { get; set; }

    /// <summary>The modifier that puts the tie on one side of the staff.</summary>
    /// <returns>The modifier.</returns>
    internal string DirectionMod()
        => ForceDirection switch { 1 => "^", -1 => "_", _ => string.Empty };

    /// <inheritdoc/>
    public string PreChordLy() => string.Empty;

    /// <inheritdoc/>
    public string PreNoteLy(bool isChordElement) => string.Empty;

    /// <inheritdoc/>
    public string PostNoteLy(bool isChordElement)
    {
        List<string> result = new List<string>();
        string color = LilyMarkup.ColorToLy(Color);
        if (color != null)
        {
            result.Add("\\tweak color " + color);
        }

        string tie = Type switch
        {
            "start" => "~",
            "let-ring" => "\\laissezVibrer",
            //'continue' => r'\repeatTie',
            _ => "~",
        };
        result.Add(DirectionMod() + tie);
        return string.Join(" ", result);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ Upstream's <c>ly_expression</c> here CALLS <c>post_note_ly</c> and then falls
    /// off the end, so it answers <c>None</c> rather than the text it computed. The
    /// port reproduces the discarded call and the empty answer; nothing reaches it,
    /// because a tie is only ever an associated event.
    /// </remarks>
    internal override string LyExpression()
    {
        PostNoteLy(true);
        return null;
    }
}

/// <summary>A hairpin — a drawn crescendo or decrescendo.</summary>
internal sealed class LilyHairpinEvent : LilySpanEvent, ILilyOffsetEvent
{
    /// <summary>Builds the hairpin.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyHairpinEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Whether the hairpin runs to the next bar line.</summary>
    internal bool ToBarline { get; set; }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    internal override void SetSpanType(string type)
        => SpanType = type switch
        {
            "crescendo" => 1,
            "decrescendo" => -1,
            "diminuendo" => -1,
            _ => 0,
        };

    /// <summary>This hairpin's own mark.</summary>
    /// <returns>The mark.</returns>
    internal string HairpinToLy()
    {
        if (SpanDirection == 1)
        {
            return "\\!";
        }

        return SpanType switch { 1 => "\\<", -1 => "\\>", _ => string.Empty };
    }

    /// <summary>The modifier that puts the hairpin on one side of the staff.</summary>
    /// <returns>The modifier.</returns>
    internal string DirectionMod()
        => ForceDirection switch { 1 => "^", -1 => "_", _ => "-" };

    /// <inheritdoc/>
    internal override string LyExpression() => HairpinToLy();

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (!Offset.IsZero)
        {
            return;
        }

        string val = HairpinToLy();
        if (val.Length == 0)
        {
            return;
        }

        if (SpanDirection == -1)
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                printer.Dump("-\\tweak color " + color);
            }

            printer.Dump(DirectionMod() + val);
        }
        else
        {
            string pre = ToBarline ? "<>" : string.Empty;
            printer.Dump(pre + val);
        }
    }
}

/// <summary>A dynamics mark.</summary>
internal sealed class LilyDynamicsEvent : LilyEvent, ILilyWaitForNote, ILilyOffsetEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyDynamicsEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which mark this is.</summary>
    internal string Type { get; set; }

    /// <summary>Which side of the staff the mark is drawn on.</summary>
    internal int? ForceDirection { get; set; }

    /// <summary>What to multiply the mark's font size by.</summary>
    internal double FontSizeScale { get; set; } = 1.0;

    /// <summary>Whether the mark runs to the next bar line.</summary>
    internal bool ToBarline { get; set; }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    public bool WaitForNote() => true;

    /// <summary>The modifier that puts the mark on one side of the staff.</summary>
    /// <returns>The modifier.</returns>
    internal string DirectionMod()
        => ForceDirection switch { 1 => "^", -1 => "_", _ => "-" };

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        if (!Offset.IsZero)
        {
            return string.Empty;
        }

        List<string> result = new List<string>();
        if (!string.IsNullOrEmpty(Type))
        {
            //TODO (upstream's): This is a temporary solution because LilyPond ignores a
            //dynamics symbol at the end of music with a warning. A solution similar to
            //handling `<offset>' is needed.
            if (ToBarline)
            {
                result.Add("<>");
            }

            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("-\\tweak color " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false, FontSizeScale);
            if (fontSize != null)
            {
                result.Add("-\\tweak font-size " + fontSize);
            }

            result.Add(DirectionMod() + "\\" + Type);
        }

        return string.Join(" ", result);
    }
}

/// <summary>A rehearsal mark.</summary>
internal sealed class LilyMarkEvent : LilyEvent, ILilyWaitForNote, ILilyOffsetEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyMarkEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The text the mark draws.</summary>
    internal List<LilyMarkupElement> TextElements { get; set; }

    /// <summary>Which side of the staff the mark is drawn on.</summary>
    internal int? ForceDirection { get; set; }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    public bool WaitForNote() => false;

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string textMarkup = LilyMarkup.TextToLy(State, TextElements);
        return textMarkup.Length > 0 ? "\\mark \\markup " + textMarkup : string.Empty;
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (!Offset.IsZero)
        {
            return;
        }

        string direction = ForceDirection switch
        {
            1 => "#UP",
            -1 => "#DOWN",
            _ => string.Empty,
        };
        if (direction.Length > 0)
        {
            printer.Dump("\\tweak direction " + direction);
        }

        printer.Dump(LyExpression());
    }
}

/// <summary>A text mark.</summary>
internal sealed class LilyTextMarkEvent : LilyEvent, ILilyWaitForNote, ILilyOffsetEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyTextMarkEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The text the mark draws.</summary>
    internal List<LilyMarkupElement> TextElements { get; set; }

    /// <summary>Which side of the staff the mark is drawn on.</summary>
    internal int? ForceDirection { get; set; }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    public bool WaitForNote() => false;

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string textMarkup = LilyMarkup.TextToLy(State, TextElements);
        return textMarkup.Length > 0 ? "\\textMark \\markup " + textMarkup : string.Empty;
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (!Offset.IsZero)
        {
            return;
        }

        string direction = ForceDirection switch
        {
            1 => "#UP",
            -1 => "#DOWN",
            _ => string.Empty,
        };
        if (direction.Length > 0)
        {
            printer.Dump("\\tweak direction " + direction);
        }

        printer.Dump(LyExpression());
    }
}
