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
using System.Text;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (Base, Music and the wrappers and containers around them);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>Anything that can be written into a LilyPond document.</summary>
/// <remarks>
/// ⚠ THE `make-music' SCHEME PATH IS NOT PORTED, deliberately. Upstream's
/// <c>Music.lisp_expression</c>, <c>Music.name</c>, <c>NestedMusic.get_properties</c>
/// and <c>SequentialMusic.lisp_sub_expression</c> exist but NOTHING in
/// <c>musicxml2ly</c> reaches them; they are a leftover from a shared ancestor of these
/// scripts. Porting them would have meant carrying every class's python NAME as data,
/// since that name is what they print, for output no input can produce. The two
/// <c>lisp_expression</c> methods that ARE reached — the duration's and the pitch's —
/// are ported in full. Recorded in PORT-COVERAGE.
/// </remarks>
internal abstract class LilyExpression
{
    /// <summary>Builds the expression.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    protected LilyExpression(MusicXmlImportState state) => State = state;

    /// <summary>The import this expression belongs to.</summary>
    internal MusicXmlImportState State { get; }

    /// <summary>Whether this expression is, or holds, the given one.</summary>
    /// <param name="element">The expression to look for.</param>
    /// <returns>Whether it is here.</returns>
    internal virtual bool Contains(LilyExpression element) => this == element;

    /// <summary>Writes this expression.</summary>
    /// <param name="printer">Where to write.</param>
    internal abstract void PrintLy(LilyOutputPrinter printer);
}

/// <summary>A music expression.</summary>
internal class LilyMusic : LilyExpression
{
    /// <summary>Builds the expression.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyMusic(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The expression this one sits inside.</summary>
    internal LilyNestedMusic Parent { get; set; }

    /// <summary>The moment this expression starts at.</summary>
    internal PythonFraction Start { get; set; } = PythonFraction.Zero;

    /// <summary>A comment to write above this expression.</summary>
    internal string Comment { get; set; } = string.Empty;

    /// <summary>The name this expression is written under, when it has one.</summary>
    internal string Identifier { get; set; }

    /// <summary>The colour this expression is drawn in.</summary>
    internal string Color { get; set; }

    /// <summary>The font size this expression is drawn at.</summary>
    internal string FontSize { get; set; }

    /// <summary>How long this expression lasts.</summary>
    /// <param name="withFactor">Whether to include any scaling factor.</param>
    /// <returns>The length.</returns>
    internal virtual PythonFraction GetLength(bool withFactor = true) => PythonFraction.Zero;

    /// <summary>Where this expression sits among its siblings.</summary>
    /// <returns>The index, or null when it has no parent.</returns>
    internal int? GetIndex() => Parent?.Elements.IndexOf(this);

    /// <summary>Sets the moment this expression starts at.</summary>
    /// <param name="start">The moment.</param>
    internal virtual void SetStart(PythonFraction start) => Start = start;

    /// <summary>Writes this expression's comment, if it has one.</summary>
    /// <param name="printer">Where to write.</param>
    /// <param name="text">The comment, or null for this expression's own.</param>
    internal void PrintComment(LilyOutputPrinter printer, string text = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            text = Comment;
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (text == "\n")
        {
            printer.Newline();
            return;
        }

        foreach (string line in text.Split('\n'))
        {
            if (line.Length > 0)
            {
                printer.UnformattedOutput("% " + line);
            }

            printer.Newline();
        }
    }

    /// <summary>Writes this expression, or the name it was given.</summary>
    /// <param name="printer">Where to write.</param>
    internal void PrintWithIdentifier(LilyOutputPrinter printer)
    {
        if (!string.IsNullOrEmpty(Identifier))
        {
            printer.Dump("\\" + Identifier);
        }
        else
        {
            PrintLy(printer);
        }
    }

    /// <summary>This expression as LilyPond input.</summary>
    /// <returns>The text.</returns>
    internal virtual string LyExpression() => string.Empty;

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer) => printer.Dump(LyExpression());
}

/// <summary>A music expression that wraps exactly one other.</summary>
internal class LilyMusicWrapper : LilyMusic
{
    /// <summary>Builds the wrapper.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyMusicWrapper(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>What is wrapped.</summary>
    internal LilyMusic Element { get; set; }

    /// <inheritdoc/>
    internal override bool Contains(LilyExpression element)
        => this == element || Element.Contains(element);

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer) => Element.PrintLy(printer);
}

/// <summary>A wrapper that puts the parser into a different mode first.</summary>
internal class LilyModeChangingMusicWrapper : LilyMusicWrapper
{
    /// <summary>Builds the wrapper.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyModeChangingMusicWrapper(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which mode the parser is put into.</summary>
    internal string Mode { get; set; } = "notemode";

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        printer.Dump("\\" + Mode);
        base.PrintLy(printer);
    }
}

/// <summary>A wrapper that writes its contents relative to a starting pitch.</summary>
internal sealed class LilyRelativeMusic : LilyMusicWrapper
{
    /// <summary>Builds the wrapper.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyRelativeMusic(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The pitch the first note is measured against.</summary>
    internal LilyPitch BasePitch { get; set; }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        bool previousRelativePitches = State.RelativePitches;
        State.RelativePitches = true;
        State.PreviousPitch = BasePitch;
        if (State.PreviousPitch == null)
        {
            State.PreviousPitch = new LilyPitch(State);
        }

        printer.Dump("\\relative "
                     + State.PitchGeneratingFunction(State.PreviousPitch)
                     + State.PreviousPitch.AbsolutePitch());
        base.PrintLy(printer);
        State.RelativePitches = previousRelativePitches;
    }
}

/// <summary>A wrapper that scales its contents into a tuplet.</summary>
internal sealed class LilyTimeScaledMusic : LilyMusicWrapper
{
    /// <summary>Builds the wrapper.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyTimeScaledMusic(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How many notes the tuplet holds.</summary>
    internal int Numerator { get; set; } = 1;

    /// <summary>How many notes it is written in the time of.</summary>
    internal int Denominator { get; set; } = 1;

    /// <summary>Which number is drawn: 'actual', 'both' or none.</summary>
    internal string DisplayNumber { get; set; } = "actual";

    /// <summary>Which note value is drawn beside the number: 'actual', 'both' or none.</summary>
    internal string DisplayType { get; set; }

    /// <summary>How the bracket is drawn: 'bracket', 'curved' or none.</summary>
    internal string DisplayBracket { get; set; } = "bracket";

    /// <summary>The actually played unit of the scaling.</summary>
    internal LilyDuration ActualType { get; set; }

    /// <summary>The basic unit of the scaling.</summary>
    internal LilyDuration NormalType { get; set; }

    /// <summary>The numerator to draw, when it is not the real one.</summary>
    internal int? DisplayNumerator { get; set; }

    /// <summary>The denominator to draw, when it is not the real one.</summary>
    internal int? DisplayDenominator { get; set; }

    /// <summary>Which way the bracket is forced to point.</summary>
    internal int ForceDirection { get; set; }

    /// <summary>Whether the tuplet's bracket and number are drawn at all.</summary>
    internal bool Visible { get; set; } = true;

    /// <summary>python's truth test for a count the document may have left out.</summary>
    /// <param name="count">The count.</param>
    /// <returns>Whether it is present and not zero.</returns>
    private static bool IsTruthy(int? count) => count.HasValue && count.Value != 0;

    /// <summary>python's <c>%s</c> for a count the document may have left out.</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    private static string FormatCount(int? count)
        => count.HasValue ? count.Value.ToString(CultureInfo.InvariantCulture) : "None";

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (DisplayBracket == null)
        {
            printer.Dump("\\tweak TupletBracket.stencil ##f");
        }
        else if (DisplayBracket == "curved")
        {
            printer.Dump("\\tweak TupletBracket.tuplet-slur ##t");
        }

        string color = LilyMarkup.ColorToLy(Color);
        if (color != null)
        {
            printer.Dump("\\tweak TupletNumber.color " + color);
        }

        string fontSize = LilyMarkup.GetFontSize(State, FontSize, command: false);
        if (fontSize != null)
        {
            printer.Dump("\\tweak TupletNumber.font-size " + fontSize);
        }

        string direction = ForceDirection == -1
            ? "\\tweak TupletBracket.direction #DOWN"
            : ForceDirection == 1
                ? "\\tweak TupletBracket.direction #UP"
                : string.Empty;
        if (direction.Length > 0)
        {
            printer.Dump(direction);
        }

        string baseNumberFunction =
            DisplayNumber == null ? "#f"
            : DisplayNumber == "actual" ? "tuplet-number::calc-denominator-text"
            : DisplayNumber == "both" ? "tuplet-number::calc-fraction-text"
            : null;

        //If we have non-standard numerator/denominator, use our custom function
        if (DisplayNumber == "actual" && IsTruthy(DisplayDenominator))
        {
            baseNumberFunction =
                "(tuplet-number::non-default-tuplet-denominator-text "
                + FormatCount(DisplayDenominator) + ")";
        }
        else if (DisplayNumber == "both"
                 && (IsTruthy(DisplayDenominator) || IsTruthy(DisplayNumerator)))
        {
            string num = IsTruthy(DisplayNumerator)
                ? FormatCount(DisplayNumerator) : "#f";
            string den = IsTruthy(DisplayDenominator)
                ? FormatCount(DisplayDenominator) : "#f";
            baseNumberFunction =
                "(tuplet-number::non-default-tuplet-fraction-text " + den + " " + num + ")";
        }

        if (DisplayType == "actual" && NormalType != null)
        {
            string baseDuration = NormalType.LispExpression();
            printer.Dump("\\tweak TupletNumber.text");
            printer.Dump("#(tuplet-number::append-note-wrapper "
                         + baseNumberFunction + " " + baseDuration + ")");
        }
        else if (DisplayType == "both")
        {
            //TODO (upstream's): Implement this using actual_type and normal_type!
            if (DisplayNumber == null)
            {
                printer.Dump("\\tweak TupletNumber.stencil ##f");
            }
            else if (DisplayNumber == "both")
            {
                string denDuration = NormalType.LispExpression();
                //If we don't have an actual type set, use the normal duration!
                string numDuration = ActualType != null
                    ? ActualType.LispExpression()
                    : denDuration;
                if (IsTruthy(DisplayDenominator) || IsTruthy(DisplayNumerator))
                {
                    printer.Dump("\\tweak TupletNumber.text");
                    printer.Dump(
                        "#(tuplet-number::non-default-fraction-with-notes "
                        + FormatCount(DisplayDenominator) + " " + denDuration + " "
                        + FormatCount(DisplayNumerator) + " " + numDuration + ")");
                }
                else
                {
                    printer.Dump("\\tweak TupletNumber.text");
                    printer.Dump("#(tuplet-number::fraction-with-notes "
                                 + denDuration + " " + numDuration + ")");
                }
            }
        }
        else
        {
            if (DisplayNumber == null)
            {
                printer.Dump("\\tweak TupletNumber.stencil ##f");
            }
            else if (DisplayNumber == "both"
                     || !((DisplayDenominator == null || DisplayDenominator == Denominator)
                          && (DisplayNumerator == null || DisplayNumerator == Numerator)))
            {
                printer.Dump("\\tweak TupletNumber.text #" + baseNumberFunction);
            }
        }

        if (!Visible)
        {
            printer.Dump("\\tweak TupletBracket.transparent ##t");
            printer.Dump("\\tweak TupletNumber.transparent ##t");
        }

        printer.Dump("\\tuplet");
        printer.PrintVerbatim(" " + Denominator.ToString(CultureInfo.InvariantCulture)
                              + "/" + Numerator.ToString(CultureInfo.InvariantCulture));
        printer.AddFactor(new PythonFraction(Numerator, Denominator));
        base.PrintLy(printer);
        printer.Revert();
    }
}

/// <summary>A music expression holding a list of others.</summary>
internal class LilyNestedMusic : LilyMusic
{
    /// <summary>Builds the expression.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyNestedMusic(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>What this expression holds.</summary>
    internal List<LilyMusic> Elements { get; set; } = new List<LilyMusic>();

    /// <summary>Adds one expression, unless there is nothing to add.</summary>
    /// <param name="what">The expression.</param>
    internal void Append(LilyMusic what)
    {
        if (what != null)
        {
            Elements.Add(what);
        }
    }

    /// <inheritdoc/>
    internal override bool Contains(LilyExpression element)
    {
        if (this == element)
        {
            return true;
        }

        foreach (LilyMusic child in Elements)
        {
            if (child.Contains(element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts one expression before or after another.</summary>
    /// <param name="successor">The expression to place against, or null for an end.</param>
    /// <param name="element">The expression to place.</param>
    /// <param name="direction">Which side to place it on.</param>
    internal void InsertAround(LilyMusic successor, LilyMusic element, int direction)
    {
        int index = 0;
        if (successor != null)
        {
            index = Elements.IndexOf(successor);
            if (direction > 0)
            {
                index += 1;
            }
        }
        else if (direction > 0)
        {
            index = Elements.Count;
        }

        Elements.Insert(index, element);
        element.Parent = this;
    }

    /// <summary>The expression beside a given one.</summary>
    /// <param name="music">The expression to start from.</param>
    /// <param name="direction">Which side to look.</param>
    /// <returns>The neighbour, clamped to the ends.</returns>
    internal LilyMusic GetNeighbor(LilyMusic music, int direction)
    {
        int index = Elements.IndexOf(music) + direction;
        index = Math.Min(index, Elements.Count - 1);
        index = Math.Max(index, 0);
        return Elements[index];
    }

    /// <summary>Takes one expression out.</summary>
    /// <param name="element">The expression.</param>
    internal void DeleteElement(LilyMusic element)
    {
        Elements.Remove(element);
        element.Parent = null;
    }

    /// <inheritdoc/>
    internal override void SetStart(PythonFraction start)
    {
        Start = start;
        foreach (LilyMusic child in Elements)
        {
            child.SetStart(start);
        }
    }
}

/// <summary>A braced run of music expressions.</summary>
internal class LilySequentialMusic : LilyNestedMusic
{
    /// <summary>Builds the expression.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilySequentialMusic(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The last chord written, if the run ends in one.</summary>
    /// <returns>The chord, or null.</returns>
    internal LilyChordEvent GetLastEventChord()
    {
        int at = Elements.Count - 1;
        while (at >= 0
               && !(Elements[at] is LilyChordEvent || Elements[at] is LilyBarLine))
        {
            at -= 1;
        }

        return at >= 0 ? Elements[at] as LilyChordEvent : null;
    }

    /// <summary>Writes this run.</summary>
    /// <param name="printer">Where to write.</param>
    /// <param name="newline">Whether to break the line around the braces.</param>
    /// <param name="closing">Whether to write the closing brace.</param>
    internal void PrintLy(LilyOutputPrinter printer, bool newline = true, bool closing = true)
    {
        printer.Dump("{");
        if (!string.IsNullOrEmpty(Comment))
        {
            PrintComment(printer);
        }

        if (newline)
        {
            printer.Newline();
        }

        foreach (LilyMusic child in Elements)
        {
            child.PrintLy(printer);
        }

        if (closing)
        {
            printer.Dump("}");
            if (newline)
            {
                printer.Newline();
            }
        }
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer) => PrintLy(printer, true, true);

    /// <inheritdoc/>
    internal override void SetStart(PythonFraction start)
    {
        foreach (LilyMusic child in Elements)
        {
            child.SetStart(start);
            start += child.GetLength();
        }
    }
}

/// <summary>The command that selects one of a staff's numbered voices.</summary>
internal sealed class LilyVoiceSelector : LilyMusic
{
    /// <summary>Builds the selector.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    /// <param name="voice">Which voice.</param>
    /// <remarks>
    /// The clamp to four must stay in sync with the similar code in the staff's own
    /// contents printer.
    /// </remarks>
    internal LilyVoiceSelector(MusicXmlImportState state, int voice)
        : base(state)
        => Voice = Math.Min(voice, 4);

    /// <summary>Which voice.</summary>
    internal int Voice { get; }

    /// <inheritdoc/>
    internal override string LyExpression() => LilyStaff.VoiceTextDict[Voice];
}

/// <summary>How a volta's number and bracket are drawn.</summary>
internal sealed class LilyVoltaStyleEvent : LilyMusic
{
    /// <summary>Builds the event.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyVoltaStyleEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The element the text is read from, and how it is drawn.</summary>
    internal LilyMarkupElement Element { get; set; }

    /// <summary>Whether the bracket is drawn at all.</summary>
    internal bool Visible { get; set; } = true;

    /// <summary>The overrides this event needs.</summary>
    /// <returns>The overrides, one per line.</returns>
    internal List<string> VoltaStyleToLy()
    {
        List<string> answer = new List<string>();

        //We can't use `\tweak' here.
        string textMarkup = LilyMarkup.TextToLy(
            State, new List<LilyMarkupElement> { Element });
        if (!string.IsNullOrEmpty(textMarkup))
        {
            answer.Add("\\once \\override Score.VoltaBracket.text = \\markup " + textMarkup);
        }

        //TODO (upstream's): Handle `number' attribute.
        if (Visible)
        {
            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                answer.Add("\\once \\override Score.VoltaBracket.color = " + color);
            }
        }
        else
        {
            answer.Add("\\once \\override Score.VoltaBracket.transparent = ##t");
        }

        return answer;
    }

    /// <inheritdoc/>
    internal override string LyExpression() => string.Join(" ", VoltaStyleToLy());

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        foreach (string line in VoltaStyleToLy())
        {
            printer.Dump(line);
        }
    }
}

/// <summary>A passage that is played more than once.</summary>
/// <remarks>
/// ⚠ Upstream derives this from <c>Base</c> rather than from <c>Music</c>, yet puts it
/// straight into a voice's music list beside chords and bar lines, and into a sequential
/// music's elements — python does not mind. C# does, so the port derives it from the
/// music expression, which is what every list holding it is typed as. The two members it
/// gains that <c>Base</c> lacks, a length and a start, are never asked of a repeat by
/// <c>musicxml2ly</c>: every <c>get_length</c> call reaches a chord or a duration, and
/// <c>set_start</c> is never called at all. Recorded in PORT-COVERAGE.
/// </remarks>
internal sealed class LilyRepeatedMusic : LilyMusic
{
    /// <summary>Builds the repeat.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyRepeatedMusic(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>What kind of repeat.</summary>
    internal string RepeatType { get; set; } = "volta";

    /// <summary>How many times it is played.</summary>
    internal object RepeatCount { get; set; } = 2;

    /// <summary>The alternative endings.</summary>
    internal List<(List<int> Volte, LilySequentialMusic Music)> Endings { get; }
        = new List<(List<int>, LilySequentialMusic)>();

    /// <summary>How many strokes a tremolo's beams carry.</summary>
    internal object TremoloStrokes { get; set; }

    /// <summary>The passage itself.</summary>
    internal LilySequentialMusic Music { get; set; }

    /// <summary>Sets the passage.</summary>
    /// <param name="music">The passage.</param>
    internal void SetMusic(LilySequentialMusic music) => Music = music;

    /// <summary>Sets the passage from a list of expressions.</summary>
    /// <param name="music">The expressions.</param>
    internal void SetMusic(List<LilyMusic> music)
    {
        LilySequentialMusic sequential = new LilySequentialMusic(State);
        sequential.Elements = music;
        Music = sequential;
    }

    /// <summary>Adds one alternative ending.</summary>
    /// <param name="volte">Which times through it applies to.</param>
    /// <param name="music">The ending.</param>
    internal void AddEnding(List<int> volte, LilySequentialMusic music)
        => Endings.Add((volte, music));

    /// <inheritdoc/>
    internal override bool Contains(LilyExpression element)
        => this == element || Music.Contains(element);

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        bool isTremolo = RepeatType == "tremolo";

        if (TremoloStrokes != null)
        {
            //We can't use `\tweak' here.
            printer.Dump("\\once \\override Beam.gap-count = "
                         + LilyOutputPrinter.FormatNumber(TremoloStrokes));
        }

        if (isTremolo && Color != null)
        {
            printer.Dump("\\once \\override Beam.color = " + LilyMarkup.ColorToLy(Color));
        }

        string header = "\\repeat " + RepeatType + " "
                        + LilyOutputPrinter.FormatNumber(RepeatCount);

        if (isTremolo)
        {
            printer.Dump(header);
            if (Music != null)
            {
                Music.PrintLy(printer, newline: false);
            }
            else
            {
                State.Warning("encountered tremolo repeat without body");
                printer.Dump("{}");
            }

            return;
        }

        printer.Dump(header);
        if (Music != null)
        {
            Music.PrintLy(printer, closing: false);
        }
        else
        {
            State.Warning("encountered volta repeat without body");
            printer.Dump("{");
        }

        if (Endings.Count > 0)
        {
            printer.Dump("\\alternative {");
            printer.Newline();
            foreach ((List<int> volte, LilySequentialMusic ending) in Endings)
            {
                List<string> numbers = new List<string>();
                foreach (int v in volte)
                {
                    numbers.Add(v.ToString(CultureInfo.InvariantCulture));
                }

                printer.Dump("\\volta " + string.Join(",", numbers));
                ending.PrintLy(printer);
            }

            printer.Dump("}");
            printer.Newline();
        }

        printer.Dump("}");
        printer.Newline();
    }
}

/// <summary>One voice's worth of lyrics.</summary>
internal sealed class LilyLyrics : LilyExpression
{
    /// <summary>Builds the lyrics.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyLyrics(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The syllables, already formatted.</summary>
    internal List<string> LyricsSyllables { get; } = new List<string>();

    /// <summary>Which stanza these lyrics are.</summary>
    internal string StanzaId { get; set; }

    /// <summary>Where the lyrics are drawn.</summary>
    internal string Placement { get; set; }

    /// <summary>The three pieces the lyrics block is made of.</summary>
    /// <returns>The two settings and the syllables.</returns>
    internal (string NoMelismata, string IncludeGraceNotes, string Lyrics) LyricsToLy()
    {
        StringBuilder lyrics = new StringBuilder();
        foreach (string syllable in LyricsSyllables)
        {
            lyrics.Append(syllable);
        }

        return ("\\set ignoreMelismata = ##t", "\\set includeGraceNotes = ##t",
                lyrics.ToString());
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        (string noMelismata, string includeGraceNotes, string lyrics) = LyricsToLy();

        printer.Dump("\\lyricmode {");
        printer.Newline();

        printer.Dump(noMelismata);
        printer.Newline();
        printer.Dump(includeGraceNotes);
        printer.Newline();

        printer.DumpLyrics(lyrics);
        printer.Newline();

        printer.Dump("}");
        printer.Newline();
    }

    /// <summary>These lyrics as LilyPond input.</summary>
    /// <returns>The text.</returns>
    internal string LyExpression()
    {
        (string noMelismata, string includeGraceNotes, string lyrics) = LyricsToLy();
        return noMelismata + " " + includeGraceNotes + " " + lyrics;
    }
}

/// <summary>The document's header block.</summary>
internal sealed class LilyHeader : LilyExpression
{
    /// <summary>Builds the header.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyHeader(MusicXmlImportState state)
        : base(state)
    {
    }

    private readonly List<string> _fieldOrder = new List<string>();

    private readonly Dictionary<string, string> _headerFields
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether the header has anything in it.</summary>
    internal bool HasFields => _headerFields.Count > 0;

    /// <summary>Sets one header field.</summary>
    /// <param name="field">The field name.</param>
    /// <param name="value">The value.</param>
    internal void SetField(string field, string value)
    {
        if (!_headerFields.ContainsKey(field))
        {
            _fieldOrder.Add(field);
        }

        _headerFields[field] = value;
    }

    /// <summary>Writes one header field.</summary>
    /// <param name="key">The field name.</param>
    /// <param name="value">The value.</param>
    /// <param name="printer">Where to write.</param>
    /// <remarks>
    /// If a header item contains a line break, it is segmented. The substrings are
    /// formatted with the help of markup, using column and line. An exception, however,
    /// are texidoc items, which should not contain LilyPond formatting commands.
    /// </remarks>
    internal void FormatHeaderStrings(string key, string value, LilyOutputPrinter printer)
    {
        printer.Dump(key + " =");

        if (key == "texidoc")
        {
            printer.DumpTexidoc(value);
        }
        else if (value.Contains("\n"))
        {
            value = value.Replace("\"", string.Empty);
            printer.Dump("\\markup \\column {");
            foreach (string substring in value.Split('\n'))
            {
                printer.Newline();
                printer.Dump("\\line { \"" + substring + "\" }");
            }

            printer.Newline();
            printer.Dump("}");
            printer.PrintVerbatim("\n");
        }
        else
        {
            printer.Dump(value);
        }

        printer.Newline();
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        printer.Dump("\\header {");
        printer.Newline();
        foreach (string key in _fieldOrder)
        {
            string value = _headerFields[key];
            if (!string.IsNullOrEmpty(value))
            {
                FormatHeaderStrings(key, value, printer);
            }
        }

        printer.Dump("}");
        printer.PrintVerbatim("\n");
        printer.Newline();
    }
}
