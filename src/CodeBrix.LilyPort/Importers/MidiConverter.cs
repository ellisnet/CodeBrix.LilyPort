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
using System.IO;
using System.Linq;
using System.Text;

namespace CodeBrix.LilyPort.Importers; //was previously: scripts/midi2ly.py;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// One run of <c>midi2ly</c>: reads a Standard MIDI File and writes LilyPond.
/// </summary>
/// <remarks>
/// ⚠ EVERY FIELD HERE IS ONE OF UPSTREAM'S MODULE GLOBALS, for the same reason
/// <see cref="AbcConverter"/>'s are: midi2ly keeps its world at module scope, and a run
/// of the script is a run of the process. The music item classes each hold a reference
/// back here because they read those globals — <c>clocks_per_1</c>, the key, and above
/// all <c>reference_note</c>, which every note both reads and writes.
/// </remarks>
internal sealed class MidiConverter
{
    //midi2ly.py:47.
    private const int LineBell = 60;

    //midi2ly.py:1231. The tag names the program, and the program is this one.
    private const string ProgramName = "midi2ly";

    /// <summary>
    /// midi2ly.py:1235-1237. The release whose syntax this converter's OUTPUT was last
    /// verified against — upstream's own frozen number, on the same footing as
    /// abc2ly's.
    /// </summary>
    /// <remarks>
    /// ⚠ NOT THE PORTED RELEASE; rule 16 does not govern it. See
    /// <c>AbcConverter.LastVerifiedOutputVersion</c> and D63.
    /// </remarks>
    private const string LastVerifiedOutputVersion = "2.14.0";

    private readonly ImportDiagnostics _stderr;

    internal MidiConverter(MidiImportOptions options, ImportDiagnostics diagnostics)
    {
        Options = options;
        _stderr = diagnostics;
    }

    /// <summary>Gets what was asked of this run.</summary>
    internal MidiImportOptions Options { get; }

    //midi2ly.py:50-62, the module globals.
    internal int ClocksPer1 { get; private set; } = 1536;

    internal double ClocksPer4 { get; private set; }

    internal MidiTime Time { get; set; }

    internal MidiNote ReferenceNote { get; set; }

    internal double StartQuantClocks { get; private set; }

    internal double DurationQuantClocks { get; private set; }

    internal List<double> AllowedTupletClocks { get; private set; } = new List<double>();

    internal List<int[]> AllowedTuplets { get; private set; } = new List<int[]>();

    internal double BarMax { get; private set; }

    /// <summary>Gets or sets the key in force — <c>global_options.key</c>.</summary>
    /// <remarks>
    /// ⚠ MUTATED WHILE PARSING. Upstream's comment says it plainly: "ugh, must set key
    /// while parsing because Note init uses key". The option is where it starts, not
    /// where it stays.
    /// </remarks>
    internal MidiKey Key { get; set; }

    /// <summary>midi2ly.py:152-153.</summary>
    /// <param name="x">The number.</param>
    /// <returns>1 for zero and above, -1 below.</returns>
    internal static int Sign(int x) => x >= 0 ? 1 : -1;

    /// <summary>python's <c>int()</c> over a number: truncation towards zero.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The truncated value.</returns>
    internal static long PyInt(double value) => (long)Math.Truncate(value);

    /// <summary>python's <c>//</c> over two whole numbers.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient, rounded towards negative infinity.</returns>
    internal static int FloorDiv(int a, int b)
    {
        int q = a / b;
        return (a % b != 0) && ((a < 0) != (b < 0)) ? q - 1 : q;
    }

    /// <summary>python's <c>%</c>, whose result takes the divisor's sign.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The remainder.</returns>
    internal static int PyMod(int a, int b)
    {
        int r = a % b;
        return r != 0 && ((r < 0) != (b < 0)) ? r + b : r;
    }

    /// <summary>midi2ly.py:1245-1276, the option post-processing <c>do_options</c> does.</summary>
    private void PrepareOptions()
    {
        if (!string.IsNullOrEmpty(Options.Key))
        {
            string[] parts = (Options.Key + ":0").Split(':');
            if (parts.Length < 2
                || !int.TryParse(parts[0], NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int alterations)
                || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int minor))
            {
                throw new ImportAbortedException(
                    "invalid literal for int() with base 10: '" + Options.Key + "'");
            }

            int sharps = 0;
            int flats = 0;
            if (alterations >= 0)
            {
                sharps = alterations;
            }
            else
            {
                flats = -alterations;
            }

            Key = new MidiKey(this, sharps, flats, minor);
        }

        if (Options.Preview)
        {
            BarMax = 4;
        }

        AllowedTuplets = new List<int[]>();
        foreach (string a in Options.AllowTuplet)
        {
            string[] parts = a.Replace("/", "*").Split('*');
            int[] numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out numbers[i]))
                {
                    throw new ImportAbortedException(
                        "invalid literal for int() with base 10: '" + parts[i] + "'");
                }
            }

            if (numbers.Length < 3)
            {
                throw new ImportAbortedException(
                    "not enough values to unpack (expected 3, got "
                    + numbers.Length.ToString(CultureInfo.InvariantCulture) + ")");
            }

            AllowedTuplets.Add(numbers);
        }
    }

    /// <summary>midi2ly.py:485-487.</summary>
    /// <param name="channel">Which channel this is.</param>
    /// <param name="music">The music found on it.</param>
    /// <returns>The threads it splits into.</returns>
    private static List<List<MidiMusicEvent>> GetVoice(
        int? channel, List<MidiMusicEvent> music)
        => UnthreadNotes(music);

    /// <summary>midi2ly.py:490-620. One MIDI channel's worth of events.</summary>
    internal class MidiChannel
    {
        protected readonly MidiConverter Owner;

        internal MidiChannel(MidiConverter owner, int? number)
        {
            Owner = owner;
            Number = number;
        }

        /// <summary>Gets which channel this is.</summary>
        internal int? Number { get; }

        /// <summary>Gets the raw events.</summary>
        internal List<MidiTrackEvent> Events { get; } = new List<MidiTrackEvent>();

        /// <summary>Gets or sets the parsed music.</summary>
        internal List<MidiMusicEvent> Music { get; set; }

        /// <summary>Gets or sets the track name found in a meta event.</summary>
        internal string Name { get; set; }

        /// <summary>
        /// Gets or sets whether a lyric was seen. python grows this attribute on the
        /// object when the first lyric arrives; here it simply starts false.
        /// </summary>
        internal bool LyricsP { get; set; }

        /// <summary>Adds one event.</summary>
        /// <param name="e">The event.</param>
        internal virtual void Add(MidiTrackEvent e) => Events.Add(e);

        /// <summary>midi2ly.py:497-500.</summary>
        /// <returns>The threads this channel splits into.</returns>
        internal List<List<MidiMusicEvent>> GetVoice()
        {
            Music ??= Parse();
            return MidiConverter.GetVoice(Number, Music);
        }

        /// <summary>midi2ly.py:502-620.</summary>
        /// <returns>The music, in time order.</returns>
        internal List<MidiMusicEvent> Parse()
        {
            Dictionary<int, (double Time, int Velocity)> pitches
                = new Dictionary<int, (double, int)>();
            List<MidiMusicEvent> notes = new List<MidiMusicEvent>();
            List<MidiMusicEvent> music = new List<MidiMusicEvent>();
            MidiText lastLyric = null;
            double lastTime = 0;
            double? endOfTrackTime = null;

            foreach (MidiTrackEvent e in Events)
            {
                double t = e.Time;
                MidiMessage message = e.Message;

                if (Owner.StartQuantClocks != 0)
                {
                    t = Owner.QuantiseClocks(t, Owner.StartQuantClocks);
                }

                if (message.Status == MidiFileReader.NoteOff
                    || (message.Status == MidiFileReader.NoteOn && message.Arg2 == 0))
                {
                    Owner.EndNote(pitches, notes, t, message.Arg1);
                }
                else if (message.Status == MidiFileReader.NoteOn)
                {
                    if (!pitches.ContainsKey(message.Arg1))
                    {
                        pitches[message.Arg1] = (t, message.Arg2);
                    }
                }
                else if (message.Status >= MidiFileReader.AllSoundOff
                    && message.Status <= MidiFileReader.PolyModeOn)
                {
                    // all include ALL_NOTES_OFF
                    Owner.EndEveryNote(pitches, notes, t);
                }
                else if (message.Status == MidiFileReader.MetaEvent)
                {
                    if (message.Arg1 == MidiFileReader.EndOfTrack)
                    {
                        Owner.EndEveryNote(pitches, notes, t);
                        endOfTrackTime = t;
                        break;
                    }

                    if (message.Arg1 == MidiFileReader.SetTempo)
                    {
                        int[] u = Bytes(message.Data, 3);
                        long usPer4 = u[2] + (256 * (u[1] + (256 * (long)u[0])));
                        MidiFraction secondsPer1 = new MidiFraction(usPer4 * 4, 1000000);
                        music.Add(new MidiMusicEvent(t, new MidiTempo(secondsPer1)));
                    }
                    else if (message.Arg1 == MidiFileReader.TimeSignature)
                    {
                        int[] parts = Bytes(message.Data, 4);
                        int den = (int)Math.Pow(2, parts[1]);
                        music.Add(new MidiMusicEvent(
                            t, new MidiTime(Owner, parts[0], den, parts[2])));
                    }
                    else if (message.Arg1 == MidiFileReader.KeySignature)
                    {
                        int[] parts = Bytes(message.Data, 2);
                        int alterations = parts[0];
                        int minor = parts[1];
                        int sharps = 0;
                        int flats = 0;
                        if (alterations < 127)
                        {
                            sharps = alterations;
                        }
                        else
                        {
                            flats = 256 - alterations;
                        }

                        MidiKey k = new MidiKey(Owner, sharps, flats, minor);
                        if (t == 0 && Owner.Key != null)
                        {
                            // At t == 0, a set --key overrides us
                            k = Owner.Key;
                        }

                        music.Add(new MidiMusicEvent(t, k));

                        // ugh, must set key while parsing because Note init uses key
                        Owner.Key = k;
                    }
                    else if (message.Arg1 == MidiFileReader.Lyric
                        || (Owner.Options.TextLyrics
                            && message.Arg1 == MidiFileReader.TextEvent))
                    {
                        LyricsP = true;
                        if (lastLyric != null)
                        {
                            lastLyric.Clocks = t - lastTime;
                            music.Add(new MidiMusicEvent(lastTime, lastLyric));
                        }

                        lastTime = t;
                        lastLyric = new MidiText(
                            Owner, MidiFileReader.Lyric, message.Data);
                    }
                    else if (message.Arg1 >= MidiFileReader.SequenceNumber
                        && message.Arg1 <= MidiFileReader.CuePoint)
                    {
                        MidiText text = new MidiText(Owner, message.Arg1, message.Data);
                        text.Track = this;
                        music.Add(new MidiMusicEvent(t, text));
                        if (text.Type == MidiFileReader.SequenceTrackName)
                        {
                            Name = text.Text;
                        }
                    }

                    //Upstream's remaining else writes a SKIP line when --verbose is on;
                    //that switch is log-level and deliberately absent (D58).
                }
            }

            if (lastLyric != null)
            {
                // last_lyric.clocks = t - last_time
                // hmm
                lastLyric.Clocks = Owner.ClocksPer4;
                music.Add(new MidiMusicEvent(lastTime, lastLyric));
            }

            int i = 0;
            while (notes.Count > 0)
            {
                if (i < music.Count && notes[0].Time >= music[i].Time)
                {
                    i += 1;
                }
                else
                {
                    music.Insert(i, notes[0]);
                    notes.RemoveAt(0);
                }
            }

            if (endOfTrackTime != null)
            {
                music.Add(new MidiMusicEvent(endOfTrackTime.Value, new MidiEndOfTrack()));
            }

            return music;
        }

        /// <summary>python's <c>list(map(ord, text))</c>, with its own unpacking error.</summary>
        /// <param name="text">The meta event's data.</param>
        /// <param name="expected">How many values upstream unpacks.</param>
        /// <returns>The byte values.</returns>
        private static int[] Bytes(string text, int expected)
        {
            string data = text ?? string.Empty;
            if (data.Length != expected)
            {
                throw new ImportAbortedException(
                    data.Length < expected
                        ? "not enough values to unpack (expected "
                            + expected.ToString(CultureInfo.InvariantCulture) + ", got "
                            + data.Length.ToString(CultureInfo.InvariantCulture) + ")"
                        : "too many values to unpack (expected "
                            + expected.ToString(CultureInfo.InvariantCulture) + ")");
            }

            int[] values = new int[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                values[i] = data[i];
            }

            return values;
        }
    }

    /// <summary>midi2ly.py:623-643. A track, which is a channel with channels in it.</summary>
    internal sealed class MidiTrack : MidiChannel
    {
        internal MidiTrack(MidiConverter owner)
            : base(owner, null)
        {
        }

        /// <summary>Gets the channels found inside this track.</summary>
        internal SortedDictionary<int, MidiChannel> Channels { get; }
            = new SortedDictionary<int, MidiChannel>();

        /// <summary>midi2ly.py:631-637.</summary>
        /// <param name="e">The event.</param>
        /// <param name="channel">Which channel, or null for the track itself.</param>
        internal void Add(MidiTrackEvent e, int? channel)
        {
            if (channel == null)
            {
                Events.Add(e);
            }
            else
            {
                if (!Channels.TryGetValue(channel.Value, out MidiChannel found))
                {
                    found = new MidiChannel(Owner, channel.Value);
                    Channels[channel.Value] = found;
                }

                found.Add(e);
            }
        }

        /// <summary>midi2ly.py:639-643.</summary>
        /// <returns>Every voice: the track's own, then each channel's.</returns>
        internal List<List<List<MidiMusicEvent>>> GetVoices()
        {
            List<List<List<MidiMusicEvent>>> voices
                = new List<List<List<MidiMusicEvent>>> { GetVoice() };
            foreach (KeyValuePair<int, MidiChannel> entry in Channels)
            {
                voices.Add(entry.Value.GetVoice());
            }

            return voices;
        }
    }

    /// <summary>midi2ly.py:646-656.</summary>
    /// <param name="events">The track's events.</param>
    /// <returns>The track.</returns>
    private MidiTrack CreateTrack(List<MidiTrackEvent> events)
    {
        MidiTrack track = new MidiTrack(this);
        foreach (MidiTrackEvent e in events)
        {
            int status = e.Message.Status;
            if (status > 0x7f && status < 0xf0)
            {
                int channel = status & 0x0f;
                track.Add(
                    new MidiTrackEvent(e.Time, e.Message.WithStatus(status & 0xf0)),
                    channel);
            }
            else
            {
                track.Add(e, null);
            }
        }

        return track;
    }

    /// <summary>midi2ly.py:659-667.</summary>
    /// <param name="clocks">The value to quantise.</param>
    /// <param name="quant">What to quantise on.</param>
    /// <returns>The quantised value.</returns>
    private double QuantiseClocks(double clocks, double quant)
    {
        double q = PyInt(clocks / quant) * quant;
        if (q != clocks)
        {
            foreach (double tquant in AllowedTupletClocks)
            {
                if (PyInt(clocks / tquant) * tquant == clocks)
                {
                    return clocks;
                }
            }

            if (2 * (clocks - q) > quant)
            {
                q += quant;
            }
        }

        return q;
    }

    /// <summary>midi2ly.py:670-691.</summary>
    /// <param name="pitches">The notes still sounding.</param>
    /// <param name="notes">Where finished notes go.</param>
    /// <param name="t">When the note ended.</param>
    /// <param name="e">Which pitch.</param>
    private void EndNote(
        Dictionary<int, (double Time, int Velocity)> pitches,
        List<MidiMusicEvent> notes, double t, int e)
    {
        if (!pitches.TryGetValue(e, out (double Time, int Velocity) held))
        {
            //except KeyError: pass
            return;
        }

        double lt = held.Time;
        int vel = held.Velocity;
        pitches.Remove(e);

        int i = notes.Count - 1;
        while (i > 0)
        {
            if (notes[i].Time > lt)
            {
                i -= 1;
            }
            else
            {
                break;
            }
        }

        double d = t - lt;
        if (DurationQuantClocks != 0)
        {
            d = QuantiseClocks(d, DurationQuantClocks);
            if (d == 0)
            {
                d = DurationQuantClocks;
            }
        }

        notes.Insert(i + 1, new MidiMusicEvent(lt, new MidiNote(this, d, e, vel)));
    }

    /// <summary>
    /// midi2ly.py:539-540 and 549-550: <c>for i in pitches: end_note(...)</c>.
    /// </summary>
    /// <param name="pitches">The notes still sounding.</param>
    /// <param name="notes">Where finished notes go.</param>
    /// <param name="t">When they ended.</param>
    /// <remarks>
    /// ⚠ AN UPSTREAM DEFECT, REPRODUCED. The loop DELETES from the dictionary it is
    /// walking, so python raises "dictionary changed size during iteration" as soon as
    /// there is anything to end — a MIDI file with a note still sounding at an
    /// all-notes-off or at the end of a track does not convert. An empty dictionary is
    /// fine, which is why the whole of LilyPond's own MIDI output converts: it always
    /// ends its notes first.
    /// </remarks>
    private void EndEveryNote(
        Dictionary<int, (double Time, int Velocity)> pitches,
        List<MidiMusicEvent> notes, double t)
    {
        int at = 0;
        int size = pitches.Count;
        List<int> keys = new List<int>(pitches.Keys);
        while (at < keys.Count)
        {
            if (pitches.Count != size)
            {
                throw new ImportAbortedException(
                    "dictionary changed size during iteration");
            }

            EndNote(pitches, notes, t, keys[at]);
            at++;
        }

        if (pitches.Count != size)
        {
            throw new ImportAbortedException("dictionary changed size during iteration");
        }
    }

    /// <summary>midi2ly.py:694-713.</summary>
    /// <param name="channel">The music.</param>
    /// <returns>The threads it splits into.</returns>
    private static List<List<MidiMusicEvent>> UnthreadNotes(List<MidiMusicEvent> channel)
    {
        List<List<MidiMusicEvent>> threads = new List<List<MidiMusicEvent>>();
        while (channel.Count > 0)
        {
            List<MidiMusicEvent> thread = new List<MidiMusicEvent>();
            double endBusyT = 0;
            double startBusyT = 0;
            List<MidiMusicEvent> todo = new List<MidiMusicEvent>();
            foreach (MidiMusicEvent e in channel)
            {
                double t = e.Time;
                if (e.Item is MidiNote note
                    && ((t == startBusyT && note.Clocks + t == endBusyT)
                        || t >= endBusyT))
                {
                    thread.Add(e);
                    startBusyT = t;
                    endBusyT = t + note.Clocks;
                }
                else if (e.Item is MidiTime || e.Item is MidiKey || e.Item is MidiText
                    || e.Item is MidiTempo || e.Item is MidiEndOfTrack)
                {
                    thread.Add(e);
                }
                else
                {
                    todo.Add(e);
                }
            }

            threads.Add(thread);
            channel = todo;
        }

        return threads;
    }

    /// <summary>midi2ly.py:716-723.</summary>
    /// <param name="skip">How a rest is spelled.</param>
    /// <param name="clocks">How long it lasts.</param>
    /// <returns>The rest.</returns>
    private string DumpSkip(string skip, double clocks)
    {
        MidiDuration savedDuration = ReferenceNote.Duration;
        string result = skip + new MidiDuration(this, clocks).Dump() + " ";

        // "\skip D" does not change the reference duration like "sD", so we restore it
        // after Duration.dump changes it.
        if (skip[0] == '\\')
        {
            ReferenceNote.Duration = savedDuration;
        }

        return result;
    }

    /// <summary>midi2ly.py:730-753.</summary>
    /// <param name="ch">The items sounding together.</param>
    /// <returns>The chord.</returns>
    private string DumpChord(List<MidiMusicItem> ch)
    {
        string s = string.Empty;
        List<MidiNote> notes = new List<MidiNote>();
        foreach (MidiMusicItem i in ch)
        {
            if (i is MidiNote note)
            {
                notes.Add(note);
            }
            else
            {
                s += i.Dump();
            }
        }

        if (notes.Count == 1)
        {
            s += notes[0].Dump();
        }
        else if (notes.Count > 1)
        {
            MidiDuration referenceDur = ReferenceNote.Duration;
            s += "<";
            s += notes[0].Dump(false);
            MidiNote r = ReferenceNote;
            for (int i = 1; i < notes.Count; i++)
            {
                s += notes[i].Dump(false);
            }

            s += ">";
            if (r.Duration.Compare(referenceDur) != 0 || Options.ExplicitDurations)
            {
                s += r.Duration.Dump();
            }

            s += " ";
            ReferenceNote = r;
        }

        return s;
    }

    /// <summary>midi2ly.py:756-769.</summary>
    /// <param name="lastBarT">When the last bar line was written.</param>
    /// <param name="t">Now.</param>
    /// <param name="barCount">Which bar this is.</param>
    /// <returns>The bar line, and the two counters.</returns>
    private (string Text, double LastBarT, double BarCount) DumpBarLine(
        double lastBarT, double t, double barCount)
    {
        string s = string.Empty;
        double barT = Time.BarClocks();
        if (t - lastBarT >= barT)
        {
            barCount += (t - lastBarT) / barT;

            if (t - lastBarT == barT)
            {
                s = "\n  | % " + PyInt(barCount).ToString(CultureInfo.InvariantCulture)
                    + "\n  ";
                lastBarT = t;
            }
            else
            {
                // urg, this will barf at meter changes
                lastBarT += (t - lastBarT) / barT * barT;
            }
        }

        return (s, lastBarT, barCount);
    }

    /// <summary>midi2ly.py:772-838.</summary>
    /// <param name="thread">One voice's events.</param>
    /// <param name="skip">How a rest is spelled.</param>
    /// <returns>The voice.</returns>
    private string DumpVoice(List<MidiMusicEvent> thread, string skip)
    {
        MidiNote reference = new MidiNote(this, 0, 4 * 12, 0);
        if (ReferenceNote == null)
        {
            ReferenceNote = reference;
        }
        else
        {
            reference.Duration = ReferenceNote.Duration;
            ReferenceNote = reference;
        }

        MidiMusicEvent lastE = null;
        List<(double Time, List<MidiMusicItem> Items)> chs
            = new List<(double, List<MidiMusicItem>)>();
        List<MidiMusicItem> ch = new List<MidiMusicItem>();

        foreach (MidiMusicEvent e in thread)
        {
            if (lastE != null && lastE.Time == e.Time)
            {
                ch.Add(e.Item);
            }
            else
            {
                if (ch.Count > 0 && lastE != null)
                {
                    chs.Add((lastE.Time, ch));
                }

                ch = new List<MidiMusicItem> { e.Item };
            }

            lastE = e;
        }

        if (ch.Count > 0)
        {
            chs.Add((lastE.Time, ch));
        }

        double lastT = 0;
        double lastBarT = 0;
        double barCount = 1;

        List<string> lines = new List<string> { string.Empty };
        foreach ((double Time, List<MidiMusicItem> Items) entry in chs)
        {
            double t = entry.Time;

            int i = lines[lines.Count - 1].LastIndexOf('\n') + 1;
            if (lines[lines.Count - 1].Length - i > LineBell)
            {
                lines.Add(string.Empty);
            }

            if (t - lastT > 0)
            {
                double d = t - lastT;
                if (BarMax != 0 && t > Time.BarClocks() * BarMax)
                {
                    d = (Time.BarClocks() * BarMax) - lastT;
                }

                lines[lines.Count - 1] += DumpSkip(skip, d);
            }
            else if (t - lastT < 0)
            {
                _stderr.Write(ProgramName + ": error: BUG: time skew\n");
            }

            (string s, double newLastBarT, double newBarCount)
                = DumpBarLine(lastBarT, t, barCount);
            lastBarT = newLastBarT;
            barCount = newBarCount;

            if (BarMax != 0 && barCount > BarMax)
            {
                break;
            }

            lines[lines.Count - 1] += s;
            lines[lines.Count - 1] += DumpChord(entry.Items);

            double clocks = entry.Items.Count > 0
                ? entry.Items.Max(item => item.Clocks)
                : 0;

            lastT = t + clocks;

            (s, newLastBarT, newBarCount) = DumpBarLine(lastBarT, lastT, barCount);
            lastBarT = newLastBarT;
            barCount = newBarCount;
            lines[lines.Count - 1] += s;
        }

        return string.Join("\n  ", lines) + "\n";
    }

    /// <summary>midi2ly.py:841-849.</summary>
    /// <param name="i">The index.</param>
    /// <returns>The letters.</returns>
    private static string Number2Ascii(int i)
    {
        string s = string.Empty;
        i += 1;
        while (i > 0)
        {
            int m = PyMod(i - 1, 26);
            s = ((char)(m + 'A')).ToString() + s;
            i = FloorDiv(i - m, 26);
        }

        return s;
    }

    private static string GetTrackName(int i) => "track" + Number2Ascii(i);

    private static string GetChannelName(int i) => "channel" + Number2Ascii(i);

    private static string GetVoiceName(int i, bool zeroTooP = false)
        => i != 0 || zeroTooP ? "voice" + Number2Ascii(i) : string.Empty;

    /// <summary>midi2ly.py:870-887.</summary>
    /// <param name="averagePitch">Each voice's average pitch.</param>
    /// <returns>The voice name each one takes.</returns>
    private static string[] GetVoiceLayout(List<double> averagePitch)
    {
        Dictionary<double, List<int>> d = new Dictionary<double, List<int>>();
        for (int i = 0; i < averagePitch.Count; i++)
        {
            double pitch = averagePitch[i];
            if (!d.TryGetValue(pitch, out List<int> at))
            {
                at = new List<int>();
                d[pitch] = at;
            }

            at.Add(i);
        }

        List<double> s = new List<double>(averagePitch);
        s.Sort();
        s.Reverse();

        int nonEmpty = s.Count(x => x != 0);
        string[] names = nonEmpty > 2
            ? new[] { "One", "Three", "Four", "Two" }
            : new[] { "One", "Two" };

        string[] layout = new string[averagePitch.Count];
        for (int at = 0; at < layout.Length; at++)
        {
            layout[at] = string.Empty;
        }

        for (int at = 0; at < Math.Min(s.Count, names.Length); at++)
        {
            double i = s[at];
            if (i == 0)
            {
                continue;
            }

            List<int> v = d[i];
            if (v.Count == 0)
            {
                //python: v[0] on an emptied list is an IndexError.
                throw new ImportAbortedException("list index out of range");
            }

            int first = v[0];
            d[i] = v.GetRange(1, v.Count - 1);
            layout[first] = names[at];
        }

        return layout;
    }

    /// <summary>midi2ly.py:890-955.</summary>
    /// <param name="track">The staff's channels of voices.</param>
    /// <param name="n">Which staff this is.</param>
    /// <returns>The staff's definitions.</returns>
    private string DumpTrack(List<List<List<MidiMusicEvent>>> track, int n)
    {
        string s = "\n";
        string trackName = GetTrackName(n);

        List<double> averagePitch = TrackAveragePitch(track);
        int voices = averagePitch.Skip(1).Count(x => x != 0);
        MidiClef clef = GetBestClef(averagePitch[0]);

        int c = 0;
        int vv = 0;
        foreach (List<List<MidiMusicEvent>> channel in track)
        {
            int v = 0;
            string channelName = GetChannelName(c);
            c += 1;
            foreach (List<MidiMusicEvent> voice in channel)
            {
                string voiceName = GetVoiceName(v);
                string voiceId = trackName + channelName + voiceName;
                MidiMusicItem item = VoiceFirstItem(voice);

                string skip;
                if (item is MidiNote)
                {
                    skip = Options.Skip ? "s" : "r";
                    s += voiceId + " = ";
                    if (!Options.AbsolutePitches)
                    {
                        s += "\\relative c ";
                    }
                }
                else if (item is MidiText)
                {
                    skip = "\" \"";
                    s += voiceId + " = \\lyricmode ";
                }
                else
                {
                    skip = "\\skip ";
                    s += voiceId + " = ";
                }

                s += "{\n";
                if (n == 0 && vv == 0 && Key != null)
                {
                    s += Key.Dump();
                }

                if (averagePitch[vv + 1] != 0 && voices > 1)
                {
                    string vl = GetVoiceLayout(averagePitch.Skip(1).ToList())[vv];
                    if (vl != string.Empty)
                    {
                        s += "  \\voice" + vl + "\n";
                    }
                    else
                    {
                        _stderr.Write(
                            ProgramName
                            + ": warning: found more than 5 voices on a staff, "
                            + "expect bad output\n");
                    }
                }

                s += "  " + DumpVoice(voice, skip);
                s += "}\n\n";
                v += 1;
                vv += 1;
            }
        }

        s += trackName + " = <<\n";

        if (clef.Type != 2)
        {
            s += clef.Dump() + "\n";
        }

        c = 0;
        vv = 0;
        foreach (List<List<MidiMusicEvent>> channel in track)
        {
            int v = 0;
            string channelName = GetChannelName(c);
            c += 1;
            foreach (List<MidiMusicEvent> voice in channel)
            {
                string voiceContextName = GetVoiceName(vv, true);
                string voiceName = GetVoiceName(v);
                v += 1;
                vv += 1;
                string voiceId = trackName + channelName + voiceName;
                MidiMusicItem item = VoiceFirstItem(voice);
                string context = item is MidiText ? "Lyrics" : "Voice";
                s += "  \\context " + context + " = " + voiceContextName + " \\"
                    + voiceId + "\n";
            }
        }

        s += ">>\n\n";
        return s;
    }

    /// <summary>midi2ly.py:958-964.</summary>
    /// <param name="voice">The voice.</param>
    /// <returns>The first note or lyric in it.</returns>
    private static MidiMusicItem VoiceFirstItem(List<MidiMusicEvent> voice)
    {
        foreach (MidiMusicEvent e in voice)
        {
            if (e.Item is MidiNote
                || (e.Item is MidiText text && text.Type == MidiFileReader.Lyric))
            {
                return e.Item;
            }
        }

        return null;
    }

    /// <summary>midi2ly.py:967-972.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The first note or lyric in it.</returns>
    private static MidiMusicItem ChannelFirstItem(List<List<MidiMusicEvent>> channel)
    {
        foreach (List<MidiMusicEvent> voice in channel)
        {
            MidiMusicItem first = VoiceFirstItem(voice);
            if (first != null)
            {
                return first;
            }
        }

        return null;
    }

    /// <summary>midi2ly.py:975-980.</summary>
    /// <param name="track">The track.</param>
    /// <returns>The first note or lyric in it.</returns>
    private static MidiMusicItem TrackFirstItem(List<List<List<MidiMusicEvent>>> track)
    {
        foreach (List<List<MidiMusicEvent>> channel in track)
        {
            MidiMusicItem first = ChannelFirstItem(channel);
            if (first != null)
            {
                return first;
            }
        }

        return null;
    }

    /// <summary>midi2ly.py:983-1002.</summary>
    /// <param name="track">The track.</param>
    /// <returns>The average pitch of the whole track, then of each voice.</returns>
    private static List<double> TrackAveragePitch(List<List<List<MidiMusicEvent>>> track)
    {
        int i = 0;
        List<double> p = new List<double> { 0 };
        int v = 1;
        foreach (List<List<MidiMusicEvent>> channel in track)
        {
            foreach (List<MidiMusicEvent> voice in channel)
            {
                int c = 0;
                p.Add(0);
                foreach (MidiMusicEvent e in voice)
                {
                    if (e.Item is MidiNote note)
                    {
                        i += 1;
                        c += 1;
                        p[v] += note.Pitch;
                    }
                }

                if (c != 0)
                {
                    p[0] += p[v];
                    p[v] = p[v] / c;
                }

                v += 1;
            }
        }

        if (i != 0)
        {
            p[0] = p[0] / i;
        }

        return p;
    }

    /// <summary>midi2ly.py:1005-1013.</summary>
    /// <param name="averagePitch">The average pitch.</param>
    /// <returns>The clef that fits.</returns>
    private static MidiClef GetBestClef(double averagePitch)
    {
        if (averagePitch != 0)
        {
            if (averagePitch <= 3 * 12)
            {
                return new MidiClef(0);
            }

            if (averagePitch <= 5 * 12)
            {
                return new MidiClef(1);
            }

            if (averagePitch >= 7 * 12)
            {
                return new MidiClef(3);
            }
        }

        return new MidiClef(2);
    }

    /// <summary>midi2ly.py:1016-1021. One staff, which is one track's voices.</summary>
    private sealed class MidiStaff
    {
        internal MidiStaff(MidiTrack track) => Voices = track.GetVoices();

        /// <summary>Gets or sets the voices.</summary>
        internal List<List<List<MidiMusicEvent>>> Voices { get; set; }
    }

    /// <summary>midi2ly.py:1024-1160.</summary>
    /// <param name="midiData">The MIDI file's bytes.</param>
    /// <returns>The LilyPond source.</returns>
    /// <remarks>
    /// The <c>\version</c> line is upstream's own frozen
    /// <see cref="LastVerifiedOutputVersion"/>. See
    /// <see cref="AbcConverter.Convert"/>'s remarks and D63; the two importers answer
    /// the same way.
    /// </remarks>
    internal string Convert(byte[] midiData)
    {
        PrepareOptions();

        double clocksMax = BarMax * ClocksPer1 * 2;
        MidiFile midiDump = MidiFileReader.Parse(midiData, (long)clocksMax);

        ClocksPer1 = midiDump.Division;
        ClocksPer4 = ClocksPer1 / 4.0;
        Time = new MidiTime(this, 4, 4, (int)ClocksPer4);

        if (Options.StartQuant != null)
        {
            StartQuantClocks = ClocksPer1 / (double)Options.StartQuant.Value;
        }

        if (Options.DurationQuant != null)
        {
            DurationQuantClocks = ClocksPer1 / (double)Options.DurationQuant.Value;
        }

        AllowedTupletClocks = new List<double>();
        foreach (int[] tuplet in AllowedTuplets)
        {
            AllowedTupletClocks.Add(ClocksPer1 / (double)tuplet[0] * tuplet[1] / tuplet[2]);
        }

        List<MidiTrack> tracks = new List<MidiTrack>();
        foreach (List<MidiTrackEvent> t in midiDump.Tracks)
        {
            tracks.Add(CreateTrack(t));
        }

        // urg, parse all global track events, such as Key first
        // this fixes key in different voice/staff problem
        foreach (MidiTrack t in tracks)
        {
            t.Music = t.Parse();
        }

        MidiTrack prev = null;
        List<MidiStaff> staves = new List<MidiStaff>();
        foreach (MidiTrack t in tracks)
        {
            List<List<List<MidiMusicEvent>>> voices = t.GetVoices();
            if (t.Name != null && prev != null && prev.Name != null
                && t.Name.Split(':')[0] == prev.Name.Split(':')[0])
            {
                // staves[-1].voices += voices
                // all global track events first
                MidiStaff last = staves[staves.Count - 1];
                List<List<List<MidiMusicEvent>>> merged
                    = new List<List<List<MidiMusicEvent>>> { last.Voices[0], voices[0] };
                merged.AddRange(last.Voices.Skip(1));
                merged.AddRange(voices.Skip(1));
                last.Voices = merged;
            }
            else
            {
                staves.Add(new MidiStaff(t));
            }

            prev = t;
        }

        string tag = "% Lily was here -- automatically converted by " + ProgramName
            + " from " + (Options.SourceName ?? string.Empty);

        StringBuilder builder = new StringBuilder(tag);
        builder.Append("\n\\version \"").Append(LastVerifiedOutputVersion).Append("\"\n");

        builder.Append(
            "\n\\layout {\n  \\context {\n    \\Voice\n"
            + "    \\remove Note_heads_engraver\n"
            + "    \\consists Completion_heads_engraver\n"
            + "    \\remove Rest_engraver\n"
            + "    \\consists Completion_rest_engraver\n  }\n}\n");

        foreach (string i in Options.IncludeHeader)
        {
            builder.Append("\n% included from ").Append(i).Append("\n");
            builder.Append(ReadIncludedHeader(i));
            //⚠ Upstream tests the WHOLE accumulated document, not the file it just
            //read; the newline is added when the document does not already end in one.
            if (builder.Length == 0 || builder[builder.Length - 1] != '\n')
            {
                builder.Append("\n");
            }

            builder.Append("% end\n");
        }

        for (int i = 0; i < staves.Count; i++)
        {
            builder.Append(DumpTrack(staves[i].Voices, i));
        }

        builder.Append("\n\\score {\n  <<\n");

        string controlTrack = null;
        int outputTrackCount = 0;
        for (int i = 0; i < staves.Count; i++)
        {
            MidiStaff staff = staves[i];
            string trackName = GetTrackName(i);
            MidiMusicItem item = TrackFirstItem(staff.Voices);
            string staffName = trackName;
            string context = null;
            if (i == 0 && item == null && staves.Count > 1)
            {
                controlTrack = trackName;
                continue;
            }

            if (item is MidiNote)
            {
                context = "Staff";
                if (controlTrack != null)
                {
                    builder.Append("    \\context ").Append(context).Append("=")
                        .Append(staffName).Append(" \\").Append(controlTrack)
                        .Append("\n");
                }
            }
            else if (item is MidiText)
            {
                context = "Lyrics";
            }

            if (context != null)
            {
                outputTrackCount += 1;
                builder.Append("    \\context ").Append(context).Append("=")
                    .Append(staffName).Append(" \\").Append(trackName).Append("\n");
            }

            // If we found a control track but no other tracks with which to combine it,
            // create a Staff for the control track alone.
            if (outputTrackCount == 0 && controlTrack != null)
            {
                builder.Append("    \\context Staff \\").Append(controlTrack).Append("\n");
            }
        }

        builder.Append("  >>\n  \\layout {}\n  \\midi {}\n}\n");
        return builder.ToString();
    }

    /// <summary>Reads a file named by <c>--include-header</c>.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    /// <remarks>
    /// Upstream opens the file and lets the exception out if it cannot; the same
    /// failure stops the import here rather than leaving a document with a hole in it.
    /// </remarks>
    private static string ReadIncludedHeader(string path)
    {
        try
        {
            return File.ReadAllText(path, new UTF8Encoding(false));
        }
        catch (IOException error)
        {
            throw new ImportAbortedException(error.Message);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new ImportAbortedException(error.Message);
        }
    }
}
