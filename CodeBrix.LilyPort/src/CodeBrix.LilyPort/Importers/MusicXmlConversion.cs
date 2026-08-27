/*
   This file is part of LilyPond, the GNU music typesetter.

   Copyright (C) 2016--2026 John Gourlay <john@weathervanefarm.net>

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
using CodeBrix.LilyPort.ConvertLy;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicxml2ly_conversion.py;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>The two conversions that sit between the input model and the output one.</summary>
internal static class MusicXmlConversion
{
    /// <summary>Turns a MusicXML step letter into LilyPond's step number.</summary>
    /// <param name="step">The letter.</param>
    /// <returns>The number, or null when there is no letter.</returns>
    internal static int? MusicXmlStepToLily(string step)
        => string.IsNullOrEmpty(step) ? (int?)null : (step[0] - 'A' + 7 - 2) % 7;

    /// <summary>Reads an ending element's volta numbers.</summary>
    /// <param name="numberString">The 'number' attribute.</param>
    /// <returns>The numbers, or an empty list when the attribute cannot be read.</returns>
    internal static List<int> MusicXmlNumbersToVolte(string numberString)
    {
        List<int> result = new List<int>();

        //Volta numbers are separated by commas, optionally followed by a space (XML
        //extraction already removed leading and trailing whitespace, also squeezing
        //sequences of whitespace characters inbetween into single spaces).
        //
        //An empty string is also valid input, but the return value is still an empty
        //list.
        List<string> numbers = PythonRegex.Split(", ?", numberString);
        foreach (string number in numbers)
        {
            //According to the standard, no leading zeroes are allowed. We ignore this
            //restriction, simply using `int' for the conversion to integers.
            if (!int.TryParse(
                    number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                return new List<int>();
            }

            if (n > 0)
            {
                result.Add(n);
            }
            else
            {
                return new List<int>();
            }
        }

        return result;
    }
}

/// <summary>
/// A repeat or ending the converter carries alongside the music until it knows what to
/// wrap in what.
/// </summary>
/// <remarks>
/// A marker is never printed: it is folded into a repeat structure first. One that
/// reaches the printer is a defect, and says so.
/// </remarks>
internal class MusicXmlMarker : LilyMusic
{
    /// <summary>Builds the marker.</summary>
    /// <param name="state">The import this marker belongs to.</param>
    internal MusicXmlMarker(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which way the marker faces.</summary>
    internal int Direction { get; set; }

    /// <summary>
    /// Whether this marker is one of upstream's <c>EndingMarker</c> family.
    /// </summary>
    /// <remarks>
    /// ⚠ THIS IS THE HALF OF UPSTREAM'S HIERARCHY C# CANNOT EXPRESS.
    /// <c>RepeatEndingMarker</c> derives from BOTH marker classes there, so
    /// <c>isinstance(x, EndingMarker)</c> answers true for it; here it derives from
    /// the repeat half only, and every site that asked that question asks this
    /// instead. The repeat half needs no such property: <c>is</c> already answers it.
    /// </remarks>
    internal virtual bool IsEndingMarker => false;

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
        => State.Warning("Encountered unprocessed marker " + GetType().Name + "\n");

    /// <inheritdoc/>
    internal override string LyExpression() => string.Empty;
}

/// <summary>A repeat sign.</summary>
internal class MusicXmlRepeatMarker : MusicXmlMarker
{
    /// <summary>Builds the marker.</summary>
    /// <param name="state">The import this marker belongs to.</param>
    internal MusicXmlRepeatMarker(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How many times the passage is played.</summary>
    /// <remarks>A simple repeat played twice is the default.</remarks>
    internal int? Times { get; set; } = 2;

    /// <summary>Whether the repeat opens the piece.</summary>
    internal bool AtStart { get; set; }
}

/// <summary>A volta bracket.</summary>
internal class MusicXmlEndingMarker : MusicXmlMarker
{
    /// <summary>Builds the marker.</summary>
    /// <param name="state">The import this marker belongs to.</param>
    internal MusicXmlEndingMarker(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    internal override bool IsEndingMarker => true;

    /// <summary>The element this marker was read from.</summary>
    internal MusicXmlNode MxlEvent { get; set; }

    /// <summary>Which times through the bracket applies to.</summary>
    internal List<int> Volte { get; set; } = new List<int>();
}

/// <summary>A repeat sign and a volta bracket at the same place.</summary>
/// <remarks>
/// ⚠ UPSTREAM INHERITS FROM BOTH <c>RepeatMarker</c> AND <c>EndingMarker</c> and calls
/// NEITHER constructor, assigning all six fields itself. C# has no multiple
/// inheritance, so the port derives from the repeat half and carries the ending half's
/// two fields — which is exactly the set upstream assigns, in the same order. The one
/// thing lost is the <c>isinstance(x, EndingMarker)</c> test, so every site that asked
/// it asks <see cref="IsEndingMarker"/> instead.
/// </remarks>
internal sealed class MusicXmlRepeatEndingMarker : MusicXmlRepeatMarker
{
    /// <summary>Builds the marker out of the two it replaces.</summary>
    /// <param name="state">The import this marker belongs to.</param>
    /// <param name="repeat">The repeat half.</param>
    /// <param name="ending">The ending half.</param>
    internal MusicXmlRepeatEndingMarker(
        MusicXmlImportState state, MusicXmlRepeatMarker repeat, MusicXmlEndingMarker ending)
        : base(state)
    {
        Direction = repeat.Direction;
        //According to the standard, `times' is not used if there is an `<ending>'
        //element at the same time.
        Times = null;
        AtStart = repeat.AtStart;
        MxlEvent = ending.MxlEvent;
        Volte = ending.Volte;
    }

    /// <inheritdoc/>
    internal override bool IsEndingMarker => true;

    /// <summary>The element the ending half was read from.</summary>
    internal MusicXmlNode MxlEvent { get; set; }

    /// <summary>Which times through the bracket applies to.</summary>
    internal List<int> Volte { get; set; } = new List<int>();
}
