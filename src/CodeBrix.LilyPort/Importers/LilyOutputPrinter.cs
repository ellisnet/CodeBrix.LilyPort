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
using CodeBrix.LilyPort.ConvertLy;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (Output_stack_element, Output_printer and the two helpers above it);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>One level of the printer's scaling stack.</summary>
internal sealed class LilyOutputStackElement
{
    /// <summary>For scaling tuplets and the like.</summary>
    internal PythonFraction Factor { get; set; } = PythonFraction.One;

    /// <summary>Copies this level.</summary>
    /// <returns>The copy.</returns>
    internal LilyOutputStackElement Copy() => new LilyOutputStackElement { Factor = Factor };
}

/// <summary>
/// Formats a music expression as a LilyPond input file, taking care of indenting.
/// </summary>
/// <remarks>
/// ⚠ THE PRINTER WRITES INTO A BUFFER, not a file. Upstream hands it an open file and
/// never closes it: the last <c>newline()</c> flushes everything the document holds and
/// the residual partial line — always just the indentation — is dropped when the
/// interpreter collects the file object. Buffering here reproduces that exactly, and is
/// what lets the importer return text at all.
/// </remarks>
internal sealed class LilyOutputPrinter
{
    private readonly StringBuilder _output = new StringBuilder();

    private string _line = string.Empty;

    private readonly int _indent = 2;

    private int _nesting;

    private string _nestingStr = string.Empty;

    private string _nestingMoreStr = string.Empty;

    private string _nestingLessStr = string.Empty;

    private readonly int _lineLen = 80;

    private readonly List<LilyOutputStackElement> _outputStateStack
        = new List<LilyOutputStackElement> { new LilyOutputStackElement() };

    private bool _skipspace;

    //⚠ Upstream sets `_last_duration' in its constructor and NEVER assigns it again, so
    //`print_duration_string' compares against None on every call and the guard it reads
    //like never fires. The port keeps the dead field and its comparison rather than
    //removing either: what upstream does not do is part of what it does (rule 2).
    private readonly string _lastDuration = null;

    /// <summary>Everything written so far.</summary>
    /// <returns>The document.</returns>
    internal string GetText() => _output.ToString();

    /// <summary>Writes the version statement.</summary>
    /// <param name="version">The release the document declares.</param>
    internal void DumpVersion(string version)
    {
        PrintVerbatim("\\version \"" + version + "\"");
        Newline();
    }

    /// <summary>How far the current line is indented.</summary>
    /// <returns>The indentation, in characters.</returns>
    internal int GetIndent() => _nesting * _indent;

    /// <summary>Pushes a copy of the top of the scaling stack.</summary>
    internal void Override()
        => _outputStateStack.Add(_outputStateStack[_outputStateStack.Count - 1].Copy());

    /// <summary>Pushes a new scaling level, multiplied by the given factor.</summary>
    /// <param name="factor">The factor.</param>
    internal void AddFactor(PythonFraction factor)
    {
        Override();
        _outputStateStack[_outputStateStack.Count - 1].Factor *= factor;
    }

    /// <summary>Pops the top of the scaling stack.</summary>
    internal void Revert()
    {
        _outputStateStack.RemoveAt(_outputStateStack.Count - 1);
        if (_outputStateStack.Count == 0)
        {
            throw new ImportAbortedException("empty stack");
        }
    }

    /// <summary>The scaling in force.</summary>
    /// <returns>The factor.</returns>
    internal PythonFraction DurationFactor()
        => _outputStateStack[_outputStateStack.Count - 1].Factor;

    /// <summary>Adds text to the current line without touching the indentation.</summary>
    /// <param name="text">The text.</param>
    internal void PrintVerbatim(string text) => _line += text;

    private void SetNestingStrings()
    {
        _nestingStr = new string(' ', _indent * _nesting);
        _nestingMoreStr = new string(' ', _indent * (_nesting + 1));
        _nestingLessStr = _nesting > 0 ? new string(' ', _indent * (_nesting - 1)) : string.Empty;
    }

    private static int Count(string text, string needle)
    {
        int total = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            total++;
            index += needle.Length;
        }

        return total;
    }

    /// <summary>Adds text, adjusting the nesting level by what the text opens and closes.</summary>
    /// <param name="text">The text.</param>
    internal void UnformattedOutput(string text)
    {
        //don't indent on \< and indent only once on <<
        _nesting += Count(text, "<") - Count(text, "\\<") - Count(text, "<<") + Count(text, "{");
        _nesting -= Count(text, ">") - Count(text, "\\>") - Count(text, ">>")
                    - Count(text, "->") - Count(text, "_>") - Count(text, "^>")
                    + Count(text, "}");
        SetNestingStrings();
        PrintVerbatim(text);
    }

    /// <summary>Writes a duration, unless it repeats the last one written.</summary>
    /// <param name="text">The duration.</param>
    internal void PrintDurationString(string text)
    {
        if (_lastDuration == text)
        {
            return;
        }

        UnformattedOutput(text);
    }

    /// <summary>Adds one word, wrapping the line when it no longer fits.</summary>
    /// <param name="text">The word.</param>
    internal void AddWord(string text)
    {
        if (MusicXmlUtilities.PythonLength(text) + 1
            + MusicXmlUtilities.PythonLength(_line) > _lineLen)
        {
            Newline();
            _skipspace = true;
        }

        if (!_skipspace)
        {
            _line += " ";
        }

        UnformattedOutput(text);
        _skipspace = false;
    }

    /// <summary>Ends the current line.</summary>
    internal void Newline()
    {
        //Correct indentation for `}', `>>', and `} <<' on a line by its own.
        _line = PythonRegex.Sub(
            PythonRegex.Escape(_nestingMoreStr) + @"(>>|})\s*$",
            _nestingStr + "\\1", _line);
        _line = PythonRegex.Sub(
            PythonRegex.Escape(_nestingStr) + @"} <<\s*$",
            _nestingLessStr + "} <<", _line);

        _output.Append(_line).Append('\n');
        _line = _nestingStr;
        _skipspace = true;
    }

    /// <summary>Suppresses the space before the next word.</summary>
    internal void Skipspace() => _skipspace = true;

    /// <summary>Adds text, breaking it into words unless a word was just skipped over.</summary>
    /// <param name="text">The text.</param>
    internal void Dump(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_skipspace)
        {
            _skipspace = false;
            UnformattedOutput(text);
        }
        else
        {
            //Avoid splitting quoted strings (e.g. "1. Wie") when indenting.
            foreach (string word
                     in MusicXmlUtilities.SplitStringAndPreserveDoublequotedSubstrings(text))
            {
                AddWord(word);
            }
        }
    }

    /// <summary>Writes a documentation string as one quoted, re-wrapped block.</summary>
    /// <param name="text">The text.</param>
    internal void DumpTexidoc(string text)
    {
        _nesting += 1;
        SetNestingStrings();

        bool start = true;
        foreach (string paragraph in SplitIntoParagraphs(text))
        {
            List<string> words
                = MusicXmlUtilities.SplitStringAndPreserveDoublequotedSubstrings(paragraph);

            if (start)
            {
                words[0] = "\"" + words[0];
                start = false;
            }
            else
            {
                PrintVerbatim("\n");
                Newline();
            }

            foreach (string word in words)
            {
                AddWord(word);
            }
        }

        PrintVerbatim("\"");

        _nesting -= 1;
        SetNestingStrings();
    }

    /// <summary>Writes lyrics, one word at a time.</summary>
    /// <param name="text">The text.</param>
    internal void DumpLyrics(string text)
    {
        foreach (string word
                 in MusicXmlUtilities.SplitStringAndPreserveDoublequotedSubstrings(text))
        {
            AddWord(word);
        }
    }

    /// <summary>Runs of non-blank lines, each joined into one paragraph.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The paragraphs.</returns>
    internal static IEnumerable<string> SplitIntoParagraphs(string text)
    {
        List<string> paragraph = new List<string>();

        foreach (string line in SplitLines(text))
        {
            if (line.Trim().Length > 0)
            {
                paragraph.Add(line);
            }
            else if (paragraph.Count > 0)
            {
                yield return string.Join(" ", paragraph);
                paragraph = new List<string>();
            }
        }

        if (paragraph.Count > 0)
        {
            yield return string.Join(" ", paragraph);
        }
    }

    /// <summary>python's <c>str.splitlines()</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The lines, without their terminators, and no trailing empty entry.</returns>
    /// <remarks>
    /// ⚠ NOT <c>Split('\n')</c>. python breaks on the whole family of line boundaries —
    /// carriage returns and the two Unicode separators among them — and does NOT leave
    /// a trailing empty entry for a text that ends in a break.
    /// </remarks>
    private static List<string> SplitLines(string text)
    {
        List<string> lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        int start = 0;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            bool isBreak = c == '\n' || c == '\r' || c == '\v' || c == '\f'
                           || c == '\u001c' || c == '\u001d' || c == '\u001e'
                           || c == '\u0085' || c == '\u2028' || c == '\u2029';
            if (!isBreak)
            {
                i++;
                continue;
            }

            lines.Add(text.Substring(start, i - start));
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            i++;
            start = i;
        }

        if (start < text.Length)
        {
            lines.Add(text.Substring(start));
        }

        return lines;
    }

    /// <summary>
    /// python's <c>'%s'</c> for a value that may be a fraction, a number or nothing.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    internal static string FormatValue(PythonFraction? value)
        => value.HasValue ? value.Value.ToString() : "None";

    /// <summary>
    /// python's <c>'%s'</c> for a number the schema left as an integer or a float.
    /// </summary>
    /// <param name="value">The value, boxed as python left it.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// ⚠ <c>'%s' % 2</c> IS '2' AND <c>'%s' % 2.0</c> IS '2.0'. The two readings reach
    /// the output, so the box has to be honoured rather than widened to a double.
    /// </remarks>
    internal static string FormatNumber(object value)
    {
        switch (value)
        {
            case null:
                return "None";
            case int i:
                return i.ToString(CultureInfo.InvariantCulture);
            case long l:
                return l.ToString(CultureInfo.InvariantCulture);
            case double d:
                return FormatDouble(d);
            case PythonFraction f:
                return f.ToString();
            case bool b:
                return b ? "True" : "False";
            default:
                return value.ToString();
        }
    }

    /// <summary>python's <c>str(float)</c>: the shortest text that reads back exactly.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// python prints a whole float with a trailing '.0'; .NET's round-trip format does
    /// not, so that one case is added back.
    /// </remarks>
    internal static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.IndexOf('.') < 0 && text.IndexOf('e') < 0 && text.IndexOf('E') < 0
            && text.IndexOf("Infinity", StringComparison.Ordinal) < 0)
        {
            text += ".0";
        }

        return text;
    }
}
