// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Importers;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// <see cref="MusicXmlImporter"/> against LilyPond's own <c>musicxml2ly</c>: every case
/// under <c>fixtures/musicxml/cases</c> pairs one of the vendored inputs with the
/// LilyPond source and the messages the pinned 2.27.2 script produced from it.
/// <para>
/// One corpus, replayed twice over. LilyPond's copy of the unofficial MusicXML Test
/// Suite holds 166 convertible files, every one recorded at the defaults; 40 further
/// cases record a representative file under one option each, because the suite exercises
/// the INPUT surface only — every file of it converts at the defaults — and the variants
/// are what reach the other twenty-two options.
/// </para>
/// <para>
/// Nothing here is recorded from the port's own output; regenerate with
/// <c>tools/musicxml2lyprobe/gen-musicxml-fixtures.py</c>.
/// </para>
/// </summary>
public class MusicXmlImporterParityTests
{
    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "musicxml");

    private static string CasesDirectory() => Path.Combine(FixturesDirectory(), "cases");

    private static string InputsDirectory() => Path.Combine(FixturesDirectory(), "inputs");

    /// <summary>Every recorded case, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> CaseNames()
        => Directory.GetFiles(CasesDirectory(), "*.mxml.json")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".mxml.json", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    private static JsonDocument Load(string name)
        => JsonDocument.Parse(
            File.ReadAllText(Path.Combine(CasesDirectory(), name + ".mxml.json")));

    /// <summary>Builds the options one case's recorded argument list asks for.</summary>
    /// <param name="arguments">The arguments, exactly as the oracle was given them.</param>
    /// <param name="sourceName">What the input is called.</param>
    /// <returns>The options.</returns>
    /// <remarks>
    /// ⚠ Deliberately a SECOND reading of the recorded arguments, beside the probe
    /// tool's. The two agreeing is what says the suite and the tool grade the same thing;
    /// sharing one reader would make an error in it invisible to both.
    /// </remarks>
    private static MusicXmlImportOptions BuildOptions(
        IReadOnlyList<string> arguments, string sourceName)
    {
        MusicXmlImportOptions options = new MusicXmlImportOptions
        {
            SourceName = sourceName,
        };

        for (int i = 0; i < arguments.Count; i++)
        {
            string Value()
            {
                i++;
                return arguments[i];
            }

            switch (arguments[i])
            {
                case "-a": options.PitchMode = MusicXmlPitchMode.Absolute; break;
                case "-r": options.PitchMode = MusicXmlPitchMode.Relative; break;
                case "-l": options.Language = Value(); break;
                case "--oe": options.OttavasEndEarly = Value(); break;
                case "--nd": options.NoArticulationDirections = true; break;
                case "--nrp": options.NoRestPositions = true; break;
                case "--nsb": options.NoSystemBreaks = true; break;
                case "--npb": options.NoPageBreaks = true; break;
                case "--npm": options.NoPageMargins = true; break;
                case "--npl": options.NoPageLayout = true; break;
                case "--nsd": options.NoStemDirections = true; break;
                case "--afs": options.AbsoluteFontSizes = true; break;
                case "--nb": options.NoBeaming = true; break;
                case "-m": options.Midi = true; break;
                case "--fb": options.Fretboards = true; break;
                case "--book": options.Book = true; break;
                case "--nt": options.NoTagline = true; break;
                case "--transpose": options.Transpose = Value(); break;
                case "--tc": options.TabClef = Value(); break;
                case "--sn": options.StringNumbers = Value(); break;
                case "--ds":
                    options.DynamicsScale = double.Parse(
                        Value(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    break;
                case "--cp":
                    options.CreditPage = int.Parse(
                        Value(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case "--sd":
                    options.ShiftDurations = int.Parse(
                        Value(), NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new InvalidOperationException(
                        "the fixture uses an option this suite does not read: "
                        + arguments[i]);
            }
        }

        return options;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void musicxml_converts_the_way_musicxml2ly_converts_it(string name)
    {
        //Arrange
        using JsonDocument fixture = Load(name);
        JsonElement root = fixture.RootElement;
        string inputFile = root.GetProperty("input_file").GetString();
        MusicXmlImportOptions options = BuildOptions(
            root.GetProperty("arguments").EnumerateArray().Select(a => a.GetString())
                .ToList(),
            root.GetProperty("source_name").GetString());
        string path = Path.Combine(InputsDirectory(), inputFile);

        //Act
        ImportResult result = inputFile.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase)
            ? MusicXmlImporter.ImportCompressed(File.ReadAllBytes(path), options)
            : MusicXmlImporter.Import(File.ReadAllText(path, Encoding.UTF8), options);

        //Assert
        //Where the port deliberately differs, the fixture carries a frozen `port_output'
        //beside the oracle's own `output' and the comparison uses it -- strict byte
        //equality either way. Nothing is frozen today; see
        //tools/musicxml2lyprobe/DIVERGENCES.txt.
        JsonElement output = root.TryGetProperty("port_output", out JsonElement port)
            ? port
            : root.GetProperty("output");
        if (output.ValueKind == JsonValueKind.Null)
        {
            result.Text.Should().BeNull();
        }
        else
        {
            result.Text.Should().Be(output.GetString());
        }

        string[] expectedMessages =
            (root.TryGetProperty("port_messages", out JsonElement portMessages)
                ? portMessages
                : root.GetProperty("messages"))
            .EnumerateArray().Select(m => m.GetString()).ToArray();
        string.Join("\n---\n", result.Messages).Should()
            .Be(string.Join("\n---\n", expectedMessages));
    }

    [Fact]
    public void the_version_line_is_the_ported_release_and_not_a_frozen_one()
    {
        //Arrange
        //D63 (2026-08-26): musicxml2ly is the OTHER case from abc2ly and midi2ly. It
        //calls dump_version(lilypond_version), and that name is substituted at install
        //time, so the line names the release being ported. Rule 16 makes
        //CompatibleWithVersion the one place that release is named in C#, and this is the
        //fence that says the two agree -- and that the constant is what the output reads.
        List<string> lines = new List<string>();

        //Act
        foreach (object[] row in CaseNames())
        {
            using JsonDocument fixture = Load((string)row[0]);
            JsonElement line = fixture.RootElement.GetProperty("version_line");
            if (line.ValueKind != JsonValueKind.Null)
            {
                lines.Add(line.GetString());
            }
        }

        //Assert
        lines.Should().NotBeEmpty();
        lines.Distinct().Should().HaveCount(1);
        lines[0].Should().Be("\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"");
    }

    [Fact]
    public void every_divergence_from_upstream_is_declared()
    {
        //Arrange
        //The port reproduces upstream exactly, including the one defect its own corpus
        //MEASURES: `71c-ChordsFrets.xml --fb' crashes musicxml2ly and writes no file, and
        //the port answers no text for it too. Nothing is repaired yet, so no case may
        //carry a frozen port baseline -- and a case that grows one without a stated
        //reason fails here rather than being baselined away.
        List<string> diverging = new List<string>();

        //Act
        foreach (object[] row in CaseNames())
        {
            string name = (string)row[0];
            using JsonDocument fixture = Load(name);
            JsonElement root = fixture.RootElement;
            bool hasBaseline = root.TryGetProperty("port_output", out JsonElement _);
            bool hasReasons = root.TryGetProperty("divergences", out JsonElement reasons)
                && reasons.GetArrayLength() > 0;

            hasBaseline.Should().Be(
                hasReasons,
                "a frozen port baseline and a stated reason go together (" + name + ")");

            if (hasReasons)
            {
                diverging.Add(name);
            }
        }

        //Assert
        diverging.Should().BeEmpty();
    }

    [Fact]
    public void the_corpus_is_the_whole_of_what_the_oracle_recorded()
    {
        //Arrange
        List<string> sources = new List<string>();
        List<string> inputs = new List<string>();
        int crashes = 0;

        //Act
        foreach (object[] row in CaseNames())
        {
            using JsonDocument fixture = Load((string)row[0]);
            JsonElement root = fixture.RootElement;
            sources.Add(root.GetProperty("source").GetString());
            inputs.Add(root.GetProperty("input_file").GetString());
            if (root.GetProperty("oracle_crash").ValueKind != JsonValueKind.Null)
            {
                crashes++;
            }
        }

        //Assert
        //166 convertible files at the defaults, 40 option variants over a representative
        //subset of them. The directory upstream ships holds 215 ENTRIES -- 31 .itexi, 15
        //.lybook, a GNUmakefile, a LICENSE and a .png besides -- and 166 is what the
        //converter converts.
        sources.Count(s => s == "upstream").Should().Be(166);
        sources.Count(s => s == "variant").Should().Be(40);
        inputs.Distinct().Should().HaveCount(166);

        //One case is upstream crashing on its own memoised string tunings, recorded as
        //such; the port reproduces the crash and writes nothing either.
        crashes.Should().Be(1);
    }

    [Fact]
    public void every_vendored_input_is_replayed()
    {
        //Arrange
        //A fixture naming an input that is not there, or an input nothing replays, is a
        //corpus that has quietly drifted from what the generator recorded.
        HashSet<string> named = new HashSet<string>(StringComparer.Ordinal);

        //Act
        foreach (object[] row in CaseNames())
        {
            using JsonDocument fixture = Load((string)row[0]);
            named.Add(fixture.RootElement.GetProperty("input_file").GetString());
        }

        string[] present = Directory.GetFiles(InputsDirectory())
            .Select(Path.GetFileName)
            .Where(n => n.EndsWith(".xml", StringComparison.Ordinal)
                        || n.EndsWith(".mxl", StringComparison.Ordinal))
            .ToArray();

        //Assert
        named.Should().HaveCount(present.Length);
        foreach (string name in named)
        {
            File.Exists(Path.Combine(InputsDirectory(), name)).Should().BeTrue(name);
        }
    }
}
