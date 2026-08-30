// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.Commands;
using Lily.Shell.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>What a <c>set</c> command line asked the shell to do.</summary>
internal enum SetCommandAction
{
    /// <summary>Show every option and its value.</summary>
    List,

    /// <summary>Show what an option is for — every option when no name was given.</summary>
    Document,

    /// <summary>Forget every setting this session made.</summary>
    Clear,

    /// <summary>Apply one setting.</summary>
    Apply,
}

/// <summary>
/// Reads and writes the engine's program options — the shell-session counterpart of
/// <c>lilypond</c>'s <c>-d</c>, as <c>include</c> is the counterpart of
/// <c>--include</c>.
/// </summary>
/// <remarks>
/// <para>
/// EVERY SPELLING THAT SETS SOMETHING IS <c>-d</c>'s OWN, and goes through <c>-d</c>'s
/// own code: <c>set NAME</c>, <c>set no-NAME</c> and <c>set NAME=VALUE</c> mean what
/// <c>-dNAME</c>, <c>-dno-NAME</c> and <c>-dNAME=VALUE</c> mean, down to how the value
/// text is turned into a value by the option's declared type. <c>set NAME VALUE</c> is
/// the same thing with the <c>=</c> spelled as a space, because this is a shell.
/// </para>
/// <para>
/// Reading is NOT one of those spellings, which is why it has a flag of its own:
/// <c>-dNAME</c> already means "set it to true", so a bare name could not also mean
/// "show it". <c>--doc</c> is where the option's documentation and its current value
/// come out, and a bare <c>set</c> lists every option, which is upstream's
/// <c>-dhelp</c> minus the prose.
/// </para>
/// </remarks>
public sealed class SetCommand : IShellCommand
{
    private readonly LilyPortHost _host;

    /// <summary>Creates the command over the engine host.</summary>
    /// <param name="host">The engine host.</param>
    public SetCommand(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Name => "set";

    /// <inheritdoc/>
    public string Summary => "Lists or sets the engine's program options (lilypond's -d).";

    /// <inheritdoc/>
    public string Usage =>
        "set [<name>[=<value>] | no-<name> | <name> <value> | --doc [<name>] | --clear]";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        SetCommandLine parsed = SetCommandLine.Parse(context.Arguments);
        if (parsed.Error != null)
        {
            context.IO.WriteLine("set: " + parsed.Error);
            context.IO.WriteLine("Usage: " + Usage);
            return;
        }

        switch (parsed.Action)
        {
            case SetCommandAction.Clear:
                int forgotten = await _host
                    .ClearOptionSettingsAsync(context.CancellationToken)
                    .ConfigureAwait(false);
                context.IO.WriteLine(forgotten == 1
                    ? "1 setting forgotten; the option table is back to the engine's own values."
                    : forgotten + " settings forgotten; the option table is back to the "
                        + "engine's own values.");
                return;

            case SetCommandAction.Apply:
                await ApplyAsync(context, parsed.Setting).ConfigureAwait(false);
                return;

            case SetCommandAction.Document:
                await DocumentAsync(context, parsed.Name).ConfigureAwait(false);
                return;

            default:
                await ListAsync(context).ConfigureAwait(false);
                return;
        }
    }

    private async Task ApplyAsync(ShellCommandContext context, string setting)
    {
        ProgramOptionChange change = await _host
            .ApplyOptionSettingAsync(setting, context.CancellationToken)
            .ConfigureAwait(false);

        //The warnings come first, for the same reason the converters print theirs first:
        //"no such program option" is the whole message, and upstream SETS IT ANYWAY, so
        //the line below it would otherwise read as a success.
        foreach (string warning in change.Warnings)
        {
            context.IO.WriteLine("  " + warning);
        }

        context.IO.WriteLine(change.Before == change.After
            ? change.Name + " = " + change.After + "  (unchanged)"
            : change.Name + " = " + change.After + "  (was " + change.Before + ")");
    }

    private async Task DocumentAsync(ShellCommandContext context, string name)
    {
        IReadOnlyList<ProgramOptionEntry> entries = await _host
            .ReadOptionsAsync(context.CancellationToken).ConfigureAwait(false);

        var written = 0;
        foreach (ProgramOptionEntry entry in entries)
        {
            if (name != null && entry.Name != name) { continue; }

            written++;
            context.IO.WriteLine(entry.Name + " = " + entry.Value);
            foreach (string line in DocumentationLines(entry.Documentation))
            {
                context.IO.WriteLine("    " + line);
            }
        }

        if (written == 0 && name != null)
        {
            //Upstream's own words for a name it does not know, and the same outcome:
            //nothing is shown, because there is nothing to show.
            context.IO.WriteLine("no such program option: " + name);
        }
    }

    private async Task ListAsync(ShellCommandContext context)
    {
        IReadOnlyList<ProgramOptionEntry> entries = await _host
            .ReadOptionsAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (ProgramOptionEntry entry in entries)
        {
            context.IO.WriteLine("  " + entry.Name + " = " + entry.Value);
        }

        context.IO.WriteLine(entries.Count + " options ('set --doc <name>' says what one is for).");

        //What this SESSION did, listed separately: these are the entries replayed into
        //every engrave, and they are the only part of the table above that survives a
        //run's own restore.
        IReadOnlyList<string> settings = _host.OptionSettings;
        if (settings.Count == 0) { return; }

        context.IO.WriteLine(string.Empty);
        context.IO.WriteLine("Set in this session, and applied to every engrave:");
        foreach (string setting in settings)
        {
            context.IO.WriteLine("  -d" + setting);
        }
    }

    /// <summary>Splits a declared documentation string into its own lines.</summary>
    /// <param name="documentation">The documentation string.</param>
    /// <returns>The lines, empty when there is nothing to say.</returns>
    /// <remarks>
    /// The strings are written with their line breaks in them — upstream wraps them by
    /// hand to keep <c>-dhelp</c> narrow — so they are printed as written rather than
    /// re-wrapped to a terminal whose width this command does not know.
    /// </remarks>
    private static IReadOnlyList<string> DocumentationLines(string documentation) =>
        string.IsNullOrWhiteSpace(documentation)
            ? []
            : documentation.Split('\n', StringSplitOptions.TrimEntries);
}

/// <summary>What a <c>set</c> command line asked for.</summary>
internal sealed class SetCommandLine
{
    private SetCommandLine()
    {
    }

    /// <summary>The message to print instead of running, or null when the line parsed.</summary>
    public string Error { get; private set; }

    /// <summary>What to do.</summary>
    public SetCommandAction Action { get; private set; } = SetCommandAction.List;

    /// <summary>The option to document, or null for all of them.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// The setting to apply, in <c>-d</c>'s own spelling and without its <c>-d</c>.
    /// </summary>
    public string Setting { get; private set; }

    /// <summary>Parses a <c>set</c> command line.</summary>
    /// <param name="arguments">The arguments after the command name.</param>
    /// <returns>The parsed line, with <see cref="Error"/> set when it did not parse.</returns>
    public static SetCommandLine Parse(IReadOnlyList<string> arguments)
    {
        SetCommandLine parsed = new SetCommandLine();
        if (arguments == null || arguments.Count == 0)
        {
            return parsed;
        }

        switch (arguments[0])
        {
            case "--doc":
                parsed.Action = SetCommandAction.Document;
                if (arguments.Count > 2)
                {
                    parsed.Error = "--doc takes one option name at a time";
                    return parsed;
                }

                parsed.Name = arguments.Count == 2 ? arguments[1] : null;
                return parsed;

            case "--clear":
                parsed.Action = SetCommandAction.Clear;
                if (arguments.Count > 1)
                {
                    parsed.Error = "--clear takes nothing else";
                }

                return parsed;
        }

        if (arguments[0].StartsWith("-", StringComparison.Ordinal))
        {
            //⚠ INCLUDING `-dfoo'. The -d belongs to a command line this shell does not
            //have; writing it here would set an option called `dfoo'.
            parsed.Error = arguments[0].StartsWith("-d", StringComparison.Ordinal)
                ? "there is no -d here — write 'set " + arguments[0].Substring(2) + "'"
                : "unknown option '" + arguments[0] + "'";
            return parsed;
        }

        if (arguments.Count > 2)
        {
            parsed.Error = "one option at a time, please";
            return parsed;
        }

        parsed.Action = SetCommandAction.Apply;

        //`set NAME VALUE' is `set NAME=VALUE'. A name that already carries an `=' and is
        //then given a second value is two spellings of the same thing at once, which is
        //likelier to be a typo than an intention.
        if (arguments.Count == 1)
        {
            parsed.Setting = arguments[0];
            return parsed;
        }

        if (arguments[0].Contains('=', StringComparison.Ordinal))
        {
            parsed.Error = "'" + arguments[0] + "' already has a value, so '"
                + arguments[1] + "' is one too many";
            return parsed;
        }

        parsed.Setting = arguments[0] + "=" + arguments[1];
        return parsed;
    }
}
