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
/// Grades the port's MIDI against Mutopia's in <c>compare-midi.py</c>'s vocabulary, then says
/// HOW the streams differ — because a 2.12-era reference will differ from a 2.27 engine almost
/// everywhere, and "EVENTS-DIFFER" alone would not tell a reader whether the notes are wrong
/// or only the velocities.
/// </summary>
public sealed class MidiComparison
{
    /// <summary>Gets or sets the verdict: MATCH, EVENTS-DIFFER, TRACKS-DIFFER, MISSING, NOREF, UNPARSEABLE, REF-UNPARSEABLE.</summary>
    public string Verdict { get; set; }

    /// <summary>Gets or sets the port's track count.</summary>
    public int PortTracks { get; set; } = -1;

    /// <summary>Gets or sets the reference's track count.</summary>
    public int ReferenceTracks { get; set; } = -1;

    /// <summary>Gets or sets the port's division.</summary>
    public int PortDivision { get; set; }

    /// <summary>Gets or sets the reference's division.</summary>
    public int ReferenceDivision { get; set; }

    /// <summary>Gets or sets the port's last tick.</summary>
    public long PortLength { get; set; }

    /// <summary>Gets or sets the reference's last tick.</summary>
    public long ReferenceLength { get; set; }

    /// <summary>Gets or sets the port's note-on count (velocity &gt; 0).</summary>
    public int PortNotes { get; set; }

    /// <summary>Gets or sets the reference's note-on count.</summary>
    public int ReferenceNotes { get; set; }

    /// <summary>Gets or sets whether the multiset of (tick, pitch) note-ons is identical — the notes, ignoring velocity and channel.</summary>
    public string NotesVerdict { get; set; }

    /// <summary>Gets or sets whether the multiset of pitches (ignoring time) is identical.</summary>
    public string PitchesVerdict { get; set; }

    /// <summary>Gets or sets the port's tempo-event count.</summary>
    public int PortTempos { get; set; }

    /// <summary>Gets or sets the reference's tempo-event count.</summary>
    public int ReferenceTempos { get; set; }

    /// <summary>Gets or sets the port's program-change count.</summary>
    public int PortPrograms { get; set; }

    /// <summary>Gets or sets the reference's program-change count.</summary>
    public int ReferencePrograms { get; set; }

    /// <summary>Gets or sets the reference's version stamp, or null.</summary>
    public string ReferenceStamp { get; set; }

    /// <summary>Gets or sets the first difference, described.</summary>
    public string FirstDifference { get; set; }

    /// <summary>
    /// Gets or sets the channel-events verdict: CHANNEL-EQUAL when every track's stream of
    /// NON-META events (notes with their velocities, program changes, controllers) is identical
    /// in order and tick, CHANNEL-DIFFER otherwise. Meta events — track names, the version stamp,
    /// tempo, time and key signatures — are what changed most between LilyPond releases, and
    /// this verdict is the performance with those set aside.
    /// </summary>
    public string ChannelVerdict { get; set; }

    /// <summary>Gets or sets the first channel-event difference, described.</summary>
    public string ChannelFirstDifference { get; set; }

    /// <summary>Grades a pair.</summary>
    /// <param name="portMidi">The port's file, or null.</param>
    /// <param name="referenceMidi">Mutopia's file, or null.</param>
    /// <returns>The comparison.</returns>
    public static MidiComparison Grade(string portMidi, string referenceMidi)
    {
        MidiComparison comparison = new MidiComparison();
        if (referenceMidi == null || !File.Exists(referenceMidi))
        {
            comparison.Verdict = "NOREF";
            return comparison;
        }

        MidiFile reference;
        try
        {
            reference = MidiFile.Parse(referenceMidi);
        }
        catch (InvalidDataException exception)
        {
            comparison.Verdict = "REF-UNPARSEABLE";
            comparison.FirstDifference = exception.Message;
            return comparison;
        }

        Summarise(reference, out int referenceTracks, out long referenceLength, out int referenceNotes, out int referenceTempos, out int referencePrograms);
        comparison.ReferenceTracks = referenceTracks;
        comparison.ReferenceDivision = reference.Division;
        comparison.ReferenceLength = referenceLength;
        comparison.ReferenceNotes = referenceNotes;
        comparison.ReferenceTempos = referenceTempos;
        comparison.ReferencePrograms = referencePrograms;
        comparison.ReferenceStamp = reference.VersionStamp;

        if (portMidi == null || !File.Exists(portMidi))
        {
            comparison.Verdict = "MISSING";
            return comparison;
        }

        MidiFile port;
        try
        {
            port = MidiFile.Parse(portMidi);
        }
        catch (InvalidDataException exception)
        {
            comparison.Verdict = "UNPARSEABLE";
            comparison.FirstDifference = exception.Message;
            return comparison;
        }

        Summarise(port, out int portTracks, out long portLength, out int portNotes, out int portTempos, out int portPrograms);
        comparison.PortTracks = portTracks;
        comparison.PortDivision = port.Division;
        comparison.PortLength = portLength;
        comparison.PortNotes = portNotes;
        comparison.PortTempos = portTempos;
        comparison.PortPrograms = portPrograms;

        comparison.NotesVerdict = SameMultiset(NoteKeys(port, true), NoteKeys(reference, true)) ? "NOTES-EQUAL" : "NOTES-DIFFER";
        GradeChannelEvents(comparison, port, reference);
        comparison.PitchesVerdict = SameMultiset(NoteKeys(port, false), NoteKeys(reference, false)) ? "PITCHES-EQUAL" : "PITCHES-DIFFER";

        if (port.Tracks.Count != reference.Tracks.Count)
        {
            comparison.Verdict = "TRACKS-DIFFER";
            comparison.FirstDifference = "port has " + port.Tracks.Count + " track(s), reference " + reference.Tracks.Count;
            return comparison;
        }

        if (port.Division != reference.Division)
        {
            comparison.Verdict = "EVENTS-DIFFER";
            comparison.FirstDifference = "division " + port.Division + " vs " + reference.Division;
            return comparison;
        }

        for (int t = 0; t < port.Tracks.Count; t++)
        {
            List<MidiFile.Event> a = port.Tracks[t];
            List<MidiFile.Event> b = reference.Tracks[t];
            int count = Math.Min(a.Count, b.Count);
            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(a[i].Key(), b[i].Key(), StringComparison.Ordinal))
                {
                    comparison.Verdict = "EVENTS-DIFFER";
                    comparison.FirstDifference = "track " + t + " event " + i + ": port " + a[i].Describe() + " | ref " + b[i].Describe();
                    return comparison;
                }
            }

            if (a.Count != b.Count)
            {
                comparison.Verdict = "EVENTS-DIFFER";
                comparison.FirstDifference = "track " + t + ": port has " + a.Count + " event(s), reference " + b.Count
                    + (a.Count > b.Count ? "; first extra: " + a[count].Describe() : "; first missing: " + b[count].Describe());
                return comparison;
            }
        }

        comparison.Verdict = "MATCH";
        return comparison;
    }

    private static void GradeChannelEvents(MidiComparison comparison, MidiFile port, MidiFile reference)
    {
        if (port.Tracks.Count != reference.Tracks.Count)
        {
            // Different track counts: compare the whole performance as one multiset of
            // (tick, status, data), which is order-blind but still exact about every event.
            List<string> a = ChannelKeys(port);
            List<string> b = ChannelKeys(reference);
            comparison.ChannelVerdict = SameMultiset(a, b) ? "CHANNEL-EQUAL" : "CHANNEL-DIFFER";
            if (comparison.ChannelVerdict == "CHANNEL-DIFFER")
            {
                comparison.ChannelFirstDifference = "track counts differ (" + port.Tracks.Count + " vs " + reference.Tracks.Count
                    + "); channel events as a multiset: port " + a.Count + ", reference " + b.Count;
            }

            return;
        }

        for (int t = 0; t < port.Tracks.Count; t++)
        {
            List<MidiFile.Event> a = port.Tracks[t].FindAll(e => e.Status != 0xFF && e.Status != 0xF0 && e.Status != 0xF7);
            List<MidiFile.Event> b = reference.Tracks[t].FindAll(e => e.Status != 0xFF && e.Status != 0xF0 && e.Status != 0xF7);
            int count = Math.Min(a.Count, b.Count);
            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(a[i].Key(), b[i].Key(), StringComparison.Ordinal))
                {
                    comparison.ChannelVerdict = "CHANNEL-DIFFER";
                    comparison.ChannelFirstDifference = "track " + t + " channel event " + i + ": port " + a[i].Describe() + " | ref " + b[i].Describe();
                    return;
                }
            }

            if (a.Count != b.Count)
            {
                comparison.ChannelVerdict = "CHANNEL-DIFFER";
                comparison.ChannelFirstDifference = "track " + t + ": port has " + a.Count + " channel event(s), reference " + b.Count
                    + (a.Count > b.Count ? "; first extra: " + a[count].Describe() : "; first missing: " + b[count].Describe());
                return;
            }
        }

        comparison.ChannelVerdict = "CHANNEL-EQUAL";
    }

    private static List<string> ChannelKeys(MidiFile file)
    {
        List<string> keys = new List<string>();
        foreach (List<MidiFile.Event> track in file.Tracks)
        {
            foreach (MidiFile.Event item in track)
            {
                if (item.Status != 0xFF && item.Status != 0xF0 && item.Status != 0xF7)
                {
                    keys.Add(item.Key());
                }
            }
        }

        return keys;
    }

    private static void Summarise(MidiFile file, out int tracks, out long length, out int notes, out int tempos, out int programs)
    {
        tracks = file.Tracks.Count;
        length = 0;
        notes = 0;
        tempos = 0;
        programs = 0;
        foreach (List<MidiFile.Event> track in file.Tracks)
        {
            foreach (MidiFile.Event item in track)
            {
                length = Math.Max(length, item.Tick);
                if ((item.Status & 0xF0) == 0x90 && item.Data.Length > 1 && item.Data[1] > 0)
                {
                    notes++;
                }
                else if ((item.Status & 0xF0) == 0xC0)
                {
                    programs++;
                }
                else if (item.Status == 0xFF && item.MetaType == 0x51)
                {
                    tempos++;
                }
            }
        }
    }

    private static List<string> NoteKeys(MidiFile file, bool withTick)
    {
        List<string> keys = new List<string>();
        foreach (List<MidiFile.Event> track in file.Tracks)
        {
            foreach (MidiFile.Event item in track)
            {
                if ((item.Status & 0xF0) == 0x90 && item.Data.Length > 1 && item.Data[1] > 0)
                {
                    keys.Add(withTick ? item.Tick + ":" + item.Data[0] : item.Data[0].ToString());
                }
            }
        }

        return keys;
    }

    private static bool SameMultiset(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in a)
        {
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        foreach (string key in b)
        {
            if (!counts.TryGetValue(key, out int n) || n == 0)
            {
                return false;
            }

            counts[key] = n - 1;
        }

        return true;
    }
}
