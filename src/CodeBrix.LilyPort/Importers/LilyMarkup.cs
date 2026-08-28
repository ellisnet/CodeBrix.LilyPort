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
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.ConvertLy;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (the markup and font-size helpers, text_to_ly, accidental_values_dict);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// One text element and the attributes it is drawn with.
/// </summary>
/// <remarks>
/// ⚠ THE ATTRIBUTE DICTIONARY IS NOT THE XML ONE. It starts as a copy of it, and the
/// converter then adds keys of its own — 'font-size-scale' holds a NUMBER, not text —
/// so the values are typed as python leaves them rather than narrowed to strings.
/// </remarks>
internal sealed class LilyMarkupElement
{
    /// <summary>Builds the pair.</summary>
    /// <param name="element">The element the text comes from.</param>
    /// <param name="attributes">How it is drawn.</param>
    internal LilyMarkupElement(MusicXmlNode element, Dictionary<string, object> attributes)
    {
        Element = element;
        Attributes = attributes ?? new Dictionary<string, object>(StringComparer.Ordinal);
    }

    /// <summary>The element the text comes from.</summary>
    internal MusicXmlNode Element { get; }

    /// <summary>How it is drawn.</summary>
    internal Dictionary<string, object> Attributes { get; }

    /// <summary>One attribute, as text.</summary>
    /// <param name="key">The attribute name.</param>
    /// <param name="defaultValue">What to answer when it is absent.</param>
    /// <returns>The value.</returns>
    internal string Get(string key, string defaultValue = null)
        => Attributes.TryGetValue(key, out object value) && value != null
            ? value as string ?? LilyOutputPrinter.FormatNumber(value)
            : defaultValue;

    /// <summary>One attribute, as it is stored.</summary>
    /// <param name="key">The attribute name.</param>
    /// <returns>The value, or null.</returns>
    internal object GetRaw(string key)
        => Attributes.TryGetValue(key, out object value) ? value : null;

    /// <summary>Copies an element's XML attributes into a markup attribute set.</summary>
    /// <param name="node">The element.</param>
    /// <returns>The copy.</returns>
    internal static Dictionary<string, object> CopyAttributes(MusicXmlNode node)
    {
        Dictionary<string, object> copy
            = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in node.AttributeDict)
        {
            copy[entry.Key] = entry.Value;
        }

        return copy;
    }
}

/// <summary>The helpers that turn MusicXML text and styling into LilyPond markup.</summary>
internal static class LilyMarkup
{
    /// <summary>Quotes an instrument name, unless it is markup already.</summary>
    /// <param name="inputString">The name.</param>
    /// <returns>The name, ready to be written.</returns>
    internal static string EscapeInstrumentString(string inputString)
    {
        if (inputString.Contains("\\"))
        {
            return "\\markup " + inputString;
        }

        if (inputString.Length > 0 && inputString[0] == '"')
        {
            return inputString;
        }

        return "\"" + inputString + "\"";
    }

    /// <summary>Turns a MusicXML colour into a LilyPond one.</summary>
    /// <param name="hexValue">The colour, in MusicXML's ARGB notation.</param>
    /// <param name="returnBlack">Whether plain black is worth writing out.</param>
    /// <returns>The colour, or null when there is nothing to say.</returns>
    internal static string ColorToLy(string hexValue, bool returnBlack = false)
    {
        if (hexValue == null
            || ((hexValue == "#000000" || hexValue == "#FF000000") && !returnBlack))
        {
            return null;
        }

        //MusicXML uses ARGB notation, while LilyPond uses RGBA.
        Match match = PythonRegex.MatchAt(
            @"(?xi)
                       \# ( [0-9a-f] [0-9a-f] | )
                          ( [0-9a-f] [0-9a-f]
                            [0-9a-f] [0-9a-f]
                            [0-9a-f] [0-9a-f] ) $
                   ",
            hexValue);
        return match.Success
            ? "\"#" + match.Groups[2].Value + match.Groups[1].Value + "\""
            : null;
    }

    /// <summary>Turns a magnification into LilyPond's font-size number.</summary>
    /// <param name="magnification">The magnification.</param>
    /// <returns>The font size.</returns>
    internal static double MagnificationToFontSize(double magnification)
        => Math.Log2(magnification) * 6;

    /// <summary>Turns a LilyPond font-size number into a magnification.</summary>
    /// <param name="size">The font size.</param>
    /// <returns>The magnification.</returns>
    internal static double FontSizeToMagnification(double size) => Math.Pow(2, size / 6);

    /// <summary>Turns a CSS point size into a traditional one.</summary>
    /// <param name="size">The size, in big points.</param>
    /// <returns>The size, in points.</returns>
    /// <remarks>
    /// MusicXML uses CSS-based font units, which means that 72 points equal one inch.
    /// On the other hand, LilyPond uses the traditional American typesetting point
    /// (similar to TeX), with 72.27pt = 1in. The former unit is called 'bp' ('big
    /// points') in LilyPond.
    /// </remarks>
    internal static double BpToPt(double size) => size * 72.27 / 72;

    /// <summary>
    /// python's <c>'%.Nf'</c>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="digits">How many decimal places.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// The two families round a value that lands EXACTLY on a midpoint differently, and
    /// nothing here can land on one: every value that reaches this is the result of a
    /// base-two logarithm or a division by 72. Recorded rather than worked around.
    /// </remarks>
    private static string FormatFixed(double value, int digits)
        => value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
                          CultureInfo.InvariantCulture);

    /// <summary>Turns a numeric font size into LilyPond markup.</summary>
    /// <param name="state">The import this belongs to.</param>
    /// <param name="size">The size, in big points.</param>
    /// <param name="ratio">How far the staff size departs from the default.</param>
    /// <param name="command">Whether a markup command is wanted rather than a number.</param>
    /// <returns>The markup, or null when the size says nothing.</returns>
    internal static string FontSizeNumberToLily(
        MusicXmlImportState state, double size, double ratio, bool command)
    {
        size = BpToPt(size);
        if (size <= 1)
        {
            return null;
        }

        if (command && state.GetAbsoluteFontSizes())
        {
            return "\\abs-fontsize #" + FormatFixed(size, 3);
        }

        //LilyPond uses 11pt as the default text font size.
        double referenceSize = 11 * ratio;
        //TODO (upstream's): Apply further scaling as soon as <staff-size> gets handled.
        double scaledSize = MagnificationToFontSize(size / referenceSize);
        return command
            ? "\\fontsize #" + FormatFixed(scaledSize, 3)
            : "#" + FormatFixed(scaledSize, 3);
    }

    private static readonly Dictionary<string, (string Command, int? Size)> FontSizeWords
        = new Dictionary<string, (string, int?)>(StringComparer.Ordinal)
        {
            { "xx-small", ("\\teeny", -3) },
            { "x-small", ("\\tiny", -2) },
            { "small", ("\\small", -1) },
            { "medium", (string.Empty, 0) },
            { "large", ("\\large", 1) },
            { "x-large", ("\\huge", 2) },
            { "xx-large", ("\\larger\\huge", 3) },
        };

    /// <summary>Turns a named font size into LilyPond markup.</summary>
    /// <param name="size">The size name.</param>
    /// <param name="ratio">How far the staff size departs from the default.</param>
    /// <param name="command">Whether a markup command is wanted rather than a number.</param>
    /// <returns>The markup, or null when the name says nothing.</returns>
    internal static string FontSizeWordToLily(string size, double ratio, bool command)
    {
        (string Command, int? Size) entry =
            size != null && FontSizeWords.TryGetValue(size, out (string, int?) found)
                ? found
                : (null, null);

        //The comparison values are heuristic: only if the scaling doesn't differ too
        //much from LilyPond's default staff size it makes sense to use commands like
        //`\teeny', since values like 'xx-small' are still absolute.
        if (command && ratio >= 0.9 && ratio <= 1.1)
        {
            return entry.Command;
        }

        if (!entry.Size.HasValue)
        {
            return null;
        }

        double magstep = FontSizeToMagnification(entry.Size.Value);
        double fontSize = MagnificationToFontSize(magstep / ratio);
        //Intentionally use low precision.
        return command
            ? "\\fontsize #" + FormatFixed(fontSize, 1)
            : "#" + FormatFixed(fontSize, 1);
    }

    /// <summary>Turns a font size of either kind into LilyPond markup.</summary>
    /// <param name="state">The import this belongs to.</param>
    /// <param name="size">The size, as the document gave it.</param>
    /// <param name="command">Whether a markup command is wanted rather than a number.</param>
    /// <param name="scale">What to multiply a numeric size by.</param>
    /// <returns>The markup, or null when the size says nothing.</returns>
    internal static string GetFontSize(
        MusicXmlImportState state, object size, bool command, double scale = 1.0)
    {
        if (size == null)
        {
            return null;
        }

        double ratio = state.Paper.GlobalStaffSize / LilyPaper.DefaultGlobalStaffSize;

        if (size is double numeric)
        {
            return FontSizeNumberToLily(state, numeric * scale, ratio, command);
        }

        string text = size as string;
        if (text != null && double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return FontSizeNumberToLily(state, parsed * scale, ratio, command);
        }

        return FontSizeWordToLily(text, ratio, command);
    }

    private static readonly Dictionary<string, string> AccidentalValues
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "sharp", "♯" },
            { "natural", "♮" },
            { "flat", "♭" },
            { "double-sharp", "𝄪" },
            { "sharp-sharp", "♯♯" },
            { "flat-flat", "𝄫" },
            { "natural-sharp", "♮♯" },
            { "natural-flat", "♮♭" },
            { "quarter-flat", "accidentals.mirroredflat" },
            { "quarter-sharp", "accidentals.sharp.slashslash.stem" },
            { "three-quarters-flat", "accidentals.mirroredflat.flat" },
            { "three-quarters-sharp", "accidentals.sharp.slashslash.stemstemstem" },
            { "sharp-down", "accidentals.sharp.arrowdown" },
            { "sharp-up", "accidentals.sharp.arrowup" },
            { "natural-down", "accidentals.natural.arrowdown" },
            { "natural-up", "accidentals.natural.arrowup" },
            { "flat-down", "accidentals.flat.arrowdown" },
            { "flat-up", "accidentals.flat.arrowup" },
            { "triple-sharp", "♯𝄪" },
            { "triple-flat", "♭𝄫" },
            { "slash-quarter-sharp", "accidentals.sharp.slashslashslash.stem" },
            { "slash-sharp", "accidentals.sharp.slashslashslash.stemstem" },
            { "slash-flat", "accidentals.flat.slash" },
            { "double-slash-flat", "accidentals.flat.slashslash" },
            { "sori", "accidentals.sharp.sori" },
            { "koron", "accidentals.flat.koron" },
        };

    /// <summary>The glyph or character an accidental name draws as.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The glyph, or null when the name is not handled.</returns>
    internal static string AccidentalValue(string name)
        => name != null && AccidentalValues.TryGetValue(name, out string value) ? value : null;

    private static readonly Dictionary<string, string> FontWeights
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "normal", string.Empty },
            { "bold", "\\bold" },
        };

    private static readonly Dictionary<string, string> FontStyles
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "normal", string.Empty },
            { "italic", "\\italic" },
        };

    private static readonly Dictionary<int, string> Underlines
        = new Dictionary<int, string>
        {
            { 0, string.Empty },
            { 1, "\\underline" },
            { 2, "\\underline \\underline" },
            { 3, "\\underline \\underline \\underline" },
        };

    //TODO (upstream's): Support more `enclosure' values.
    private static readonly Dictionary<string, string> Enclosures
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "none", string.Empty },
            { "square", "\\square" },
            { "rectangle", "\\box" },
            { "circle", "\\circle" },
            { "oval", "\\ellipse" },
        };

    private static string Lookup(Dictionary<string, string> table, string key)
        => key != null && table.TryGetValue(key, out string value) ? value : string.Empty;

    private static readonly HashSet<string> TextCarryingElements
        = new HashSet<string>(StringComparer.Ordinal)
        {
            "credit-words",
            "display-text",
            "ending",
            "group-abbreviation",
            "group-name",
            "part-abbreviation",
            "part-name",
            "rehearsal",
            "words",
        };

    /// <summary>Turns a run of text elements into one LilyPond markup expression.</summary>
    /// <param name="state">The import this belongs to.</param>
    /// <param name="elements">The elements and how each is drawn.</param>
    /// <param name="initMarkup">Markup to put in front of everything else.</param>
    /// <returns>The markup, or empty when there is nothing to draw.</returns>
    /// <remarks>
    /// The MusicXML standard doesn't specify whether a group of elements with the same
    /// enclosure should be enclosed by a single one, or whether each element should get
    /// its own. Tests with Finale and MuseScore show that they do the former, and we
    /// follow.
    /// </remarks>
    internal static string TextToLy(
        MusicXmlImportState state, List<LilyMarkupElement> elements, string initMarkup = null)
    {
        //TODO (upstream's): Handle `font-family' and other missing attributes.
        if (elements == null || elements.Count == 0)
        {
            return string.Empty;
        }

        List<string> markup = new List<string>();

        string enclosureAttribute = elements[0].Get("enclosure", "none");
        string enclosure = Lookup(Enclosures, enclosureAttribute);
        if (enclosure.Length > 0)
        {
            //Another problem is that there is no way in MusicXML to style an enclosure.
            //We use the first element's color attribute.
            string firstColor = ColorToLy(elements[0].Get("color"));
            if (firstColor != null)
            {
                markup.Add("\\with-color " + firstColor);
            }

            markup.Add(enclosure);
        }

        string previousEnclosure = enclosure;

        if (initMarkup != null)
        {
            markup.Add(initMarkup);
        }

        int closingBraces = 0;
        if (elements[0].Element.GetName() == "lilypond-markup" || elements.Count > 1)
        {
            markup.Add("\\concat {");
            closingBraces += 1;
        }

        foreach (LilyMarkupElement pair in elements)
        {
            enclosureAttribute = pair.Get("enclosure", "none");
            enclosure = Lookup(Enclosures, enclosureAttribute);
            if (previousEnclosure != enclosure)
            {
                if (enclosure.Length > 0)
                {
                    markup.Add(enclosure);
                }

                markup.Add("\\concat {");
                closingBraces += 1;

                previousEnclosure = enclosure;
            }

            object fontSizeAttribute = pair.GetRaw("font-size") ?? string.Empty;
            object rawScale = pair.GetRaw("font-size-scale");
            double fontSizeScale = rawScale == null
                ? 1.0
                : Convert.ToDouble(rawScale, CultureInfo.InvariantCulture);
            string fontSize = GetFontSize(
                state, fontSizeAttribute, command: true, scale: fontSizeScale);
            if (fontSize != null)
            {
                markup.Add(fontSize);
            }

            string fontWeight = Lookup(FontWeights, pair.Get("font-weight", "normal"));
            if (fontWeight.Length > 0)
            {
                markup.Add(fontWeight);
            }

            string fontStyle = Lookup(FontStyles, pair.Get("font-style", "normal"));
            if (fontStyle.Length > 0)
            {
                markup.Add(fontStyle);
            }

            int underlineAttribute = int.Parse(
                pair.Get("underline", "0"), CultureInfo.InvariantCulture);
            string underline = Underlines.TryGetValue(underlineAttribute, out string u)
                ? u : string.Empty;
            if (underline.Length > 0)
            {
                markup.Add(underline);
            }

            string color = ColorToLy(pair.Get("color"));
            if (color != null)
            {
                markup.Add("\\with-color " + color);
            }

            string text = string.Empty;
            string name = pair.Element.GetName();
            if (TextCarryingElements.Contains(name))
            {
                text = pair.Element.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    text = NormalizeElementText(text, pair.Get("xml:space", "default"));
                }
            }
            else if (name == "segno")
            {
                text = "\\fontsize #2 \\segno";
            }
            else if (name == "coda")
            {
                text = "\\fontsize #2 \\coda";
            }
            else if (name == "accidental-text")
            {
                string accidental = AccidentalValue(pair.Element.GetText());
                if (accidental != null)
                {
                    text = IsAscii(accidental)
                        ? "\\tiny \\musicglyph \"" + accidental + "\""
                        : "\\tiny \\number \"" + accidental + "\"";
                }
            }
            else if (name == "lilypond-markup")
            {
                text = pair.Element.GetText();
            }

            //XXX: anything else is dropped.
            if (!string.IsNullOrEmpty(text))
            {
                markup.Add(text);
            }
        }

        for (int i = 0; i < closingBraces; i++)
        {
            markup.Add("}");
        }

        return string.Join(" ", markup);
    }

    /// <summary>
    /// Applies the whitespace handling one text element asked for.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="xmlSpace">What the 'xml:space' attribute said.</param>
    /// <returns>The text, ready to be written.</returns>
    /// <remarks>
    /// For whitespace handling, if the 'xml:space' attribute is set to 'default' (or
    /// not explicitly specified), W3C recommends to apply the normalization algorithm
    /// of attribute values also for the content of XML elements: remove leading and
    /// trailing whitespace, convert all whitespace to normal spaces, and finally
    /// collapse a sequence of spaces to a single space.
    /// <para>
    /// Note that not all programs do this while importing MusicXML. Using a words
    /// element as an example, MuseScore 4.4 ignores its 'xml:space' attribute, always
    /// assuming the value 'preserve'. On the other hand, Finale 27.4 also ignores
    /// 'xml:space', does not collapse spaces, but applies a special treatment to
    /// collapse carriage returns and line feeds. Since (at least) these two programs
    /// also export MusicXML with meaningful leading and trailing whitespace without
    /// setting 'xml:space' to 'preserve', we follow.
    /// </para>
    /// </remarks>
    private static string NormalizeElementText(string text, string xmlSpace)
    {
        if (xmlSpace != "preserve")
        {
            //Upstream uses python's `split()' with no separator, which eliminates runs
            //of consecutive whitespace, temporarily adding guards so that leading and
            //trailing whitespace are handled the same way.
            string guarded = "|" + text + "|";
            guarded = string.Join(" ", PythonSplit(guarded));
            text = guarded.Substring(1, guarded.Length - 2);
            return MusicXmlUtilities.EscapeLyOutputString(text);
        }

        //`\r' can only be created with `&#xD;'.
        string[] lines = text.Replace("\r", "\n").Split('\n');
        if (lines.Length > 1)
        {
            List<string> parts = new List<string> { "\\center-column {" };
            foreach (string line in lines)
            {
                parts.Add(line.Length > 0
                    ? MusicXmlUtilities.EscapeLyOutputString(line)
                    : "\\null");
            }

            parts.Add("}");
            return string.Join(" ", parts);
        }

        return MusicXmlUtilities.EscapeLyOutputString(lines[0]);
    }

    /// <summary>
    /// python's <c>str.split()</c> with no separator: runs of whitespace split, and
    /// empty pieces never appear.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The pieces.</returns>
    private static List<string> PythonSplit(string text)
    {
        List<string> pieces = new List<string>();
        int start = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                if (start >= 0)
                {
                    pieces.Add(text.Substring(start, i - start));
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            pieces.Add(text.Substring(start));
        }

        return pieces;
    }

    /// <summary>python's <c>str.isascii()</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether every character is ASCII.</returns>
    private static bool IsAscii(string text)
    {
        foreach (char c in text)
        {
            if (c > 127)
            {
                return false;
            }
        }

        return true;
    }
}
