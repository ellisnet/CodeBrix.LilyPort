================================================================================
CodeBrix.LilyPort -- tools/Lily.Shell/
================================================================================

Lily.Shell is the interactive shell for the port: a CodeBrix.Platform application
whose window is a terminal, hosting the LilyPond engine IN PROCESS. Parse a file,
engrave it, talk to the Scheme layer, render a manual -- without a twenty-second
engine start-up between each one.

It is a REPO TOOL. Nothing here ships: VERIFIED 2026-08-19, the one packable
project in this repository (src/CodeBrix.LilyPort) references nothing under tools/
at all, and no packaging step reaches this directory. Decision D14 settled what
this replaces -- there is no public `lilypond'-style console CLI; this is the
user-facing surface instead.

--------------------------------------------------------------------------------
RUNNING IT
--------------------------------------------------------------------------------

    cd tools/Lily.Shell
    dotnet run --project src/Lily.Shell.LinuxX11 -c Release

Six heads, all from one solution and one shared UI project:

    Lily.Shell.LinuxX11          net10.0            the one used day to day here
    Lily.Shell.LinuxWayland      net10.0
    Lily.Shell.LinuxFrameBuffer  net10.0            no desktop needed
    Lily.Shell.MacOS             net10.0
    Lily.Shell.Win32Skia         net10.0
    Lily.Shell.WinWpfSkia        net10.0-windows

⚠ THE ENGINE LOADS IN THE BACKGROUND AND THE FIRST LOAD IS THE SLOW ONE: about
20 s warm, and roughly 67 s the first time after a build, which is JIT rather than
work. The window title is the progress bar -- one more dot every five seconds --
and the banner says so. Commands that need the engine wait for it and say they are
waiting; commands that do not are usable immediately.

--------------------------------------------------------------------------------
COMMANDS
--------------------------------------------------------------------------------

    help       Lists commands, or shows usage for one command.
    clear      Clears the terminal screen.
    version    Shows the ported LilyPond version and engine state.
    usage      Prints the command-line usage message.
    parse      Parses a .ly file and shows diagnostics.
    engrave    Engraves a .ly file to SVG (and .midi when the score has \midi).
    demo       Engraves the first-light demo (quarter-note c'4) to SVG.
    include    Lists or adds parser include directories.
    set        Lists or sets the engine's program options (lilypond's -d).
    display-music
               Shows the internal representation of a music expression.
    scheme     Enters the LilyScheme REPL (the engine's Scheme sandbox).
    docs       Renders one of the port's nine manuals to HTML and PDF.
    convert-ly Converts an old .ly file to the current syntax.
    import     Converts ABC, MIDI or MusicXML to LilyPond source.
    exit       Closes Lily.Shell.

That is fifteen commands, and it is the WHOLE of the v1 sketch that is going to be
built -- see THE TWO COMMANDS THAT WERE DROPPED, below.

`usage' prints the engine's own UsageText.Text -- the SAME string the ly:usage
Scheme binding prints. One string, two callers, deliberately.

`engrave -o' means what lilypond's -o means: a NAME, not merely a directory
(BatchRunner.SplitOutputName, ported from main.cc:729-761). It lists EVERY page it
wrote plus any MIDI, because a multi-page book reported by its first page is a
book three quarters lost.

--------------------------------------------------------------------------------
`set' AND `display-music' -- THE SKETCH'S LAST TWO
--------------------------------------------------------------------------------

The v1 command sketch paired these with two commands that already existed:
`set' with `include', and `display-music' with `parse'. Both pairings are the
point of them.

`set' IS lilypond's -d, and `include' is lilypond's --include.

    lily> set                          every option and its value, then whatever
                                       this session has set
    lily> set --doc                    what every option is for -- upstream's -dhelp
    lily> set --doc point-and-click    what one option is for, and its value now
    lily> set debug-voices             -ddebug-voices        (set it to #t)
    lily> set no-point-and-click       -dno-point-and-click  (set it to #f)
    lily> set resolution=150           -dresolution=150
    lily> set resolution 150           the same, with the = spelled as a space
    lily> set --clear                  forget everything this session set

Every spelling that SETS something is the command line's own, and goes through the
command line's own code (Engine.Bootstrap.CommandLineOptions), so the value TEXT is
turned into a value by the option's declared type exactly as -d would turn it. What
`set' adds is the two things a command line does not need: it says what the value
WAS, and it prints the engine's warnings -- `no such program option: foo', which
upstream emits and then sets the option anyway -- which would otherwise go to a
console a windowed application does not have.

⚠ READING AN OPTION IS `--doc', NOT A BARE NAME. `-dfoo' already means "set foo to
#t", so `set foo' cannot also mean "show me foo" without one of the two being a
surprise. The bare name sets, like -d; `--doc' reads.

⚠ AND A SETTING IS REPLAYED INTO EVERY `engrave', because it would not survive one
otherwise. Every run opens with LilyPondInit.RestoreDefaults, which puts the whole
option table back to what the init layer left -- upstream engraves one file per
process and cannot leak an option between files, and the port keeps that promise by
restoring. So the settings this session made are carried into each run through
BatchRunOptions.Options, the same -d road, and put back on the live table
afterwards. `set' therefore means one thing before an engrave and the same thing
after it.

⚠ `docs' IS THE ONE COMMAND `set' DOES NOT REACH, and deliberately. Lily.Docs
renders the manuals against frozen expected-warnings and page-count baselines; a
session option injected into that render would move them, and a manual is not the
place to find out that -ddebug-skylines was still on from an hour ago.

`display-music' says what `parse' just read.

    lily> display-music { c'4 d'8 }           the internal representation
    lily> display-music --lily { c'4 d'8 }    back into LilyPond syntax
    lily> display-music --tree { c'4 d'8 }    the terse property dump

The three are upstream's own displayers, called through the engine: the default is
`display-scheme-music', which is what \displayMusic calls, `--lily' is
`display-lily-music' behind \displayLilyMusic, and `--tree' is the procedure
literally called `display-music'. ⚠ SO THE COMMAND AND ITS DEFAULT ARE NAMED AFTER
DIFFERENT THINGS: the NAME is the sketch's, the DEFAULT is upstream's user-visible
one. MEASURED against the pinned 2.27.2 in the session that built it, on
{ c'4 d'8 }: `--lily' prints `{ c'4 d'8 }', BYTE-IDENTICAL to \displayLilyMusic's;
`--tree' prints the same Prob dump as `(display-music ...)', same properties in the
same order; and the default prints the same S-expression as \displayMusic -- same
forms, same (ly:make-duration 2) and (ly:make-pitch 0 0) values. Origin locations
differ on all three, and should: they name the input.

⚠ THE DEFAULT IS NOT LINE-BROKEN THE WAY THE ORACLE'S IS, AND THAT IS NOT
LilyPort's. display-scheme-music is `(pretty-print (music->make-music obj) port)',
and LilyScheme's (ice-9 pretty-print) does not break lines -- worse, it emits
NOTHING where the newline-and-indent belongs, so the port prints
`(make-music'SequentialMusic'elements(list ...' on one line, which does not read
back. That copy is LilyScheme's, not vendored here; rule 7 makes it a pin-bump item
and it is on the board under 2e. Nothing else in the port reaches that procedure.

⚠ THE EXPRESSION IS READ FROM THE RAW LINE, NOT FROM THE TOKENS. A shell tokenizer
eats the characters LilyPond spells with: `c'4^"text"' comes back from the token
list as `c'4^text', which is not an error, it is DIFFERENT MUSIC. The kernel's
ShellCommandContext.RawArguments exists for exactly this, and this is the only
command that reads it.

⚠ AND IT ASSIGNS RATHER THAN EVALUATES. A music expression written at toplevel is
collected into a book and engraved, which is the opposite of what this command is
for, so the expression is assigned to an identifier nobody would type and the
displayer is handed the value. The assignment stays in the session's parser scope,
as a `parse'd file's definitions do.

--------------------------------------------------------------------------------
THE TEXT CONVERTERS -- `convert-ly' AND `import'
--------------------------------------------------------------------------------

The package has carried convert-ly since 2026-08-20 and the ABC, MIDI and
MusicXML importers since 2026-08-26; standing rule 14 says a user-visible
capability is reflected here the same session it lands, and these two are that
debt paid.
Neither touches the engine -- both are text in, LilyPond source out -- so both
answer before the engine has finished loading.

    lily> convert-ly old.ly                      print the converted document
    lily> convert-ly old.ly -o new.ly            write it somewhere
    lily> convert-ly old.ly --from 2.12.0        override the document's \version
    lily> convert-ly old.ly --to 2.18.2          stop at a version

    lily> import                                 list the formats and their options
    lily> import abc tune.abc                    print the LilyPond source
    lily> import abc tune.abc --beams -o t.ly    ABC's own notion of beams
    lily> import midi song.midi --skip           s rather than r for rests
    lily> import midi song.midi --key -2:1 --duration-quant 32
    lily> import musicxml score.xml              plain MusicXML
    lily> import musicxml score.mxl -o s.ly      a compressed container
    lily> import musicxml score.xml --language deutsch --no-page-layout

⚠ NEITHER REWRITES A FILE IN PLACE. Upstream's convert-ly edits the file it was
given and keeps a backup; a shell session is not the place to acquire that
habit, so the converted text is printed unless -o says where to put it.

⚠ THE WARNINGS ARE PRINTED FIRST, ALWAYS. What the rules and the converters had
to say is the part of a conversion that still needs a human: a transcription
with a hole in it looks finished until you read them.

The per-format switches are named after the long options of the script each
format comes from, so someone who knows abc2ly, midi2ly or musicxml2ly already
knows these. Only the LONG spelling is read: upstream's two-letter abbreviations
(`--npl', `--nsd' and the rest) belong to its command line rather than to this
one, where there is room to say what is meant.

⚠ `import musicxml' READS THE CONTAINER TOO. A file whose name ends in `.mxl' is
a zip container and is opened as one, following its own manifest to the score
inside; anything else is read as the XML itself. That is upstream's `-z' option,
which is a statement about the INPUT rather than about the conversion, so it is
not a switch here.

--------------------------------------------------------------------------------
THE TWO COMMANDS THAT WERE DROPPED, AND WHY
--------------------------------------------------------------------------------

The 2026-08-02 v1 command sketch named six commands beyond the eleven that
shipped. Four were built -- `convert-ly' and `import' (2026-08-27), `set' and
`display-music' (2026-08-30). TWO ARE DELIBERATELY NOT GOING TO BE, ruled by
Jeremy on 2026-08-27 as decision D65, and the reasons are written down HERE
rather than left to be inferred, so that a completeness audit reads the two
absences as decisions instead of as gaps.

  `render' -- inline rendered pages in the terminal.
      DROPPED because there is no backend to render THROUGH. It was gated on the
      master plan's Milestone 7, `output-skia', and decision D61 (2026-08-27)
      closed Milestone 7 as SUPERSEDED, never to be built: "LilyPond does not
      need Skia output capability. SVG output is good enough." What consumes the
      SVG is downstream -- screen rendering is Fresco.Brix's Music View, PDF is
      the Html2Pdf vector route -- so `render' has nothing left to do that
      `engrave' does not already do.

  `regression next|sweep|status' -- the Phase 4 cockpit over the internal runner.
      DROPPED as UNWANTED rather than as blocked; it could be built today. Phase
      4 ran twenty-six sessions without it, and what a session actually reaches
      for is the harness under tools/regression-harness/ -- BatchDriver,
      compare-output.py, ratchet.py -- which is scriptable, greppable and already
      the thing every gate is defined in terms of. A second way to start a sweep,
      inside a GUI, with its own idea of what a run is, would be a second thing to
      keep true.

With those two ruled out, `set' and `display-music' were the sketch's remainder,
and Lily.Shell v1 IS FINISHED as of 2026-08-30. What it acquires from here is
whatever standing rule 14 brings it -- see THE STANDING EXPECTATION below.

--------------------------------------------------------------------------------
THE `docs' COMMAND -- PHASE 5's CAPABILITY, IN THE SHELL
--------------------------------------------------------------------------------

    lily> docs                        list the nine manuals, and say whether the
                                      port's nineteen documentation files have
                                      been generated yet this session
    lily> docs contributor            both formats, into /tmp/lily-shell-docs/
    lily> docs notation --html        one format
    lily> docs learning -o ~/manuals  somewhere else
    lily> docs notation --no-snippets the control run: no engraver, seconds

Decision D52 ruled tools/Lily.Docs a repo tool that ships nothing AND a `docs'
command here, so the manuals are reachable without building a separate tool. This
command does not reimplement anything: DocsCommand and DocsRunner (in
Lily.Shell.Core) drive Lily.Docs in this process through
LilyPortHost.RunEngineWorkAsync. The full description of what is rendered, what
the baselines mean and where the manuals come from is in
tools/Lily.Docs/README.txt.

Four things about it are load-bearing rather than incidental:

  * GENERATION IS CACHED FOR THE SESSION, because it works once per PROCESS. The
    second run of ly/generate-documentation.ly in a process writes NOTHING,
    reports all nineteen files missing, and does not throw -- and a shell is
    exactly where two `docs' commands in one process is the normal case. An
    INCOMPLETE generation is deliberately not cached, and the retry it allows
    fails identically; restarting the shell is the fix. The alternative is a
    manual rendered out of a half-written directory, successfully, with its
    appendices simply absent.
  * ASKING FOR BOTH FORMATS IS ONE RENDER, not two. Rendering HTML and then PDF
    runs the Texinfo source twice and engraves the music once per format -- two
    and a half thousand engravings and five minutes, for the notation manual.
  * THE COUNTS REPORTED ARE ASKED AND FAILED, never "it finished". The Texinfo
    package CATCHES a snippet renderer that throws and prints the snippet's
    source instead, so a render that completed is entirely compatible with every
    engraving in it having failed.
  * THERE IS NO --baseline HERE, and there is a test asserting there is not.
    Lily.Docs can freeze a manual's expected-warnings baseline from a run; the
    shell only renders. A baseline is frozen from a run that was READ, in the
    repository, by the tool that owns the file.

⚠ THE EIGHT CORPUS MANUALS NEED THE REPOSITORY; `internals' does not. The corpus
mirror is found by walking up from the running assembly to CodeBrix.LilyPort.slnx,
while the vendored GFDL assets travel beside the assembly -- so a copy of this app
moved out of its build tree still renders `internals' and answers any other manual
with "could not find CodeBrix.LilyPort.slnx above ...".

⚠ AND THIS IS WHY Lily.Shell CARRIES THE Texinfo -> Html2Pdf CHAIN, which decision
D52 refuses for CodeBrix.LilyPort itself. Lily.Shell ships nothing, and since the
2026-08-26 package bump the chain is FULLY MANAGED -- Texinfo2Html/Texinfo2Pdf ->
CodeBrix.PdfDocCreate.Html2Pdf -> CodeBrix.Imaging.Drawing.NoSkia for the SVG ->
PDF vector content, no SkiaSharp and no native library at all -- so the reference
costs managed assemblies plus two font packages, with Roboto/Roboto Mono at the
versions pinned here. The SkiaSharp the desktop heads DO carry (MEASURED
2026-08-19: 561 MB of SkiaSharp 4.151.0 native assets) comes from
CodeBrix.Platform's runtime, as in every Platform application, and has nothing
to do with this chain.

--------------------------------------------------------------------------------
LAYOUT
--------------------------------------------------------------------------------

    src/libs/Lily.Shell.Kernel/        VT input tokenizer, line editor, command
                                       registry, sub-interpreter stack, the
                                       ShellSession itself. No UI, no engine.
    src/libs/Lily.Shell.TerminalView/  an EMPTY PASS-THROUGH project. The terminal
                                       control graduated into the platform family
                                       as CodeBrix.Platform.TerminalView; this
                                       project only flows that package to the app
                                       and the tests.
    src/Lily.Shell.UI/                 the shared XAML (a .shproj): App + MainPage.
    src/Lily.Shell.Core/               LilyPortHost, the commands, the view model,
                                       window chrome. Engine by ProjectReference
                                       to all four LilyPort projects (the facade
                                       packs them PrivateAssets="all", so an
                                       in-repo consumer must name each one).
    src/Lily.Shell.<head>/             one Program.cs per platform head.
    tests/Lily.Shell.Core.Tests/       the command SURFACES -- `docs' and the
                                       once-per-process generation contract, the two
                                       text converters, and `set' and `display-music'.
                                       Gated at the command line, because a path
                                       through any of these commands reaches the engine
                                       or the file system.
    tests/libs/*.Tests/                Kernel and TerminalView.

The Emmentaler faces are copied to <appdir>/fonts/otf from Core, because the
engine's font layer probes there.

--------------------------------------------------------------------------------
TESTS -- THE MTP DIALECT, AND WHY PLAIN `dotnet test' IS REFUSED
--------------------------------------------------------------------------------

    dotnet test --solution Lily.Shell.slnx -c Release      120 tests

    43  Lily.Shell.Kernel.Tests
    28  Lily.Shell.TerminalView.Tests
    49  Lily.Shell.Core.Tests

This solution is the Microsoft.Testing.Platform dialect. xunit.v3 4.0 brings MTP
2.3.3, which removed the legacy VSTest-mode bridge, so on the .NET 10 SDK a plain
`dotnet test' is refused outright. The fix is the global.json BESIDE THIS FILE:

    { "test": { "runner": "Microsoft.Testing.Platform" } }

and the new CLI syntax -- `--solution X.slnx' or `--project x.csproj'; positional
arguments no longer work, and `--logger trx' is ignored here (two MTP0001
warnings). ⚠ The main CodeBrix.LilyPort solution and tools/Lily.Docs are the OTHER
dialect, VSTest, where `dotnet test <solution>' works as written. Both dialects
live in this repository on purpose; do not "fix" one into the other.

--------------------------------------------------------------------------------
SHARP EDGES
--------------------------------------------------------------------------------

  * THE KERNEL EMITS EXPLICIT CRLF, so the terminal control must have
    ConvertEol = false or every line double-spaces.
  * CodeBrix.Terminal.Engine.Buffer COLLIDES WITH System.Buffer -- alias it.
  * SKIA HEADS DELIVER SHIFTED DIGIT-ROW SYMBOLS UNDER KEYSYMS THE VirtualKey
    PATH NEVER SEES (parentheses vanished until this was found). The control
    reads the internal KeyRoutedEventArgs.UnicodeKey by reflection, falls back to
    a US-QWERTY encoder, and tracks modifiers itself; there is no
    CharacterReceived and no IME on Skia heads.
  * ASYNC MESSAGES GO THROUGH ShellSession.WriteOutOfBand, never straight to the
    output: idle means message plus a prompt repaint, busy means message only.
    Writing directly is what produced the doubled prompt when the engine-ready
    announcement raced an awaiting command.
  * A RUNNING SCHEME EVALUATION CANNOT BE INTERRUPTED. Ctrl+C is honoured between
    engine operations, not inside one -- so `docs' says outright that it has
    stopped WAITING and the render is still running.

  ⚠ X11 AUTOMATION: FINDING THIS WINDOW IS HARDER THAN IT LOOKS, AND GETTING IT
    WRONG TYPES INTO SOMEBODY ELSE'S APPLICATION. Two ways it has actually gone
    wrong:
      (a) `xdotool search --name "Lily.Shell"' -- the dot is regex-any, and it
          matched "Lily Shell - CodeBrix Develop"; the keystrokes went to the IDE.
      (b) `xdotool search --pid P --name .' -- the criteria are ORed, not ANDed,
          so it matches every named window on the display; `tail -1' then returned
          an unrelated terminal.
    ⚠ And the obvious fix does not work either: MEASURED, this app's X11 window
    carries NO _NET_WM_PID, so `search --all --pid' finds nothing and
    getwindowpid refuses. What works is the TITLE, anchored and dot-escaped
    (^Lily\.Shell), PLUS UNIQUENESS -- no such window before launch, exactly one
    after -- with getwindowname re-checked before every keystroke and NOTHING
    typed if the check fails.

--------------------------------------------------------------------------------
THE STANDING EXPECTATION
--------------------------------------------------------------------------------

Lily.Shell IS KEPT CURRENT AS THE ENGINE GROWS (Jeremy, 2026-08-07; the boards
carry it as rule 14). A session that lands user-visible engine capability reflects
it here in the same session, even when the answer is a recorded "nothing owed" --
so this finishes as the full shell, sandbox and REPL for LilyPort with no catch-up
project at the end. `engrave' reaching the real batch pipeline and `docs' reaching
the manuals are both that rule being paid, as are `convert-ly', `import', `set'
and `display-music'.

MIDI PLAYBACK IS OUT OF SCOPE -- not just out of Lily.Shell's, but out of
LilyPort's entirely (decision D27). The port generates MIDI files and compares
them; playing them is a later project in the Fresco.Brix direction.

--------------------------------------------------------------------------------
LICENSING
--------------------------------------------------------------------------------

GPL-3, like the rest of this repository; every file carries the header. Lily.Shell
incorporates no third-party source of its own -- what it consumes arrives as
packages, and THIRD-PARTY-NOTICES.txt §12.1 records the documentation chain that
the `docs' command brought in.

================================================================================
