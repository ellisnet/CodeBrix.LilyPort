================================================================================
AGENT-README: CodeBrix.LilyPort
A Guide for AI Coding Agents — CONSUMING the CodeBrix.LilyPort.GplLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.LilyPort is a managed, cross-platform music engraving engine for .NET:
LilyPond input (a `.ly' file or string) in, SVG pages and Standard MIDI Files
out, entirely in-process. It is a port of GNU LilyPond 2.27.2 -- the parser, the
whole C++ engraving engine (contexts, engravers, grobs, spacing, line and page
breaking), the MIDI performers, the fonts, and LilyPond's own Scheme layer,
which is vendored verbatim and runs on the CodeBrix.LilyScheme interpreter. The
port is verified page for page against LilyPond's own 2,146-file regression
suite rendered by the real LilyPond binary.

Four of LilyPond's command-line companions are in the same package, as plain
text transformers that need no engine at all: convert-ly (bring an old document
up to current syntax), abc2ly, midi2ly and musicxml2ly (ABC / MIDI / MusicXML in,
LilyPond source out).

Target framework: .NET 10 or later. Windows, Linux and macOS. No native
libraries: the music fonts, the text fonts and the Scheme layer are embedded
resources inside the assemblies.

PROVENANCE. The C# is a translation of LilyPond's lily/ and flower/ sources and
of its python/ converter scripts; the scm/ and ly/ layers are vendored VERBATIM
and loaded from embedded resources. Every namespace begins with
CodeBrix.LilyPort -- there are no upstream namespaces to use, and "LilyPond"
appears in no assembly, package, namespace or type name. LilyPond's own
vocabulary (grob, stencil, engraver, performer, output-def, book, paper) is kept
throughout, so LilyPond's Notation Reference and Internals Reference describe
this engine's behaviour and its `.ly' input language exactly.

THE CENTRAL IDEA, in one paragraph, because it shapes the whole API: LilyPond
is one process per input file, and everything in it -- the Scheme interpreter,
the option table, the parser's toplevel scope, the font registry -- is global
state that dies with the process. The port keeps that state PROCESS-GLOBAL, boots
it ONCE (tens of seconds cold, a few seconds with the on-disk cache), and then
engraves any number of files through it, restoring the per-file state itself
between runs. So the API is a long-lived, serialised engine driven through
BatchRunner, not a stateless function you call in parallel. Read STARTUP,
LIFETIME AND THREADING before writing a host.

INSTALLATION
============
NuGet package id: CodeBrix.LilyPort.GplLicenseForever

    dotnet add package CodeBrix.LilyPort.GplLicenseForever

The package requires license acceptance when installed
(PackageRequireLicenseAcceptance is set).

The ROOT NAMESPACE is CodeBrix.LilyPort, without the .GplLicenseForever suffix;
the suffix is part of the package id only, chosen so the license identification
travels with the package name.

ONE PACKAGE, FIVE ASSEMBLIES. Installing the package puts all five of these in
your output directory; there is nothing else to reference:

    CodeBrix.LilyPort.dll           the facade: BatchRunner, LilyPondInit,
                                    LilyPortEngraver, LilyPortPerformer,
                                    LilyPortInfo, ConvertLy.*, Importers.*
    CodeBrix.LilyPort.Engine.dll    the engine (lily/ port): music, contexts,
                                    engravers, grobs, layout, fonts, MIDI, the
                                    Scheme host bridge; carries the embedded
                                    Scheme layer and the fonts
    CodeBrix.LilyPort.Backends.dll  SvgBackend
    CodeBrix.LilyPort.Parsing.dll   the LilyPond lexer/parser (LilyParserSession)
    CodeBrix.LilyPort.Flower.dll    the utility layer (flower/ port): Warn and
                                    the diagnostics, Rational, Interval, Offset

WHICH ONE DO I REFERENCE: the package. The four sub-assemblies are bundled
inside it (they are not separate packages and not package dependencies), so a
single PackageReference gives your project all five compile-time references.

NuGet dependencies: CodeBrix.LilyScheme.LgplLicenseForever (the Scheme
interpreter; LGPL-3.0-or-later). It is pulled in automatically, and its
namespaces (CodeBrix.LilyScheme, CodeBrix.LilyScheme.Reader,
CodeBrix.LilyScheme.Runtime, CodeBrix.LilyScheme.Values) appear in this API
wherever the engine hands you a Scheme value or asks for the interpreter. Its own
AGENT-README documents that interpreter.

License: GPL-3.0-only -- deliberately NOT "or later". The package bundles
LilyPond's articulate.ly, which its author licenses under GPL version 3 only;
GPL-3-only and GPL-3-or-later material combine legally, but the combined work can
then be conveyed only under GPL version 3 exactly. Everything that references
this package inherits the obligation. See LICENSING AND REDISTRIBUTION below
BEFORE choosing this package. The full GPL text (LICENSE), the SIL Open Font
License for the music fonts (LICENSE.OFL) and THIRD-PARTY-NOTICES.txt ship at
the root of the package.

Requirements: .NET 10 or later; a couple of hundred megabytes of working
memory for a booted engine (measured: under 200 MB peak for small scores); a
writable per-user cache directory (optional, see THE BOOT CACHE). No native
libraries, no fontconfig, no Ghostscript, no LilyPond installation.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.LilyPort;                  // BatchRunner, BatchRunOptions,
                                              //   BatchRunResult, LilyPondInit,
                                              //   LilyPortEngraver, EngraveResult,
                                              //   LilyPortPerformer, LilyPortInfo
    using CodeBrix.LilyPort.ConvertLy;        // DocumentConverter, ConversionVersion,
                                              //   ConversionRule, ConversionResult
    using CodeBrix.LilyPort.Importers;        // AbcImporter, MidiImporter,
                                              //   MusicXmlImporter, *ImportOptions,
                                              //   ImportResult
    using CodeBrix.LilyPort.Backends;         // SvgBackend
    using CodeBrix.LilyPort.Flower;           // Warn, LogLevel,
                                              //   LilyPondErrorException,
                                              //   LineTrackingWriter, Rational,
                                              //   Interval, Offset, Axis, Direction
    using CodeBrix.LilyPort.Engine;           // LoadReport, NotPortedException
    using CodeBrix.LilyPort.Engine.Bootstrap; // LilyPondScheme, ProgramOptions,
                                              //   CommandLineOptions,
                                              //   BootExpansionCache, LilyVersion
    using CodeBrix.LilyPort.Engine.Layout;    // OutputDef, Stencil, Performance,
                                              //   PaperBook, PaperScore, IStencilSink
    using CodeBrix.LilyPort.Engine.Music;     // MusicObject, Moment, Duration, Pitch
    using CodeBrix.LilyPort.Engine.Fonts;     // FontAssets, TextFontChain, TextFace
    using CodeBrix.LilyPort.Engine.Objects;   // Grob, Prob, Book, Score, SystemGrob
    using CodeBrix.LilyPort.Engine.Translation; // Context, GlobalContext,
                                              //   ContextDef
    using CodeBrix.LilyPort.Engine.Audio;     // MidiStream, AudioStaff, MidiTrack
    using CodeBrix.LilyPort.Engine.Origins;   // Input, SourceFile, PointAndClick
    using CodeBrix.LilyPort.Parsing.Session;  // LilyParserSession, ParseOutcome
    using CodeBrix.LilyPort.Parsing.Driver;   // ParseAbortedException, SourceSpan
    using CodeBrix.LilyScheme;                // Interpreter (RunWithLargeStack)

Most applications need only the first line: BatchRunner does the whole job.

QUICK START
===========
    using System;
    using System.IO;
    using CodeBrix.LilyPort;

    string source =
        "\\version \"2.24.0\"\n" +
        "\\relative { c'4 d e f | g1 }\n";
    string outputDirectory = Path.Combine(Path.GetTempPath(), "lilyport-out");

    BatchRunResult result = BatchRunner.RunText(
        source,            // the LilyPond source text
        "first",           // output base name (no extension)
        null,              // directory that \include resolves against
        outputDirectory);  // where the .svg (and .midi) files land

    foreach (string line in result.Diagnostics)
    {
        Console.Error.WriteLine(line);
    }

    if (result.ErrorCount == 0 && result.SvgPath != null)
    {
        Console.WriteLine("wrote " + result.SvgPath);        // .../first.svg
    }

Three things about that snippet:

* THE FIRST CALL BOOTS THE ENGINE. RunText loads the Scheme layer and the ly/
  init layer on first use: about 35 seconds cold on a fast machine, about 4
  seconds when the on-disk expansion cache is warm (it is written by the first
  cold boot and reused by every later process). Every further RunText in the
  same process is quick -- a fraction of a second for a small score. Never start
  a process per file.
* IT WRITES FILES, NOT STRINGS. One SVG per PAGE (result.SvgPaths) and one
  MIDI file per performance (result.MidiPaths), into outputDirectory, named the
  way LilyPond names them. Read the SVG text back with File.ReadAllText if you
  need it in memory.
* THE ENGINE PRINTS PROGRESS TO Console.Error BY DEFAULT ("Parsing...",
  "Drawing systems..."), exactly as lilypond does. Set Warn.Level, redirect
  Warn.Output, or pass BatchRunOptions.MessageWriter -- see DIAGNOSTICS.

CORE API REFERENCE
==================
The reference is organised by feature area in the sections that follow: the
pipeline shape first, then the batch runner (what most hosts use), startup and
threading, the direct engraver/performer surface, the SVG backend, diagnostics,
the parser, convert-ly, the importers, fonts, and finally a map of the rest of
the engine.

HOW THE ENGINE IS SHAPED
========================
    .ly text ---> LilyParserSession (Parsing)   the real Bison grammar, reimplemented
                    |  toplevel handlers run DURING the parse (Scheme, vendored)
                    v
                  Book / Score (Engine.Objects)
                    |  Book.Process -> PaperBook: iterate music through a context
                    |  tree built from ly/engraver-init.ly, engravers make grobs,
                    |  spacing, line breaking, page breaking (Engine.Translation,
                    |  Engine.Objects, Engine.Layout)
                    v
                  one Stencil per page  ---> SvgBackend (Backends) ---> <base>.svg
                    |
                    +--> (score has \midi) performers make audio elements
                         ---> Performance.WriteOutput ---> <base>.midi

Everything above the parser is driven by LilyPond's OWN Scheme (scm/*.scm and
ly/*.ly, embedded verbatim) running on CodeBrix.LilyScheme; the C# engine is
what that Scheme calls. That is why so much of the API hands you Scheme values
(object-typed Pair lists, Symbol keys, MutableString text) -- they are LilyPond's
property alists, unchanged.

The FIVE ASSEMBLIES and what a consumer takes from each:

    CodeBrix.LilyPort           the entry points. BatchRunner for whole files;
                                LilyPondInit for the booted session; LilyPortEngraver
                                and LilyPortPerformer for one music tree at a time;
                                ConvertLy and Importers for text transformation.
    ...Engine                   the object model you meet when you go below the
                                facade: MusicObject, OutputDef, Stencil, Grob,
                                Context, Performance; the Scheme host bridge
                                (LilyPondScheme) and option table (ProgramOptions);
                                the fonts (FontAssets, TextFontChain).
    ...Backends                 SvgBackend -- Stencil in, SVG document out.
    ...Parsing                  LilyParserSession -- parse text or a music
                                expression, add \include directories, read the
                                diagnostics.
    ...Flower                   Warn -- the single diagnostics sink every layer
                                writes to -- plus the small value types (Rational,
                                Interval, Offset, Axis, Direction) that measure
                                everything.

THE BATCH RUNNER: BatchRunner
=============================
    namespace CodeBrix.LilyPort;
    public static class BatchRunner

This is the `lilypond' program as a method: a file's text goes through the real
ly/init.ly lifecycle (version check, toplevel score collection, book
construction, expect-error handshake), every book is processed by the real
ly:book-process path (paper scaling, page breaking), and the pages and
performances are written to disk under LilyPond's own file names. It is what the
port's regression suite drives 2,146 files through, and what the interactive
shell that ships with the repository uses for its `engrave' command.

RUNNING A FILE
--------------
    static BatchRunResult RunFile(string filePath, string outputDirectory)
    static BatchRunResult RunFile(string filePath, string outputDirectory,
                                  string outputBaseName)
    static BatchRunResult RunFile(string filePath, string outputDirectory,
                                  string outputBaseName, BatchRunOptions runOptions)
        Reads the file, uses ITS OWN DIRECTORY as the \include root, and writes
        under outputBaseName (null: the file's name without extension). The
        named form keeps whatever extension the name carries -- `name.pdf'
        engraves to `name.pdf.svg' -- because that is what lilypond -o does.

        A NAMED OUTPUT RENAMES WHAT IS WRITTEN, NOT WHAT WAS READ. The progress
        line ("Processing `...'"), the `input-file-name' a document can look
        up, the file named in every diagnostic's location and every music
        object's `origin' all keep the INPUT's name; only
        `ly:parser-output-name' -- and the files on disk -- answer the new one.
        RunFile fills BatchRunOptions.InputName in for you from the path it was
        handed, so a caller that names a file gets this without asking.

    static BatchRunResult RunText(string text, string baseName,
                                  string includeDirectory, string outputDirectory)
    static BatchRunResult RunText(string text, string baseName,
                                  string includeDirectory, string outputDirectory,
                                  BatchRunOptions runOptions)
        The same pipeline over a string. baseName is the output name WITHOUT
        extension and also what diagnostics call the input ("<baseName>.ly").
        includeDirectory is the directory the text's own \include statements
        resolve against, or null for none (the vendored ly/ files -- articulate.ly,
        english.ly, gregorian.ly and the rest -- always resolve). An include
        INSIDE an included file resolves against ITS OWN directory first, so a
        piece laid out in subdirectories only needs its top directory passed here.
        outputDirectory is created if missing.

    static void SplitOutputName(string outputName, out string directory,
                                out string baseName)
        Turns one `lilypond -o VALUE' into its two halves, with upstream's rules:
        an EXISTING directory is a directory (baseName null); otherwise the value
        splits into a directory part (null when none, or "." ) and a file part.
        Pair it with the three-argument RunFile.

    static void UseFontsFrom(string directory)
        Consults directory for font FILES (matched by file name, e.g.
        emmentaler-20.otf, C059-Roman.otf) before the embedded copies, for the
        rest of the process. Same as FontAssets.SearchPaths.Add(directory).

    static void ReportWorkingDirectoryChange(string directory)
        Prints "Changing working directory to: `...'" at INFO, for a host that
        wants its log to read like lilypond's.

    static void InstallSessionBindings(Interpreter interpreter)
        Installs the ly:parse-file and ly:parse-init Scheme bindings. Called for
        you when the init layer loads; you do not need it.

WHAT A RUN DOES, IN ORDER, because each step is a consumer-visible fact:

  1. Takes the process-wide engine lock and moves onto a large-stack thread.
     Calls are SERIALISED; you may call from any thread.
  2. Loads the layers if this is the first call (LilyPondInit.DefaultLayout).
  3. LilyPondInit.RestoreDefaults(): puts back everything the PREVIOUS file may
     have changed -- $defaultpaper/$defaultlayout/$defaultmidi, program options,
     note-name language, toplevel variables, session variables, document fonts,
     the default duration. A file cannot leak into the next one.
  4. Applies runOptions (Options list, then PointAndClick; MessageWriter swap).
  5. Parses: prologue (init.ly's session variables), your text, the version
     check, epilogue (book construction). Toplevel \score, \book, \markup and
     bare music reach their handlers DURING the parse.
  6. Processes every collected book: Book.Process -> PaperBook -> pages.
  7. Changes the CURRENT DIRECTORY to outputDirectory for the span in which
     files are written (the engine's file names are bare, as upstream's are),
     writes pages and MIDI, and changes back.
  8. Runs the expected-warnings check and closes any open output line.

BatchRunOptions
---------------
    public sealed class BatchRunOptions
        object          PointAndClick   { get; set; }   // null = leave default (true)
        IList<string>   Options         { get; set; }   // -d options, in order
        string          InputName       { get; set; }   // the INPUT's base name
        TextWriter      MessageWriter   { get; set; }   // this run's output
        CancellationToken CancellationToken { get; set; }

Everything here lives for ONE run: it is applied after the per-file restore, so
the next run's restore takes it off again -- the lifetime a per-process -d option
has upstream.

    PointAndClick   The point-and-click option: true (the default -- every note
                    head becomes an <a xlink:href="textedit://..."> anchor),
                    false (a publish build), a CodeBrix.LilyScheme.Values.Symbol
                    naming one event class, or a Scheme list of such symbols.
                    Applied AFTER Options, so it wins when both name it.
    Options         Each entry is the text that follows -d on a lilypond command
                    line, read by exactly that command line's rules:
                    "no-point-and-click" sets an option false, "debug-skylines"
                    sets one true, "include-settings=/path/to/house.ily" gives
                    one a value (and accumulates -- pass it several times to
                    include several files). A value the reader cannot make sense
                    of warns in upstream's own words -- byte for byte, including
                    the way a read error inside the value has its own source
                    name, line and column stripped and its "end of file" rewritten
                    as "end of string" -- and changes nothing; an undeclared name
                    warns "no such program option".
    InputName       The base name, WITHOUT extension, of the file this run's
                    text came from -- separate from the output base name, and
                    null to let the output base name answer for both. For a run
                    with no rename the two ARE the same name and this can be left
                    alone; they come apart the moment a caller renames the
                    output, and then this is the name the progress line,
                    `input-file-name', every diagnostic location and every music
                    object's `origin' use. RunFile fills it in from the path it
                    was given; RunText has no file to take it from, and its
                    callers pass the input's own base name as the output one, so
                    leaving it unset is right for them.
    MessageWriter   Receives everything the engine prints for this run --
                    progress, warnings and errors with file:line:col -- as it
                    prints. The process-wide Warn.Output is put back when the run
                    ends. Parse diagnostics ALSO land in BatchRunResult.Diagnostics
                    either way.
    CancellationToken
                    Cooperative, at the runner's own boundaries: before the
                    parse, between books, before output is written. One book's
                    engraving is one uninterruptible engine call. A cancelled run
                    throws OperationCanceledException and writes nothing further;
                    the next run's restore leaves the engine consistent.

BatchRunResult
--------------
    public sealed class BatchRunResult
        string                 SvgPath          // FIRST page written, or null
        IReadOnlyList<string>  SvgPaths         // every page, in page order
        IReadOnlyList<string>  MidiPaths        // every performance, in order
        int                    BookCount        // books the toplevel handlers made
        int                    SystemCount      // LINES the breaker chose (not scores)
        int                    SkippedEntries   // always 0 in a complete port
        int                    ErrorCount       // parse + epilogue errors
        IReadOnlyList<string>  Diagnostics      // parse-side messages, in order
        string                 DeclaredVersion  // the main input's \version, or null

All paths are absolute (outputDirectory made full). A file that parsed with
errors can still produce a page -- LilyPond recovers from a syntax error and
engraves what it kept -- so test ErrorCount, not just SvgPath. Diagnostics can
carry lines while ErrorCount is 0 (a missing \include is "cannot find file",
which is not a parse error). DeclaredVersion is what the lexer recorded, so an
editor deciding whether to offer a convert-ly update reads it rather than
re-scanning the text; a document with no \version gets the usual
"no \version statement found" warning on its first line.

OUTPUT FILE NAMES
-----------------
These are LilyPond's own rules (scm/framework-svg.scm and
scm/lily-library.scm), so a host that already knows lilypond's output knows
these:

    one-page book                <base>.svg
    multi-page book              <base>-<pageNumber>.svg, numbered from the
                                 book's first-page-number (a book that starts on
                                 page 3 writes -3 and -4 and has no -1)
    output-suffix set            <base>-<suffix>.svg (a toplevel
                                 `#(define output-suffix "x")' or a paper
                                 variable)
    several books, same key      <base>.svg, <base>-1.svg, <base>-2.svg ...
                                 (keyed by base name AND suffix together)
    first performance of a book  <base>.midi
    later performances of it     <base>-1.midi, <base>-2.midi ...
    a score with no \midi block  no MIDI file at all

/!\ MIDI NAMES ARE THE BOOK'S, AND THE PERFORMANCE COUNTER RESTARTS FOR EVERY
BOOK. The `<base>' above is the name computed for the BOOK being written, not
the input file's, because upstream writes performances once per book from that
book's own output name. So a file whose SECOND book performs writes
`<base>-1.midi' for its first performance and `<base>-1-1.midi' for its second
-- the book suffix and the performance suffix both present. A host that pairs
MIDI files with movements by name alone gets this wrong on any multi-book
document; read MidiPaths, which is in the order written.

STARTUP, LIFETIME AND THREADING
===============================
There is ONE engine per process. Its state lives in statics: the ambient
interpreter (LilyPondScheme.Current), the option table (LilyPondScheme.Options),
the parser session and output definitions (LilyPondInit), the diagnostics sink
(Warn), the font registry. This is LilyPond's own architecture -- its C++ reaches
one Guile through file-scope globals -- and the port keeps it because the
vendored Scheme assumes it.

Consequences, all of them load-bearing:

* Boot once, keep the process alive, engrave many files. A process per file
  pays the boot every time.
* Engine operations must not overlap. BatchRunner.RunText/RunFile take the lock
  for you. LilyPortEngraver, LilyPortPerformer and LilyParserSession do NOT;
  serialise them yourself (one SemaphoreSlim, as the host example below does).
* Every engine call must run on a LARGE STACK. LilyPond's Scheme (psyntax, the
  markup macros, deeply nested music) overflows the CLR's default 1 MB thread
  stack. BatchRunner and LilyPondInit wrap themselves in
  Interpreter.RunWithLargeStack; when you call the engine directly
  (LilyPortEngraver, LilyPortPerformer, LilyParserSession, evaluating Scheme),
  wrap the call yourself:

      Interpreter.RunWithLargeStack(() => { ... });        // Action
      T value = Interpreter.RunWithLargeStack(() => ...);  // Func<T>

  An exception thrown inside reaches you as itself, with its stack trace.
* A running Scheme evaluation cannot be interrupted. Cancellation is honoured
  between operations (and at BatchRunner's boundaries), never inside one.

The Scheme host bridge: LilyPondScheme
--------------------------------------
    namespace CodeBrix.LilyPort.Engine.Bootstrap;
    public static class LilyPondScheme

    static Interpreter CreateInterpreter()
        A CodeBrix.LilyScheme interpreter with the LilyScheme core loaded, every
        engine primitive (the ly:* procedures) installed, and the boot expansion
        cache attached. Publishes itself as Current.
    static LoadReport LoadViaLilyScm(Interpreter interpreter)
        Loads LilyPond's Scheme layer the way LilyPond does -- lily.scm first,
        which pulls in the rest. Returns what loaded and what failed.
    static Interpreter Current { get; }              // the ambient interpreter
    static ProgramOptions Options { get; }           // ly:set-option's table
    static EngineRegistries Registries { get; }      // grob interfaces,
                                                     //   translators, stencil heads
    static LoadReport CurrentLoadReport { get; }     // includes on-demand loads
    static object LookupProcedure(Symbol name)       // (lily) module, then current
    static object PublicRef(string[] moduleName, string name)
    static IReadOnlyList<string> LoadOrder()
    static IReadOnlyList<string> AllFiles()          // every vendored .scm
    static IEnumerable<string> VendoredNames()
    static string ReadSource(string name)            // a vendored .scm's text
    static string ReadInitFile(string name)          // a vendored ly/ file's text
    static IEnumerable<string> InitFileNames()       // the vendored ly/ files
    static string ReadSupportResource(string fileName)

    namespace CodeBrix.LilyPort.Engine;
    public sealed class LoadReport
        List<string> Loaded;  Dictionary<string, string> Failed;  int Total

You rarely call these: LilyPondInit (and therefore BatchRunner) creates and
loads an interpreter when none is ambient. Call them yourself when you want the
boot to happen at a time of YOUR choosing -- at application start, on a
background thread -- so the first engrave does not pay for it:

    Interpreter.RunWithLargeStack(() =>
    {
        Interpreter interpreter = LilyPondScheme.CreateInterpreter();
        LoadReport report = LilyPondScheme.LoadViaLilyScm(interpreter);
        // report.Failed is empty on a healthy boot
        LilyPondInit.DefaultLayout();   // the ly/ init layer + parse tables
    });

The session: LilyPondInit
-------------------------
    namespace CodeBrix.LilyPort;
    public static class LilyPondInit

    static OutputDef DefaultLayout()
        The $defaultlayout the ly/ init layer builds (every context definition),
        loading both layers on first use. Boots the interpreter if none is
        ambient.
    static OutputDef DefaultPaper()
        The $defaultpaper (page size, margins, fonts, output-scale), or null.
    static LilyParserSession Session()
        THE parser session the init layer was read into. There is deliberately
        one; BatchRunner parses every file through it. Use it for parse-only
        work (see THE PARSER) and for LookupIdentifier("$defaultmidi").
    static void RestoreDefaults()
        Puts the shared session back the way the init layer left it: paper,
        layout and midi definitions, program options, note names, toplevel and
        session variables, document fonts, default duration. BatchRunner calls
        it before every run; call it yourself after driving the session directly.
    static void Reset()
        Forgets the cached layout so the next call reloads both layers (a full
        re-boot of the ly/ layer; expensive).
    static IReadOnlyList<string> Diagnostics { get; }
        What the init layer reported when it loaded. Empty on a healthy boot.

THE BOOT CACHE: BootExpansionCache
----------------------------------
    namespace CodeBrix.LilyPort.Engine.Bootstrap;
    public static class BootExpansionCache

Macro-expanding the vendored Scheme is ~99% of a cold boot; replaying a recorded
expansion is milliseconds. The first boot on a machine records the expansion
into a per-user cache file and every later process replays it. Measured on a
fast Linux workstation: about 35 s cold, about 4 s warm, to a booted engine with
its first page engraved.

    const string EnabledVariable   = "LILYPORT_EXPANSION_CACHE"     // "0" disables
    const string DirectoryVariable = "LILYPORT_EXPANSION_CACHE_DIR" // override
    static bool Enabled { get; }
    static string CacheDirectory { get; }
        $XDG_CACHE_HOME/CodeBrix.LilyScheme, else ~/.cache/CodeBrix.LilyScheme
        (Linux), ~/Library/Caches/CodeBrix.LilyScheme (macOS),
        %LOCALAPPDATA%\CodeBrix.LilyScheme (Windows).
    static string CacheFilePath { get; }     // boot-<16 hex>.lsxc, keyed to the
                                             //   exact assembly builds
    static ExpansionCache Acquire()          // attached by CreateInterpreter
    static void SaveIfDirty(Interpreter interpreter)   // done by LoadViaLilyScm
    static void ResetProcessMemo()

The key is a hash over the LilyScheme and Engine assembly identities and every
embedded .scm, so a package upgrade means one cold boot and then warm boots
again; a corrupt or foreign file is simply a miss. A boot that cannot write its
cache still boots. Old generations are pruned (three kept). Set the environment
variable to a directory your application owns if the per-user default is not
writable in your deployment.

THE DIRECT SURFACE: LilyPortEngraver AND LilyPortPerformer
==========================================================
Below BatchRunner sit two static classes that take ONE music tree and give back
its layout or its performance, without a book, a paper or any file. They exist
for hosts that build music themselves (from Scheme, or from a parsed
expression) and want the drawing in memory.

    namespace CodeBrix.LilyPort;
    public static class LilyPortEngraver
        static EngraveResult Engrave(MusicObject music, OutputDef layout = null)
        static string EngraveToSvg(MusicObject music, OutputDef layout = null)
            layout null takes LilyPondInit.DefaultLayout(). Builds the context
            tree from the layout's context definitions, iterates the music,
            runs spacing and line breaking, stacks the lines by the real page
            layout problem (ragged, no page), and answers the stencil.
            ⚠ NOT wrapped in RunWithLargeStack and NOT serialised: do both.

    public sealed class EngraveResult
        GlobalContext Global            // the root context the run used
        ScoreEngraver ScoreEngraver     // owns the PaperScore
        SystemGrob    System            // the first broken piece (or the root)
        Stencil       Stencil           // every line, stacked
        int           LineCount         // lines the breaker chose
        PaperScore    PaperScore        // or null
        static IReadOnlyList<string> MissingTranslators()
            Translators ly/engraver-init.ly names that the engine cannot make.
            Empty in a complete port; a non-empty answer names a FEATURE gap
            rather than a wrong result.

    public static class LilyPortPerformer
        static Performance Perform(MusicObject music, OutputDef midi)
            midi is a \midi output definition -- typically
            LilyPondInit.Session().LookupIdentifier("$defaultmidi") as OutputDef.
            Builds the tree from performer-init.ly's definitions, iterates,
            calls Performance.Process. Null when nothing was performed.

    namespace CodeBrix.LilyPort.Engine.Layout;
    public sealed class Performance : MusicOutput
        IList<AudioStaff> AudioStaffs
        OutputDef Midi { get; set; }
        object Headers
        void Process()
        void WriteOutput(string output, string performanceName)
            Writes a Standard MIDI File to the path (relative to the current
            directory). A performance with no staffs warns and writes nothing.
        void Output(MidiStream midiStream, string performanceName)
            Writes into a MidiStream instead (MidiStream(string fileName) is
            IDisposable and buffers until Dispose; ToBytes() answers the bytes).

Getting a MusicObject: parse a music EXPRESSION through the shared session
(ParseStringExpression, below), or build one in Scheme with make-music and cast
the value the interpreter returns:

    Interpreter.RunWithLargeStack(() =>
    {
        LilyParserSession session = LilyPondInit.Session();
        MusicObject music = (MusicObject)session.ParseStringExpression(
            "\\relative { c'4 d e f }", "<host>", 1);

        string svg = LilyPortEngraver.EngraveToSvg(music);

        OutputDef midi = session.LookupIdentifier("$defaultmidi") as OutputDef;
        Performance performance = LilyPortPerformer.Perform(music, midi);
        performance?.WriteOutput("direct.midi", "direct");
    });

The direct surface engraves one SCORE against an unscaled layout with no page,
so headers, titles, page numbers, \paper variables and multi-score books are
BatchRunner's job, not this one's.

THE SVG BACKEND: SvgBackend
===========================
    namespace CodeBrix.LilyPort.Backends;
    public sealed class SvgBackend : IStencilSink
        int    Precision  { get; set; }   // decimals per coordinate (4)
        double UnitLength { get; set; }   // mm per staff space; the layout's
                                          //   output-scale (1.7573 = 20 pt staff)
        List<(object Grob, Offset At)> Causes          // grobs that drew, in order
        List<string> UnhandledCommands                 // commands not understood
        string Body                                    // fragment so far
        void   Clear()
        string RenderFragment(Stencil stencil)         // no document wrapper
        string RenderDocument(Stencil stencil)         // complete <svg> document
        object Output(object expression)               // one drawing command

    namespace CodeBrix.LilyPort.Engine.Layout;
    public interface IStencilSink
        object Output(object expression);   // false = not understood

What the document looks like, because it decides what a viewer needs:

* width/height are MILLIMETRES (the stencil's extent times UnitLength); the
  viewBox is in staff spaces. Set UnitLength from the paper the score was laid
  out under -- LilyPondInit.DefaultPaper().GetDimension("output-scale") -- or
  the page is 1/output-scale of its real size. BatchRunner does this per book.
* Music glyphs are written as OUTLINE PATHS (<path d="...">) taken from the
  Emmentaler SVG companions, so no music font is needed to view the output.
* Text is written as <text font-family="serif" ...> (or "sans", "monospace"),
  which is what LilyPond's own SVG backend writes: the ENGINE measured the text
  with its vendored faces (C059, Nimbus Sans, Nimbus Mono PS, TeX Gyre), but the
  VIEWER resolves the generic family through its own fonts. Text will fit
  exactly only in a viewer whose serif/sans/monospace map to those faces; a
  PDF converter that lets you choose fonts should be pointed at them.
* With point-and-click on (the default), every grob with an input origin is
  wrapped in <a xlink:href="textedit://<absolute file>:<line>:<char>:<col>">.
  The file is made absolute against the CURRENT directory at draw time -- the
  output directory, for BatchRunner -- so RunText anchors name
  "<outputDirectory>/<baseName>.ly". Pass PointAndClick = false for output you
  publish, or RunFile the real file into its own directory if you want anchors
  that resolve.
* The xlink namespace is bound and the document is well-formed XML.

Stencil, the thing a backend draws:

    namespace CodeBrix.LilyPort.Engine.Layout;
    public sealed class Stencil
        static Stencil Empty
        object   Expression        // the Scheme drawing expression
        Interval XExtent, YExtent  // in staff spaces; Y measures UPWARD
        Box      ExtentBox
        bool     IsEmpty
        void AddStencil(Stencil other)
        void Translate(Offset offset);  void TranslateAxis(double amount, Axis axis)
        Stencil Translated(Offset offset)
        void Scale(double x, double y);  void AlignTo(Axis axis, double position)
        void AddAtEdge(Axis axis, Direction direction, Stencil other, double padding)
        Stencil InColor(double red, double green, double blue, double alpha = 1.0)
        Stencil InColor(string cssColor)

DIAGNOSTICS: Warn, LogLevel, LilyPondErrorException, ProgramOptions
=====================================================================
    namespace CodeBrix.LilyPort.Flower;
    public static class Warn

Every layer -- the C# engine and the vendored Scheme's ly:warning family alike
-- reports through this one static class, with LilyPond's own prefixes and
wording ("warning: ", "error: ", "fatal error: ", "programming error: "). It is
process-wide.

    static LogLevel   Level { get; set; }          // default LogLevel.LevelInfo
    static TextWriter Output { get; set; }         // default: Console.Error,
                                                   //   wrapped in LineTrackingWriter
    static bool       RecordMessages { get; set; } // also keep them in memory
    static IReadOnlyList<string> Messages { get; } // what was recorded
    static void       ClearMessages()
    static bool       WarningAsError { get; set; } // -dwarning-as-error
    static Func<bool> WarningAsErrorSource { get; set; }
    static bool       IsEnabled(LogLevel severity)

    static void Warning(string message, string location = null)
    static bool DeprecationWarning(string message, string location = null)
    static void Message(string message, string location = null)   // INFO
    static void Progress(string message)                          // PROGRESS
    static void BasicProgress(string message)                     // BASIC
    static void Debug(string message)                             // DEBUG
    static void ProgrammingError(string message, string location = null)
    static void NonFatalError(string message, string location = null)
    static void Error(string message, string location = null)
        Where LilyPond calls exit(): prints "fatal error: " and THROWS
        LilyPondErrorException. A library cannot end your process, so the
        decision is yours.
    static void DeferrableError(string message, string location = null)
    static void ExpectWarning(string message)     // ly:expect-warning
    static void CheckExpectedWarnings()           // BatchRunner calls it per file
    sealed class WarningAsErrorExitDeferrer : IDisposable

    public enum LogLevel   // flags
        None, Error, Warn, Basic, Progress, Info, Debug,
        LevelError, LevelWarn, LevelBasic, LevelProgress, LevelInfo, LevelDebug

    public sealed class LilyPondErrorException : Exception
        LilyPondErrorException(string message)
        LilyPondErrorException(string message, string location)
        string Location { get; }

    public sealed class LineTrackingWriter : TextWriter
        LineTrackingWriter(TextWriter inner)
        bool AtLineStart { get; }
        void EndOpenLine()

WHERE TO READ A RUN'S DIAGNOSTICS -- three places, choose by scope:

    BatchRunResult.Diagnostics      parse-side messages of THAT run
                                    ("file.ly:2:15: syntax error, unexpected '}'",
                                    "cannot find file: `x.ily'") -- always
                                    collected, whatever Level says.
    BatchRunOptions.MessageWriter   everything printed for THAT run, at the
                                    current Level, without touching the
                                    process-wide writer.
    Warn.RecordMessages/Messages    everything from every layer, process-wide,
                                    recorded REGARDLESS of Level (debug lines
                                    included). Clear between files yourself.

Warn.Level is LevelInfo by default, as lilypond's is: progress lines
("Interpreting music...", "Drawing systems...") are printed to Console.Error.
Set LogLevel.LevelWarn for a quiet host, LevelDebug to see the Scheme layer's
on-demand loads.

Errors you can meet from a run:

    LilyPondErrorException   a fatal error (Warn.Error): e.g. a font the document
                             registered cannot be read, or a warning promoted by
                             -dwarning-as-error. Propagates out of RunText.
    ParseAbortedException    (CodeBrix.LilyPort.Parsing.Driver) the parser hit
                             end of input while recovering from a syntax error --
                             a TRUNCATED document, an unclosed brace. Propagates
                             out of RunText; ordinary syntax errors do not (they
                             are counted in ErrorCount and the run continues).
    OperationCanceledException   the run's token was cancelled.
    NotPortedException       (CodeBrix.LilyPort.Engine) Scheme called an entry
                             point the port declares not applicable -- the
                             PostScript/Ghostscript bindings (ly:spawn, ly:gs-api)
                             and the like. The message names the upstream file.
    CodeBrix.LilyScheme.Runtime.SchemeThrow   an uncaught Scheme-level throw
                             (ly:error, a wrong-type-arg in embedded Scheme).
    "book processing failed: ..."   NOT an exception: an engraving failure inside
                             one book is caught and reported as a Diagnostics
                             line, and the run goes on to the next book.

The option table: ProgramOptions and CommandLineOptions
-------------------------------------------------------
    namespace CodeBrix.LilyPort.Engine.Bootstrap;
    public sealed class ProgramOptions
        object Get(string name)            // false when undeclared
        void   Set(string name, object value)
        bool   IsDeclared(string name)
        string Documentation(string name)
        object ToAlist()
        IReadOnlyDictionary<string, object> SnapshotValues()
        void   RestoreValues(IReadOnlyDictionary<string, object> snapshot)
        TextWriter Output { get; set; }    // bookkeeping sink; printing is Warn's
        List<string> Messages;  int WarningCount

    public static class CommandLineOptions
        static void Apply(ProgramOptions options, string argument)
            One -d option's text ("no-point-and-click",
            "include-settings=/p/x.ily"), read with lilypond's own rules.

    public enum MessageSeverity { Debug, Progress, Message, Warning, Error }
    public enum OptionValueSyntax { StringOrBoolean, String, StringOrFalse, Read }

LilyPondScheme.Options is the live table (what ly:get-option reads and
ly:set-option writes). Prefer BatchRunOptions.Options for per-run settings: a
value you Set directly is put back by the next run's restore anyway, so only the
per-run route has a predictable lifetime.

THE PARSER: LilyParserSession
=============================
    namespace CodeBrix.LilyPort.Parsing.Session;
    public sealed partial class LilyParserSession

    LilyParserSession(Interpreter interpreter)
        A FRESH session over a loaded interpreter, with NO init layer -- it does
        not know what a Staff or a note name is until LoadInitLayer() has run,
        and the vendored layer's session guards mean only ONE session per
        process should do that. In practice: use LilyPondInit.Session().

    ParseOutcome ParseText(string text, string fileName)
    ParseOutcome ParseText(string text, string fileName, string startToken)
        Parses a whole document. The parse IS the execution: a toplevel \score
        reaches its handler from the rule that reduces it. Recoverable syntax
        errors are counted and reported; end of input during recovery throws
        ParseAbortedException.
    object ParseStringExpression(string code, string fileName, int line)
        Parses ONE music expression (upstream's ly:parse-string-expression) and
        answers it -- a MusicObject for music. The parse-only way to get a
        MusicObject for LilyPortEngraver.
    void ParseString(string code);  void IncludeString(string code)
    ParseOutcome LoadInitLayer()
    List<string> IncludePath { get; }
        Directories an \include searches after the vendored ly/ layer and after
        the INCLUDING FILE'S OWN DIRECTORY. The full order is: the vendored ly/
        files (found by name, and they cannot be shadowed), then the directory
        of the file the \include was read from (or, for text handed over under
        a bare name, MainInputDirectory below) -- so a piece laid out in
        subdirectories reaches its siblings by bare name and its neighbours
        through "../other/x.ily" -- then these directories in order. An absolute
        include name is used as it stands.
        This is the port's `global_path': what a host's -I entries and
        ly:parser-append-to-include-path go on, and THE ONLY ONE OF THE TWO
        LISTS THAT SCHEME CAN SEE -- ly:find-file, ly:parse-file and
        ly:parse-init search this and nothing else.
    string MainInputDirectory { get; set; }
        The directory the MAIN INPUT came from, which is what an \include read
        from the main input resolves against when that input was handed over as
        text under a bare name (the batch runner sets it, and restores it, on
        every run).
        /!\ IT IS DELIBERATELY NOT PART OF IncludePath, AND THE SEPARATION IS
        UPSTREAM'S. Upstream keeps two lists: the lexer's own current directory,
        which only \include consults, and the include path, which is what the
        Scheme file-finding procedures search. The main input's directory is on
        the first and NOT on the second: with an asset sitting beside the input
        and the process's working directory elsewhere,
        `#(ly:find-file "asset.txt")' answers #f. Putting the input's directory
        on IncludePath instead -- which the batch runner once did -- makes the
        engine find files upstream cannot, so a document that works here would
        fail against the real program. If you WANT a directory visible to both,
        add it to IncludePath yourself.
    object LookupIdentifier(string name)
    void SetIdentifier(object key, object value)
    string OutputBaseName { get; set; }
    string MainInputVersionString { get; set; }
    List<string> Diagnostics { get; }  int ErrorLevel { get; set; }
    int LexerErrorLevel { get; set; }
    IReadOnlyList<SourceFile> SourceFiles
    bool IsMusic(object value);  bool IsScore(object value)
    bool IsMarkup(object value)
    object NoteNames();  void SetNoteNames(object names)
    void SnapshotToplevelScope();  void RestoreToplevelScope()
    object AsCurrentParser(Func<object> action)   // publishes %parser while inside
    Interpreter Interpreter;  SchemeModule LilyModule
    static ParseTables Tables                     // the LALR tables, once per process

    public sealed class ParseOutcome
        object Result;  int ErrorCount
        IReadOnlyList<string> Diagnostics;  IReadOnlyList<string> LexerErrors
        bool Success                                // no errors of either kind
        IReadOnlyList<string> AllDiagnostics()

    namespace CodeBrix.LilyPort.Parsing.Driver;
    public sealed class ParseAbortedException : Exception
    public readonly struct SourceSpan          // FileName + offsets, in locations

Parse-only use, for an editor's syntax check:

    Interpreter.RunWithLargeStack(() =>
    {
        LilyParserSession session = LilyPondInit.Session();
        session.IncludePath.Add(projectDirectory);
        try
        {
            ParseOutcome outcome = session.ParseText(text, "check.ly");
            // outcome.Success, outcome.ErrorCount, outcome.AllDiagnostics()
        }
        catch (ParseAbortedException aborted) { /* truncated input */ }
    });
    LilyPondInit.RestoreDefaults();   // undo whatever the text defined

Parsing a whole document through the shared session runs its toplevel handlers
(a \score is collected, a toplevel assignment lands in the shared scope) and the
prologue BatchRunner would have parsed first is absent, so treat the result as
diagnostics only, and RestoreDefaults afterwards. To ENGRAVE, use BatchRunner.

CONVERT-LY: DocumentConverter
=============================
    namespace CodeBrix.LilyPort.ConvertLy;
    public static class DocumentConverter

The in-process convert-ly: every conversion rule from LilyPond 1.2.3 up to the
port's release, ported rule for rule from convertrules.py (the patterns are the
verbatim python expressions, translated to .NET regular expressions at run
time), verified against upstream's own output on a corpus of real files. No
engine, no boot: it answers in milliseconds.

    static IReadOnlyList<ConversionRule> Rules { get; }     // all, in order
    static ConversionVersion LatestVersion { get; }         // the default target
    static bool TryReadDeclaredVersion(string text, out ConversionVersion version)
    static bool TryReadDeclaredVersion(string text, out ConversionVersion version,
                                       out bool malformed)
        The \version a document declares. malformed: a version line exists but
        is unusable (an odd minor with no third component).
    static ConversionResult Convert(string text,
                                    ConversionVersion? from = null,
                                    ConversionVersion? to = null)
        Applies, in order, every rule with from < rule.Version <= to, and
        rewrites the \version line to the version reached. from null: the
        document's own \version (VersionUnknown when it has none). to null:
        LatestVersion. Line endings are normalised to \n first, as upstream
        does. A rule that must give up stops the run at the last successful
        rule, exactly as upstream.
    static IReadOnlyList<ConversionRule> RulesBetween(ConversionVersion from,
                                                      ConversionVersion to)
        What --show-rules prints: the rules that WOULD run.

    public readonly struct ConversionVersion : IComparable<ConversionVersion>,
                                               IEquatable<ConversionVersion>
        ConversionVersion(int major, int minor, int patch)
        int Major, Minor, Patch;  bool IsUnstable      // odd minor
        static bool TryParse(string text, out ConversionVersion version)
        <, >, <=, >=, ==, != ;  ToString() => "2.24.0"

    public sealed class ConversionRule
        ConversionVersion Version;  string Message;  Func<string, string> Convert

    public sealed class ConversionResult
        string Text                     // the converted document
        ConversionVersion FromVersion, ToVersion
        ConversionVersion? LastRuleApplied, LastChange, StampedVersion
        IReadOnlyList<ConversionVersion> AppliedRules
        IReadOnlyList<string> Messages  // "Not smart enough to convert ..."
        int  Errors                     // rules that gave up
        bool Changed                    // text differs from input
        bool VersionUnknown             // no usable \version to start from

    public sealed class FatalConversionError : Exception   // thrown BY a rule
    public static class PythonRegex                        // python-flavoured
        static Match Search(string pattern, string text, ...)   //   regex helpers
        static string Sub(...);  static List<string> FindAll(...);  ...

The version stamped is the last rule that actually CHANGED the text (an
unstable series is rounded up to the next stable release), and a document that
comes out unchanged keeps the version it had. Messages are the part of a
conversion that still needs a human: show them.

THE IMPORTERS: ABC, MIDI AND MusicXML TO LILYPOND SOURCE
========================================================
    namespace CodeBrix.LilyPort.Importers;

Ports of abc2ly, midi2ly and musicxml2ly: text or bytes in, LilyPond source and
the converter's own messages out. No engine, no boot. Each carries exactly
upstream's support (and upstream's limitations: ABC is incomplete in the same
ways, MusicXML is the same subset), verified against upstream's output on its
own test corpora. The option classes are named after the scripts' LONG options.

    public static class AbcImporter
        static ImportResult Import(string abcText, AbcImportOptions options = null)
    public sealed class AbcImportOptions
        bool   Strict           // -s: stop at the first thing not understood
        bool   Beams            // -b: keep ABC's own beaming
        string SourceName       // what diagnostics call the input (port-only)

    public static class MidiImporter
        static ImportResult Import(byte[] midiData, MidiImportOptions options = null)
    public sealed class MidiImportOptions
        bool   AbsolutePitches      // -a
        int?   DurationQuant        // -d
        bool   ExplicitDurations    // -e
        IList<string> IncludeHeader // -i, paths, each copied into the output
        string Key                  // -k "ALT[:MINOR]": +sharps/-flats, minor 1
        bool   Preview              // -p: first four bars only
        bool   Skip                 // -S: s instead of r for rests
        int?   StartQuant           // -s
        IList<string> AllowTuplet   // -t "DUR*NUM/DEN", may be given several times
        bool   TextLyrics           // -x
        string SourceName           // the tag line's file name (port-only)

    public static class MusicXmlImporter
        static ImportResult Import(string xmlText, MusicXmlImportOptions options = null)
        static ImportResult ImportCompressed(byte[] mxlData,
                                             MusicXmlImportOptions options = null)
            A .mxl container: the manifest is followed to the score inside.
    public enum MusicXmlPitchMode { Relative, Absolute }      // -r / -a
    public sealed class MusicXmlImportOptions
        MusicXmlPitchMode PitchMode   // Relative by default
        string Language               // -l, e.g. "deutsch"
        string OttavasEndEarly        // --oe "t"/"f"
        bool NoArticulationDirections, NoRestPositions, NoSystemBreaks,
             NoPageBreaks, NoPageMargins, NoPageLayout, NoStemDirections,
             AbsoluteFontSizes, NoBeaming, Midi, Fretboards, Book, NoTagline
        double? DynamicsScale         // --ds; 0 = LilyPond's size, null = document's
        int    CreditPage = 1         // --cp
        string Transpose              // --transpose, a pitch name
        int    ShiftDurations         // --sd; -1 doubles, 1 halves
        string TabClef                // "tab" or "moderntab"
        string StringNumbers          // --sn "t"/"f"
        string SourceName             // the header comment's file name (port-only)

    public sealed class ImportResult
        string Text                    // LilyPond source, or NULL when nothing
                                       //   could be converted
        IReadOnlyList<string> Messages // upstream's stderr, one line each
        int  Errors
        bool Succeeded                 // Text != null && Errors == 0

What comes back is ordinary LilyPond source -- hand it to BatchRunner.RunText.
It declares `\version "2.24.0"', which is the release the upstream converters
froze their output syntax at (not the engine's own 2.27.2); the engine reads it
fine. A midi2ly transcription is a transcription, not a score: MIDI carries no
beams, slurs or accidental spelling, and the document relies on the completion
engravers to make bars.

Failure is an ImportResult, never an exception: bad bytes give Text null,
Errors 1 and a message ("midi2ly: error: expected b'MThd', got b''",
"musicxml2ly: Central Directory corrupt."). Strict ABC mode fails the same way
where the default carries on with a hole.

FONTS
=====
    namespace CodeBrix.LilyPort.Engine.Fonts;

NOTHING NEEDS TO BE ON DISK. The nine Emmentaler music fonts (sizes 11, 13, 14,
16, 18, 20, 23, 26 and the brace font), their nine SVG outline companions, and
the 24 text faces are embedded resources in the Engine assembly; the engine
reads them by name. There is no fontconfig and no system-font lookup at all.

    public static class FontAssets
        static IList<string> SearchPaths                 // directories consulted
                                                         //   BEFORE the embedded
                                                         //   copies; empty by default
        static byte[] MusicFont(string name)             // "emmentaler-20" -> OTF bytes
        static string OutlineFont(string name)           // its SVG font text
        static byte[] TextFont(string fileName)          // "C059-Roman.otf"
        static string TextFontLocation(string fileName)  // "<asm>.dll/<resource>"
        static IEnumerable<string> TextFontNames()       // the 24 file names

    public static class TextFontChain
        static IReadOnlyList<TextFace> For(string family, bool bold, bool italic)
            The faces a family resolves through, in order:
                serif      -> C059 (Roman/Bold/Italic/BdIta) -> TeX Gyre Schola
                sans       -> Nimbus Sans                    -> TeX Gyre Heros
                typewriter -> Nimbus Mono PS                 -> TeX Gyre Cursor
            and then STOPS. A document-registered family comes first.
        static TextFace Face(string fileName)
        static IReadOnlyList<TextFace> VendoredFaces()
        static string VendoredFaceLocation(string family)
        static bool AddDocumentFont(string path)         // ly:font-config-add-font
        static int  AddDocumentFontDirectory(string directory)
                                                         // ly:font-config-add-directory
        static void ResetDocumentFonts()                 // RestoreDefaults does it
        static TextFace DocumentFont(string family)
        static IReadOnlyList<KeyValuePair<string, TextFace>>
                    DocumentFontRegistrations()
        static void Reset()

    public sealed class TextFace
        static TextFace Load(string fileName)        // vendored, by file name
        static TextFace LoadFromPath(string path)    // any OpenType/CFF file
        string FamilyName;  string FileName;  string SourcePath;  int UnitsPerEm
        bool Covers(int codePoint);  int GlyphIndex(int codePoint)
        double Advance(int glyph);  double Kerning(int leftGlyph, int rightGlyph)

    public static class AllFontMetrics
        static IList<string> SearchPaths             // same list as FontAssets'
        static OpenTypeFontMetric FindOtfFont(string name)
        static void ResetAllFonts()

Three consequences a consumer must know:

* A code point none of the 24 faces covers draws the .notdef "tofu" box, with a
  "no glyph for character" warning, BY DESIGN. There is no fallback to the
  operating system's fonts. CJK, Hebrew and Arabic scripts are therefore
  unsupported unless the DOCUMENT supplies a font.
* A document CAN supply its own fonts, with LilyPond's own commands:
      #(ly:font-config-add-font "/abs/path/MyFont.otf")
      #(ly:font-config-add-directory "/abs/path/fonts")
  and then name the family in \override or \paper { fonts ... }. Relative paths
  resolve against the process's current directory; a file that cannot be read
  is a FATAL error (LilyPondErrorException). Registrations last for ONE file
  (the per-file restore clears them). A document face is consulted ALONE for
  its family -- no vendored face is chained behind it -- and any family name
  that is neither generic nor registered falls to the serif chain's TeX Gyre
  Schola.
* Font FILES can be substituted process-wide by name through
  FontAssets.SearchPaths (or BatchRunner.UseFontsFrom): a directory holding
  emmentaler-20.otf replaces that one face and nothing else. This exists for
  measurement against other Emmentaler builds; it is not a way to add families.

LilyPortInfo
============
    namespace CodeBrix.LilyPort;
    public static class LilyPortInfo
        static string Version               // the PACKAGE version, date-stamped
        static string CompatibleWithVersion // "2.27.2": the LilyPond release
                                            //   this engine reads and reports as
        static string UpstreamCommit        // the LilyPond commit ported
        static string UpstreamUrl           // https://gitlab.com/lilypond/lilypond

Version and CompatibleWithVersion are two different things and must never be
conflated: a `\version "2.27.2"' in a document is read against the second; the
first moves with every package release and is never a LilyPond version. The
engine's own view of the same fact is LilyVersion.CompatibleWithVersion (a
const, CodeBrix.LilyPort.Engine.Bootstrap) and LilyVersion.VersionString().
UsageText.Text (same namespace) is the `lilypond --help' text the ly:usage
binding prints.

THE REST OF THE ENGINE: A MAP BY FEATURE AREA
=============================================
The Engine assembly has several hundred public types because it is the whole of
lily/. You will not construct most of them; you meet them when you read what
BatchRunner or LilyPortEngraver produced, or when you call Scheme. Every type
keeps its upstream name (Paper_column -> PaperColumn, Note_head -> NoteHead), so
the Internals Reference is the type catalogue.

CodeBrix.LilyPort.Engine.Music -- the music tree
    MusicObject : Prob            a music expression; GetProperty("elements"),
                                  IsMusicType("NoteEvent"), GetLength(),
                                  StartMoment(), Origin, Clone()
    Moment                        (MainPart, GracePart) Rationals; Zero, Infinity
    Duration, Pitch, PitchInterval, Scale, StreamEvent, MusicFunction
    MusicFactory.MakeMusic(Symbol name)

CodeBrix.LilyPort.Engine.Objects -- probs, grobs, books and scores
    Prob                          property object: GetProperty(string)/SetProperty
    Grob : Prob                   Name, Layout (OutputDef), GetProperty,
                                  XParent/YParent, Original, IsLive
      Item, Spanner, PaperColumn, SystemGrob, NoteHead, Stem, Beam, Slur, Tie,
      Rest, Clef, StaffSymbol, TextInterface, ... (every lily/*.cc grob class)
    Book                          Paper, Header, Scores, Bookparts;
                                  PaperBook Process(...)
    Score                         Defs (the \layout/\midi output definitions)
    SchemeUtilities               Assq, LyAssocGet, CallCallback, IsString, ...

CodeBrix.LilyPort.Engine.Translation -- contexts and translators
    Context                       ContextName, IdString, Parent, Children,
                                  GetProperty(string), Implementation, NowMoment
    GlobalContext, ContextDef     ContextDef.FindContextDef(OutputDef, object name)
    Translator, EngraverGroup, PerformerGroup, TranslatorRegistry
      TranslatorRegistry.GetTranslatorCreator(Symbol), MissingTranslators(...)
    ScoreEngraver, ScorePerformer, StaffPerformer.ResetStaticChannelState()
    every *Engraver and *Performer class, and every *Iterator

CodeBrix.LilyPort.Engine.Layout -- output definitions, layout, pages
    OutputDef                     a \paper/\layout/\midi block:
                                  LookupVariable(Symbol), CVariable(string),
                                  SetVariable(string, object),
                                  GetDimension(string), Variables(), Parent,
                                  Clone(), ScaledClone(double), Normalize()
    Stencil, IStencilSink, StencilInterpreter, Box, Skyline, SkylinePair
    PaperBook                     Paper, Pages(), Performances(), Output(),
                                  BookTitle()
    PaperScore, PaperSystem, PageBreaking (+ Optimal/OnePage/OneLine/
      PageTurn variants), PageLayoutProblem, Performance, MusicOutput,
      PaperDefaults, Dimensions (unit factors), Spring, Rod, SimpleSpacer

CodeBrix.LilyPort.Engine.Audio -- the MIDI side
    AudioStaff, AudioNote, AudioTempo, AudioKey, AudioTimeSignature,
    AudioText, AudioControlChange, ... (audio elements)
    MidiStream (IDisposable; Write, ToBytes), MidiChunk, MidiTrack,
    MidiHeader, MidiNote, MidiWalker, MidiItem ...

CodeBrix.LilyPort.Engine.Origins -- source locations
    Input                         LocationString(), FileString(), LineNumber(),
                                  ColumnNumber()
    SourceFile, Sources, PointAndClick.FormatUrl(Input)

CodeBrix.LilyPort.Engine.Bootstrap -- the Scheme bridge and tables
    LilyPondScheme, ProgramOptions, CommandLineOptions, BootExpansionCache,
    LilyVersion, UsageText, EngineRegistries, SchemeConvert (ToDouble, ToInt,
    ToLong, TryToRational, IsNumber), EngineSupport, the *Primitives and
    *Callbacks classes that install the ly:* procedures

CodeBrix.LilyPort.Engine (root)
    LoadReport, NotPortedException, EntryPoint, PortLedger (the file-by-file
    provenance ledger: Ported, NoPort, Rows)

CodeBrix.LilyPort.Flower -- the utility layer
    Warn, LogLevel, LilyPondErrorException, LineTrackingWriter
    Rational                      exact fractions with Infinity and NaN
    Interval                      (Left, Right); IsEmpty, Length, Center, Unite
    Offset (X, Y), Axis { X, Y }, Direction (Negative/Center/Positive), DrulArray
    Bezier, Polynomial, Matrix, IntervalSet, Slice, FileName, FilePath,
    StringConvert, PriorityQueue<T>

Scheme values you will see: a property alist is a CodeBrix.LilyScheme.Values.Pair
list of (Symbol . value); strings are MutableString; numbers are long, double,
Ratio or Flower Rational; #f is bool false; the empty list is Nil.Instance.
SchemeConvert and SchemeUtilities are the engine's own readers for them.

LICENSING AND REDISTRIBUTION
============================
The package is GPL-3.0-only. What that means for an application that references
it:

* Your application becomes a work based on the package and, when conveyed to
  anyone, must be licensed under GPL version 3 with its complete corresponding
  source available. Internal use with no distribution carries no obligation.
* "Only", not "or later": because articulate.ly is GPL-3-only, you cannot
  relicense the combination under a later GPL version.
* The music fonts are dual-licensed GPL-with-font-exception / SIL OFL 1.1
  ("Emmentaler" and "Feta" are Reserved Font Names); the text faces are URW
  base35 (AGPL-3.0 with the font-embedding exception) and TeX Gyre (GUST Font
  License). All ship unmodified inside the Engine assembly, and the SVG the
  engine writes contains NO font data -- glyphs are outline paths and text is
  text with a family name -- so a rendered document does not carry a font
  license with it.
* The Scheme interpreter dependency is LGPL-3.0-or-later; a GPL work may consume
  it freely.
* LICENSE, LICENSE.OFL and THIRD-PARTY-NOTICES.txt travel in the package root;
  keep them with any redistribution.

COMPLETE EXAMPLES
=================
All examples below were compiled and run against the package's assemblies.
They assume `using CodeBrix.LilyPort;' and the usings named in each.

EXAMPLE 1: engrave a file the way `lilypond -o' does
----------------------------------------------------
    using System;
    using System.IO;
    using CodeBrix.LilyPort;

    static void Engrave(string inputPath, string outputOption)
    {
        // An existing directory is a directory; anything else is a name.
        BatchRunner.SplitOutputName(
            outputOption, out string directory, out string baseName);
        string outputDirectory =
            directory ?? Path.GetDirectoryName(Path.GetFullPath(inputPath));

        // The file's own directory is its \include root.
        BatchRunResult result = BatchRunner.RunFile(inputPath, outputDirectory, baseName);

        foreach (string page in result.SvgPaths) { Console.WriteLine("page " + page); }
        foreach (string midi in result.MidiPaths) { Console.WriteLine("midi " + midi); }
        foreach (string line in result.Diagnostics) { Console.Error.WriteLine(line); }
        Environment.ExitCode = result.ErrorCount == 0 ? 0 : 1;
    }

EXAMPLE 2: SVG pages plus a MIDI file from one score
----------------------------------------------------
    string source =
        "\\version \"2.24.0\"\n" +
        "\\score {\n" +
        "  \\relative { c'4 d e f | g1 }\n" +
        "  \\layout { }\n" +    // pages -> tune.svg
        "  \\midi { }\n" +      // performance -> tune.midi
        "}\n";

    BatchRunResult result = BatchRunner.RunText(source, "tune", null, outputDirectory);

    foreach (string midi in result.MidiPaths)
    {
        byte[] bytes = File.ReadAllBytes(midi);     // a Standard MIDI File
    }

EXAMPLE 3: paper size, margins and a multi-page book
----------------------------------------------------
    string source =
        "\\version \"2.24.0\"\n" +
        "#(set-default-paper-size \"a5\")\n" +
        "\\paper { top-margin = 15\\mm  ragged-bottom = ##t }\n" +
        "\\layout { indent = 0 }\n" +
        "\\book {\n" +
        "  \\header { title = \"Two pages\" }\n" +
        "  \\score { \\relative { c'1 \\pageBreak d1 } }\n" +
        "}\n";

    BatchRunResult result = BatchRunner.RunText(source, "pages", null, outputDirectory);
    // result.SvgPaths = [ .../pages-1.svg, .../pages-2.svg ]

    OutputDef paper = LilyPondInit.DefaultPaper();     // CodeBrix.LilyPort.Engine.Layout
    double outputScale = paper.GetDimension("output-scale");   // mm per staff space
    double paperWidth = paper.GetDimension("paper-width");     // the DEFAULT paper (a4)

Paper size, margins, staff size and every other \paper variable are set IN THE
DOCUMENT, exactly as in LilyPond; the runner restores the defaults after each
file so one document's \paper never reaches the next. A host that wants a house
style for every document passes it as a -d include:
BatchRunOptions.Options = { "include-settings=/abs/path/house-style.ily" }.

EXAMPLE 4: per-run options, a log panel, and cancellation
---------------------------------------------------------
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    using StringWriter log = new StringWriter();
    using CancellationTokenSource cancellation =
        new CancellationTokenSource(TimeSpan.FromMinutes(2));

    BatchRunOptions options = new BatchRunOptions
    {
        PointAndClick = false,                          // publish build: no anchors
        Options = new List<string> { "no-point-and-click" },   // any -d option
        MessageWriter = log,                            // this run's output
        CancellationToken = cancellation.Token,
    };

    try
    {
        BatchRunResult result = BatchRunner.RunText(
            source, "preview", includeDirectory, outputDirectory, options);
    }
    catch (OperationCanceledException)
    {
        // nothing was written after the cancellation point
    }

    string transcript = log.ToString();   // "Parsing...", "Drawing systems...", warnings

EXAMPLE 5: warm the engine at startup, then serialise every operation
---------------------------------------------------------------------
    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.LilyPort;
    using CodeBrix.LilyPort.Engine.Bootstrap;
    using CodeBrix.LilyScheme;

    public sealed class EngineHost
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private Task _load;

        public Task BeginLoadingAsync()
        {
            return _load ??= Task.Run(() => Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);   // the scm/ layer
                LilyPondInit.DefaultLayout();                 // the ly/ layer
            }));
        }

        public async Task<BatchRunResult> EngraveAsync(
            string source, string baseName, string includeDirectory,
            string outputDirectory, CancellationToken cancellationToken)
        {
            await BeginLoadingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                BatchRunOptions options = new BatchRunOptions
                {
                    CancellationToken = cancellationToken,
                };
                return await Task.Run(
                    () => BatchRunner.RunText(
                        source, baseName, includeDirectory, outputDirectory, options),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    // at application start:
    EngineHost host = new EngineHost();
    host.BeginLoadingAsync();          // the UI stays responsive meanwhile
    // later:
    BatchRunResult result = await host.EngraveAsync(text, "doc", dir, outDir, token);

The gate is needed for anything that bypasses BatchRunner (direct engraving,
parse-only checks); RunText/RunFile alone would serialise themselves. The
BeginLoadingAsync task is what "the engine is loading" means in the UI --
measured, the load takes about 35 s the first time on a machine and about 4 s
after that.

EXAMPLE 6: a music expression straight to SVG and MIDI, no files
----------------------------------------------------------------
    using System.IO;
    using CodeBrix.LilyPort;
    using CodeBrix.LilyPort.Backends;
    using CodeBrix.LilyPort.Engine.Layout;
    using CodeBrix.LilyPort.Engine.Music;
    using CodeBrix.LilyPort.Parsing.Session;
    using CodeBrix.LilyScheme;

    string directory = Path.Combine(Path.GetTempPath(), "lilyport-direct");
    Directory.CreateDirectory(directory);

    Interpreter.RunWithLargeStack(() =>
    {
        LilyParserSession session = LilyPondInit.Session();   // boots if needed

        MusicObject music = (MusicObject)session.ParseStringExpression(
            "\\relative { c'4 d e f }", "<host>", 1);

        // One call:
        string svg = LilyPortEngraver.EngraveToSvg(music);

        // Or the pieces, with the paper's scale so the mm size is right:
        EngraveResult engraved = LilyPortEngraver.Engrave(music);
        SvgBackend backend = new SvgBackend
        {
            UnitLength = LilyPondInit.DefaultPaper().GetDimension("output-scale"),
            Precision = 4,
        };
        string document = backend.RenderDocument(engraved.Stencil);
        int lines = engraved.LineCount;

        // The MIDI twin:
        OutputDef midi = session.LookupIdentifier("$defaultmidi") as OutputDef;
        Performance performance = LilyPortPerformer.Perform(music, midi);
        performance?.WriteOutput(Path.Combine(directory, "direct.midi"), "direct");
    });
    LilyPondInit.RestoreDefaults();

EXAMPLE 7: convert-ly on an old document
----------------------------------------
    using System;
    using CodeBrix.LilyPort.ConvertLy;

    string old = "\\version \"2.12.0\"\n\\relative c' { c4 d e f }\n";

    // Preview what would run (what --show-rules prints).
    if (DocumentConverter.TryReadDeclaredVersion(
            old, out ConversionVersion declared, out bool malformed))
    {
        foreach (ConversionRule rule in DocumentConverter.RulesBetween(
                     declared, DocumentConverter.LatestVersion))
        {
            Console.WriteLine(rule.Version + ": " + rule.Message.Trim());
        }
    }

    // Convert to the newest version the rules know.
    ConversionResult converted = DocumentConverter.Convert(old);
    string text = converted.Text;                        // rewritten \version line
    foreach (string message in converted.Messages)       // what still needs a human
    {
        Console.Error.WriteLine(message);
    }
    bool changed = converted.Changed;
    ConversionVersion? stamped = converted.StampedVersion;

    // Or between two explicit versions.
    ConversionVersion.TryParse("2.18.2", out ConversionVersion target);
    ConversionResult partial = DocumentConverter.Convert(
        old, new ConversionVersion(2, 12, 0), target);

    // A document with no \version at all:
    ConversionResult unknown = DocumentConverter.Convert("{ c'4 }");
    // unknown.VersionUnknown == true

An editor host pairs this with BatchRunResult.DeclaredVersion: when the engraved
document declares a version older than LilyPortInfo.CompatibleWithVersion, offer
the conversion.

EXAMPLE 8: import ABC, MIDI and MusicXML, then engrave
------------------------------------------------------
    using System;
    using System.IO;
    using CodeBrix.LilyPort;
    using CodeBrix.LilyPort.Importers;

    // ABC
    ImportResult fromAbc = AbcImporter.Import(abcText, new AbcImportOptions
    {
        Beams = true,
        SourceName = "tune.abc",
    });

    // MIDI
    MidiImportOptions midiOptions = new MidiImportOptions
    {
        Key = "-2:1",             // two flats, minor
        DurationQuant = 32,
        Skip = true,
        SourceName = "song.midi",
    };
    midiOptions.AllowTuplet.Add("4*2/3");
    ImportResult fromMidi = MidiImporter.Import(File.ReadAllBytes(midiPath), midiOptions);

    // MusicXML, plain or compressed
    MusicXmlImportOptions xmlOptions = new MusicXmlImportOptions
    {
        PitchMode = MusicXmlPitchMode.Absolute,
        Language = "deutsch",
        NoPageLayout = true,
        Midi = true,
        SourceName = Path.GetFileName(xmlPath),
    };
    ImportResult fromXml = xmlPath.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase)
        ? MusicXmlImporter.ImportCompressed(File.ReadAllBytes(xmlPath), xmlOptions)
        : MusicXmlImporter.Import(File.ReadAllText(xmlPath), xmlOptions);

    // Every result has the same shape.
    foreach (ImportResult result in new[] { fromAbc, fromMidi, fromXml })
    {
        foreach (string message in result.Messages) { Console.Error.WriteLine(message); }
        if (result.Succeeded)
        {
            BatchRunResult engraved = BatchRunner.RunText(
                result.Text, "imported", null, outputDirectory);
        }
    }

EXAMPLE 9: capturing warnings and handling the fatal cases
----------------------------------------------------------
    using CodeBrix.LilyPort;
    using CodeBrix.LilyPort.Flower;
    using CodeBrix.LilyPort.Parsing.Driver;

    Warn.Level = LogLevel.LevelWarn;                       // quiet: no progress lines
    Warn.ClearMessages();
    Warn.RecordMessages = true;
    try
    {
        BatchRunResult result = BatchRunner.RunText(text, "doc", null, outputDirectory);
        foreach (string line in result.Diagnostics) { /* parse-side */ }
        foreach (string line in Warn.Messages) { /* everything, every layer */ }
    }
    catch (ParseAbortedException aborted)
    {
        // "syntax error at end of input": the document is truncated
    }
    catch (LilyPondErrorException fatal)
    {
        // where lilypond would have exited: fatal.Message, fatal.Location
    }
    finally
    {
        Warn.RecordMessages = false;
    }

MINIMUM VIABLE PROJECT
======================
    <!-- Engrave.csproj -->
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.LilyPort.GplLicenseForever" Version="*" />
      </ItemGroup>
    </Project>

    // Program.cs
    using System;
    using System.IO;
    using CodeBrix.LilyPort;

    if (args.Length == 0)
    {
        Console.Error.WriteLine("usage: engrave <file.ly> [-o NAME]");
        return 2;
    }

    string outputOption = args.Length >= 3 && args[1] == "-o" ? args[2] : null;
    BatchRunner.SplitOutputName(
        outputOption, out string directory, out string baseName);

    BatchRunResult result = BatchRunner.RunFile(
        args[0],
        directory ?? Path.GetDirectoryName(Path.GetFullPath(args[0])),
        baseName);

    foreach (string page in result.SvgPaths) { Console.WriteLine(page); }
    foreach (string midi in result.MidiPaths) { Console.WriteLine(midi); }
    return result.ErrorCount == 0 ? 0 : 1;

    dotnet run -- score.ly -o out/score

The first run on a machine takes half a minute to boot; every later run of the
same binary boots from the cache in a few seconds. A one-shot command-line tool
like this is the WRONG shape for anything interactive -- keep the process alive
and reuse it (EXAMPLE 5).

PERFORMANCE TIPS
================
* Boot once per process, keep the process alive. The Scheme layer costs about
  35 s cold and about 4 s from the cache; each further small score is a fraction
  of a second. A process per file is the single most expensive mistake this
  package allows.
* Let the cache work: leave BootExpansionCache enabled, make sure its directory
  is writable (or point LILYPORT_EXPANSION_CACHE_DIR at one that is), and expect
  exactly one cold boot after every package upgrade.
* Warm up in the background at application start (EXAMPLE 5), so the first
  engrave the user asks for does not pay the boot.
* Cancellation is coarse. Keep long documents in their own runs and let the
  token stop the NEXT book rather than expecting a mid-score stop.
* The runner is serialised; parallel calls queue. Throughput is one engine's --
  for a batch, feed files in sequence and do not spawn threads to speed it up.
  (Two engines means two processes.)
* Set Warn.Level to LevelWarn when nobody reads the progress lines; they are
  written through a TextWriter and a busy console costs more than the check.
* convert-ly and the importers are pure text transformers: milliseconds, no boot,
  safe to run before the engine is ready (an editor can offer them at once).
* Memory: a booted engine holds the whole Scheme layer plus the fonts --
  under 200 MB peak measured for small scores, more for big books. Do not
  create a second interpreter in the same process.
* Direct engraving (LilyPortEngraver) skips book and page layout and is the
  fastest way to a small preview of one expression; it is not faster for a
  whole document, which needs BatchRunner anyway.

COMMON PITFALLS TO AVOID
========================
* STARTING A PROCESS PER FILE. See above; it is unusable at scale and it is why
  the port has a batch runner instead of a `lilypond' executable.
* CALLING THE ENGINE ON A DEFAULT THREAD STACK. LilyPortEngraver,
  LilyPortPerformer, LilyParserSession and any Scheme evaluation must run inside
  Interpreter.RunWithLargeStack; a stack overflow kills the process without an
  exception. BatchRunner wraps itself.
* OVERLAPPING ENGINE CALLS. Everything is process-global. BatchRunner takes a
  lock; the direct surface does not. Two direct calls at once corrupt each
  other's state silently.
* EXPECTING A STRING BACK. RunText/RunFile write files and return their PATHS.
  Read the SVG back if you need it; or use the direct surface for an in-memory
  fragment of one expression.
* RunText WITH includeDirectory NULL AND A DOCUMENT THAT \include's ITS
  NEIGHBOURS. Only the vendored ly/ files resolve then; the file's own
  directory must be passed (RunFile does it for you). A missing include is
  "cannot find file" in Diagnostics, ErrorCount stays 0, and a page is still
  produced -- without the included music.
* ParseAbortedException ON TRUNCATED INPUT. An unclosed brace at end of input
  throws out of RunText rather than being counted. Catch it in an editor host,
  where half-typed documents are the normal case.
* A PAGE WITH ERRORS. ErrorCount > 0 and SvgPath != null happen together:
  LilyPond recovers from syntax errors and engraves what it kept. Decide on
  ErrorCount.
* POINT-AND-CLICK ANCHORS BY DEFAULT. Every note becomes an
  <a xlink:href="textedit://..."> wrapper naming an ABSOLUTE path composed at
  draw time against the output directory. Pass PointAndClick = false for output
  you publish or compare; keep it on for an editor preview and read the anchors
  back for click-to-source.
* TEXT FONTS IN THE SVG ARE GENERIC NAMES. font-family="serif"/"sans"/
  "monospace" is what the file says; the engine measured with C059/Nimbus/
  TeX Gyre. A viewer with different serif fonts shows slightly different text
  widths; a converter that lets you map families should be pointed at those
  faces.
* NO SYSTEM FONT FALLBACK. Characters outside the 24 vendored faces (CJK,
  Hebrew, Arabic, many symbols) draw as tofu boxes with a warning, by design.
  The document must register its own font (#(ly:font-config-add-font ...)) --
  with an ABSOLUTE path, since a relative one resolves against the process's
  current directory, and a failure is a fatal error.
* THE `-o name.pdf' TRAP. A named output keeps its extension: `name.pdf'
  engraves to `name.pdf.svg', exactly as lilypond does. Give base names.
* EXPECTING A NAMED OUTPUT TO RENAME THE INPUT. RunFile with an outputBaseName
  renames only what is WRITTEN. Progress ("Processing `...'"), `input-file-name',
  every diagnostic's file:line:col and every music object's `origin' still name
  the file that was read -- which is what a reader needs, since that is the file
  they can open. A host that matches diagnostics against the output name finds
  nothing.
* PAIRING MIDI FILES WITH MOVEMENTS BY NAME. The performance counter restarts
  for every BOOK and the book's own suffix is already in the name, so a file
  with two books writes `<base>-1.midi' and `<base>-1-1.midi'. Read MidiPaths,
  in order, rather than composing names.
* MULTI-PAGE NAMING. Page files carry the PAGE NUMBER, starting at the book's
  first-page-number, not an index from 1: a book that begins on page 2 writes
  `-2', `-3' and no `-1'. Read SvgPaths rather than guessing names.
* SETTING OPTIONS DIRECTLY ON LilyPondScheme.Options. The next run's restore
  puts them back. Per-run settings go through BatchRunOptions; a house style
  goes through "include-settings=...".
* Warn IS PROCESS-WIDE AND PRINTS TO Console.Error BY DEFAULT. A GUI host that
  forgets this floods its stderr; a test host that redirects it and forgets to
  put it back changes every later test. Prefer BatchRunOptions.MessageWriter,
  which is scoped to the run and restored for you.
* Warn.Messages RECORDS EVERYTHING REGARDLESS OF Level, debug lines included.
  Filter by prefix, and ClearMessages between documents.
* MISTAKING LilyPortInfo.CompatibleWithVersion FOR THE PACKAGE VERSION. Showing
  "2.27.2" as the library's version, or the package's date-stamped version as a
  LilyPond version, is wrong both ways.
* \version IN IMPORTED DOCUMENTS IS "2.24.0". That is the upstream converters'
  frozen output syntax, not a defect and not the engine's version; do not
  "correct" it before engraving.
* USING THE SHARED PARSER SESSION AND NOT RESTORING. ParseText through
  LilyPondInit.Session() runs toplevel handlers and defines variables in the
  shared scope. Call LilyPondInit.RestoreDefaults() afterwards (BatchRunner does
  it before every run, so a following RunText is safe either way).
* SECOND INTERPRETERS. CreateInterpreter replaces the ambient interpreter; the
  cached init layer is keyed to it and reloads. One per process.
* THE GPL. Referencing this package makes your application a GPL-3.0 work when
  distributed. Decide that before writing code against it.

WHAT THIS PACKAGE DOES NOT DO
=============================
* NO PDF, PostScript, EPS or PNG output. The engine has exactly one backend, SVG
  (plus MIDI). The PostScript/Ghostscript pipeline is not ported; the Scheme
  bindings that drive it (ly:spawn, ly:gs-api, ly:shutdown-gs) exist and throw
  NotPortedException with their reason. To make a PDF, convert the SVG pages
  with a vector SVG-to-PDF tool; the CodeBrix family's route is
  CodeBrix.PdfDocCreate.Html2Pdf.MitLicenseForever, which places SVG as vector
  content into a PDF (the repository's own manual renderer uses it, outside the
  package). That dependency is deliberately NOT part of this package.
* NO `lilypond' COMMAND-LINE PROGRAM and no command-line parsing:
  ly:command-line-options answers the empty list; options are per-run
  BatchRunOptions. UsageText.Text is the help text, for a host that wants to
  print it.
* NO GUI, NO EDITOR, NO PREVIEW CONTROL. It writes SVG; displaying it is yours.
  (The repository contains an interactive shell built on this package; it is
  not in the package.)
* NO MIDI PLAYBACK, NO AUDIO. It writes Standard MIDI Files; playing them is
  another library's job.
* NO lilypond-book, NO musicxml OUTPUT, NO abc/midi EXPORT. The converters go
  one way: into LilyPond source.
* NO SYSTEM FONTS, NO fontconfig, NO Pango: ly:pango-font? is always #f, the
  text faces are the 24 vendored ones plus what a document registers, and
  CJK/Hebrew/Arabic coverage is not provided.
* NO SCHEME REPL OF ITS OWN. The interpreter is CodeBrix.LilyScheme; evaluate
  Scheme through its API (SchemeReader.ReadAll +
  interpreter.TreeIlEvaluator.ExpandAndEval), on a large stack, serialised with
  everything else. Its AGENT-README is the reference for that.
* NO PARALLEL ENGRAVING within a process, and no second engine per process.
* NO EDITING OF FILES IN PLACE. convert-ly answers text; writing it back (and
  the backup lilypond's script keeps) is the caller's decision.
* NO VERSIONS OF LilyPond OTHER THAN 2.27.2. Documents older than that are read
  through the same grammar lilypond 2.27.2 reads them with (run convert-ly
  first for very old ones); syntax newer than 2.27.2 is not understood.
* NO Documentation/ CONTENT: LilyPond's manuals are not in the package. Read
  them at lilypond.org for the input language; they describe this engine.

WORKING EXAMPLES ON GITHUB
==========================
The port's own test suite is the best source of working, asserted usage. Every
file below exists at https://github.com/ellisnet/CodeBrix.LilyPort/blob/main/<path>.

BatchRunner, end to end
    tests/CodeBrix.LilyPort.Tests/BatchRunnerTests.cs
        RunText with a \score and with bare toplevel music; named output
        (a_named_output_writes_under_the_given_base_name_not_the_input_s); a
        real file from disk; two files in sequence without state bleeding; the
        \language leak closed.
    tests/CodeBrix.LilyPort.Tests/OutputNameSplitTests.cs
        BatchRunner.SplitOutputName's rules for every shape of -o value.
    tests/CodeBrix.LilyPort.Tests/OutputFileNamingEndToEndTests.cs
        Page and book file naming: first-page-number, output-suffix, several
        books.
    tests/CodeBrix.LilyPort.Tests/PageBreakingEndToEndTests.cs
        \paper sizes, page breaking, toplevel \layout blocks, skipTypesetting.
    tests/CodeBrix.LilyPort.Tests/PointAndClickEndToEndTests.cs
        BatchRunOptions: PointAndClick true/false/symbol/list, the Options list,
        MessageWriter, CancellationToken, DeclaredVersion.
    tests/CodeBrix.LilyPort.Tests/SessionLeakEndToEndTests.cs
        What RestoreDefaults guarantees between runs (variables, options,
        session state).
    tests/CodeBrix.LilyPort.Tests/IncludeIdentifierEndToEndTests.cs
        \include resolving against the include directory.
    tests/CodeBrix.LilyPort.Tests/DiagnosticWordingEndToEndTests.cs
        Warn.RecordMessages / Warn.Messages around a run; the exact wording
        of common warnings.
    tests/CodeBrix.LilyPort.Tests/ToplevelLayoutEndToEndTests.cs
    tests/CodeBrix.LilyPort.Tests/BookPathEndToEndTests.cs
        \book, \bookpart, \markup scores, headers and MIDI titles.

MIDI
    tests/CodeBrix.LilyPort.Tests/MidiEndToEndTests.cs
        A \midi block to a Standard MIDI File, checked byte by byte; no \midi
        block means no file; lyrics, ties, tempo.

The direct surface
    tests/CodeBrix.LilyPort.Tests/FirstLightTests.cs
    tests/CodeBrix.LilyPort.Tests/StemFirstLightTests.cs
        Scheme-built music through LilyPortEngraver.Engrave/EngraveToSvg inside
        Interpreter.RunWithLargeStack; reading grobs off the EngraveResult.
    tests/CodeBrix.LilyPort.Backends.Tests/SvgBackendTests.cs
        SvgBackend command by command: RenderFragment/RenderDocument, UnitLength
        and the mm size, the xlink namespace, unknown commands.

Startup, options, diagnostics
    tests/CodeBrix.LilyPort.Engine.Tests/LilyPondSchemeLoadTests.cs
        LilyPondScheme.CreateInterpreter + LoadViaLilyScm and the LoadReport.
    tests/CodeBrix.LilyPort.Engine.Tests/BootExpansionCacheTests.cs
        The cache directory override and disable switch; replay vs live boot.
    tests/CodeBrix.LilyPort.Engine.Tests/CommandLineOptionsTests.cs
    tests/CodeBrix.LilyPort.Engine.Tests/ProgramOptionsTests.cs
        -d option texts through CommandLineOptions.Apply; the option store.
    tests/CodeBrix.LilyPort.Engine.Tests/WarningAsErrorTests.cs
    tests/CodeBrix.LilyPort.Flower.Tests/WarnTests.cs
        Warn.Level, Warn.Output, WarningAsError, expected warnings.

The parser
    tests/CodeBrix.LilyPort.Parsing.Tests/LilyParserSessionTests.cs
        ParseText outcomes, embedded Scheme, \include switching input, a
        missing include reported, the vendored init layer.

convert-ly and the importers
    tests/CodeBrix.LilyPort.Tests/ConvertLyParityTests.cs
        DocumentConverter.Convert against upstream's recorded output for 149
        real files, both from-first-rule and from the declared version.
    tests/CodeBrix.LilyPort.Tests/ImportersTests.cs
        AbcImporter/MidiImporter surface: options, strict mode, SourceName,
        failure shapes.
    tests/CodeBrix.LilyPort.Tests/AbcImporterParityTests.cs
    tests/CodeBrix.LilyPort.Tests/MidiImporterParityTests.cs
    tests/CodeBrix.LilyPort.Tests/MusicXmlImporterParityTests.cs
        Each importer against upstream's recorded output, including
        MusicXmlImporter.ImportCompressed on .mxl input.

Fonts
    tests/CodeBrix.LilyPort.Engine.Tests/DocumentFontTests.cs
        TextFace.Load, family names, document font registration and its
        per-file lifetime.
    tests/CodeBrix.LilyPort.Tests/PackagedFontTests.cs
        Which fonts the package ships.

Info
    tests/CodeBrix.LilyPort.Tests/LilyPortInfoTests.cs

QUICK REFERENCE CARD
====================
    PACKAGE   CodeBrix.LilyPort.GplLicenseForever   (GPL-3.0-only; .NET 10+)
              depends on CodeBrix.LilyScheme.LgplLicenseForever
              five assemblies in one package; namespaces CodeBrix.LilyPort.*

    ENGRAVE   BatchRunner.RunText(text, baseName, includeDir, outDir[, options])
              BatchRunner.RunFile(path, outDir[, baseName[, options]])
              BatchRunner.SplitOutputName(oValue, out dir, out baseName)
              -> BatchRunResult { SvgPath, SvgPaths, MidiPaths, ErrorCount,
                                  Diagnostics, BookCount, SystemCount,
                                  DeclaredVersion }
              files: <base>.svg | <base>-<page>.svg ; <base>.midi |
                     <base>-1.midi -- MIDI names are the BOOK's and the
                     counter restarts per book (a second book that performs
                     twice writes <base>-1.midi and <base>-1-1.midi)

    OPTIONS   new BatchRunOptions { PointAndClick = false,
                  Options = { "no-point-and-click", "include-settings=/p/x.ily" },
                  MessageWriter = writer, CancellationToken = token }
              one run's lifetime; the next run restores the defaults

    BOOT      first call boots (~35 s cold, ~4 s cached); keep the process alive
              warm up: Interpreter.RunWithLargeStack(() => {
                  var i = LilyPondScheme.CreateInterpreter();
                  LilyPondScheme.LoadViaLilyScm(i);
                  LilyPondInit.DefaultLayout(); });
              cache: BootExpansionCache.CacheDirectory /
                     LILYPORT_EXPANSION_CACHE=0 / LILYPORT_EXPANSION_CACHE_DIR

    THREADS   one engine per process; BatchRunner serialises itself; everything
              else: your own gate + Interpreter.RunWithLargeStack(...)

    DIRECT    session = LilyPondInit.Session()
              music = (MusicObject)session.ParseStringExpression(code, name, 1)
              LilyPortEngraver.EngraveToSvg(music) / .Engrave(music).Stencil
              new SvgBackend { UnitLength = paper.GetDimension("output-scale") }
                  .RenderDocument(stencil)
              LilyPortPerformer.Perform(music,
                  session.LookupIdentifier("$defaultmidi") as OutputDef)
                  ?.WriteOutput(path, name)
              then LilyPondInit.RestoreDefaults()

    PARSE     LilyPondInit.Session().ParseText(text, "name.ly") -> ParseOutcome
              { Success, ErrorCount, AllDiagnostics() }; session.IncludePath.Add
              catch ParseAbortedException (truncated input)

    LOG       Warn.Level = LogLevel.LevelWarn ; Warn.Output = writer
              Warn.RecordMessages = true ; Warn.Messages ; Warn.ClearMessages()
              catch LilyPondErrorException (fatal), OperationCanceledException,
                    ParseAbortedException, NotPortedException

    CONVERT   DocumentConverter.Convert(text[, from, to]) -> ConversionResult
              { Text, Changed, StampedVersion, Messages, Errors, VersionUnknown }
              DocumentConverter.RulesBetween(from, to) ; .LatestVersion
              DocumentConverter.TryReadDeclaredVersion(text, out v[, out bad])
              ConversionVersion.TryParse("2.18.2", out v)

    IMPORT    AbcImporter.Import(abcText, new AbcImportOptions { Beams, Strict })
              MidiImporter.Import(bytes, new MidiImportOptions { Key, Skip, ... })
              MusicXmlImporter.Import(xml, opts) / .ImportCompressed(mxlBytes, opts)
              -> ImportResult { Text (null on failure), Messages, Errors, Succeeded }

    FONTS     embedded; none on disk. FontAssets.SearchPaths.Add(dir) to override
              files by name; documents add their own with
              #(ly:font-config-add-font "/abs/path.otf"); no system fallback

    INFO      LilyPortInfo.Version (package) vs .CompatibleWithVersion ("2.27.2")

    NOT HERE  PDF/PS/PNG output, a lilypond CLI, GUI, MIDI playback, system
              fonts, parallel engraving
