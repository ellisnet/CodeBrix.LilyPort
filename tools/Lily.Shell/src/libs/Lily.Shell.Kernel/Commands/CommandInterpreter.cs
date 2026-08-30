// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lily.Shell.Kernel.Commands;

/// <summary>
/// The session's default line interpreter: tokenizes the line, looks the
/// first token up in the <see cref="CommandRegistry"/>, and executes it.
/// </summary>
public sealed class CommandInterpreter : ILineInterpreter
{
    private readonly CommandRegistry _registry;

    /// <summary>Creates the interpreter over a registry, with the prompt it shows.</summary>
    public CommandInterpreter(CommandRegistry registry, string prompt)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Prompt = prompt ?? string.Empty;
    }

    /// <inheritdoc/>
    public string Prompt { get; }

    /// <inheritdoc/>
    public async Task HandleLineAsync(ShellSession session, string line,
        CancellationToken cancellationToken)
    {
        var tokens = CommandLineTokenizer.Tokenize(line);
        if (tokens.Count == 0) { return; }

        if (!_registry.TryGet(tokens[0], out var command))
        {
            session.Output.WriteLine($"Unknown command: {tokens[0]}  (try 'help')");
            return;
        }

        var arguments = tokens.GetRange(1, tokens.Count - 1);
        var context = new ShellCommandContext(
            session, arguments, RawArgumentsOf(line), cancellationToken);
        await command.ExecuteAsync(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the line with its leading whitespace and its command word removed, and
    /// nothing else changed — <see cref="ShellCommandContext.RawArguments"/>.
    /// </summary>
    /// <param name="line">The line as typed.</param>
    /// <returns>The verbatim remainder of the line.</returns>
    /// <remarks>
    /// The command word is taken as the first run of non-whitespace, NOT looked up in
    /// the token list: a token may differ from the text it was read from (the tokenizer
    /// drops quotes), so searching for it would fail on text this has no trouble with.
    /// Every registered command name is a plain word, so the two agree wherever a
    /// command was actually found.
    /// </remarks>
    private static string RawArgumentsOf(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index])) { index++; }
        while (index < line.Length && !char.IsWhiteSpace(line[index])) { index++; }

        return line.Substring(index).Trim();
    }
}