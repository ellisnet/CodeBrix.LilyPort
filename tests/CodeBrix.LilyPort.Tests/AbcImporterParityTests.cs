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
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// <see cref="AbcImporter"/> against LilyPond's own <c>abc2ly</c>: every fixture under
/// <c>fixtures/abc</c> pairs a real ABC file with the LilyPond source and the messages
/// the pinned 2.27.2 script produced from it.
/// <para>
/// Two corpora. Upstream's own eight regression files are replayed byte for byte, and
/// ten probes written for this port cover what those eight never reach — voice
/// overlays, numbered voices, broken rhythms, decorations, the header fields, guitar
/// chords, an unknown mode, the repeat-bar warnings, a broken rhythm inside a chord,
/// and the inline-field and comment escapes. Each is recorded twice, with and without
/// <c>--beams</c>.
/// </para>
/// <para>
/// Nothing here is recorded from the port's own output; regenerate with
/// <c>tools/abcprobe/gen-abc-fixtures.py</c>.
/// </para>
/// </summary>
public class AbcImporterParityTests
{
    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "abc");

    /// <summary>Every recorded case, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> CaseNames()
        => Directory.GetFiles(FixturesDirectory(), "*.abc.json")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".abc.json", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    private static JsonDocument Load(string name)
        => JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixturesDirectory(), name + ".abc.json")));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void abc_converts_the_way_abc2ly_converts_it(string name)
    {
        //Arrange
        using JsonDocument fixture = Load(name);
        JsonElement root = fixture.RootElement;
        AbcImportOptions options = new AbcImportOptions
        {
            Beams = root.GetProperty("options").GetProperty("beams").GetBoolean(),
            SourceName = root.GetProperty("source_name").GetString(),
        };

        //Act
        ImportResult result = AbcImporter.Import(
            root.GetProperty("input").GetString(), options);

        //Assert
        //Where the port deliberately differs, the fixture carries a frozen `port_output'
        //beside the oracle's own `output' and the comparison uses it -- strict byte
        //equality either way. The five reasons are in tools/abcprobe/DIVERGENCES.txt.
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
    public void the_frozen_version_line_is_upstreams_own_and_not_the_ported_release()
    {
        //Arrange
        //D63 (2026-08-26): the converter writes the release upstream FROZE -- the last
        //one whose output syntax was verified -- and NOT the release being ported.
        //They are two constants meaning two different things, and this is the fence
        //that stops them being conflated again: it fails both if the frozen number
        //drifts and if someone wires it to CompatibleWithVersion.
        List<string> frozen = new List<string>();

        //Act
        foreach (object[] row in CaseNames())
        {
            using JsonDocument fixture = Load((string)row[0]);
            JsonElement line = fixture.RootElement.GetProperty("frozen_version_line");
            if (line.ValueKind != JsonValueKind.Null)
            {
                frozen.Add(line.GetString());
            }
        }

        //Assert
        frozen.Should().NotBeEmpty();
        frozen.Distinct().Should().HaveCount(1);
        frozen[0].Should().Be("\\version \"2.24.0\"");
        frozen[0].Should().NotBe(
            "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"");
    }

    [Fact]
    public void every_divergence_from_upstream_is_declared()
    {
        //Arrange
        //The port repairs five defects in upstream's abc2ly, each written up in
        //tools/abcprobe/DIVERGENCES.txt with the measurement that proved it. A case may
        //carry a frozen port baseline ONLY for one of those five, and only if it says
        //which -- so a fix that quietly changed a case nobody reasoned about cannot be
        //baselined away.
        string[] known =
        [
            "abc-backslash-k-crash",
            "abc-header-append-drops-new-field",
            "abc-lyric-underscore-doubled",
            "abc-numbered-voice-name-mismatch",
            "abc-open-repeat-unclosed",
        ];
        List<string> diverging = new List<string>();
        List<string> reasonsSeen = new List<string>();

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

            if (!hasReasons)
            {
                continue;
            }

            diverging.Add(name);
            foreach (JsonElement reason in reasons.EnumerateArray())
            {
                reasonsSeen.Add(reason.GetString());
                known.Should().Contain(reason.GetString());
            }
        }

        //Assert
        //Fourteen: five defects across four probe pairs and three corpus pairs. The
        //control is the other thirty cases, which are byte-identical to upstream.
        diverging.Should().HaveCount(14);
        reasonsSeen.Distinct().Should().HaveCount(known.Length);
    }

    [Fact]
    public void both_corpora_are_present()
    {
        //Arrange
        List<string> sources = new List<string>();

        //Act
        foreach (object[] row in CaseNames())
        {
            using JsonDocument fixture = Load((string)row[0]);
            sources.Add(fixture.RootElement.GetProperty("source").GetString());
        }

        //Assert
        //Upstream's eight files and ten probes, each recorded with and without --beams.
        sources.Count(s => s == "upstream").Should().Be(16);
        sources.Count(s => s == "probe").Should().Be(28);
    }
}
