// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using Lily.Shell.Kernel;
using Lily.Shell.Kernel.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lily.Shell.Services;

/// <summary>
/// Hosts the in-process LilyPort engine for the shell: one interpreter for
/// the process lifetime, created on a background 256 MB-stack thread (the
/// Scheme layer overflows a default CLR stack), with every engine operation
/// serialized through one gate — the engine's process-global state
/// (LilyPondScheme.Current and friends) does not tolerate concurrency.
/// </summary>
/// <remarks>
/// A running Scheme evaluation cannot be interrupted; cancellation is honored
/// between operations, not inside one.
/// </remarks>
public sealed class LilyPortHost
{
    private const string DemoMusicScheme = """
        (define lily-shell-demo-music
          (make-music 'SequentialMusic
            'elements (list (make-music 'NoteEvent
                              'duration (ly:make-duration 2)
                              'pitch (ly:make-pitch 0 0 0)))))
        lily-shell-demo-music
        """;

    /// <summary>
    /// The identifier <c>display-music</c> assigns its expression to. Deliberately not
    /// a name anybody would write in a document, because the assignment lands in the
    /// shell's shared parser session and stays there.
    /// </summary>
    private const string DisplayMusicVariable = "lilyShellDisplayedMusic";

    private readonly ShellSession _session;
    private readonly IShellIO _io;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _engineLock = new(1, 1);
    private readonly List<string> _includeDirectories = [];
    private readonly List<string> _optionSettings = [];

    private Task _loadTask;
    private Interpreter _interpreter;
    private ShellIOTextWriter _schemeOutput;
    private LilyParserSession _parserSession;

    /// <summary>
    /// Creates the host over the session it serves: in-command messages go to
    /// the session's output; the async load-completion announcement goes
    /// through the session's out-of-band path, which knows whether a prompt
    /// repaint is needed.
    /// </summary>
    public LilyPortHost(ShellSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _io = session.Output;
    }

    /// <summary>
    /// Raised when the background load finishes, success or failure — on the
    /// load worker thread; marshal before touching UI state.
    /// </summary>
    public event Action LoadFinished;

    /// <summary>True once the Scheme layer has loaded successfully.</summary>
    public bool IsReady
    {
        get { lock (_gate) { return _loadTask is { IsCompletedSuccessfully: true }; } }
    }

    /// <summary>The include directories applied to parses (list grows via 'include').</summary>
    public IReadOnlyList<string> IncludeDirectories
    {
        get { lock (_gate) { return _includeDirectories.ToArray(); } }
    }

    /// <summary>Adds an include directory for subsequent parses.</summary>
    public void AddIncludeDirectory(string directory)
    {
        lock (_gate)
        {
            if (!_includeDirectories.Contains(directory)) { _includeDirectories.Add(directory); }
        }
    }

    /// <summary>Starts the ~20 s background engine load (idempotent).</summary>
    public void BeginLoading() => EnsureLoadTask();

    /// <summary>Waits until the engine is loaded, announcing the wait when it is not.</summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        var task = EnsureLoadTask();
        if (!task.IsCompleted)
        {
            _io.WriteLine("(waiting for the engine to finish loading...)");
        }

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates Scheme source through the psyntax expander (the EvalString
    /// shortcut bypasses macros and is wrong for LilyPond Scheme) and returns
    /// the last result printed with `write` conventions.
    /// </summary>
    public Task<string> EvaluateSchemeAsync(string source, CancellationToken cancellationToken) =>
        RunOnEngineAsync(interpreter =>
        {
            object result = null;
            foreach (var form in SchemeReader.ReadAll(source, "<lily-shell>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }

            _schemeOutput?.Flush();
            return Printer.Write(result);
        }, cancellationToken);

    /// <summary>Parses a .ly file and returns the parser's outcome.</summary>
    public Task<ParseOutcome> ParseFileAsync(string path, CancellationToken cancellationToken)
    {
        var text = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);
        AddIncludeDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

        return RunOnEngineAsync(interpreter =>
        {
            EnsureParserSession(interpreter);
            return _parserSession.ParseText(text, fileName);
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a <c>.ly</c> file through the real batch pipeline — parse, engrave
    /// to SVG, and MIDI whenever a score carries a <c>\midi</c> block — writing
    /// into <paramref name="outputDirectory"/>. The file's own directory is its
    /// include root, exactly as the regression driver treats it.
    /// </summary>
    /// <remarks>
    /// Added for the standing keep-Lily.Shell-current expectation:
    /// the engine produces pages and <c>.midi</c> files, and
    /// <c>engrave</c> once stopped at the parse step.
    /// </remarks>
    public Task<BatchRunResult> EngraveFileAsync(
        string path, string outputDirectory, CancellationToken cancellationToken) =>
        EngraveFileAsync(path, outputDirectory, null, cancellationToken);

    /// <summary>
    /// Runs a <c>.ly</c> file through the real batch pipeline, writing into
    /// <paramref name="outputDirectory"/> under <paramref name="outputBaseName"/>.
    /// </summary>
    /// <remarks>
    /// The named form exists because <c>-o</c> is lilypond's option and lilypond's
    /// <c>-o</c> names a FILE, not only a directory; <see cref="BatchRunner.SplitOutputName"/>
    /// is the half that decides which of the two a given value is.
    /// </remarks>
    public Task<BatchRunResult> EngraveFileAsync(
        string path,
        string outputDirectory,
        string outputBaseName,
        CancellationToken cancellationToken) =>
        RunOnEngineAsync(
            // The token reaches the RUN, not only this host's queue: the runner
            // honours it at its own boundaries (before the parse, between books,
            // before output), which is as fine as in-process cancellation gets.
            _ =>
            {
                string[] settings;
                lock (_gate) { settings = _optionSettings.ToArray(); }

                try
                {
                    // `set' is replayed into the run rather than left in the live table,
                    // because the run's own opening RestoreDefaults would wipe it: see
                    // ApplyOptionSettingAsync. MessageWriter is what makes the engine's
                    // warnings land in the terminal instead of on a console this
                    // application does not have — the run's own, and the replay's, since
                    // the runner applies BatchRunOptions.Options while this writer is in
                    // place. It restores the previous writer when it finishes.
                    return BatchRunner.RunFile(path, outputDirectory, outputBaseName,
                        new BatchRunOptions
                        {
                            CancellationToken = cancellationToken,
                            Options = settings,
                            MessageWriter = new ShellIOTextWriter(_io),
                        });
                }
                finally
                {
                    // The run restored the option table to the init layer's values on
                    // its way in, so the session's own settings have to go back on
                    // afterwards or `set' would mean one thing before an engrave and
                    // another after it. A fresh table each time, so an accumulative
                    // option gathers its entries once rather than once per engrave.
                    //
                    // Whatever this second pass warns about goes nowhere, and that is
                    // right: the runner has already put the process-wide writer back, and
                    // the user was told the first time — at the `set' that made the
                    // setting. Saying `no such program option' once per engrave forever
                    // after would be the shell nagging about a decision already taken.
                    foreach (string setting in settings)
                    {
                        CommandLineOptions.Apply(LilyPondScheme.Options, setting);
                    }
                }
            },
            cancellationToken);

    /// <summary>
    /// Engraves the first-light demo (a Scheme-built quarter-note c'4) end to
    /// end and returns the SVG document.
    /// </summary>
    public Task<string> EngraveDemoAsync(CancellationToken cancellationToken) =>
        RunOnEngineAsync(interpreter =>
        {
            object music = null;
            foreach (var form in SchemeReader.ReadAll(DemoMusicScheme, "<lily-shell-demo>"))
            {
                music = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }

            return LilyPortEngraver.EngraveToSvg((MusicObject)music);
        }, cancellationToken);

    /// <summary>
    /// Runs work that drives the engine but needs no interpreter handle of its own —
    /// documentation generation and manual rendering, whose engine calls go through
    /// BatchRunner rather than through this host's interpreter.
    /// </summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work to run.</param>
    /// <param name="cancellationToken">Cancels the WAIT for the engine gate; a running
    /// evaluation cannot be interrupted.</param>
    /// <returns>The work's result.</returns>
    /// <remarks>
    /// It still goes through the engine gate and the big-stack thread, because the engine
    /// is process-global in both senses: two engine operations at once corrupt each other's
    /// state, and the Scheme layer overflows a default CLR stack wherever it is called from.
    /// </remarks>
    public Task<T> RunEngineWorkAsync<T>(Func<T> work, CancellationToken cancellationToken) =>
        RunOnEngineAsync(_ => work(), cancellationToken);

    /// <summary>
    /// The <c>-d</c> settings this session has made, in the order they were made — each
    /// entry the text that follows a <c>-d</c> on a <c>lilypond</c> command line.
    /// </summary>
    public IReadOnlyList<string> OptionSettings
    {
        get { lock (_gate) { return _optionSettings.ToArray(); } }
    }

    /// <summary>
    /// Applies one <c>-d</c> setting to the live option table and remembers it for
    /// every later run.
    /// </summary>
    /// <param name="setting">The option text, without its <c>-d</c> prefix:
    /// <c>debug-voices</c>, <c>no-point-and-click</c>, <c>resolution=150</c>.</param>
    /// <param name="cancellationToken">Cancels the wait for the engine.</param>
    /// <returns>Whatever the engine warned about while applying it.</returns>
    /// <remarks>
    /// <para>
    /// TWO PLACES HAVE TO HEAR ABOUT IT, because they are different lifetimes. The live
    /// table is what <c>scheme</c>, <c>parse</c> and <c>demo</c> read, and writing it is
    /// what makes a setting take effect now. But every <c>engrave</c> opens with
    /// <c>LilyPondInit.RestoreDefaults</c>, which puts the whole option table back to
    /// what the init layer left — upstream engraves one file per process and cannot leak
    /// an option between files, and the port keeps that promise by restoring. So a
    /// setting that only wrote the live table would be silently dropped by the very
    /// command it was most likely made for. The remembered list is replayed into each
    /// run through <c>BatchRunOptions.Options</c>, which is the same <c>-d</c> road a
    /// command line takes.
    /// </para>
    /// <para>
    /// The warnings are CAPTURED rather than left to the process-wide writer, which in a
    /// windowed application is a console nobody is looking at. Upstream tells the user
    /// that <c>-dnosuchthing</c> is not an option and then sets it anyway; that sentence
    /// is the whole value of the exchange, so it goes to the terminal.
    /// </para>
    /// </remarks>
    public Task<ProgramOptionChange> ApplyOptionSettingAsync(
        string setting, CancellationToken cancellationToken) =>
        RunOnEngineAsync(_ =>
        {
            ProgramOptions options = LilyPondScheme.Options;
            string name = AffectedOptionName(setting);
            string before = Printer.Write(options.Get(name));

            IReadOnlyList<string> warnings = CaptureWarnings(
                () => CommandLineOptions.Apply(options, setting));

            lock (_gate) { _optionSettings.Add(setting); }
            return new ProgramOptionChange(
                name, before, Printer.Write(options.Get(name)), warnings);
        }, cancellationToken);

    /// <summary>Forgets every <c>set</c> this session has made.</summary>
    /// <param name="cancellationToken">Cancels the wait for the engine.</param>
    /// <returns>How many settings were forgotten.</returns>
    /// <remarks>
    /// The live table is put back by re-running the init layer's own values — the same
    /// restore every engrave opens with — rather than by undoing each setting, because
    /// a setting has no inverse: <c>-dinclude-settings=x</c> appended, and an option
    /// the user invented was never in the table to put back.
    /// </remarks>
    public Task<int> ClearOptionSettingsAsync(CancellationToken cancellationToken) =>
        RunOnEngineAsync(_ =>
        {
            int count;
            lock (_gate)
            {
                count = _optionSettings.Count;
                _optionSettings.Clear();
            }

            LilyPondInit.RestoreDefaults();
            return count;
        }, cancellationToken);

    /// <summary>Reads the whole option table, in the order the engine declared it.</summary>
    /// <param name="cancellationToken">Cancels the wait for the engine.</param>
    /// <returns>Every option with its current value and its documentation.</returns>
    public Task<IReadOnlyList<ProgramOptionEntry>> ReadOptionsAsync(
        CancellationToken cancellationToken) =>
        RunOnEngineAsync<IReadOnlyList<ProgramOptionEntry>>(
            _ => ReadOptions(), cancellationToken);

    /// <summary>
    /// Displays a music expression through one of the engine's own displayers.
    /// </summary>
    /// <param name="musicSource">The music, in LilyPond syntax, exactly as typed.</param>
    /// <param name="displayer">The <c>(lily)</c> procedure to display it with —
    /// <c>display-scheme-music</c>, <c>display-lily-music</c> or
    /// <c>display-music</c>.</param>
    /// <param name="cancellationToken">Cancels the wait for the engine.</param>
    /// <returns>What the parser said, and why nothing was displayed when nothing was.</returns>
    /// <remarks>
    /// <para>
    /// The expression is read by ASSIGNING it, because that is the one place LilyPond's
    /// grammar takes a bare music expression and keeps it: a music expression written at
    /// toplevel is collected into a book and engraved, which is the opposite of what
    /// this command is for. The assignment goes into the shell's own parser session, so
    /// the name it uses is one nobody would type.
    /// </para>
    /// <para>
    /// The displayers are the engine's, called with one argument, so their output goes
    /// to the current output port — which the host pointed at the terminal when it
    /// loaded. Nothing is returned to print.
    /// </para>
    /// </remarks>
    public Task<MusicDisplayOutcome> DisplayMusicAsync(
        string musicSource, string displayer, CancellationToken cancellationToken) =>
        RunOnEngineAsync(interpreter =>
        {
            EnsureParserSession(interpreter);

            ParseOutcome outcome = _parserSession.ParseText(
                DisplayMusicVariable + " = " + musicSource, "<display-music>");
            if (!outcome.Success)
            {
                return new MusicDisplayOutcome(
                    outcome.AllDiagnostics(), "that is not a music expression I can read");
            }

            object value = _parserSession.LookupIdentifier(DisplayMusicVariable);
            if (value is not MusicObject)
            {
                return new MusicDisplayOutcome(
                    outcome.AllDiagnostics(),
                    "that parsed, but it is not music (these displayers take music, "
                    + "the way \\displayMusic does)");
            }

            _parserSession.Call(_parserSession.LilyImport(displayer), value);
            _schemeOutput?.Flush();
            return new MusicDisplayOutcome(outcome.AllDiagnostics(), null);
        }, cancellationToken);

    private Task EnsureLoadTask()
    {
        lock (_gate)
        {
            return _loadTask ??= Task.Run(LoadEngine);
        }
    }

    private void LoadEngine()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Interpreter interpreter = null;
            Interpreter.RunWithLargeStack(() =>
            {
                interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);
            });

            //Scheme display/format output goes to the terminal from here on
            _schemeOutput = new ShellIOTextWriter(_io);
            interpreter.OutputWriter = _schemeOutput;
            interpreter.ErrorWriter = new ShellIOTextWriter(_io);
            _interpreter = interpreter;

            stopwatch.Stop();
            _session.WriteOutOfBand(
                $"LilyPond Scheme layer ready ({stopwatch.Elapsed.TotalSeconds:0.0} s). " +
                "Try 'scheme' or 'demo'.");
            LoadFinished?.Invoke();
        }
        catch (Exception ex)
        {
            _session.WriteOutOfBand("Engine load FAILED: " + ex.Message);
            LoadFinished?.Invoke();
            throw;
        }
    }

    private async Task<T> RunOnEngineAsync<T>(Func<Interpreter, T> work,
        CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
                Interpreter.RunWithLargeStack(() => work(_interpreter))).ConfigureAwait(false);
        }
        finally
        {
            _engineLock.Release();
        }
    }

    /// <summary>
    /// Names the option a <c>-d</c> setting actually writes: the text before the first
    /// <c>=</c>, with a <c>no-</c> prefix removed.
    /// </summary>
    /// <param name="setting">The setting text.</param>
    /// <returns>The option's name.</returns>
    /// <remarks>
    /// The prefix is stripped UNCONDITIONALLY, which is what
    /// <c>ProgramOptions.SetFromOptionName</c> does and what upstream's
    /// <c>ly_set_option</c> does before it — so a name that merely begins with
    /// <c>no-</c> would be misread here exactly as it is misread there, and no option
    /// the engine declares does.
    /// </remarks>
    private static string AffectedOptionName(string setting)
    {
        int equals = setting.IndexOf('=');
        string name = equals < 0 ? setting : setting.Substring(0, equals);

        return name.StartsWith("no-", StringComparison.Ordinal) ? name.Substring(3) : name;
    }

    /// <summary>Reads the option table into entries the shell can print.</summary>
    /// <returns>Every declared option, in the order the engine declared it.</returns>
    /// <remarks>
    /// <c>ly:all-options</c>' own alist is the source, so the order and the values are
    /// exactly what a document would see. <c>ly:option-usage</c> — upstream's
    /// <c>-dhelp</c> printer — is a recorded port-only stub, so the documentation is
    /// read from the store beside each value rather than through it.
    /// </remarks>
    private static IReadOnlyList<ProgramOptionEntry> ReadOptions()
    {
        ProgramOptions options = LilyPondScheme.Options;
        List<ProgramOptionEntry> entries = [];

        for (object cursor = options.ToAlist(); cursor is Pair pair; cursor = pair.Cdr)
        {
            if (pair.Car is not Pair entry) { continue; }

            string name = Printer.Write(entry.Car);
            entries.Add(new ProgramOptionEntry(
                name, Printer.Write(entry.Cdr), options.Documentation(name)));
        }

        return entries;
    }

    /// <summary>
    /// Runs work with the engine's diagnostic writer pointed at a string, and returns
    /// the lines it wrote.
    /// </summary>
    /// <param name="work">The work to run.</param>
    /// <returns>The diagnostics, one per line, in order.</returns>
    /// <remarks>
    /// Callers are already inside the engine gate, so the swap of a process-wide writer
    /// is not racing anything. The previous writer is put back whatever happens.
    /// </remarks>
    private static IReadOnlyList<string> CaptureWarnings(Action work)
    {
        TextWriter previous = Warn.Output;
        StringWriter captured = new StringWriter();
        Warn.Output = new LineTrackingWriter(captured);
        try
        {
            work();
        }
        finally
        {
            Warn.Output = previous;
        }

        return captured.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private void EnsureParserSession(Interpreter interpreter)
    {
        if (_parserSession == null)
        {
            _io.WriteLine("(generating parse tables - first parse only...)");
            _parserSession = new LilyParserSession(interpreter);
        }

        lock (_gate)
        {
            foreach (var directory in _includeDirectories)
            {
                if (!_parserSession.IncludePath.Contains(directory))
                {
                    _parserSession.IncludePath.Add(directory);
                }
            }
        }
    }
}
