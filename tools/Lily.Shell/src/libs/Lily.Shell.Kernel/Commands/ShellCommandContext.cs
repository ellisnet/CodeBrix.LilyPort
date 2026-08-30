// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.IO;
using System.Collections.Generic;
using System.Threading;

namespace Lily.Shell.Kernel.Commands;

/// <summary>
/// Everything a command execution gets to work with: its arguments, the
/// session, the output surface, and a cancellation token that is signalled
/// when the user presses Ctrl+C while the command runs.
/// </summary>
public sealed class ShellCommandContext
{
    internal ShellCommandContext(ShellSession session, IReadOnlyList<string> arguments,
        string rawArguments, CancellationToken cancellationToken)
    {
        Session = session;
        Arguments = arguments;
        RawArguments = rawArguments ?? string.Empty;
        CancellationToken = cancellationToken;
    }

    /// <summary>The session the command runs in.</summary>
    public ShellSession Session { get; }

    /// <summary>The arguments after the command name, tokenized.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// The text after the command name, exactly as it was typed — quotes, backslashes
    /// and runs of whitespace all still in it.
    /// </summary>
    /// <remarks>
    /// TOKENIZING LOSES CHARACTERS THAT MATTER TO A LANGUAGE THAT IS NOT THIS ONE.
    /// <see cref="CommandLineTokenizer"/> treats a double quote as grouping and drops
    /// it, which is right for a file name and wrong for LilyPond source: the music
    /// <c>c'4^"text"</c> comes back through <see cref="Arguments"/> as
    /// <c>c'4^text</c>, which parses as something else entirely rather than failing.
    /// A command whose argument is source code in another language reads it from here
    /// and never from the tokens.
    /// </remarks>
    public string RawArguments { get; }

    /// <summary>Signalled when the user cancels the running command with Ctrl+C.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>The output surface to write results to.</summary>
    public IShellIO IO => Session.Output;
}
