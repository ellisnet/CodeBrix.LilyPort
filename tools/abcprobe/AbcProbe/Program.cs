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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AbcProbe;

/// <summary>
/// Replays every recorded <c>abc2ly</c> case through <see cref="AbcImporter"/> and
/// grades the answer against what upstream's own script produced.
/// </summary>
/// <remarks>
/// A repo tool; it ships nothing. The fixtures it reads are written by
/// <c>gen-abc-fixtures.py</c> from the pinned 2.27.2 oracle, never from this port.
/// Where the parity suite asserts, this PRINTS — the first differing line of every
/// case that does not match — which is what a session porting or repairing the
/// converter actually needs to read.
/// </remarks>
internal static class Program
{
    //A rule of the probes (board trap 23): nothing is allowed to simply not return,
    //and anything skipped is named rather than dropped.
    private static readonly TimeSpan PerCaseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// THE DECLARED DIVERGENCES: which cases are allowed to differ from upstream, and
    /// on account of which defect.
    /// </summary>
    /// <remarks>
    /// ⚠ THIS TABLE IS THE PERMISSION. <c>--accept</c> refuses to freeze a port output
    /// for any case not named here, so a fix that quietly changed a case nobody
    /// reasoned about cannot be baselined; it shows up as a DIFFERS and stays one. Each
    /// id is written up in DIVERGENCES.txt with the upstream site, the measurement that
    /// proved the defect, and what the port does instead.
    /// </remarks>
    internal static readonly Dictionary<string, string[]> DeclaredDivergences
        = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["header-fields"] = ["abc-header-append-drops-new-field"],
            ["header-fields-beams"] = ["abc-header-append-drops-new-field"],
            ["defect-history"] = ["abc-header-append-drops-new-field"],
            ["defect-history-beams"] = ["abc-header-append-drops-new-field"],
            ["multi-voice-stave"] = ["abc-numbered-voice-name-mismatch"],
            ["multi-voice-stave-beams"] = ["abc-numbered-voice-name-mismatch"],
            ["voice-overlay"] = ["abc-numbered-voice-name-mismatch"],
            ["voice-overlay-beams"] = ["abc-numbered-voice-name-mismatch"],
            ["defect-lyric-underscore"] = ["abc-lyric-underscore-doubled"],
            ["defect-lyric-underscore-beams"] = ["abc-lyric-underscore-doubled"],
            ["defect-open-repeat"] = ["abc-open-repeat-unclosed"],
            ["defect-open-repeat-beams"] = ["abc-open-repeat-unclosed"],
            ["defect-escape-k"] = ["abc-backslash-k-crash"],
            ["defect-escape-k-beams"] = ["abc-backslash-k-crash"],
        };

    private static int Main(string[] args)
    {
        bool accept = Array.IndexOf(args, "--accept") >= 0;
        args = args.Where(a => a != "--accept").ToArray();

        string fixtures = args.Length > 0
            ? args[0]
            : Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tests",
                "CodeBrix.LilyPort.Tests", "fixtures", "abc");
        fixtures = Path.GetFullPath(fixtures);

        if (!Directory.Exists(fixtures))
        {
            Console.Error.WriteLine("no fixtures at " + fixtures);
            return 2;
        }

        string[] files = Directory.GetFiles(fixtures, "*.abc.json")
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("no fixtures in " + fixtures);
            return 2;
        }

        Console.WriteLine("AbcProbe: " + files.Length + " cases from " + fixtures);
        Console.WriteLine();

        int match = 0;
        int accepted = 0;
        int differs = 0;
        List<string> skipped = new List<string>();
        Stopwatch clock = Stopwatch.StartNew();

        foreach (string file in files)
        {
            string name = Path.GetFileName(file).Replace(".abc.json", string.Empty);
            Verdict verdict = Grade(file, name, skipped, accept);
            if (verdict == Verdict.Match)
            {
                match++;
            }
            else if (verdict == Verdict.Accepted)
            {
                accepted++;
            }
            else if (verdict == Verdict.Differs)
            {
                differs++;
            }
        }

        clock.Stop();
        Console.WriteLine();
        Console.WriteLine(
            match + " MATCH / " + accepted + " ACCEPTED / " + differs + " DIFFERS / "
            + skipped.Count + " SKIPPED of " + files.Length + " in "
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
        Accepted,
        Differs,
        Skipped,
    }

    private static Verdict Grade(
        string file, string name, List<string> skipped, bool accept)
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement root = fixture.RootElement;

        AbcImportOptions options = new AbcImportOptions
        {
            Beams = root.GetProperty("options").GetProperty("beams").GetBoolean(),
            SourceName = root.GetProperty("source_name").GetString(),
        };
        string input = root.GetProperty("input").GetString();

        ImportResult result = null;
        Task<ImportResult> run = Task.Run(() => AbcImporter.Import(input, options));
        if (!run.Wait(PerCaseTimeout))
        {
            skipped.Add(name);
            return Verdict.Skipped;
        }

        result = run.Result;

        JsonElement output = root.GetProperty("output");
        bool hasPortBaseline = root.TryGetProperty("port_output", out JsonElement portOutput);
        JsonElement authority = hasPortBaseline ? portOutput : output;
        string expected = authority.ValueKind == JsonValueKind.Null
            ? null
            : authority.GetString();

        string[] expectedMessages =
            (root.TryGetProperty("port_messages", out JsonElement portMessages)
                ? portMessages
                : root.GetProperty("messages"))
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

        if (accept && DeclaredDivergences.TryGetValue(name, out string[] reasons))
        {
            Freeze(file, result, reasons);
            Console.WriteLine(
                "ACCEPTED " + name + "  [" + string.Join(", ", reasons) + "]");
            return Verdict.Accepted;
        }

        Console.WriteLine("DIFFERS " + name);
        if (accept)
        {
            Console.WriteLine(
                "        NOT ACCEPTED: no declared divergence for this case. Add one to "
                + "DeclaredDivergences and write it up in DIVERGENCES.txt first.");
        }

        foreach (string complaint in complaints)
        {
            Console.WriteLine("        " + complaint);
        }

        return Verdict.Differs;
    }

    /// <summary>
    /// Freezes what the port produces for a case that deliberately differs, beside the
    /// oracle's own output rather than in place of it.
    /// </summary>
    /// <param name="file">The fixture.</param>
    /// <param name="result">What the port produced.</param>
    /// <param name="reasons">Which declared divergences apply.</param>
    /// <remarks>
    /// ⚠ THIS IS THE ONE PLACE ANYTHING IS EVER RECORDED FROM THE PORT, and standing
    /// rule 33 still holds around it: <c>output</c> keeps the ORACLE's own text, always,
    /// so the fixture carries the before and the after and the diff stays readable. The
    /// frozen <c>port_output</c> is a reviewed claim in the same sense as the ratchet's
    /// pass-manifest decisions — it is what makes an UNintended change fail.
    /// </remarks>
    private static void Freeze(string file, ImportResult result, string[] reasons)
    {
        using JsonDocument existing = JsonDocument.Parse(File.ReadAllText(file));
        Dictionary<string, JsonNode> fields = new Dictionary<string, JsonNode>(
            StringComparer.Ordinal);
        foreach (JsonProperty property in existing.RootElement.EnumerateObject())
        {
            if (property.Name != "port_output" && property.Name != "divergences")
            {
                fields[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        fields["port_output"] = result.Text == null ? null : JsonValue.Create(result.Text);
        fields["port_messages"] = new JsonArray(
            result.Messages.Select(m => (JsonNode)JsonValue.Create(m)).ToArray());
        fields["divergences"] = new JsonArray(
            reasons.Select(r => (JsonNode)JsonValue.Create(r)).ToArray());

        JsonObject ordered = new JsonObject();
        foreach (string key in fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            ordered[key] = fields[key];
        }

        File.WriteAllText(
            file,
            ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Names the first line the two texts do not agree on.</summary>
    /// <param name="expected">What upstream wrote.</param>
    /// <param name="actual">What the port wrote.</param>
    /// <returns>The complaint.</returns>
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
