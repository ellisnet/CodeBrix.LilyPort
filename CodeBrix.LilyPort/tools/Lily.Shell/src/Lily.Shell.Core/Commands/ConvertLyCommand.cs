// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.ConvertLy;
using Lily.Shell.Kernel.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Brings an old document up to the syntax this engine reads — the shell's half of the
/// port's <c>convert-ly</c>.
/// </summary>
/// <remarks>
/// ⚠ NEVER REWRITES IN PLACE. Upstream's script edits the file it was given and keeps a
/// backup; a shell session is not the place to acquire that habit, so the converted
/// text is printed unless <c>-o</c> names somewhere to put it. What the rules had to
/// say is printed either way — those remarks are the part of a conversion that needs a
/// human, and losing them is how a half-converted document looks finished.
/// </remarks>
public sealed class ConvertLyCommand : IShellCommand
{
    /// <inheritdoc/>
    public string Name => "convert-ly";

    /// <inheritdoc/>
    public string Summary => "Converts an old .ly file to the current syntax.";

    /// <inheritdoc/>
    public string Usage => "convert-ly <file.ly> [--from <version>] [--to <version>] [-o <out.ly>]";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        ConvertLyCommandLine parsed = ConvertLyCommandLine.Parse(context.Arguments);
        if (parsed.Error != null)
        {
            context.IO.WriteLine("convert-ly: " + parsed.Error);
            context.IO.WriteLine("Usage: " + Usage);
            return Task.CompletedTask;
        }

        if (!File.Exists(parsed.InputPath))
        {
            context.IO.WriteLine("No such file: " + parsed.InputPath);
            return Task.CompletedTask;
        }

        string text;
        try
        {
            text = File.ReadAllText(parsed.InputPath);
        }
        catch (IOException error)
        {
            context.IO.WriteLine("Could not read " + parsed.InputPath + ": " + error.Message);
            return Task.CompletedTask;
        }

        ConversionResult result = DocumentConverter.Convert(
            text, parsed.From, parsed.To);

        if (result.VersionUnknown)
        {
            context.IO.WriteLine(
                "The file declares no usable \\version, and no --from was given; "
                + "nothing was converted.");
            return Task.CompletedTask;
        }

        foreach (string message in result.Messages)
        {
            context.IO.WriteLine(message);
        }

        context.IO.WriteLine(
            "Converted from " + result.FromVersion + " to " + result.ToVersion
            + " (" + result.AppliedRules.Count + " rules ran, "
            + (result.Changed ? "document changed" : "document unchanged") + ").");
        if (result.StampedVersion != null)
        {
            context.IO.WriteLine("\\version now reads " + result.StampedVersion.Value + ".");
        }

        if (result.Errors > 0)
        {
            context.IO.WriteLine(
                result.Errors + " rule(s) gave up; what came before them was kept.");
        }

        if (parsed.OutputPath == null)
        {
            context.IO.WriteLine(string.Empty);
            context.IO.WriteLine(result.Text);
            return Task.CompletedTask;
        }

        try
        {
            File.WriteAllText(parsed.OutputPath, result.Text);
            context.IO.WriteLine("Written to " + parsed.OutputPath);
        }
        catch (IOException error)
        {
            context.IO.WriteLine(
                "Could not write " + parsed.OutputPath + ": " + error.Message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>What a <c>convert-ly</c> command line asked for.</summary>
internal sealed class ConvertLyCommandLine
{
    private ConvertLyCommandLine()
    {
    }

    /// <summary>The message to print instead of running, or null when the line parsed.</summary>
    public string Error { get; private set; }

    /// <summary>The file to convert.</summary>
    public string InputPath { get; private set; }

    /// <summary>Where to write, or null to print.</summary>
    public string OutputPath { get; private set; }

    /// <summary>The version to convert from, or null for the document's own.</summary>
    public ConversionVersion? From { get; private set; }

    /// <summary>The version to convert to, or null for the newest any rule targets.</summary>
    public ConversionVersion? To { get; private set; }

    /// <summary>Parses a <c>convert-ly</c> command line.</summary>
    /// <param name="arguments">The arguments after the command name.</param>
    /// <returns>The parsed line, with <see cref="Error"/> set when it did not parse.</returns>
    public static ConvertLyCommandLine Parse(IReadOnlyList<string> arguments)
    {
        ConvertLyCommandLine parsed = new ConvertLyCommandLine();
        if (arguments == null || arguments.Count == 0)
        {
            parsed.Error = "which file?";
            return parsed;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            switch (argument)
            {
                case "-f":
                case "--from":
                    if (++i >= arguments.Count)
                    {
                        parsed.Error = argument + " needs a version";
                        return parsed;
                    }

                    if (!ConversionVersion.TryParse(arguments[i], out ConversionVersion from))
                    {
                        parsed.Error = "'" + arguments[i] + "' is not a version";
                        return parsed;
                    }

                    parsed.From = from;
                    break;

                case "-t":
                case "--to":
                    if (++i >= arguments.Count)
                    {
                        parsed.Error = argument + " needs a version";
                        return parsed;
                    }

                    if (!ConversionVersion.TryParse(arguments[i], out ConversionVersion to))
                    {
                        parsed.Error = "'" + arguments[i] + "' is not a version";
                        return parsed;
                    }

                    parsed.To = to;
                    break;

                case "-o":
                case "--output":
                    if (++i >= arguments.Count)
                    {
                        parsed.Error = argument + " needs a file";
                        return parsed;
                    }

                    parsed.OutputPath = Path.GetFullPath(arguments[i]);
                    break;

                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        parsed.Error = "unknown option '" + argument + "'";
                        return parsed;
                    }

                    if (parsed.InputPath != null)
                    {
                        parsed.Error = "one file at a time, please ('" + argument
                            + "' is a second)";
                        return parsed;
                    }

                    parsed.InputPath = Path.GetFullPath(argument);
                    break;
            }
        }

        if (parsed.InputPath == null)
        {
            parsed.Error = "which file?";
        }

        return parsed;
    }
}
