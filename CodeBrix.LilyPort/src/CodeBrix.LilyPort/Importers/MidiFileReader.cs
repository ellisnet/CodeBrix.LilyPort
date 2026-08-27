/*
   This file is part of LilyPond, the GNU music typesetter.

   Copyright (C) 2001--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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
using System.Text;

namespace CodeBrix.LilyPort.Importers; //was previously: python/midi.py;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Reads a Standard MIDI File into the tracks of events <see cref="MidiImporter"/>
/// walks.
/// </summary>
/// <remarks>
/// ⚠ THIS IS A PORT OF LILYPOND'S OWN <c>python/midi.py</c>, and not a general MIDI
/// reader. It is here because <c>midi2ly</c> is written against exactly what that
/// module does — including where it gives up, what it calls the parts, and the fact
/// that it multiplies the file's division by four before anyone sees it. Reaching for
/// a real MIDI library instead would be a new dependency and a different answer.
/// <para>
/// A file is returned as <c>((format, division), tracks)</c> in upstream's own shape:
/// see <see cref="MidiFile"/>.
/// </para>
/// </remarks>
internal static class MidiFileReader
{
    // Channel voice messages
    internal const int NoteOff = 0x80;
    internal const int NoteOn = 0x90;
    internal const int PolyphonicKeyPressure = 0xa0;
    internal const int ControllerChange = 0xb0;
    internal const int ProgramChange = 0xc0;
    internal const int ChannelKeyPressure = 0xd0;
    internal const int PitchBend = 0xe0;

    // Channel mode messages
    internal const int AllSoundOff = 0x78;
    internal const int ResetAllControllers = 0x79;
    internal const int LocalControl = 0x7a;
    internal const int AllNotesOff = 0x7b;
    internal const int OmniModeOff = 0x7c;
    internal const int OmniModeOn = 0x7d;
    internal const int MonoModeOn = 0x7e;
    internal const int PolyModeOn = 0x7f;

    // Meta events
    internal const int SequenceNumber = 0x0;
    internal const int TextEvent = 0x1;
    internal const int CopyrightNotice = 0x2;
    internal const int SequenceTrackName = 0x3;
    internal const int InstrumentName = 0x4;
    internal const int Lyric = 0x5;
    internal const int Marker = 0x6;
    internal const int CuePoint = 0x7;
    internal const int ProgramName = 0x8;
    internal const int DeviceName = 0x9;
    internal const int MidiChannelPrefix = 0x20;
    internal const int MidiPort = 0x21;
    internal const int EndOfTrack = 0x2f;
    internal const int SetTempo = 0x51;
    internal const int SmtpeOffset = 0x54;
    internal const int TimeSignature = 0x58;
    internal const int KeySignature = 0x59;
    internal const int XmfPatchTypePrefix = 0x60;
    internal const int SequencerSpecificMetaEvent = 0x7f;
    internal const int MetaEvent = 0xff;

    /// <summary>python/midi.py:96-101.</summary>
    /// <param name="nextByte">The byte already read.</param>
    /// <param name="reader">Where the rest comes from.</param>
    /// <returns>The number.</returns>
    private static long GetVariableLengthNumber(int nextByte, ByteReader reader)
    {
        long sum = 0;
        while (nextByte >= 0x80)
        {
            sum = (sum + (nextByte & 0x7F)) << 7;
            nextByte = reader.Next();
        }

        return sum + nextByte;
    }

    /// <summary>python/midi.py:112-114.</summary>
    /// <param name="nextByte">The byte already read.</param>
    /// <param name="reader">Where the rest comes from.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// ⚠ ONE BYTE IS ONE CHARACTER. Upstream builds the string with <c>chr(byte)</c>,
    /// so a byte over 127 becomes the code point of that number and nothing is decoded
    /// as UTF-8. midi2ly then reads the bytes back out with <c>ord</c>, which only
    /// works because of this; a "correct" decode here would break the meta events.
    /// </remarks>
    private static string ReadString(int nextByte, ByteReader reader)
    {
        long length = GetVariableLengthNumber(nextByte, reader);
        StringBuilder text = new StringBuilder();
        for (long i = 0; i < length; i++)
        {
            text.Append((char)reader.Next());
        }

        return text.ToString();
    }

    /// <summary>python/midi.py:117-152. One event, by the top nibble of its status.</summary>
    /// <param name="status">The running status.</param>
    /// <param name="nextByte">The first data byte.</param>
    /// <param name="reader">Where the rest comes from.</param>
    /// <returns>The message.</returns>
    private static MidiMessage ReadMidiEvent(int status, int nextByte, ByteReader reader)
    {
        switch (status >> 4)
        {
            case 0x0:
                //_first_command_is_repeat: a track whose first command relies on a
                //running status that was never set.
                throw new MidiFormatException(
                    "the first midi command in the track is a repeat");

            case 0x8:  // note off
            case 0x9:  // note on
            case 0xa:  // poly aftertouch
            case 0xb:  // control
            case 0xe:  // pitchwheel range
                return MidiMessage.ThreeBytes(status, nextByte, reader.Next());

            case 0xc:  // prog change
            case 0xd:  // ch aftertouch
                return MidiMessage.TwoBytes(status, nextByte);

            case 0xf:
                return status == 0xff
                    ? MidiMessage.Meta(status, nextByte, ReadString(reader.Next(), reader))
                    : MidiMessage.SystemString(status, ReadString(nextByte, reader));

            default:
                //10 through 70 are None in upstream's table, so python raises
                //TypeError: 'NoneType' object is not callable and the read stops.
                throw new MidiFormatException(
                    "'NoneType' object is not callable");
        }
    }

    /// <summary>python/midi.py:155-175.</summary>
    /// <param name="data">The track's bytes.</param>
    /// <param name="clocksMax">Stop after this many clocks, or zero for all of them.</param>
    /// <returns>The events.</returns>
    private static List<MidiTrackEvent> ParseTrackBody(byte[] data, long clocksMax)
    {
        ByteReader reader = new ByteReader(data);
        List<MidiTrackEvent> events = new List<MidiTrackEvent>();

        long time = 0;
        int status = 0;
        try
        {
            while (reader.TryNext(out int nextByte))
            {
                time += GetVariableLengthNumber(nextByte, reader);
                if (clocksMax != 0 && time > clocksMax)
                {
                    break;
                }

                nextByte = reader.Next();
                if (nextByte >= 0x80)
                {
                    status = nextByte;
                    nextByte = reader.Next();
                }

                events.Add(new MidiTrackEvent(time, ReadMidiEvent(status, nextByte, reader)));
            }
        }
        catch (MidiEndOfDataException)
        {
            // If the track ended just before the start of an event, the loop above
            // exits normally. If it ends anywhere else, we end up here.
            //⚠ Upstream also PRINTS the number of bytes it had left, on standard
            //output — a debugging leftover. A library does not own standard output,
            //so the count is not printed; the error it then raises is identical.
            throw new MidiFormatException("a track ended in the middle of a MIDI command");
        }

        return events;
    }

    /// <summary>python/midi.py:178-192.</summary>
    /// <param name="data">The whole file.</param>
    /// <param name="pos">Where the hunk starts.</param>
    /// <param name="type">What to call it in a diagnostic.</param>
    /// <param name="magic">The four bytes it must open with.</param>
    /// <returns>The hunk's body, and where the next one starts.</returns>
    private static (byte[] Body, int EndPos) ParseHunk(
        byte[] data, int pos, string type, string magic)
    {
        string found = Ascii(data, pos, 4);
        if (found != magic)
        {
            throw new MidiFormatException(
                "expected " + BytesRepr(magic) + ", got " + BytesRepr(found));
        }

        if (pos + 8 > data.Length)
        {
            throw new MidiFormatException(
                "the " + type + " header is truncated (may be an incomplete download)");
        }

        long length = ((long)data[pos + 4] << 24) | ((long)data[pos + 5] << 16)
            | ((long)data[pos + 6] << 8) | data[pos + 7];
        long endPos = pos + 8 + length;
        int available = Math.Max(0, Math.Min(data.Length, (int)Math.Min(endPos, int.MaxValue)) - (pos + 8));
        if (available != length)
        {
            throw new MidiFormatException(
                "the " + type + " is truncated (may be an incomplete download)");
        }

        byte[] body = new byte[available];
        Array.Copy(data, pos + 8, body, 0, available);
        return (body, (int)endPos);
    }

    /// <summary>python/midi.py:195-203.</summary>
    /// <param name="midi">The whole file.</param>
    /// <param name="pos">Where the first track starts.</param>
    /// <param name="numTracks">How many tracks the header claims.</param>
    /// <param name="clocksMax">Stop after this many clocks, or zero for all of them.</param>
    /// <returns>The tracks.</returns>
    private static List<List<MidiTrackEvent>> ParseTracks(
        byte[] midi, int pos, int numTracks, long clocksMax)
    {
        if (numTracks > 256)
        {
            throw new MidiFormatException(
                "too many tracks: " + numTracks.ToString(CultureInfo.InvariantCulture));
        }

        List<List<MidiTrackEvent>> tracks = new List<List<MidiTrackEvent>>();
        for (int i = 0; i < numTracks; i++)
        {
            (byte[] trackData, int next) = ParseHunk(midi, pos, "track", "MTrk");
            pos = next;
            tracks.Add(ParseTrackBody(trackData, clocksMax));
        }

        return tracks;
    }

    /// <summary>python/midi.py:213-222.</summary>
    /// <param name="midi">The file's bytes.</param>
    /// <param name="clocksMax">Stop after this many clocks, or zero for all of them.</param>
    /// <returns>The file.</returns>
    /// <remarks>
    /// ⚠ THE DIVISION IS MULTIPLIED BY FOUR before it is returned — upstream's own
    /// header comment says "division (&gt;0) = TPQN*4" — so what midi2ly calls
    /// <c>clocks_per_1</c> is clocks per WHOLE note, not per quarter.
    /// </remarks>
    internal static MidiFile Parse(byte[] midi, long clocksMax)
    {
        (byte[] header, int firstTrackPos) = ParseHunk(midi ?? Array.Empty<byte>(), 0, "file", "MThd");
        if (header.Length < 6)
        {
            throw new MidiFormatException("the file header is too short");
        }

        int format = (header[0] << 8) | header[1];
        int numTracks = (header[2] << 8) | header[3];
        int division = (header[4] << 8) | header[5];

        List<List<MidiTrackEvent>> tracks
            = ParseTracks(midi, firstTrackPos, numTracks, clocksMax);
        return new MidiFile(format, division * 4, tracks);
    }

    /// <summary>Reads bytes as characters, the way upstream compares its magics.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="pos">Where to start.</param>
    /// <param name="count">How many.</param>
    /// <returns>The text, shorter than asked for when the data runs out.</returns>
    private static string Ascii(byte[] data, int pos, int count)
    {
        StringBuilder text = new StringBuilder(count);
        for (int i = pos; i < pos + count && i < data.Length; i++)
        {
            if (i >= 0)
            {
                text.Append((char)data[i]);
            }
        }

        return text.ToString();
    }

    /// <summary>python's <c>repr</c> of a bytes object, for the one message that uses it.</summary>
    /// <param name="text">The bytes, one per character.</param>
    /// <returns>The repr.</returns>
    private static string BytesRepr(string text)
    {
        StringBuilder result = new StringBuilder("b'");
        foreach (char c in text)
        {
            if (c == '\\' || c == '\'')
            {
                result.Append('\\').Append(c);
            }
            else if (c == '\n')
            {
                result.Append("\\n");
            }
            else if (c == '\r')
            {
                result.Append("\\r");
            }
            else if (c == '\t')
            {
                result.Append("\\t");
            }
            else if (c >= ' ' && c <= '~')
            {
                result.Append(c);
            }
            else
            {
                result.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
            }
        }

        return result.Append('\'').ToString();
    }

    /// <summary>python's byte iterator, whose exhaustion is an exception.</summary>
    private sealed class ByteReader
    {
        private readonly byte[] _data;
        private int _pos;

        internal ByteReader(byte[] data) => _data = data;

        /// <summary>The <c>for</c> loop's own read, which simply ends.</summary>
        /// <param name="value">The byte.</param>
        /// <returns>Whether there was one.</returns>
        internal bool TryNext(out int value)
        {
            if (_pos >= _data.Length)
            {
                value = 0;
                return false;
            }

            value = _data[_pos++];
            return true;
        }

        /// <summary><c>next()</c>, which raises <c>StopIteration</c> at the end.</summary>
        /// <returns>The byte.</returns>
        internal int Next()
            => _pos < _data.Length ? _data[_pos++] : throw new MidiEndOfDataException();
    }
}

/// <summary>A MIDI file, in the shape <c>python/midi.py</c> returns it.</summary>
internal sealed class MidiFile
{
    internal MidiFile(int format, int division, List<List<MidiTrackEvent>> tracks)
    {
        Format = format;
        Division = division;
        Tracks = tracks;
    }

    /// <summary>Gets the file format — 0, 1 or 2.</summary>
    internal int Format { get; }

    /// <summary>Gets the division, ALREADY multiplied by four.</summary>
    internal int Division { get; }

    /// <summary>Gets the tracks.</summary>
    internal List<List<MidiTrackEvent>> Tracks { get; }
}

/// <summary>One event: a cumulative time, and what happened at it.</summary>
internal sealed class MidiTrackEvent
{
    internal MidiTrackEvent(long time, MidiMessage message)
    {
        Time = time;
        Message = message;
    }

    /// <summary>Gets when, in cumulative delta time.</summary>
    internal long Time { get; }

    /// <summary>Gets what.</summary>
    internal MidiMessage Message { get; }
}

/// <summary>
/// One MIDI or meta message, held as the two- or three-element tuple upstream yields.
/// </summary>
/// <remarks>
/// The arity is part of the data: a program change is <c>(status, arg)</c> and a note
/// on is <c>(status, pitch, velocity)</c>, and midi2ly reads the third element without
/// checking. A meta event's third element is a STRING of one character per byte.
/// </remarks>
internal sealed class MidiMessage
{
    private MidiMessage(int status, int arg1, int arg2, string data, int length, bool arg1IsData)
    {
        Status = status;
        Arg1 = arg1;
        Arg2 = arg2;
        Data = data;
        Length = length;
        Arg1IsData = arg1IsData;
    }

    /// <summary>Gets the status byte — <c>e[1][0]</c>.</summary>
    internal int Status { get; }

    /// <summary>Gets the first argument — <c>e[1][1]</c>.</summary>
    internal int Arg1 { get; }

    /// <summary>Gets the second argument — <c>e[1][2]</c>, when there is one.</summary>
    internal int Arg2 { get; }

    /// <summary>Gets the string argument, for a meta or system message.</summary>
    internal string Data { get; }

    /// <summary>Gets how many elements the tuple has, counting the status.</summary>
    internal int Length { get; }

    /// <summary>Gets whether the string argument is the FIRST one.</summary>
    internal bool Arg1IsData { get; }

    internal static MidiMessage TwoBytes(int status, int arg1)
        => new MidiMessage(status, arg1, 0, null, 2, false);

    internal static MidiMessage ThreeBytes(int status, int arg1, int arg2)
        => new MidiMessage(status, arg1, arg2, null, 3, false);

    internal static MidiMessage Meta(int status, int arg1, string data)
        => new MidiMessage(status, arg1, 0, data, 3, false);

    internal static MidiMessage SystemString(int status, string data)
        => new MidiMessage(status, 0, 0, data, 2, true);

    /// <summary>
    /// The same message with its status replaced — midi2ly's <c>create_track</c>
    /// rebuilds a channel message with the channel nibble masked off.
    /// </summary>
    /// <param name="status">The new status.</param>
    /// <returns>The message.</returns>
    internal MidiMessage WithStatus(int status)
        => new MidiMessage(status, Arg1, Arg2, Data, Length, Arg1IsData);
}

/// <summary>python/midi.py's <c>error</c>.</summary>
internal sealed class MidiFormatException : Exception
{
    internal MidiFormatException(string message)
        : base(message)
    {
    }
}

/// <summary>python's <c>StopIteration</c>, raised where upstream's byte iterator ends.</summary>
internal sealed class MidiEndOfDataException : Exception
{
}
