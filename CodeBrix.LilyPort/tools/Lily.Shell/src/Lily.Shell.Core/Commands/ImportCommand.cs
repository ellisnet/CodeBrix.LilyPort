// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Importers;
using Lily.Shell.Kernel.Commands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Reads another music format and writes LilyPond — the shell's half of the port's
/// input converters.
/// </summary>
/// <remarks>
/// The per-format switches are named after the long options of the script each format
/// comes from, so someone who knows <c>abc2ly</c> or <c>midi2ly</c> already knows this.
/// What the converter had to say is printed before the source, for the same reason
/// <c>convert-ly</c> prints its remarks: a transcription with a hole in it looks
/// finished until you read the warnings.
/// </remarks>
public sealed class ImportCommand : IShellCommand
{
    /// <inheritdoc/>
    public string Name => "import";

    /// <inheritdoc/>
    public string Summary => "Converts ABC, MIDI or MusicXML to LilyPond source.";

    /// <inheritdoc/>
    public string Usage
        => "import abc|midi|musicxml <file> [-o <out.ly>] "
           + "[format options — 'import' alone lists them]";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        ImportCommandLine parsed = ImportCommandLine.Parse(context.Arguments);
        if (parsed.ListOnly)
        {
            WriteFormats(context);
            return Task.CompletedTask;
        }

        if (parsed.Error != null)
        {
            context.IO.WriteLine("import: " + parsed.Error);
            context.IO.WriteLine("Usage: " + Usage);
            return Task.CompletedTask;
        }

        if (!File.Exists(parsed.InputPath))
        {
            context.IO.WriteLine("No such file: " + parsed.InputPath);
            return Task.CompletedTask;
        }

        ImportResult result;
        try
        {
            result = parsed.Format switch
            {
                ImportFormat.Abc => AbcImporter.Import(
                    File.ReadAllText(parsed.InputPath), parsed.AbcOptions),
                ImportFormat.Midi => MidiImporter.Import(
                    File.ReadAllBytes(parsed.InputPath), parsed.MidiOptions),
                //A `.mxl' is a zip container; everything else is the XML itself.
                _ => parsed.InputPath.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase)
                    ? MusicXmlImporter.ImportCompressed(
                        File.ReadAllBytes(parsed.InputPath), parsed.MusicXmlOptions)
                    : MusicXmlImporter.Import(
                        File.ReadAllText(parsed.InputPath), parsed.MusicXmlOptions),
            };
        }
        catch (IOException error)
        {
            context.IO.WriteLine(
                "Could not read " + parsed.InputPath + ": " + error.Message);
            return Task.CompletedTask;
        }

        foreach (string message in result.Messages)
        {
            context.IO.WriteLine(message);
        }

        if (result.Text == null)
        {
            context.IO.WriteLine(
                "Nothing was converted (" + result.Errors + " error(s)).");
            return Task.CompletedTask;
        }

        if (result.Errors > 0)
        {
            context.IO.WriteLine(
                result.Errors + " error(s); what could be converted was kept.");
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

    private static void WriteFormats(ShellCommandContext context)
    {
        context.IO.WriteLine("Formats:");
        context.IO.WriteLine("  abc    ABC music notation (abc2ly)");
        context.IO.WriteLine("           --strict           be strict about success");
        context.IO.WriteLine("           --beams            preserve ABC's notion of beams");
        context.IO.WriteLine("  midi   Standard MIDI File (midi2ly)");
        context.IO.WriteLine("           --absolute-pitches print absolute pitches");
        context.IO.WriteLine("           --duration-quant N quantise note durations on N");
        context.IO.WriteLine("           --explicit-durations  print explicit durations");
        context.IO.WriteLine("           --include-header F prepend F to the output (repeatable)");
        context.IO.WriteLine("           --key ALT[:MINOR]  set key: ALT=+sharps|-flats; MINOR=1");
        context.IO.WriteLine("           --preview          only the first four bars");
        context.IO.WriteLine("           --skip             use s instead of r for rests");
        context.IO.WriteLine("           --start-quant N    quantise note starts on N");
        context.IO.WriteLine("           --allow-tuplet D*N/M  allow that tuplet (repeatable)");
        context.IO.WriteLine("           --text-lyrics      treat every text as a lyric");
        context.IO.WriteLine("  musicxml  MusicXML, plain or compressed (musicxml2ly)");
        context.IO.WriteLine("           --absolute         absolute rather than relative pitches");
        context.IO.WriteLine("           --language LANG    note names in LANG, e.g. deutsch");
        context.IO.WriteLine("           --ottavas-end-early t|f  <octave-shift> ends before its note");
        context.IO.WriteLine("           --no-articulation-directions  drop ^, _ and - modifiers");
        context.IO.WriteLine("           --no-rest-positions   drop exact vertical rest positions");
        context.IO.WriteLine("           --no-system-breaks    ignore system breaks");
        context.IO.WriteLine("           --no-page-breaks      ignore page breaks");
        context.IO.WriteLine("           --no-page-margins     ignore page margins");
        context.IO.WriteLine("           --no-page-layout      the three above together");
        context.IO.WriteLine("           --no-stem-directions  let LilyPond choose stems");
        context.IO.WriteLine("           --no-beaming          let LilyPond beam");
        context.IO.WriteLine("           --dynamics-scale F    scale <dynamics> by F");
        context.IO.WriteLine("           --absolute-font-sizes  markup sizes ignore the score size");
        context.IO.WriteLine("           --midi                write a \\midi block");
        context.IO.WriteLine("           --credit-page N       take header fields from page N");
        context.IO.WriteLine("           --transpose PITCH     transpose c to PITCH");
        context.IO.WriteLine("           --shift-durations N   -1 doubles durations, 1 halves");
        context.IO.WriteLine("           --tab-clef NAME       tab or moderntab");
        context.IO.WriteLine("           --string-numbers t|f  write string numbers");
        context.IO.WriteLine("           --fretboards          <frame> becomes a FretBoards voice");
        context.IO.WriteLine("           --book                wrap the score in \\book");
        context.IO.WriteLine("           --no-tagline          leave out the tag line");
    }
}

/// <summary>Which converter an <c>import</c> asked for.</summary>
internal enum ImportFormat
{
    /// <summary>ABC music notation.</summary>
    Abc,

    /// <summary>A Standard MIDI File.</summary>
    Midi,

    /// <summary>MusicXML, plain or in a compressed container.</summary>
    MusicXml,
}

/// <summary>What an <c>import</c> command line asked for.</summary>
internal sealed class ImportCommandLine
{
    private ImportCommandLine()
    {
    }

    /// <summary>The message to print instead of running, or null when the line parsed.</summary>
    public string Error { get; private set; }

    /// <summary>True when the line asked for the format list rather than a conversion.</summary>
    public bool ListOnly { get; private set; }

    /// <summary>Which converter.</summary>
    public ImportFormat Format { get; private set; }

    /// <summary>The file to convert.</summary>
    public string InputPath { get; private set; }

    /// <summary>Where to write, or null to print.</summary>
    public string OutputPath { get; private set; }

    /// <summary>What was asked of the ABC importer.</summary>
    public AbcImportOptions AbcOptions { get; private set; }

    /// <summary>What was asked of the MIDI importer.</summary>
    public MidiImportOptions MidiOptions { get; private set; }

    /// <summary>What was asked of the MusicXML importer.</summary>
    public MusicXmlImportOptions MusicXmlOptions { get; private set; }

    /// <summary>
    /// The file as the user spelled it, which is what upstream's converters print.
    /// </summary>
    /// <remarks>
    /// ⚠ NOT <see cref="InputPath"/>. That has been through Path.GetFullPath so the
    /// command can open it; upstream writes the argument AS GIVEN -- into abc2ly's one
    /// location-bearing message, and into the first line of every document midi2ly
    /// produces. Resolving it there would put this machine's directory layout in the
    /// user's score.
    /// </remarks>
    public string SourceName { get; private set; }

    /// <summary>Parses an <c>import</c> command line.</summary>
    /// <param name="arguments">The arguments after the command name.</param>
    /// <returns>The parsed line, with <see cref="Error"/> set when it did not parse.</returns>
    public static ImportCommandLine Parse(IReadOnlyList<string> arguments)
    {
        ImportCommandLine parsed = new ImportCommandLine();
        if (arguments == null || arguments.Count == 0)
        {
            parsed.ListOnly = true;
            return parsed;
        }

        switch (arguments[0])
        {
            case "abc":
                parsed.Format = ImportFormat.Abc;
                parsed.AbcOptions = new AbcImportOptions();
                break;
            case "midi":
                parsed.Format = ImportFormat.Midi;
                parsed.MidiOptions = new MidiImportOptions();
                break;
            case "musicxml":
                parsed.Format = ImportFormat.MusicXml;
                parsed.MusicXmlOptions = new MusicXmlImportOptions();
                break;
            default:
                parsed.Error = "unknown format '" + arguments[0]
                    + "' (abc, midi or musicxml)";
                return parsed;
        }

        for (int i = 1; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            if (argument == "-o" || argument == "--output")
            {
                if (++i >= arguments.Count)
                {
                    parsed.Error = argument + " needs a file";
                    return parsed;
                }

                parsed.OutputPath = Path.GetFullPath(arguments[i]);
                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                if (!ReadFormatOption(parsed, arguments, ref i))
                {
                    return parsed;
                }

                continue;
            }

            if (parsed.InputPath != null)
            {
                parsed.Error = "one file at a time, please ('" + argument
                    + "' is a second)";
                return parsed;
            }

            parsed.InputPath = Path.GetFullPath(argument);
            parsed.SourceName = argument;
        }

        if (parsed.AbcOptions != null)
        {
            parsed.AbcOptions.SourceName = parsed.SourceName ?? string.Empty;
        }
        else if (parsed.MidiOptions != null)
        {
            parsed.MidiOptions.SourceName = parsed.SourceName ?? string.Empty;
        }
        else if (parsed.MusicXmlOptions != null)
        {
            parsed.MusicXmlOptions.SourceName = parsed.SourceName ?? string.Empty;
        }

        if (parsed.InputPath == null)
        {
            parsed.Error = "which file?";
        }

        return parsed;
    }

    /// <summary>Reads one per-format option.</summary>
    /// <param name="parsed">The line being built.</param>
    /// <param name="arguments">Every argument.</param>
    /// <param name="i">Where the option was found; advanced past any value.</param>
    /// <returns>Whether the option was understood.</returns>
    private static bool ReadFormatOption(
        ImportCommandLine parsed, IReadOnlyList<string> arguments, ref int i)
    {
        string argument = arguments[i];
        if (parsed.Format == ImportFormat.MusicXml)
        {
            return ReadMusicXmlOption(parsed, arguments, ref i);
        }

        if (parsed.Format == ImportFormat.Abc)
        {
            switch (argument)
            {
                case "--strict":
                    parsed.AbcOptions.Strict = true;
                    return true;
                case "--beams":
                    parsed.AbcOptions.Beams = true;
                    return true;
                default:
                    parsed.Error = "unknown option '" + argument + "' for abc";
                    return false;
            }
        }

        switch (argument)
        {
            case "--absolute-pitches":
                parsed.MidiOptions.AbsolutePitches = true;
                return true;
            case "--explicit-durations":
                parsed.MidiOptions.ExplicitDurations = true;
                return true;
            case "--preview":
                parsed.MidiOptions.Preview = true;
                return true;
            case "--skip":
                parsed.MidiOptions.Skip = true;
                return true;
            case "--text-lyrics":
                parsed.MidiOptions.TextLyrics = true;
                return true;

            case "--duration-quant":
            case "--start-quant":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                if (!int.TryParse(
                    value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out int quant))
                {
                    parsed.Error = "'" + value + "' is not a number";
                    return false;
                }

                if (argument == "--duration-quant")
                {
                    parsed.MidiOptions.DurationQuant = quant;
                }
                else
                {
                    parsed.MidiOptions.StartQuant = quant;
                }

                return true;
            }

            case "--key":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                parsed.MidiOptions.Key = value;
                return true;
            }

            case "--include-header":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                parsed.MidiOptions.IncludeHeader.Add(Path.GetFullPath(value));
                return true;
            }

            case "--allow-tuplet":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                parsed.MidiOptions.AllowTuplet.Add(value);
                return true;
            }

            default:
                parsed.Error = "unknown option '" + argument + "' for midi";
                return false;
        }
    }

    /// <summary>Reads one MusicXML option.</summary>
    /// <param name="parsed">The line being built.</param>
    /// <param name="arguments">Every argument.</param>
    /// <param name="i">Where the option was found; advanced past any value.</param>
    /// <returns>Whether the option was understood.</returns>
    /// <remarks>
    /// The LONG spellings only, because those are what the option surface is named after;
    /// upstream's two-letter abbreviations belong to its command line, not to this one.
    /// </remarks>
    private static bool ReadMusicXmlOption(
        ImportCommandLine parsed, IReadOnlyList<string> arguments, ref int i)
    {
        string argument = arguments[i];
        switch (argument)
        {
            case "--absolute":
                parsed.MusicXmlOptions.PitchMode = MusicXmlPitchMode.Absolute;
                return true;
            case "--relative":
                parsed.MusicXmlOptions.PitchMode = MusicXmlPitchMode.Relative;
                return true;
            case "--no-articulation-directions":
                parsed.MusicXmlOptions.NoArticulationDirections = true;
                return true;
            case "--no-rest-positions":
                parsed.MusicXmlOptions.NoRestPositions = true;
                return true;
            case "--no-system-breaks":
                parsed.MusicXmlOptions.NoSystemBreaks = true;
                return true;
            case "--no-page-breaks":
                parsed.MusicXmlOptions.NoPageBreaks = true;
                return true;
            case "--no-page-margins":
                parsed.MusicXmlOptions.NoPageMargins = true;
                return true;
            case "--no-page-layout":
                parsed.MusicXmlOptions.NoPageLayout = true;
                return true;
            case "--no-stem-directions":
                parsed.MusicXmlOptions.NoStemDirections = true;
                return true;
            case "--no-beaming":
                parsed.MusicXmlOptions.NoBeaming = true;
                return true;
            case "--absolute-font-sizes":
                parsed.MusicXmlOptions.AbsoluteFontSizes = true;
                return true;
            case "--midi":
                parsed.MusicXmlOptions.Midi = true;
                return true;
            case "--fretboards":
                parsed.MusicXmlOptions.Fretboards = true;
                return true;
            case "--book":
                parsed.MusicXmlOptions.Book = true;
                return true;
            case "--no-tagline":
                parsed.MusicXmlOptions.NoTagline = true;
                return true;

            case "--language":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                parsed.MusicXmlOptions.Language = value;
                return true;
            }

            case "--ottavas-end-early":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                parsed.MusicXmlOptions.OttavasEndEarly = value;
                return true;
            }

            case "--transpose":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                parsed.MusicXmlOptions.Transpose = value;
                return true;
            }

            case "--tab-clef":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                parsed.MusicXmlOptions.TabClef = value;
                return true;
            }

            case "--string-numbers":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                parsed.MusicXmlOptions.StringNumbers = value;
                return true;
            }

            case "--dynamics-scale":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                if (!double.TryParse(
                        value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double scale))
                {
                    parsed.Error = "'" + value + "' is not a number";
                    return false;
                }

                parsed.MusicXmlOptions.DynamicsScale = scale;
                return true;
            }

            case "--credit-page":
            case "--shift-durations":
            {
                if (!TakeValue(parsed, arguments, ref i, out string value))
                {
                    return false;
                }

                if (!int.TryParse(
                        value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                        out int number))
                {
                    parsed.Error = "'" + value + "' is not a number";
                    return false;
                }

                if (argument == "--credit-page")
                {
                    parsed.MusicXmlOptions.CreditPage = number;
                }
                else
                {
                    parsed.MusicXmlOptions.ShiftDurations = number;
                }

                return true;
            }

            default:
                parsed.Error = "unknown option '" + argument + "' for musicxml";
                return false;
        }
    }

    private static bool TakeValue(
        ImportCommandLine parsed, IReadOnlyList<string> arguments, ref int i,
        out string value)
    {
        string argument = arguments[i];
        if (++i >= arguments.Count)
        {
            parsed.Error = argument + " needs a value";
            value = null;
            return false;
        }

        value = arguments[i];
        return true;
    }
}
