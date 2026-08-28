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

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (extract_paper_information, extract_score_information, extract_score_structure and the helpers around them);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>Where one part group starts and where it stops.</summary>
/// <remarks>
/// This lives in the staff list during the grouping pass and is replaced by a real
/// group before printing; one that never finds its closing element survives, and
/// upstream then raises <c>AttributeError</c> on it.
/// </remarks>
internal sealed class LilyPartGroupInfo : LilyExpression
{
    /// <summary>Builds the record.</summary>
    /// <param name="state">The import this record belongs to.</param>
    internal LilyPartGroupInfo(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The groups that start here, by their number.</summary>
    internal PythonDictionary<string, MusicXmlNode> Start { get; set; }
        = new PythonDictionary<string, MusicXmlNode>();

    /// <summary>The groups that stop here, by their number.</summary>
    internal PythonDictionary<string, MusicXmlNode> End { get; set; }
        = new PythonDictionary<string, MusicXmlNode>();

    /// <summary>Whether this record says nothing.</summary>
    /// <returns>Whether it is empty.</returns>
    internal bool IsEmpty() => Start.Count + End.Count == 0;

    /// <summary>Records a group that starts here.</summary>
    /// <param name="group">The group element.</param>
    internal void AddStart(MusicXmlNode group) => Start[group.Attribute("number", "1")] = group;

    /// <summary>Records a group that stops here.</summary>
    /// <param name="group">The group element.</param>
    internal void AddEnd(MusicXmlNode group) => End[group.Attribute("number", "1")] = group;

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
        => State.Warning("Unprocessed PartGroupInfo " + this + " encountered");

    /// <summary>This record as LilyPond input, which it never is.</summary>
    /// <returns>Nothing.</returns>
    internal string LyExpression()
    {
        State.Warning("Unprocessed PartGroupInfo " + this + " encountered");
        return string.Empty;
    }
}

/// <summary>The whole of <c>musicxml2ly</c>, as one object per import.</summary>
/// <remarks>
/// ⚠ Upstream is four modules that EACH keep part of their world in module globals AND
/// READ EACH OTHER'S. Those globals live in <see cref="MusicXmlImportState"/>, which
/// every half of the port is handed; this class is the converter's own functions. Same
/// two consequences as for <c>abc2ly</c> and <c>midi2ly</c>: two imports at once cannot
/// see each other, and the diagnostic sink is per-import.
/// </remarks>
internal sealed partial class MusicXmlConverter
{
    /// <summary>
    /// The attributes NOT carried over from one <c>&lt;direction-type&gt;</c> or
    /// <c>&lt;credit&gt;</c> child to the next.
    /// </summary>
    /// <remarks>
    /// Contrary to other parts of MusicXML, "for a series of
    /// <c>&lt;direction-type&gt;</c> children, non-positional formatting attributes are
    /// carried over from previous elements by default." The same holds for children of
    /// <c>&lt;credit&gt;</c>. Unfortunately, it is not defined what 'non-positional
    /// formatting attributes' actually means. The following set of attributes to be
    /// ignored for this 'carry-over' is thus a heuristic guess, combined with attributes
    /// <c>musicxml2ly</c> doesn't handle.
    /// </remarks>
    internal static readonly HashSet<string> FormattingAttributesToIgnore
        = new HashSet<string>(StringComparer.Ordinal)
        {
            "default-x",
            "default-y",
            "dir",  //not handled
            "id",  //not handled
            "relative-x",
            "relative-y",
            "smufl",
            "type",
            "xml:lang",  //not handled
        };

    /// <summary>
    /// The MusicXML enclosure types <c>musicxml2ly</c> provides additional support for.
    /// </summary>
    /// <remarks>
    /// Enclosure types unsupported by LilyPond are filtered out in <c>text_to_ly</c>.
    /// ⚠ Upstream writes this as a parenthesised STRING rather than a tuple — there is
    /// no trailing comma — so <c>in</c> against it is a SUBSTRING test over the six
    /// letters of 'square'. Every enclosure name that reaches it is a whole word, and
    /// the only word that is a substring of 'square' is 'square' itself, so the two
    /// readings agree; the port asks the question upstream meant to ask.
    /// </remarks>
    private static readonly HashSet<string> ExtraEnclosures
        = new HashSet<string>(StringComparer.Ordinal) { "square" };

    /// <summary>A map from credit-type standard values to LilyPond's header fields.</summary>
    private static readonly PythonDictionary<string, string> CreditTypeDict
        = new PythonDictionary<string, string>
        {
            { string.Empty, null },
            { "arranger", "arranger" },
            { "composer", "composer" },
            { "lyricist", "poet" },
            //Not ideal because it persists for the whole document instead of a single
            //page.
            { "part name", "instrument" },
            { "rights", "copyright" },
            { "subtitle", "subtitle" },
            { "title", "title" },
        };

    private static readonly Dictionary<string, string> IgnoreBeamingSoftware
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Dolet 4 for Sibelius, Beta 2", "Dolet 4 for Sibelius, Beta 2" },
            { "Dolet 3.5 for Sibelius", "Dolet 3.5 for Sibelius" },
            { "Dolet 3.4 for Sibelius", "Dolet 3.4 for Sibelius" },
            { "Dolet 3.3 for Sibelius", "Dolet 3.3 for Sibelius" },
            { "Dolet 3.2 for Sibelius", "Dolet 3.2 for Sibelius" },
            { "Dolet 3.1 for Sibelius", "Dolet 3.1 for Sibelius" },
            { "Dolet for Sibelius 1.3", "Dolet for Sibelius 1.3" },
            { "Noteworthy Composer", "Noteworthy Composer's nwc2xm[" },
        };

    /// <summary>Builds the converter.</summary>
    /// <param name="state">The import this converter belongs to.</param>
    internal MusicXmlConverter(MusicXmlImportState state) => State = state;

    /// <summary>The import this converter belongs to.</summary>
    internal MusicXmlImportState State { get; }

    /// <summary>The options this import was given.</summary>
    private MusicXmlImportOptions Options => State.Options;

    /// <summary>Reads the paper block out of the document's defaults.</summary>
    /// <param name="scorePartwise">The document.</param>
    /// <returns>The paper block, or null when the document says nothing about it.</returns>
    internal LilyPaper ExtractPaperInformation(MusicXmlNode scorePartwise)
    {
        LilyPaper earlyReturnVal = null;
        if (!State.GetTagline())
        {
            //We need a `\paper' block for suppressing the tagline.
            earlyReturnVal = State.Paper;
        }

        MusicXmlNode defaults = scorePartwise.GetMaybeExistNamedChild("defaults");
        if (defaults == null)
        {
            return earlyReturnVal;
        }

        double oneTenthInMm = -1;

        MusicXmlNode scaling = defaults.GetMaybeExistNamedChild("scaling");
        if (scaling != null)
        {
            MusicXmlNode millimetersElem = scaling.GetNamedChild("millimeters");
            double millimeters = ParseFloat(millimetersElem.GetText());

            //A normal five-line staff measures 40 tenths vertically.
            MusicXmlNode tenthsElem = scaling.GetMaybeExistNamedChild("tenths");
            double tenths = ParseFloat(tenthsElem.GetText());

            oneTenthInMm = millimeters / tenths;

            //In LilyPond, 72.27 points equal one inch.
            double staffSizeInMm = 40 * oneTenthInMm;
            double staffSizeInPt = staffSizeInMm * 72.27 / 25.4;
            if (staffSizeInPt > 1 && staffSizeInPt < 100)
            {
                State.Paper.GlobalStaffSize = staffSizeInPt;
            }
            else
            {
                string size = staffSizeInPt <= 1 ? "small" : "large";
                State.Warning(
                    "requested global staff size ("
                    + staffSizeInMm.ToString("F2", CultureInfo.InvariantCulture) + "mm="
                    + staffSizeInPt.ToString("F2", CultureInfo.InvariantCulture)
                    + "pt) is too " + size + ", using "
                    + LilyOutputPrinter.FormatNumber(LilyPaper.DefaultGlobalStaffSize)
                    + "pt instead");
            }
        }

        //We need a valid tenth value for the rest of this function.
        if (oneTenthInMm <= 0)
        {
            return earlyReturnVal;
        }

        double TenthsToCm(string text)
            => MusicXmlUtilities.RoundToTwoDigits(ParseFloat(text) * oneTenthInMm / 10);

        double TenthsToStaffSpace(string text)
            => MusicXmlUtilities.RoundToTwoDigits(ParseFloat(text) / 10);

        void SetPaperVariable(
            Action<double> setter, MusicXmlNode parent, string elementName,
            bool relative = false)
        {
            MusicXmlNode element = parent.GetMaybeExistNamedChild(elementName);
            if (element != null)
            {
                setter(
                    relative
                        ? TenthsToStaffSpace(element.GetText())
                        : TenthsToCm(element.GetText()));
            }
        }

        MusicXmlNode pageLayout = defaults.GetMaybeExistNamedChild("page-layout");
        if (pageLayout != null)
        {
            //TODO (upstream's): How can one have different margins for even and odd
            //pages???
            SetPaperVariable(v => State.Paper.PageHeight = v, pageLayout, "page-height");
            SetPaperVariable(v => State.Paper.PageWidth = v, pageLayout, "page-width");

            if (State.ConversionSettings.ConvertPageMargins)
            {
                foreach (MusicXmlNode pageMargins in pageLayout.GetNamedChildren("page-margins"))
                {
                    SetPaperVariable(
                        v => State.Paper.LeftMargin = v, pageMargins, "left-margin");
                    SetPaperVariable(
                        v => State.Paper.RightMargin = v, pageMargins, "right-margin");
                    SetPaperVariable(
                        v => State.Paper.BottomMargin = v, pageMargins, "bottom-margin");
                    SetPaperVariable(
                        v => State.Paper.TopMargin = v, pageMargins, "top-margin");
                }
            }
        }

        MusicXmlNode systemLayout = defaults.GetMaybeExistNamedChild("system-layout");
        if (systemLayout != null)
        {
            MusicXmlNode systemMargins
                = systemLayout.GetMaybeExistNamedChild("system-margins");
            if (systemMargins != null)
            {
                SetPaperVariable(
                    v => State.Paper.SystemLeftMargin = v, systemMargins, "left-margin");
                SetPaperVariable(
                    v => State.Paper.SystemRightMargin = v, systemMargins, "right-margin");
            }

            SetPaperVariable(
                v => State.Paper.SystemDistance = v, systemLayout, "system-distance", true);
            SetPaperVariable(
                v => State.Paper.TopSystemDistance = v, systemLayout,
                "top-system-distance", true);
        }

        //⚠ Upstream reads the staff layouts into locals it never uses; its own TODO says
        //the staff distance needs to be set in the Staff context. The loop is kept for
        //the reads it performs on the tree, which are what upstream performs.
        foreach (MusicXmlNode staffLayout in defaults.GetNamedChildren("staff-layout"))
        {
            _ = staffLayout.Attribute("number", "1");
            _ = staffLayout.GetNamedChild("staff-distance");
        }

        //TODO (upstream's): Finish appearance?, music-font?, word-font?, lyric-font*,
        //lyric-language*
        MusicXmlNode appearance = defaults.GetNamedChild("appearance");
        if (appearance != null)
        {
            foreach (MusicXmlNode lineWidth in appearance.GetNamedChildren("line-width"))
            {
                //Possible types are: beam, bracket, dashes, enclosure, ending, extend,
                //heavy barline, leger, light barline, octave shift, pedal, slur middle,
                //slur tip, staff, stem, tie middle, tie tip, tuplet bracket, and wedge
                _ = lineWidth.Attribute("type");
                _ = TenthsToCm(lineWidth.GetText());
                //TODO (upstream's): Do something with these values!
            }

            foreach (MusicXmlNode noteSize in appearance.GetNamedChildren("note-size"))
            {
                //Possible types: `cue', `grace', `grace-cue', `large'.
                _ = noteSize.Attribute("type");
                _ = TenthsToCm(noteSize.GetText());
                //TODO (upstream's): Do something with these values!
            }

            //<other-appearance> elements have no specified meaning
        }

        //TODO (upstream's): Convert the fonts.
        _ = defaults.GetNamedChild("music-font");
        _ = defaults.GetNamedChild("word-font");
        _ = defaults.GetNamedChildren("lyric-font");

        return State.Paper;
    }

    /// <summary>python's <c>float</c> of an element's text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    private static double ParseFloat(string text)
        => double.Parse(
            text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the header block out of the document's work, identification, movement title
    /// and credit elements.
    /// </summary>
    /// <param name="tree">The document.</param>
    /// <returns>The header block.</returns>
    /// <remarks>
    /// We only handle <c>&lt;credit&gt;</c> elements for the 'credit page' as given by
    /// the command line option <c>--credit-page</c>, ignoring the remaining ones.
    /// </remarks>
    internal LilyHeader ExtractScoreInformation(MusicXmlNode tree)
    {
        LilyHeader header = new LilyHeader(State);
        PythonDictionary<string, MusicXmlCredit> creditDict
            = new PythonDictionary<string, MusicXmlCredit>();

        void SetIfExists(
            string field, string value, string altField = null, bool isMarkup = false)
        {
            if (field == null && altField == null)
            {
                throw new InvalidOperationException(
                    "Neither a field nor an alternative field was named.");
            }

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (field == "texidoc")
            {
                //Don't surround the string with doublequotes yet so that it gets split
                //into words. Doublequotes are added later when the field is written.
                header.SetField(field, value.Replace("\"", "\\\""));
                return;
            }

            value = isMarkup
                ? "\\markup \\normal-text \\normalsize " + value
                : MusicXmlUtilities.EscapeLyOutputString(value);

            if (field != null)
            {
                if (altField != null && creditDict.Count > 0)
                {
                    header.SetField(MusicXmlUtilities.EscapeLyOutputString(altField), value);
                }
                else
                {
                    header.SetField(MusicXmlUtilities.EscapeLyOutputString(field), value);
                }
            }
            else if (creditDict.Count > 0)
            {
                header.SetField(MusicXmlUtilities.EscapeLyOutputString(altField), value);
            }
        }

        //If we have one or more `<credit>' elements, don't use metadata for typesetting
        //headers.
        string creditPageString = Options.CreditPage.ToString(CultureInfo.InvariantCulture);
        List<MusicXmlCredit> credits = tree.GetNamedChildren("credit")
            .Cast<MusicXmlCredit>()
            .Where(c => c.Attribute("page", "1") == creditPageString)
            .ToList();

        MusicXmlCreditGroup creditGroup = new MusicXmlCreditGroup(credits);

        //Collect all `<credit>' entries, with and without a type.
        PythonDictionary<string, MusicXmlCredit> creditNamedDict
            = new PythonDictionary<string, MusicXmlCredit>();
        PythonDictionary<string, MusicXmlCredit> creditGuessedDict
            = new PythonDictionary<string, MusicXmlCredit>();
        int noTypeCount = 1;
        foreach (MusicXmlCredit credit in credits)
        {
            string creditType = credit.GetCreditType();
            if (creditType == "page number")
            {
                //We only look at the first `<credit-words>' element. Because LilyPond's
                //mechanism to modify the appearance of page numbers is not done with
                //`\markup' we cannot use any style attributes.
                List<MusicXmlNode> words = credit.GetNamedChildren("credit-words");
                if (words.Count > 0)
                {
                    string pageNumber = words[0].GetText();
                    if (int.TryParse(
                            pageNumber, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out int parsed))
                    {
                        if (parsed > 1)
                        {
                            State.Paper.FirstPageNumber = parsed;
                        }
                    }
                    else
                    {
                        State.Warning(
                            "cannot use non-integer page number '" + pageNumber + "'");
                    }
                }

                continue;
            }

            if (!string.IsNullOrEmpty(creditType))
            {
                string headerType = CreditTypeDict.GetOrDefault(creditType);
                if (headerType != null)
                {
                    creditNamedDict[headerType] = credit;
                }
                else
                {
                    //While LilyPond can't process unknown header fields without manual
                    //support (i.e., code supplied by the user), it still makes sense to
                    //output them.
                    creditNamedDict["credit: " + creditType] = credit;
                }
            }
            else
            {
                string guessedType = credit.FindType(creditGroup);
                string headerType = CreditTypeDict.GetOrDefault(guessedType);
                if (headerType != null)
                {
                    creditGuessedDict[headerType] = credit;
                }
                else
                {
                    //If we can't guess a type for an entry, output the data with a
                    //sequence number in the field name.
                    creditGuessedDict[
                            "credit: " + noTypeCount.ToString(CultureInfo.InvariantCulture)]
                        = credit;
                    noTypeCount += 1;
                }
            }
        }

        //Only use guessed entries for entries that don't have a type.
        creditDict = PythonDictionary<string, MusicXmlCredit>.Merge(
            creditGuessedDict, creditNamedDict);

        foreach ((string type, MusicXmlCredit credit) in creditDict.Items())
        {
            List<LilyMarkupElement> elements = new List<LilyMarkupElement>();
            Dictionary<string, object> attributes = new Dictionary<string, object>();

            List<MusicXmlNode> creditChildren = credit.GetAllChildren()
                .Where(c => c is MusicXmlCreditWords || c is MusicXmlCreditSymbol)
                .ToList();
            foreach (MusicXmlNode element in creditChildren)
            {
                //Attributes are 'carried over', so update attributes with data from the
                //current element.
                foreach (KeyValuePair<string, string> attribute in element.AttributeDict)
                {
                    if (!FormattingAttributesToIgnore.Contains(attribute.Key))
                    {
                        attributes[attribute.Key] = attribute.Value;
                    }
                }

                string enclosure = attributes.TryGetValue("enclosure", out object value)
                    ? value as string
                    : null;
                if (enclosure != null && ExtraEnclosures.Contains(enclosure))
                {
                    State.NeededAdditionalDefinitions.Add(enclosure);
                }

                elements.Add(
                    new LilyMarkupElement(
                        element, new Dictionary<string, object>(attributes)));
            }

            SetIfExists(type, LilyMarkup.TextToLy(State, elements), null, true);
        }

        //Emit metadata.
        MusicXmlNode work = tree.GetMaybeExistNamedChild("work");
        string workTitleText = string.Empty;
        if (work != null)
        {
            MusicXmlWork typedWork = (MusicXmlWork)work;
            SetIfExists("opus", typedWork.GetWorkNumber(), "work-number");

            workTitleText = typedWork.GetWorkTitle();
            SetIfExists("title", workTitleText, "work-title");

            //TODO (upstream's): Support inclusion of other MusicXML files via the
            //`<opus>' element; see
            //
            //  https://www.w3.org/2021/06/musicxml40/opus-reference/
            //
            //for details.
        }

        MusicXmlNode movementTitle = tree.GetMaybeExistNamedChild("movement-title");
        string movementTitleText = string.Empty;
        if (movementTitle != null)
        {
            movementTitleText = movementTitle.GetText();
        }

        if (!string.IsNullOrEmpty(movementTitleText))
        {
            string field = !string.IsNullOrEmpty(workTitleText) ? "subtitle" : "title";
            SetIfExists(field, movementTitleText, "movement-title");
        }

        MusicXmlNode movementNumber = tree.GetMaybeExistNamedChild("movement-number");
        if (movementNumber != null)
        {
            //TODO (upstream's): The movement number should be visible in the score,
            //probably in the 'piece' field of `\header'.
            SetIfExists(null, movementNumber.GetText(), "movement-number");
        }

        foreach (MusicXmlNode identification in tree.GetNamedChildren("identification"))
        {
            MusicXmlIdentification ids = (MusicXmlIdentification)identification;

            //<rights>
            SetIfExists("copyright", ids.GetRights(), "id: copyright");

            //<creator>
            SetIfExists("composer", ids.GetComposer(), "id: composer");
            SetIfExists("arranger", ids.GetArranger(), "id: arranger");
            SetIfExists("editor", ids.GetEditor(), "id: editor");
            SetIfExists("poet", ids.GetPoet(), "id: lyricist");

            //<encoding>
            //We only get the data from the first child, irrespective of its 'type'
            //attribute.
            string software = ids.GetEncodingSoftware();
            SetIfExists("id: software", software);  //<software>
            SetIfExists("id: encoding-date", ids.GetEncodingDate());  //<encoding-date>
            SetIfExists("id: encoder", ids.GetEncodingPerson());  //<encoder>
            //<encoding-description>
            SetIfExists("id: encoding-description", ids.GetEncodingDescription());

            //<source>
            SetIfExists("id: source", ids.GetSource());

            //<miscellaneous>
            //The element `<miscellaneous-field name="description">' becomes the
            //`texidoc' field in `\header'.
            SetIfExists("texidoc", ids.GetFileDescription());

            //TODO (upstream's): Handle `<relation>' element.

            //Finally, apply the required compatibility modes.
            //
            //Some applications created invalid MusicXML files, so we need to apply some
            //compatibility settings, e.g., ignoring some features or elements in such
            //files.
            foreach (string line in SplitLines(software))
            {
                //"Sibelius 5.1" with the "Dolet 3.4 for Sibelius" plugin is missing all
                //beam ends; we thus ignore all beaming information.
                if (IgnoreBeamingSoftware.TryGetValue(line, out string appDescription))
                {
                    State.ConversionSettings.IgnoreBeaming = true;
                    State.Warning(
                        "Encountered file created by " + appDescription
                        + ", containing wrong beaming information. All beaming "
                        + "information in the MusicXML file will be ignored");
                }

                //Finale encodes ottava ends differently than many other applications.
                if (line.Contains("Finale"))
                {
                    State.OttavasEndEarlyOption = "t";
                }
            }
        }

        //TODO (upstream's): Check for other unsupported features
        return header;
    }

    /// <summary>python's <c>str.splitlines</c>.</summary>
    /// <param name="text">The text, which may be null.</param>
    /// <returns>The lines, without their terminators.</returns>
    /// <remarks>
    /// ⚠ python splits on more than <c>\n</c> and <c>\r\n</c>; the ones a MusicXML
    /// document can carry through an XML parser are those two, because XML normalises
    /// every other line terminator to <c>\n</c> before the document model sees it.
    /// </remarks>
    private static List<string> SplitLines(string text)
    {
        List<string> lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int end = i;
                if (end > start && text[end - 1] == '\r')
                {
                    end -= 1;
                }

                lines.Add(text.Substring(start, end - start));
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text.Substring(start));
        }

        return lines;
    }
}
