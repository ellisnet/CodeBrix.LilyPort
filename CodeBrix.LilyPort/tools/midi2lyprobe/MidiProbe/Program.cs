// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort;
using CodeBrix.LilyPort.Importers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MidiProbe;

/// <summary>
/// Replays every recorded <c>midi2ly</c> case through <see cref="MidiImporter"/> and
/// grades the answer against what upstream's own script produced.
/// </summary>
/// <remarks>
/// A repo tool; it ships nothing. The corpus is the round trip described in
/// <c>gen-midi2ly-fixtures.py</c>: this repo's regression <c>.ly</c> files, engraved to
/// MIDI by the pinned LilyPond, read back as LilyPond source.
/// </remarks>
internal static class Program
{
    private static readonly TimeSpan PerCaseTimeout = TimeSpan.FromSeconds(60);

    private static int Main(string[] args)
    {
        string fixtures = args.Length > 0
            ? args[0]
            : Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tests",
                "CodeBrix.LilyPort.Tests", "fixtures", "midi2ly");
        fixtures = Path.GetFullPath(fixtures);

        if (!Directory.Exists(fixtures))
        {
            Console.Error.WriteLine("no fixtures at " + fixtures);
            return 2;
        }

        string[] files = Directory.GetFiles(fixtures, "*.midi2ly.json")
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("no fixtures in " + fixtures);
            return 2;
        }

        Console.WriteLine("MidiProbe: " + files.Length + " cases from " + fixtures);
        Console.WriteLine();

        int match = 0;
        int differs = 0;
        List<string> skipped = new List<string>();
        Stopwatch clock = Stopwatch.StartNew();

        foreach (string file in files)
        {
            string name = Path.GetFileName(file).Replace(".midi2ly.json", string.Empty);
            switch (Grade(file, name, skipped))
            {
                case Verdict.Match:
                    match++;
                    break;
                case Verdict.Differs:
                    differs++;
                    break;
            }
        }

        clock.Stop();
        Console.WriteLine();
        Console.WriteLine(
            match + " MATCH / " + differs + " DIFFERS / " + skipped.Count
            + " SKIPPED of " + files.Length + " in "
            + clock.Elapsed.TotalSeconds.ToString("0.0") + "s");
        foreach (string name in skipped)
        {
            Console.WriteLine("  SKIPPED " + name + " (did not finish in "
                + PerCaseTimeout.TotalSeconds + "s)");
        }

        return differs == 0 && skipped.Count == 0 ? 0 : 1;
    }

    private enum Verdict
    {
        Match,
        Differs,
        Skipped,
    }

    /// <summary>Reads the option settings a fixture records.</summary>
    /// <param name="root">The fixture.</param>
    /// <returns>The options.</returns>
    internal static MidiImportOptions ReadOptions(JsonElement root)
    {
        MidiImportOptions options = new MidiImportOptions
        {
            SourceName = root.GetProperty("source_name").GetString(),
        };

        JsonElement settings = root.GetProperty("options");
        if (settings.TryGetProperty("absolutePitches", out JsonElement absolute))
        {
            options.AbsolutePitches = absolute.GetBoolean();
        }

        if (settings.TryGetProperty("explicitDurations", out JsonElement explicitDur))
        {
            options.ExplicitDurations = explicitDur.GetBoolean();
        }

        if (settings.TryGetProperty("skip", out JsonElement skip))
        {
            options.Skip = skip.GetBoolean();
        }

        if (settings.TryGetProperty("textLyrics", out JsonElement textLyrics))
        {
            options.TextLyrics = textLyrics.GetBoolean();
        }

        if (settings.TryGetProperty("preview", out JsonElement preview))
        {
            options.Preview = preview.GetBoolean();
        }

        if (settings.TryGetProperty("key", out JsonElement key))
        {
            options.Key = key.GetString();
        }

        if (settings.TryGetProperty("durationQuant", out JsonElement durationQuant))
        {
            options.DurationQuant = durationQuant.GetInt32();
        }

        if (settings.TryGetProperty("startQuant", out JsonElement startQuant))
        {
            options.StartQuant = startQuant.GetInt32();
        }

        if (settings.TryGetProperty("allowTuplet", out JsonElement allowTuplet))
        {
            foreach (JsonElement entry in allowTuplet.EnumerateArray())
            {
                options.AllowTuplet.Add(entry.GetString());
            }
        }

        return options;
    }

    /// <summary>Fills in the one line the port and upstream deliberately disagree on.</summary>
    /// <param name="output">The recorded output.</param>
    /// <returns>What the port should produce.</returns>
    internal static string Expected(JsonElement output)
        => output.ValueKind == JsonValueKind.Null
            ? null
            : output.GetString();

    private static Verdict Grade(string file, string name, List<string> skipped)
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement root = fixture.RootElement;

        MidiImportOptions options = ReadOptions(root);
        byte[] data = Convert.FromBase64String(root.GetProperty("midi_base64").GetString());

        Task<ImportResult> run = Task.Run(() => MidiImporter.Import(data, options));
        if (!run.Wait(PerCaseTimeout))
        {
            skipped.Add(name);
            return Verdict.Skipped;
        }

        ImportResult result = run.Result;
        string expected = Expected(root.GetProperty("output"));
        string[] expectedMessages = root.GetProperty("messages")
            .EnumerateArray().Select(m => m.GetString()).ToArray();

        List<string> complaints = new List<string>();
        if (expected != result.Text)
        {
            complaints.Add(FirstDifference(expected, result.Text));
        }

        if (!expectedMessages.SequenceEqual(result.Messages))
        {
            complaints.Add(
                "messages: expected [" + string.Join(" | ", expectedMessages)
                + "] got [" + string.Join(" | ", result.Messages) + "]");
        }

        if (complaints.Count == 0)
        {
            return Verdict.Match;
        }

        Console.WriteLine("DIFFERS " + name);
        foreach (string complaint in complaints)
        {
            Console.WriteLine("        " + complaint);
        }

        return Verdict.Differs;
    }

    private static string FirstDifference(string expected, string actual)
    {
        if (expected == null)
        {
            return "text: expected NO output, got " + actual.Length + " bytes";
        }

        if (actual == null)
        {
            return "text: expected " + expected.Length + " bytes, got NO output";
        }

        string[] want = expected.Split('\n');
        string[] got = actual.Split('\n');
        for (int i = 0; i < Math.Max(want.Length, got.Length); i++)
        {
            string a = i < want.Length ? want[i] : "<no line>";
            string b = i < got.Length ? got[i] : "<no line>";
            if (a != b)
            {
                return "text line " + (i + 1) + ":\n          want |" + a
                    + "|\n          got  |" + b + "|";
            }
        }

        return "text: same lines, different length ("
            + expected.Length + " vs " + actual.Length + ")";
    }
}
