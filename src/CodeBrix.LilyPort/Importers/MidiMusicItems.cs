/*
   This file is part of LilyPond, the GNU music typesetter.

   Copyright (C) 1998--2026  Han-Wen Nienhuys <hanwen@xs4all.nl>
                             Jan Nieuwenhuizen <janneke@gnu.org>

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

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/midi2ly.py (Duration, Note, Time, Tempo, Clef, Key, Text, EndOfTrack);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Something that happens at a point in a MIDI track and writes itself as LilyPond.
/// </summary>
/// <remarks>
/// midi2ly has no such base class — it asks <c>e[1].__class__ == Note</c> and calls
/// <c>.dump()</c> on whatever it finds. The class TESTS are the mechanism (three of
/// them decide voice layout, clefs and contexts), so they are kept as type tests here
/// and this base exists only to give <c>clocks</c> and <c>dump</c> one home.
/// </remarks>
internal abstract class MidiMusicItem
{
    /// <summary>Gets or sets how long this lasts, in clocks.</summary>
    internal double Clocks { get; set; }

    /// <summary>Writes this item as LilyPond.</summary>
    /// <returns>The source.</returns>
    internal abstract string Dump();
}

/// <summary>midi2ly.py:99-149. A duration, as the numbers LilyPond spells it with.</summary>
internal sealed class MidiDuration
{
    internal static readonly double[] AllowedDurs
        = { 0.125, 0.25, 0.5, 1, 2, 4, 8, 16, 32, 64, 128 };

    private readonly MidiConverter _owner;

    internal MidiDuration(MidiConverter owner, double clocks)
    {
        _owner = owner;
        Clocks = clocks;
        (Dur, Num, Den) = DurNumDen(owner, clocks);
    }

    /// <summary>Gets the clocks this was built from.</summary>
    internal double Clocks { get; }

    /// <summary>Gets the base duration.</summary>
    internal double Dur { get; }

    /// <summary>Gets the multiplier's numerator.</summary>
    internal double Num { get; }

    /// <summary>Gets the multiplier's denominator.</summary>
    internal double Den { get; }

    /// <summary>midi2ly.py:104-119.</summary>
    /// <param name="owner">The run this belongs to.</param>
    /// <param name="clocks">The length in clocks.</param>
    /// <returns>The base duration and the multiplier.</returns>
    private static (double Dur, double Num, double Den) DurNumDen(
        MidiConverter owner, double clocks)
    {
        for (int i = 0; i < owner.AllowedTupletClocks.Count; i++)
        {
            if (clocks == owner.AllowedTupletClocks[i])
            {
                int[] allowed = owner.AllowedTuplets[i];
                return (allowed[0], allowed[1], allowed[2]);
            }
        }

        double dur = 0;
        double num = 1;
        double den = 1;
        long g = Gcd(
            Math.Abs((long)MidiConverter.PyInt(clocks)),
            Math.Abs((long)(8 * owner.ClocksPer1)));
        if (g != 0)
        {
            dur = owner.ClocksPer1 / (double)g;
            num = clocks / g;
        }

        if (Array.IndexOf(AllowedDurs, dur) < 0)
        {
            dur = 4;
            num = clocks;
            den = owner.ClocksPer4;
        }

        return (dur, num, den);
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    /// <summary>midi2ly.py:121-140. python's <c>__repr__</c>.</summary>
    /// <returns>The duration as LilyPond spells it.</returns>
    public override string ToString()
    {
        if (Den == 1)
        {
            if (Num == 1)
            {
                return DurStr(Dur);
            }

            if (Num == 3 && Dur != 0.125)
            {
                return DurStr(Dur / 2) + ".";
            }

            return DurStr(Dur) + "*"
                + MidiConverter.PyInt(Num).ToString(CultureInfo.InvariantCulture);
        }

        return DurStr(Dur) + "*"
            + MidiConverter.PyInt(Num).ToString(CultureInfo.InvariantCulture) + "/"
            + MidiConverter.PyInt(Den).ToString(CultureInfo.InvariantCulture);
    }

    private static string DurStr(double d)
    {
        if (d == 0.125)
        {
            return "\\maxima";
        }

        if (d == 0.25)
        {
            return "\\longa";
        }

        if (d == 0.5)
        {
            return "\\breve";
        }

        return MidiConverter.PyInt(d).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// midi2ly.py:142-145. ⚠ Writing a duration SETS the reference duration; that side
    /// effect is what makes the next note able to leave its own duration out.
    /// </summary>
    /// <returns>The duration as LilyPond spells it.</returns>
    internal string Dump()
    {
        _owner.ReferenceNote.Duration = this;
        return ToString();
    }

    /// <summary>midi2ly.py:147-148.</summary>
    /// <param name="other">The duration to compare with.</param>
    /// <returns>The difference in clocks — nonzero meaning "write it out".</returns>
    internal double Compare(MidiDuration other) => Clocks - other.Clocks;
}

/// <summary>midi2ly.py:155-286. One note.</summary>
internal sealed class MidiNote : MidiMusicItem
{
    private static readonly int[] Names = { 0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6 };
    private static readonly int[] Alterations = { 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 1, 0 };

    internal static readonly string[] AlterationNames = { "eses", "es", "", "is", "isis" };

    private readonly MidiConverter _owner;

    internal MidiNote(MidiConverter owner, double clocks, int pitch, int velocity)
    {
        _owner = owner;
        Pitch = pitch;
        Velocity = velocity;
        // hmm
        Clocks = clocks;
        Duration = new MidiDuration(owner, clocks);
        (Octave, NoteName, Alteration) = ComputeOctaveNameAlteration(owner, pitch);
    }

    /// <summary>Gets the MIDI pitch.</summary>
    internal int Pitch { get; }

    /// <summary>Gets the MIDI velocity.</summary>
    internal int Velocity { get; }

    /// <summary>Gets or sets how long the note lasts.</summary>
    internal MidiDuration Duration { get; set; }

    /// <summary>Gets the octave.</summary>
    internal int Octave { get; }

    /// <summary>Gets the note name, 0 for c.</summary>
    internal int NoteName { get; }

    /// <summary>Gets the alteration, -2 through 2.</summary>
    internal int Alteration { get; }

    /// <summary>midi2ly.py:166-247. Spells the pitch as a name the key wants.</summary>
    /// <param name="owner">The run this belongs to.</param>
    /// <param name="pitch">The MIDI pitch.</param>
    /// <returns>The octave, the note name and the alteration.</returns>
    private static (int Octave, int NoteName, int Alteration)
        ComputeOctaveNameAlteration(MidiConverter owner, int pitch)
    {
        // major scale: do-do
        // minor scale: la-la  (= + 5)
        int n = Names[MidiConverter.PyMod(pitch, 12)];
        int a = Alterations[MidiConverter.PyMod(pitch, 12)];

        MidiKey key = owner.Key ?? new MidiKey(owner, 0, 0, 0);

        if (a != 0 && key.Flats != 0)
        {
            a = -Alterations[MidiConverter.PyMod(pitch, 12)];
            n = MidiConverter.PyMod(n - a, 7);
        }

        //  By tradition, all scales now consist of a sequence of 7 notes each with a
        //  distinct name, from amongst a b c d e f g. But, minor scales have a wide
        //  second interval at the top - the 'leading note' is sharped.
        //
        //  John Sankey <bf250@freenet.carleton.ca>
        //  Let's also do a-minor: a b c d e f gis a  --jcn
        int o = MidiConverter.FloorDiv(pitch, 12) - 4;

        if (key.Minor != 0)
        {
            // as -> gis
            if (key.Sharps == 0 && key.Flats == 0 && n == 5 && a == -1)
            {
                n = 4;
                a = 1;
            }
            else if (key.Flats == 1 && n == 1 && a == -1)  // des -> cis
            {
                n = 0;
                a = 1;
            }
            else if (key.Flats == 2 && n == 4 && a == -1)  // ges -> fis
            {
                n = 3;
                a = 1;
            }
            else if (key.Sharps == 5 && n == 4 && a == 0)  // g -> fisis
            {
                n = 3;
                a = 2;
            }
            else if (key.Sharps == 6 && n == 1 && a == 0)  // d -> cisis
            {
                n = 0;
                a = 2;
            }
            else if (key.Sharps == 7 && n == 5 && a == 0)  // a -> gisis
            {
                n = 4;
                a = 2;
            }
        }

        // b -> ces
        if (key.Flats >= 6 && n == 6 && a == 0)
        {
            n = 0;
            a = -1;
            o = o + 1;
        }

        // e -> fes
        if (key.Flats >= 7 && n == 2 && a == 0)
        {
            n = 3;
            a = -1;
        }

        // f -> eis
        if (key.Sharps >= 3 && n == 3 && a == 0)
        {
            n = 2;
            a = 1;
        }

        // c -> bis
        if (key.Sharps >= 4 && n == 0 && a == 0)
        {
            n = 6;
            a = 1;
            o = o - 1;
        }

        return (o, n, a);
    }

    /// <inheritdoc/>
    internal override string Dump() => Dump(true);

    /// <summary>midi2ly.py:253-286.</summary>
    /// <param name="dumpDur">Whether the duration may be written.</param>
    /// <returns>The note as LilyPond spells it.</returns>
    internal string Dump(bool dumpDur)
    {
        string s = ((char)(MidiConverter.PyMod(NoteName + 2, 7) + 'a')).ToString();
        s += AlterationNames[Alteration + 2];

        int commas;
        if (_owner.Options.AbsolutePitches)
        {
            commas = Octave;
        }
        else
        {
            int delta = Pitch - _owner.ReferenceNote.Pitch;
            commas = MidiConverter.Sign(delta) * MidiConverter.FloorDiv(Math.Abs(delta), 12);
            if (MidiConverter.PyMod(
                    (MidiConverter.Sign(delta) * (NoteName - _owner.ReferenceNote.NoteName)) + 7,
                    7) >= 4
                || (NoteName == _owner.ReferenceNote.NoteName
                    && Math.Abs(delta) > 4 && Math.Abs(delta) < 12))
            {
                commas += MidiConverter.Sign(delta);
            }
        }

        if (commas > 0)
        {
            s += new string('\'', commas);
        }
        else if (commas < 0)
        {
            s += new string(',', -commas);
        }

        if (dumpDur
            && (Duration.Compare(_owner.ReferenceNote.Duration) != 0
                || _owner.Options.ExplicitDurations))
        {
            s += Duration.Dump();
        }

        // Chords need to handle their reference duration themselves
        _owner.ReferenceNote = this;

        // TODO: move space
        return s + " ";
    }
}

/// <summary>midi2ly.py:289-325. A time signature.</summary>
internal sealed class MidiTime : MidiMusicItem
{
    private readonly MidiConverter _owner;

    internal MidiTime(MidiConverter owner, int num, int den, int metronomeClocks)
    {
        _owner = owner;
        Clocks = 0;
        Num = num;
        Den = den;
        MetronomeClocks = metronomeClocks;
    }

    /// <summary>Gets the numerator.</summary>
    internal int Num { get; }

    /// <summary>Gets the denominator.</summary>
    internal int Den { get; }

    /// <summary>Gets how many clocks the metronome ticks in.</summary>
    internal int MetronomeClocks { get; }

    /// <summary>midi2ly.py:296-297.</summary>
    /// <returns>How many clocks one bar lasts.</returns>
    internal double BarClocks() => _owner.ClocksPer1 * Num / (double)Den;

    /// <summary>midi2ly.py:299-302.</summary>
    /// <returns>How many beats there are in a measure.</returns>
    internal MidiFraction BeatsPerMeasure()
    {
        MidiFraction measureLen = new MidiFraction(Num, Den);
        MidiFraction beatLen = new MidiFraction(MetronomeClocks, 96);
        return measureLen / beatLen;
    }

    /// <inheritdoc/>
    internal override string Dump()
    {
        _owner.Time = this;
        string beatStructure = string.Empty;
        MidiFraction actualBeats = BeatsPerMeasure();
        if (actualBeats.IsWhole && actualBeats < MidiFraction.FromLong(Num))
        {
            // The metronome suggests a uniformly grouped beat structure.
            MidiFraction actualGroupSize = MidiFraction.FromLong(Num) / actualBeats;

            // LilyPond defaults to groups of 3 when the time signature numerator is >=
            // 6 and is a multiple of 3, so we probably don't need to emit the beat
            // structure option for it to do the right thing in those cases.
            int defaultGroupSize = 1;
            if (Num > 3 && MidiConverter.PyMod(Num, 3) == 0)
            {
                defaultGroupSize = 3;
            }

            if (actualGroupSize != MidiFraction.FromLong(defaultGroupSize))
            {
                // We only need to list the number once because LilyPond repeats
                // beatStructure to the end of the measure.
                beatStructure = "#'(" + actualGroupSize + ") ";
            }
        }

        // Note: The beat structure option does not override default beam exceptions, so
        // for example, in 4/4 LilyPond will still beam eighths in half-measure groups
        // even if this says otherwise.
        return "\n  \\time " + beatStructure
            + Num.ToString(CultureInfo.InvariantCulture) + "/"
            + Den.ToString(CultureInfo.InvariantCulture) + "\n  ";
    }
}

/// <summary>midi2ly.py:328-346. A tempo.</summary>
internal sealed class MidiTempo : MidiMusicItem
{
    internal MidiTempo(MidiFraction secondsPerWhole)
    {
        Clocks = 0;
        WholesPerMinute = MidiFraction.FromLong(60) / secondsPerWhole;
    }

    /// <summary>Gets how many whole notes pass in a minute.</summary>
    internal MidiFraction WholesPerMinute { get; }

    /// <inheritdoc/>
    internal override string Dump()
    {
        MidiFraction qpm = 4 * WholesPerMinute;
        string comment = string.Empty;
        string value = qpm.ToString();
        if (!qpm.IsWhole)
        {
            // Express as a rational with a decimal representation in a comment.
            comment = MidiFraction.FormatG(qpm.ToDouble());
            if (!qpm.EqualsDecimalText(comment))  // Is it approximate?
            {
                comment = "≈" + comment;
            }

            comment = " % " + comment;
            value = "#" + qpm;
        }

        return "\n  " + "\\tempo 4 = " + value + comment + "\n  ";
    }
}

/// <summary>midi2ly.py:349-358. A clef.</summary>
internal sealed class MidiClef
{
    private static readonly string[] Clefs
        = { "\"bass_8\"", "bass", "violin", "\"violin^8\"" };

    internal MidiClef(int cleftype) => Type = cleftype;

    /// <summary>Gets which clef.</summary>
    internal int Type { get; }

    /// <summary>midi2ly.py:357-358.</summary>
    /// <returns>The clef as LilyPond spells it.</returns>
    internal string Dump() => "\n  \\clef " + Clefs[Type] + "\n  ";
}

/// <summary>midi2ly.py:361-426. A key signature.</summary>
internal sealed class MidiKey : MidiMusicItem
{
    private readonly MidiConverter _owner;

    internal MidiKey(MidiConverter owner, int sharps, int flats, int minor)
    {
        _owner = owner;
        Clocks = 0;
        Flats = flats;
        Sharps = sharps;
        Minor = minor;
    }

    /// <summary>Gets how many flats.</summary>
    internal int Flats { get; }

    /// <summary>Gets how many sharps.</summary>
    internal int Sharps { get; }

    /// <summary>Gets whether the key is minor.</summary>
    internal int Minor { get; }

    /// <inheritdoc/>
    internal override string Dump()
    {
        _owner.Key = this;

        string s = string.Empty;
        if (Sharps != 0 && Flats != 0)
        {
            // pass
        }
        else
        {
            int k = Flats != 0
                ? MidiConverter.PyMod(
                    "cfbeadg"[MidiConverter.PyMod(Flats, 7)] - 'a' - 2 - (2 * Minor) + 7, 7)
                : MidiConverter.PyMod(
                    "cgdaebf"[MidiConverter.PyMod(Sharps, 7)] - 'a' - 2 - (2 * Minor) + 7, 7);

            //⚠ Upstream's two arms of this if/else are IDENTICAL; ported as written.
            string name = ((char)(MidiConverter.PyMod(k + 2, 7) + 'a')).ToString();

            // fis cis gis dis ais eis bis
            int[] sharps = { 2, 4, 6, 1, 3, 5, 7 };
            // bes es as des ges ces fes
            int[] flats = { 6, 4, 2, 7, 5, 3, 1 };
            int a = 0;
            if (Flats != 0)
            {
                if (flats[k] <= Flats)
                {
                    a = -1;
                }
            }
            else
            {
                if (sharps[k] <= Sharps)
                {
                    a = 1;
                }
            }

            if (a != 0)
            {
                name += MidiNote.AlterationNames[a + 2];
            }

            s = "\\key " + name;
            s += Minor != 0 ? " \\minor" : " \\major";
        }

        return "\n\n  " + s + "\n  ";
    }
}

/// <summary>midi2ly.py:429-476. A text or lyric event.</summary>
internal sealed class MidiText : MidiMusicItem
{
    private static readonly string[] TextTypes =
    {
        "SEQUENCE_NUMBER",
        "TEXT_EVENT",
        "COPYRIGHT_NOTICE",
        "SEQUENCE_TRACK_NAME",
        "INSTRUMENT_NAME",
        "LYRIC",
        "MARKER",
        "CUE_POINT",
        "PROGRAM_NAME",
        "DEVICE_NAME",
    };

    private readonly MidiConverter _owner;

    internal MidiText(MidiConverter owner, int texttype, string text)
    {
        _owner = owner;
        Clocks = 0;
        Type = texttype;
        Text = TextOnly(text);
    }

    /// <summary>Gets which kind of text event this is.</summary>
    internal int Type { get; }

    /// <summary>Gets the text, with everything unprintable replaced.</summary>
    internal string Text { get; }

    /// <summary>Gets or sets the channel this text was found on.</summary>
    internal MidiConverter.MidiChannel Track { get; set; }

    /// <summary>midi2ly.py:442-446.</summary>
    /// <param name="text">The raw text.</param>
    /// <returns>The text, printable.</returns>
    private static string TextOnly(string text)
    {
        char[] result = new char[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            result[i] = (c >= ' ' && c <= '~') || c == '\n' || c == '\r' ? c : '~';
        }

        return new string(result);
    }

    /// <inheritdoc/>
    internal override string Dump()
    {
        // urg, we should be sure that we're in a lyrics staff
        string s = string.Empty;
        if (Type == MidiFileReader.Lyric)
        {
            s = "\"" + Text + "\"";
            MidiDuration d = new MidiDuration(_owner, Clocks);
            if (_owner.Options.ExplicitDurations
                || d.Compare(_owner.ReferenceNote.Duration) != 0)
            {
                s += new MidiDuration(_owner, Clocks).Dump();
            }

            s += " ";
        }
        else if (Text.Trim().Length != 0
            && Type == MidiFileReader.SequenceTrackName
            && Text != "control track"
            && !Track.LyricsP)
        {
            string text = Text.Replace("(MIDI)", string.Empty).Trim();
            if (text.Length != 0)
            {
                s = "\n  \\set Staff.instrumentName = \"" + text + "\"\n  ";
            }
        }
        else if (Text.Trim().Length != 0)
        {
            s = "\n  % [" + TextTypes[Type] + "] " + Text + "\n  ";
        }

        return s;
    }
}

/// <summary>midi2ly.py:479-487.</summary>
internal sealed class MidiEndOfTrack : MidiMusicItem
{
    internal MidiEndOfTrack() => Clocks = 0;

    /// <inheritdoc/>
    internal override string Dump() => string.Empty;
}

/// <summary>One music entry: when, and what.</summary>
internal sealed class MidiMusicEvent
{
    internal MidiMusicEvent(double time, MidiMusicItem item)
    {
        Time = time;
        Item = item;
    }

    /// <summary>Gets when.</summary>
    internal double Time { get; }

    /// <summary>Gets what.</summary>
    internal MidiMusicItem Item { get; }
}
