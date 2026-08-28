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

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (KeySignatureChange, TimeSignatureChange, Clef_StaffLinesEvent, TempoMark, the figured-bass classes and the small staff-level events beside them);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>One alteration of a non-traditional key signature, ready to write.</summary>
/// <remarks>
/// ⚠ Upstream carries these as python lists of two OR three members and asks
/// <c>len(a)</c> which reading it has; the third member is the octave. It also REWRITES
/// the first member in place, turning the document's step LETTER into LilyPond's step
/// NUMBER. The port converts into this type instead, so the step is a number by
/// construction and <see cref="HasOctave"/> is upstream's length test.
/// </remarks>
internal sealed class LilyKeyAlteration
{
    /// <summary>Builds an alteration without an octave.</summary>
    /// <param name="step">Which of the seven steps is altered.</param>
    /// <param name="alter">By how much.</param>
    internal LilyKeyAlteration(int step, double alter)
    {
        Step = step;
        Alter = alter;
    }

    /// <summary>Builds an alteration in one octave.</summary>
    /// <param name="step">Which of the seven steps is altered.</param>
    /// <param name="alter">By how much.</param>
    /// <param name="octave">Which octave, counted from the middle one.</param>
    internal LilyKeyAlteration(int step, double alter, int octave)
    {
        Step = step;
        Alter = alter;
        Octave = octave;
        HasOctave = true;
    }

    /// <summary>Which of the seven steps is altered.</summary>
    internal int Step { get; }

    /// <summary>By how much.</summary>
    internal double Alter { get; }

    /// <summary>Which octave, when the document names one.</summary>
    internal int Octave { get; }

    /// <summary>Whether the document named an octave.</summary>
    internal bool HasOctave { get; }
}

/// <summary>A key signature.</summary>
internal sealed class LilyKeySignatureChange : LilyMusic
{
    private static readonly Dictionary<double, string> AlterDict
        = new Dictionary<double, string>
        {
            { -2, ",DOUBLE-FLAT" },
            { -1.5, ",THREE-Q-FLAT" },
            { -1, ",FLAT" },
            { -0.5, ",SEMI-FLAT" },
            { 0, ",NATURAL" },
            { 0.5, ",SEMI-SHARP" },
            { 1, ",SHARP" },
            { 1.5, ",THREE-Q-SHARP" },
            { 2, ",DOUBLE-SHARP" },
        };

    /// <summary>Builds the signature.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyKeySignatureChange(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How many sharps or flats.</summary>
    internal int Fifths { get; set; }

    /// <summary>The key's tonic, when it is a traditional key.</summary>
    internal LilyPitch Tonic { get; set; }

    /// <summary>Which mode the key is in.</summary>
    internal string Mode { get; set; } = "major";

    /// <summary>The alterations, when the key is not a traditional one.</summary>
    internal List<LilyKeyAlteration> NonStandardAlterations { get; set; }

    /// <summary>How many accidentals the previous key is cancelled with.</summary>
    internal int? CancelFifths { get; set; }

    /// <summary>Where the cancellation is drawn.</summary>
    internal string CancelLocation { get; set; }

    /// <summary>Whether the signature is drawn at all.</summary>
    internal bool Visible { get; set; } = true;

    /// <summary>Writes one non-standard alteration as a Scheme pair.</summary>
    /// <param name="alteration">The alteration.</param>
    /// <returns>The text, or empty when the alteration cannot be written.</returns>
    internal string FormatNonStandardAlteration(LilyKeyAlteration alteration)
    {
        if (!AlterDict.TryGetValue(alteration.Alter, out string accidental))
        {
            State.Warning(
                "Unable to convert alteration "
                + LilyOutputPrinter.FormatDouble(alteration.Alter)
                + " to a lilypond expression");
            return string.Empty;
        }

        if (!alteration.HasOctave)
        {
            return "(" + alteration.Step.ToString(CultureInfo.InvariantCulture) + " . "
                   + accidental + ")";
        }

        return "((" + alteration.Octave.ToString(CultureInfo.InvariantCulture) + " . "
               + alteration.Step.ToString(CultureInfo.InvariantCulture) + ") . "
               + accidental + ")";
    }

    /// <summary>How far each of the seven steps is altered by this signature.</summary>
    /// <returns>The seven alterations, for the pitches C to B.</returns>
    internal double[] GetAlterations()
    {
        double[] alterations = new double[7];

        if (NonStandardAlterations != null && NonStandardAlterations.Count > 0)
        {
            //The alterations can name an octave or not, which is why upstream unpacks
            //with a starred name.
            foreach (LilyKeyAlteration alteration in NonStandardAlterations)
            {
                alterations[alteration.Step] = alteration.Alter;
            }
        }
        else
        {
            int count = Fifths;
            if (count > 0)
            {
                int pitch = -1;  //B
                while (count > 0)
                {
                    pitch = PythonModulo(pitch + 4, 7);  //a fifth up
                    alterations[pitch] += 1;
                    count -= 1;
                }
            }
            else if (count < 0)
            {
                int pitch = 3;  //F
                while (count < 0)
                {
                    pitch = PythonModulo(pitch - 4, 7);  //a fifth down
                    alterations[pitch] -= 1;
                    count += 1;
                }
            }
        }

        return alterations;
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

    /// <summary>This signature as the two halves upstream writes it in.</summary>
    /// <returns>What goes before the key, and the key itself.</returns>
    internal (string Left, string Right) KeyChangeToLy()
    {
        string color = LilyMarkup.ColorToLy(Color);
        string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);

        if (Tonic != null)
        {
            string cancellation = string.Empty;
            if (CancelFifths.HasValue && CancelFifths.Value != 0)
            {
                //We ignore the value of the cancellation.
                cancellation = CancelLocation switch
                {
                    "left" => "\\once \\set Staff.printKeyCancellation = ##t",
                    "right" => "\\cancelAfterKey",
                    "before-barline" => "\\cancelBeforeBarline",
                    _ => string.Empty,
                };
            }

            string colorTweak = color == null ? string.Empty : "\\tweak color " + color + " ";
            string fontSizeTweak = fontSize == null
                ? string.Empty
                : "\\tweak font-size " + fontSize + " ";

            return (cancellation,
                colorTweak + fontSizeTweak + "\\key " + Tonic.LyStepExpression()
                + " \\" + Mode);
        }

        if (NonStandardAlterations != null && NonStandardAlterations.Count > 0)
        {
            List<string> alterations = NonStandardAlterations
                .Select(FormatNonStandardAlteration)
                .ToList();

            string colorOverride = color == null
                ? string.Empty
                : " \\once \\override Staff.KeySignature.color = " + color;

            string fontSizeOverride = fontSize == null
                ? string.Empty
                : " \\once \\override Staff.KeySignature.font-size = " + fontSize;

            return ("\\set Staff.keyAlterations =",
                "#`(" + string.Join(" ", alterations) + ")" + colorOverride
                + fontSizeOverride);
        }

        return (string.Empty, string.Empty);
    }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        (string left, string right) = KeyChangeToLy();
        return left + " " + right;
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        (string left, string right) = KeyChangeToLy();
        printer.Dump(left);
        printer.Dump(right);
    }
}

/// <summary>A wrapper that shifts every duration inside it.</summary>
internal sealed class LilyShiftDurations : LilyMusicWrapper
{
    /// <summary>Builds the wrapper.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyShiftDurations(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        printer.Dump(
            " \\shiftDurations "
            + State.ShiftDurations.ToString(CultureInfo.InvariantCulture) + " 0");
        base.PrintLy(printer);
    }
}

/// <summary>A time signature.</summary>
internal sealed class LilyTimeSignatureChange : LilyMusic
{
    /// <summary>Builds the signature.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyTimeSignatureChange(MusicXmlImportState state)
        : base(state)
        => Fractions = new MusicXmlTimeSignature(new List<int> { 4, 4 });

    /// <summary>The signature itself.</summary>
    internal MusicXmlTimeSignature Fractions { get; set; }

    /// <summary>The signature alternated with, when the document names one.</summary>
    internal MusicXmlTimeSignature Alternate { get; set; }

    /// <summary>How the alternation is drawn.</summary>
    internal string AlternateStyle { get; set; }

    /// <summary>How the signature is drawn.</summary>
    internal string Style { get; set; }

    /// <summary>Whether the signature is drawn at all.</summary>
    internal bool Visible { get; set; } = true;

    /// <summary>Writes a signature as the Scheme structure LilyPond reads.</summary>
    /// <param name="fraction">The signature.</param>
    /// <returns>The text.</returns>
    internal string FormatFraction(MusicXmlTimeSignature fraction)
    {
        if (fraction.Kind == MusicXmlTimeSignature.SignatureKind.Compound)
        {
            //List of lists -> alternating meter
            List<string> parts = fraction.Parts
                .Select(f => FormatFraction(new MusicXmlTimeSignature(f)))
                .ToList();
            return "(" + string.Join(" ", parts) + ")";
        }

        //Just a list -> fraction
        List<int> beats = fraction.Beats;
        List<string> numerators = beats
            .Take(beats.Count - 1)
            .Select(i => i.ToString(CultureInfo.InvariantCulture))
            .ToList();
        string denominator = beats[beats.Count - 1].ToString(CultureInfo.InvariantCulture);
        if (numerators.Count >= 2)
        {
            //Subdivided (additive) numerator
            return "((" + string.Join(" ", numerators) + ") . " + denominator + ")";
        }

        //Single-term numerator
        return "(" + numerators[0] + " . " + denominator + ")";
    }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();

        //Print out the style if we have one, but the '() should only be forced for 2/2 or
        //4/4, since in all other cases we'll get numeric signatures anyway despite the
        //default 'C signature style!
        if (Visible)
        {
            if (!string.IsNullOrEmpty(Style))
            {
                bool isCommonSignature =
                    Fractions.Kind == MusicXmlTimeSignature.SignatureKind.Simple
                    && (SameBeats(Fractions.Beats, 2, 2)
                        || SameBeats(Fractions.Beats, 4, 4)
                        || SameBeats(Fractions.Beats, 4, 2));

                if (Style == "common")
                {
                    result.Add("\\defaultTimeSignature");
                }
                else if (Style != "'()")
                {
                    result.Add(
                        "\\once \\override Staff.TimeSignature.style = #" + Style);
                }
                else if (Style != "'()" || isCommonSignature)
                {
                    result.Add("\\numericTimeSignature");
                }
            }

            if (Fractions.Kind == MusicXmlTimeSignature.SignatureKind.SenzaMisuraCross)
            {
                result.Add("\\set Timing.timeSignature = ##f");
            }

            string color = LilyMarkup.ColorToLy(Color);
            if (color != null)
            {
                result.Add("\\once \\override Staff.TimeSignature.color = " + color);
            }

            string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
            if (fontSize != null)
            {
                result.Add("\\once \\override Staff.TimeSignature.font-size = " + fontSize);
            }
        }
        else
        {
            result.Add("\\once \\omit Staff.TimeSignature");
        }

        if (Alternate != null)
        {
            result.Add(
                "\\timeAlternate #'" + FormatFraction(Fractions)
                + " #'" + FormatFraction(Alternate)
                + " #'" + AlternateStyle);
        }
        else if (Fractions.Kind == MusicXmlTimeSignature.SignatureKind.Simple
                 && Fractions.Beats.Count == 2)
        {
            //Easy case: the signature is [n, d] => normal \time n/d call.
            result.Add(
                "\\time " + Fractions.Beats[0].ToString(CultureInfo.InvariantCulture)
                + "/" + Fractions.Beats[1].ToString(CultureInfo.InvariantCulture));
        }
        else if (SignatureLength(Fractions) > 1)
        {
            result.Add("\\time #'" + FormatFraction(Fractions));
        }

        return string.Join(" ", result);
    }

    /// <summary>Whether a signature's beats are exactly the two given.</summary>
    /// <param name="beats">The beats.</param>
    /// <param name="first">The first.</param>
    /// <param name="second">The second.</param>
    /// <returns>Whether they match.</returns>
    private static bool SameBeats(List<int> beats, int first, int second)
        => beats.Count == 2 && beats[0] == first && beats[1] == second;

    /// <summary>python's <c>len</c> of whatever the signature is held as.</summary>
    /// <param name="signature">The signature.</param>
    /// <returns>The length.</returns>
    private static int SignatureLength(MusicXmlTimeSignature signature)
        => signature.Kind switch
        {
            MusicXmlTimeSignature.SignatureKind.Compound => signature.Parts.Count,
            MusicXmlTimeSignature.SignatureKind.Simple => signature.Beats.Count,
            //⚠ The X-shaped senza-misura signature is the one-character STRING 'X'
            //upstream, so python's `len' answers one for it.
            MusicXmlTimeSignature.SignatureKind.SenzaMisuraCross => 1,
            //⚠ The empty senza-misura signature is the INTEGER zero upstream, which
            //python's `len' refuses; the caller filters it out before reaching here.
            _ => throw new InvalidOperationException(
                "an empty senza-misura signature has no length"),
        };
}

/// <summary>A clef, the staff lines it is drawn on, or both.</summary>
internal sealed class LilyClefChange : LilyMusic
{
    private static readonly string[] SupportedClefs
        = { "G", "F", "C", "percussion", "TAB" };

    private static readonly Dictionary<(string Sign, int Line), string> LilyClefDict
        = new Dictionary<(string, int), string>
        {
            { ("G", 2), "treble" },
            { ("G", 1), "french" },
            { ("C", 1), "soprano" },
            { ("C", 2), "mezzosoprano" },
            { ("C", 3), "alto" },
            { ("C", 4), "tenor" },
            { ("C", 5), "baritone" },
            { ("F", 3), "varbaritone" },
            { ("F", 4), "bass" },
            { ("F", 5), "subbass" },
            { ("percussion", 2), "percussion" },
            //Old MuseScore versions used `PERC' instead of `percussion'.
            { ("PERC", 2), "percussion" },
            { ("TAB", 5), "tab" },
        };

    private static readonly Dictionary<string, (string Glyph, int Position, int MiddleC)>
        ClefDict
            = new Dictionary<string, (string, int, int)>(StringComparer.Ordinal)
            {
                { "G", ("clefs.G", -2, -6) },
                { "C", ("clefs.C", 0, 0) },
                { "F", ("clefs.F", 2, 6) },
            };

    /// <summary>Builds the clef.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyClefChange(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>Which clef letter the document gave.</summary>
    internal string Type { get; set; }

    /// <summary>Which staff line the clef sits on.</summary>
    internal int? Position { get; set; }

    /// <summary>The pitch of the middle line, where the clef has one.</summary>
    internal LilyPitch Pitch { get; set; }

    /// <summary>How many octaves the clef transposes by.</summary>
    internal int? Octave { get; set; }

    /// <summary>How many staff lines there are.</summary>
    internal int? Lines { get; set; }

    /// <summary>Which staff lines are drawn, when the document says.</summary>
    internal Dictionary<int, string> LineDetails { get; set; }

    /// <summary>The colour the staff lines are drawn in.</summary>
    internal string LinesColor { get; set; }

    /// <summary>Whether the clef is drawn at all.</summary>
    internal bool Visible { get; set; } = true;

    /// <summary>The mark that raises or lowers the clef by octaves.</summary>
    /// <returns>The mark.</returns>
    internal string OctaveModifier()
        => Octave switch
        {
            1 => "^8",
            2 => "^15",
            -1 => "_8",
            -2 => "_15",
            _ => string.Empty,
        };

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string clef = null;
        int lines;
        string details = null;
        List<int> staffLines = null;

        if (Type != null)
        {
            if (!Position.HasValue || Position.Value == 0)
            {
                //Set default position.
                Position = Type switch
                {
                    "G" => 2,
                    "F" => 4,
                    "C" => 3,
                    "percussion" => 2,
                    "PERC" => 2,
                    _ => (int?)null,
                };
            }

            string clefName;
            if (SupportedClefs.Contains(Type))
            {
                clefName = Position.HasValue
                           && LilyClefDict.TryGetValue((Type, Position.Value), out string named)
                    ? named
                    : null;
                if (clefName == "tab")
                {
                    clefName = State.GetTabClef();
                }
                else if (string.IsNullOrEmpty(clefName))
                {
                    State.Warning(
                        "Non-standard clef positions are not supported yet, "
                        + "using 'treble' instead");
                    clefName = "treble";
                }
            }
            else if (Type == "none")
            {
                //Deprecated in MusicXML version 4.0.
                clefName = "treble";
            }
            else
            {
                State.Warning("Unsupported clef '" + Type + "', using 'treble' instead");
                clefName = "treble";
            }

            clef = clefName + OctaveModifier();
        }

        lines = Lines ?? 5;

        //TODO (upstream's): Also handle `line-type' and `color' attributes of the
        //`<line-detail>' element, which unfortunately needs a much more convoluted
        //solution because LilyPond lacks direct support (see LSR snippets #700 and #880).
        bool useLineDetails = false;
        if (LineDetails != null)
        {
            List<int> defaultLines = Enumerable.Range(1, lines).ToList();
            staffLines = defaultLines
                .Where(e => !LineDetails.ContainsKey(e) || LineDetails[e] != "no")
                .ToList();
            if (!staffLines.SequenceEqual(defaultLines))
            {
                useLineDetails = true;
            }
        }

        if (useLineDetails)
        {
            details = string.Join(
                " ", staffLines.Select(e => e.ToString(CultureInfo.InvariantCulture)));
        }

        string color = LilyMarkup.ColorToLy(Color);
        string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);

        //LilyPond handles a 'percussion' clef similar to an alto clef; we thus use
        //`\staffLines' for this clef to get the right vertical offset.
        if (clef != null && clef != "percussion" && !Lines.HasValue && details == null)
        {
            List<string> result = new List<string>();

            if (color != null)
            {
                //We can't use `\tweak' here.
                result.Add("\\once \\override Staff.Clef.color = " + color);
            }

            if (fontSize != null)
            {
                result.Add("\\once \\override Staff.Clef.font-size = " + fontSize);
            }

            result.Add("\\clef \"" + clef + "\"");
            return string.Join(" ", result);
        }

        if (clef == null)
        {
            clef = string.Empty;
        }

        string linesColor = LilyMarkup.ColorToLy(LinesColor);

        List<string> properties = new List<string>();
        if (details != null)
        {
            properties.Add("(details . (" + details + "))");
        }

        if (color != null)
        {
            properties.Add("(clef-color . " + color + ")");
        }

        if (fontSize != null)
        {
            //Skip the leading `#' character in the font size.
            properties.Add("(clef-font-size . " + fontSize.Substring(1) + ")");
        }

        if (linesColor != null)
        {
            properties.Add("(staff-color . " + linesColor + ")");
        }

        string props = string.Join(" ", properties);
        return props.Length > 0
            ? "\\staffLines #'(" + props + ") \"" + clef + "\" "
              + lines.ToString(CultureInfo.InvariantCulture)
            : "\\staffLines \"" + clef + "\" " + lines.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>This clef as a <c>make-music</c> expression.</summary>
    /// <returns>The expression, or empty when the clef has no glyph.</returns>
    internal string LispExpression()
    {
        if (Type == null || !ClefDict.TryGetValue(Type, out (string Glyph, int Position, int MiddleC) entry))
        {
            return string.Empty;
        }

        return "\n        (make-music 'SequentialMusic\n"
               + "        'elements (list\n"
               + "   (context-spec-music\n"
               + "   (make-property-set 'clefGlyph \"" + entry.Glyph + "\") 'Staff)\n"
               + "   (context-spec-music\n"
               + "   (make-property-set 'clefPosition "
               + entry.Position.ToString(CultureInfo.InvariantCulture) + ") 'Staff)\n"
               + "   (context-spec-music\n"
               + "   (make-property-set 'middleCPosition "
               + entry.MiddleC.ToString(CultureInfo.InvariantCulture) + ") 'Staff)))\n";
    }
}

/// <summary>A transposing instrument's written-to-sounding interval.</summary>
internal sealed class LilyTransposition : LilyMusic
{
    /// <summary>Builds the transposition.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyTransposition(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The interval.</summary>
    internal LilyPitch Pitch { get; set; }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        Pitch.ForceAbsolutePitch = true;
        return "\\transposition " + Pitch.LyExpression();
    }
}

/// <summary>A change to another staff of the same part.</summary>
internal sealed class LilyStaffChange : LilyMusic
{
    /// <summary>Builds the change.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    /// <param name="staff">Which staff to change to.</param>
    internal LilyStaffChange(MusicXmlImportState state, string staff)
        : base(state)
        => Staff = staff;

    /// <summary>Which staff to change to.</summary>
    internal string Staff { get; }

    /// <inheritdoc/>
    internal override string LyExpression()
        => !string.IsNullOrEmpty(Staff)
            ? "\\change Staff=\"" + Staff + "\""
            : string.Empty;
}

/// <summary>A context property set to a value.</summary>
internal sealed class LilySetEvent : LilyMusic
{
    /// <summary>Builds the setting.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    /// <param name="contextProp">Which property.</param>
    /// <param name="value">What to set it to.</param>
    /// <param name="once">Whether the setting lasts one moment only.</param>
    internal LilySetEvent(
        MusicXmlImportState state, string contextProp, string value, bool once = false)
        : base(state)
    {
        ContextProp = contextProp;
        Value = value;
        Once = once;
    }

    /// <summary>Which property is set.</summary>
    internal string ContextProp { get; }

    /// <summary>What it is set to.</summary>
    internal string Value { get; }

    /// <summary>Whether the setting lasts one moment only.</summary>
    internal bool Once { get; }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string onceStr = Once ? "\\once " : string.Empty;
        return !string.IsNullOrEmpty(Value)
            ? onceStr + "\\set " + ContextProp + " = " + Value
            : string.Empty;
    }
}

/// <summary>A grob left undrawn.</summary>
internal sealed class LilyOmitEvent : LilyMusic
{
    /// <summary>Builds the omission.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    /// <param name="grob">Which grob.</param>
    /// <param name="undo">Whether to undo an earlier omission.</param>
    /// <param name="once">Whether the omission lasts one moment only.</param>
    internal LilyOmitEvent(
        MusicXmlImportState state, string grob, bool undo = false, bool once = false)
        : base(state)
    {
        Grob = grob;
        Undo = undo;
        Once = once;
    }

    /// <summary>Which grob is omitted.</summary>
    internal string Grob { get; }

    /// <summary>Whether this undoes an earlier omission.</summary>
    internal bool Undo { get; }

    /// <summary>Whether the omission lasts one moment only.</summary>
    internal bool Once { get; }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string prefix = string.Empty;
        if (Once)
        {
            prefix += "\\once ";
        }

        if (Undo)
        {
            prefix += "\\undo ";
        }

        return prefix + "\\omit " + Grob;
    }
}

/// <summary>A multi-measure rest's own tweaks.</summary>
internal sealed class LilyMeasureStyleEvent : LilyMusic
{
    /// <summary>Builds the tweaks.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyMeasureStyleEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How many measures the rest covers.</summary>
    internal int MultipleRestLength { get; set; }

    /// <summary>Whether the rest is drawn with church-rest symbols.</summary>
    internal bool UseSymbols { get; set; }

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        List<string> result = new List<string>();

        if (UseSymbols)
        {
            result.Add("\\tweak expand-limit 10");
        }

        string color = LilyMarkup.ColorToLy(Color);
        if (color != null)
        {
            result.Add("\\tweak color " + color);
            result.Add("\\tweak MultiMeasureRestNumber.color " + color);
        }

        string fontSize = LilyMarkup.GetFontSize(State, FontSize, false);
        if (fontSize != null)
        {
            result.Add("\\tweak font-size " + fontSize);
            result.Add("\\tweak MultiMeasureRestNumber.font-size " + fontSize);
        }

        return string.Join(" ", result);
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        string text = LyExpression();
        if (text.Length > 0)
        {
            printer.Dump(text);
        }
    }
}

/// <summary>A tempo mark, with or without a metronome part.</summary>
internal sealed class LilyTempoMark : LilyMusic, ILilyWaitForNote, ILilyOffsetEvent
{
    /// <summary>Builds the mark.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyTempoMark(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The note value the metronome mark counts.</summary>
    internal LilyDuration BaseDuration { get; set; }

    /// <summary>The note value it equates to, for a note-to-note mark.</summary>
    internal LilyDuration NewDuration { get; set; }

    /// <summary>How many beats a minute, for a note-to-number mark.</summary>
    /// <remarks>
    /// ⚠ An integer rather than its text: upstream tests it for TRUTH, and a document
    /// whose <c>&lt;per-minute&gt;</c> cannot be read gives it zero, which must read as
    /// "no metronome number" rather than as the two characters '0'.
    /// </remarks>
    internal int? Bpm { get; set; }

    /// <summary>The text the mark draws.</summary>
    internal List<LilyMarkupElement> TextElements { get; set; }

    /// <summary>Which side of the staff the mark is drawn on.</summary>
    internal int? ForceDirection { get; set; }

    /// <summary>Whether the metronome part is parenthesised.</summary>
    internal bool Parentheses { get; set; }

    /// <summary>What encloses the metronome number.</summary>
    internal string Enclosure { get; set; }

    /// <summary>Whether the metronome part is drawn at all.</summary>
    internal bool Visible { get; set; } = true;

    /// <inheritdoc/>
    public PythonFraction Offset { get; set; } = PythonFraction.Zero;

    /// <inheritdoc/>
    public bool WaitForNote() => false;

    /// <summary>python's truth test for a beat count the document may have left out.</summary>
    /// <param name="count">The count.</param>
    /// <returns>Whether it is present and not zero.</returns>
    private static bool IsTruthy(int? count) => count.HasValue && count.Value != 0;

    /// <summary>This mark's markup.</summary>
    /// <returns>The markup, or empty when there is nothing to draw.</returns>
    /// <remarks>
    /// Scheme function <c>format-metronome-markup</c> always uses bold face. Since we
    /// don't want to redefine this function we explicitly use <c>\normal-text</c> to
    /// reset the font.
    /// </remarks>
    internal string MetronomeToLy()
    {
        if (!Visible
            || BaseDuration == null
            || (!IsTruthy(Bpm) && NewDuration == null))
        {
            return TextElements != null && TextElements.Count > 0
                ? LilyMarkup.TextToLy(State, TextElements, "\\normal-text")
                : string.Empty;
        }

        //All the following markup gets handled within a `\concat' block.
        List<string> markup = new List<string>();

        //Both Finale and MuseScore automatically insert some horizontal space between
        //the tempo and the metronome part, and we follow.
        if (TextElements != null && TextElements.Count > 0)
        {
            MusicXmlNode element = TextElements[TextElements.Count - 1].Element;
            string text = element.GetText();
            if (element.GetName() == "words"
                && text.Length > 0 && text[text.Length - 1] == ' ')
            {
                //Nothing to add.
            }
            else
            {
                markup.Add("\" \"");
            }
        }

        markup.Add("\\normal-text \\smaller {");

        if (Parentheses)
        {
            markup.Add("( \\char ##x200A");  //U+200A HAIR SPACE
        }

        markup.Add("\\fontsize #-2 \\rhythm { " + BaseDuration.LyExpression() + " }");

        markup.Add("\\char ##x2009 = \\char ##x2009");  //U+2009 THIN SPACE

        if (IsTruthy(Bpm))
        {
            markup.Add(Bpm.Value.ToString(CultureInfo.InvariantCulture));
            if (Parentheses)
            {
                markup.Add(")");
            }
        }
        else
        {
            markup.Add("\\fontsize #-2 \\rhythm { " + NewDuration.LyExpression() + " }");
            if (Parentheses)
            {
                markup.Add("\\char ##x200A )");
            }
        }

        markup.Add("}");

        MusicXmlLilyPondMarkup markupNode = new MusicXmlLilyPondMarkup { State = State };
        markupNode.Data = string.Join(" ", markup);
        Dictionary<string, object> markupAttributes = new Dictionary<string, object>();
        if (Enclosure != null)
        {
            markupAttributes["enclosure"] = Enclosure;
        }

        //We extend MusicXML by making the metronome number inherit the `enclosure'
        //attribute -- or rather, we simplify the code here by allowing this :-)
        List<LilyMarkupElement> textElements = new List<LilyMarkupElement>();
        if (TextElements != null && TextElements.Count > 0)
        {
            textElements.AddRange(TextElements);
        }

        textElements.Add(new LilyMarkupElement(markupNode, markupAttributes));

        return LilyMarkup.TextToLy(State, textElements, "\\normal-text");
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
            -1 => "#DOWN",
            1 => "#UP",
            _ => string.Empty,
        };
        if (direction.Length > 0)
        {
            printer.Dump("\\tweak direction " + direction);
        }

        string markup = MetronomeToLy();
        if (markup.Length > 0)
        {
            printer.Dump("\\tempo \\markup");
            printer.Dump(markup);
        }
    }
}

/// <summary>One figure of a figured bass.</summary>
internal sealed class LilyFiguredBassNote : LilyMusic
{
    /// <summary>Builds the figure.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyFiguredBassNote(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>The number itself.</summary>
    internal string Number { get; private set; } = string.Empty;

    /// <summary>What is drawn before the number.</summary>
    internal string Prefix { get; private set; } = string.Empty;

    /// <summary>What is drawn after the number.</summary>
    internal string Suffix { get; private set; } = string.Empty;

    /// <summary>Records what is drawn before the number.</summary>
    /// <param name="prefix">The prefix.</param>
    internal void SetPrefix(string prefix) => Prefix = prefix;

    /// <summary>Records what is drawn after the number.</summary>
    /// <param name="suffix">The suffix.</param>
    /// <remarks>
    /// ⚠ AN UPSTREAM DEFECT, REPRODUCED (D64(a): a defect is proved by MEASUREMENT
    /// against the oracle, never by reading the python). Upstream assigns the suffix to
    /// the PREFIX field, so a figured-bass suffix both overwrites the prefix and never
    /// reaches the output. Recorded as a candidate in
    /// tools/musicxml2lyprobe/DIVERGENCES.txt; the fix goes on top of a green parity
    /// baseline, not into the port that establishes it.
    /// </remarks>
    internal void SetSuffix(string suffix) => Prefix = suffix;

    /// <summary>Records the number.</summary>
    /// <param name="number">The number.</param>
    internal void SetNumber(string number) => Number = number;

    /// <inheritdoc/>
    internal override string LyExpression()
    {
        string result = string.Empty;
        result += !string.IsNullOrEmpty(Number) ? Number : "_";

        if (!string.IsNullOrEmpty(Prefix))
        {
            result += Prefix;
        }

        if (!string.IsNullOrEmpty(Suffix))
        {
            result += Suffix;
        }

        return result;
    }
}

/// <summary>A figured-bass entry.</summary>
internal sealed class LilyFiguredBassEvent : LilyNestedMusic
{
    /// <summary>Builds the entry.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyFiguredBassEvent(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <summary>How long the entry lasts.</summary>
    internal LilyDuration Duration { get; private set; }

    /// <summary>How long the entry actually sounds for.</summary>
    internal PythonFraction RealDuration { get; private set; } = PythonFraction.Zero;

    /// <summary>Whether the entry is parenthesised.</summary>
    internal bool Parentheses { get; private set; }

    /// <summary>Records how long the entry lasts.</summary>
    /// <param name="duration">The duration.</param>
    internal void SetDuration(LilyDuration duration) => Duration = duration;

    /// <summary>Records whether the entry is parenthesised.</summary>
    /// <param name="parentheses">Whether it is.</param>
    internal void SetParentheses(bool parentheses) => Parentheses = parentheses;

    /// <summary>Records how long the entry actually sounds for.</summary>
    /// <param name="duration">The length.</param>
    internal void SetRealDuration(PythonFraction duration) => RealDuration = duration;

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        List<LilyFiguredBassNote> figuredBassEvents
            = Elements.OfType<LilyFiguredBassNote>().ToList();
        if (figuredBassEvents.Count > 0)
        {
            List<string> notes = new List<string>();
            foreach (LilyFiguredBassNote note in figuredBassEvents)
            {
                notes.Add(note.LyExpression());
            }

            string contents = string.Join(" ", notes);
            if (Parentheses)
            {
                contents = "[" + contents + "]";
            }

            printer.Dump("<" + contents + ">");
            Duration.PrintLy(printer);
        }
    }
}

/// <summary>A line or page break.</summary>
internal sealed class LilyBreak : LilyMusic
{
    /// <summary>Builds the break.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    /// <param name="type">Which break this is.</param>
    internal LilyBreak(MusicXmlImportState state, string type = "break")
        : base(state)
        => Type = type;

    /// <summary>Which break this is.</summary>
    internal string Type { get; }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer)
    {
        if (!string.IsNullOrEmpty(Type))
        {
            printer.Dump("\\" + Type);
        }
    }
}

/// <summary>An empty chord, which carries an event without sounding.</summary>
internal sealed class LilyEmptyChord : LilyMusic
{
    /// <summary>Builds the chord.</summary>
    /// <param name="state">The import this expression belongs to.</param>
    internal LilyEmptyChord(MusicXmlImportState state)
        : base(state)
    {
    }

    /// <inheritdoc/>
    internal override void PrintLy(LilyOutputPrinter printer) => printer.Dump("<>");
}
