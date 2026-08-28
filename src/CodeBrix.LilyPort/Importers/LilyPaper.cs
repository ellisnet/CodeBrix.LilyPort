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

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (Paper and Layout);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>The document's paper block.</summary>
internal sealed class LilyPaper : LilyExpression
{
    /// <summary>Builds the paper block.</summary>
    /// <param name="state">The import this block belongs to.</param>
    internal LilyPaper(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The staff size a document that says nothing gets.</summary>
    internal const double DefaultGlobalStaffSize = 20;

    /// <summary>How big a staff is.</summary>
    internal double GlobalStaffSize { get; set; } = DefaultGlobalStaffSize;

    /// <summary>How wide the page is.</summary>
    internal double PageWidth { get; set; } = -1000;

    /// <summary>How tall the page is.</summary>
    internal double PageHeight { get; set; } = -1000;

    /// <summary>How much space is left at the top.</summary>
    internal double TopMargin { get; set; } = -1000;

    /// <summary>How much space is left at the bottom.</summary>
    internal double BottomMargin { get; set; } = -1000;

    /// <summary>How much space is left at the left.</summary>
    internal double LeftMargin { get; set; } = -1000;

    /// <summary>How much space is left at the right.</summary>
    internal double RightMargin { get; set; } = -1000;

    /// <summary>How much space is left at the left of a system.</summary>
    internal double SystemLeftMargin { get; set; } = -1000;

    /// <summary>How much space is left at the right of a system.</summary>
    internal double SystemRightMargin { get; set; } = -1000;

    /// <summary>How far apart systems sit.</summary>
    internal double SystemDistance { get; set; } = -1000;

    /// <summary>How far the first system sits from the top.</summary>
    internal double TopSystemDistance { get; set; } = -1000;

    /// <summary>How far the first system is indented.</summary>
    internal double Indent { get; set; }

    /// <summary>How far later systems are indented.</summary>
    internal double ShortIndent { get; set; }

    /// <summary>Which number the first page carries.</summary>
    /// <remarks>
    /// ⚠ An integer rather than a length: it is set from python's <c>int()</c> of a
    /// credit or print element's page number, and printed with <c>%s</c>, so '8' rather
    /// than '8.0' reaches the output.
    /// </remarks>
    internal int FirstPageNumber { get; set; }

    /// <summary>The instrument names, for working out the indentation.</summary>
    internal List<string> InstrumentNames { get; } = new List<string>();

    /// <summary>Writes one length setting, if the document gave one.</summary>
    /// <param name="printer">Where to write.</param>
    /// <param name="field">The setting's name.</param>
    /// <param name="value">The length.</param>
    internal static void PrintLengthField(
        LilyOutputPrinter printer, string field, double value)
    {
        if (value >= 0)
        {
            printer.Dump(field + " = " + LilyOutputPrinter.FormatDouble(value) + "\\cm");
            printer.Newline();
        }
    }

    /// <summary>Writes one spacing setting, if the document gave one.</summary>
    /// <param name="printer">Where to write.</param>
    /// <param name="field">The setting's name.</param>
    /// <param name="value">The distance.</param>
    /// <remarks>
    /// We only set the basic-distance field of the alist: musicxml2ly doesn't activate
    /// ragged bottom output, which means that the distances get stretched or squeezed
    /// anyway.
    /// </remarks>
    internal static void PrintAlistField(
        LilyOutputPrinter printer, string field, double value)
    {
        if (value >= 0)
        {
            printer.Dump(field + ".basic-distance = " + LilyOutputPrinter.FormatDouble(value));
            printer.Newline();
        }
    }

    /// <summary>The longest line of any instrument name.</summary>
    /// <returns>The line.</returns>
    internal string GetLongestInstrumentName()
    {
        string result = string.Empty;
        foreach (string name in InstrumentNames)
        {
            foreach (string line in name.Split('\n'))
            {
                if (MusicXmlUtilities.PythonLength(line)
                    > MusicXmlUtilities.PythonLength(result))
                {
                    result = line;
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (GlobalStaffSize > 0 && GlobalStaffSize != DefaultGlobalStaffSize)
        {
            printer.Dump("#(set-global-staff-size "
                         + LilyOutputPrinter.FormatDouble(GlobalStaffSize) + ")");
            printer.Newline();
        }

        printer.Dump("\\paper {");
        printer.Newline();
        if (FirstPageNumber > 0)
        {
            printer.Dump("first-page-number = "
                         + FirstPageNumber.ToString(CultureInfo.InvariantCulture));
            printer.Newline();
            printer.Dump("print-first-page-number = ##t");
            printer.Newline();
        }

        PrintLengthField(printer, "paper-width", PageWidth);
        PrintLengthField(printer, "paper-height", PageHeight);
        PrintLengthField(printer, "top-margin", TopMargin);
        PrintLengthField(printer, "bottom-margin", BottomMargin);
        PrintLengthField(printer, "left-margin", LeftMargin);
        //TODO (upstream's): maybe set line-width instead of right-margin?
        PrintLengthField(printer, "right-margin", RightMargin);
        //TODO (upstream's): What's the corresponding setting for the system margins?

        //The <system-distance> element gives the distance "from the bottom line of the
        //previous system to the top line of the current system". LilyPond, however,
        //takes the measure between the vertical centers of the staves. We thus add one
        //staff height.
        //
        //Note that in MusicXML you can change the system-to-system distance anywhere,
        //which doesn't make sense for LilyPond. Consequently, <system-distance>
        //children of <print> are ignored, which unfortunately reduces the usefulness
        //of the value.
        PrintAlistField(printer, "system-system-spacing", SystemDistance + 4);

        //<top-system-distance> is similar to <system-distance> and thus of limited use
        //only. In particular, it gets ignored on the first page because it doesn't
        //take top markup into account.
        //
        //In MusicXML, the value "is measured from the page's top margin to the top line
        //of the first system". We thus add half a staff height.
        PrintAlistField(printer, "top-system-spacing", TopSystemDistance + 2);

        //TODO (upstream's): Compute the indentation with the instrument name lengths
        //TODO (upstream's): font width ?
        double charPerCm =
            MusicXmlUtilities.PythonLength(GetLongestInstrumentName()) * 13 / PageWidth;
        if (charPerCm != 0)
        {
            if (Indent != 0)
            {
                PrintLengthField(
                    printer, "indent", MusicXmlUtilities.RoundToTwoDigits(Indent / charPerCm));
            }

            if (ShortIndent != 0)
            {
                PrintLengthField(
                    printer, "short-indent",
                    MusicXmlUtilities.RoundToTwoDigits(ShortIndent / charPerCm));
            }
        }

        if (!State.GetTagline())
        {
            printer.Dump("tagline = ##f");
            printer.Newline();
        }

        printer.Dump("}");
        printer.PrintVerbatim("\n");
        printer.Newline();
    }
}

/// <summary>The document's layout block.</summary>
internal sealed class LilyLayout : LilyExpression
{
    /// <summary>Builds the layout block.</summary>
    /// <param name="state">The import this block belongs to.</param>
    internal LilyLayout(MusicXmlImportState state)
        : base(state)
    {
    }

    private readonly List<string> _contextOrder = new List<string>();

    private readonly Dictionary<string, List<string>> _contextDict
        = new Dictionary<string, List<string>>(StringComparer.Ordinal);

    /// <summary>Whether the block has anything in it.</summary>
    internal bool HasContexts => _contextDict.Count > 0;

    /// <summary>Makes sure one context is mentioned.</summary>
    /// <param name="context">The context name.</param>
    internal void AddContext(string context)
    {
        if (!_contextDict.ContainsKey(context))
        {
            _contextDict[context] = new List<string>();
            _contextOrder.Add(context);
        }
    }

    /// <summary>Adds one setting to a context, unless it is already there.</summary>
    /// <param name="context">The context name.</param>
    /// <param name="item">The setting.</param>
    internal void SetContextItem(string context, string item)
    {
        AddContext(context);
        if (!_contextDict[context].Contains(item))
        {
            _contextDict[context].Add(item);
        }
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (_contextDict.Count == 0)
        {
            return;
        }

        printer.Dump("\\layout {");
        printer.Newline();
        foreach (string context in _contextOrder)
        {
            printer.Dump("\\context {");
            printer.Newline();
            printer.Dump("\\" + context);
            printer.Newline();
            foreach (string definition in _contextDict[context])
            {
                printer.Dump(definition);
                printer.Newline();
            }

            printer.Dump("}");
            printer.Newline();
        }

        printer.Dump("}");
        printer.PrintVerbatim("\n");
        printer.Newline();
    }
}
