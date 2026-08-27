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
/// <see cref="MidiImporter"/> against LilyPond's own <c>midi2ly</c>: every fixture
/// under <c>fixtures/midi2ly</c> pairs a MIDI file with the LilyPond source and the
/// messages the pinned 2.27.2 script produced from it.
/// <para>
/// The corpus is this repo's own ROUND TRIP — the ninety regression <c>.ly</c> files
/// engraved to MIDI by the pinned LilyPond and read back — which is fifteen files
/// wider than the corpus upstream ships for midi2ly, and is made of music this port
/// already engraves. Nine representative files are recorded again under each of the
/// nine options that changes the output.
/// </para>
/// <para>
/// Nothing here is recorded from the port's own output; regenerate with
/// <c>tools/midi2lyprobe/gen-midi2ly-fixtures.py</c>.
/// </para>
/// </summary>
public class MidiImporterParityTests
{
    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "midi2ly");

    /// <summary>Every recorded case, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> CaseNames()
        => Directory.GetFiles(FixturesDirectory(), "*.midi2ly.json")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".midi2ly.json", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    private static JsonDocument Load(string name)
        => JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixturesDirectory(), name + ".midi2ly.json")));

    private static MidiImportOptions ReadOptions(JsonElement root)
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

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void midi_converts_the_way_midi2ly_converts_it(string name)
    {
        //Arrange
        using JsonDocument fixture = Load(name);
        JsonElement root = fixture.RootElement;
        MidiImportOptions options = ReadOptions(root);
        byte[] data = Convert.FromBase64String(
            root.GetProperty("midi_base64").GetString());

        //Act
        ImportResult result = MidiImporter.Import(data, options);

        //Assert
        JsonElement output = root.GetProperty("output");
        if (output.ValueKind == JsonValueKind.Null)
        {
            result.Text.Should().BeNull();
        }
        else
        {
            result.Text.Should().Be(output.GetString());
        }

        string[] expectedMessages = root.GetProperty("messages")
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
        frozen[0].Should().Be("\\version \"2.14.0\"");
        frozen[0].Should().NotBe(
            "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"");
    }

    [Fact]
    public void the_whole_round_trip_corpus_is_recorded()
    {
        //Arrange
        //Ninety files come out of tools/regression-harness/reference-midi, and every
        //one of them is a case. A corpus that quietly shrank would otherwise read as a
        //green run.
        int defaults = 0;

        //Act
        foreach (object[] row in CaseNames())
        {
            using JsonDocument fixture = Load((string)row[0]);
            if (fixture.RootElement.GetProperty("options")
                .EnumerateObject().Count() == 0)
            {
                defaults++;
            }
        }

        //Assert
        defaults.Should().Be(90);
    }
}
