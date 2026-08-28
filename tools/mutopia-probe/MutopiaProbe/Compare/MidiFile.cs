// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MutopiaProbe.Compare;

/// <summary>
/// A Standard MIDI File parsed into per-track event streams with the regression harness's four
/// normalisations (<c>compare-midi.py</c>): delta times become absolute ticks, running status is
/// expanded, the "LilyPond &lt;version&gt;" stamp becomes a marker, end-of-track is dropped.
/// Everything else — including the order of events sharing a tick — is kept as written.
/// </summary>
public sealed class MidiFile
{
    /// <summary>One normalised event.</summary>
    public sealed class Event
    {
        /// <summary>Gets or sets the absolute tick.</summary>
        public long Tick { get; set; }

        /// <summary>Gets or sets the status byte (0xFF for meta, 0xF0/0xF7 for sysex).</summary>
        public int Status { get; set; }

        /// <summary>Gets or sets the meta type for meta events, else -1.</summary>
        public int MetaType { get; set; } = -1;

        /// <summary>Gets or sets the data bytes.</summary>
        public byte[] Data { get; set; }

        /// <summary>Gets the event as a comparable string.</summary>
        /// <returns>The key.</returns>
        public string Key()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(Tick).Append(':').Append(Status.ToString("X2"));
            if (MetaType >= 0)
            {
                builder.Append('/').Append(MetaType.ToString("X2"));
            }

            builder.Append(':');
            foreach (byte value in Data)
            {
                builder.Append(value.ToString("X2"));
            }

            return builder.ToString();
        }

        /// <summary>Describes the event for a human.</summary>
        /// <returns>The description.</returns>
        public string Describe()
        {
            int kind = Status & 0xF0;
            int channel = Status & 0x0F;
            if (Status == 0xFF)
            {
                switch (MetaType)
                {
                    case 0x51:
                        int tempo = Data.Length >= 3 ? (Data[0] << 16) | (Data[1] << 8) | Data[2] : 0;
                        return "@" + Tick + " tempo " + tempo + "us/qn";
                    case 0x58:
                        return "@" + Tick + " time-sig " + (Data.Length > 1 ? Data[0] + "/" + (1 << Data[1]) : "?");
                    case 0x59:
                        return "@" + Tick + " key-sig " + (Data.Length > 1 ? ((sbyte)Data[0]).ToString() + (Data[1] == 1 ? "m" : "M") : "?");
                    case 0x01:
                    case 0x02:
                    case 0x03:
                    case 0x04:
                        return "@" + Tick + " text(" + MetaType.ToString("X2") + ") \"" + Encoding.Latin1.GetString(Data) + "\"";
                    default:
                        return "@" + Tick + " meta " + MetaType.ToString("X2") + " " + BitConverter.ToString(Data);
                }
            }

            switch (kind)
            {
                case 0x90:
                    return "@" + Tick + " note-on ch" + channel + " " + (Data.Length > 0 ? Data[0] : -1) + " v" + (Data.Length > 1 ? Data[1] : -1);
                case 0x80:
                    return "@" + Tick + " note-off ch" + channel + " " + (Data.Length > 0 ? Data[0] : -1);
                case 0xC0:
                    return "@" + Tick + " program ch" + channel + " " + (Data.Length > 0 ? Data[0] : -1);
                case 0xB0:
                    return "@" + Tick + " control ch" + channel + " " + (Data.Length > 0 ? Data[0] : -1) + "=" + (Data.Length > 1 ? Data[1] : -1);
                default:
                    return "@" + Tick + " status " + Status.ToString("X2") + " " + BitConverter.ToString(Data);
            }
        }
    }

    /// <summary>Gets the SMF format.</summary>
    public int Format { get; private set; }

    /// <summary>Gets the division (ticks per quarter note, or the SMPTE word).</summary>
    public int Division { get; private set; }

    /// <summary>Gets the tracks.</summary>
    public List<List<Event>> Tracks { get; } = new List<List<Event>>();

    /// <summary>Gets the version-stamp text found, or null.</summary>
    public string VersionStamp { get; private set; }

    /// <summary>Parses a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="InvalidDataException">When the bytes are not an SMF.</exception>
    public static MidiFile Parse(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        MidiFile file = new MidiFile();
        int position = 0;
        if (bytes.Length < 14 || bytes[0] != 'M' || bytes[1] != 'T' || bytes[2] != 'h' || bytes[3] != 'd')
        {
            throw new InvalidDataException("not an SMF: no MThd header");
        }

        int headerLength = ReadInt32(bytes, 4);
        file.Format = ReadInt16(bytes, 8);
        int trackCount = ReadInt16(bytes, 10);
        file.Division = ReadInt16(bytes, 12);
        position = 8 + headerLength;

        for (int t = 0; t < trackCount; t++)
        {
            if (position + 8 > bytes.Length)
            {
                throw new InvalidDataException("track " + t + " header runs past the end");
            }

            if (bytes[position] != 'M' || bytes[position + 1] != 'T' || bytes[position + 2] != 'r' || bytes[position + 3] != 'k')
            {
                throw new InvalidDataException("track " + t + ": no MTrk header");
            }

            int length = ReadInt32(bytes, position + 4);
            int start = position + 8;
            int end = start + length;
            if (end > bytes.Length)
            {
                throw new InvalidDataException("track " + t + " runs past the end");
            }

            file.Tracks.Add(ParseTrack(bytes, start, end, file));
            position = end;
        }

        return file;
    }

    private static List<Event> ParseTrack(byte[] bytes, int position, int end, MidiFile file)
    {
        List<Event> events = new List<Event>();
        long tick = 0;
        int running = -1;
        while (position < end)
        {
            tick += ReadVariable(bytes, ref position, end);
            int status = bytes[position];
            if (status == 0xFF)
            {
                position++;
                int type = bytes[position++];
                int length = ReadVariable(bytes, ref position, end);
                byte[] data = Slice(bytes, position, length);
                position += length;
                if (type == 0x2F)
                {
                    continue; // end-of-track: dropped
                }

                if ((type == 0x01 || type == 0x02 || type == 0x03) && IsVersionStamp(data))
                {
                    file.VersionStamp = Encoding.Latin1.GetString(data).Trim();
                    data = Encoding.ASCII.GetBytes("<LILYPOND-VERSION-STAMP>");
                }

                events.Add(new Event { Tick = tick, Status = 0xFF, MetaType = type, Data = data });
                continue;
            }

            if (status == 0xF0 || status == 0xF7)
            {
                position++;
                int length = ReadVariable(bytes, ref position, end);
                byte[] data = Slice(bytes, position, length);
                position += length;
                events.Add(new Event { Tick = tick, Status = status, Data = data });
                running = -1;
                continue;
            }

            if ((status & 0x80) != 0)
            {
                running = status;
                position++;
            }
            else if (running < 0)
            {
                throw new InvalidDataException("running status with no status byte at offset " + position);
            }
            else
            {
                status = running;
            }

            int dataLength = ((status & 0xF0) == 0xC0 || (status & 0xF0) == 0xD0) ? 1 : 2;
            byte[] channelData = Slice(bytes, position, dataLength);
            position += dataLength;
            events.Add(new Event { Tick = tick, Status = status, Data = channelData });
        }

        return events;
    }

    private static bool IsVersionStamp(byte[] data)
    {
        string text = Encoding.Latin1.GetString(data);
        return text.Contains("LilyPond", StringComparison.Ordinal) || text.StartsWith("creator:", StringComparison.Ordinal);
    }

    private static byte[] Slice(byte[] bytes, int start, int length)
    {
        if (start + length > bytes.Length || length < 0)
        {
            throw new InvalidDataException("data runs past the end at offset " + start);
        }

        byte[] data = new byte[length];
        Array.Copy(bytes, start, data, 0, length);
        return data;
    }

    private static int ReadVariable(byte[] bytes, ref int position, int end)
    {
        int value = 0;
        for (int i = 0; i < 4; i++)
        {
            if (position >= end)
            {
                throw new InvalidDataException("variable-length quantity runs past the end");
            }

            int b = bytes[position++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("variable-length quantity longer than four bytes");
    }

    private static int ReadInt32(byte[] bytes, int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];

    private static int ReadInt16(byte[] bytes, int at) => (bytes[at] << 8) | bytes[at + 1];
}
