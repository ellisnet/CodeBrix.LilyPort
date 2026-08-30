// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Commands;
using SilverAssertions;
using Xunit;

namespace Lily.Shell.Core.Tests;

/// <summary>
/// What the v1 sketch's last two commands accept — gated at the command LINE, the way
/// <see cref="DocsCommandLineTests"/> and <see cref="TextConverterCommandLineTests"/>
/// gate theirs, because a path through either command reaches the engine. What the
/// engine then PRINTS was measured against the pinned oracle in the session that built
/// them; see tools/Lily.Shell/README.txt.
/// </summary>
public class ShellCommandLineTests
{
    [Fact]
    public void display_music_defaults_to_what_displayMusic_calls()
    {
        //Act
        var parsed = DisplayMusicCommandLine.Parse("{ c'4 }");

        //Assert
        //⚠ The command is named after the sketch and the DEFAULT after upstream's own
        //\displayMusic, which calls display-scheme-music. The control is --tree, which
        //is the procedure the command shares a name with.
        parsed.Error.Should().BeNull();
        parsed.Displayer.Should().Be("display-scheme-music");
        DisplayMusicCommandLine.Parse("--tree { c'4 }").Displayer.Should().Be("display-music");
    }

    [Fact]
    public void display_music_reads_its_three_displayers()
    {
        //Act & Assert
        DisplayMusicCommandLine.Parse("--scheme { c'4 }").Displayer
            .Should().Be("display-scheme-music");
        DisplayMusicCommandLine.Parse("--lily { c'4 }").Displayer
            .Should().Be("display-lily-music");
        DisplayMusicCommandLine.Parse("--tree { c'4 }").Displayer
            .Should().Be("display-music");
    }

    [Fact]
    public void display_music_keeps_the_expression_exactly_as_typed()
    {
        //Act
        //The quotes are LilyPond's. A tokenizer would eat them and hand the parser
        //`c'4^text', which is a different expression that parses.
        var parsed = DisplayMusicCommandLine.Parse("{ c'4^\"a  b\"  d'8 }");

        //Assert
        parsed.Music.Should().Be("{ c'4^\"a  b\"  d'8 }");
    }

    [Fact]
    public void display_music_stops_reading_options_at_the_music()
    {
        //Act
        //`--' inside music is a manual beam, and \override carries `--' shaped text of
        //its own; only the LEADING words are options.
        var parsed = DisplayMusicCommandLine.Parse("--lily { c'8-- d'8 }");

        //Assert
        parsed.Error.Should().BeNull();
        parsed.Displayer.Should().Be("display-lily-music");
        parsed.Music.Should().Be("{ c'8-- d'8 }");
    }

    [Fact]
    public void display_music_refuses_an_option_it_does_not_know()
    {
        //Act
        var parsed = DisplayMusicCommandLine.Parse("--pretty { c'4 }");

        //Assert
        //The control is the same line with an option that exists.
        parsed.Error.Should().Be("unknown option '--pretty'");
        DisplayMusicCommandLine.Parse("--lily { c'4 }").Error.Should().BeNull();
    }

    [Fact]
    public void display_music_asks_for_music_when_given_none()
    {
        //Act & Assert
        DisplayMusicCommandLine.Parse(string.Empty).Error.Should().Be("which music?");
        DisplayMusicCommandLine.Parse("   ").Error.Should().Be("which music?");
        DisplayMusicCommandLine.Parse("--lily").Error.Should().Be("which music?");
    }

    [Fact]
    public void set_with_nothing_lists()
    {
        //Act
        var parsed = SetCommandLine.Parse([]);

        //Assert
        parsed.Error.Should().BeNull();
        parsed.Action.Should().Be(SetCommandAction.List);
    }

    [Fact]
    public void set_spells_every_setting_the_way_a_d_option_does()
    {
        //Act
        var bare = SetCommandLine.Parse(["debug-voices"]);
        var negated = SetCommandLine.Parse(["no-point-and-click"]);
        var valued = SetCommandLine.Parse(["resolution=150"]);

        //Assert
        //Each is the text that FOLLOWS a -d, handed on unchanged: the engine's own
        //CommandLineOptions.Apply decides what it means, so the shell agrees with the
        //command line by not having an opinion.
        bare.Action.Should().Be(SetCommandAction.Apply);
        bare.Setting.Should().Be("debug-voices");
        negated.Setting.Should().Be("no-point-and-click");
        valued.Setting.Should().Be("resolution=150");
    }

    [Fact]
    public void set_takes_a_value_spelled_with_a_space()
    {
        //Act
        var spaced = SetCommandLine.Parse(["resolution", "150"]);

        //Assert
        //The control is the = spelling, which must produce the identical setting.
        spaced.Setting.Should().Be("resolution=150");
        spaced.Setting.Should().Be(SetCommandLine.Parse(["resolution=150"]).Setting);
    }

    [Fact]
    public void set_refuses_a_value_given_twice()
    {
        //Act
        var parsed = SetCommandLine.Parse(["resolution=150", "300"]);

        //Assert
        //The control is either spelling on its own, both of which parse.
        parsed.Error.Should().Be("'resolution=150' already has a value, so '300' is one too many");
        SetCommandLine.Parse(["resolution=150"]).Error.Should().BeNull();
        SetCommandLine.Parse(["resolution", "150"]).Error.Should().BeNull();
    }

    [Fact]
    public void set_refuses_more_than_one_option_at_a_time()
    {
        //Act
        var parsed = SetCommandLine.Parse(["debug-voices", "resolution", "150"]);

        //Assert
        parsed.Error.Should().Be("one option at a time, please");
    }

    [Fact]
    public void set_says_where_the_d_went()
    {
        //Act
        //⚠ `set -ddebug-voices' would otherwise set an option called `ddebug-voices',
        //which upstream would accept and warn about, and which nothing reads.
        var parsed = SetCommandLine.Parse(["-ddebug-voices"]);

        //Assert
        parsed.Error.Should().Be("there is no -d here — write 'set debug-voices'");
        SetCommandLine.Parse(["debug-voices"]).Error.Should().BeNull();
    }

    [Fact]
    public void set_refuses_an_option_it_does_not_know()
    {
        //Act
        var parsed = SetCommandLine.Parse(["--verbose"]);

        //Assert
        parsed.Error.Should().Be("unknown option '--verbose'");
    }

    [Fact]
    public void set_doc_takes_one_name_or_none()
    {
        //Act
        var all = SetCommandLine.Parse(["--doc"]);
        var one = SetCommandLine.Parse(["--doc", "resolution"]);
        var two = SetCommandLine.Parse(["--doc", "resolution", "backend"]);

        //Assert
        all.Action.Should().Be(SetCommandAction.Document);
        all.Name.Should().BeNull();
        one.Name.Should().Be("resolution");
        two.Error.Should().Be("--doc takes one option name at a time");
    }

    [Fact]
    public void set_clear_takes_nothing_else()
    {
        //Act
        var clear = SetCommandLine.Parse(["--clear"]);
        var confused = SetCommandLine.Parse(["--clear", "resolution"]);

        //Assert
        clear.Action.Should().Be(SetCommandAction.Clear);
        clear.Error.Should().BeNull();
        confused.Error.Should().Be("--clear takes nothing else");
    }
}
