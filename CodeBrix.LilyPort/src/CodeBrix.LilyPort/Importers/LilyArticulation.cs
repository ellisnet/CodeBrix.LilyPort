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

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (TextEvent, ArticulationEvent and everything drawn beside a note below them);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>Free text drawn beside a note.</summary>
internal sealed class LilyTextEvent : LilyEvent, ILilyWaitForNote, ILilyOffsetEvent
{
    /// <summary>Builds the text.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyTextEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The text this event draws.</summary>
    internal List<LilyMarkupElement> TextElements { get; set; }

    /// <summary>Which side of the staff the text is drawn on.</summary>
    internal int? ForceDirection { get; set; }

    /// <summary>Whether the text runs to the next bar line.</summary>
    internal bool ToBarline { get; set; }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    /// <remarks>
    /// This is problematic: LilyPond markup like <c>^"text"</c> requires this to be
    /// true, otherwise compilation will fail; we are thus forced to answer true.
    /// However, this might lead to wrong placement of text if derived from
    /// <c>&lt;direction-type&gt;</c> combinations not handled specially in
    /// <c>musicxml_direction_to_lily</c>.
    /// </remarks>
    public bool WaitForNote() => true;

    /// <summary>The modifier that puts the text on one side of the staff.</summary>
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

        string textMarkup = LilyMarkup.TextToLy(State, TextElements);
        if (textMarkup.Length == 0)
        {
            return string.Empty;
        }

        //TODO (upstream's): This is a temporary solution because text at the end of
        //music silently disappears. A solution similar to handling `<offset>' is needed.
        string pre = ToBarline ? "<>" : string.Empty;
        return pre + DirectionMod() + "\\markup " + textMarkup;
    }
}

/// <summary>An articulation drawn beside a note.</summary>
internal class LilyArticulationEvent : LilyEvent, ILilyWaitForNote
{
    /// <summary>Builds the articulation.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyArticulationEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which articulation this is.</summary>
    internal string Type { get; set; }

    /// <summary>Which side of the staff the articulation is drawn on.</summary>
    internal int? ForceDirection { get; set; }

    /// <inheritdoc/>
    public virtual bool WaitForNote() => true;

    /// <summary>The modifier that puts the articulation on one side of the staff.</summary>
    /// <returns>The modifier.</returns>
    internal virtual string DirectionMod()
        => ForceDirection switch { 1 => "^", -1 => "_", _ => string.Empty };

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();

        if (!string.IsNullOrEmpty(Type))
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("-\\tweak color " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
            if (fontSize != null)
            {
                result.Add("-\\tweak font-size " + fontSize);
            }

            result.Add(DirectionMod() + "\\" + Type);
        }

        return string.Join(" ", result);
    }
}

/// <summary>An ornament, with the accidental marks that may sit around it.</summary>
internal class LilyOrnamentEvent : LilyArticulationEvent
{
    /// <summary>Builds the ornament.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyOrnamentEvent(MusicXmlImportState state)
        : base(state)
    {
        ForceDirection = 1;
    }

    /// <summary>The colour the note carries, which an accidental mark inherits.</summary>
    internal string NoteColor { get; set; }

    /// <summary>The font size the note carries, which an accidental mark inherits.</summary>
    internal string NoteFontSize { get; set; }

    /// <summary>The accidental marks drawn with this ornament.</summary>
    internal List<MusicXmlNode> AccidentalMarks { get; set; } = new List<MusicXmlNode>();

    /// <summary>Where the ornament sits, for placing its accidental marks.</summary>
    /// <remarks>
    /// ⚠ This is the <c>default-y</c> ATTRIBUTE, so it is the document's TEXT, and
    /// upstream compares it against another attribute's text with python's <c>&gt;</c>
    /// — a lexicographic comparison, not a numeric one. The port compares ordinally,
    /// which is what python's string comparison is. Recorded in PORT-COVERAGE.
    /// </remarks>
    internal string YPos { get; set; }

    /// <summary>The ornament's glyph and its LilyPond command.</summary>
    /// <remarks>
    /// ⚠ NAMING (rule 6). Upstream reuses the name <c>type</c> that
    /// <c>ArticulationEvent</c> holds a STRING in, and stores a two-member tuple in it
    /// instead; python simply overwrites the attribute. C# cannot give one name two
    /// types, so the ornament's reading is named for what it holds. Recorded in
    /// PORT-COVERAGE.
    /// </remarks>
    internal (string Glyph, string Command) OrnamentType { get; set; }

    /// <summary>Whether every character of a string is ASCII — python's isascii.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it is all ASCII.</returns>
    private static bool IsAscii(string text) => text.All(c => c < 128);

    /// <summary>This ornament's tweaks, its command and the command's arguments.</summary>
    /// <param name="generalCase">
    /// Null for the ordinary reading; 'optional' to emit only <c>\accTrill</c> or
    /// <c>\accs-ornament</c>; 'mandatory' to emit only <c>\accs-ornament</c>.
    /// </param>
    /// <returns>The tweaks, the command and its arguments.</returns>
    /// <remarks>
    /// <c>&lt;accidental-mark&gt;</c> elements get mapped to the LilyPond markup command
    /// <c>\ornament</c> for 'simple' cases (i.e., there is at most one accidental mark
    /// above and below that both have the same color as the ornament, and there is no
    /// 'font-size' attribute for the two accidental marks) or <c>\accs-ornament</c> for
    /// the general case. If there are no accidental marks at all, use LilyPond's
    /// standard ornament command. For trills, we use <c>\accTrill</c> if there is a
    /// single accidental mark above the trill.
    /// </remarks>
    internal (List<string> Tweaks, string Command, string Args) OrnamentToLy(
        string generalCase = null)
    {
        List<string> tweaks = new List<string>();
        string command = null;
        string args = string.Empty;

        string color = LilyMarkup.ColorToLy(Color);
        if (color != null)
        {
            tweaks.Add("-\\tweak color " + color);
        }
        else
        {
            color = "\"#000000\"";
        }

        string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
        if (fontSize != null)
        {
            tweaks.Add("-\\tweak font-size " + fontSize);
        }

        List<(string Value, string Color, string FontSize, string Enclosure)> aboveMarks
            = new List<(string, string, string, string)>();
        List<(string Value, string Color, string FontSize, string Enclosure)> belowMarks
            = new List<(string, string, string, string)>();
        bool sameColor = true;
        bool haveFontSize = false;

        foreach (MusicXmlNode mark in AccidentalMarks)
        {
            string markValue = LilyMarkup.AccidentalValue(mark.GetText());
            if (markValue == null)
            {
                continue;
            }

            //Color and font size gets inherited from `<note>' (if not part of a spanner).
            string markColor = LilyMarkup.ColorToLy(mark.Attribute("color", NoteColor));
            string markFontSize = LilyMarkup.GetFontSize(
                State, mark.Attribute("font-size", NoteFontSize), false);

            //Do a simple check of the position relative to the base glyph if the
            //'placement' attribute is missing and 'default-y' is present.
            bool haveYPos = false;
            string markPlacement = null;
            if (YPos != null)
            {
                string markYPos = mark.Attribute("default-y");
                if (markYPos != null)
                {
                    markPlacement = string.CompareOrdinal(markYPos, YPos) > 0
                        ? "above"
                        : "below";
                    haveYPos = true;
                }
            }

            if (!haveYPos)
            {
                markPlacement = mark.Attribute("placement", "above");
            }

            //Similar to normal accidentals, give brackets precedence over parentheses.
            string markBracket = mark.Attribute("bracket", "no");
            string markParentheses = mark.Attribute("parentheses", "no");
            string markEnclosure = null;
            if (markBracket == "yes")
            {
                markEnclosure = "[]";
            }
            else if (markParentheses == "yes")
            {
                markEnclosure = "()";
            }

            if (markColor == null)
            {
                markColor = "\"#000000\"";
            }

            (string, string, string, string) entry
                = (markValue, markColor, markFontSize, markEnclosure);
            if (markPlacement == "above")
            {
                aboveMarks.Add(entry);
            }
            else
            {
                belowMarks.Add(entry);
            }

            if (markColor != color)
            {
                sameColor = false;
            }

            if (markFontSize != null)
            {
                haveFontSize = true;
            }
        }

        if (sameColor && !haveFontSize
            && aboveMarks.Count <= 1 && belowMarks.Count <= 1
            && generalCase != "mandatory"
            && !(generalCase == "optional" && belowMarks.Count > 0))
        {
            //The 'simple' case.
            string above = string.Empty;
            if (aboveMarks.Count > 0)
            {
                above = aboveMarks[0].Value;
                if (aboveMarks[0].Enclosure != null)
                {
                    above = aboveMarks[0].Enclosure[0] + above + aboveMarks[0].Enclosure[1];
                }
            }

            string below = string.Empty;
            if (belowMarks.Count > 0)
            {
                below = belowMarks[0].Value;
                if (belowMarks[0].Enclosure != null)
                {
                    below = belowMarks[0].Enclosure[0] + below + belowMarks[0].Enclosure[1];
                }
            }

            if (above.Length > 0 || below.Length > 0)
            {
                if (OrnamentType.Command == "trill" && below.Length == 0)
                {
                    command = "\\accTrill";
                    args = "\"" + above + "\"";
                }
                else
                {
                    //This case doesn't happen for trill spanners.
                    command = "\\ornament";
                    args = "\"" + above + "\" \"" + OrnamentType.Glyph + "\" \"" + below + "\"";
                }
            }
            else
            {
                //This case doesn't happen for trill spanners.
                command = "\\" + OrnamentType.Command;
            }
        }
        else
        {
            //The general case.
            List<string> above = new List<string>();
            List<string> below = new List<string>();
            foreach ((List<(string Value, string Color, string FontSize, string Enclosure)> marks,
                      List<string> acc)
                     in new[] { (aboveMarks, above), (belowMarks, below) })
            {
                foreach ((string markValue, string markColor, string markFontSize,
                          string markEnclosure) in marks)
                {
                    List<string> markup = new List<string>();
                    if (color != "\"#000000\"" || markColor != "\"#000000\"")
                    {
                        markup.Add("\\with-color " + markColor);
                    }

                    if (markFontSize != null)
                    {
                        markup.Add("\\normalsize \\fontsize " + markFontSize);
                    }

                    string glyph = IsAscii(markValue)
                        ? "\\musicglyph \"" + markValue + "\""
                        : "\\number \"" + markValue + "\"";

                    if (markEnclosure != null)
                    {
                        //We increase the font size of the enclosure by three magnitudes
                        //(see `acc-font-size' and `enclosure-font-size' properties of the
                        //`\accs-ornament' markup command). This is a bit awkward since we
                        //have to hard-code it here. Also be careful not to change the
                        //precision.
                        const int delta = 3;
                        string enclosureCommand;
                        if (!string.IsNullOrEmpty(markFontSize))
                        {
                            string[] parts = markFontSize.Substring(1).Split('.');
                            string encFontSize = (int.Parse(parts[0], CultureInfo.InvariantCulture)
                                                  + delta)
                                .ToString(CultureInfo.InvariantCulture);
                            if (parts.Length > 1)
                            {
                                encFontSize += parts[1];
                            }

                            enclosureCommand = "\\normalsize \\fontsize #" + encFontSize;
                        }
                        else
                        {
                            enclosureCommand = "\\fontsize #"
                                               + delta.ToString(CultureInfo.InvariantCulture);
                        }

                        glyph = "\\concat { " + enclosureCommand + " \"" + markEnclosure[0]
                                + "\" " + glyph + " " + enclosureCommand + " \""
                                + markEnclosure[1] + "\" }";
                    }

                    markup.Add(glyph);

                    acc.Add(string.Join(" ", markup));
                }
            }

            string ornament = !string.IsNullOrEmpty(OrnamentType.Glyph)
                ? "\\musicglyph \"" + OrnamentType.Glyph + "\""
                : "##f";

            tweaks.Add("-\\tweak parent-alignment-X #CENTER");
            tweaks.Add("-\\tweak self-alignment-X #CENTER");
            command = "\\markup \\accs-ornament";
            args = "{ " + string.Join(" ", above) + " } " + ornament + " { "
                   + string.Join(" ", below) + " }";
        }

        return (tweaks, command, args);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ Upstream REPLACES <c>ly_expression</c> here with one returning a triple rather
    /// than a string, so the inherited string form does not exist for an ornament. C#
    /// cannot change a method's return type, so the triple is
    /// <see cref="OrnamentToLy"/> and this throws rather than answering the base
    /// class's string, which no caller of an ornament ever wants (standing rule 4).
    /// </remarks>
    internal override string LyExpression()
        => throw new InvalidOperationException(
            "An ornament's LilyPond expression is a command and its arguments; "
            + "call OrnamentToLy instead.");

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        string direction = DirectionMod();
        (List<string> tweaks, string command, string args) = OrnamentToLy();

        foreach (string tweak in tweaks)
        {
            printer.Dump(tweak);
        }

        printer.Dump(direction + command);
        printer.Dump(args);
    }
}

/// <summary>A turn drawn between this note and the next.</summary>
internal sealed class LilyDelayedTurnEvent : LilyOrnamentEvent
{
    /// <summary>Builds the turn.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyDelayedTurnEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How long the note this turn follows lasts.</summary>
    internal PythonFraction Duration { get; set; }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        string direction = DirectionMod();
        (List<string> tweaks, string command, string args) = OrnamentToLy();

        //We position the delayed turn in the middle between the current and the next note.
        LilyDuration duration = LilyDuration.FromFraction(
            State, Duration / PythonFraction.FromLong(2));
        //Also take care of tuplet timing.
        printer.Dump(
            "\\after " + duration.LyExpression(duration.Factor / printer.DurationFactor())
            + " ");
        foreach (string tweak in tweaks)
        {
            printer.Dump(tweak);
        }

        printer.Dump(direction + command);
        printer.Dump(args);
    }
}

/// <summary>An articulation written as a one-character modifier.</summary>
internal class LilyShortArticulationEvent : LilyArticulationEvent
{
    /// <summary>Builds the articulation.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyShortArticulationEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    /// <remarks>The default is the neutral modifier.</remarks>
    internal override string DirectionMod()
        => ForceDirection switch { 1 => "^", -1 => "_", _ => "-" };

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();
        if (!string.IsNullOrEmpty(Type))
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("-\\tweak color " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
            if (fontSize != null)
            {
                result.Add("\\tweak font-size " + fontSize);
            }

            result.Add(DirectionMod() + Type);
        }

        return string.Join(" ", result);
    }
}

/// <summary>A toe or heel mark, which may stand in for the one before it.</summary>
internal sealed class LilyArticulationWithSubstitutionEvent : LilyArticulationEvent
{
    /// <summary>Builds the articulation.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyArticulationWithSubstitutionEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Whether this mark substitutes for the one before it.</summary>
    internal bool Substitution { get; set; }
}

/// <summary>An articulation LilyPond draws without a direction modifier.</summary>
internal sealed class LilyNoDirectionArticulationEvent : LilyArticulationEvent
{
    /// <summary>Builds the articulation.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyNoDirectionArticulationEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();
        if (!string.IsNullOrEmpty(Type))
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("\\tweak color " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
            if (fontSize != null)
            {
                result.Add("\\tweak font-size " + fontSize);
            }

            result.Add("\\" + Type);
        }

        return string.Join(" ", result);
    }
}

/// <summary>A fingering or a plucking mark.</summary>
internal sealed class LilyFingeringEvent : LilyShortArticulationEvent, ILilyAssociatedEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyFingeringEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Whether this is a plucking mark rather than a fingering.</summary>
    internal bool IsPluck { get; set; }

    /// <summary>Whether this fingering is an alternative to another.</summary>
    internal bool Alternate { get; set; }

    /// <summary>Whether this fingering substitutes for the one before it.</summary>
    internal bool Substitution { get; set; }

    /// <summary>Whether the mark is drawn at all.</summary>
    internal bool Visible { get; set; } = true;

    /// <inheritdoc/>
    public string PreChordLy() => string.Empty;

    /// <inheritdoc/>
    public string PreNoteLy(bool isChordElement) => string.Empty;

    /// <inheritdoc/>
    public string PostNoteLy(bool isChordElement)
    {
        List<string> result = new List<string>();
        if (!string.IsNullOrEmpty(Type))
        {
            //Color and font size gets handled by the chord's own printing.
            result.Add(DirectionMod() + Type);
        }

        return string.Join(" ", result);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ Upstream's <c>ly_expression</c> here CALLS <c>post_note_ly</c> and falls off
    /// the end, so it answers <c>None</c>. Reproduced; nothing reaches it, because a
    /// fingering is only ever an associated event.
    /// </remarks>
    internal override string LyExpression()
    {
        PostNoteLy(true);
        return null;
    }
}

/// <summary>Ready-made markup drawn beside a note.</summary>
internal class LilyMarkupEvent : LilyShortArticulationEvent, ILilyOffsetEvent
{
    /// <summary>Builds the markup.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyMarkupEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The markup to draw.</summary>
    internal string Contents { get; set; }

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        if (!Offset.IsZero)
        {
            return string.Empty;
        }

        List<string> result = new List<string>();
        if (!string.IsNullOrEmpty(Contents))
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("-\\tweak color " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
            if (fontSize != null)
            {
                result.Add("-\\tweak font-size " + fontSize);
            }

            result.Add(DirectionMod() + "\\markup { " + Contents + " }");
        }

        return string.Join(" ", result);
    }
}

/// <summary>An accidental mark drawn above or below a note.</summary>
internal sealed class LilyAccidentalMarkEvent : LilyMarkupEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyAccidentalMarkEvent(MusicXmlImportState state)
        : base(state)
    {
        ForceDirection = 1;
    }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string contents = LilyMarkup.AccidentalValue(Contents);
        if (contents == null)
        {
            return string.Empty;
        }

        contents = contents.All(c => c < 128)
            ? "\\musicglyph \"" + contents + "\""
            : "\\number \"" + contents + "\"";

        List<string> result = new List<string>();

        //Accidental marks should be horizontally centered on the note head.
        result.Add("-\\tweak parent-alignment-X #CENTER");
        result.Add("-\\tweak self-alignment-X #CENTER");

        string color = LilyMarkup.ColorToLy(Color);
        if (color != null)
        {
            result.Add("-\\tweak color " + color);
        }

        string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
        if (fontSize != null)
        {
            result.Add("-\\tweak font-size " + fontSize);
        }

        result.Add(DirectionMod() + "\\markup " + contents);

        return string.Join(" ", result);
    }
}

/// <summary>A fret diagram.</summary>
internal sealed class LilyFretEvent : LilyMarkupEvent
{
    /// <summary>Builds the diagram.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyFretEvent(MusicXmlImportState state)
        : base(state)
    {
        ForceDirection = 1;
    }

    /// <summary>How many strings the instrument has.</summary>
    internal int Strings { get; set; } = 6;

    /// <summary>How many frets the diagram shows.</summary>
    internal int Frets { get; set; } = 4;

    /// <summary>The barre, as its fret and the two strings it spans.</summary>
    internal List<int> Barre { get; set; }

    /// <summary>
    /// One entry per string: the string, then the fret — an integer, or the text
    /// <c>o</c> or <c>x</c> — and then the finger, when the document names one.
    /// </summary>
    internal List<LilyFretElement> Elements { get; } = new List<LilyFretElement>();

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string val = string.Empty;
        if (Strings != 6)
        {
            val += "w:" + Strings.ToString(CultureInfo.InvariantCulture) + ";";
        }

        if (Frets != 4)
        {
            val += "h:" + Frets.ToString(CultureInfo.InvariantCulture) + ";";
        }

        if (Barre != null && Barre.Count >= 3)
        {
            val += "c:" + Barre[0].ToString(CultureInfo.InvariantCulture)
                   + "-" + Barre[1].ToString(CultureInfo.InvariantCulture)
                   + "-" + (Barre[2] + State.GetTransposeSemitones())
                       .ToString(CultureInfo.InvariantCulture) + ";";
        }

        bool haveFingering = false;
        foreach (LilyFretElement element in Elements)
        {
            if (element.Count > 1)
            {
                val += element.StringNumber.ToString(CultureInfo.InvariantCulture) + "-"
                       + (element.FretText != null
                           ? element.FretText
                           : (element.FretNumber + State.GetTransposeSemitones())
                               .ToString(CultureInfo.InvariantCulture));
            }

            if (element.Count > 2)
            {
                haveFingering = true;
                val += "-" + element.Fingering;
            }

            val += ";";
        }

        if (haveFingering)
        {
            val = "f:1;" + val;
        }

        return val.Length > 0
            ? DirectionMod() + "\\markup { \\fret-diagram #\"" + val + "\" }"
            : string.Empty;
    }
}

/// <summary>One string's entry in a fret diagram.</summary>
/// <remarks>
/// ⚠ Upstream builds these as python lists of two or three members whose second member
/// is EITHER an integer fret OR one of the strings <c>o</c> and <c>x</c>, and asks
/// <c>isinstance(i[1], str)</c> to decide whether the transposition applies. One C#
/// type cannot hold both, so the two readings are separate members and
/// <see cref="Count"/> carries the python list's own length, which upstream tests.
/// </remarks>
internal sealed class LilyFretElement
{
    /// <summary>Builds an entry naming a fret by number.</summary>
    /// <param name="stringNumber">Which string.</param>
    /// <param name="fretNumber">Which fret.</param>
    internal LilyFretElement(int stringNumber, int fretNumber)
    {
        StringNumber = stringNumber;
        FretNumber = fretNumber;
        Count = 2;
    }

    /// <summary>Builds an entry naming a fret by text.</summary>
    /// <param name="stringNumber">Which string.</param>
    /// <param name="fretText">The text — <c>o</c> or <c>x</c>.</param>
    internal LilyFretElement(int stringNumber, string fretText)
    {
        StringNumber = stringNumber;
        FretText = fretText;
        Count = 2;
    }

    /// <summary>Which string this entry is for.</summary>
    internal int StringNumber { get; }

    /// <summary>Which fret, when the entry names one by number.</summary>
    internal int FretNumber { get; }

    /// <summary>The fret's text, when the entry names one that way.</summary>
    internal string FretText { get; }

    /// <summary>Which finger stops the string, when the document names one.</summary>
    internal string Fingering { get; private set; }

    /// <summary>How many members upstream's own list would have had.</summary>
    internal int Count { get; private set; }

    /// <summary>Adds the finger that stops this string.</summary>
    /// <param name="fingering">The finger.</param>
    internal void SetFingering(string fingering)
    {
        Fingering = fingering;
        Count = 3;
    }
}

/// <summary>One note of a fretboard.</summary>
internal sealed class LilyFretBoardNote : LilyMusic
{
    /// <summary>Builds the note.</summary>
    /// <param name="state">The import this note belongs to.</param>
    internal LilyFretBoardNote(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which pitch is sounded.</summary>
    internal LilyPitch Pitch { get; set; }

    /// <summary>Which string it is played on.</summary>
    internal string StringNumber { get; set; }

    /// <summary>Which finger stops it.</summary>
    internal string Fingering { get; set; }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string text = Pitch.LyExpression();
        if (!string.IsNullOrEmpty(Fingering))
        {
            text += "-" + Fingering;
        }

        if (!string.IsNullOrEmpty(StringNumber))
        {
            text += "\\" + StringNumber;
        }

        return text;
    }
}

/// <summary>A fretboard chord.</summary>
internal sealed class LilyFretBoardEvent : LilyNestedMusic
{
    /// <summary>Builds the chord.</summary>
    /// <param name="state">The import this chord belongs to.</param>
    internal LilyFretBoardEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How long the chord lasts, as LilyPond spells it.</summary>
    internal string Duration { get; set; }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        List<LilyFretBoardNote> fretboardNotes
            = Elements.OfType<LilyFretBoardNote>().ToList();
        if (fretboardNotes.Count > 0)
        {
            List<string> notes = new List<string>();
            foreach (LilyFretBoardNote note in fretboardNotes)
            {
                notes.Add(note.LyExpression());
            }

            string contents = string.Join(" ", notes);
            printer.Dump("<" + contents + ">" + Duration);
        }
    }
}

/// <summary>An event that wraps the note in a LilyPond function call.</summary>
internal class LilyFunctionWrapperEvent : LilyEvent, ILilyAssociatedEvent
{
    /// <summary>Builds the wrapper.</summary>
    /// <param name="state">The import this event belongs to.</param>
    /// <param name="functionName">The function to call.</param>
    internal LilyFunctionWrapperEvent(MusicXmlImportState state, string functionName = null)
        : base(state)
        => FunctionName = functionName;

    /// <summary>The function to call.</summary>
    internal string FunctionName { get; set; }

    /// <inheritdoc/>
    public string PreNoteLy(bool isChordElement)
        => !string.IsNullOrEmpty(FunctionName) ? "\\" + FunctionName : string.Empty;

    /// <inheritdoc/>
    public string PreChordLy() => string.Empty;

    /// <inheritdoc/>
    public string PostNoteLy(bool isChordElement) => string.Empty;

    /// <inheritdoc/>
    internal override string LyExpression()
        => !string.IsNullOrEmpty(FunctionName) ? "\\" + FunctionName : string.Empty;
}

/// <summary>The wrapper that puts a note in parentheses.</summary>
internal sealed class LilyParenthesizeEvent : LilyFunctionWrapperEvent
{
    /// <summary>Builds the wrapper.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyParenthesizeEvent(MusicXmlImportState state)
        : base(state, "parenthesize")
    {
    }
}

/// <summary>A stem, as its four values are written.</summary>
internal sealed class LilyStemEvent : LilyEvent, ILilyAssociatedEvent
{
    private static readonly Dictionary<string, string> StemValueDict
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "down", "\\D" },
            { "up", "\\U" },
            { "double", null },  //TODO (upstream's): Implement
            { "none", "\\tweak Stem.transparent ##t" },
        };

    /// <summary>Builds the stem.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyStemEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which of the four stem values the document gave.</summary>
    internal string Value { get; set; }

    /// <summary>Whether the stem is a rest's stemlet.</summary>
    internal bool IsStemlet { get; set; }

    /// <inheritdoc/>
    public string PreChordLy()
    {
        List<string> result = new List<string>();

        string value = Value != null && StemValueDict.TryGetValue(Value, out string mapped)
            ? mapped
            : null;
        if (value != null)
        {
            result.Add(value);
        }

        if (value != "none")
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("\\tweak Stem.color " + color);
            }
        }

        if (IsStemlet)
        {
            result.Add("\\tweak Stem.stemlet-length #1");
        }

        return string.Join(" ", result);
    }

    /// <inheritdoc/>
    public string PreNoteLy(bool isChordElement) => string.Empty;

    /// <inheritdoc/>
    public string PostNoteLy(bool isChordElement) => string.Empty;

    /// <inheritdoc/>
    internal override string LyExpression() => PreChordLy();
}

/// <summary>A notehead style.</summary>
internal sealed class LilyNoteStyleEvent : LilyEvent, ILilyAssociatedEvent
{
    /// <summary>
    /// The notehead tweaks each MusicXML style asks for: the style, then the direction.
    /// </summary>
    /// <remarks>
    /// LilyPond's sense of head direction may seem backward for some styles, but it is
    /// consistently the direction of the stem to which the head is designed to attach.
    /// </remarks>
    private static readonly Dictionary<string, (string Style, string Direction)>
        NoteheadStylesDict
            = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                { "arrow down", ("'arrow", "UP") },
                { "arrow up", ("'arrow", "DOWN") },
                { "back slashed", (null, null) },  //TODO (upstream's): Implement
                { "circle dot", (null, null) },  //TODO (upstream's): Implement
                { "circle-x", ("'xcircle", null) },
                { "circled", (null, null) },  //TODO (upstream's): Implement
                { "cluster", (null, null) },  //TODO (upstream's): Implement
                { "cross", (null, null) },  //TODO (upstream's): + shaped note head
                { "diamond", ("'harmonic-mixed", null) },
                { "do", ("'do", null) },
                { "fa", ("'fa", null) },  //LilyPond uses this for down-stem
                { "fa up", ("'fa", null) },  //LilyPond uses this for up-stem
                { "inverted triangle", (null, null) },  //TODO (upstream's): Implement
                { "la", ("'la", null) },
                { "left triangle", (null, null) },  //TODO (upstream's): Implement
                { "mi", ("'mi", null) },
                { "none", (string.Empty, null) },
                { "normal", (null, null) },
                { "re", ("'re", null) },
                { "rectangle", (null, null) },  //TODO (upstream's): Implement
                { "slash", ("'slash", null) },
                { "slashed", (null, null) },  //TODO (upstream's): Implement
                { "so", ("'sol", null) },
                { "square", ("'la", null) },  //TODO (upstream's): Proper squared note head
                { "ti", ("'ti", null) },
                { "triangle", ("'do", null) },
                { "x", ("'cross", null) },
            };

    private static readonly PythonFraction Half = new PythonFraction(1, 2);

    /// <summary>Builds the style.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyNoteStyleEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How long the note lasts, as a fraction of a whole note.</summary>
    /// <remarks>
    /// ⚠ NAMING (rule 6). Upstream calls this <c>duration</c>, but it holds the note's
    /// LENGTH rather than a <see cref="LilyDuration"/>, which every other
    /// <c>duration</c> in this file does; the name says which.
    /// </remarks>
    internal PythonFraction? NoteDuration { get; set; }

    /// <summary>Which MusicXML notehead style the document asked for.</summary>
    internal string Style { get; set; }

    /// <summary>Whether the notehead is filled in.</summary>
    internal string Filled { get; set; }

    /// <inheritdoc/>
    public string PreChordLy() => string.Empty;

    /// <inheritdoc/>
    public string PreNoteLy(bool isChordElement)
    {
        List<string> result = new List<string>();

        if (isChordElement)
        {
            (string style, string direction)
                = Style != null
                  && NoteheadStylesDict.TryGetValue(Style, out (string, string) mapped)
                    ? mapped
                    : (null, null);
            if (style == string.Empty)
            {
                result.Add("\\tweak transparent ##t");
            }
            else if (style != null)
            {
                result.Add("\\tweak style #" + style);
            }

            if (direction != null)
            {
                result.Add("\\tweak direction #" + direction);
            }

            if (style != string.Empty)
            {
                if (NoteDuration.Value < Half && Filled == "no")
                {
                    result.Add("\\tweak duration-log #1");
                }
                else if (NoteDuration.Value >= Half && Filled == "yes")
                {
                    result.Add("\\tweak duration-log #2");
                }

                string color = LilyMarkup.ColorToLy(Color);
                if (color != null)
                {
                    result.Add("\\tweak color " + color);
                }

                string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
                if (fontSize != null)
                {
                    result.Add("\\tweak font-size " + fontSize);
                }
            }
        }

        return string.Join(" ", result);
    }

    /// <inheritdoc/>
    public string PostNoteLy(bool isChordElement) => string.Empty;

    /// <inheritdoc/>
    internal override string LyExpression() => PreChordLy();
}

/// <summary>The root or bass of a chord name.</summary>
internal sealed class LilyChordPitch
{
    /// <summary>Builds the pitch.</summary>
    /// <param name="state">The import this pitch belongs to.</param>
    internal LilyChordPitch(MusicXmlImportState state) => State = state;

    /// <summary>The import this pitch belongs to.</summary>
    internal MusicXmlImportState State { get; }

    /// <summary>How far the pitch is altered.</summary>
    internal double Alteration { get; set; }

    /// <summary>Which of the seven steps this is.</summary>
    internal int Step { get; set; }

    /// <summary>This pitch as LilyPond input.</summary>
    /// <returns>The text.</returns>
    /// <remarks>
    /// ⚠ Upstream hands the chord pitch itself to the note-name function, which reads
    /// only the step and the alteration off it. The port hands over a pitch carrying
    /// those two, because the function is typed.
    /// </remarks>
    internal string LyExpression()
    {
        LilyPitch pitch = new LilyPitch(State) { Step = Step, Alteration = Alteration };
        return State.PitchGeneratingFunction(pitch);
    }

    /// <inheritdoc/>
    public override string ToString() => LyExpression();
}

/// <summary>An addition to, or a subtraction from, a chord name.</summary>
internal sealed class LilyChordModification
{
    /// <summary>Builds the modification.</summary>
    /// <param name="state">The import this modification belongs to.</param>
    internal LilyChordModification(MusicXmlImportState state) => State = state;

    /// <summary>The import this modification belongs to.</summary>
    internal MusicXmlImportState State { get; }

    /// <summary>How far the step is altered.</summary>
    /// <remarks>
    /// ⚠ A double rather than an integer: the value comes from
    /// <c>&lt;degree-alter&gt;</c>, which a document may write as <c>1.0</c>. python's
    /// dictionary lookup treats <c>1.0</c> and <c>1</c> as the same key, so the port
    /// compares numerically.
    /// </remarks>
    internal double Alteration { get; set; }

    /// <summary>Which step is modified.</summary>
    internal int Step { get; set; }

    /// <summary>Whether this adds, subtracts, or does nothing.</summary>
    internal int Type { get; set; }

    /// <summary>This modification as LilyPond input.</summary>
    /// <returns>The text.</returns>
    internal string LyExpression()
    {
        if (Type == 0)
        {
            return string.Empty;
        }

        string val = Type switch { 1 => ".", -1 => "^", _ => string.Empty };
        val += Step.ToString(CultureInfo.InvariantCulture);
        val += Alteration == 1 ? "+" : Alteration == -1 ? "-" : string.Empty;
        return val;
    }
}

/// <summary>A chord name.</summary>
internal sealed class LilyChordNameEvent : LilyEvent
{
    /// <summary>Builds the name.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyChordNameEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The chord's root.</summary>
    internal LilyChordPitch Root { get; set; }

    /// <summary>Which kind of chord this is.</summary>
    internal string Kind { get; set; }

    /// <summary>How long the chord lasts.</summary>
    internal LilyDuration Duration { get; set; }

    /// <summary>The additions and subtractions.</summary>
    internal List<LilyChordModification> Modifications { get; } = new List<LilyChordModification>();

    /// <summary>The chord's bass note, when it has one.</summary>
    internal LilyChordPitch Bass { get; set; }

    /// <summary>Records one addition or subtraction.</summary>
    /// <param name="modification">The modification.</param>
    internal void AddModification(LilyChordModification modification)
        => Modifications.Add(modification);

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        if (Root == null)
        {
            return string.Empty;
        }

        string value = Root.LyExpression();
        if (Duration != null)
        {
            value += Duration.LyExpression();
        }

        if (!string.IsNullOrEmpty(Kind))
        {
            value += Kind;
        }

        //If there are modifications, we need a `:' (plain major chords don't have that).
        if (Modifications.Count > 0 && !value.Contains(":"))
        {
            value += ":";
        }

        //First print all additions and changes, then handle all subtractions.
        foreach (LilyChordModification modification in Modifications)
        {
            if (modification.Type == 1)
            {
                //Additions start with `.', but that requires a trailing digit. If none,
                //omit the `.'.
                if (PythonRegex.Search(@":.*?\d$", value).Success)
                {
                    value += modification.LyExpression();
                }
                else
                {
                    value += modification.LyExpression().Substring(1);
                }
            }
        }

        foreach (LilyChordModification modification in Modifications)
        {
            if (modification.Type == -1)
            {
                value += modification.LyExpression();
            }
        }

        if (Bass != null)
        {
            value += "/+" + Bass.LyExpression();
        }

        return value;
    }
}

/// <summary>A single-stem tremolo.</summary>
internal sealed class LilyTremoloEvent : LilyArticulationEvent
{
    /// <summary>Builds the tremolo.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyTremoloEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How many strokes the tremolo is drawn with.</summary>
    internal int Strokes { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The import's <c>LyDur</c> stores the current duration log value.
    /// <list type="bullet">
    /// <item>If it is smaller than 3 — quarter, half, and whole notes —
    /// <c>:(2 ** (2 + number of tremolo strokes))</c> should be appended to the pitch
    /// and duration: one stroke gives <c>c4:8</c>, <c>c2:8</c>, or <c>c1:8</c>; two
    /// strokes give <c>c4:16</c>, and so on.</item>
    /// <item>If it is equal to or greater than 3, we need to make sure that the tremolo
    /// value appended to the pitch and duration is twice the duration for a single
    /// tremolo stroke; each additional stroke doubles it: one stroke gives <c>c8:16</c>,
    /// <c>c16:32</c>, <c>c32:64</c>; two strokes give <c>c8:32</c>, and so on.</item>
    /// </list>
    /// </remarks>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();
        if (Strokes > 0)
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("\\tweak color " + color);
            }

            int exponent = State.LyDur < 3 ? 2 + Strokes : State.LyDur + Strokes;
            result.Add(
                ":" + System.Numerics.BigInteger.Pow(2, exponent)
                    .ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(" ", result);
    }
}

/// <summary>A bend after a note.</summary>
internal sealed class LilyBendEvent : LilyArticulationEvent
{
    /// <summary>Builds the bend.</summary>
    /// <param name="state">The import this event belongs to.</param>
    internal LilyBendEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How far the note bends.</summary>
    /// <remarks>
    /// ⚠ Boxed as python leaves it, because it is PRINTED: <c>-4</c> and <c>4</c> are
    /// integers, and a document's own <c>&lt;bend-alter&gt;</c> is whatever python's
    /// <c>eval</c> made of its text. <c>'%s' % 4</c> is '4' and <c>'%s' % 4.0</c> is
    /// '4.0', and both readings reach the output.
    /// </remarks>
    internal object Alter { get; set; }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();
        if (Alter != null)
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("-\\tweak color " + color);
            }

            result.Add("-\\bendAfter #" + LilyOutputPrinter.FormatNumber(Alter));
        }

        return string.Join(" ", result);
    }
}
