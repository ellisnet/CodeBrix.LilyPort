================================================================================
MAINTAINER-README: CodeBrix.LilyPort
Notes for people and agents MAINTAINING this repository — not for package consumers
================================================================================

If you are CONSUMING the NuGet package, read AGENT-README.txt instead. This file
is about changing this repository. Tools, samples and other non-package content
are catalogued in EXTRAS-README.txt; this file only says how they fit into
maintaining the port.

PURPOSE AND SCOPE
=================
The repository produces exactly one NuGet package:

    CodeBrix.LilyPort.GplLicenseForever
        from src/CodeBrix.LilyPort/CodeBrix.LilyPort.csproj
        License: GPL-3.0-only (see THE LICENCE POSITION below)
        Consumer documentation: AGENT-README.txt (repo root, packed into the
        .nupkg)

It is a managed port of GNU LilyPond 2.27.2, the music engraving program:
parser, Scheme layer, engravers, page layout, fonts, SVG and MIDI output, plus
the four text converters (convert-ly, abc2ly, midi2ly, musicxml2ly). The Scheme
interpreter underneath it is NOT in this repository: it is the sibling
CodeBrix.LilyScheme project, consumed as its published package
CodeBrix.LilyScheme.LgplLicenseForever.

The one package bundles FIVE assemblies, one per src project:

    CodeBrix.LilyPort            src/CodeBrix.LilyPort/
        the packable facade. Holds the public entry surface (BatchRunner,
        LilyPortEngraver, LilyPortPerformer, LilyPondInit, LilyPortInfo,
        BatchRunOptions) and the text converters under ConvertLy/ (the
        convert-ly port) and Importers/ (the abc2ly, midi2ly and musicxml2ly
        ports). The converters are the only PORTED source in this project.
    CodeBrix.LilyPort.Flower     src/CodeBrix.LilyPort.Flower/
        the utility layer ported from upstream flower/: Rational, Interval,
        Offset, Polynomial, Bezier, Direction, FileName, StringConvert, Warn.
        No dependency on anything else in the repo.
    CodeBrix.LilyPort.Engine     src/CodeBrix.LilyPort.Engine/
        the engraving engine ported from upstream lily/: Objects/ (grobs),
        Translation/ (engravers, performers, contexts), Layout/ (stencils,
        skylines, spacing, breaking), Music/, Audio/ (MIDI), Fonts/ (OpenType,
        CFF, the text-face chain), Origins/ (source locations), Bootstrap/
        (the entry points and callbacks registered into the Scheme layer), and
        Scheme/ (the vendored .scm and .ly layer, embedded). Also embeds the
        music and text fonts. References CodeBrix.LilyScheme as a package.
    CodeBrix.LilyPort.Backends   src/CodeBrix.LilyPort.Backends/
        the SVG output backend (SvgBackend.cs).
    CodeBrix.LilyPort.Parsing    src/CodeBrix.LilyPort.Parsing/
        the LilyPond-language parser: Grammar/ (reads the mirrored parser.yy),
        Lalr/ (in-repo LALR(1) table construction), Driver/ (a Bison-semantics
        runtime), Lexing/ (the hand-ported modal scanner), Actions/ (the
        hand-ported rule actions and their manifest), Session/.

Reference graph, bottom to top:

    Flower  <-  Engine (+ CodeBrix.LilyScheme package)  <-  Backends
                                                        <-  Parsing
    facade CodeBrix.LilyPort references all four with PrivateAssets="all"

THE LICENCE POSITION -- TWO TRUE STATEMENTS
-------------------------------------------
* The PACKAGE conveys as GPL-3.0-only. PackageLicenseExpression says so, the
  package Description says so, and THIRD-PARTY-NOTICES.txt section 2 records
  the ruling.
* OUR OWN source files are GPL-3.0-or-later, which is what every header says.

Both are true at once, and the facade csproj carries a comment saying not to
"correct" either to match the other. The reason is ly/articulate.ly, vendored
verbatim at src/CodeBrix.LilyPort.Engine/Scheme/ly/articulate.ly, whose header
reads "WARNING: this file under GPLv3 only, not GPLv3+". GPL-3-only and
GPL-3-or-later material combine legally, but the COMBINED work can then only be
conveyed under GPL version 3 exactly, because the "or later" option cannot be
exercised over a part that never granted it. Anything depending on this package
inherits the same constraint.

The fonts are a third regime: the Emmentaler sources under mf/ are dual-licensed
GPL-3.0-or-later-with-Font-Exception OR SIL OFL 1.1, and the 24 text faces are
AGPL-3.0-with-embedding-exception (URW) and GUST Font License (TeX Gyre). That
is why LICENSE.OFL travels in the package beside LICENSE. Documentation/ is GNU
FDL 1.3 (COPYING.FDL), and is NOT packed.

REPOSITORY LAYOUT
=================
    CodeBrix.LilyPort.slnx        the solution: the five src projects, the five
                                  test projects, and the two harness drivers
                                  (BatchDriver, DocsDriver) under /Tools/
    src/                          the five assemblies listed above; each ported
                                  project carries a PORT-COVERAGE.txt (see
                                  PROVENANCE) and an InternalsVisibleTo.cs
    tests/                        five xUnit v3 projects (one per src project),
                                  tests/regression/ (the vendored upstream
                                  regression corpus) and tests/fixtures/
                                  (LilyPond's own Emmentaler binaries, test
                                  fixtures only, never shipped)
    tools/                        thirteen repo tools; none ships anything
    assets/fonts/                 the fonts the Engine embeds: otf/ and svg/
                                  (the port's OWN Emmentaler build, nine files
                                  each) and text/ (24 prebuilt text faces plus
                                  their licence texts)
    mf/                           byte-identical mirror of upstream mf/ (115
                                  files): the Metafont sources the music fonts
                                  are built from, plus upstream's generator
                                  scripts. Never edited.
    parser-mirror/                byte-identical mirror of upstream
                                  lily/parser.yy and lily/lexer.ll, EMBEDDED
                                  into the Parsing assembly. Never edited.
    book-mirror/                  byte-identical mirror of four lilypond-book
                                  Python sources: reference text for the
                                  snippet-composition seam in tools/Lily.Docs.
                                  Never executed, never edited.
    Documentation/                byte-identical PARTIAL mirror of upstream
                                  Documentation/ (690 files, every sha256 in
                                  MANIFEST.sha256): the FDL manual sources
                                  tools/Lily.Docs renders. Never edited.
    box.eps                       a 205-byte side file. tests/regression/
                                  markup-eps.ly writes "box.eps" with
                                  open-output-file and reads it back through
                                  \epsfile; before BatchDriver ran each input
                                  from its own scratch directory, sweeps
                                  launched from the repo root left it here and
                                  it was committed. Nothing at the root refers
                                  to it.
    README.md                     human-facing overview, packed
    AGENT-README.txt              consumer documentation, packed
    MAINTAINER-README.txt         this file
    EXTRAS-README.txt             tools and non-package content
    README-INDEX.txt              map of the README files
    THIRD-PARTY-NOTICES.txt       the attribution and compliance ledger, packed
    LICENSE                       the GPL-3 text, packed
    LICENSE.OFL                   the SIL OFL 1.1 text, packed
    COPYING.FDL                   the GNU FDL 1.3 text (covers Documentation/),
                                  not packed
    icon-codebrix-128.png         the package icon, packed
    .gitignore                    the standard Visual Studio ignore set
                                  (bin/, obj/, TestResults/). Two nested
                                  .gitignore files matter: tools/regression-
                                  harness/.gitignore ignores reference/,
                                  candidate/ and out/; tools/font-build/
                                  .gitignore ignores out/ and the build logs.
    AGENTS.md, CLAUDE.md, .clinerules, .cursorrules,
    .cursor/rules/agent-readme.mdc, .windsurfrules,
    .github/copilot-instructions.md, .junie/guidelines.md
                                  the eight AI-agent pointer stubs; they all
                                  point at README-INDEX.txt and are maintained
                                  centrally. Do not edit them here.

THE THIRTEEN TOOLS (tools/)
---------------------------
Each is described for running in EXTRAS-README.txt; here is only what each is
FOR when maintaining the port.

    regression-harness/    the correctness oracle: reference generation from
                           the pinned LilyPond binary, the graded SVG
                           comparator, the ratchet, the diagnostics gate, the
                           MIDI scoreboard, the font-delta ledger, the
                           glyph-identity index, and the BatchDriver /
                           DocsDriver console projects. README.txt there is the
                           authority; TESTING below summarises it.
    parity-probes/         the measurement probes the port's recorded rulings
                           rest on (18 lilyport-probe-* directories plus
                           analysis/); historical instruments, not a suite.
    font-build/            build-fonts.sh: rebuilds the Emmentaler fonts from
                           mf/ (one-time; outputs are committed to
                           assets/fonts); compare-fonts.sh verifies a build
                           against an official release.
    parser-baseline/       what real GNU Bison made of parser.yy at the pin:
                           committed data the in-repo LALR generator is diffed
                           against (AutomatonAgreementTests,
                           BaselineAgreementTests).
    convertrules-port/     gen-convert-rules.py: generates
                           src/CodeBrix.LilyPort/ConvertLy/ConvertRules.g.cs
                           from upstream python/convertrules.py.
    convertlyprobe/        gen-convertly-fixtures.py: records what upstream's
                           convert-ly produces, as the JSON fixtures
                           ConvertLyParityTests replays.
    abcprobe/              the abc2ly oracle harness: fixture generator, the
                           AbcProbe replayer, ten probe inputs, DIVERGENCES.txt.
    midi2lyprobe/          the same shape for midi2ly (MidiProbe).
    musicxml2lyprobe/      the same shape for musicxml2ly (MusicXmlProbe).
    table-extract/         cxxscan.py plus the two scripts that extract the
                           entry-point docstrings and translator descriptions
                           out of upstream lily/*.cc into the committed tables
                           under src/CodeBrix.LilyPort.Engine/Scheme/.
    mutopia-probe/         opens a locally downloaded Mutopia corpus with the
                           port and grades the result; explicitly NOT a
                           fidelity oracle (version drift), and the corpus is
                           never copied into the repository.
    Lily.Docs/             its own solution (Lily.Docs.slnx): generates the
                           port's nineteen documentation files and renders the
                           nine manuals from Documentation/ through the
                           published CodeBrix.Texinfo packages. Ships nothing.
    Lily.Shell/            its own solution (Lily.Shell.slnx): a
                           CodeBrix.Platform terminal application hosting the
                           engine in process, six desktop heads. Ships nothing.

BUILDING
========
    dotnet build CodeBrix.LilyPort.slnx

There is no global.json at the repository root; the build uses whatever .NET
10 SDK is installed. The one global.json in the repo is
tools/Lily.Shell/global.json, which selects the Microsoft.Testing.Platform test
runner for THAT solution only -- see TESTING.

Every project targets net10.0 only -- no multi-targeting, ever, anywhere in the
CodeBrix family. GenerateDocumentationFile is on in all five src projects, so
every public type and public/protected member needs an XML doc comment; CS1591
is fixed at the source, never suppressed. There is no <NoWarn> or warning
demotion in any src/ or tests/ csproj and none may be added. A clean build is
0 warnings, 0 errors (measured 2026-08-27: 0 W / 0 E).

GeneratePackageOnBuild is FALSE on the facade, so a build does not produce a
.nupkg; packing is a separate, deliberate step (PACKAGING AND PUBLISHING).

WHAT THE BUILD EMBEDS
---------------------
The Engine csproj embeds three things, and all three are load-bearing:

  * Scheme\**\* -- the whole vendored Scheme layer: Scheme/lily/*.scm (91
    files from upstream scm/), Scheme/ly/*.ly (62 files from upstream ly/,
    including articulate.ly), Scheme/lilyport/support.scm (the port's own),
    load-order.txt, and the six committed data tables (entry-points.tsv,
    entry-point-docs.tsv, grob-interfaces.tsv, translators.tsv,
    translator-descriptions.tsv). The bytes on disk at BUILD time are what
    every consumer runs.
  * lily-cc-ledger.tsv -- the upstream lily/*.cc ledger, embedded so that
    LedgerTests reads the same bytes the library ships and no test step needs
    an upstream checkout.
  * the fonts: ..\..\assets\fonts\otf\*.otf, svg\*.svg and text\*.otf, each
    with LogicalName CodeBrix.LilyPort.Engine.Fonts.<otf|svg|text>.<file>. They
    ship INSIDE the assembly exactly as the Scheme layer does. They used to be
    found by probing directories beside the running assembly, which silently
    failed for any consumer whose output directory was not the repository (the
    BatchDriver, six levels down, engraved the whole suite with NO music
    glyphs and only a warning said so). The LogicalName keeps the on-disk file
    name intact because the files are byte-for-byte assets that are never
    subset, converted or edited.

The Parsing csproj embeds ..\..\parser-mirror\parser.yy and lexer.ll (linked as
Mirror\) and Actions\rule-manifest.tsv. The grammar reader reads parser.yy out
of the assembly at run time and builds the LALR(1) tables in-process, so the
generator always reads exactly the mirror the assembly was built from and a
CodeBrix build never depends on a path outside the assembly.

GENERATED FILES, AND WHAT REGENERATES THEM
------------------------------------------
None of these is regenerated by the build. Each is committed and regenerated
only on a deliberate re-sync to a newer LilyPond (or a deliberate fix to its
generator):

    src/CodeBrix.LilyPort/ConvertLy/ConvertRules.g.cs
        tools/convertrules-port/gen-convert-rules.py, from upstream
        python/convertrules.py. It carries every pattern and replacement
        VERBATIM as C# literals (read with ast.literal_eval); PythonRegex
        translates the python spelling to .NET at run time. Rules the
        generator cannot translate with certainty are listed as HAND and are
        expected in ConvertRules.Manual.cs -- the generated table names every
        rule, so a missing hand port is a compile error, never a silently
        absent rule. Re-run after a version bump; new rules appear at the end
        of the table. The script takes the path of a LilyPond checkout as its
        one argument.
    src/CodeBrix.LilyPort.Parsing/Actions/rule-manifest.tsv
        RuleManifest.Render (Actions/RuleManifest.cs); one row per production
        in the mirrored parser.yy. RuleActionFenceTests asserts every rule is
        either implemented or on the recorded not-yet list.
    src/CodeBrix.LilyPort.Engine/Scheme/entry-point-docs.tsv
    src/CodeBrix.LilyPort.Engine/Scheme/translator-descriptions.tsv
        tools/table-extract/extract-entry-point-docs.py and
        extract-translator-descriptions.py, over upstream lily/*.cc via
        cxxscan.py. These carry the docstrings and ADD_TRANSLATOR text that
        exist only at C++ compile time upstream; the port registers them from
        the tables at interpreter creation.
    src/CodeBrix.LilyPort.Engine/Scheme/entry-points.tsv, grob-interfaces.tsv,
    translators.tsv, load-order.txt
        extracted from upstream lily/*.cc and scm/lily.scm at the pin; each
        file's header says what it was read from. Fenced by
        EntryPointClosureTests, GrobInterfaceTableTests and the G4 translator
        fence (LilyPortEngraver.MissingTranslators must be empty).
    tests/CodeBrix.LilyPort.Tests/fixtures/{convertly,abc,midi2ly,musicxml}/
        the converter parity corpora, recorded from the ORACLE's own
        convert-ly, abc2ly, midi2ly and musicxml2ly by the four gen-*-fixtures
        scripts under tools/. Nothing is ever recorded from the port's own
        output. The musicxml inputs are Reinhold Kainhofer's MIT-licensed
        MusicXML test suite, vendored beside the cases with its LICENSE.
    assets/fonts/otf, assets/fonts/svg
        tools/font-build/build-fonts.sh over mf/. See PROVENANCE.

The generator scripts under tools/ read an upstream LilyPond checkout; that is a
manual tool run, not a build or test step. The standing rule (rule 7 of the
LilyPort plan, restated in Documentation/README.txt and the Engine csproj) is
that NO BUILD OR TEST STEP touches an upstream checkout: the only permitted edge
out of the repository is a package reference.

TESTING
=======
    dotnet test CodeBrix.LilyPort.slnx

The main solution is the VSTest dialect (Microsoft.NET.Test.Sdk +
xunit.runner.visualstudio + xunit.v3), so plain `dotnet test <solution>' works
as written and `--logger trx' is honoured. tools/Lily.Shell is the OTHER
dialect (Microsoft.Testing.Platform, selected by its own global.json), where
the command is `dotnet test --solution Lily.Shell.slnx'; tools/Lily.Docs is
VSTest again. Both dialects live in this repository on purpose -- do not "fix"
one into the other.

THE FIVE SUITES
---------------
    dotnet test CodeBrix.LilyPort.slnx -c Release

RUN THE BATTERY IN RELEASE. Every harness command is `-c Release' and the
recorded green runs are Release. The suites are green in Debug too, but know
this about them: each suite runs in ONE process against ONE engine, and
xunit's default test order is a hash that differs between the Debug and
Release builds, so a test that leaves shared engine state behind shows up as
a failure in one configuration only. That happened once (2026-08-27): a test
that `#(set! recording-group-emulate ...)'-wrapped a (lily) procedure in its
input never put it back -- `set!' on an imported binding mutates the shared
module variable, which outlives the run exactly as it does in upstream
LilyPond, while the `orig' the wrapper referred to lived in that file's own
parser module and died with it -- and the next `\autoChange' in the process
failed with "Unbound variable: orig". The test now restores the binding. The
rule: a test that mutates a (lily) binding from its .ly input MUST restore it
before the file ends; a configuration-only failure means test order, look
for the leak before suspecting the engine.

Counts, from the Release run recorded in each project's TestResults/*.trx on
2026-08-27 09:13 (all five green), with the wall times of a Debug
`dotnet test --no-build' the same afternoon (five projects concurrent, 2 m 20 s
in all; every count reproduced, 952/952 in the facade suite after the fix above):

    tests/CodeBrix.LilyPort.Flower.Tests      169 passed   under 1 s
    tests/CodeBrix.LilyPort.Backends.Tests     37 passed   ~7 s
    tests/CodeBrix.LilyPort.Parsing.Tests     557 passed   ~1 m 19 s
    tests/CodeBrix.LilyPort.Engine.Tests      855 passed   ~2 m 18 s
    tests/CodeBrix.LilyPort.Tests             952 passed   ~1 m 19 s
    ----------------------------------------------------------------
                                            2,570 passed, 0 failed

Time is dominated by engine boots (see THE TWENTY-SECOND BOOT under NOTES), not
by assertions.

    Flower.Tests     the utility layer. Where upstream ships tests
                     (test-rational.cc, test-interval.cc, test-direction.cc,
                     test-drul-array.cc) the cases are TRANSLATED from
                     upstream, so the port is checked against LilyPond's own
                     expectations rather than the porter's.
    Backends.Tests   the SVG backend: element shapes, glyph emission, the
                     music-string fallback, partial ellipses. Copies
                     emmentaler-20.otf beside the assembly as TestFonts/.
    Parsing.Tests    the grammar reader and the LALR generator against the
                     committed Bison baseline (AutomatonAgreementTests,
                     BaselineAgreementTests, GrammarShapeTests), the lexer,
                     the driver, the rule-action fence and the nineteen
                     rule-action group classes (RuleActionRag1..19Tests), the
                     init layer (ly/*.ly loading through the real parser),
                     embedded Scheme, markup reachability and the Scheme-layer
                     closure.
    Engine.Tests     102 classes over the engine proper: grobs, engravers,
                     iterators, spacing, skylines, fonts (OpenType, CFF,
                     kerning, the text-face chain, missing-glyph warnings),
                     program options, the Scheme load (LilyPondSchemeLoadTests,
                     LilyPondOnDemandLoadTests), the boot expansion cache, and
                     three FENCES worth knowing by name:
                       LedgerTests          every upstream lily/*.cc file has
                                            exactly one disposition in
                                            lily-cc-ledger.tsv and every
                                            claimed C# file exists
                       ThrowOnUnportedTests suite mode: an unported primitive
                                            normally answers a placeholder so
                                            loading continues; under suite
                                            mode it THROWS, so nothing can
                                            depend on a placeholder silently
                       EntryPointClosureTests / GrobInterfaceTableTests
                                            the committed tables agree with
                                            what the engine registers
                     Copies emmentaler-20.otf and emmentaler-brace.otf as
                     TestFonts/.
    Tests            the facade: FirstLightTests and ~40 *EndToEndTests
                     classes that engrave real music through BatchRunner and
                     inspect the grobs or the SVG (beams, ties, lyrics,
                     page breaking, point-and-click, session leaks, ...),
                     BatchRunnerTests, LilyPortInfoTests, the four converter
                     parity suites (ConvertLyParityTests,
                     AbcImporterParityTests, MidiImporterParityTests,
                     MusicXmlImporterParityTests -- replaying the JSON
                     fixtures byte for byte), and PackagedFontTests.
                     This project references the facade AND Engine AND
                     Backends explicitly, because the facade's PrivateAssets
                     references are deliberately not transitive.

/!\ PackagedFontTests READS A BUILT .nupkg. It looks under
src/CodeBrix.LilyPort/bin (any configuration, newest first) for a
CodeBrix.LilyPort.GplLicenseForever.*.nupkg, opens it, and asserts that no .otf
of any kind rides inside it and that the nine LilyPond-built fixtures under
tests/fixtures/lilypond-fonts would have been caught. If no package exists it
FAILS with a message telling you to run `dotnet pack -c Release' first -- a
silent pass would be the exact failure mode it exists to prevent. On a fresh
clone, pack once before expecting a green facade suite.

Test conventions: xUnit v3 plus SilverAssertions fluent assertions
(x.Should().Be(y)), coverlet.collector, one <Class>Tests.cs per fenced area,
snake_case test method names, //Arrange //Act //Assert comments, and
TestContext.Current.CancellationToken threaded through any call that accepts a
CancellationToken.

THE REGRESSION HARNESS -- THE ONLY CORRECTNESS ORACLE
-----------------------------------------------------
Upstream lily/ has ZERO unit tests. LilyPond establishes correctness by
rendering input/regression/*.ly and comparing against a previous run of itself,
so a reimplementation has nothing to test against until it has produced a
reference from the real LilyPond. tools/regression-harness/ is that machinery;
its README.txt (about a thousand lines) is the authority and records every
correction the harness has needed. What follows is the map.

THE ORACLE. GNU LilyPond 2.27.2, the official self-contained Linux tarball,
extracted anywhere -- nothing is installed. The scripts find it through
LILYPOND_BIN (or lilypond on PATH); the docs comparison and the converter probes
name it at ~/ClaudeHome/oracle/lilypond-2.27.2/, which is where it lives on the
development machine. MATCH THE VERSION TO THE PORT: comparing against another
LilyPond measures version drift, not port fidelity (generate-reference.sh warns
but does not stop).

THE CORPUS. tests/regression/ holds 2,146 .ly inputs plus 29 .ily includes,
vendored verbatim from upstream input/regression/ (without the .ily files, 113
inputs fail on "cannot find file"), and tests/regression/midi/ holds the
73-file MIDI subsuite.

WHAT IS COMMITTED AND WHAT IS NOT. The reference SVGs (about 2,300 pages,
62 MB) are NOT committed: reference/ and candidate/ are gitignored, regenerable
in about four minutes, and a committed copy would go silently stale when the
oracle moves. The durable record is reference-manifest.tsv (sha256 + size per
page) and reference-status.txt (per-input outcome). The MIDI reference IS
committed (reference-midi/midi, 90 files, 364 KB) because it is cheaper to
commit than to avoid.

GENERATING THE REFERENCE (from tools/regression-harness/):

    export LILYPOND_BIN=/path/to/lilypond-2.27.2/bin/lilypond
    ./generate-reference.sh                 # ~4 minutes at JOBS=10
    LILYPOND_BIN=... MODE=diagnostics ./generate-reference.sh
                                            # ~97 s; writes
                                            # reference/diagnostics/ only
    ./generate-midi-reference.sh            # the 90 .midi files

Variables: LILYPOND_BIN, JOBS (default nproc), LIMIT (first N inputs),
PER_FILE_TIMEOUT (default 60 s -- a few inputs are deliberately pathological),
MODE (svg, the default, or diagnostics). The script renders with --formats=svg
-dbackend=svg -dno-point-and-click --silent, and the README explains each flag;
the one to know is that WITHOUT --formats=svg the oracle attempts PDF, warns,
and --silent promotes the warning to a fatal error.

THE FONTS ARE PINNED. generate-reference.sh builds reference-fonts.conf from
reference-fonts.conf.in and points FONTCONFIG_FILE at it, so the oracle's
generic "serif"/"sans"/"monospace" resolve to the same 24 faces the port
embeds, out of the oracle's own bundled font directory (byte-identical to
assets/fonts/text, 24 of 24). Regenerate WITHOUT it and every text run in the
corpus moves and the ratchet floor becomes meaningless. The face lists mirror
TextFontChain.Families in src/CodeBrix.LilyPort.Engine/Fonts/TextFace.cs one for
one; change one, change the other, regenerate.

RUNNING THE PORT OVER THE CORPUS (from the repo root):

    dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
        tests/regression tools/regression-harness/candidate/svg \
        > /tmp/sweep.log 2>&1

BatchDriver SUITE_DIR OUT_DIR [--limit N] [--files a.ly,b.ly] [--keep-existing]
[--diagnostics] [--fonts DIR] [--point-and-click]. A full sweep is about ten
minutes. Two of its behaviours are load-bearing rather than tidiness: the output
directory is EMPTIED of .svg files at startup and self-checked at the end (exit
3 if the directory does not hold exactly what the sweep wrote), because stale
pages from earlier runs once hid a real regression from the ratchet and
overstated the committed floor by 97 rows; and every input runs from its own
scratch directory under the temp folder, because a .ly file may write files
named relative to the working directory (that is how box.eps and
-violin-1.notes once landed in the repo root, and how one file's writes became
readable by the next). --keep-existing opts out of both for a deliberate
partial run, and a run made that way is not evidence. --fonts substitutes a
font directory without a rebuild (FontAssets consults its SearchPaths before
the embedded copies). Run the sweep as `> log 2>&1', merged: the diagnostics
gate attributes stderr lines to files by their position in the merged stream.

GRADING. compare-output.py grades each page on a LADDER, coarse to fine
(ratchet.py's list, worst first):

    MISSING             no output produced at all
    REFERENCE-BAD       the reference side is unusable
    UNPARSEABLE         output is not well-formed SVG
    GLYPHS-DIFFER       wrong musical content (reports which glyphs, how many)
    PLACEMENT-COUNT     right glyph inventory, wrong number of placements
    PLACEMENT-ORDER     right glyphs, emitted in a different order
    PLACEMENT-DIFFERS   right glyphs, wrong positions
    MATCH               same glyphs, same places

A byte diff yields one bit; the ladder says WHERE the port diverged, so
progress is measurable before the first MATCH. Glyph identity is NAMED-GLYPH
identity (decision D29): a music glyph's path bytes are resolved to the glyph
NAME they are a verbatim copy of, against THAT SIDE's own fonts, through the
committed glyph-identity.tsv index -- because the port's Emmentaler build and
the oracle's serialize identically-shaped outlines differently. Everything
else is byte-exact; visual and tolerance comparison are forbidden. The
resolution is fail-strict (an unresolvable path keeps raw-byte identity), and
the whitespace normalisation the index applies is load-bearing: get it wrong
and every lookup misses while the reference-against-reference self-check still
passes, which is what the --selftest canary is for.

    python3 compare-output.py reference/svg candidate/svg --tsv /tmp/run.tsv

THE RATCHET. Progress may only go forward, per file:

    ./ratchet.py check /tmp/run.tsv        # gate: exit 1 on ANY backslide
    ./ratchet.py update /tmp/run.tsv       # ratchet forward, then commit
    ./ratchet.py self-test                 # prove the gate logic itself
    ./ratchet.py rebaseline /tmp/run.tsv --reason "why" [--only a.svg,b.svg]

pass-manifest.tsv records, per reference page, the BEST VERDICT EVER ACHIEVED.
A run must meet or beat every row; sliding backwards on one file fails the gate
even while the total MATCH count rises, because totals can hide a swap and
per-file floors cannot. `update' cannot lower a row -- it takes the better of
manifest and run. Lowering is `rebaseline' only: it demands a written reason
and appends every change to pass-manifest-decisions.tsv (committed,
append-only), so an earned floor can always be told from an unearned one.
Advance the manifest only through ratchet.py, never by hand.

THREE FILES THAT MUST NOT BE MERGED (the third is RETIRED). g1-skip-list.tsv
holds the rows RULED OUT of the "everything matches" gate (G1), each with its
date and reason; it is read by nothing automatically and exists so the
exception list is answerable from the repository. font-delta-ledger.tsv PRICES,
per page in millimetres against a 0.05 mm ceiling, what the port's own font
build costs relative to LilyPond's build (see THE FONT-PARITY PAIR).
compare-output.py's R10 post-pass re-graded four named files whose only
difference was a bounded text font-size skew, and reported the upgrade --
RETIRED 2026-09-01 (L14): the skew was fixed by measurement on 2026-08-27, the
post-pass read "0 of 4" on every run from then on, and its code is now DELETED
from compare-output.py, with all 2,316 verdicts proven byte-for-byte identical
across the removal. They make different claims; a row that stops matching must
stay distinguishable from a row that was never required to.

THE FONT-PARITY PAIR. Every graded run goes both ways:

    BatchDriver ... /tmp/gate --fonts tests/fixtures/lilypond-fonts
    python3 compare-output.py reference/svg /tmp/gate --tsv /tmp/gate.tsv
    python3 font-delta.py candidate/svg /tmp/gate --gate /tmp/gate.tsv \
        --check font-delta-ledger.tsv

The GATE run (LilyPond's own release binaries, never shipped) must match the
oracle exactly: any divergence there is the ENGINE. The ledger then records
what the port's OWN Emmentaler build costs, and --check fails on ANY change to
ANY recorded number, in either direction. A row crossing the ceiling is a
re-ruling for Jeremy, not a number to accept. Only the .otf files are
substituted, never the .svg fonts: a sweep with both substituted graded 121 of
2,316 MATCH against 2,304 with the .otf alone, because D29's index is built
from the port's own .svg fonts.

THE DIAGNOSTICS GATE. Nothing above reads a diagnostic, which is how the port's
type-check message differed from upstream's in wording AND severity for the
life of the project. compare-diagnostics.py grades the port's merged sweep log
against the oracle's per-file logs from MODE=diagnostics:

    python3 compare-diagnostics.py reference/diagnostics /tmp/sweep.log
    python3 compare-diagnostics.py reference/diagnostics --selftest

TEXT-DIFFERS is not purely a wording bucket -- the first run caught the same
sentence carrying a different NUMBER, which is a layout difference wearing a
wording verdict. Read them before assuming.

THE MIDI SCOREBOARD. A separate oracle, subsuite and comparator:

    dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
        tests/regression/midi <out-dir>
    python3 compare-midi.py reference-midi/midi <out-dir>

compare-midi.py parses both sides and compares event streams with exactly four
normalisations (absolute ticks, running status expanded, the version stamp
elided, end-of-track dropped); everything else INCLUDING the order of events
sharing a tick is compared exactly, because lily/midi-chunk.cc forces
instrument changes ahead of their notes on purpose. Verdicts: MATCH,
EVENTS-DIFFER, MISSING, UNPARSEABLE. There is no MIDI ratchet yet; it is a
scoreboard read by hand.

THE STANDING VERIFICATION PROTOCOL -- all four after any comparator or font
change:

    python3 compare-output.py --selftest
    python3 compare-output.py reference/svg reference/svg    # 2316/2316 MATCH
    python3 generate-glyph-identity.py --check
    python3 compare-midi.py reference-midi/midi reference-midi/midi  # 90 of 90

A comparator that always says MATCH is worse than none; the second and fourth
lines are what caught a comparator reading zero glyphs out of every page for
four sessions.

THE DOCS COMPARISON (G8) -- BYTE FOR BYTE
-----------------------------------------
ly/generate-documentation.ly is the port's other oracle comparison, the ONLY
one that grades how values PRINT (procedures, smobs, module interfaces,
docstrings), and it is cheap:

    dotnet run --project tools/regression-harness/DocsDriver -c Release -- \
        /tmp/port-docs
    mkdir -p /tmp/oracle-docs && cd /tmp/oracle-docs \
        && ~/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond \
           ~/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/2.27.2/ly/generate-documentation.ly
    for f in /tmp/oracle-docs/*; do cmp -s "$f" "/tmp/port-docs/$(basename $f)" \
        || echo "DIFFER $(basename $f)"; done

EXPECT NO OUTPUT AT ALL: all nineteen files match byte for byte
(internals.texi at 2,619,154 bytes). The oracle takes 1.5 s and the port about
40 s, so the references are regenerated rather than committed. The output
directory is the process working directory, not an argument -- the script
writes through open-output-file with relative names -- which is why both sides
are invoked by changing into the target directory. Generation is ONCE PER
PROCESS: a second call in the same process writes nothing, reports all nineteen
missing, and does not throw.

Run it whenever the Scheme layer, an entry point, a print representation, the
module system, or the CodeBrix.LilyScheme package pin moves. It found five
defect classes the layout comparator is structurally blind to.

Upstream's procedure printer has a re-entry latch it never clears, so the
manual carries 206 degraded "#<program ...>" forms against 29 ordinary ones,
and the port reproduces the latch deliberately. Soft-port buffering decides
where an abort lands, so a change to port buffering (in CodeBrix.LilyScheme)
changes which procedures print which way.

THE PARITY PROBES, AND WHERE THE RULINGS COME FROM
--------------------------------------------------
tools/parity-probes/ holds the instruments behind every "measured" ruling in
PORT-COVERAGE.txt and on the project board: a probe .ly that isolates one
behaviour, the script that paired the marks, the driver that ran both engines
over the same input. They were vendored on 2026-08-27 so that the measurements
a ruling rests on can be reproduced by someone other than the session that made
it. They are historical instruments, not a suite: nothing there runs on its own,
several hard-code paths from the session that wrote them, and the directory
names are deliberately unchanged because rulings cite them by path. Read a
probe before running it. Their captured outputs are program output, not copies
of upstream material, and nothing under tools/ is packed.

THE TOOL SOLUTIONS' OWN TESTS
-----------------------------
tools/Lily.Docs and tools/Lily.Shell have their own suites and their own
solution files, deliberately outside CodeBrix.LilyPort.slnx (decision D52: the
shipped package must not acquire the Texinfo -> Html2Pdf dependency chain).
`dotnet test Lily.Docs.slnx -c Release' and
`dotnet test --solution Lily.Shell.slnx -c Release' respectively; the Lily.Docs
render gates take minutes (the notation manual alone is two and a half thousand
engravings) and are NOT part of the port's battery. Lily.Docs.slnx LISTS the
five engine projects on purpose: a project a solution does not name builds in
its own default configuration, and `dotnet test -c Release' once silently ran
against a Debug engine. Details in tools/Lily.Docs/README.txt,
tools/Lily.Shell/README.txt and EXTRAS-README.txt.

PACKAGING AND PUBLISHING
========================
    dotnet pack src/CodeBrix.LilyPort/CodeBrix.LilyPort.csproj -c Release

Pack the FACADE project. It is the only project that is packed: IsPackable is
false on the other four src projects, on all five test projects, and on every
driver and tool project in CodeBrix.LilyPort.slnx and Lily.Docs.slnx (the
Lily.Shell projects do not set it, but they live in their own solution and are
never packed). The facade references nothing under tools/ -- verified as part
of decision D52 and re-checked by the parity-probes README on 2026-08-27.
GeneratePackageOnBuild is false, so nothing packs by accident;
PackageRequireLicenseAcceptance is true.

ONE PACKAGE, FIVE ASSEMBLIES
----------------------------
The facade references Flower, Engine, Backends and Parsing with
PrivateAssets="all", which stops each of them becoming a package DEPENDENCY.
TargetsForTfmSpecificBuildOutput then runs the IncludeSubAssembliesInPackage
target, which places each sub-assembly's .dll and .xml into the package's lib/
folder as BuildOutputInPackage items. This is the CodeBrix.Audio pattern.

CONSEQUENCE: THIS PROJECT MUST SIT ON TOP OF EVERYTHING IT BUNDLES, because you
can only bundle what you reference. A new assembly added below the facade needs
a PrivateAssets="all" ProjectReference AND a pair of BuildOutputInPackage lines
(dll + conditional xml), or it will compile into the solution and be absent
from the package.

THE LilyScheme DEPENDENCY RULE
------------------------------
CodeBrix.LilyScheme.LgplLicenseForever is a real nuget.org DEPENDENCY of the
package, not a bundled sub-assembly. It is declared in TWO csprojs, and both
declarations are required:

  * the Engine csproj, which is what actually uses it; and
  * the facade csproj, AGAIN -- because the facade's Engine reference is
    PrivateAssets="all", which suppresses everything the Engine would otherwise
    contribute to the nuspec. Without the facade's own PackageReference the
    packed nuspec carries NO LilyScheme dependency, and a consumer that
    references only this project (the regression BatchDriver did) compiles
    clean and fails at run time unable to load the interpreter assembly.

Keep the two Version attributes IDENTICAL. The Engine csproj carries the old
ProjectReference into the sibling repository commented out with the family's
`//was previously:' marker, and the comment beside it states the rule:
the interpreter is consumed as its PUBLISHED package, and nothing in this
repository may reach outside the repository with a filesystem reference. This
repository is a consumer of that package only -- CodeBrix.LilyScheme's own test
suite is never run from here. A pin bump is a deliberate change made in both
csprojs at once and then re-verified: the five suites, and the G8 docs
comparison (a new interpreter is exactly the case "anything in the module
system moves").

VERSIONING
----------
Date-stamped and auto-incrementing, computed in every src csproj from
System.DateTime.UtcNow:

    1.<years since _VersionBaseYear>.<day of year>.<minute of day UTC>

Every field is derived from the clock, so the version strictly increases; two
builds within the same UTC minute produce the same version (do not publish two
packages from one minute); and it is NOT SemVer -- minor encodes the year and
major is pinned to 1, so major/minor say nothing about API compatibility. All
five csprojs carry the same block so the bundled assemblies and the package
agree. To re-baseline, change _VersionBaseYear in all five.

LilyPortInfo.Version reports THIS version (the package's); LilyPortInfo
.CompatibleWithVersion reports the LilyPond version the port tracks (2.27.2,
from Engine/Bootstrap/LilyVersion.cs). They are never conflated: anything that
shows "LilyPort 2.27.2" to a user is a defect.

WHAT SHIPS INSIDE THE .nupkg (all from the repo root, via <None Pack="true">):

    icon-codebrix-128.png      the PackageIcon
    README.md                  the PackageReadmeFile
    AGENT-README.txt           the consumer documentation
    THIRD-PARTY-NOTICES.txt    the attribution and compliance ledger
    LICENSE                    the full GPL-3 text
    LICENSE.OFL                the full SIL OFL 1.1 text (the fonts)

plus lib/net10.0/ with the five assemblies and their XML doc files, and the
embedded resources inside them (the Scheme layer, the fonts, the grammar
mirror). No loose font file travels in the package -- PackagedFontTests asserts
it against the built .nupkg. COPYING.FDL is NOT packed: nothing FDL-licensed is
in the package.

Family rules that apply here as everywhere in CodeBrix: net10.0 only; the
package id carries its licence in its suffix (GplLicenseForever); the icon,
readme, agent-readme, notices and licence texts are packed from the repo root.

PROVENANCE AND VENDORED SOURCES
===============================
Upstream: GNU LilyPond, https://gitlab.com/lilypond/lilypond, tag v2.27.2,
commit 2d621459bd44cb1758f822a69757242eab843060. Every mirror, table and
PORT-COVERAGE file in the repository names that commit, and LilyPortInfo
.UpstreamCommit carries it. THIRD-PARTY-NOTICES.txt is the complete, living
record; this section is the map to it.

WHAT IS VENDORED VERBATIM (byte-identical, never edited):

    src/CodeBrix.LilyPort.Engine/Scheme/lily/*.scm    91 files <- scm/
    src/CodeBrix.LilyPort.Engine/Scheme/ly/*.ly       62 files <- ly/, including
                                                      articulate.ly (GPL-3-only)
    parser-mirror/parser.yy, lexer.ll                 <- lily/
    mf/                                               115 files <- mf/ (the
                                                      Metafont sources and
                                                      generator scripts)
    book-mirror/                                      4 files <- scripts/ and
                                                      python/ (lilypond-book)
    Documentation/                                    690 files <-
                                                      Documentation/ (FDL;
                                                      snippets/ public domain),
                                                      MANIFEST.sha256
    tests/regression/                                 2,146 .ly + 29 .ily +
                                                      73 midi/ .ly <-
                                                      input/regression/
    tools/font-build/mf2pt1.pl, mf-to-table.py        <- scripts/build/
                                                      (mf2pt1 is Scott Pakin's,
                                                      LPPL, not GPL)
    assets/fonts/text/                                24 prebuilt text faces
                                                      <- the official 2.27.2
                                                      binary distribution
    tests/fixtures/lilypond-fonts/                    LilyPond's own nine
                                                      Emmentaler .otf <- the
                                                      official 2.27.2 binary
                                                      distribution; fixtures
                                                      only

THIRD-PARTY-NOTICES.txt section 11.6 lists vendored files edited away from
upstream; it reads "(none)". Keep it that way: minimising changes to
LilyPond's Scheme is an explicit project goal, and a verbatim mirror is what
makes a re-sync a straight copy plus a diff. Each mirror directory's README
carries its own re-sync procedure (parser-mirror/README.txt,
book-mirror/README.txt, Documentation/README.txt, tools/font-build/README.txt
section 11).

WHAT IS PORTED (C++ and Python to C#):

    flower/            -> src/CodeBrix.LilyPort.Flower/, with upstream's own
                          tests translated
    lily/*.cc          -> src/CodeBrix.LilyPort.Engine/ (419 of 448 files
                          ported, 29 recorded no-ports; the ledger below is the
                          count, not this sentence)
    lily/parser.yy     -> the 479 rule-action bodies under
                          src/CodeBrix.LilyPort.Parsing/Actions/ (the grammar
                          itself is READ from the mirror, not translated)
    lily/lexer.ll      -> src/CodeBrix.LilyPort.Parsing/Lexing/ (a modal
                          scanner with 13 exclusive start conditions)
    python/convertrules.py + scripts/convert-ly.py, scripts/abc2ly.py,
    scripts/midi2ly.py + python/midi.py, scripts/musicxml2ly.py + its python/
    modules            -> src/CodeBrix.LilyPort/ConvertLy/ and Importers/
    python/book_snippets.py (compose_ly) -> tools/Lily.Docs (a tool, not the
                          package)

Every ported file preserves upstream's copyright header verbatim above the
usings and carries, on its namespace line, the provenance comment and the
modification notice GPL-3 section 5(a) requires:

    namespace CodeBrix.LilyPort.<Area>; //was previously: lily/beam.cc;
    // Modified by Jeremy Ellis on <YYYY-MM-DD> as part of the CodeBrix port.

The `//was previously:' comment alone does NOT satisfy 5(a) -- it names neither
who nor when. 390 files under src/ carry the marker today. Files written from a
published specification rather than from upstream code (the Type 2 charstring
interpreter, the MT19937 generator in Bootstrap/RandomPrimitives.cs) carry no
marker and no fabricated upstream header; THIRD-PARTY-NOTICES.txt records each
such case explicitly. Our own copyright line is added additively and never
displaces an upstream one. "LilyPond" appears in no assembly name, package id,
namespace or type name (copyleft grants no trademark rights); it is named in
prose as attribution only.

THE LEDGER. src/CodeBrix.LilyPort.Engine/lily-cc-ledger.tsv carries one row per
upstream lily/*.cc file with its disposition (ported / group / no-port) and the
C# file(s) it landed in. It is embedded, read by PortLedger.cs, and fenced by
LedgerTests: a file cannot be in two groups, in none, or claim a C# file that is
not on disk. "What is left" is COMPUTED from it, never remembered.
entry-point-na-candidates.tsv beside it is the rolling list of entry points
proposed as not-applicable (decision D25): a session that meets one appends a
row THEN, while it still has the context to say why.

WHAT IS GENERATED: see GENERATED FILES under BUILDING.

THE FONTS PIPELINE
------------------
The port does NOT redistribute LilyPond's prebuilt MUSIC fonts. It vendors the
Metafont sources (mf/) and builds Emmentaler itself -- ruling R19, final -- so
provenance runs end to end from Metafont source to shipped binary:

    mf/*.mf  --(mf2pt1, mpost)-->  .pfb/.tfm/.log  --(mf-to-table.py)-->
    .lisp/.global-lisp  --(fontforge, gen-emmentaler*.fontforge.py)-->
    assets/fonts/otf/emmentaler-{11,13,14,16,18,20,23,26,brace}.otf
    assets/fonts/svg/emmentaler-*.svg

tools/font-build/build-fonts.sh is a standalone reimplementation of the
font-producing subset of upstream mf/GNUmakefile; its README section 2 lists
the Debian packages and the EXACT toolchain versions used (TeX Live Metafont
and MetaPost, FontForge 20230101, python3-fontforge). TOOLCHAIN VERSIONS ARE
LOAD-BEARING: a different FontForge produces different glyph OUTLINES (not just
bytes), the skyline builder reads outlines, and eleven corpus rows are decided
by that alone. The build is byte-reproducible (SOURCE_DATE_EPOCH set to the
v2.27.2 tag's commit timestamp; USER/LOGNAME set to the designers' names so
FontForge's "By" line credits them, not the builder). The outputs are
COMMITTED and are not part of `dotnet build'; nobody needs the toolchain to
build or use the port.

Against the official 2.27.2 fonts, the LILC and LILY tables (the engraver's
metric source), the glyph inventory (668 per design size, 577 brace) and every
advance width are identical; bounding boxes differ by 1-3 units of a 1000-unit
em on about 18% of glyphs, and outlines differ structurally. The engraver reads
dimensions from LILC, so layout is identical everywhere except the skyline
path -- which is exactly what THE FONT-PARITY PAIR under TESTING measures and
prices.

If the fonts are ever rebuilt on a different toolchain, IN THE SAME SESSION:
re-run the font-parity pair and re-write font-delta-ledger.tsv with a reasoned
record of every changed number; re-run generate-glyph-identity.py (the
committed index is built from the port's own fonts and goes stale with them;
--check is the fence); re-run GlyphOutlineSkylineTests; update
assets/fonts/README.txt (build date, checksums) and THIRD-PARTY-NOTICES.txt
section 3.

LICENCE OF THE FONT SOURCES. Everything under mf/ is dual-licensed at the
recipient's option: GPL-3.0-or-later WITH the LilyPond Font Exception, OR SIL
OFL 1.1. Both options are conveyed onward, which is why LICENSE.OFL is packed.
"Emmentaler" and "Feta" are OFL Reserved Font Names; the reservation binds
MODIFIED fonts only, and building unmodified sources through the documented
pipeline is not a modification. IF ANY FILE UNDER mf/ IS EVER EDITED, THAT
CHANGES: either rename the font or take the GPL+Font-Exception option, add a
modification notice to the edited file, and record the decision in
THIRD-PARTY-NOTICES.txt section 3. mf2pt1 (LPPL) must not be edited in place at
all -- LPPL requires renaming modified versions; wrap it instead.

THE TEXT FACES. assets/fonts/text/ holds 24 prebuilt faces (URW C059, Nimbus
Sans, Nimbus Mono PS; TeX Gyre Schola, Heros, Cursor) vendored byte-for-byte
from the official 2.27.2 binary distribution (decision D13), because text parity
needs metrics identical to the oracle's. ASSETS FOREVER, NEVER EDIT: subsetting
or converting one creates a derived font, wakes the GUST rename request, and
attaches AGPL source obligations. The chain deliberately ENDS at the TeX Gyre
face: there is NO fallback to system fonts, so uncovered scripts render as
.notdef on both sides (decision D23). No Roboto or other CodeBrix font package
is referenced by the Engine; the Roboto packages in the repository belong to
Lily.Shell's UI chrome only.

THE LilyPond-BUILT FIXTURES. tests/fixtures/lilypond-fonts/ holds LilyPond's
own nine Emmentaler .otf files (SHA256SUMS.txt beside them) as a MEASURING
INSTRUMENT for the font-parity pair. They live under tests/, not assets/, so no
asset glob can reach them; PackagedFontTests fences that they never ship. The
.svg fonts are deliberately absent from that directory -- do not add them.

DOCUMENTATION/ AND THE FDL BOUNDARY
-----------------------------------
Documentation/ is the @include closure of the nine corpus manuals (notation,
learning, usage, extending, essay, changes, music-glossary, contributor,
snippets) plus the snippets, pictures, ly-examples and bib sources they name.
It exists so that tools/Lily.Docs can render the manuals from the repository
alone, keeping rule 7 intact (no build or test step reads an upstream
checkout). The render gates therefore ALWAYS run; a gate that skipped when a
checkout is absent would now be a defect.

The GNU FDL is not a software licence and is not GPL-compatible in either
direction, so:

  * FDL text must never be copied into source files or XML doc comments
    anywhere under src/. The FDL tree is Documentation/ and nothing else.
  * The port's own nineteen generated documentation files are build products
    of the engine, not derived from the mirror.
  * The ENGRAVED PICTURES a manual render produces (2,557 SVGs for the
    Notation Reference alone) are derived from FDL material inside a GPL-3
    repository. They are never committed and must NOT be taken as test
    fixtures -- not here, and not by the MIT-licensed packages that place
    them. tools/Lily.Docs/svg-dialect/inventory.tsv, a 57-line vocabulary of
    element, attribute and font-family names with counts, is the deliverable
    that travels in their place; verification against the real pictures
    happens only in Lily.Docs' gates. THIRD-PARTY-NOTICES.txt section 4 and
    tools/Lily.Docs/svg-dialect/README.txt carry the rule.
  * texi2any is an ORACLE run by hand against an upstream checkout, never
    against this mirror (an oracle reading our copy would inherit any copy
    defect on both sides). texi2any's source is never mirrored or read;
    lilypond-book's is the one named exception (book-mirror/), because the
    snippet-composition semantics must be reproduced exactly.

THE SEPARATION FROM CodeBrix.LilyScheme
---------------------------------------
LilyPond-derived source lives ONLY in this repository (GPL-3); Guile-derived
source lives ONLY in CodeBrix.LilyScheme (LGPL-3). A single LilyPond-derived
file placed in the Scheme repository would forfeit its LGPL position
permanently. This project, being GPL-3, consumes the LGPL library freely.

PORT-COVERAGE.txt -- THE DIVERGENCE RECORD
------------------------------------------
Each of the four ported projects carries a PORT-COVERAGE.txt:

    src/CodeBrix.LilyPort.Engine/PORT-COVERAGE.txt    (the large one)
    src/CodeBrix.LilyPort.Parsing/PORT-COVERAGE.txt   the rule-action layer
    src/CodeBrix.LilyPort.Flower/PORT-COVERAGE.txt    ported / not ported /
                                                      differs, with test counts
    src/CodeBrix.LilyPort/PORT-COVERAGE.txt           the text converters

They record every DELIBERATE DIVERGENCE from upstream with its reason, the
upstream-file to C#-file mapping, what is deliberately not ported and why, and
the defects each pass found -- so that "this file is a literal translation" is
never assumed and "milestone N is complete" is a checkable claim. Read the
relevant one before touching a ported file. The Engine file is organised as a
sequence of dated, appended sections (one per porting pass or parity wave);
its opening sections cover value-type semantics (Stencil and Spring are mutable
STRUCTS with an initialised flag), naming (class Music -> MusicObject, class
System -> SystemGrob, and the porting-group-to-functional rename table), and
upstream behaviour that looks like a bug but is reproduced on purpose.

HOW TO MAINTAIN IT: when a session diverges from upstream on purpose, chooses
not to port something, reproduces an upstream defect, or measures a behaviour
against the oracle, it APPENDS a dated entry to the owning project's
PORT-COVERAGE.txt in the same change, naming the upstream file, the C# file,
the reason, and the test that fences it. Entries are never rewritten to look
tidier; a superseded entry gets a dated correction beside it (the file is full
of "CORRECTED <date>" paragraphs, and that is the intended shape). Things that
live only in a session's status file are structurally invisible to the next
session -- the Engine file's last entry exists precisely because a known quirk
had been recorded nowhere the repository could see. Consumer-visible
consequences go to AGENT-README.txt as well.

THIRD-PARTY-NOTICES.txt -- THE COMPLIANCE LEDGER
------------------------------------------------
It is updated IN THE SAME COMMIT as any change that incorporates, adapts,
modifies or removes third-party source; it is never allowed to fall behind the
code. Four checks, all of which passed at the 2026-08-15 audit and should be
re-run after any bulk change: (a) every derived file (upstream header or
`//was previously:' marker) has an entry, matched BY PATH, never by base name;
(b) every entry points at a file that exists; (c) every upstream path named
exists in the pinned tree; (d) every copyright year range matches its file.
Read a failing count from such a sweep as a statement about the sweep first.

Its sections: 1 LilyPond (the five licence regimes, scope, marking, the
per-file inventory in 1.5), 2 articulate.ly, 3 the music fonts, 4 the FDL
documentation, 5 the public-domain snippets, 6 the MIT MusicXML suite,
7 Pygments, 8 dllist.py, 9 the font-build toolchain (mf2pt1, LPPL), 10 the
text fonts and how fonts ship, 11 the GPL-3 obligations observed, 11b the
MT19937 provenance, 12 the related repositories and the D52 tooling chain.

It MUST be updated when: a file is ported (an entry in 1.5); a mirror is
re-synced (the pinned revision, counts and sha256s); the fonts are rebuilt
(section 3 and the toolchain record); a vendored file is edited away from
verbatim (11.6 plus a per-file notice -- currently none); a fixture or asset is
added; a package dependency of a TOOL changes (section 12.1, which lists the
Lily.Docs / Lily.Shell chain as dependencies, not incorporated material).

CODING CONVENTIONS
==================
* Target framework is always net10.0 -- no multi-targeting.
* <Nullable> is never enabled in src/ or tests/ (family-wide). Do not write
  `?' on reference types and never use the null-forgiveness `!' operator.
  Value-type nullables (int?, bool?) are fine. The tool csprojs say
  <Nullable>disable</Nullable> explicitly.
* File-scoped namespaces only. The top of every .cs file is: the licence
  header, the usings (System.* first, then alphabetical, fully qualified, no
  blank lines inside the block), the namespace line. On a ported file the
  upstream copyright header comes first, verbatim, and the namespace line
  carries the `//was previously:' comment and the modification notice.
* `//was previously:' is the family's marker for anything that used to be
  otherwise: a ported file's upstream origin, a replaced ProjectReference in a
  csproj, a superseded dependency in THIRD-PARTY-NOTICES.txt. Comment out and
  mark; do not delete history silently.
* Every public type and public/protected member has an XML doc comment.
  CS1591 is fixed at the source, never suppressed.
* No project-level warning suppression in src/ or tests/.
* Each src project has an InternalsVisibleTo.cs granting exactly its own
  .Tests project and nothing else.
* Source is organised into sub-folders that are also namespaces (Layout/,
  Objects/, Translation/, ...); entry types sit at the project root.
* Tests: xUnit v3, SilverAssertions, coverlet.collector, snake_case names,
  //Arrange //Act //Assert, TestContext.Current.CancellationToken.
* Upstream's own defects are reproduced, not corrected, and recorded in
  PORT-COVERAGE.txt (the procedure-printer latch, the once-per-clause quirks,
  the aesthetic scorers translated faithfully rather than cleanly). A
  divergence is a RULING with a measurement behind it, never a tidy-up.
* Skia: nothing under src/ or tests/ references SkiaSharp or any Skia type,
  and nothing may. The only Skia in the repository is what CodeBrix.Platform
  imposes on the Lily.Shell desktop heads under tools/, and it is a fact
  about that shell's UI, not about the port or the documentation chain
  (which has been fully managed since the 2026-08-26 package bump).

NOTES
=====
THE TWENTY-SECOND BOOT, AND THE EXPANSION CACHE
-----------------------------------------------
Booting the vendored Scheme layer costs roughly twenty seconds per process
(the Lily.Shell README says ~20 s warm and ~67 s the first time after a build,
which is JIT). Measured, ~25 s of a ~26 s cold boot is macro-expanding the
.scm files through psyntax; replaying recorded Tree-IL takes ~50 ms.
Engine/Bootstrap/BootExpansionCache.cs does that replay:

  * The cache key is a SHA-256 over the MVIDs of the CodeBrix.LilyScheme and
    CodeBrix.LilyPort.Engine assemblies plus the name and content of every
    embedded .scm resource in both. Deterministic builds make an MVID a content
    identity, so a real change to either binary invalidates the cache and a
    no-change rebuild does not. Any mismatch is a MISS: the boot loads live
    (about half a minute) and re-records.
  * Each interpreter gets its OWN deserialised instance (~75 ms), because
    recorded Tree-IL holds quoted constants that become live mutable data.
  * LILYPORT_EXPANSION_CACHE=0 disables it (for A/B against a live boot);
    LILYPORT_EXPANSION_CACHE_DIR overrides the directory, which defaults to
    $XDG_CACHE_HOME/CodeBrix.LilyScheme or ~/.cache/CodeBrix.LilyScheme on
    Linux (%LOCALAPPDATA% and ~/Library/Caches on the other platforms).
    BatchDriver pins a relative override to a full path at startup, because it
    changes directory per file.

WHAT IT IMPLIES: the first test run or sweep after a rebuild that changed the
Engine or the interpreter pays one live boot; everything after replays. A test
project boots the engine per process, not per test, so the suites are shaped
around a shared, restored engine (LilyPondInit.RestoreDefaults) rather than a
fresh one; every tool that hosts the engine (BatchDriver, DocsDriver,
Lily.Docs, Lily.Shell) does the same. Nine separate cross-file leak defects
have come from state surviving between inputs in one process -- the
SessionLeakEndToEndTests class and the per-file scratch directory exist
because of them. Treat any new process-global state as a leak until proven
otherwise.

OTHER THINGS A MAINTAINER MUST KNOW
-----------------------------------
* The regression sweep, the docs run, Lily.Docs renders and the tools are NOT
  part of `dotnet test'. The battery for a change to src/ is: the five suites,
  the sweep graded through the ratchet (with the font-parity pair when fonts
  or skylines are involved), the diagnostics gate when messages move, and the
  G8 docs comparison when the Scheme layer or an entry point moves. The
  facade's PORT-COVERAGE.txt says even a converter-only change still owes the
  full battery, with every corpus figure expected to reproduce EXACTLY.
* tools/regression-harness/README.txt still describes the committed manifest
  as "manifest.tsv" in section 4; the actual committed files are
  reference-manifest.tsv and reference-status.txt (the .gitignore beside it
  says so). reference/ and candidate/ are working directories.
* tools/Lily.Docs finds the corpus by walking up from the running assembly to
  CodeBrix.LilyPort.slnx, so the eight corpus manuals need the repository;
  the Internals Reference renders from the vendored assets alone, on purpose.
* Do not add a .github/workflows/*.yml file to this repository.
* The AI-agent pointer stubs all point at README-INDEX.txt and are maintained
  centrally across the CodeBrix family. Do not edit them here.
* AGENT-README.txt is packed into the .nupkg. Anything added to it ships to
  every consumer, so repository-internal material belongs in this file or in
  EXTRAS-README.txt instead.
* CodeBrix.LilyPort has been its own repository at
  https://github.com/ellisnet/CodeBrix.LilyPort, with the tree at the root,
  since 2026-08-27. Before that it lived as a sub-folder of the
  CodeBrix.Samples.Gpl3 repository (which still holds Fresco.Brix, the
  Frescobaldi port built on this package). Any path or sentence that still
  says "Samples.Gpl3" or "CodeBrix.LilyPort/tools/..." is a leftover from the
  old layout and should be corrected when met; paths in this file are
  repo-root-relative.
