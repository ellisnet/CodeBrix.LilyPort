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

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/musicxml2ly.py (musicxml_clef_staff_details_to_lily, musicxml_time_to_lily, musicxml_key_to_lily, musicxml_transpose_to_lily, musicxml_measure_style_to_lily, musicxml_attributes_to_lily, the display-text helpers and musicxml_print_to_lily);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

internal sealed partial class MusicXmlConverter
{
    /// <summary>
    /// The pitch of the middle line (i.e., line 3) if the clef is positioned on 'line 0'.
    /// </summary>
    private static readonly Dictionary<string, (int Step, int Octave)> ClefMiddleLinePitch
        = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            { "G", (3, 1) },
            { "C", (6, 0) },
            { "F", (2, 0) },
            { "percussion", (6, 0) },
            { "PERC", (6, 0) },
        };

    private static readonly (int Step, int Alteration)[] ChromaticShiftPitches
        = {
            (0, 0), (0, 1), (1, 0), (2, -1), (2, 0),
            (3, 0), (3, 1), (4, 0), (5, -1), (5, 0),
            (6, -1), (6, 0),
        };

    private static readonly Dictionary<string, string> TimeSignatureStyles
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "single-number", "'single-number" },
            { "cut", null },
            { "common", null },
            { "normal", "'()" },
        };

    /// <summary>The pitch the middle staff line carries under a clef.</summary>
    /// <param name="clef">The clef.</param>
    /// <returns>The pitch, or null when the clef has no letter we know.</returns>
    private LilyPitch GetClefPitch(LilyClefChange clef)
    {
        if (clef.Type == null || !ClefMiddleLinePitch.ContainsKey(clef.Type))
        {
            return null;
        }

        LilyPitch pitch = new LilyPitch(State);

        (int step, int octave) = ClefMiddleLinePitch[clef.Type];

        if (clef.Position.HasValue)
        {
            step -= clef.Position.Value * 2;
        }
        else
        {
            step = 6;
        }

        if (clef.Lines.HasValue)
        {
            step -= 5 - clef.Lines.Value;
        }

        if (step < 0)
        {
            pitch.Step = step + 7;
            pitch.Octave = octave - 1 + clef.Octave.Value;
        }
        else if (step > 6)
        {
            pitch.Step = step - 7;
            pitch.Octave = octave + 1 + clef.Octave.Value;
        }
        else
        {
            pitch.Step = step;
            pitch.Octave = octave + clef.Octave.Value;
        }

        return pitch;
    }

    /// <summary>Turns a clef and a staff-details element into one output-side event.</summary>
    /// <param name="attributes">The attributes element.</param>
    /// <returns>The event, or null when neither is present.</returns>
    /// <remarks>We handle both together for technical reasons.</remarks>
    internal LilyClefChange MusicXmlClefStaffDetailsToLily(MusicXmlAttributes attributes)
    {
        LilyClefChange ev = new LilyClefChange(State);

        MusicXmlClefInfo clefInformation = attributes.GetClefInformation();
        if (clefInformation != null)
        {
            ev.Type = clefInformation.Sign;
            ev.Position = clefInformation.Line;
            ev.Octave = clefInformation.OctaveChange;
            ev.Color = clefInformation.Color;
            ev.FontSize = clefInformation.FontSize;
            ev.Visible = clefInformation.PrintObject;
        }

        MusicXmlNode staffLines = null;

        MusicXmlNode details = attributes.GetMaybeExistNamedChild("staff-details");
        if (details != null)
        {
            //TODO (upstream's): Handle staff-type, staff-tuning, capo, staff-size
            staffLines = details.GetMaybeExistNamedChild("staff-lines");
            if (staffLines != null)
            {
                ev.Lines = int.Parse(staffLines.GetText().Trim(), CultureInfo.InvariantCulture);

                //TODO (upstream's): Handle `color' attributes of staff lines individually.
                //Handle `line-type'.
                List<MusicXmlNode> lineDetails = details.GetNamedChildren("line-detail");
                if (lineDetails.Count > 0)
                {
                    ev.LineDetails = new Dictionary<int, string>();
                    foreach (MusicXmlNode lineDetail in lineDetails)
                    {
                        int line = int.Parse(
                            lineDetail.Attribute("line", "1"), CultureInfo.InvariantCulture);
                        string printObject = lineDetail.Attribute("print-object", "yes");
                        if (ev.LinesColor == null)
                        {
                            ev.LinesColor = lineDetail.Attribute("color");
                        }

                        ev.LineDetails[line] = printObject;
                    }
                }
            }
        }

        ev.Pitch = GetClefPitch(ev);

        if (clefInformation == null && staffLines == null)
        {
            return null;
        }

        //The percussion clef is a special case.
        if (ev.Type == "percussion" || ev.Type == "PERC" || staffLines != null)
        {
            State.NeededAdditionalDefinitions.Add("staff-lines");
        }

        return ev;
    }

    /// <summary>Shifts a signature's durations by the option's amount, in place.</summary>
    /// <param name="signature">The signature.</param>
    /// <returns>The signature, re-wrapped the way upstream's own unwrap leaves it.</returns>
    /// <remarks>
    /// ⚠ Upstream MUTATES the lists it was handed rather than copying them, and its
    /// caller relies on that for the alternate signature. The port mutates the same
    /// lists.
    /// </remarks>
    private MusicXmlTimeSignature ShiftDurations(MusicXmlTimeSignature signature)
    {
        List<List<int>> parts
            = signature.Kind == MusicXmlTimeSignature.SignatureKind.Compound
                ? signature.Parts
                : new List<List<int>> { signature.Beats };

        List<int> denominators = parts.Select(part => part[part.Count - 1]).ToList();

        //Starting with python 3.9, `gcd' allows an arbitrary number of arguments.
        int gcdDenominator = denominators
            .Aggregate(0, (accumulator, value) => Gcd(accumulator, value));

        int shift = Options.ShiftDurations;
        if (shift < 0)
        {
            int denominatorShift = 0;
            while (shift < 0)
            {
                //Only make the nominator larger if we no longer can make the denominator
                //smaller.
                if (gcdDenominator % 2 != 0)
                {
                    break;
                }

                gcdDenominator >>= 1;
                denominatorShift += 1;
                shift += 1;
            }

            int nominatorShift = -shift;

            foreach (List<int> part in parts)
            {
                for (int i = 0; i < part.Count - 1; i++)
                {
                    part[i] <<= nominatorShift;
                }

                part[part.Count - 1] >>= denominatorShift;
            }
        }
        else
        {
            foreach (List<int> part in parts)
            {
                part[part.Count - 1] <<= shift;
            }
        }

        return parts.Count == 1
            ? new MusicXmlTimeSignature(parts[0])
            : new MusicXmlTimeSignature(parts);
    }

    /// <summary>python's <c>math.gcd</c> for two values.</summary>
    /// <param name="first">The first value.</param>
    /// <param name="second">The second value.</param>
    /// <returns>The greatest common divisor.</returns>
    private static int Gcd(int first, int second)
    {
        first = Math.Abs(first);
        second = Math.Abs(second);
        while (second != 0)
        {
            (first, second) = (second, first % second);
        }

        return first;
    }

    /// <summary>Turns a time element into an output-side event.</summary>
    /// <param name="attributes">The attributes element.</param>
    /// <returns>The event, or null when the document says nothing usable.</returns>
    internal LilyTimeSignatureChange MusicXmlTimeToLily(MusicXmlAttributes attributes)
    {
        //⚠ Upstream writes `get_time_signature().copy()' inside a try that catches
        //AttributeError, because the two senza-misura readings are an int and a string,
        //neither of which has `copy'. The port asks the question directly. The copy is
        //SHALLOW, so a compound signature's inner lists stay shared with the cache, which
        //is what makes the shift below reach it.
        MusicXmlTimeSignature signature = attributes.GetTimeSignature();
        if (signature != null
            && (signature.Kind == MusicXmlTimeSignature.SignatureKind.Simple
                || signature.Kind == MusicXmlTimeSignature.SignatureKind.Compound))
        {
            signature = signature.ShallowCopy();
        }

        if (signature == null
            || signature.Kind == MusicXmlTimeSignature.SignatureKind.SenzaMisuraEmpty
            || IsEmptySignature(signature))
        {
            //No time signature or an empty senza-misura time signature.
            return null;
        }

        LilyTimeSignatureChange change = new LilyTimeSignatureChange(State);

        if (signature.Kind == MusicXmlTimeSignature.SignatureKind.SenzaMisuraCross)
        {
            //An X-shaped senza-misura time signature.
            State.LayoutInformation.SetContextItem(
                "Staff", "\\senzaMisuraTimeSignatureX");
            change.Fractions = signature;
            return change;
        }

        if (Options.ShiftDurations != 0)
        {
            signature = ShiftDurations(signature);
        }

        change.Fractions = signature;

        MusicXmlNode timeElement = attributes.GetMaybeExistNamedChild("time");
        string symbol = timeElement?.Attribute("symbol");
        if (symbol != null)
        {
            change.Style = TimeSignatureStyles.TryGetValue(symbol, out string style)
                ? style
                : "'()";
        }
        else
        {
            change.Style = "'()";
        }

        if (timeElement != null && timeElement.Attribute("print-object", "yes") == "no")
        {
            change.Visible = false;
        }

        change.Color = timeElement?.Attribute("color");
        change.FontSize = timeElement?.Attribute("font-size");

        change.Alternate = attributes.GetAlternateTimeSignature();
        if (change.Alternate != null)
        {
            State.NeededAdditionalDefinitions.Add("time-alternate");
            if (Options.ShiftDurations != 0)
            {
                change.Alternate = ShiftDurations(change.Alternate);
            }

            change.AlternateStyle = attributes.GetAlternateTimeSignatureStyle();
        }

        return change;
    }

    /// <summary>python's <c>len(sig) == 0</c> for whatever a signature is held as.</summary>
    /// <param name="signature">The signature.</param>
    /// <returns>Whether it holds nothing.</returns>
    private static bool IsEmptySignature(MusicXmlTimeSignature signature)
        => signature.Kind == MusicXmlTimeSignature.SignatureKind.Compound
            ? signature.Parts.Count == 0
            : signature.Kind == MusicXmlTimeSignature.SignatureKind.Simple
              && signature.Beats.Count == 0;

    /// <summary>Turns a key element into an output-side event.</summary>
    /// <param name="attributes">The attributes element.</param>
    /// <returns>The event, or null when the document says nothing usable.</returns>
    internal LilyKeySignatureChange MusicXmlKeyToLily(MusicXmlAttributes attributes)
    {
        MusicXmlKeyInfo keySignature = attributes.GetKeySignature();
        if (keySignature == null)
        {
            State.Warning("Unable to extract key signature!");
            return null;
        }

        State.LayoutInformation.SetContextItem("Staff", "printKeyCancellation = ##f");

        LilyKeySignatureChange change = new LilyKeySignatureChange(State);
        change.Color = keySignature.Color;
        change.FontSize = keySignature.FontSize;
        change.Visible = keySignature.Visible;

        if (keySignature.IsTraditional)
        {
            //Standard key signature, (fifths, mode).
            int fifths = keySignature.Fifths.Value;
            string mode = keySignature.Mode;
            change.Fifths = fifths;
            change.Mode = mode;

            LilyPitch startPitch = new LilyPitch(State);
            startPitch.Octave = 0;
            if (ModeSteps.TryGetValue(mode ?? string.Empty, out (int Step, int Alter) tonic))
            {
                startPitch.Step = tonic.Step;
                startPitch.Alteration = tonic.Alter;
            }
            else
            {
                State.Warning(
                    "unknown mode " + mode
                    + ", expecting 'major' or 'minor' or a church mode!");
            }

            LilyPitch fifth = new LilyPitch(State);
            fifth.Step = 4;
            if (fifths < 0)
            {
                fifths *= -1;
                fifth.Step *= -1;
                fifth.Normalize();
            }

            for (int x = 0; x < fifths; x++)
            {
                startPitch = startPitch.Transposed(fifth);
            }

            change.Tonic = startPitch;
        }
        else
        {
            //Non-standard key signature of the form [[step, alter<, octave>], ...].
            //MusicXML contains C, D, E, F, G, A, B as steps, lily uses 0-7, so convert.
            List<LilyKeyAlteration> alterations = new List<LilyKeyAlteration>();
            foreach (MusicXmlKeyAlteration alteration in keySignature.Alterations)
            {
                int step = MusicXmlConversion.MusicXmlStepToLily(alteration.Step).Value;
                alterations.Add(
                    alteration.Octave.HasValue
                        ? new LilyKeyAlteration(step, alteration.Alter, alteration.Octave.Value)
                        : new LilyKeyAlteration(step, alteration.Alter));
            }

            change.NonStandardAlterations = alterations;
        }

        (int Cancel, string Location)? cancel = attributes.GetCancellation();
        if (cancel.HasValue)
        {
            change.CancelFifths = cancel.Value.Cancel;
            change.CancelLocation = cancel.Value.Location;

            if (change.CancelLocation != "left")
            {
                State.NeededAdditionalDefinitions.Add("insert-before");
            }

            if (change.CancelLocation == "before-barline")
            {
                State.NeededAdditionalDefinitions.Add("cancel-before-barline");
            }
            else if (change.CancelLocation == "right")
            {
                State.NeededAdditionalDefinitions.Add("cancel-after-key");
            }
        }

        return change;
    }

    private static readonly Dictionary<string, (int Step, int Alter)> ModeSteps
        = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            { "major", (0, 0) },
            { "minor", (5, 0) },
            { "ionian", (0, 0) },
            { "dorian", (1, 0) },
            { "phrygian", (2, 0) },
            { "lydian", (3, 0) },
            { "mixolydian", (4, 0) },
            { "aeolian", (5, 0) },
            { "locrian", (6, 0) },
        };

    /// <summary>Turns a transpose element into an output-side event.</summary>
    /// <param name="attributes">The attributes element.</param>
    /// <returns>The event, or null when there is no transposition.</returns>
    internal LilyTransposition MusicXmlTransposeToLily(MusicXmlAttributes attributes)
    {
        MusicXmlNode transpose = attributes.GetTransposition();
        if (transpose == null)
        {
            return null;
        }

        LilyPitch shift = new LilyPitch(State);
        MusicXmlNode octaveChange = transpose.GetMaybeExistNamedChild("octave-change");
        if (octaveChange != null)
        {
            shift.Octave = int.Parse(
                octaveChange.GetText().Trim(), CultureInfo.InvariantCulture);
        }

        int chromaticShift = int.Parse(
            transpose.GetNamedChild("chromatic").GetText().Trim(),
            CultureInfo.InvariantCulture);
        int chromaticShiftNormalized = PythonModulo(chromaticShift, 12);
        (int step, int alteration) = ChromaticShiftPitches[chromaticShiftNormalized];
        shift.Step = step;
        shift.Alteration = alteration;

        shift.Octave += (chromaticShift - chromaticShiftNormalized) / 12;

        MusicXmlNode diatonic = transpose.GetMaybeExistNamedChild("diatonic");
        if (diatonic != null)
        {
            int diatonicStep = PythonModulo(
                int.Parse(diatonic.GetText().Trim(), CultureInfo.InvariantCulture), 7);
            if (diatonicStep != shift.Step)
            {
                //We got the alter incorrect!
                double oldSemitones = shift.Semitones();
                shift.Step = diatonicStep;
                double newSemitones = shift.Semitones();
                shift.Alteration += oldSemitones - newSemitones;
            }
        }

        LilyTransposition transposition = new LilyTransposition(State);
        transposition.Pitch = new LilyPitch(State).Transposed(shift);
        return transposition;
    }

    /// <summary>python's <c>%</c>, which always answers the divisor's sign.</summary>
    /// <param name="value">The value.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The remainder.</returns>
    private static int PythonModulo(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    /// <summary>Turns the measure-style elements into output-side events.</summary>
    /// <param name="attributes">The attributes element.</param>
    /// <returns>The events, or null when there are none.</returns>
    internal List<LilyMusic> MusicXmlMeasureStyleToLily(MusicXmlAttributes attributes)
    {
        List<MusicXmlNode> details = attributes.GetNamedChildren("measure-style");
        if (details.Count == 0)
        {
            return null;
        }

        //TODO (upstream's): Handle `measure-repeat', `beat-repeat', `slash'.
        List<LilyMusic> result = new List<LilyMusic>();
        foreach (MusicXmlNode detail in details)
        {
            string color = detail.Attribute("color");
            string fontSize = detail.Attribute("font-size");

            MusicXmlNode multipleRest = detail.GetMaybeExistNamedChild("multiple-rest");
            if (multipleRest != null)
            {
                LilyMeasureStyleEvent measureStyleEvent = new LilyMeasureStyleEvent(State);
                measureStyleEvent.Color = color;
                measureStyleEvent.FontSize = fontSize;

                measureStyleEvent.MultipleRestLength = int.Parse(
                    multipleRest.GetText().Trim(), CultureInfo.InvariantCulture);

                if (multipleRest.Attribute("use-symbols", "no") == "yes")
                {
                    measureStyleEvent.UseSymbols = true;
                }

                result.Add(measureStyleEvent);
            }
        }

        return result;
    }

    /// <summary>Turns an attributes element into the output-side events it asks for.</summary>
    /// <param name="attrs">The attributes element.</param>
    /// <returns>The events.</returns>
    internal List<LilyMusic> MusicXmlAttributesToLily(MusicXmlAttributes attrs)
    {
        List<LilyMusic> elts = new List<LilyMusic>();

        //We handle `<clef>' and `<staff-details>' together for technical reasons.
        if (attrs.GetNamedChildren("clef").Count > 0
            || attrs.GetNamedChildren("staff-details").Count > 0)
        {
            LilyClefChange ev = MusicXmlClefStaffDetailsToLily(attrs);
            if (ev != null)
            {
                elts.Add(ev);
            }
        }

        if (attrs.GetNamedChildren("time").Count > 0)
        {
            LilyTimeSignatureChange ev = MusicXmlTimeToLily(attrs);
            if (ev != null)
            {
                elts.Add(ev);
            }
        }

        if (attrs.GetNamedChildren("key").Count > 0)
        {
            LilyKeySignatureChange ev = MusicXmlKeyToLily(attrs);
            if (ev != null)
            {
                elts.Add(ev);
            }
        }

        if (attrs.GetNamedChildren("transpose").Count > 0)
        {
            LilyTransposition ev = MusicXmlTransposeToLily(attrs);
            if (ev != null)
            {
                elts.Add(ev);
            }
        }

        if (attrs.GetNamedChildren("measure-style").Count > 0)
        {
            List<LilyMusic> ev = MusicXmlMeasureStyleToLily(attrs);
            if (ev != null)
            {
                elts.AddRange(ev);
            }
        }

        return elts;
    }

    /// <summary>The plain text a display element carries.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The text, or empty when the element has no children.</returns>
    internal static string ExtractDisplayText(MusicXmlNode element)
    {
        List<MusicXmlNode> children = element.GetAllChildren();
        if (children.Count == 0)
        {
            return string.Empty;
        }

        List<string> text = new List<string>();
        foreach (MusicXmlNode child in children)
        {
            string name = child.GetName();
            if (name == "accidental-text")
            {
                //This is sufficient for a character count.
                text.Add("#");
            }
            else
            {
                string childText = child.GetText();
                if (!string.IsNullOrEmpty(childText))
                {
                    text.Add(childText);
                }
            }
        }

        return string.Join(" ", text);
    }

    /// <summary>The markup a display element carries.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The markup, or null when the element has no children.</returns>
    internal string ExtractDisplayMarkup(MusicXmlNode element)
    {
        List<MusicXmlNode> children = element.GetAllChildren();
        if (children.Count == 0)
        {
            return null;
        }

        List<LilyMarkupElement> elements = new List<LilyMarkupElement>();
        foreach (MusicXmlNode child in children)
        {
            elements.Add(
                new LilyMarkupElement(child, LilyMarkupElement.CopyAttributes(child)));
        }

        return LilyMarkup.TextToLy(State, elements);
    }

    /// <summary>Turns a print element into the output-side events it asks for.</summary>
    /// <param name="element">The print element.</param>
    /// <returns>The events.</returns>
    /// <remarks>
    /// TODO (upstream's): Implement other print attributes — <c>staff-spacing</c>,
    /// <c>blank-page</c>, <c>measure-layout</c>, <c>measure-numbering</c>.
    /// </remarks>
    internal List<LilyMusic> MusicXmlPrintToLily(MusicXmlMusicNode element)
    {
        List<LilyMusic> elts = new List<LilyMusic>();

        if (State.ConversionSettings.ConvertSystemBreaks)
        {
            if (element.Attribute("new-system") == "yes")
            {
                elts.Add(new LilyBreak(State, "break"));
            }
        }

        if (element.Attribute("new-page") == "yes")
        {
            //The solution to set arbitrary page numbers in LilyPond is non-trivial; see
            //
            //  https://lists.gnu.org/archive/html/lilypond-user/2025-08/msg00124.html
            //
            //for an example. For this reason, we only support setting the page number at
            //the very beginning of a piece.
            //
            //We use this as a fallback if the page number is not set in a `<credit>'
            //element.
            if (State.Paper.FirstPageNumber == 0)
            {
                string pageNumber = element.Attribute("page-number", "0");
                if (int.TryParse(
                        pageNumber, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int parsed))
                {
                    if (parsed > 1 && element.When.Value.IsZero)
                    {
                        State.Paper.FirstPageNumber = parsed;
                    }
                }
                else
                {
                    State.Warning("cannot use non-integer page number '" + pageNumber + "'");
                }
            }

            if (State.ConversionSettings.ConvertPageBreaks)
            {
                elts.Add(new LilyBreak(State, "pageBreak"));
            }
        }

        MusicXmlNode child = element.GetMaybeExistNamedChild("part-name-display");
        if (child != null)
        {
            string name = child.Attribute("print-object", "yes") == "yes"
                ? ExtractDisplayMarkup(child)
                : string.Empty;
            name = LilyMarkup.EscapeInstrumentString(name);
            elts.Add(new LilySetEvent(State, "Staff.instrumentName", name));
        }

        child = element.GetMaybeExistNamedChild("part-abbreviation-display");
        if (child != null)
        {
            string name = child.Attribute("print-object", "yes") == "yes"
                ? ExtractDisplayMarkup(child)
                : string.Empty;
            name = LilyMarkup.EscapeInstrumentString(name);
            elts.Add(new LilySetEvent(State, "Staff.shortInstrumentName", name));
        }

        return elts;
    }
}
