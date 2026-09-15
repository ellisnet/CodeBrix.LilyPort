// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CodeBrix.LilyPort.Backends;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort;

/// <summary>
/// The INTERNAL in-process batch runner: a <c>.ly</c> file in, an SVG document per
/// file out, through the REAL toplevel handlers.
/// <para>
/// This is the machinery decision D14 kept when Lily.Shell took over the public
/// CLI: the port's Scheme startup makes a process per file unusable, so the
/// regression harness drives thousands of files through one process, serialised
/// (standing rule 8) because the engine's Scheme layer is process-global state.
/// </para>
/// <para>
/// The lifecycle is <c>ly/init.ly</c>'s own: the prologue and epilogue this class
/// parses around each file are VERBATIM extracts of the vendored file (see
/// <see cref="ProloguelLy"/> and <see cref="EpilogueLy"/> for exactly what and why),
/// so score collection, book construction, the version check and the
/// expect-error handshake are all upstream's real code running through the real
/// parser. D20's divergence is DISCHARGED since the page-layout group landed: the runner used to
/// define <c>default-toplevel-book-handler</c> — the escape hatch <c>init.ly</c> itself
/// checks for — and take each book's scores STRAIGHT to the SVG backend, score by score.
/// It now runs the real <c>Book::process</c> → <c>Paper_book</c> → page-breaker path and
/// writes ONE FILE PER PAGE under the oracle's own naming.
/// </para>
/// <para>
/// What it still does differently from upstream is WHERE book processing happens:
/// upstream reaches it from inside the parser, this runner collects books during the
/// parse and processes them after it. That is why the loop publishes <c>%parser</c>
/// itself — see the note on <see cref="LilyParserSession.AsCurrentParser"/>'s call site.
/// </para>
/// </summary>
public static class BatchRunner
{
    private static readonly object Gate = new object();

    /// <summary>
    /// The identifier <c>print-book-with</c> reads the layout out of at book-processing
    /// time. Looked up by NAME because a toplevel <c>\layout</c> block rebinds it.
    /// </summary>
    private const string DefaultLayoutName = "$defaultlayout";
    private const string DefaultPaperName = "$defaultpaper";

    /// <summary>
    /// <c>ly/init.ly</c> lines 27–35, verbatim: the session variables the toplevel
    /// handlers collect into. Parsing this before each file is also what RESETS the
    /// collection state between files — upstream resets by replaying the whole
    /// session (<c>session-replay</c>), which the port does not carry; identifier
    /// leakage between files of one batch is therefore possible and recorded as a
    /// divergence. <c>input-file-name</c> is added here because upstream's main
    /// loop defines it before init.ly runs, and the epilogue's version check reads
    /// it when present.
    /// </summary>
    private const string ProloguelLy = @"
#(define toplevel-scores (list))
#(define toplevel-bookparts (list))
#(define $defaultheader #f)
#(define $current-book #f)
#(define $current-bookpart #f)
#(define version-seen #f)
#(define expect-error #f)
#(define output-empty-score-list #f)
#(define output-suffix #f)
";

    /// <summary>
    /// <c>ly/init.ly</c> lines 41–45, verbatim: every file <c>-dinclude-settings</c>
    /// gathered, <c>\include</c>d BEFORE the input, in the order the switches were
    /// given.
    /// </summary>
    /// <remarks>
    /// This is the one part of <c>init.ly</c>'s lifecycle the runner had left out, and
    /// leaving it out is what made <c>include-settings</c> a declared option nothing
    /// ever read: the port drives <c>declarations-init.ly</c> rather than
    /// <c>init.ly</c> (see <see cref="LilyParserSession.LoadInitLayer"/>), so the runner
    /// owns everything <c>init.ly</c> does around <c>\maininput</c> — and this block is
    /// part of that, not part of the declarations.
    /// <para>
    /// It is parsed as its own step rather than folded into <see cref="ProloguelLy"/>
    /// so that the settings file's own diagnostics — "cannot find file" above all —
    /// carry a name that says where the include came from, and so that its errors are
    /// counted the way upstream counts them: a settings file that cannot be found is a
    /// lexer error inside <c>init.ly</c>'s own parse, which is fatal, exactly as the
    /// port's standing principle requires.
    /// </para>
    /// </remarks>
    private const string IncludeSettingsLy = @"
$(for-each
   (lambda (file)
     (ly:parser-include-string
      (format #f ""\\include \""~a\"""" file)))
   (ly:get-option 'include-settings))
";

    /// <summary>
    /// <c>ly/init.ly</c> lines 54–91, verbatim except as noted: the version check,
    /// the book construction from what the toplevel handlers collected, the handler
    /// dispatch (which finds the runner's <c>default-toplevel-book-handler</c> by
    /// <c>init.ly</c>'s own <c>defined?</c> test), and the expect-error handshake.
    /// The <c>verbose</c> gc-stats block is omitted: it prints Guile's collector
    /// statistics, which do not exist here.
    /// </summary>
    private const string EpilogueLy = @"
#(cond
  ((not (defined? 'input-file-name)))

  ((not version-seen)
   (version-not-seen-message input-file-name))

  ((ly:parser-has-error?)
   (suggest-convert-ly-message version-seen)))

#(ly:set-option 'protected-scheme-parsing #f)

#(let ((book-handler (if (defined? 'default-toplevel-book-handler)
                         default-toplevel-book-handler
                         toplevel-book-handler)))
   (cond ((pair? toplevel-bookparts)
          (let ((book (ly:make-book $defaultpaper $defaultheader)))
            (for-each (lambda (part)
                        (ly:book-add-bookpart! book part))
                      (reverse! toplevel-bookparts))
            (set! toplevel-bookparts (list))
            ;; if scores have been defined after the last explicit \bookpart:
            (if (pair? toplevel-scores)
                (for-each (lambda (score)
                            (ly:book-add-score! book score))
                          (reverse! toplevel-scores)))
            (set! toplevel-scores (list))
            (book-handler book)))
         ((or (pair? toplevel-scores) output-empty-score-list)
          (let ((book (apply ly:make-book $defaultpaper
                             $defaultheader toplevel-scores)))
            (set! toplevel-scores (list))
            (book-handler book)))))

#(if (eq? expect-error (ly:parser-has-error?))
  (ly:parser-clear-error)
  (if expect-error
   (ly:parser-error (G_ ""expected error, but none found""))))
";

    /// <summary>
    /// THE RUNNER'S OWN, and not part of <c>init.ly</c>: one read-only question asked in
    /// the ONE line before <see cref="EpilogueLy"/> runs, so that the runner knows what
    /// the epilogue is about to do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It asks exactly what the epilogue's own <c>let</c> and <c>cond</c> ask, in the same
    /// module and one statement earlier: WHICH procedure <c>init.ly</c>'s
    /// <c>defined?</c> test will hand the toplevel book to, and WHETHER there is going to
    /// be a toplevel book at all. Both have to be read HERE — the epilogue empties
    /// <c>toplevel-scores</c> and <c>toplevel-bookparts</c> as it builds the book, so
    /// afterwards the answer is always "no book".
    /// </para>
    /// <para>
    /// This is the safety net's only input, and the safety net is what makes the class of
    /// defect it guards against impossible to have again: a document may install its own
    /// toplevel book handler, and one that neither writes nor hands the book back leaves
    /// a run reporting success with nothing on disk and nothing in the log.
    /// </para>
    /// <para>
    /// It is parsed as its own step, beside <c>&lt;batch-version-seen&gt;</c>, rather than
    /// folded into the epilogue, so that the epilogue stays the verbatim extract it says
    /// it is.
    /// </para>
    /// </remarks>
    private const string BookHandlerProbeLy = @"
#(lilyport-batch-book-handler
   (if (defined? 'default-toplevel-book-handler)
       default-toplevel-book-handler
       toplevel-book-handler)
   (if (or (pair? toplevel-bookparts)
           (pair? toplevel-scores)
           output-empty-score-list)
       #t
       #f))
";

    /// <summary>
    /// Installs the two parse entry points that were waiting on this class:
    /// <c>ly:parse-file</c> (the full session lifecycle over a named file, books
    /// flowing to whatever toplevel book handler is bound) and <c>ly:parse-init</c>
    /// (a bare parse of one init file). Called by <see cref="LilyPondInit"/> once
    /// the layers are up, so the bindings exist in every real session.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void InstallSessionBindings(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // THE PROBE'S DEFAULT ANSWER-TAKER, so that BookHandlerProbeLy is never an
        // unbound name: every path through RunLifecycle parses it, including the one
        // `ly:parse-file' reaches, and only a full run (RunConfigured) has anywhere to put
        // the answer. A no-op here is the honest default — a nested parse has no safety
        // net of its own and never claimed one.
        interpreter.DefinePrimitive(
            "lilyport-batch-book-handler", 2, 2, a => Unspecified.Instance);

        interpreter.DefinePrimitive("ly:parse-file", 1, 1, a =>
        {
            string name = ArgumentText(a[0], "ly:parse-file");
            LilyParserSession session = LilyPondInit.Session();
            string path = ResolveSource(name, session.IncludePath);
            if (path == null)
            {
                throw FileFailed(name);
            }

            List<string> diagnostics = new List<string>();

            // Output name and input name are the SAME name here, and the repetition is
            // upstream's: ly:parse-file names its output from the input it was handed
            // (output_file_name_for_input_file_name), and the port has no command line
            // for a -o to have come from. The two only come apart when a HOST renames
            // an output, which is BatchRunOptions.InputName's job.
            string parsedName = Path.GetFileNameWithoutExtension(path);
            int errors = RunLifecycle(
                session,
                File.ReadAllText(path),
                parsedName,
                parsedName,
                Path.GetDirectoryName(Path.GetFullPath(path)),
                diagnostics);
            if (errors > 0)
            {
                throw FileFailed(name);
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:parse-init", 1, 1, a =>
        {
            string name = ArgumentText(a[0], "ly:parse-init");

            // Upstream builds a FRESH parser over fresh Sources for this. The port
            // has one session per process (the call-after-session guards make a
            // second init-layer load impossible), so the shared session parses the
            // file directly — recorded in PORT-COVERAGE.
            LilyParserSession session = LilyPondInit.Session();

            string text = LilyPondScheme.ReadInitFile(name);
            if (text == null)
            {
                // Upstream's `global_path.find (file)' — the working directory and then
                // the include path, and NO extension list, which is the one thing that
                // separates this entry point from ly:parse-file. The vendored layer keeps
                // its place in front, which is the port's recorded shadowing divergence.
                string path = ResolveOnPath(name, session.IncludePath);
                if (path != null)
                {
                    text = File.ReadAllText(path);
                }
            }

            if (text == null)
            {
                throw FileFailed(name);
            }

            ParseOutcome outcome = session.ParseText(text, name);
            if (!outcome.Success)
            {
                throw FileFailed(name);
            }

            return Unspecified.Instance;
        });
    }

    private static string ArgumentText(object value, string procedureName)
        => value is MutableString || value is string
            ? CodeBrix.LilyScheme.Primitives.StringPrimitives.Text(value, procedureName)
            : throw new ArgumentException(procedureName + ": expected a string file name");

    /// <summary>
    /// Finds a <c>ly:parse-file</c> argument the way upstream's
    /// <c>global_path.find (file, input_extensions)</c> does: the extension loop is the
    /// OUTER one, so <c>name.ly</c> is looked for in the working directory and in every
    /// include-path entry before bare <c>name</c> is looked for in any of them.
    /// <para>
    /// ⚠ TWO DIVERGENCES THIS CLOSES, both MEASURED on the pinned 2.27.2. The extension
    /// used to be tried SECOND and only in the working directory, so a directory holding
    /// both <c>x</c> and <c>x.ly</c> opened the one upstream does not; and the include
    /// path was not searched at all, so <c>(ly:parse-file "sub")</c> threw
    /// <c>ly-file-failed</c> with <c>sub.ly</c> sitting on the path, where the oracle
    /// parses it.
    /// </para>
    /// <para>
    /// ⚠ The engine carries a twin of this resolver for a session that never loads this
    /// facade (<c>ParserPrimitives.ResolveWithInputExtensions</c>), because this class
    /// REBINDS <c>ly:parse-file</c> over the engine's. The two move together.
    /// </para>
    /// </summary>
    /// <param name="name">The file name as written.</param>
    /// <param name="includePath">
    /// The session's include path, which is the port's <c>global_path</c>.
    /// </param>
    /// <returns>The resolved path, or <see langword="null"/>.</returns>
    private static string ResolveSource(string name, IReadOnlyList<string> includePath)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        Flower.FileName fileName = new Flower.FileName(name);
        string originalExtension = fileName.Extension;

        foreach (string extension in InputExtensions)
        {
            // Upstream joins the extension onto whatever the name already carried,
            // separated by a dot only when BOTH are non-empty: `sub' asks for `sub.ly'
            // and then `sub', while `sub.ly' asks for `sub.ly.ly' and then `sub.ly'. The
            // second pass is what keeps a fully spelled name working.
            fileName.Extension = originalExtension;
            if (extension.Length != 0 && fileName.Extension.Length != 0)
            {
                fileName.Extension += ".";
            }

            fileName.Extension += extension;

            string found = ResolveOnPath(fileName.ToString(), includePath);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// The extensions <c>ly:parse-file</c> searches, in upstream's order —
    /// <c>lily-parser-scheme.cc</c>'s <c>input_extensions</c>. <c>ly:parse-init</c> passes
    /// NONE, and that is not an oversight: MEASURED on 2.27.2, <c>(ly:parse-init "sub")</c>
    /// does not open <c>sub.ly</c> where <c>(ly:parse-file "sub")</c> does.
    /// </summary>
    private static readonly string[] InputExtensions = { "ly", string.Empty };

    /// <summary>
    /// Searches one exact name the way upstream's <c>File_path::find (name)</c> does — the
    /// working directory, then each include-path entry in turn.
    /// </summary>
    /// <param name="name">The file name to look for, extension already decided.</param>
    /// <param name="includePath">
    /// The session's include path, which is the port's <c>global_path</c>.
    /// </param>
    /// <returns>The path found, or <see langword="null"/>.</returns>
    private static string ResolveOnPath(string name, IReadOnlyList<string> includePath)
    {
        if (File.Exists(name))
        {
            return name;
        }

        if (includePath != null)
        {
            foreach (string directory in includePath)
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static Exception FileFailed(string name)
        => new CodeBrix.LilyScheme.Runtime.SchemeThrow(
            Symbol.Intern("ly-file-failed"),
            Pair.List(new MutableString(name)));

    /// <summary>Runs one <c>.ly</c> file, writing its SVG beside nothing — into
    /// <paramref name="outputDirectory"/> under the file's base name.</summary>
    /// <param name="filePath">The <c>.ly</c> file to run.</param>
    /// <param name="outputDirectory">Where the <c>.svg</c> lands.</param>
    /// <returns>What the run produced and reported.</returns>
    public static BatchRunResult RunFile(string filePath, string outputDirectory)
        => RunFile(filePath, outputDirectory, null);

    /// <summary>
    /// Runs one <c>.ly</c> file, writing its output into
    /// <paramref name="outputDirectory"/> under <paramref name="outputBaseName"/>.
    /// </summary>
    /// <param name="filePath">The <c>.ly</c> file to run.</param>
    /// <param name="outputDirectory">Where the <c>.svg</c> lands.</param>
    /// <param name="outputBaseName">
    /// The output base name, or <see langword="null"/> to take the input file's own.
    /// </param>
    /// <returns>What the run produced and reported.</returns>
    /// <remarks>
    /// The named-output form is what <c>lilypond -o</c> gives a caller:
    /// <c>output_file_name_for_input_file_name</c>
    /// (<c>lily-parser-scheme.cc:37-60</c>) uses <c>output_name_global</c> when it is set
    /// and the input's base name otherwise, and it does NOT strip an extension from the
    /// named form the way it does from the derived one. Pair it with
    /// <see cref="SplitOutputName"/>, which is the half that turns one <c>-o</c> value
    /// into a directory and a name.
    /// </remarks>
    public static BatchRunResult RunFile(
        string filePath, string outputDirectory, string outputBaseName)
        => RunFile(filePath, outputDirectory, outputBaseName, null);

    /// <summary>
    /// Runs one <c>.ly</c> file with a host's per-run adjustments.
    /// </summary>
    /// <param name="filePath">The <c>.ly</c> file to run.</param>
    /// <param name="outputDirectory">Where the <c>.svg</c> lands.</param>
    /// <param name="outputBaseName">
    /// The output base name, or <see langword="null"/> to take the input file's own.
    /// </param>
    /// <param name="runOptions">
    /// The run's adjustments, or <see langword="null"/> for none.
    /// </param>
    /// <returns>What the run produced and reported.</returns>
    public static BatchRunResult RunFile(
        string filePath, string outputDirectory, string outputBaseName,
        BatchRunOptions runOptions)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        // THE INPUT'S OWN NAME, WHICH IS NOT THE OUTPUT'S once -o renames. Filled in
        // here because this is the overload that HAS a file to take it from; RunText's
        // callers hand it text and one name, and for them the two are the same name.
        // Upstream keeps output_name_global out of everything that reports what was
        // READ (BatchRunOptions.InputName carries the measurement).
        string inputName = Path.GetFileNameWithoutExtension(filePath);
        BatchRunOptions options = runOptions;
        if (!string.IsNullOrEmpty(outputBaseName) && outputBaseName != inputName)
        {
            // The caller's own options object is not written to: a host may reuse one
            // across runs of different files, and the name belongs to THIS run.
            options = CopyWithInputName(runOptions, inputName);
        }

        return RunText(
            File.ReadAllText(filePath),
            string.IsNullOrEmpty(outputBaseName)
                ? inputName
                : outputBaseName,
            Path.GetDirectoryName(Path.GetFullPath(filePath)),
            outputDirectory,
            options);
    }

    /// <summary>
    /// Copies a caller's run options with <see cref="BatchRunOptions.InputName"/> filled
    /// in, leaving the caller's own object untouched.
    /// </summary>
    /// <param name="runOptions">The caller's options, or <see langword="null"/>.</param>
    /// <param name="inputName">The input file's base name, without extension.</param>
    /// <returns>The options to run with.</returns>
    private static BatchRunOptions CopyWithInputName(
        BatchRunOptions runOptions, string inputName)
        => new BatchRunOptions
        {
            InputName = inputName,
            PointAndClick = runOptions?.PointAndClick,
            Options = runOptions?.Options,
            MessageWriter = runOptions?.MessageWriter,
            CancellationToken = runOptions?.CancellationToken ?? CancellationToken.None,
        };

    /// <summary>
    /// Splits one <c>--output</c>/<c>-o</c> value into the directory the run writes into
    /// and the base name it writes under — upstream's <c>main.cc:729-761</c>.
    /// </summary>
    /// <param name="outputName">
    /// The <c>-o</c> value, or <see langword="null"/>/empty when none was given.
    /// </param>
    /// <param name="directory">
    /// Receives the directory part, or <see langword="null"/> when the value names none
    /// (the caller then keeps whatever directory it would have used).
    /// </param>
    /// <param name="baseName">
    /// Receives the file part, or <see langword="null"/> when the value names none (the
    /// caller then derives the name from the input file).
    /// </param>
    /// <remarks>
    /// <para>
    /// ⚠ AN EXISTING DIRECTORY IS TAKEN AS ONE, AND THAT TEST IS ON THE FILE SYSTEM, not
    /// on a trailing separator: upstream asks <c>is_dir (output_name_global)</c> first
    /// and only splits when the answer is no. So <c>-o out</c> writes
    /// <c>out/&lt;input&gt;.svg</c> when <c>out</c> exists and <c>./out.svg</c> when it
    /// does not, and both are correct.
    /// </para>
    /// <para>
    /// The file part keeps its extension, because upstream's <c>File_name::file_part</c>
    /// rejoins <c>base</c> and <c>ext</c> and the <c>--output</c> path is the one arm of
    /// <c>output_file_name_for_input_file_name</c> that does NOT clear <c>ext_</c>. So
    /// <c>-o name.pdf</c> really does engrave to <c>name.pdf.svg</c>; that is upstream's
    /// behaviour and reproducing it is rule 2.
    /// </para>
    /// <para>
    /// A directory part of <c>"."</c> is dropped, which is upstream's
    /// <c>dir != "."</c> guard: it is what keeps <c>-o ./name</c> from being a different
    /// instruction from <c>-o name</c>.
    /// </para>
    /// </remarks>
    public static void SplitOutputName(
        string outputName, out string directory, out string baseName)
    {
        directory = null;
        baseName = null;

        if (string.IsNullOrEmpty(outputName))
        {
            return;
        }

        if (Directory.Exists(outputName))
        {
            directory = outputName;
            return;
        }

        string directoryPart = Path.GetDirectoryName(outputName);
        string filePart = Path.GetFileName(outputName);

        if (!string.IsNullOrEmpty(directoryPart) && directoryPart != ".")
        {
            directory = directoryPart;
        }

        if (!string.IsNullOrEmpty(filePart))
        {
            baseName = filePart;
        }
    }

    /// <summary>Runs one file's text through the full pipeline.</summary>
    /// <param name="text">The LilyPond source.</param>
    /// <param name="baseName">The output base name, without extension.</param>
    /// <param name="includeDirectory">
    /// The directory the file's own <c>\include</c>s resolve against, or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="outputDirectory">Where the <c>.svg</c> lands.</param>
    /// <returns>What the run produced and reported.</returns>
    public static BatchRunResult RunText(
        string text,
        string baseName,
        string includeDirectory,
        string outputDirectory)
        => RunText(text, baseName, includeDirectory, outputDirectory, null);

    /// <summary>Runs one file's text through the full pipeline, with a host's per-run
    /// adjustments.</summary>
    /// <param name="text">The LilyPond source.</param>
    /// <param name="baseName">The output base name, without extension.</param>
    /// <param name="includeDirectory">
    /// The directory the file's own <c>\include</c>s resolve against, or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="outputDirectory">Where the <c>.svg</c> lands.</param>
    /// <param name="runOptions">
    /// The run's adjustments, or <see langword="null"/> for none.
    /// </param>
    /// <returns>What the run produced and reported.</returns>
    public static BatchRunResult RunText(
        string text,
        string baseName,
        string includeDirectory,
        string outputDirectory,
        BatchRunOptions runOptions)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (baseName == null)
        {
            throw new ArgumentNullException(nameof(baseName));
        }

        if (outputDirectory == null)
        {
            throw new ArgumentNullException(nameof(outputDirectory));
        }

        lock (Gate)
        {
            BatchRunResult result = null;
            Interpreter.RunWithLargeStack(() => result = RunLocked(
                text, baseName, includeDirectory, outputDirectory, runOptions));
            return result;
        }
    }

    private static BatchRunResult RunLocked(
        string text,
        string baseName,
        string includeDirectory,
        string outputDirectory,
        BatchRunOptions runOptions)
    {
        // Both layers, cached; this is also what guarantees an ambient interpreter.
        OutputDef defaultLayout = LilyPondInit.DefaultLayout();

        // One process per input file is what upstream gets for free and a batch runner
        // has to arrange. Without this, `#(set-global-staff-size 30)` in one regression
        // file rescales every file engraved after it.
        int orphansBefore = LilyPondInit.Session()?.OrphanScopesDiscarded ?? 0;
        LilyPondInit.RestoreDefaults();

        // Reported AFTER the message writer is swapped in, further down: at this point the
        // writer is still the previous run's, and a warning emitted here lands in the
        // previous file's log — which is exactly where a reader would not look for it.
        int orphansDiscarded = (LilyPondInit.Session()?.OrphanScopesDiscarded ?? 0) - orphansBefore;

        // The host's adjustments go on AFTER the restore, so they hold for exactly this
        // run and the NEXT run's restore takes them off again — the lifetime a
        // per-process option has upstream. The message writer is swapped the same way
        // and put back in the finally, wrapped in a LineTrackingWriter so the per-file
        // boundary's EndOpenLine keeps working.
        CancellationToken cancellationToken
            = runOptions?.CancellationToken ?? CancellationToken.None;
        cancellationToken.ThrowIfCancellationRequested();

        if (runOptions?.Options != null)
        {
            foreach (string option in runOptions.Options)
            {
                CommandLineOptions.Apply(LilyPondScheme.Options, option);
            }
        }

        // After the list, so a host that sets both gets the typed property's value --
        // and the regression harness, which sets only this one, is unaffected either
        // way.
        if (runOptions?.PointAndClick != null)
        {
            LilyPondScheme.Options.Set("point-and-click", runOptions.PointAndClick);
        }

        // THE SCHEME ERROR PORT GOES WITH THE DIAGNOSTIC WRITER, AND IT IS THE SAME
        // OBJECT. LilyPondScheme.CreateInterpreter assigns
        // `interpreter.ErrorWriter = Flower.Warn.Output' once, at creation, because
        // ruling R17 requires the two to be one object: neither can otherwise tell
        // whether the other left a line open. Swapping only Flower.Warn.Output for the
        // run made them two objects for the duration of the run, and everything Scheme
        // wrote to the error port — `ly:font-config-display-fonts' prints its whole
        // font world there — bypassed the job's log and landed on the process console.
        TextWriter previousOutput = null;
        TextWriter previousErrorWriter = null;
        Interpreter errorPortInterpreter = null;
        bool outputSwapped = false;
        if (runOptions?.MessageWriter != null)
        {
            Flower.LineTrackingWriter tracking
                = new Flower.LineTrackingWriter(runOptions.MessageWriter);
            previousOutput = Flower.Warn.Output;
            Flower.Warn.Output = tracking;

            // DefaultLayout() ran at the top of this method and that is what guarantees
            // an ambient interpreter, so this is never null in a real run; the test is
            // for the caller who reaches the runner before the layers are up.
            errorPortInterpreter = LilyPondScheme.Current;
            if (errorPortInterpreter != null)
            {
                previousErrorWriter = errorPortInterpreter.ErrorWriter;
                errorPortInterpreter.ErrorWriter = tracking;
            }

            outputSwapped = true;
        }

        try
        {
            return RunConfigured(
                defaultLayout, text, baseName,
                string.IsNullOrEmpty(runOptions?.InputName) ? baseName : runOptions.InputName,
                includeDirectory, outputDirectory,
                cancellationToken, orphansDiscarded);
        }
        finally
        {
            if (outputSwapped)
            {
                (Flower.Warn.Output as Flower.LineTrackingWriter)?.EndOpenLine();
                Flower.Warn.Output = previousOutput;

                // Put back the object the interpreter was created with, which is the
                // object Flower.Warn.Output has just been put back to — R17 holds
                // outside a run as well as inside one.
                if (errorPortInterpreter != null)
                {
                    errorPortInterpreter.ErrorWriter = previousErrorWriter;
                }
            }
        }
    }

    private static BatchRunResult RunConfigured(
        OutputDef defaultLayout,
        string text,
        string baseName,
        string inputName,
        string includeDirectory,
        string outputDirectory,
        CancellationToken cancellationToken,
        int orphanScopesDiscarded)
    {
        Interpreter interpreter = LilyPondScheme.Current;

        List<string> diagnostics = new List<string>();

        // EVERY BOOK THE FILE HANDED TO A TOPLEVEL BOOK HANDLER, in the order it handed
        // them over, whichever handler took it: the runner's own collector (an ordinary
        // document) or the document's, through ly:book-process / ly:book-process-to-systems
        // (a document that installed one, which is what lilypond-book-preamble.ly does).
        // ONE list, because the two must share ONE name counter — see PendingBook.
        //
        // The name each book prints under is captured AS IT IS HANDED OVER.
        // ⚠ Upstream calls get-outfile-name from print-book-with, at the moment the
        // toplevel handler takes the book — DURING the parse — and the timing is
        // load-bearing, because `output-suffix' is an ordinary toplevel variable that a
        // file may set, print under, and reset. Naming the books after the parse reads
        // whatever value the file happened to END on:
        // book-change-global-staffsize-abs-fonts sets "standard-size", prints its first
        // book, then sets #f and prints its second, so both would be named from #f.
        List<PendingBook> pending = new List<PendingBook>();

        // get-outfile-name's `counter-alist', keyed as upstream keys it: the base name
        // concatenated with the suffix. The counter is per KEY, NOT a running book index.
        Dictionary<string, int> outfileCounters = new Dictionary<string, int>();

        // Assigned below, before anything can call the collector; captured here so the
        // collector can do its own $defaultpaper and toplevel lookups at PRINT time.
        LilyParserSession collectingSession = null;

        // init.ly's own escape hatch, and D20's interception point: when this name is
        // bound, init.ly's epilogue hands each finished book HERE instead of to
        // ly:book-process. Rebound per run so a stale capture list can never leak
        // between runs.
        Primitive defaultBookCollector = interpreter.DefinePrimitive(
            "default-toplevel-book-handler", 1, 1, a =>
        {
            if (a[0] is Book book)
            {
                pending.Add(new PendingBook(
                    GetOutfileName(collectingSession, book, baseName, outfileCounters),
                    book));
            }
            else
            {
                diagnostics.Add("toplevel book handler received a non-book: "
                    + (a[0]?.GetType().Name ?? "null"));
            }

            return Unspecified.Instance;
        });

        // The escape hatch above only catches the book init.ly's EPILOGUE builds from
        // implicit toplevel scores. An EXPLICIT \book block never gets there: the parser
        // hands it to `toplevel-book-handler' AT PARSE TIME, and the vendored binding
        // (declarations-init.ly: print-book-with-defaults → ly:book-process) computes the
        // pages and DISCARDS them — upstream's ly:book-process writes the output files
        // itself, this port collects pages off the paper book in the caller. So the
        // runner rebinds the parse-time name to the same collector, exactly the move
        // upstream's own lilypond-book-preamble.ly makes when IT wants books collected
        // instead of printed. Rebound per run for the same no-stale-capture reason as
        // the escape hatch. Before this, every explicit \book file was
        // NOOUT: "0 book(s)" with the book fully built — the sequence-name* MIDI rows.
        //
        // ⚠ The interpreter define alone is NOT ENOUGH: the parser resolves
        // `toplevel-book-handler' through ITS scope stack (Lily_lexer semantics), where
        // declarations-init.ly's #(define ...) landed — so the same procedure must also
        // be SET as a parser identifier, which happens below once the session is in hand.
        Primitive bookCollector = interpreter.DefinePrimitive("toplevel-book-handler", 1, 1, a =>
        {
            if (a[0] is Book book)
            {
                pending.Add(new PendingBook(
                    GetOutfileName(collectingSession, book, baseName, outfileCounters),
                    book));
            }
            else
            {
                diagnostics.Add("toplevel book handler received a non-book: "
                    + (a[0]?.GetType().Name ?? "null"));
            }

            return Unspecified.Instance;
        });

        // THE SECOND INTERCEPTION POINT, AND THE ONE THE DOCUMENT CHOOSES.
        //
        // Both collectors above are DEFAULTS. `ly/init.ly' lets a document install its own
        // toplevel book handler and the port must honour that, because upstream does:
        // `ly/lilypond-book-preamble.ly' — every lilypond-book document opens by including
        // it, and four of Fresco.Brix's six shipped font templates do — points
        // `default-toplevel-book-handler' at `print-book-with-defaults-as-systems' and
        // `toplevel-book-handler' at a lambda calling `print-book-with-defaults'. The
        // document's handlers then WIN, exactly as init.ly intends, and the two collectors
        // above never run.
        //
        // ⚠ THAT IS THE WHOLE DEFECT THIS HOOK CLOSES. Upstream's two `ly:book-process'
        // entry points WRITE the output files (`Paper_book::output' and `classic_output');
        // the port's compute the book and hand the paper book back, because output is
        // written HERE, by the caller. So every book a document routed to its own handler
        // was engraved and then dropped: "Completed successfully", zero errors, no file
        // anywhere. A six-line file with the preamble include and two bars of music
        // produced nothing, where the oracle writes its SVG.
        //
        // Observing the two primitives — rather than rebinding them — keeps ONE
        // implementation of the processing itself, and keeps the runner out of the
        // question of which module `print-book-with' resolves `ly:book-process' through.
        // The book is named HERE, from the runner's own counter, so that a file mixing an
        // explicit \book (collected above) with a document-handled toplevel book numbers
        // them as upstream's single `counter-alist' does instead of giving both the base
        // name and letting the second overwrite the first.
        BookProcessObserver.ProcessedBook previousObserver = BookProcessObserver.Current;
        BookProcessObserver.Current = (book, paperBook, toSystems) =>
            pending.Add(new PendingBook(
                GetOutfileName(collectingSession, book, baseName, outfileCounters),
                book,
                paperBook,
                toSystems));

        // THE SAFETY NET'S THREE FACTS, filled in by BookHandlerProbeLy one statement
        // before init.ly's epilogue dispatches — see that constant. They are read back
        // after the parse to decide whether a book went to a handler and was never heard
        // of again.
        object chosenBookHandler = null;
        bool epilogueBuildsBook = false;
        int pendingBeforeEpilogue = 0;
        interpreter.DefinePrimitive("lilyport-batch-book-handler", 2, 2, a =>
        {
            chosenBookHandler = a[0];
            epilogueBuildsBook = SchemeUtilities.IsSchemeTrue(a[1]);
            pendingBeforeEpilogue = pending.Count;
            return Unspecified.Instance;
        });

        // THE session — the one the init layer was read into. A second session would
        // trip the interpreter's call-after-session guards, exactly as a second
        // init.ly run would upstream.
        LilyParserSession session = LilyPondInit.Session();
        collectingSession = session;

        // The parser half of the toplevel-book-handler rebind (see the note on the
        // collector above). RestoreDefaults ran at the top of this method, so this
        // per-run identifier survives the whole file and the NEXT run's restore wipes
        // it before its own re-set — no stale capture list can leak.
        session.SetIdentifier(Symbol.Intern("toplevel-book-handler"), bookCollector);

        // The two lines upstream's `ly:parse-file' prints before it parses anything
        // (`lily-parser-scheme.cc:112,116'): the file it opened, at BASIC, and "Parsing..."
        // at INFO. This runner replaces that driver (trap 17f), so it owes them; until the
        // log level was upstream's they could not have been seen anyway.
        Flower.Warn.BasicProgress("Processing `" + ResolvedInputName(inputName, includeDirectory)
            + "'");
        Flower.Warn.Message("Parsing...");

        // An earlier file in this session died inside a \layout, \midi, \paper or \with
        // block and left its scope on the stack; RestoreDefaults has just discarded it, so
        // THIS run starts from the base scope as upstream's fresh parser would. Said out
        // loud rather than corrected silently: while the orphan was on the stack, every
        // toplevel assignment landed in it instead of the base scope, which is how one
        // file's `"\\<" = ...' escaped into later files (the ELEVENTH LEAK).
        if (orphanScopesDiscarded > 0)
        {
            Flower.Warn.Warning("discarded " + orphanScopesDiscarded + " scope(s) left open by an"
                + " earlier file in this session; that file's parse died inside a \\layout,"
                + " \\midi, \\paper or \\with block");
        }

        int errorCount;
        try
        {
            errorCount = RunLifecycle(
                session, text, baseName, inputName, includeDirectory, diagnostics);
        }
        finally
        {
            // The observer's whole life is the PARSE: every book reaches a toplevel
            // handler while the file is being read, and the loop below processes what the
            // runner's own collector took without going near ly:book-process. Put back
            // rather than cleared, so a run nested inside another leaves the outer run's
            // observer exactly as it found it.
            BookProcessObserver.Current = previousObserver;
        }

        // What the lexer recorded off the MAIN input's \version statement, for the
        // host: an editor decides whether to offer convert-ly from exactly this
        // string, and reading it back off the run costs nothing.
        string declaredVersion = string.IsNullOrEmpty(session.MainInputVersionString)
            ? null
            : session.MainInputVersionString;

        // One stencil per PAGE, as the page breaker chose them.
        // Until this group it was one per SCORE, stacked at a fixed padding into a single
        // document per input file -- which is why every multi-page reference page in the
        // oracle read as MISSING no matter how well the port engraved it.
        //
        // GROUPED PER BOOK: upstream names output PER
        // TOPLEVEL BOOK — scm/lily-library.scm's get-outfile-name — and only within one
        // book's output does the SVG framework number the pages. Concatenating every
        // book's pages under one name mispaired a file holding both toplevel content
        // and an explicit \book against the oracle (header-book-multiplescores).
        List<BookOutput> bookOutputs = new List<BookOutput>();

        // How many LINES the scores broke into, which is not how many scores there are.
        // Until line breaking landed this figure was systems.Count -- one per
        // score -- and every sweep log in the project reported it under the name
        // "system(s)". It read as a line count and was not one: accidental-styles.ly has
        // twenty scores and reported twenty systems before line breaking existed at all.
        int lines = 0;
        int skipped = 0;
        // THE PARSER STAYS CURRENT ACROSS ENGRAVING.
        //
        // Upstream never leaves the parser's dynamic extent to engrave: book processing
        // is reached from `default-toplevel-book-handler', which the PARSER calls, so
        // ly:parser-lookup and ly:parser-clone answer normally all the way down. D20's
        // score-level short-circuit moved the port's engraving OUT of that extent, and
        // The harness sweep measured what it cost: 18 files died on "there is no current
        // parser", because \markup \note asks for $defaultpaper while BUILDING ITS
        // STENCIL.
        //
        // ⚠ THIS IS NOT A STAND-IN AND IT DOES NOT RETIRE. The page-layout work inherited
        // it on the premise that moving the runner onto the real ly:book-process
        // path would make it unnecessary. That premise is WRONG and was MEASURED
        // wrong: with the runner fully on the book path, removing this wrapper puts
        // apply-output, fermata-dot-position, markup-rhythm-ragged and
        // flags-straight-layout-staff-size straight back on "there is no current parser".
        // The reason is that the book path is not the parser's DYNAMIC EXTENT. Upstream
        // reaches book processing from default-toplevel-book-handler, which the parser
        // calls WHILE PARSING, so %parser is live by construction; this runner collects
        // books during the parse and processes them after it, so it must publish %parser
        // itself. AsCurrentParser is upstream's own fluid, not a workaround. Retiring it
        // would mean processing each book from inside the toplevel handler, which is a
        // different design decision and not a clean-up.
        // THE LAYOUT IS RESOLVED BY NAME HERE, NOT CAPTURED BEFORE THE PARSE.
        //
        // `print-book-with' (scm/lily-library.scm) does (ly:parser-lookup '$defaultlayout)
        // at BOOK-PROCESSING time and hands the answer to ly:book-process, so a toplevel
        // \layout block is in place by then: parser.yy's toplevel_expression REBINDS the
        // $defaultlayout IDENTIFIER to the new definition rather than mutating the old
        // one. Reading the cached init-layer object instead — which is what this once
        // did — silently discarded every toplevel \layout in the suite:
        // \consists still worked, because a translator list is read off the definition
        // the layout block built, but no property operation from it ever ran, so
        // `\layout { \context { \Score scriptDefinitions = ... } }' set nothing at all.
        // Same shape as the earlier $defaultpaper finding, one identifier over.
        OutputDef parsedLayout
            = session.LookupIdentifier(DefaultLayoutName) as OutputDef ?? defaultLayout;

        // $defaultpaper is resolved BY NAME for the same reason $defaultlayout is: a
        // toplevel \paper block REBINDS the identifier rather than mutating the object,
        // so the one captured before the parse is the wrong one. It is what a book with
        // no \paper of its own falls back to.
        OutputDef parsedPaper = session.LookupIdentifier(DefaultPaperName) as OutputDef;

        session.AsCurrentParser(() =>
        {
        for (int bookIndex = 0; bookIndex < pending.Count; bookIndex++)
        {
            // A book is one uninterruptible engine call, so between books is the
            // finest grain cancellation can honestly have here.
            cancellationToken.ThrowIfCancellationRequested();

            Book book = pending[bookIndex].Book;

            // THE REAL ly:book-process PATH. D20's score-level
            // short-circuit is RETIRED: this used to walk the book's scores and hand each
            // one to LilyPortEngraver, producing one stacked drawing per input file. It
            // now runs Book::process, which builds a Paper_book, scales and normalizes its
            // paper, renders every score through Score::book_rendering, and then lets the
            // paper block's own `page-breaking' procedure choose the pages.
            //
            // The scaling that used to happen here is gone because Paper_book's
            // CONSTRUCTOR does it -- doing it twice would scale the paper squared.
            try
            {
                // A BOOK THE DOCUMENT'S OWN HANDLER ALREADY PROCESSED IS NOT PROCESSED
                // AGAIN. It came through ly:book-process or ly:book-process-to-systems
                // DURING the parse, which is where upstream reaches book processing from
                // too, and it arrives here with its half already forced: the pages for
                // the page path, the systems for the systems path. Re-running
                // Book::process would engrave the whole book a second time and answer a
                // different paper book from the one the document's handler was handed.
                PaperBook paperBook = pending[bookIndex].Processed
                    ?? book.Process(parsedPaper, parsedLayout);
                if (paperBook == null)
                {
                    diagnostics.Add("book produced no paper book");
                    continue;
                }

                // Paper_book::output's own first step, and it must run BEFORE the pages
                // are forced: it walks the bookparts telling each where its page numbers
                // start and whether it is the last, and page.scm reads both off the paper
                // while it is BUILDING each page. Asking for Pages() first bakes in the
                // unset values. A book the document's handler processed has had the step
                // its own path calls for already — Paper_book::output for the page path
                // and nothing at all for classic_output, which never numbers pages.
                if (pending[bookIndex].Processed == null)
                {
                    paperBook.Output();
                }

                // PER BOOK, not once per file. framework-svg.scm's output-framework
                // calls (set-unit-length (ly:output-def-lookup layout 'output-scale))
                // for the book it is about to write, and a file may hold books at
                // DIFFERENT global staff sizes — book-change-global-staffsize-abs-fonts
                // is two books, 20pt then 10pt, from one source. Keeping one value for
                // the whole file wrote every book with the LAST book's scale, which the
                // backend divides font sizes by: the 20pt book came out with every text
                // font-size exactly doubled while its geometry stayed right, so it read
                // as a glyph-inventory difference rather than as a layout error.
                double unitLength = paperBook.Paper.GetDimension("output-scale");

                // ONE STENCIL PER FILE-TO-BE, and WHICH stencils is the whole difference
                // between upstream's two output halves: Paper_book::output writes the
                // PAGES and Paper_book::classic_output writes the SYSTEMS. Both then go
                // through the SAME framework-svg.scm `output-stencils', which is why one
                // writer serves both here.
                List<Stencil> bookPages = pending[bookIndex].ToSystems
                    ? SystemStencils(paperBook, diagnostics)
                    : PageStencils(paperBook);

                // THE PERFORMANCES BELONG TO THE BOOK THAT PRODUCED THEM, and that is what
                // names their files: upstream reaches write-performances-midis from
                // `Paper_book::output', which is called once PER BOOK with the name
                // get-outfile-name computed for THAT book (lily/paper-book.cc::output ->
                // scm/midi.scm::write-performances-midis). Collected into one flat list
                // for the whole file they were named from the INPUT's base name with one
                // running counter, which is right only while a file holds a single book.
                List<Performance> bookPerformances = new List<Performance>();
                foreach (object performance in Pair.ToList(paperBook.Performances()))
                {
                    if (performance is Performance performed)
                    {
                        bookPerformances.Add(performed);
                    }
                }

                // Both figures are read AFTER Pages(): `first-page-number' is not
                // necessarily the one the paper block asked for. Page_turn_page_breaking's
                // make_pages WRITES it back, because with auto-first-page-number the
                // breaker may start the book on page 2 to avoid a bad turn -- and
                // output-stencils then names the FILES from it.
                bookOutputs.Add(new BookOutput(
                    pending[bookIndex].Name ?? baseName,
                    bookPages,
                    SchemeConvert.ToInt(paperBook.Paper.CVariable("first-page-number"), 1),
                    unitLength,
                    bookPerformances));

                lines += pending[bookIndex].ToSystems
                    ? Pair.ToList(paperBook.Systems()).Count
                    : CountLines(paperBook);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                diagnostics.Add("book processing failed: " + exception.Message);
            }
        }

            return Unspecified.Instance;
        });

        // The page and performance naming rules — the ORACLE's, so the comparator pairs a
        // candidate with a reference by name alone — are in WriteBookPages and
        // WritePerformances below, with the file names they produce.
        //
        //was previously: both writers composed `Path.Combine (outputDirectory, name)' and
        // handed the ENGINE the result. Upstream's engine never sees a directory —
        // `main.cc:729-761' splits --output into a directory and a file part, prepends the
        // old working directory to global_path, prints "Changing working directory to:",
        // chdir's, and reduces the output name to the BARE file part, and
        // `lily-parser-scheme.cc:40-42' states the consequence as a contract: "Output name
        // is treated simply as a file name because any directory part should have been
        // handled in main ()." Because the port broke that contract,
        // `Performance.WriteOutput' faithfully printed an ABSOLUTE path in
        // "cannot create a zero-track MIDI file; skipping `%s'" and in "MIDI output to
        // `%s'" — one graded diagnostics row (skiptypesetting-all-true-midi) and 65 files'
        // worth of the other.
        //
        // The port takes the output directory as the working directory for the span in
        // which output is WRITTEN, and no longer. The engraving that came before ran in
        // the driver's per-file scratch directory, and that is what keeps a file's SIDE
        // files out of the output directory; the oracle harness gets the same isolation
        // from a fresh temp directory per process.
        string outputRoot = Path.GetFullPath(outputDirectory);
        string svgPath = null;
        List<string> svgPaths = new List<string>();
        List<string> midiPaths = new List<string>();
        cancellationToken.ThrowIfCancellationRequested();
        if (bookOutputs.Exists(
                output => output.Pages.Count > 0 || output.Performances.Count > 0))
        {
            Directory.CreateDirectory(outputRoot);
            string previousDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(outputRoot);
            try
            {
                WriteBookPages(bookOutputs, svgPaths, diagnostics);
                WritePerformances(bookOutputs, midiPaths, diagnostics);
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }

            for (int i = 0; i < svgPaths.Count; i++)
            {
                svgPaths[i] = Path.Combine(outputRoot, svgPaths[i]);
            }

            for (int i = 0; i < midiPaths.Count; i++)
            {
                midiPaths[i] = Path.Combine(outputRoot, midiPaths[i]);
            }

            svgPath = svgPaths.Count > 0 ? svgPaths[0] : null;
        }

        // THE SAFETY NET. A book went to a toplevel book handler and nothing came back:
        // the run would otherwise say "Completed successfully", count zero errors, and
        // leave no file and no explanation anywhere — which is exactly how the
        // lilypond-book-preamble defect cost two agents an afternoon.
        //
        // It is a WARNING and not an error on purpose: upstream honours whatever handler
        // the document installed and says nothing when that handler chooses to write
        // nothing, so failing here would make the port STRICTER than 2.27.2, which it is
        // never allowed to be. What the port owes is the sentence, not the exit code.
        //
        // The two conditions are deliberately narrow, so that a legitimately silent run
        // stays silent: a book with no scores (upstream writes nothing for it either),
        // and a book that produces only MIDI (its file IS written), both reach the runner
        // and are accounted for.
        ReportUnwrittenBooks(
            pending,
            bookOutputs,
            svgPaths.Count + midiPaths.Count,
            chosenBookHandler,
            defaultBookCollector,
            bookCollector,
            epilogueBuildsBook && pending.Count == pendingBeforeEpilogue,
            baseName);

        // Where upstream calls it: lily.scm runs (ly:check-expected-warnings) between
        // (lilypond-file handler x) and (session-terminate). A file that registered an
        // expectation with ly:expect-warning and never triggered it says so HERE, and the
        // list is cleared either way — which is also what keeps one file's expectation
        // from suppressing the NEXT file's warning in a batch run (trap 16).
        //
        // ⚠ IT BELONGS AT THE END OF THE FILE'S WORK, NOT AFTER THE PARSE. Placed
        // straight after RunLifecycle it fired before engraving had run, so
        // tie-unterminated reported its expected warning MISSING and then emitted it one
        // line later. Upstream's lilypond-file parses AND engraves AND writes before the
        // check is reached; this runner collects books during the parse and processes
        // them afterwards, so the equivalent point is here.
        Flower.Warn.CheckExpectedWarnings();

        // THE PER-FILE BOUNDARY closes any line the file's output left open. Upstream
        // gets this for free — its per-file process exits and the stream ends — and
        // R17 puts formatting fixes at exactly this boundary in scope. Without it, a
        // file whose LAST output is a phase marker (message() leaves its line OPEN,
        // as upstream's does) would have the driver's result line glued onto it, and
        // the diagnostics comparator attributes everything since the previous result
        // line, so one glued line mis-files a whole file's diagnostics.
        (Flower.Warn.Output as Flower.LineTrackingWriter)?.EndOpenLine();

        return new BatchRunResult(
            svgPath,
            pending.Count,
            lines,
            skipped,
            errorCount,
            diagnostics,
            midiPaths,
            svgPaths,
            declaredVersion);
    }

    /// <summary>
    /// Substitutes the font files under <paramref name="directory"/> for the assembly's
    /// own embedded copies, for the rest of the process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A face is looked up by FILE NAME, so a directory holding
    /// <c>emmentaler-&lt;size&gt;.otf</c> and <c>.svg</c> replaces exactly those and
    /// leaves the other faces alone.
    /// </para>
    /// <para>
    /// THIS EXISTS FOR ONE MEASUREMENT, and it is a measurement the project needs
    /// standing rather than improvised: the port ships Emmentaler fonts it builds itself
    /// from the Metafont sources, and LilyPond ships fonts IT built from the same
    /// sources. Two FontForge runs do not produce identical outlines, and a skyline reads
    /// outlines, so a corpus row can differ from the oracle for two entirely different
    /// reasons. Running the same sweep against BOTH font builds separates them: with
    /// LilyPond's own fonts any divergence is the ENGINE and is a defect, and with the
    /// port's own fonts the divergence is the FONT BUILD and is measured and recorded
    /// (ruling R19).
    /// </para>
    /// </remarks>
    /// <param name="directory">The directory to consult before the embedded copies.</param>
    public static void UseFontsFrom(string directory)
        => Engine.Fonts.FontAssets.SearchPaths.Add(directory);

    /// <summary>
    /// Reports a change of working directory the way <c>main.cc:735-756</c> does, at INFO.
    /// </summary>
    /// <remarks>
    /// Upstream changes directory once, in <c>main ()</c>, when <c>--output</c> names one;
    /// its harness passes an absolute path into a fresh temporary directory, so every
    /// reference log opens with this line. A driver that engraves many files in one
    /// process changes directory per file for the same isolation and owes the same line;
    /// the wording and severity are upstream's (rule 15).
    /// </remarks>
    /// <param name="directory">The directory just changed to.</param>
    public static void ReportWorkingDirectoryChange(string directory)
        => Flower.Warn.Message("Changing working directory to: `" + directory + "'");

    /// <summary>
    /// Names the input the way upstream's "Processing `%s'" does: the file as the parser
    /// resolved it, which is a full path when the caller supplied a directory to resolve
    /// against and a bare name when it did not.
    /// </summary>
    /// <param name="inputName">The INPUT file's base name, without extension.</param>
    /// <param name="includeDirectory">The directory the file came from, or null.</param>
    /// <returns>The name to report.</returns>
    private static string ResolvedInputName(string inputName, string includeDirectory)
        => string.IsNullOrEmpty(includeDirectory)
            ? inputName + ".ly"
            : Path.Combine(includeDirectory, inputName + ".ly");

    /// <summary>
    /// The stencils <c>Paper_book::output</c> writes: one per PAGE, off each page prob's
    /// <c>stencil</c> property.
    /// </summary>
    /// <param name="paperBook">The processed paper book.</param>
    /// <returns>The page stencils, in page order.</returns>
    private static List<Stencil> PageStencils(PaperBook paperBook)
    {
        List<Stencil> stencils = new List<Stencil>();
        foreach (object entry in Pair.ToList(paperBook.Pages()))
        {
            if (entry is Prob page && page.GetProperty("stencil") is Stencil pageStencil)
            {
                stencils.Add(pageStencil);
            }
        }

        return stencils;
    }

    /// <summary>
    /// The stencils <c>Paper_book::classic_output</c> writes: one per SYSTEM, from
    /// <c>scm/backend-library.scm</c>'s <c>generate-system-stencils</c>.
    /// </summary>
    /// <remarks>
    /// The Scheme procedure is called rather than reimplemented because what it does is
    /// not merely a map: it unions the LEFT EXTENT of every system so that each file's
    /// music starts at the same x, which is the whole reason lilypond-book's pictures line
    /// up when they are dropped into a document one after another. It is a plain
    /// <c>define</c> in <c>(lily)</c>, not a <c>define-public</c>, so it is reached the way
    /// the runner reaches <c>markup-&gt;string</c>.
    /// </remarks>
    /// <param name="paperBook">The processed paper book.</param>
    /// <param name="diagnostics">Receives a line when the Scheme layer cannot be reached.</param>
    /// <returns>The system stencils, in system order.</returns>
    private static List<Stencil> SystemStencils(PaperBook paperBook, List<string> diagnostics)
    {
        List<Stencil> stencils = new List<Stencil>();
        object generate
            = LilyPondScheme.LookupProcedure(Symbol.Intern("generate-system-stencils"));
        if (generate == null)
        {
            diagnostics.Add("generate-system-stencils is not bound;"
                + " no per-system output was written");
            return stencils;
        }

        foreach (object entry in Pair.ToList(
            SchemeUtilities.CallCallback(generate, paperBook)))
        {
            if (entry is Stencil systemStencil)
            {
                stencils.Add(systemStencil);
            }
        }

        return stencils;
    }

    /// <summary>
    /// The safety net: says out loud that a book reached a toplevel book handler and that
    /// nothing was written for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO SHAPES, and both were reachable. The first is a book the runner never saw at
    /// all, because the document installed a handler that neither wrote it nor handed it
    /// to <c>ly:book-process</c>; the runner knows a book was built from the probe it asks
    /// one statement before <c>init.ly</c>'s epilogue dispatches. The second is a book the
    /// runner DID see, that drew stencils, and whose files are nevertheless not on disk —
    /// which is a writer fault rather than a handler fault, and is worth a sentence for
    /// the same reason.
    /// </para>
    /// <para>
    /// ⚠ NEITHER FIRES FOR A BOOK THAT LEGITIMATELY PRODUCES NOTHING. A book with no
    /// scores draws no stencils and the oracle writes no file for it either; a book whose
    /// scores only carry <c>\midi</c> writes its MIDI and is counted as written. Both were
    /// measured against 2.27.2 before this was written.
    /// </para>
    /// </remarks>
    /// <param name="pending">Every book handed to a toplevel handler.</param>
    /// <param name="bookOutputs">What the runner managed to turn into output.</param>
    /// <param name="filesWritten">How many files the run actually wrote.</param>
    /// <param name="chosenHandler">The handler <c>init.ly</c>'s epilogue was going to use.</param>
    /// <param name="defaultCollector">The runner's own default-toplevel-book-handler.</param>
    /// <param name="collector">The runner's own toplevel-book-handler.</param>
    /// <param name="epilogueBookVanished">
    /// Whether the epilogue built a toplevel book that never reached the runner.
    /// </param>
    /// <param name="baseName">The input's base name, for the sentence.</param>
    private static void ReportUnwrittenBooks(
        List<PendingBook> pending,
        List<BookOutput> bookOutputs,
        int filesWritten,
        object chosenHandler,
        object defaultCollector,
        object collector,
        bool epilogueBookVanished,
        string baseName)
    {
        if (epilogueBookVanished)
        {
            bool ours = ReferenceEquals(chosenHandler, defaultCollector)
                || ReferenceEquals(chosenHandler, collector);
            Flower.Warn.Warning("the toplevel book of `" + baseName + "' went to "
                + HandlerName(chosenHandler, ours)
                + " and was neither written nor handed back; nothing was written for it");
        }

        if (filesWritten > 0)
        {
            return;
        }

        for (int i = 0; i < bookOutputs.Count; i++)
        {
            if (bookOutputs[i].Pages.Count > 0)
            {
                Flower.Warn.Warning("book `" + bookOutputs[i].Name + "' drew "
                    + bookOutputs[i].Pages.Count
                    + " stencil(s) and none of them was written");
            }
        }
    }

    /// <summary>
    /// Names a toplevel book handler for a warning: the runner's own by the role it
    /// plays, the document's by whatever the interpreter calls it.
    /// </summary>
    /// <param name="handler">The handler object.</param>
    /// <param name="ours">Whether it is one of the runner's own collectors.</param>
    /// <returns>The phrase to put in the sentence.</returns>
    private static string HandlerName(object handler, bool ours)
    {
        if (ours)
        {
            return "the runner's own book collector";
        }

        if (handler is Primitive primitive && !string.IsNullOrEmpty(primitive.Name))
        {
            return "`" + primitive.Name + "'";
        }

        return "a toplevel book handler the document installed ("
            + (handler == null ? "unbound" : handler.ToString()) + ")";
    }

    /// <summary>
    /// Writes one page per stencil, into the CURRENT working directory and under BARE
    /// names, which is the state upstream's engine runs in.
    /// </summary>
    /// <remarks>
    /// ONE FILE PER PAGE, named the way <c>scm/framework-svg.scm</c>'s
    /// <c>output-stencils</c> names them: a single-page book is <c>&lt;base&gt;.svg</c>
    /// and a multi-page one carries the PAGE NUMBER, not a running index.
    /// <c>output-stencils</c> seeds its counter at <c>(1- first-page-number)</c> and bumps
    /// it before each page, so a book whose first page number is 2 writes
    /// <c>&lt;base&gt;-2.svg</c> first and has no <c>-1</c> at all.
    /// ⚠ The port counted from ONE regardless, and its comment asserted that was the
    /// oracle's rule (trap 26). The whole page-turn-page-breaking family sets
    /// <c>auto-first-page-number</c>, which starts those books on page 2: every page of
    /// every one of them was therefore named one too low, so each family member's LAST
    /// page read MISSING and the five before it were graded against the oracle's next
    /// page. That naming is the ORACLE's, so the comparator pairs candidate with
    /// reference by name alone.
    /// </remarks>
    /// <param name="bookOutputs">The books to write.</param>
    /// <param name="names">Receives each page's bare file name, in the order written.</param>
    /// <param name="diagnostics">Receives one line per failed page.</param>
    private static void WriteBookPages(
        List<BookOutput> bookOutputs,
        List<string> names,
        List<string> diagnostics)
    {
        // framework-svg.scm's (set-unit-length (lookup 'output-scale)) — the one
        // number the backend needs that is not in the stencil.
        SvgBackend backend = new SvgBackend();

        for (int b = 0; b < bookOutputs.Count; b++)
        {
            List<Stencil> bookPages = bookOutputs[b].Pages;
            string bookName = bookOutputs[b].Name;
            if (bookOutputs[b].UnitLength > 0.0)
            {
                backend.UnitLength = bookOutputs[b].UnitLength;
            }

            int pageNumber = bookOutputs[b].FirstPageNumber;
            for (int i = 0; i < bookPages.Count; i++, pageNumber++)
            {
                string pageName = bookPages.Count > 1
                    ? bookName + "-" + pageNumber + ".svg"
                    : bookName + ".svg";

                try
                {
                    File.WriteAllText(pageName, backend.RenderDocument(bookPages[i]));
                    names.Add(pageName);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    diagnostics.Add("SVG output failed: " + exception.Message);
                }
            }
        }
    }

    /// <summary>
    /// Writes each performance, into the CURRENT working directory and under a BARE name,
    /// which is the name upstream's <c>Performance::write_output</c> reports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>scm/midi.scm</c>'s <c>write-performances-midis</c> counts from 0 and suffixes
    /// only when the count is POSITIVE — so the FIRST performance is always
    /// <c>&lt;name&gt;.midi</c>, even in a book that goes on to produce more. The old
    /// <c>-1/-2</c> naming for multi-performance files paired every candidate with the
    /// WRONG reference: the port's first output was compared against the oracle's second,
    /// and the oracle's first was reported missing.
    /// </para>
    /// <para>
    /// ⚠ THE NAME IS THE BOOK'S, NOT THE INPUT FILE'S, AND THE COUNTER RESTARTS PER BOOK.
    /// Upstream reaches <c>write-performances-midis</c> from
    /// <c>lily/paper-book.cc</c>'s <c>Paper_book::output</c>, which runs once per book and
    /// is handed the name <c>get-outfile-name</c> computed for THAT book — so a file whose
    /// SECOND book performs writes <c>&lt;base&gt;-1.midi</c> and
    /// <c>&lt;base&gt;-1-1.midi</c>, the book counter and the performance counter both
    /// present. Naming every performance in a file from the input's base name with one
    /// running counter is right only while the file holds a single book; the Mendelssohn
    /// Octet holds two (an explicit <c>\book</c> that only prints, then the implicit book
    /// its toplevel <c>\score … \midi</c> blocks build), and its four MIDI files — byte
    /// for byte upstream's — came out one book-suffix short, so a comparator pairing by
    /// name alone matched the port's first movement against upstream's second.
    /// </para>
    /// </remarks>
    /// <param name="bookOutputs">The books to write, each under its own name.</param>
    /// <param name="names">Receives each file's bare name, in the order written.</param>
    /// <param name="diagnostics">Receives one line per failed performance.</param>
    private static void WritePerformances(
        List<BookOutput> bookOutputs,
        List<string> names,
        List<string> diagnostics)
    {
        for (int b = 0; b < bookOutputs.Count; b++)
        {
            List<Engine.Layout.Performance> performances = bookOutputs[b].Performances;
            string bookName = bookOutputs[b].Name;

            for (int i = 0; i < performances.Count; i++)
            {
                string midiName = i > 0
                    ? bookName + "-" + i + ".midi"
                    : bookName + ".midi";

                try
                {
                    performances[i].WriteOutput(midiName, PerformanceName(performances[i]));
                    names.Add(midiName);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    diagnostics.Add("MIDI output failed: " + exception.Message);
                }
            }
        }
    }

    /// <summary>
    /// Names a performance the way <c>scm/midi.scm</c>'s
    /// <c>write-performances-midis</c> does: <c>markup-&gt;string</c> of the headers'
    /// <c>midititle</c>, else <c>title</c>, else the empty string.
    /// </summary>
    /// <remarks>
    /// <c>performance-name-from-headers</c> is module-private in <c>(lily)</c>, so its
    /// two-lookup chain is reproduced through the same primitives rather than resolved
    /// by name. The runner once passed <see cref="string.Empty"/> here,
    /// which was right for every headerless regression file and wrong for the one that
    /// sets a title — the control track's name placeholder was erased instead of
    /// filled.
    /// </remarks>
    /// <param name="performance">The performance being written.</param>
    /// <returns>The name, possibly empty.</returns>
    private static string PerformanceName(Engine.Layout.Performance performance)
    {
        object lookup = LilyPondScheme.LookupProcedure(Symbol.Intern("ly:modules-lookup"));
        object markupToString = LilyPondScheme.LookupProcedure(Symbol.Intern("markup->string"));
        if (lookup == null || markupToString == null)
        {
            return string.Empty;
        }

        object title = SchemeUtilities.CallCallback(
            lookup, performance.Headers, Symbol.Intern("midititle"));
        if (title is bool noMidiTitle && !noMidiTitle)
        {
            title = SchemeUtilities.CallCallback(
                lookup, performance.Headers, Symbol.Intern("title"));
        }

        if (title is bool noTitle && !noTitle)
        {
            return string.Empty;
        }

        return SchemeUtilities.CallCallback(markupToString, title)?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Returns a score's <c>\midi</c> output definition, or <see langword="null"/> when
    /// the score asks for no MIDI.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ScoreLayout"/> there is no falling back to a default: a score
    /// without a <c>\midi</c> block produces no MIDI at all, and inventing one would give
    /// every one of the 2,146 regression files a MIDI file the oracle does not have.
    /// </remarks>
    private static OutputDef ScoreMidi(Score score)
    {
        Symbol midiSymbol = Symbol.Intern("midi");

        foreach (OutputDef def in score.Defs)
        {
            if (ReferenceEquals(def.CVariable("output-def-kind"), midiSymbol))
            {
                return def;
            }
        }

        return null;
    }

    /// <summary>
    /// The session lifecycle around one file: prologue (init.ly's collection-state
    /// defines, which is also the per-file reset), the file itself, and the epilogue
    /// (init.ly's version check, book construction and handler dispatch). Books flow
    /// to whichever toplevel book handler is bound when the epilogue runs.
    /// </summary>
    /// <returns>The parse error count of the file and epilogue together.</returns>
    /// <summary>
    /// The <c>version-seen</c> definition for a run whose main input declared
    /// <paramref name="version"/>.
    /// </summary>
    /// <param name="version">The <c>\version</c> string the lexer read.</param>
    /// <returns>Scheme that defines <c>version-seen</c> the way the lexer does.</returns>
    /// <remarks>
    /// <c>parse-and-check-version</c> is a plain <c>define</c> in
    /// <c>lily-library.scm</c> rather than a <c>define-public</c>, so it is reached
    /// through a <c>defined?</c> test rather than assumed: when it cannot be reached the
    /// answer falls back to <see langword="true"/>, which is upstream's own value for
    /// "a version was found but could not be parsed" and suppresses the same messages.
    /// A version string carrying a quote or a backslash cannot be embedded, and takes
    /// the same fallback — which is also what upstream answers for an unparseable one.
    /// </remarks>
    private static string VersionSeenLy(string version)
    {
        if (version.IndexOf('"') >= 0 || version.IndexOf('\\') >= 0)
        {
            return "#(define version-seen #t)";
        }

        return "#(define version-seen"
            + " (let ((v (if (defined? 'parse-and-check-version)"
            + " (parse-and-check-version \"" + version + "\")"
            + " #f)))"
            + " (if v v #t)))";
    }

    private static int RunLifecycle(
        LilyParserSession session,
        string text,
        string baseName,
        string inputName,
        string includeDirectory,
        List<string> diagnostics)
    {
        // WHERE THE ENTRY DIRECTORY GOES, AND WHY IT IS NO LONGER THE INCLUDE PATH.
        //
        // This is upstream's `main_input_name_' half: the directory an \include read from
        // the main input resolves against (`includable-lexer.cc:52-56'). It used to be
        // pushed onto session.IncludePath, which is the port's `global_path', and that put
        // it in front of ly:find-file, ly:parse-file and ly:parse-init as well. Upstream
        // keeps the two apart, and the difference is observable: MEASURED on 2.27.2,
        // `#(ly:find-file "asset.txt")' with the asset beside the input and the working
        // directory elsewhere answers #f, where the port answered the asset's path.
        // A host's -I entries still belong on IncludePath, which is where
        // ly:parser-append-to-include-path still puts them.
        //
        // Saved and restored rather than cleared, so a run nested inside another leaves
        // the outer run's directory exactly as it found it — the shape the IncludePath
        // add/remove pair had.
        string enclosingMainInputDirectory = session.MainInputDirectory;
        session.MainInputDirectory = includeDirectory;

        try
        {
            // The INPUT's name, never the output's: `-o' renames what is written and
            // upstream leaves what was read alone (see BatchRunOptions.InputName).
            session.SetIdentifier("input-file-name", new MutableString(inputName + ".ly"));

            // ly:parser-output-name, which upstream's Lily_parser::parse_file sets from
            // output_file_name_for_input_file_name. The port's ly:parse-file primitive
            // assigns it and this driver does not go through that primitive (trap 17f),
            // so it had kept its empty default for the life of the sweep. A `.ly' that
            // builds its own file names from it — clip-systems.ly composes
            // "~a-~a-~a" out of (ly:parser-output-name), a suffix and a tail — got a name
            // missing its whole base.
            session.OutputBaseName = baseName;

            // One file's \version must not answer the next file's version check.
            session.MainInputVersionString = null;

            ParseOutcome prologue = session.ParseText(ProloguelLy, "<batch-prologue>");
            diagnostics.AddRange(prologue.AllDiagnostics());

            // BEFORE THE INPUT, which is where init.ly puts it: a settings file exists to
            // change what the input is then read under (`-dinclude-settings' plus the
            // `-d' switches that file sets is LilyPond's own layout-control idiom), so
            // running it afterwards would be running it too late to matter.
            ParseOutcome includeSettings
                = session.ParseText(IncludeSettingsLy, "<batch-include-settings>");
            diagnostics.AddRange(includeSettings.AllDiagnostics());

            // The name every diagnostic's location and every music object's `origin'
            // carries, so it is the INPUT's too — a warning about a file has to name the
            // file a reader can open.
            ParseOutcome parsed = session.ParseText(text, inputName + ".ly");
            diagnostics.AddRange(parsed.AllDiagnostics());

            // WHAT THE LEXER RECORDED, HANDED TO THE EPILOGUE'S VERSION CHECK.
            //
            // Upstream's lexer defines `version-seen' itself, in the (lily) top scope,
            // the moment it reads the main input's \version string (lexer.ll:243-264):
            // #f means none was found, #t means one was found but could not be parsed,
            // and anything else is the parsed version as a list. The port's lexer only
            // remembered the STRING, so version-seen kept the prologue's #f and
            // ly/init.ly's epilogue announced "no \version statement found" for every
            // file in the suite. The message went to ProgramOptions' null writer, so it
            // had never been seen.
            string mainVersion = session.MainInputVersionString;
            if (!string.IsNullOrEmpty(mainVersion))
            {
                ParseOutcome versionSeen = session.ParseText(
                    VersionSeenLy(mainVersion), "<batch-version-seen>");
                diagnostics.AddRange(versionSeen.AllDiagnostics());
            }

            // ONE STATEMENT BEFORE THE EPILOGUE, because that is the last moment at which
            // the answer exists: see BookHandlerProbeLy. Its error count is deliberately
            // not added to the run's, exactly as <batch-version-seen>'s is not — a
            // question the runner asks on its own behalf is not one of the file's errors.
            ParseOutcome bookHandler
                = session.ParseText(BookHandlerProbeLy, "<batch-book-handler>");
            diagnostics.AddRange(bookHandler.AllDiagnostics());

            ParseOutcome epilogue = session.ParseText(EpilogueLy, "<batch-epilogue>");
            diagnostics.AddRange(epilogue.AllDiagnostics());

            // The settings file's errors count too. Upstream reads it from inside
            // init.ly's own parse, so a settings file that cannot be found is a parse
            // error of the run and lilypond ends non-zero; a run that swallowed it would
            // be more lenient than 2.27.2.
            return includeSettings.ErrorCount + parsed.ErrorCount + epilogue.ErrorCount;
        }
        finally
        {
            session.MainInputDirectory = enclosingMainInputDirectory;
        }
    }

    /// <summary>
    /// The layout a score engraves under: its own <c>\layout</c> block when it has
    /// one, the session's <c>$defaultlayout</c> otherwise, parented into the book's
    /// paper so paper variables resolve. The upstream path runs this through
    /// <c>Paper_book</c>'s scaling; here the parenting
    /// carries the variables and the scale stays 1 (PORT-COVERAGE, DIVERGENCES).
    /// </summary>
    private static OutputDef ScoreLayout(Score score, OutputDef paper, OutputDef defaultLayout)
    {
        Symbol layoutSymbol = Symbol.Intern("layout");
        OutputDef found = null;
        foreach (OutputDef def in score.Defs)
        {
            if (ReferenceEquals(def.CVariable("output-def-kind"), layoutSymbol))
            {
                found = def;
                break;
            }
        }

        OutputDef layout = found ?? defaultLayout;

        if (paper != null)
        {
            // Score::book_rendering scales the score's layout by the BOOK paper's
            // output-scale and re-parents it onto that paper, unconditionally. The
            // re-parenting used to happen only for an unparented layout, which meant the
            // $defaultlayout kept pointing at the ORIGINAL $defaultpaper while the book
            // carried a clone of it; every paper variable the book computed, line-width
            // among them, then resolved to nothing.
            //
            // output-scale is deliberately NOT a dimension variable, so the scaled paper
            // still reports the original factor and this reads the same number the paper
            // was scaled by.
            double outputScale = paper.GetDimension("output-scale");
            OutputDef scaled = outputScale > 0.0 ? layout.ScaledClone(outputScale) : layout;

            // ScaledClone answers the SAME object when the Scheme layer is unreachable.
            // Re-parenting that would reach into a definition other scores share.
            layout = ReferenceEquals(scaled, layout) ? layout.Clone() : scaled;
            layout.Parent = paper;
        }

        return layout;
    }

    /// <summary>
    /// How many LINES the book came to, summed over its pages — the figure the sweep log
    /// reports under "system(s)".
    /// <para>Counted off each page's <c>lines</c> property rather than off the page count,
    /// because a page carries as many systems as the breaker put on it.</para>
    /// </summary>
    private static int CountLines(PaperBook paperBook)
    {
        int total = 0;
        foreach (object entry in Pair.ToList(paperBook.Pages()))
        {
            if (entry is Prob page)
            {
                total += Pair.ToList(page.GetProperty("lines")).Count;
            }
        }

        return total;
    }

    /// <summary>
    /// <c>scm/lily-library.scm</c>'s <c>get-outfile-name</c>: the file name one BOOK's
    /// output prints under.
    /// <para>
    /// The book's own printed name — <c>\bookOutputName</c>'s, when it set one, and the
    /// input's base name otherwise — then <c>-&lt;output-suffix&gt;</c> when a suffix is
    /// set, then <c>-&lt;n&gt;</c> for the n-th book already printed under the SAME key. ⚠ The
    /// counter is keyed by base name AND suffix together, so it is NOT a running book
    /// index: <c>book-change-global-staffsize-abs-fonts</c> prints two books, one under
    /// the suffix "standard-size" and one under none, and upstream numbers NEITHER
    /// because their keys differ. Numbering them 0 and 1 named both files wrongly.
    /// </para>
    /// </summary>
    /// <param name="session">The parser session, for the toplevel <c>output-suffix</c>.</param>
    /// <param name="book">The book being named.</param>
    /// <param name="baseName">The input file's base name.</param>
    /// <param name="counters">The run's <c>counter-alist</c>.</param>
    /// <returns>The name, without extension.</returns>
    private static string GetOutfileName(
        LilyParserSession session,
        Book book,
        string baseName,
        Dictionary<string, int> counters)
    {
        // get-current-suffix: `paper-variable book 'output-suffix' first -- which searches
        // the book's own paper, then the enclosing \paper stack, then $defaultpaper, taking
        // the first NON-#f -- and only when that is not a string does it fall back on the
        // toplevel `output-suffix' identifier. The \paper STACK ($papers) has no port
        // equivalent; it is non-empty only inside a bookpart, which cannot name a file.
        // Everything here is read NOW, while the book is being printed, for the reason
        // recorded where bookNames is declared.
        object suffix = book.Paper != null ? book.Paper.CVariable("output-suffix") : null;
        if (!SchemeUtilities.IsString(suffix) && session != null)
        {
            OutputDef defaultPaper = session.LookupIdentifier(DefaultPaperName) as OutputDef;
            if (defaultPaper != null)
            {
                suffix = defaultPaper.CVariable("output-suffix");
            }
        }

        if (!SchemeUtilities.IsString(suffix) && session != null)
        {
            suffix = session.LookupIdentifier("output-suffix");
        }

        string suffixText = SchemeUtilities.IsString(suffix)
            ? SchemeUtilities.StringText(suffix)
            : null;

        // get-current-filename, the same three-step chain one variable over: the book's
        // own paper, then $defaultpaper (`paper-variable'), then the toplevel
        // `output-filename' identifier, and only then the name the input was read under.
        //
        // ⚠ MEASURED AGAINST 2.27.2, AND THE PORT DID NOT HAVE IT AT ALL: a file whose
        // first statement is `\bookOutputName "renamed"' writes renamed.svg from the
        // oracle and wrote <input>.svg here, so a document that renames its own output
        // was silently ignored — and a file that renames SEVERAL books put them all under
        // the input's name, where the last one written wins. The counter key is built from
        // this name too, exactly as upstream builds it, which is what keeps two books
        // renamed to the same thing numbered instead of overwriting each other.
        object fileName = book.Paper != null
            ? book.Paper.CVariable("output-filename")
            : null;
        if (!SchemeUtilities.IsString(fileName) && session != null)
        {
            OutputDef defaultPaper = session.LookupIdentifier(DefaultPaperName) as OutputDef;
            if (defaultPaper != null)
            {
                fileName = defaultPaper.CVariable("output-filename");
            }
        }

        if (!SchemeUtilities.IsString(fileName) && session != null)
        {
            fileName = session.LookupIdentifier("output-filename");
        }

        string printedName = SchemeUtilities.IsString(fileName)
            ? SchemeUtilities.StringText(fileName)
            : baseName;

        // The KEY is the base name and the suffix concatenated, exactly as upstream builds
        // it, and the RESULT joins them with a dash. The two are deliberately different.
        string key = printedName + suffixText;
        string result = suffixText != null ? printedName + "-" + suffixText : printedName;

        counters.TryGetValue(key, out int count);
        if (count > 0)
        {
            result = result + "-" + count;
        }

        counters[key] = count + 1;
        return result;
    }

    /// <summary>
    /// One book on its way to being written, and which of upstream's two output halves it
    /// is on.
    /// </summary>
    /// <remarks>
    /// A book reaches the runner by one of two routes, and the difference is the
    /// document's to make. An ordinary document leaves <c>init.ly</c>'s toplevel handlers
    /// alone, so the runner's own collector takes the book and
    /// <see cref="Processed"/> is <see langword="null"/>: the runner processes it after
    /// the parse, on the page path. A document that installs its own handler — which
    /// <c>lilypond-book-preamble.ly</c> does, and which upstream honours — sends the book
    /// through <c>ly:book-process</c> or <c>ly:book-process-to-systems</c> DURING the
    /// parse, and it arrives already processed, with <see cref="ToSystems"/> saying which
    /// of the two it went through.
    /// </remarks>
    private sealed class PendingBook
    {
        /// <summary>Initializes a book the runner's own collector took.</summary>
        /// <param name="name">The name <c>get-outfile-name</c> gave the book.</param>
        /// <param name="book">The book itself.</param>
        public PendingBook(string name, Book book)
            : this(name, book, null, false)
        {
        }

        /// <summary>Initializes a book, processed or not.</summary>
        /// <param name="name">The name <c>get-outfile-name</c> gave the book.</param>
        /// <param name="book">The book itself.</param>
        /// <param name="processed">
        /// The paper book the document's own handler already produced, or
        /// <see langword="null"/> when the runner still owes the processing.
        /// </param>
        /// <param name="toSystems">
        /// Whether the document's handler asked for one file per SYSTEM.
        /// </param>
        public PendingBook(string name, Book book, PaperBook processed, bool toSystems)
        {
            Name = name;
            Book = book;
            Processed = processed;
            ToSystems = toSystems;
        }

        /// <summary>Gets the name the book's files print under.</summary>
        public string Name { get; }

        /// <summary>Gets the book.</summary>
        public Book Book { get; }

        /// <summary>
        /// Gets the paper book the document's own handler already produced, or
        /// <see langword="null"/>.
        /// </summary>
        public PaperBook Processed { get; }

        /// <summary>Gets whether the book prints one file per system rather than per page.</summary>
        public bool ToSystems { get; }
    }

    /// <summary>One book's rendered pages, under the name and page numbering it prints at.</summary>
    private sealed class BookOutput
    {
        /// <summary>Initializes a book's output.</summary>
        /// <param name="name">The name <c>get-outfile-name</c> gave the book.</param>
        /// <param name="pages">The book's pages, in order.</param>
        /// <param name="firstPageNumber">The page number the first page prints at.</param>
        /// <param name="unitLength">The book's own <c>output-scale</c>.</param>
        /// <param name="performances">The book's performances, in order.</param>
        public BookOutput(
            string name,
            List<Stencil> pages,
            int firstPageNumber,
            double unitLength,
            List<Engine.Layout.Performance> performances)
        {
            Name = name;
            Pages = pages;
            FirstPageNumber = firstPageNumber;
            UnitLength = unitLength;
            Performances = performances;
        }

        /// <summary>Gets the name the book's files print under.</summary>
        public string Name { get; }

        /// <summary>Gets the book's pages, in order.</summary>
        public List<Stencil> Pages { get; }

        /// <summary>Gets the page number the first page prints at.</summary>
        public int FirstPageNumber { get; }

        /// <summary>Gets the book's own <c>output-scale</c>, which the backend divides
        /// font sizes by.</summary>
        public double UnitLength { get; }

        /// <summary>Gets the book's performances, in the order they were produced —
        /// the list <c>write-performances-midis</c> names from this book's name.</summary>
        public List<Engine.Layout.Performance> Performances { get; }
    }
}

/// <summary>What one batch run produced.</summary>
public sealed class BatchRunResult
{
    /// <summary>Initializes a result.</summary>
    /// <param name="svgPath">The SVG written, or <see langword="null"/>.</param>
    /// <param name="bookCount">How many books the toplevel handlers produced.</param>
    /// <param name="systemCount">How many scores engraved to a system.</param>
    /// <param name="skippedEntries">Toplevel entries skipped as not-yet-portable.</param>
    /// <param name="errorCount">Parse and epilogue errors.</param>
    /// <param name="diagnostics">Everything reported along the way.</param>
    /// <param name="midiPaths">The MIDI files written, in performance order.</param>
    /// <param name="svgPaths">The SVG pages written, in page order.</param>
    /// <param name="declaredVersion">The main input's <c>\version</c> string, or
    /// <see langword="null"/> when it declared none.</param>
    public BatchRunResult(
        string svgPath,
        int bookCount,
        int systemCount,
        int skippedEntries,
        int errorCount,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<string> midiPaths = null,
        IReadOnlyList<string> svgPaths = null,
        string declaredVersion = null)
    {
        DeclaredVersion = declaredVersion;
        SvgPath = svgPath;
        SvgPaths = svgPaths ?? (svgPath != null
            ? new[] { svgPath }
            : System.Array.Empty<string>());
        BookCount = bookCount;
        SystemCount = systemCount;
        SkippedEntries = skippedEntries;
        ErrorCount = errorCount;
        Diagnostics = diagnostics;
        MidiPaths = midiPaths ?? System.Array.Empty<string>();
    }

    /// <summary>Gets the FIRST SVG file written, or <see langword="null"/> when nothing engraved.</summary>
    public string SvgPath { get; }

    /// <summary>
    /// Gets every SVG file written, one per page, in page order.
    /// <para>On the book path a file may produce several. The driver's self-check counts what
    /// is on disk against what was reported written, so reporting only the first page
    /// would make every later page of every multi-page book look like a stale leftover.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> SvgPaths { get; }

    /// <summary>Gets how many books the toplevel handlers delivered.</summary>
    public int BookCount { get; }

    /// <summary>Gets how many scores engraved to a system.</summary>
    public int SystemCount { get; }

    /// <summary>Gets how many toplevel entries were skipped as not yet portable.</summary>
    public int SkippedEntries { get; }

    /// <summary>Gets the parse error count.</summary>
    public int ErrorCount { get; }

    /// <summary>Gets everything the run reported.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Gets the MIDI files this run wrote, in performance order.</summary>
    public IReadOnlyList<string> MidiPaths { get; }

    /// <summary>
    /// Gets the <c>\version</c> string the MAIN input declared, or
    /// <see langword="null"/> when it declared none — what the lexer recorded, so a
    /// host deciding whether to offer a convert-ly update reads the engine's own
    /// answer rather than re-scanning the text.
    /// </summary>
    public string DeclaredVersion { get; }
}
