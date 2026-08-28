// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MutopiaProbe.Oracle;

/// <summary>
/// Runs the PINNED upstream LilyPond over the same source the port was given, with the same
/// backend, the same point-and-click setting and the same font resolution — so that the pages it
/// produces can go through the tool's own SVG-to-PDF writer and be graded against the port's on
/// exactly the ladder Mutopia's are.
/// <para>
/// This is what turns "differs from Mutopia" into "differs from LilyPond". Mutopia's reference
/// was built by the 2.4-to-2.19 release named in the piece's <c>\version</c>; the oracle is
/// 2.27.2, the version the port targets. When the port and the oracle agree and both differ from
/// Mutopia, the difference is version drift. When they disagree, it is the port's.
/// </para>
/// </summary>
public static class OracleRunner
{
    private static readonly Regex Diagnostic = new Regex(
        @"(^|:\d+:\d+: )(?<kind>warning|error|programming error|fatal error):",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>What one oracle run produced.</summary>
    public sealed class Outcome
    {
        /// <summary>Gets or sets the status: OK, NOOUT, FAIL, TIMEOUT or LAUNCH-ERROR.</summary>
        public string Status { get; set; }

        /// <summary>Gets or sets the failure text, or null.</summary>
        public string Error { get; set; }

        /// <summary>Gets or sets the process exit code, or -1 when it never exited.</summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>Gets the SVG pages, in page order.</summary>
        public List<string> SvgPages { get; } = new List<string>();

        /// <summary>Gets the MIDI files, in name order.</summary>
        public List<string> MidiFiles { get; } = new List<string>();

        /// <summary>Gets or sets how many <c>error:</c> lines the run printed.</summary>
        public int Errors { get; set; }

        /// <summary>Gets or sets how many <c>warning:</c> lines the run printed.</summary>
        public int Warnings { get; set; }

        /// <summary>Gets or sets the wall-clock seconds the run took.</summary>
        public double Seconds { get; set; }
    }

    /// <summary>Gets the fontconfig file the oracle is run under, or null when it is not pinned.</summary>
    public static string FontConfigFile { get; private set; }

    /// <summary>Gets the directory the pinned fonts were found in, or null.</summary>
    public static string FontDirectory { get; private set; }

    /// <summary>Reads the oracle's version banner.</summary>
    /// <param name="binary">The <c>lilypond</c> executable.</param>
    /// <returns>The first line of <c>--version</c>, or the failure text.</returns>
    public static string Version(string binary)
    {
        try
        {
            using Process process = Start(binary, "--version", Path.GetTempPath());
            StringBuilder text = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { text.AppendLine(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { text.AppendLine(e.Data); } };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (process.WaitForExit(30000))
            {
                // WaitForExit(int) returns when the process ends, which can be BEFORE the
                // redirected streams are drained; the argument-less overload waits for both.
                process.WaitForExit();
            }

            string banner = text.ToString();
            int end = banner.IndexOfAny(new[] { '\r', '\n' });
            return end < 0 ? banner.Trim() : banner.Substring(0, end).Trim();
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            return "(cannot run " + binary + ": " + exception.Message + ")";
        }
    }

    /// <summary>
    /// Writes the fontconfig file the oracle runs under and returns its path, or null when the
    /// oracle's own font directory cannot be found.
    /// </summary>
    /// <param name="binary">The <c>lilypond</c> executable.</param>
    /// <param name="directory">Where the generated <c>.conf</c> goes.</param>
    /// <returns>The configuration file, or null.</returns>
    /// <remarks>
    /// This mirrors tools/regression-harness/reference-fonts.conf.in and exists for the same
    /// reason: under <c>-dbackend=svg</c>, ly/paper-defaults-init.ly makes LilyPond name the CSS
    /// GENERIC families, so Pango resolves "serif"/"sans"/"monospace" through the HOST's
    /// fontconfig and the oracle silently measures its text in whatever that machine has
    /// installed. The port's faces are fixed (D23, no system fallback ever), so an unpinned
    /// oracle would report a text-metric difference that is the host's, not the port's. The
    /// only <c>dir</c> element is the oracle's own bundled font directory — the same files the
    /// port vendors — so there is no system font to fall back to even in principle. If
    /// reference-fonts.conf.in changes, this changes with it.
    /// </remarks>
    public static string PinFonts(string binary, string directory)
    {
        string prefix = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(binary)), ".."));
        string share = Path.Combine(prefix, "share", "lilypond");
        string fonts = null;
        if (Directory.Exists(share))
        {
            foreach (string version in Directory.GetDirectories(share))
            {
                string candidate = Path.Combine(version, "fonts", "otf");
                if (Directory.Exists(candidate))
                {
                    fonts = candidate;
                }
            }
        }

        if (fonts == null)
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "oracle-fonts.conf");
        File.WriteAllText(path, Configuration.Replace("@FONTDIR@", fonts));
        FontConfigFile = path;
        FontDirectory = fonts;
        return path;
    }

    /// <summary>Runs the oracle over one file.</summary>
    /// <param name="binary">The <c>lilypond</c> executable.</param>
    /// <param name="lyPath">The <c>.ly</c> — the SAME one the port was given.</param>
    /// <param name="outputDirectory">Where the pages and MIDI land.</param>
    /// <param name="outputBaseName">The output base name.</param>
    /// <param name="scratchDirectory">The working directory; emptied first.</param>
    /// <param name="logPath">Where everything the oracle prints is captured.</param>
    /// <param name="timeout">The wall-clock budget; the process is killed when it expires.</param>
    /// <returns>The outcome.</returns>
    public static Outcome Run(
        string binary, string lyPath, string outputDirectory, string outputBaseName,
        string scratchDirectory, string logPath, TimeSpan timeout)
    {
        Outcome outcome = new Outcome();
        Stopwatch clock = Stopwatch.StartNew();
        if (Directory.Exists(scratchDirectory))
        {
            Directory.Delete(scratchDirectory, true);
        }

        Directory.CreateDirectory(scratchDirectory);
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, true);
        }

        Directory.CreateDirectory(outputDirectory);

        StringBuilder captured = new StringBuilder();
        try
        {
            // --formats=svg, not svg,midi: MIDI is written by the \midi block regardless, and
            // naming it as a format only earns an "ignoring unsupported formats" warning that
            // would then be counted as a diagnostic. --silent is NOT passed: the oracle's own
            // warnings and errors are half of what this mode is for.
            string arguments = "--formats=svg -dbackend=svg -dno-point-and-click -o "
                + Quote(Path.Combine(outputDirectory, outputBaseName)) + " " + Quote(lyPath);
            using Process process = Start(binary, arguments, scratchDirectory);
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (captured) { captured.AppendLine(e.Data); } } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (captured) { captured.AppendLine(e.Data); } } };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    // The process is already gone; the TIMEOUT verdict stands either way.
                }

                outcome.Status = "TIMEOUT";
                outcome.Error = "killed after " + timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s";
            }
            else
            {
                outcome.ExitCode = process.ExitCode;
                // WaitForExit(int) returns when the process ends, which can be BEFORE the
                // redirected streams are drained; the argument-less overload waits for both.
                process.WaitForExit();
            }
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            outcome.Status = "LAUNCH-ERROR";
            outcome.Error = exception.GetType().Name + ": " + exception.Message;
        }
        finally
        {
            outcome.Seconds = clock.Elapsed.TotalSeconds;
        }

        string log = captured.ToString();
        File.WriteAllText(logPath, log);
        foreach (Match match in Diagnostic.Matches(log))
        {
            if (match.Groups["kind"].Value == "warning")
            {
                outcome.Warnings++;
            }
            else
            {
                outcome.Errors++;
            }
        }

        Collect(outcome, outputDirectory, outputBaseName);
        if (outcome.Status == null)
        {
            outcome.Status = outcome.SvgPages.Count > 0 ? "OK" : outcome.ExitCode == 0 ? "NOOUT" : "FAIL";
            if (outcome.Status == "FAIL")
            {
                outcome.Error = "exit " + outcome.ExitCode.ToString(CultureInfo.InvariantCulture);
            }
        }

        return outcome;
    }

    private static void Collect(Outcome outcome, string outputDirectory, string outputBaseName)
    {
        // Upstream's naming, from scm/framework-svg.scm and scm/midi.scm: a one-page score is
        // <base>.svg and a longer one is <base>-1.svg .. <base>-N.svg; the first performance is
        // <base>.midi and any further ones <base>-<n>.midi. The port reproduces both exactly,
        // so the same ordering rule serves both sides.
        List<(int Number, string Path)> pages = new List<(int, string)>();
        List<string> midi = new List<string>();
        if (!Directory.Exists(outputDirectory))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(outputDirectory))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string extension = Path.GetExtension(file);
            if (!name.StartsWith(outputBaseName, StringComparison.Ordinal))
            {
                continue;
            }

            string suffix = name.Substring(outputBaseName.Length);
            if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
            {
                if (suffix.Length == 0)
                {
                    pages.Add((0, file));
                }
                else if (suffix[0] == '-' && int.TryParse(suffix.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                {
                    pages.Add((number, file));
                }
            }
            else if (string.Equals(extension, ".midi", StringComparison.OrdinalIgnoreCase))
            {
                midi.Add(file);
            }
        }

        pages.Sort((a, b) => a.Number.CompareTo(b.Number));
        foreach ((int _, string path) in pages)
        {
            outcome.SvgPages.Add(path);
        }

        midi.Sort(StringComparer.Ordinal);
        outcome.MidiFiles.AddRange(midi);
    }

    private static Process Start(string binary, string arguments, string workingDirectory)
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (FontConfigFile != null)
        {
            start.Environment["FONTCONFIG_FILE"] = FontConfigFile;
            start.Environment["FONTCONFIG_PATH"] = Path.GetDirectoryName(FontConfigFile);
        }

        return Process.Start(start);
    }

    private static string Quote(string path) => "\"" + path + "\"";

    private const string Configuration = @"<?xml version=""1.0""?>
<fontconfig>

  <!--
    Generated by tools/mutopia-probe (Oracle/OracleRunner.cs). Mirrors
    tools/regression-harness/reference-fonts.conf.in: the oracle's generic families are pinned
    to the same faces the port uses, out of the oracle's OWN bundled font directory, with no
    system directory in scope. Do not add a dir element here.
    (This is an XML comment, so it may not contain a double hyphen anywhere.)
  -->

  <dir>@FONTDIR@</dir>
  <cachedir prefix=""xdg"">fontconfig</cachedir>

  <alias binding=""strong"">
    <family>serif</family>
    <prefer>
      <family>C059</family>
      <family>TeX Gyre Schola</family>
    </prefer>
  </alias>

  <alias binding=""strong"">
    <family>sans</family>
    <prefer>
      <family>Nimbus Sans</family>
      <family>TeX Gyre Heros</family>
    </prefer>
  </alias>
  <alias binding=""strong"">
    <family>sans-serif</family>
    <prefer>
      <family>Nimbus Sans</family>
      <family>TeX Gyre Heros</family>
    </prefer>
  </alias>

  <alias binding=""strong"">
    <family>monospace</family>
    <prefer>
      <family>Nimbus Mono PS</family>
      <family>TeX Gyre Cursor</family>
    </prefer>
  </alias>

</fontconfig>
";
}
