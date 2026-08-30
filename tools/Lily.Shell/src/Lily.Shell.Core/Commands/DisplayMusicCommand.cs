// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.Commands;
using Lily.Shell.Services;
using System;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Shows what the parser made of a music expression — the shell's counterpart of
/// <c>\displayMusic</c>, <c>\displayLilyMusic</c> and the raw <c>display-music</c>
/// dump.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ THE EXPRESSION IS READ FROM THE RAW LINE, NOT FROM THE TOKENS. LilyPond source
/// uses the characters a shell tokenizer eats: <c>c'4^"text"</c> loses its quotes and
/// silently becomes different music. <see cref="ShellCommandContext.RawArguments"/>
/// exists for this.
/// </para>
/// <para>
/// The command is named after the sketch and the default is named after upstream's
/// user-visible command, which are two different procedures: <c>\displayMusic</c> calls
/// <c>display-scheme-music</c>, the pretty-printed <c>make-music</c> form that can be
/// read back. The procedure literally called <c>display-music</c> — the terse dump of
/// each expression's mutable properties — is <c>--tree</c> here.
/// </para>
/// </remarks>
public sealed class DisplayMusicCommand : IShellCommand
{
    private readonly LilyPortHost _host;

    /// <summary>Creates the command over the engine host.</summary>
    /// <param name="host">The engine host.</param>
    public DisplayMusicCommand(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Name => "display-music";

    /// <inheritdoc/>
    public string Summary => "Shows the internal representation of a music expression.";

    /// <inheritdoc/>
    public string Usage => "display-music [--scheme|--lily|--tree] <music>";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        DisplayMusicCommandLine parsed =
            DisplayMusicCommandLine.Parse(context.RawArguments);
        if (parsed.Error != null)
        {
            context.IO.WriteLine("display-music: " + parsed.Error);
            context.IO.WriteLine("Usage: " + Usage);
            context.IO.WriteLine("   eg. display-music { c'4 d'8 e'8 }");
            return;
        }

        MusicDisplayOutcome outcome = await _host
            .DisplayMusicAsync(parsed.Music, parsed.Displayer, context.CancellationToken)
            .ConfigureAwait(false);

        foreach (string diagnostic in outcome.Diagnostics)
        {
            context.IO.WriteLine("  " + diagnostic);
        }

        if (outcome.Error != null)
        {
            context.IO.WriteLine("display-music: " + outcome.Error);
        }
    }
}

/// <summary>What a <c>display-music</c> command line asked for.</summary>
internal sealed class DisplayMusicCommandLine
{
    /// <summary>The <c>(lily)</c> procedure <c>\displayMusic</c> calls.</summary>
    internal const string SchemeDisplayer = "display-scheme-music";

    /// <summary>The one <c>\displayLilyMusic</c> calls.</summary>
    internal const string LilyDisplayer = "display-lily-music";

    /// <summary>The terse property dump the sketch named this command after.</summary>
    internal const string TreeDisplayer = "display-music";

    private DisplayMusicCommandLine()
    {
    }

    /// <summary>The message to print instead of running, or null when the line parsed.</summary>
    public string Error { get; private set; }

    /// <summary>The music expression, verbatim.</summary>
    public string Music { get; private set; }

    /// <summary>The displayer to call it with.</summary>
    public string Displayer { get; private set; } = SchemeDisplayer;

    /// <summary>Parses a <c>display-music</c> command line.</summary>
    /// <param name="rawArguments">The text after the command name, verbatim.</param>
    /// <returns>The parsed line, with <see cref="Error"/> set when it did not parse.</returns>
    /// <remarks>
    /// Only the LEADING words are read as options, and only while they begin with
    /// <c>--</c>: everything from the first word that does not is the music, including
    /// any later word that happens to look like an option. LilyPond source is full of
    /// things a flag parser would claim — <c>\repeat</c>, <c>-\markup</c>, <c>--</c>
    /// itself is a manual beam — so the music is taken as one run of text rather than
    /// scanned.
    /// </remarks>
    public static DisplayMusicCommandLine Parse(string rawArguments)
    {
        DisplayMusicCommandLine parsed = new DisplayMusicCommandLine();
        string rest = (rawArguments ?? string.Empty).TrimStart();

        while (rest.StartsWith("--", StringComparison.Ordinal))
        {
            int end = rest.Length;
            for (int i = 0; i < rest.Length; i++)
            {
                if (char.IsWhiteSpace(rest[i])) { end = i; break; }
            }

            string option = rest.Substring(0, end);
            switch (option)
            {
                case "--scheme":
                    parsed.Displayer = SchemeDisplayer;
                    break;

                case "--lily":
                    parsed.Displayer = LilyDisplayer;
                    break;

                case "--tree":
                    parsed.Displayer = TreeDisplayer;
                    break;

                default:
                    parsed.Error = "unknown option '" + option + "'";
                    return parsed;
            }

            rest = rest.Substring(end).TrimStart();
        }

        if (rest.Length == 0)
        {
            parsed.Error = "which music?";
            return parsed;
        }

        parsed.Music = rest;
        return parsed;
    }
}
