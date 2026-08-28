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
using System.Numerics;

namespace CodeBrix.LilyPort.Importers; //was previously: python/musicexp.py (Duration, Pitch and the pitch languages);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>A note value, as LilyPond writes it.</summary>
internal sealed class LilyDuration
{
    /// <summary>Builds a duration.</summary>
    /// <param name="state">The import this duration belongs to.</param>
    internal LilyDuration(MusicXmlImportState state) => State = state;

    /// <summary>The import this duration belongs to.</summary>
    internal MusicXmlImportState State { get; }

    /// <summary>The note value, as a logarithm of the reciprocal.</summary>
    internal int DurationLog { get; set; }

    /// <summary>How many augmentation dots.</summary>
    internal int Dots { get; set; }

    /// <summary>What the written value is multiplied by.</summary>
    internal PythonFraction Factor { get; set; } = PythonFraction.One;

    /// <summary>How many times it repeats, for multi-measure rests.</summary>
    internal int Repeat { get; set; } = 1;

    /// <summary>Builds a duration out of a length.</summary>
    /// <param name="state">The import this duration belongs to.</param>
    /// <param name="length">The length.</param>
    /// <returns>The duration.</returns>
    internal static LilyDuration FromFraction(MusicXmlImportState state, PythonFraction length)
    {
        LilyDuration duration = new LilyDuration(state);
        duration.SetFromFraction(length);
        return duration;
    }

    /// <summary>Sets this duration from a length.</summary>
    /// <param name="length">The length.</param>
    internal void SetFromFraction(PythonFraction length)
    {
        //⚠ Upstream's `except AttributeError' branch is UNREACHABLE for a number:
        //python's int carries `.denominator' too, answering 1, so an integer length
        //simply falls through the `d > 1' test to the same final assignment.
        BigInteger denominator = length.Denominator;
        if (denominator > BigInteger.One)
        {
            int dlog = (int)(denominator.GetBitLength() - 1);
            if (BigInteger.One << dlog == denominator)
            {
                //d is a power of 2.
                //TODO (upstream's): Handling n % 3 == 0 (with factor = n // 3) improved
                //code readability for a real-world sample score with a mix of 2/4, 3/4,
                //4/4, 6/4, 6/8, and 12/8 time signatures.
                BigInteger n = length.Numerator;
                if (n == 3)
                {
                    //e.g., s1. rather than s2*3
                    DurationLog = dlog - 1;
                    Dots = 1;
                    Factor = PythonFraction.One;
                    return;
                }

                //e.g., s8*5
                DurationLog = dlog;
                Dots = 0;
                Factor = new PythonFraction(n, 1);
                return;
            }
        }

        //e.g., s1*length
        DurationLog = 0;
        Dots = 0;
        Factor = length;
    }

    /// <summary>This duration as a Scheme expression.</summary>
    /// <returns>The expression.</returns>
    internal string LispExpression()
        => "(ly:make-duration "
           + DurationLog.ToString(CultureInfo.InvariantCulture) + " "
           + Dots.ToString(CultureInfo.InvariantCulture) + " "
           + Factor + ")";

    /// <summary>This duration as LilyPond input.</summary>
    /// <param name="factor">What to scale by, or null for this duration's own factor.</param>
    /// <param name="schemeMode">Whether the long note values are named rather than escaped.</param>
    /// <returns>The text.</returns>
    internal string LyExpression(PythonFraction? factor = null, bool schemeMode = false)
    {
        //python's `if not factor' is a TRUTH test, so a factor of ZERO also falls back
        //to this duration's own.
        PythonFraction effective = factor.HasValue && !factor.Value.IsZero
            ? factor.Value : Factor;

        //For communication with the tremolo event.
        State.LyDur = DurationLog;

        string durationText;
        if (DurationLog < 0)
        {
            switch (DurationLog)
            {
                case -1:
                    durationText = schemeMode ? "breve" : "\\breve";
                    break;
                case -2:
                    durationText = schemeMode ? "longa" : "\\longa";
                    break;
                case -3:
                    durationText = schemeMode ? "maxima" : "\\maxima";
                    break;
                default:
                    durationText = "1";
                    break;
            }
        }
        else
        {
            durationText = (1 << DurationLog).ToString(CultureInfo.InvariantCulture);
        }

        durationText += new string('.', Dots);

        if (effective != PythonFraction.One)
        {
            durationText += "*" + effective;
        }

        if (Repeat != 1)
        {
            durationText += "*" + Repeat.ToString(CultureInfo.InvariantCulture);
        }

        return durationText;
    }

    /// <summary>Writes this duration.</summary>
    /// <param name="printer">Where to write.</param>
    internal void PrintLy(LilyOutputPrinter printer)
        => printer.PrintDurationString(LyExpression(Factor / printer.DurationFactor()));

    /// <summary>Copies this duration.</summary>
    /// <returns>The copy.</returns>
    /// <remarks>⚠ Upstream's copy does NOT carry <c>repeat</c>; neither does this.</remarks>
    internal LilyDuration Copy()
        => new LilyDuration(State)
        {
            Dots = Dots,
            DurationLog = DurationLog,
            Factor = Factor,
        };

    /// <summary>How long this duration actually is.</summary>
    /// <param name="withFactor">Whether to include the scaling factor.</param>
    /// <returns>The length.</returns>
    internal PythonFraction GetLength(bool withFactor = true)
    {
        long num = (1L << (1 + Dots)) - 1;
        PythonFraction dotFactor = Dots != 0
            ? new PythonFraction(num, 1L << Dots)
            : PythonFraction.FromLong(num);

        PythonFraction baseValue = DurationLog <= 0
            ? PythonFraction.FromLong(1L << -DurationLog)
            : new PythonFraction(1, 1L << DurationLog);

        return withFactor
            ? baseValue * dotFactor * Factor
            : baseValue * dotFactor;
    }
}

/// <summary>A pitch, as LilyPond writes it.</summary>
internal sealed class LilyPitch
{
    /// <summary>Builds a pitch.</summary>
    /// <param name="state">The import this pitch belongs to.</param>
    internal LilyPitch(MusicXmlImportState state) => State = state;

    /// <summary>The import this pitch belongs to.</summary>
    internal MusicXmlImportState State { get; }

    /// <summary>How far the pitch is altered from its step.</summary>
    /// <remarks>
    /// A double rather than an integer, because a microtone alters by a half.
    /// </remarks>
    internal double Alteration { get; set; }

    /// <summary>Which step of the scale, counting C as zero.</summary>
    internal int Step { get; set; }

    /// <summary>Which octave, counting the one below middle C as zero.</summary>
    /// <remarks>
    /// A double rather than an integer, because the fret-diagram string tunings
    /// compute one with python's true division and get a fraction.
    /// </remarks>
    internal double Octave { get; set; }

    /// <summary>Whether this pitch is written absolutely even in relative mode.</summary>
    internal bool ForceAbsolutePitch { get; set; }

    /// <summary>This pitch, transposed by an interval.</summary>
    /// <param name="interval">The interval.</param>
    /// <returns>The transposed pitch.</returns>
    internal LilyPitch Transposed(LilyPitch interval)
    {
        LilyPitch copy = Copy();
        copy.Alteration += interval.Alteration;
        copy.Step += interval.Step;
        copy.Octave += interval.Octave;
        copy.Normalize();

        double targetSemitones = Semitones() + interval.Semitones();
        copy.Alteration += targetSemitones - copy.Semitones();
        return copy;
    }

    /// <summary>Brings the step back into one octave.</summary>
    internal void Normalize()
    {
        while (Step < 0)
        {
            Step += 7;
            Octave -= 1;
        }

        //Step is not negative here, so python's floor division and .NET's truncation
        //agree.
        Octave += Step / 7;
        Step = Step % 7;
    }

    /// <summary>This pitch as a Scheme expression.</summary>
    /// <returns>The expression.</returns>
    internal string LispExpression()
        => "(ly:make-pitch "
           + ((int)Octave).ToString(CultureInfo.InvariantCulture) + " "
           + Step.ToString(CultureInfo.InvariantCulture) + " "
           + ((int)Alteration).ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>Copies this pitch.</summary>
    /// <returns>The copy.</returns>
    internal LilyPitch Copy()
        => new LilyPitch(State)
        {
            Alteration = Alteration,
            Step = Step,
            Octave = Octave,
            ForceAbsolutePitch = ForceAbsolutePitch,
        };

    /// <summary>How many scale steps this pitch is above the reference.</summary>
    /// <returns>The count.</returns>
    internal double Steps() => Step + (Octave * 7);

    /// <summary>How many semitones this pitch is above the reference.</summary>
    /// <returns>The count.</returns>
    internal double Semitones()
        => (Octave * 12) + new[] { 0, 2, 4, 5, 7, 9, 11 }[Step] + Alteration;

    /// <summary>Moves an alteration onto the neighbouring step where one exists.</summary>
    internal void NormalizeAlteration()
    {
        if (Alteration < 0 && new[] { true, false, false, true, false, false, false }[Step])
        {
            Alteration += 1;
            Step -= 1;
        }
        else if (Alteration > 0
                 && new[] { false, false, true, false, false, false, true }[Step])
        {
            Alteration -= 1;
            Step += 1;
        }

        Normalize();
    }

    /// <summary>Raises this pitch by a number of semitones.</summary>
    /// <param name="number">The count.</param>
    internal void AddSemitones(double number)
    {
        double semi = number + Alteration;
        Alteration = 0;
        if (semi == 0)
        {
            return;
        }

        int sign = semi < 0 ? -1 : 1;
        double previous = Semitones();
        while (Math.Abs(previous + semi - Semitones()) > 1)
        {
            Step += sign;
            Normalize();
        }

        Alteration += previous + semi - Semitones();
        NormalizeAlteration();
    }

    /// <summary>This pitch's step, in the current note-name language.</summary>
    /// <returns>The name.</returns>
    internal string LyStepExpression() => State.PitchGeneratingFunction(this);

    /// <summary>The octave marks for this pitch, written absolutely.</summary>
    /// <returns>The marks.</returns>
    internal string AbsolutePitch()
    {
        if (Octave >= 0)
        {
            return new string('\'', (int)(Octave + 1));
        }

        if (Octave < -1)
        {
            return new string(',', (int)(-Octave - 1));
        }

        return string.Empty;
    }

    /// <summary>The octave marks for this pitch, written against the previous one.</summary>
    /// <returns>The marks.</returns>
    internal string RelativePitch()
    {
        if (State.PreviousPitch == null)
        {
            State.PreviousPitch = this;
            return AbsolutePitch();
        }

        double previousSteps = (State.PreviousPitch.Octave * 7) + State.PreviousPitch.Step;
        double theseSteps = (Octave * 7) + Step;
        double pitchDiff = theseSteps - previousSteps;
        State.PreviousPitch = this;
        if (pitchDiff > 3)
        {
            return new string('\'', (int)Math.Floor((pitchDiff + 3) / 7));
        }

        if (pitchDiff < -3)
        {
            return new string(',', (int)Math.Floor((-pitchDiff + 3) / 7));
        }

        return string.Empty;
    }

    /// <summary>This pitch as LilyPond input.</summary>
    /// <returns>The text.</returns>
    internal string LyExpression()
    {
        string text = LyStepExpression();
        text += State.RelativePitches && !ForceAbsolutePitch
            ? RelativePitch()
            : AbsolutePitch();
        return text;
    }

    /// <summary>Writes this pitch.</summary>
    /// <param name="printer">Where to write.</param>
    /// <param name="pitchMods">The accidental marks to write after it.</param>
    internal void PrintLy(LilyOutputPrinter printer, string pitchMods = "")
        => printer.Dump(LyExpression() + pitchMods);

    /// <inheritdoc/>
    public override string ToString() => LyExpression();
}

/// <summary>How each supported language spells a pitch.</summary>
internal static class LilyPitchLanguages
{
    /// <summary>Spells a pitch from a language's note names and accidental suffixes.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <param name="noteNames">The seven note names.</param>
    /// <param name="accidentals">Flat, half-flat, half-sharp and sharp; null where absent.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchGeneric(LilyPitch pitch, string[] noteNames, string[] accidentals)
    {
        string text = noteNames[pitch.Step];
        int halftones = (int)pitch.Alteration;
        if (halftones < 0)
        {
            text += Repeat(accidentals[0], -halftones);
        }
        else if (pitch.Alteration > 0)
        {
            text += Repeat(accidentals[3], halftones);
        }

        //Handle remaining fraction to the alteration (for microtones)
        if (halftones != pitch.Alteration)
        {
            if (accidentals[1] == null || accidentals[2] == null)
            {
                pitch.State.Warning(
                    "Language does not support microtones contained in the piece");
            }
            else
            {
                double remainder = pitch.Alteration - halftones;
                if (remainder == -0.5)
                {
                    text += accidentals[1];
                }
                else if (remainder == 0.5)
                {
                    text += accidentals[2];
                }
                else
                {
                    pitch.State.Warning(
                        "Language does not support microtones contained in the piece");
                }
            }
        }

        return text;
    }

    private static string Repeat(string text, int count)
    {
        if (text == null || count <= 0)
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
        {
            builder.Append(text);
        }

        return builder.ToString();
    }

    /// <summary>The default spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchGeneral(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "c", "d", "e", "f", "g", "a", "b" },
            new[] { "es", "eh", "ih", "is" });
        if (text.Contains("h"))
        {
            //no short forms for quarter tones
            return text;
        }

        return text.Replace("aes", "as").Replace("ees", "es");
    }

    /// <summary>The Dutch spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchNederlands(LilyPitch pitch) => PitchGeneral(pitch);

    /// <summary>The Catalan spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchCatalan(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
            new[] { "b", "qb", "qd", "d" });
        return text.Replace("bq", "tq").Replace("dq", "tq")
            .Replace("bt", "c").Replace("dt", "c");
    }

    /// <summary>The German spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchDeutsch(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "c", "d", "e", "f", "g", "a", "h" },
            new[] { "es", "eh", "ih", "is" });
        if (text == "hes")
        {
            return "b";
        }

        if (text.Length > 0 && text[0] == 'a')
        {
            return text.Replace("e", "a").Replace("aa", "a");
        }

        return text.Replace("ee", "e");
    }

    /// <summary>The English spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchEnglish(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "c", "d", "e", "f", "g", "a", "b" },
            new[] { "f", "qf", "qs", "s" });
        return text.Substring(0, 1)
               + text.Substring(1).Replace("fq", "tq").Replace("sq", "tq");
    }

    /// <summary>The Spanish spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchEspanol(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
            new[] { "b", "cb", "cs", "s" });
        return text.Replace("bc", "tc").Replace("sc", "tc");
    }

    /// <summary>The French spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchFrancais(LilyPitch pitch)
        => PitchGeneric(
            pitch,
            new[] { "do", "ré", "mi", "fa", "sol", "la", "si" },
            new[] { "b", "sb", "sd", "d" });

    /// <summary>The Italian spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchItaliano(LilyPitch pitch)
        => PitchGeneric(
            pitch,
            new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
            new[] { "b", "sb", "sd", "d" });

    /// <summary>The Norwegian spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchNorsk(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "c", "d", "e", "f", "g", "a", "h" },
            new[] { "ess", "eh", "ih", "iss" });
        return text.Replace("hess", "b");
    }

    /// <summary>The Portuguese spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchPortugues(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
            new[] { "b", "bqt", "sqt", "s" });
        return text.Replace("bbq", "btq").Replace("ssq", "stq");
    }

    /// <summary>The Finnish spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchSuomi(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "c", "d", "e", "f", "g", "a", "h" },
            new[] { "es", "eh", "ih", "is" });
        if (text == "hes")
        {
            return "b";
        }

        return text.Replace("aes", "as").Replace("ees", "es");
    }

    /// <summary>The Swedish spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchSvenska(LilyPitch pitch)
    {
        string text = PitchGeneric(
            pitch,
            new[] { "c", "d", "e", "f", "g", "a", "h" },
            new[] { "ess", "eh", "ih", "iss" });
        if (text == "hess")
        {
            return "b";
        }

        return text.Replace("aes", "as").Replace("ees", "es");
    }

    /// <summary>The Flemish spelling.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The spelling.</returns>
    internal static string PitchVlaams(LilyPitch pitch)
        => PitchGeneric(
            pitch,
            new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
            new[] { "b", "hb", "hk", "k" });

    private static readonly Dictionary<string, Func<LilyPitch, string>> Languages
        = new Dictionary<string, Func<LilyPitch, string>>(StringComparer.Ordinal)
        {
            { "nederlands", PitchNederlands },
            { "català", PitchCatalan },
            { "deutsch", PitchDeutsch },
            { "english", PitchEnglish },
            { "español", PitchEspanol },
            { "français", PitchFrancais },
            { "italiano", PitchItaliano },
            { "norsk", PitchNorsk },
            { "português", PitchPortugues },
            { "suomi", PitchSuomi },
            { "svenska", PitchSvenska },
            { "vlaams", PitchVlaams },
        };

    /// <summary>Chooses how pitches are spelled.</summary>
    /// <param name="state">The import to set the language for.</param>
    /// <param name="language">The language name.</param>
    internal static void SetPitchLanguage(MusicXmlImportState state, string language)
        => state.PitchGeneratingFunction
            = language != null && Languages.TryGetValue(language, out Func<LilyPitch, string> f)
                ? f
                : PitchGeneral;
}
