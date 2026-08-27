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

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicxml.py (the element classes);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>The work element.</summary>
internal sealed class MusicXmlWork : MusicXmlNode
{
    /// <summary>Reads one of the work's fields.</summary>
    /// <param name="tag">The element name.</param>
    /// <returns>The text, or empty.</returns>
    internal string GetWorkInformation(string tag)
    {
        MusicXmlNode child = GetMaybeExistNamedChild(tag);
        return child != null ? child.GetText() : string.Empty;
    }

    /// <summary>The work's title.</summary>
    /// <returns>The title.</returns>
    internal string GetWorkTitle() => GetWorkInformation("work-title");

    /// <summary>The work's number.</summary>
    /// <returns>The number.</returns>
    internal string GetWorkNumber() => GetWorkInformation("work-number");
}

/// <summary>The identification element.</summary>
internal sealed class MusicXmlIdentification : MusicXmlNode
{
    /// <summary>The rights statements, one per line.</summary>
    /// <returns>The text.</returns>
    internal string GetRights()
    {
        List<MusicXmlNode> rights = GetNamedChildren("rights");
        List<string> answer = new List<string>();
        foreach (MusicXmlNode r in rights)
        {
            string text = r.GetText();
            //If this node has a 'type' attribute such as `type="words"', include it
            //in the return value. Otherwise it is assumed that the text contents of
            //this node looks something like this: 'Copyright: X.Y.' and thus already
            //contains the relevant information.
            string rightsType = r.Attribute("type");
            if (rightsType != null)
            {
                answer.Add(TitleCase(rightsType) + ": " + text);
            }
            else
            {
                answer.Add(text);
            }
        }

        return string.Join("\n", answer);
    }

    /// <summary>
    /// python's <c>str.title()</c>: every run of letters gets a capital first letter
    /// and a lower-case rest.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The title-cased text.</returns>
    private static string TitleCase(string text)
    {
        char[] characters = text.ToCharArray();
        bool previousWasLetter = false;
        for (int i = 0; i < characters.Length; i++)
        {
            bool isLetter = char.IsLetter(characters[i]);
            characters[i] = isLetter && !previousWasLetter
                ? char.ToUpperInvariant(characters[i])
                : char.ToLowerInvariant(characters[i]);
            previousWasLetter = isLetter;
        }

        return new string(characters);
    }

    /// <summary>The source statement.</summary>
    /// <returns>The text, or empty.</returns>
    internal string GetSource()
    {
        MusicXmlNode source = GetMaybeExistNamedChild("source");
        return source != null ? source.GetText() : string.Empty;
    }

    /// <summary>Every creator of one kind, one per line.</summary>
    /// <param name="type">The creator type.</param>
    /// <returns>The text.</returns>
    internal string GetCreator(string type)
    {
        List<MusicXmlNode> creators = GetNamedChildren("creator");
        List<string> answer = new List<string>();
        foreach (MusicXmlNode creator in creators)
        {
            if (creator.Attribute("type") == type)
            {
                string text = creator.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    answer.Add(text);
                }
            }
        }

        return string.Join("\n", answer);
    }

    /// <summary>The composer.</summary>
    /// <returns>The name, or null.</returns>
    internal string GetComposer()
    {
        string composer = GetCreator("composer");
        if (!string.IsNullOrEmpty(composer))
        {
            return composer;
        }

        //XXX Why a heuristic second try?
        List<MusicXmlNode> creators = GetNamedChildren("creator");
        //Return the first `<creator>' element that has no type.
        foreach (MusicXmlNode creator in creators)
        {
            if (!creator.HasAttribute("type"))
            {
                return creator.GetText();
            }
        }

        return null;
    }

    /// <summary>The arranger.</summary>
    /// <returns>The name.</returns>
    internal string GetArranger() => GetCreator("arranger");

    /// <summary>The editor.</summary>
    /// <returns>The name.</returns>
    internal string GetEditor() => GetCreator("editor");

    /// <summary>The poet, however the document named the role.</summary>
    /// <returns>The name.</returns>
    internal string GetPoet()
    {
        string value = GetCreator("lyricist");
        return !string.IsNullOrEmpty(value) ? value : GetCreator("poet");
    }

    /// <summary>One of the encoding's fields, one entry per line.</summary>
    /// <param name="type">The element name.</param>
    /// <returns>The text, or empty.</returns>
    internal string GetEncodingInformation(string type)
    {
        MusicXmlNode encoding = GetMaybeExistNamedChild("encoding");
        if (encoding != null)
        {
            List<MusicXmlNode> children = encoding.GetNamedChildren(type);
            List<string> answer = new List<string>();
            foreach (MusicXmlNode child in children)
            {
                string text = child.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    answer.Add(text);
                }
            }

            return string.Join("\n", answer);
        }

        return string.Empty;
    }

    /// <summary>What wrote the file.</summary>
    /// <returns>The text.</returns>
    internal string GetEncodingSoftware() => GetEncodingInformation("software");

    /// <summary>When the file was written.</summary>
    /// <returns>The text.</returns>
    internal string GetEncodingDate() => GetEncodingInformation("encoding-date");

    /// <summary>Who wrote the file.</summary>
    /// <returns>The text.</returns>
    internal string GetEncodingPerson() => GetEncodingInformation("encoder");

    /// <summary>What the encoding says about itself.</summary>
    /// <returns>The text.</returns>
    internal string GetEncodingDescription()
        => GetEncodingInformation("encoding-description");

    /// <summary>The miscellaneous field named 'description'.</summary>
    /// <returns>The text, or empty.</returns>
    internal string GetFileDescription()
    {
        MusicXmlNode misc = GetMaybeExistNamedChild("miscellaneous");
        if (misc != null)
        {
            foreach (MusicXmlNode field in misc.GetNamedChildren("miscellaneous-field"))
            {
                if (field.Attribute("name") == "description")
                {
                    return field.GetText();
                }
            }
        }

        return string.Empty;
    }
}

/// <summary>
/// The credit elements of a page, read together so that each one can be placed
/// relative to the others.
/// </summary>
internal sealed class MusicXmlCreditGroup
{
    /// <summary>Reads the group's shared measurements.</summary>
    /// <param name="credits">The credits of one page.</param>
    internal MusicXmlCreditGroup(IEnumerable<MusicXmlCredit> credits)
    {
        //Collect 'font-size', 'default-x', and 'default-y' attribute values of the
        //first `<credit-words>' child of all `<credit>' elements.
        foreach (MusicXmlCredit credit in credits)
        {
            MusicXmlNode words = credit.GetFirstCreditWords();

            string text = words?.Attribute("font-size");
            if (text != null)
            {
                WordsFontSizes.Add((int)ParseDouble(text));
            }

            text = words?.Attribute("default-x");
            if (text != null)
            {
                WordsDefaultXs.Add(MusicXmlUtilities.PythonRound(ParseDouble(text)));
            }

            text = words?.Attribute("default-y");
            if (text != null)
            {
                WordsDefaultYs.Add(MusicXmlUtilities.PythonRound(ParseDouble(text)));
            }
        }

        WordsFontSizes.Sort((a, b) => b.CompareTo(a));
        //Coordinates are relative to the bottom-left corner of a page.
        WordsDefaultXs.Sort((a, b) => b.CompareTo(a));
        WordsDefaultYs.Sort((a, b) => b.CompareTo(a));
    }

    private static double ParseDouble(string text)
        => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>The font sizes seen, largest first.</summary>
    internal List<int> WordsFontSizes { get; } = new List<int>();

    /// <summary>The horizontal positions seen, rightmost first.</summary>
    internal List<double> WordsDefaultXs { get; } = new List<double>();

    /// <summary>The vertical positions seen, topmost first.</summary>
    internal List<double> WordsDefaultYs { get; } = new List<double>();
}

/// <summary>The credit element.</summary>
internal sealed class MusicXmlCredit : MusicXmlNode
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int> { { "credit-words", 2 } };

    /// <summary>Builds the element.</summary>
    internal MusicXmlCredit() => Content["credit-words"] = new List<MusicXmlNode>();

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>What the document says this credit is.</summary>
    /// <returns>The types, comma separated.</returns>
    internal string GetCreditType()
    {
        List<string> values = new List<string>();
        foreach (MusicXmlNode type in GetNamedChildren("credit-type"))
        {
            values.Add(type.GetText());
        }

        //The choice of using ', ' as a separator between multiple credit types in
        //the return value is arbitrary.
        return string.Join(", ", values);
    }

    /// <summary>The first block of words this credit carries.</summary>
    /// <returns>The element, or null.</returns>
    internal MusicXmlNode GetFirstCreditWords()
    {
        List<MusicXmlNode> words = GetList("credit-words");
        return words.Count > 0 ? words[0] : null;
    }

    /// <summary>
    /// Applies heuristics to find out where this credit is positioned on a page and
    /// what it does, then tries to derive a proper type for it.
    /// </summary>
    /// <param name="creditGroup">What the page's other credits measured.</param>
    /// <returns>The derived type, or empty when none was recognised.</returns>
    internal string FindType(MusicXmlCreditGroup creditGroup)
    {
        //Collect various attribute values of the first `<credit-words>' child of the
        //current `<credit>' element.
        MusicXmlNode words = GetFirstCreditWords();

        string sizeText = words?.Attribute("font-size");
        int? size = sizeText != null ? (int)ParseDouble(sizeText) : (int?)null;

        string xText = words?.Attribute("default-x");
        double? x = xText != null
            ? MusicXmlUtilities.PythonRound(ParseDouble(xText))
            : (double?)null;

        string yText = words?.Attribute("default-y");
        double? y = yText != null
            ? MusicXmlUtilities.PythonRound(ParseDouble(yText))
            : (double?)null;

        string justify = words?.Attribute("justify", "left") ?? "left";
        //The standard says that if the 'halign' attribute is not present, it takes
        //its value from the 'justify' attribute.
        string halign = words?.Attribute("halign", justify) ?? justify;
        string valign = words?.Attribute("valign");

        List<int> fontSizes = creditGroup.WordsFontSizes;
        List<double> xs = creditGroup.WordsDefaultXs;
        List<double> ys = creditGroup.WordsDefaultYs;

        //EVERY TEST BELOW IS python's TRUTH TEST, so a measured ZERO counts as
        //absent: `size and size == ...' is false for a font size of 0 and for a y
        //of 0.0 alike. Keeping that is the difference between deriving a type and
        //not, so the port spells it out rather than relying on a null check.
        bool sizeTruthy = size.HasValue && size.Value != 0;
        bool xTruthy = x.HasValue && x.Value != 0;
        bool yTruthy = y.HasValue && y.Value != 0;

        //The arrays in `creditGroup' are sorted in reverse order.
        if (sizeTruthy && fontSizes.Count > 0 && size.Value == fontSizes[0]
            && yTruthy && ys.Count > 0 && y.Value == ys[0]
            && halign == "center")
        {
            return "title";
        }

        if (yTruthy && ys.Count > 0 && y.Value > ys[ys.Count - 1] && y.Value < ys[0]
            && halign == "center")
        {
            return "subtitle";
        }

        if (halign == "left" && (!xTruthy || (xs.Count > 0 && x.Value == xs[xs.Count - 1])))
        {
            return "lyricist";
        }

        if (halign == "right" && (!xTruthy || (xs.Count > 0 && x.Value == xs[0])))
        {
            return "composer";
        }

        if (sizeTruthy && fontSizes.Count > 0 && size.Value == fontSizes[fontSizes.Count - 1]
            && ys.Count > 0 && y.HasValue && y.Value == ys[ys.Count - 1])
        {
            return "rights";
        }

        //Special cases for Finale NotePad.
        if (valign == "top" && yTruthy && ys.Count > 1 && y.Value == ys[1])
        {
            return "subtitle";
        }

        if (valign == "top" && xTruthy && xs.Count > 0 && x.Value == xs[xs.Count - 1])
        {
            return "lyricist";
        }

        if (valign == "top" && yTruthy && ys.Count > 0 && y.Value == ys[ys.Count - 1])
        {
            return "rights";
        }

        //Other special cases.
        if (valign == "bottom")
        {
            return "rights";
        }

        if (y.HasValue && ys.Count(item => item == y.Value) == 2)
        {
            //The first one is the composer, the second one is the lyricist.
            return "composer";
        }

        return string.Empty; //No type recognized.
    }

    private static double ParseDouble(string text)
        => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>A text node.</summary>
internal sealed class MusicXmlHashText : MusicXmlMusicNode
{
}

/// <summary>A comment node.</summary>
internal sealed class MusicXmlHashComment : MusicXmlMusicNode
{
}

/// <summary>The pitch element.</summary>
internal sealed class MusicXmlPitch : MusicXmlMusicNode
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int>
        {
            { "alter", 1 },
            { "octave", 1 },
            { "step", 1 },
        };

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>Turns this into an output-side pitch.</summary>
    /// <returns>The pitch.</returns>
    internal LilyPitch ToLilyObject()
    {
        LilyPitch pitch = new LilyPitch(State);
        pitch.Alteration = Convert.ToDouble(Get("alter", 0), CultureInfo.InvariantCulture);
        pitch.Step = MusicXmlConversion.MusicXmlStepToLily(GetString("step")).Value;
        pitch.Octave = GetInt("octave") - 4;
        return pitch;
    }
}

/// <summary>The unpitched element.</summary>
internal sealed class MusicXmlUnpitched : MusicXmlMusicNode
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int>
        {
            { "display-octave", 1 },
            { "display-step", 1 },
        };

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>Turns this into an output-side pitch.</summary>
    /// <param name="clef">The clef in force, which decides where an unplaced note sits.</param>
    /// <returns>The pitch.</returns>
    internal LilyPitch ToLilyObject(LilyClefChange clef)
    {
        //Unpitched elements can also have `<display-step>' and `<display-octave>'
        //elements.
        LilyPitch pitch = new LilyPitch(State);
        string step = GetString("display-step");
        if (!string.IsNullOrEmpty(step))
        {
            pitch.Step = MusicXmlConversion.MusicXmlStepToLily(step).Value;
            //If `<display-step>' is present, `<display-octave>' must be present, too.
            pitch.Octave = GetInt("display-octave") - 4;
        }
        else
        {
            //We have to position the note on the middle line (or gap) of the staff.
            pitch = clef.Pitch;
        }

        return pitch;
    }
}

/// <summary>The arpeggiate element.</summary>
internal sealed class MusicXmlArpeggiate : MusicXmlMusicNode
{
}

/// <summary>The non-arpeggiate element.</summary>
internal sealed class MusicXmlNonArpeggiate : MusicXmlMusicNode
{
}

/// <summary>The accidental element.</summary>
internal sealed class MusicXmlAccidental : MusicXmlMusicNode
{
}

/// <summary>The backup element.</summary>
internal sealed class MusicXmlBackup : MusicXmlMeasureElement
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int> { { "duration", 1 } };

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;
}

/// <summary>A partial measure the converter synthesises for a pickup.</summary>
internal sealed class MusicXmlPartial : MusicXmlMeasureElement
{
    /// <summary>Builds the element.</summary>
    /// <param name="partial">How long the pickup is.</param>
    internal MusicXmlPartial(PythonFraction partial) => PartialLength = partial;

    /// <summary>How long the pickup is.</summary>
    internal PythonFraction PartialLength { get; }
}

/// <summary>The stem element.</summary>
internal sealed class MusicXmlStem : MusicXmlMusicNode
{
    /// <summary>Turns this into an output-side stem event.</summary>
    /// <param name="noteColor">The note's colour, which the stem inherits.</param>
    /// <param name="isRest">Whether the stem belongs to a rest.</param>
    /// <param name="convertStemDirections">Whether directions are wanted at all.</param>
    /// <returns>The event, or null when there is nothing to say.</returns>
    internal LilyStemEvent ToStemEvent(
        string noteColor = null, bool isRest = false, bool convertStemDirections = true)
    {
        LilyStemEvent stemEvent = new LilyStemEvent(State);
        //MusicXML 4.0 doesn't provide a means to control the color of the flag
        //separately. In LilyPond, `Stem.color' by default controls the color of the
        //flag, too.
        stemEvent.Color = Attribute("color", noteColor);
        stemEvent.IsStemlet = isRest;

        string value = GetText().Trim();
        //Only catch 'up' and 'down' with the command-line option.
        if (convertStemDirections || value == "none")
        {
            stemEvent.Value = value;
            if (value == "down" || value == "up")
            {
                State.HaveStemDirections = true;
            }
        }

        if (stemEvent.Value != null || stemEvent.Color != null || stemEvent.IsStemlet)
        {
            return stemEvent;
        }

        return null;
    }
}

/// <summary>The notehead element.</summary>
internal sealed class MusicXmlNotehead : MusicXmlMusicNode
{
    /// <summary>Turns this into the output-side events that shape a note head.</summary>
    /// <param name="noteColor">The note's colour.</param>
    /// <param name="noteFontSize">The note's font size.</param>
    /// <returns>The events.</returns>
    internal List<LilyExpression> ToLilyObject(
        string noteColor = null, string noteFontSize = null)
    {
        List<LilyExpression> styles = new List<LilyExpression>();

        //Note head style.
        LilyNoteStyleEvent styleEvent = new LilyNoteStyleEvent(State);
        styleEvent.Style = GetText().Trim();
        styleEvent.Color = Attribute("color", noteColor);
        styleEvent.FontSize = Attribute("font-size", noteFontSize);
        styleEvent.NoteDuration = DurationValue;
        styleEvent.Filled = Attribute("filled");

        if (!string.IsNullOrEmpty(styleEvent.Style)
            || styleEvent.Filled != null
            || styleEvent.Color != null
            || styleEvent.FontSize != null)
        {
            styles.Add(styleEvent);
        }

        //Parentheses.
        if (Attribute("parentheses") == "yes")
        {
            styles.Add(new LilyParenthesizeEvent(State));
        }

        return styles;
    }
}

/// <summary>The part-list element.</summary>
internal sealed class MusicXmlPartList : MusicXmlMusicNode
{
    private Dictionary<string, string> _idInstrumentNameDict
        = new Dictionary<string, string>();

    private void GenerateIdInstrumentDict()
    {
        //not empty to make sure this happens only once.
        Dictionary<string, string> mapping
            = new Dictionary<string, string> { { string.Empty, string.Empty } };
        foreach (MusicXmlNode scorePart in GetNamedChildren("score-part"))
        {
            foreach (MusicXmlNode instrument
                     in scorePart.GetNamedChildren("score-instrument"))
            {
                string id = instrument.Attribute("id");
                MusicXmlNode name = instrument.GetNamedChild("instrument-name");
                mapping[id ?? string.Empty] = name.GetText();
            }
        }

        _idInstrumentNameDict = mapping;
    }

    /// <summary>The instrument a score-instrument identifier names.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The instrument name.</returns>
    internal string GetInstrument(string id)
    {
        if (_idInstrumentNameDict.Count == 0)
        {
            GenerateIdInstrumentDict();
        }

        if (id != null
            && _idInstrumentNameDict.TryGetValue(id, out string instrumentName)
            && !string.IsNullOrEmpty(instrumentName))
        {
            return instrumentName;
        }

        State.Warning("Unable to find instrument for ID=" + id + "\n");
        return "Grand Piano";
    }
}

/// <summary>The measure element.</summary>
internal sealed class MusicXmlMeasure : MusicXmlMusicNode
{
    /// <summary>How long the pickup at the start of this measure is.</summary>
    internal PythonFraction Partial { get; set; } = PythonFraction.Zero;

    /// <summary>How long this measure is, from the time signature.</summary>
    /// <remarks>Negative in 'senza misura' mode.</remarks>
    internal PythonFraction Length { get; set; } = PythonFraction.Zero;

    /// <summary>The sum of the elements' durations.</summary>
    internal PythonFraction RealLength { get; set; } = PythonFraction.Zero;

    /// <summary>Whether this measure is not counted.</summary>
    /// <returns>Whether it is implicit.</returns>
    /// <remarks>
    /// There are many scores that only set the 'number' attribute to zero.
    /// </remarks>
    internal bool IsImplicit()
        => Attribute("implicit") == "yes" || Attribute("number") == "0";

    /// <summary>The notes of this measure.</summary>
    /// <returns>The notes, in document order.</returns>
    internal List<MusicXmlNode> GetNotes() => GetNamedChildren("note");
}

/// <summary>The syllabic element.</summary>
internal sealed class MusicXmlSyllabic : MusicXmlMusicNode
{
    /// <summary>Whether the syllable carries on into the next note.</summary>
    /// <returns>Whether it continues.</returns>
    internal bool Continued()
    {
        string text = GetText();
        return text == "begin" || text == "middle";
    }
}

/// <summary>The lyric element.</summary>
internal sealed class MusicXmlLyric : MusicXmlMusicNode
{
}

/// <summary>The sound element.</summary>
internal sealed class MusicXmlSound : MusicXmlMusicNode
{
    /// <summary>The tempo this element asks for, if it asks for one.</summary>
    /// <returns>The value of the 'tempo' attribute.</returns>
    internal string GetTempo() => Attribute("tempo");
}

/// <summary>The notations element.</summary>
internal sealed class MusicXmlNotations : MusicXmlMusicNode
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int>
        {
            { "arpeggiate", 2 },
            { "non-arpeggiate", 2 },
            { "slur", 2 },
            { "tied", 2 },
        };

    /// <summary>Builds the element.</summary>
    internal MusicXmlNotations()
    {
        Content["arpeggiate"] = new List<MusicXmlNode>();
        Content["non-arpeggiate"] = new List<MusicXmlNode>();
        Content["slur"] = new List<MusicXmlNode>();
        Content["tied"] = new List<MusicXmlNode>();
    }

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>The tie this element carries, if any.</summary>
    /// <returns>The tie, or null.</returns>
    internal MusicXmlNode GetTie()
    {
        List<MusicXmlNode> ties = GetList("tied");
        MusicXmlNode answer = null;
        //If we have both a normal and a laissez-vibrer tie, prefer the former and
        //ignore the latter. In any case, take the first element found.
        foreach (MusicXmlNode tie in ties)
        {
            if (tie.Attribute("type") == "start")
            {
                answer = tie;
                break;
            }

            if (tie.Attribute("type") == "let-ring" && answer == null)
            {
                answer = tie;
            }
        }

        return answer;
    }

    /// <summary>The tuplets this element carries.</summary>
    /// <returns>The tuplets.</returns>
    internal List<MusicXmlTuplet> GetTuplets() => GetTypedChildren<MusicXmlTuplet>();
}

/// <summary>The time element.</summary>
internal sealed class MusicXmlTime : MusicXmlMusicNode
{
}

/// <summary>The time-modification element.</summary>
internal sealed class MusicXmlTimeModification : MusicXmlMusicNode
{
    /// <summary>How the tuplet scales a duration.</summary>
    /// <returns>The normal count and the actual count.</returns>
    internal (int Normal, int Actual) GetFraction()
    {
        MusicXmlNode actual = GetMaybeExistNamedChild("actual-notes");
        MusicXmlNode normal = GetMaybeExistNamedChild("normal-notes");
        return (int.Parse(normal.GetText(), CultureInfo.InvariantCulture),
                int.Parse(actual.GetText(), CultureInfo.InvariantCulture));
    }

    /// <summary>The note value the tuplet is written against.</summary>
    /// <returns>The duration logarithm and dot count, or null.</returns>
    internal (int Log, int Dots)? GetNormalType()
    {
        MusicXmlNode tupletType = GetMaybeExistNamedChild("normal-type");
        if (tupletType != null)
        {
            List<MusicXmlNode> dots = GetNamedChildren("normal-dot");
            int log = MusicXmlUtilities.MusicXmlDurationToLog(tupletType.GetText().Trim());
            return (log, dots.Count);
        }

        return null;
    }
}

/// <summary>The tuplet element.</summary>
internal sealed class MusicXmlTuplet : MusicXmlSpanner
{
    private static (int Log, int Dots)? DurationInfoFromTupletNote(MusicXmlNode tupletNote)
    {
        MusicXmlNode tupletType = tupletNote.GetMaybeExistNamedChild("tuplet-type");
        if (tupletType != null)
        {
            List<MusicXmlNode> dots = tupletNote.GetNamedChildren("tuplet-dot");
            int log = MusicXmlUtilities.MusicXmlDurationToLog(tupletType.GetText().Trim());
            return (log, dots.Count);
        }

        return null;
    }

    /// <summary>The note value the tuplet is written against.</summary>
    /// <returns>The duration logarithm and dot count, or null.</returns>
    internal (int Log, int Dots)? GetNormalType()
    {
        MusicXmlNode tuplet = GetMaybeExistNamedChild("tuplet-normal");
        return tuplet != null ? DurationInfoFromTupletNote(tuplet) : null;
    }

    /// <summary>The note value the tuplet actually holds.</summary>
    /// <returns>The duration logarithm and dot count, or null.</returns>
    internal (int Log, int Dots)? GetActualType()
    {
        MusicXmlNode tuplet = GetMaybeExistNamedChild("tuplet-actual");
        return tuplet != null ? DurationInfoFromTupletNote(tuplet) : null;
    }

    private static int? GetTupletNoteCount(MusicXmlNode tupletNote)
    {
        if (tupletNote != null)
        {
            MusicXmlNode number = tupletNote.GetMaybeExistNamedChild("tuplet-number");
            if (number != null)
            {
                return int.Parse(number.GetText(), CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    /// <summary>How many notes the tuplet is written as.</summary>
    /// <returns>The count, or null.</returns>
    internal int? GetNormalNr()
        => GetTupletNoteCount(GetMaybeExistNamedChild("tuplet-normal"));

    /// <summary>How many notes the tuplet actually holds.</summary>
    /// <returns>The count, or null.</returns>
    internal int? GetActualNr()
        => GetTupletNoteCount(GetMaybeExistNamedChild("tuplet-actual"));

    /// <summary>How the tuplet's number is drawn.</summary>
    /// <returns>The colour and font size.</returns>
    /// <remarks>
    /// It is only possible to modify the appearance of the triplet number but not the
    /// appearance of the triplet bracket. We only look at the tuplet-number child of
    /// the tuplet-actual element.
    /// </remarks>
    internal (string Color, string FontSize) GetTupletNumberAttributes()
    {
        string color = null;
        string fontSize = null;

        MusicXmlNode tupletActual = GetMaybeExistNamedChild("tuplet-actual");
        if (tupletActual != null)
        {
            MusicXmlNode tupletNumber
                = tupletActual.GetMaybeExistNamedChild("tuplet-number");
            if (tupletNumber != null)
            {
                color = tupletNumber.Attribute("color");
                fontSize = tupletNumber.Attribute("font-size");
            }
        }

        return (color, fontSize);
    }
}

/// <summary>The slur element.</summary>
internal sealed class MusicXmlSlur : MusicXmlSpanner
{
}

/// <summary>The tied element.</summary>
internal sealed class MusicXmlTied : MusicXmlSpanner
{
}

/// <summary>The beam element.</summary>
internal sealed class MusicXmlBeam : MusicXmlSpanner
{
    /// <inheritdoc/>
    /// <remarks>A beam element has no 'type' attribute; its text is the type.</remarks>
    internal override string GetSpannerType() => GetText();

    /// <summary>Whether this is the outermost beam.</summary>
    /// <returns>Whether it is the first.</returns>
    internal bool IsPrimary() => Attribute("number", "1") == "1";
}

/// <summary>The octave-shift element.</summary>
internal sealed class MusicXmlOctaveShift : MusicXmlSpanner
{
    /// <summary>How far the shift reaches.</summary>
    /// <remarks>
    /// Upstream declares this as a CLASS attribute, which python's <c>getattr</c> then
    /// lets the XML attribute of the same name shadow, so the default is 8 and a
    /// document's own value wins.
    /// </remarks>
    internal string Size => Attribute("size", "8");
}

/// <summary>
/// The inner rest element, not the whole rest block, which is a note element.
/// </summary>
internal sealed class MusicXmlRest : MusicXmlMusicNode
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int>
        {
            { "display-octave", 1 },
            { "display-step", 1 },
        };

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>Whether the whole measure is this one rest.</summary>
    internal bool IsWholeMeasureValue { get; set; }

    /// <summary>Whether the voice this rest is in has no other voice beside it.</summary>
    internal bool? SingleVoice { get; set; }

    /// <summary>Whether the whole measure is this one rest.</summary>
    /// <returns>Whether it is.</returns>
    internal bool IsWholeMeasure() => IsWholeMeasureValue;

    /// <summary>Turns this into an output-side pitch, if it was placed.</summary>
    /// <returns>The pitch, or null.</returns>
    internal LilyPitch ToLilyObject()
    {
        LilyPitch pitch = null;
        string step = GetString("display-step");
        if (!string.IsNullOrEmpty(step))
        {
            pitch = new LilyPitch(State);
            pitch.Step = MusicXmlConversion.MusicXmlStepToLily(step).Value;
            //if display-step is present, display-octave must be present too
            pitch.Octave = GetInt("display-octave") - 4;
        }

        return pitch;
    }
}

/// <summary>The bend element.</summary>
internal sealed class MusicXmlBend : MusicXmlMusicNode
{
    /// <summary>How far the bend reaches.</summary>
    /// <returns>The alteration.</returns>
    internal object BendAlter()
        => MusicXmlUtilities.InterpretAlterElementBoxed(
            GetMaybeExistNamedChild("bend-alter"));
}

/// <summary>An element naming a pitch by step and alteration.</summary>
internal class MusicXmlChordPitch : MusicXmlMusicNode
{
    /// <summary>The element name that carries the step.</summary>
    /// <returns>The name.</returns>
    internal virtual string StepClassName() => "root-step";

    /// <summary>The element name that carries the alteration.</summary>
    /// <returns>The name.</returns>
    internal virtual string AlterClassName() => "root-alter";

    /// <summary>The step.</summary>
    /// <returns>The step name.</returns>
    internal string GetStep()
    {
        List<MusicXmlNode> found = GetNamedChildren(StepClassName());
        if (found.Count != 1)
        {
            //Upstream raises here and nothing catches it.
            throw new ImportAbortedException(
                "Child is not unique for " + StepClassName()
                + " found " + found.Count.ToString(CultureInfo.InvariantCulture));
        }

        return found[0].GetText().Trim();
    }

    /// <summary>The alteration.</summary>
    /// <returns>The alteration.</returns>
    internal double GetAlteration()
        => MusicXmlUtilities.InterpretAlterElement(
            GetMaybeExistNamedChild(AlterClassName()));
}

/// <summary>The root element.</summary>
internal sealed class MusicXmlRoot : MusicXmlChordPitch
{
}

/// <summary>The bass element.</summary>
internal sealed class MusicXmlBass : MusicXmlChordPitch
{
    /// <inheritdoc/>
    internal override string StepClassName() => "bass-step";

    /// <inheritdoc/>
    internal override string AlterClassName() => "bass-alter";
}

/// <summary>The degree element.</summary>
internal sealed class MusicXmlChordModification : MusicXmlMusicNode
{
    /// <summary>Whether the degree is added or taken away.</summary>
    /// <returns>1 to add or alter, -1 to subtract, 0 for anything else.</returns>
    internal int GetModificationType()
    {
        MusicXmlNode child = GetMaybeExistNamedChild("degree-type");
        string text = child.GetText().Trim();
        switch (text)
        {
            case "add":
            case "alter":
                return 1;
            case "subtract":
                return -1;
            default:
                return 0;
        }
    }

    /// <summary>Which degree.</summary>
    /// <returns>The degree number.</returns>
    internal int GetValue()
    {
        MusicXmlNode child = GetMaybeExistNamedChild("degree-value");
        return child != null
            ? int.Parse(child.GetText().Trim(), CultureInfo.InvariantCulture)
            : 0;
    }

    /// <summary>How the degree is altered.</summary>
    /// <returns>The alteration.</returns>
    internal double GetAlter()
        => MusicXmlUtilities.InterpretAlterElement(
            GetMaybeExistNamedChild("degree-alter"));
}

/// <summary>The frame element.</summary>
internal sealed class MusicXmlFrame : MusicXmlMusicNode
{
    /// <summary>How many frets the diagram shows.</summary>
    /// <returns>The count.</returns>
    internal int GetFrets() => GetNamedChildValueNumber("frame-frets", 4);

    /// <summary>How many strings the diagram shows.</summary>
    /// <returns>The count.</returns>
    internal int GetStrings() => GetNamedChildValueNumber("frame-strings", 6);

    /// <summary>Which fret the diagram starts at.</summary>
    /// <returns>The fret.</returns>
    internal int GetFirstFret() => GetNamedChildValueNumber("first-fret", 1);
}

/// <summary>The frame-note element.</summary>
internal sealed class MusicXmlFrameNote : MusicXmlMusicNode
{
    /// <summary>Which string.</summary>
    /// <returns>The string number.</returns>
    internal int GetStringNumber() => GetNamedChildValueNumber("string", 1);

    /// <summary>Which fret.</summary>
    /// <returns>The fret number.</returns>
    internal int GetFret() => GetNamedChildValueNumber("fret", 0);

    /// <summary>Which finger.</summary>
    /// <returns>The finger number, or -1.</returns>
    internal int GetFingering() => GetNamedChildValueNumber("fingering", -1);

    /// <summary>Which end of a barre this is.</summary>
    /// <returns>The value of the 'type' attribute, or empty.</returns>
    internal string GetBarre()
    {
        MusicXmlNode barre = GetMaybeExistNamedChild("barre");
        return barre != null ? barre.Attribute("type", string.Empty) : string.Empty;
    }
}

/// <summary>The bar-style element.</summary>
internal sealed class MusicXmlBarStyle : MusicXmlMusicNode
{
}

/// <summary>The metronome element.</summary>
internal sealed class MusicXmlMetronome : MusicXmlMusicNode
{
}

/// <summary>The beat-type element.</summary>
internal sealed class MusicXmlBeatType : MusicXmlMusicNode
{
}

/// <summary>The beat-unit element.</summary>
internal sealed class MusicXmlBeatUnit : MusicXmlMusicNode
{
}

/// <summary>The beat-unit-dot element.</summary>
internal sealed class MusicXmlBeatUnitDot : MusicXmlMusicNode
{
}

/// <summary>The beat-unit-tied element.</summary>
internal sealed class MusicXmlBeatUnitTied : MusicXmlMusicNode
{
}

/// <summary>The beats element.</summary>
internal sealed class MusicXmlBeats : MusicXmlMusicNode
{
}

/// <summary>The bracket element.</summary>
internal sealed class MusicXmlBracket : MusicXmlSpanner
{
}

/// <summary>The credit-words element.</summary>
internal sealed class MusicXmlCreditWords : MusicXmlNode
{
}

/// <summary>The credit-symbol element.</summary>
internal sealed class MusicXmlCreditSymbol : MusicXmlNode
{
}

/// <summary>The dashes element.</summary>
internal sealed class MusicXmlDashes : MusicXmlSpanner
{
}

/// <summary>The direction-type element.</summary>
internal sealed class MusicXmlDirType : MusicXmlMusicNode
{
}

/// <summary>The direction element.</summary>
internal sealed class MusicXmlDirection : MusicXmlMeasureElement
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int>
        {
            { "offset", 1 },
            { "staff", 1 },
            { "voice", 1 },
        };

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>How far the direction is displaced from its moment.</summary>
    internal PythonFraction? Offset { get; set; }

    /// <summary>The direction this one chains back to.</summary>
    internal MusicXmlDirection Previous { get; set; }

    /// <summary>The direction this one chains on to.</summary>
    internal MusicXmlDirection Next { get; set; }

    /// <summary>Whether this direction has been dealt with already.</summary>
    internal bool Converted { get; set; }

    /// <summary>Whether the pedal marking is drawn as a bracket rather than a sign.</summary>
    /// <returns>Whether it is a line, or null when this is not a pedal start.</returns>
    /// <remarks>
    /// We assume there is only a single pedal spanner in the direction element.
    /// Additionally, we only consider the 'line' attribute at the beginning, assuming
    /// that the remaining parts of the pedal spanner are of the same type.
    /// </remarks>
    internal bool? PedalIsLine()
    {
        MusicXmlNode pedal = null;
        foreach (MusicXmlDirType dirType in GetTypedChildren<MusicXmlDirType>())
        {
            pedal = dirType.GetNamedChild("pedal");
            if (pedal != null)
            {
                break;
            }
        }

        if (pedal != null && pedal.Attribute("type") == "start")
        {
            return pedal.Attribute("line", "no") == "yes";
        }

        return null;
    }
}

/// <summary>The elision element.</summary>
internal sealed class MusicXmlElision : MusicXmlMusicNode
{
}

/// <summary>The extend element.</summary>
internal sealed class MusicXmlExtend : MusicXmlMusicNode
{
}

/// <summary>The figured-bass element.</summary>
internal sealed class MusicXmlFiguredBass : MusicXmlMusicNode
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int> { { "duration", 1 } };

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>How many divisions of a quarter note the part measures in.</summary>
    /// <remarks>
    /// An attribute upstream grows on the object at run time; C# needs it declared.
    /// </remarks>
    internal System.Numerics.BigInteger Divisions { get; set; }
}

/// <summary>The forward element.</summary>
internal sealed class MusicXmlForward : MusicXmlMeasureElement
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int>
        {
            { "duration", 1 },
            { "staff", 1 },
            { "voice", 1 },
        };

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;
}

/// <summary>The glissando element.</summary>
internal sealed class MusicXmlGlissando : MusicXmlSpanner
{
}

/// <summary>The optional empty grace child of a note.</summary>
internal sealed class MusicXmlGrace : MusicXmlMusicNode
{
}

/// <summary>The group-abbreviation element.</summary>
internal sealed class MusicXmlGroupAbbreviation : MusicXmlMusicNode
{
}

/// <summary>The group-abbreviation-display element.</summary>
internal sealed class MusicXmlGroupAbbreviationDisplay : MusicXmlMusicNode
{
}

/// <summary>The group-name element.</summary>
internal sealed class MusicXmlGroupName : MusicXmlMusicNode
{
}

/// <summary>The group-name-display element.</summary>
internal sealed class MusicXmlGroupNameDisplay : MusicXmlMusicNode
{
}

/// <summary>The group-symbol element.</summary>
internal sealed class MusicXmlGroupSymbol : MusicXmlMusicNode
{
}

/// <summary>The harmony element.</summary>
internal sealed class MusicXmlHarmony : MusicXmlMusicNode
{
    private static readonly Dictionary<string, int> TrackedChildren
        = new Dictionary<string, int>
        {
            { "offset", 1 },
            { "staff", 1 },
        };

    /// <inheritdoc/>
    internal override Dictionary<string, int> MaxOccursByChild => TrackedChildren;

    /// <summary>How far the harmony is displaced from its moment.</summary>
    internal PythonFraction? Offset { get; set; }
}

/// <summary>The key-alter element.</summary>
internal sealed class MusicXmlKeyAlter : MusicXmlMusicNode
{
}

/// <summary>The key-octave element.</summary>
internal sealed class MusicXmlKeyOctave : MusicXmlMusicNode
{
}

/// <summary>The key-step element.</summary>
internal sealed class MusicXmlKeyStep : MusicXmlMusicNode
{
}

/// <summary>The octave element.</summary>
internal class MusicXmlOctave : MusicXmlMusicNode
{
}

/// <summary>The display-octave element.</summary>
internal sealed class MusicXmlDisplayOctave : MusicXmlOctave
{
}

/// <summary>The ornaments element.</summary>
internal sealed class MusicXmlOrnaments : MusicXmlMusicNode
{
}

/// <summary>The part-group element.</summary>
internal sealed class MusicXmlPartGroup : MusicXmlMusicNode
{
}

/// <summary>The pedal element.</summary>
internal sealed class MusicXmlPedal : MusicXmlSpanner
{
}

/// <summary>The per-minute element.</summary>
internal sealed class MusicXmlPerMinute : MusicXmlMusicNode
{
}

/// <summary>The print element.</summary>
internal sealed class MusicXmlPrint : MusicXmlMusicNode
{
}

/// <summary>The score-part element.</summary>
internal sealed class MusicXmlScorePart : MusicXmlMusicNode
{
}

/// <summary>The slide element.</summary>
internal sealed class MusicXmlSlide : MusicXmlSpanner
{
}

/// <summary>The staff element.</summary>
internal sealed class MusicXmlStaff : MusicXmlMusicNode
{
}

/// <summary>The step element.</summary>
internal class MusicXmlStep : MusicXmlMusicNode
{
}

/// <summary>The display-step element.</summary>
internal sealed class MusicXmlDisplayStep : MusicXmlStep
{
}

/// <summary>The text element.</summary>
internal sealed class MusicXmlText : MusicXmlMusicNode
{
}

/// <summary>The type element.</summary>
internal sealed class MusicXmlType : MusicXmlMusicNode
{
}

/// <summary>The voice element.</summary>
internal sealed class MusicXmlVoiceElement : MusicXmlMusicNode
{
}

/// <summary>The wavy-line element.</summary>
internal sealed class MusicXmlWavyLine : MusicXmlSpanner
{
    /// <summary>Whether this line both starts and stops here.</summary>
    internal bool StartStop { get; set; }
}

/// <summary>The wedge element.</summary>
internal sealed class MusicXmlWedge : MusicXmlSpanner
{
}

/// <summary>The words element.</summary>
internal sealed class MusicXmlWords : MusicXmlMusicNode
{
}

/// <summary>The chord element.</summary>
internal sealed class MusicXmlChord : MusicXmlMusicNode
{
}

/// <summary>
/// The tremolo element, which is either an ornament (single-note) or a spanner
/// (double-note).
/// </summary>
internal sealed class MusicXmlTremolo : MusicXmlSpanner
{
    /// <inheritdoc/>
    /// <remarks>The 'type' attribute is optional, defaulting to 'single'.</remarks>
    internal override string GetSpannerType() => Attribute("type", "single");
}
