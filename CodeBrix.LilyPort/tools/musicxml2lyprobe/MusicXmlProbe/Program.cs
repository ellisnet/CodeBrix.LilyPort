// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Importers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MusicXmlProbe;

/// <summary>
/// Replays every recorded <c>musicxml2ly</c> case through
/// <see cref="MusicXmlImporter"/> and grades the answer against what upstream's own
/// script produced.
/// </summary>
/// <remarks>
/// A repo tool; it ships nothing. The fixtures it reads are written by
/// <c>gen-musicxml-fixtures.py</c> from the pinned 2.27.2 oracle, never from this port.
/// Where the parity suite asserts, this PRINTS — the first differing line of every case
/// that does not match — which is what a session porting or repairing the converter
/// actually needs to read.
/// </remarks>
internal static class Program
{
    //A rule of the probes (board trap 23): nothing is allowed to simply not return,
    //and anything skipped is named rather than dropped.
    private static readonly TimeSpan PerCaseTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// THE DECLARED DIVERGENCES: which cases are allowed to differ from upstream, and on
    /// account of which defect.
    /// </summary>
    /// <remarks>
    /// ⚠ THIS TABLE IS THE PERMISSION. <c>--accept</c> refuses to freeze a port output
    /// for any case not named here, so a fix that quietly changed a case nobody reasoned
    /// about cannot be baselined; it shows up as a DIFFERS and stays one. Each id is
    /// written up in DIVERGENCES.txt with the upstream site, the measurement that proved
    /// the defect, and what the port does instead.
    /// <para>
    /// EMPTY, deliberately: the two divergence CANDIDATES this converter has are not
    /// ruled, and the order of work is parity first, then divergence. A fix goes on top
    /// of a green baseline, never into the port that establishes it.
    /// </para>
    /// </remarks>
    internal static readonly Dictionary<string, string[]> DeclaredDivergences
        = new Dictionary<string, string[]>(StringComparer.Ordinal);

    private static int Main(string[] args)
    {
        bool accept = Array.IndexOf(args, "--accept") >= 0;
        string only = null;
        List<string> rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--accept")
            {
                continue;
            }

            if (args[i] == "--only" && i + 1 < args.Length)
            {
                only = args[i + 1];
                i++;
                continue;
            }

            rest.Add(args[i]);
        }

        string fixtures = rest.Count > 0
            ? rest[0]
            : Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tests",
                "CodeBrix.LilyPort.Tests", "fixtures", "musicxml");
        fixtures = Path.GetFullPath(fixtures);

        string cases = Path.Combine(fixtures, "cases");
        string inputs = Path.Combine(fixtures, "inputs");
        if (!Directory.Exists(cases) || !Directory.Exists(inputs))
        {
            Console.Error.WriteLine("no fixtures at " + fixtures);
            return 2;
        }

        string[] files = Directory.GetFiles(cases, "*.mxml.json")
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (only != null)
        {
            files = files
                .Where(f => Path.GetFileName(f).Contains(only, StringComparison.Ordinal))
                .ToArray();
        }

        if (files.Length == 0)
        {
            Console.Error.WriteLine("no fixtures in " + cases);
            return 2;
        }

        Console.WriteLine("MusicXmlProbe: " + files.Length + " cases from " + cases);
        Console.WriteLine();

        int match = 0;
        int accepted = 0;
        int differs = 0;
        List<string> skipped = new List<string>();
        Stopwatch clock = Stopwatch.StartNew();

        foreach (string file in files)
        {
            string name = Path.GetFileName(file)
                .Replace(".mxml.json", string.Empty, StringComparison.Ordinal);
            Verdict verdict = Grade(file, inputs, name, skipped, accept);
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
            + clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");
        foreach (string name in skipped)
        {
            Console.WriteLine(
                "  SKIPPED " + name + " (did not finish in "
                + PerCaseTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + "s)");
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

    /// <summary>Builds the options one case's recorded argument list asks for.</summary>
    /// <param name="arguments">The arguments, exactly as the oracle was given them.</param>
    /// <param name="sourceName">What the input is called.</param>
    /// <returns>The options.</returns>
    /// <remarks>
    /// ⚠ Only the SHORT spellings the generator uses are read, because those are what the
    /// fixtures record; a spelling that turns up here and is not handled is an error
    /// rather than a silent default.
    /// </remarks>
    internal static MusicXmlImportOptions BuildOptions(
        IReadOnlyList<string> arguments, string sourceName)
    {
        MusicXmlImportOptions options = new MusicXmlImportOptions
        {
            SourceName = sourceName,
        };

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            string Value()
            {
                i++;
                return arguments[i];
            }

            switch (argument)
            {
                case "-a":
                case "--absolute":
                    options.PitchMode = MusicXmlPitchMode.Absolute;
                    break;
                case "-r":
                case "--relative":
                    options.PitchMode = MusicXmlPitchMode.Relative;
                    break;
                case "-l":
                case "--language":
                    options.Language = Value();
                    break;
                case "--oe":
                case "--ottavas-end-early":
                    options.OttavasEndEarly = Value();
                    break;
                case "--nd":
                case "--no-articulation-directions":
                    options.NoArticulationDirections = true;
                    break;
                case "--nrp":
                case "--no-rest-positions":
                    options.NoRestPositions = true;
                    break;
                case "--nsb":
                case "--no-system-breaks":
                    options.NoSystemBreaks = true;
                    break;
                case "--npb":
                case "--no-page-breaks":
                    options.NoPageBreaks = true;
                    break;
                case "--npm":
                case "--no-page-margins":
                    options.NoPageMargins = true;
                    break;
                case "--npl":
                case "--no-page-layout":
                    options.NoPageLayout = true;
                    break;
                case "--nsd":
                case "--no-stem-directions":
                    options.NoStemDirections = true;
                    break;
                case "--ds":
                case "--dynamics-scale":
                    options.DynamicsScale = double.Parse(
                        Value(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    break;
                case "--afs":
                case "--absolute-font-sizes":
                    options.AbsoluteFontSizes = true;
                    break;
                case "--nb":
                case "--no-beaming":
                    options.NoBeaming = true;
                    break;
                case "-m":
                case "--midi":
                    options.Midi = true;
                    break;
                case "--cp":
                case "--credit-page":
                    options.CreditPage = int.Parse(
                        Value(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case "--transpose":
                    options.Transpose = Value();
                    break;
                case "--sd":
                case "--shift-durations":
                    options.ShiftDurations = int.Parse(
                        Value(), NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture);
                    break;
                case "--tc":
                case "--tab-clef":
                    options.TabClef = Value();
                    break;
                case "--sn":
                case "--string-numbers":
                    options.StringNumbers = Value();
                    break;
                case "--fb":
                case "--fretboards":
                    options.Fretboards = true;
                    break;
                case "--book":
                    options.Book = true;
                    break;
                case "--nt":
                case "--no-tagline":
                    options.NoTagline = true;
                    break;
                default:
                    throw new InvalidOperationException(
                        "the fixture uses an option this probe does not read: " + argument);
            }
        }

        return options;
    }

    private static Verdict Grade(
        string file, string inputs, string name, List<string> skipped, bool accept)
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement root = fixture.RootElement;

        string inputFile = root.GetProperty("input_file").GetString();
        string sourceName = root.GetProperty("source_name").GetString();
        List<string> arguments = root.GetProperty("arguments")
            .EnumerateArray().Select(a => a.GetString()).ToList();

        MusicXmlImportOptions options = BuildOptions(arguments, sourceName);
        string path = Path.Combine(inputs, inputFile);
        bool compressed = inputFile.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase);

        Task<ImportResult> run = compressed
            ? Task.Run(
                () => MusicXmlImporter.ImportCompressed(File.ReadAllBytes(path), options))
            : Task.Run(
                () => MusicXmlImporter.Import(
                    File.ReadAllText(path, System.Text.Encoding.UTF8), options));
        bool finished;
        try
        {
            finished = run.Wait(PerCaseTimeout);
        }
        catch (AggregateException aggregate)
        {
            //⚠ An exception the importer did not turn into a diagnostic is a PORT DEFECT,
            //not a verdict about the case: upstream would have said something. Report it
            //where the difference would have gone and carry on, so one broken case does
            //not hide the state of the other two hundred.
            Console.WriteLine("DIFFERS " + name);
            Console.WriteLine(
                "        threw " + aggregate.InnerException.GetType().Name + ": "
                + aggregate.InnerException.Message);
            Console.WriteLine("        at " + FirstPortFrame(aggregate.InnerException));
            return Verdict.Differs;
        }

        if (!finished)
        {
            skipped.Add(name);
            return Verdict.Skipped;
        }

        ImportResult result = run.Result;

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
    /// ⚠ THIS IS THE ONE PLACE ANYTHING IS EVER RECORDED FROM THE PORT, and standing rule
    /// 33 still holds around it: <c>output</c> keeps the ORACLE's own text, always, so
    /// the fixture carries the before and the after and the diff stays readable.
    /// </remarks>
    private static void Freeze(string file, ImportResult result, string[] reasons)
    {
        using JsonDocument existing = JsonDocument.Parse(File.ReadAllText(file));
        Dictionary<string, JsonNode> fields = new Dictionary<string, JsonNode>(
            StringComparer.Ordinal);
        foreach (JsonProperty property in existing.RootElement.EnumerateObject())
        {
            if (property.Name != "port_output" && property.Name != "divergences"
                && property.Name != "port_messages")
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

    /// <summary>The first frame of a stack trace that is inside the port itself.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The frame, trimmed.</returns>
    private static string FirstPortFrame(Exception exception)
    {
        foreach (string line in (exception.StackTrace ?? string.Empty).Split('\n'))
        {
            if (line.Contains("CodeBrix.LilyPort.Importers", StringComparison.Ordinal))
            {
                return line.Trim();
            }
        }

        return "(no port frame)";
    }

    /// <summary>Names the first line the two texts do not agree on.</summary>
    /// <param name="expected">What upstream wrote.</param>
    /// <param name="actual">What the port wrote.</param>
    /// <returns>The complaint.</returns>
    private static string FirstDifference(string expected, string actual)
    {
        if (expected == null)
        {
            return "text: expected NO output, got "
                + actual.Length.ToString(CultureInfo.InvariantCulture) + " bytes";
        }

        if (actual == null)
        {
            return "text: expected "
                + expected.Length.ToString(CultureInfo.InvariantCulture)
                + " bytes, got NO output";
        }

        string[] want = expected.Split('\n');
        string[] got = actual.Split('\n');
        for (int i = 0; i < Math.Max(want.Length, got.Length); i++)
        {
            string a = i < want.Length ? want[i] : "<no line>";
            string b = i < got.Length ? got[i] : "<no line>";
            if (a != b)
            {
                return "text line " + (i + 1).ToString(CultureInfo.InvariantCulture)
                    + ":\n          want |" + a + "|\n          got  |" + b + "|";
            }
        }

        return "text: same lines, different length ("
            + expected.Length.ToString(CultureInfo.InvariantCulture) + " vs "
            + actual.Length.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
