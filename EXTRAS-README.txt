================================================================================
EXTRAS-README: CodeBrix.LilyPort
Samples, tools and other content in this repository that is not part of a NuGet package
================================================================================

This repository ships ONE NuGet package (CodeBrix.LilyPort.GplLicenseForever,
built from the five src/ projects). Everything else in the tree exists to
measure, document, exercise or supply that package, and none of it is packed:
tools/ is not in the package at all, and the test projects are not packable.

Two things to know before reading any section:

  * CodeBrix.LilyPort.slnx lists ONLY the five src/ projects, the five tests/
    projects, and the two regression-harness drivers (BatchDriver and
    DocsDriver, under the solution's /Tools/ folder). Every other project under
    tools/ is built on its own -- `dotnet run --project <csproj>` or its own
    .slnx -- and each section below says which. Tools without a csproj are
    Python or shell scripts.

  * Several tools run an ORACLE: the official GNU LilyPond 2.27.2 binary, or a
    script from its installation. The oracle is never needed to build, test or
    consume the package; it is needed only to (re)generate a reference or a
    fixture set. The tool READMEs address it as ~/ClaudeHome/oracle/
    lilypond-2.27.2, a local path outside this repository. No build or test
    step reads an upstream LilyPond checkout (standing rule 7 of the port).

Paths below are relative to the repository root. Each tool's own README.txt is
the authority on it and is much longer than its section here; this file says
what each thing is, how it is started, and what to watch for.

For building, testing, packing and versioning the package itself, see
MAINTAINER-README.txt. For consuming the package, see AGENT-README.txt.

--------------------------------------------------------------------------------
CONTENTS
--------------------------------------------------------------------------------

  ENGINE DRIVERS
    tools/regression-harness         the port graded against LilyPond, page by
                                     page, with a committed ratchet
    tools/parity-probes              the measurement instruments behind the
                                     port's rulings

  IMPORTER AND CONVERTER PROBES
    tools/abcprobe                   ABC importer oracle harness
    tools/midi2lyprobe               MIDI importer oracle harness
    tools/musicxml2lyprobe           MusicXML importer oracle harness
    tools/convertlyprobe             convert-ly fixture generator
    tools/convertrules-port          convertrules.py -> C# generator
    tools/mutopia-probe              the port over a local Mutopia corpus

  DOCUMENTATION TOOLS
    tools/Lily.Docs                  renders LilyPond's nine manuals
    tools/table-extract              recovers Internals Reference metadata
                                     from upstream C++
    tools/parser-baseline            the one-time Bison automaton anchor

  THE APPLICATION
    tools/Lily.Shell                 the interactive engine shell

  THE FONT PIPELINE
    tools/font-build                 builds Emmentaler from mf/

  OTHER NON-PACKAGE CONTENT
    Documentation/                   GFDL mirror of LilyPond's manual sources
    book-mirror/                     lilypond-book sources, mirrored
    parser-mirror/                   parser.yy and lexer.ll, mirrored
    mf/                              the Metafont font sources, mirrored
    assets/                          the fonts the package ships
    tests/fixtures/ and other test data
    box.eps                          a side-file the regression corpus writes


================================================================================
tools/regression-harness
================================================================================

WHAT IT IS
    The machinery that answers "is CodeBrix.LilyPort engraving the same music
    LilyPond does?" LilyPond's lily/ has no unit tests; upstream establishes
    correctness by rendering input/regression/*.ly and comparing against a
    previous run of itself. This harness renders the same corpus --
    tests/regression, 2,146 .ly inputs vendored verbatim, plus their 29 .ily
    includes -- with a pinned LilyPond 2.27.2 oracle, then grades the port's
    output against that reference PAGE BY PAGE. It holds the port to a
    committed per-file floor (the ratchet), grades MIDI on a separate
    scoreboard, grades the port's DIAGNOSTICS against the oracle's, and grades
    the nineteen files ly/generate-documentation.ly writes byte for byte.

    Two of its pieces are C# and are the only tool projects in the main
    solution (folder /Tools/ of CodeBrix.LilyPort.slnx):

      BatchDriver/   CodeBrix.LilyPort.BatchDriver -- runs the engine over a
                     directory of .ly files in ONE process (one Scheme boot),
                     writing .svg pages and .midi files. Every input runs from
                     its own emptied scratch directory under the system temp
                     folder, and the output directory is emptied of top-level
                     .svg files at startup with an end-of-run self-check that
                     the directory holds exactly the pages the sweep wrote.
      DocsDriver/    CodeBrix.LilyPort.DocsDriver -- runs the vendored
                     ly/generate-documentation.ly and writes its nineteen
                     files (internals.texi and eighteen Notation Reference
                     appendices) into a directory.

    The rest is Python 3 (standard library) and bash:

      generate-reference.sh        drives the oracle over tests/regression;
                                   MODE=svg (default) writes the parity corpus,
                                   MODE=diagnostics writes one warning log per
                                   input instead
      generate-midi-reference.sh   the same for tests/regression/midi
      compare-output.py / .sh      the layout comparator (see NOTES)
      compare-midi.py              the MIDI comparator
      compare-diagnostics.py       the diagnostics comparator
      ratchet.py                   the per-file floor: check / update /
                                   rebaseline / self-test
      font-delta.py                prices the port's own font build against
                                   the oracle, page by page, in millimetres
      generate-glyph-identity.py   builds the committed glyph-name index
      reference-fonts.conf.in      the fontconfig template that PINS the
                                   oracle's generic font families to the same
                                   24 faces the port vendors

HOW TO RUN
    Prerequisites: the official LilyPond 2.27.2 Linux tarball (self-contained;
    extract anywhere and point LILYPOND_BIN at bin/lilypond), python3, GNU
    coreutils. Match the version to the port: a different LilyPond measures
    version drift, not port fidelity.

    Generate the layout reference (about four minutes at JOBS=10):

        cd tools/regression-harness
        export LILYPOND_BIN=/path/to/lilypond-2.27.2/bin/lilypond
        ./generate-reference.sh [OUTPUT_DIR]

        Environment: JOBS (default nproc), LIMIT (first N inputs, 0 = all),
        PER_FILE_TIMEOUT (seconds, default 60), MODE (svg | diagnostics).

    Sweep the port over the corpus (from the repository root):

        dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
            tests/regression tools/regression-harness/candidate/svg > /tmp/sweep.log

        BatchDriver SUITE_DIR OUT_DIR [--limit N] [--files a.ly,b.ly]
            [--keep-existing] [--diagnostics] [--fonts DIR] [--point-and-click]

    Grade it, then gate it:

        cd tools/regression-harness
        ./compare-output.sh reference/svg candidate/svg
        python3 compare-output.py reference/svg candidate/svg --tsv /tmp/run.tsv
        ./ratchet.py check /tmp/run.tsv          # fails on any backslide
        ./ratchet.py update /tmp/run.tsv         # ratchet forward
        ./ratchet.py rebaseline /tmp/run.tsv --reason "why" [--only a.svg,b.svg]

    The font-parity pair (every graded run goes both ways):

        dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
            tests/regression /tmp/gate --fonts tests/fixtures/lilypond-fonts
        python3 compare-output.py reference/svg /tmp/gate --tsv /tmp/gate.tsv
        python3 font-delta.py candidate/svg /tmp/gate --gate /tmp/gate.tsv \
            --check font-delta-ledger.tsv

    MIDI:

        ./generate-midi-reference.sh                 # regenerates reference-midi/
        dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
            tests/regression/midi <out-dir>
        python3 compare-midi.py reference-midi/midi <out-dir>

    Diagnostics (the streams MUST be merged -- attribution depends on it):

        LILYPOND_BIN=... MODE=diagnostics ./generate-reference.sh
        dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
            tests/regression tools/regression-harness/candidate/svg > /tmp/sweep.log 2>&1
        python3 compare-diagnostics.py reference/diagnostics /tmp/sweep.log

    The docs run (expect NO output from the loop -- all nineteen files match):

        dotnet run --project tools/regression-harness/DocsDriver -c Release -- /tmp/port-docs
        mkdir -p /tmp/oracle-docs && cd /tmp/oracle-docs \
            && ~/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond \
               ~/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/2.27.2/ly/generate-documentation.ly
        for f in /tmp/oracle-docs/*; do cmp -s "$f" "/tmp/port-docs/$(basename $f)" \
            || echo "DIFFER $(basename $f)"; done

    The standing self-checks, after any comparator or font change:

        python3 compare-output.py --selftest
        python3 compare-output.py reference/svg reference/svg     # 2316 of 2316 MATCH
        python3 generate-glyph-identity.py --check
        python3 compare-midi.py reference-midi/midi reference-midi/midi   # 90 of 90
        python3 compare-diagnostics.py reference/diagnostics --selftest
        ./ratchet.py self-test

NOTES
    * COMMITTED vs GENERATED. The tool's own .gitignore excludes reference/,
      candidate/ and out/. The durable records are the committed tables:
      reference-manifest.tsv (sha256 + size per reference page, with a header
      recording the oracle and the font pinning), reference-status.txt (per-
      input outcome), pass-manifest.tsv (the ratchet floor, one row per page),
      pass-manifest-decisions.tsv (append-only: every time a floor came down,
      and why), g1-skip-list.tsv (rows ruled out of gate G1), font-delta-
      ledger.tsv (the font build's cost per page, against a 0.05 mm ceiling),
      glyph-identity.tsv (hashes only -- no font file or outline is
      redistributed). reference-midi/ IS committed (364 KB) because that is
      cheaper than the machinery for not committing it. The reference SVGs
      (62 MB, ~2,300 pages) are regenerated, never committed.

    * THE FONTS ARE PINNED, AND THEY HAVE TO BE. Under -dbackend=svg the oracle
      names the generic families "serif", "sans", "monospace" and resolves
      them through the HOST's fontconfig. generate-reference.sh builds
      reference-fonts.conf from reference-fonts.conf.in and points
      FONTCONFIG_FILE at it, pinning those names to the oracle's own bundled
      faces -- byte-identical to the 24 the port vendors under
      assets/fonts/text. A reference generated without the pinning moves every
      text run in the corpus and makes the ratchet floor meaningless. The
      manifest header records the pinning; check it before trusting a corpus
      you did not generate.

    * THE COMPARATOR IS NOT A BYTE DIFF. compare-output.py grades each page on
      a ladder -- MATCH, PLACEMENT-DIFFERS, PLACEMENT-ORDER, PLACEMENT-COUNT,
      GLYPHS-DIFFER, UNPARSEABLE, MISSING -- so progress is measurable before
      the first pass. Glyph identity is NAMED-GLYPH identity: a music glyph's
      exact outline bytes are resolved to a glyph NAME through
      glyph-identity.tsv, each side against its own fonts, because the port
      and the oracle draw from different FontForge builds of the same font.
      Everything else stays byte-exact. --raw-glyph-bytes forces the old byte
      rule and is diagnostic only. The comparator prints a per-side resolution
      rate on every run; 0.0% there means the normalization slipped.

    * ratchet.py update cannot lower a row. Lowering is `rebaseline`, which
      demands a written --reason and appends to pass-manifest-decisions.tsv.

    * --keep-existing opts out of BatchDriver's startup clean and its end-of-
      run self-check, for a partial --files/--limit run that is deliberately
      adding to a full sweep; a run made that way is not evidence. Never
      rebuild while a sweep is running -- the build rewrites the Engine DLL
      out from under it.

    * BatchDriver reports anything an input wrote to its scratch directory as
      a SIDE-FILE line. Over the full suite exactly two inputs write:
      event-listener-output (-violin-1.notes) and markup-eps (box.eps -- see
      the box.eps section at the end of this file).

    * The docs run generates ONCE PER PROCESS: a second call to the generator
      in the same process writes nothing, reports all nineteen files missing,
      and does not throw. DocsDriver never meets this; a long-lived host
      (Lily.Shell, Lily.Docs' test suite) does.

    * MIDI has no ratchet yet; it is a scoreboard read by hand. Comparison is
      of event streams with four normalisations (absolute ticks, running
      status expanded, version stamp replaced, end-of-track dropped) and is
      otherwise exact, including the order of events sharing a tick.

    * The regression inputs are GPL-3.0-or-later (THIRD-PARTY-NOTICES.txt
      section 1). input/regression/musicxml/ upstream is MIT and is NOT
      vendored here; its copy lives with the MusicXML importer fixtures (see
      tools/musicxml2lyprobe).


================================================================================
tools/parity-probes
================================================================================

WHAT IT IS
    The measurement instruments the port's RULINGS rest on. When a note in
    PORT-COVERAGE or on the LilyPort board says a value was "measured", these
    are what measured it: the .ly probe that isolated one behaviour, the script
    that paired the marks, the driver that ran both engines over one input.
    Vendored into the repository on 2026-08-27 (board item L8) after living
    outside version control; every directory keeps the `lilyport-probe-` prefix
    it was cited under, because standing traps and rulings name these paths in
    prose (trap 28 cites parity16/drift.py, parity18/ydrift.py and
    parity25/residue.py by name).

    Eighteen probe directories plus one loose file and an analysis/ folder:

      lilyport-probe-barnumber/      bar-number placement, text metrics, the
                                     brace-glyph scale probe (has README.txt)
      lilyport-probe-break-align.ly  break-alignment probe (a loose file)
      lilyport-probe-chordgrid/      chord grids
      lilyport-probe-crossstaff/     cross-staff spanners (has README.txt)
      lilyport-probe-glyph-skyline/  ft_decompose.py -- glyph outline
                                     decomposition through FreeType (R9/R20)
      lilyport-probe-jump-mark/      jump marks
      lilyport-probe-ledger/         ledger lines; carries its oracle-out /
                                     port-fixed / port-prefix captures
      lilyport-probe-pango-size/     pango_desc.py drives libpango through
                                     ctypes to measure what
                                     pango_font_description_to_string writes
                                     for a size; size_solver.py predicts the
                                     corpus's font-size values (R10/R12)
      lilyport-probe-parity16..26/   one directory per PARITY wave (16, 17,
                                     18, 19, 21, 22, 23, 24, 25, 26); parity17
                                     and parity26 have README.txt, parity19
                                     has NOTES.txt
      lilyport-probe-volta/          volta / measure-length (has README.txt)
      analysis/                      five standalone scripts: residue
                                     histogram, path clustering prep, property
                                     read/write sweep, OTF metric extraction,
                                     diagnostics attribution

HOW TO RUN
    They are historical instruments, not a suite. Each was written to answer
    ONE question, and several hard-code a path into ~/ClaudeHome or /tmp from
    the session that produced them. READ THE PROBE BEFORE RUNNING IT. The
    common shape, where a probe has a README, is:

        oracle:  cd tools/parity-probes/<probe> && ./run-oracle.sh <probe>.ly [out-dir]
                 (run-oracle.sh applies the corpus's font pinning from
                 tools/regression-harness/reference-fonts.conf.in; without it
                 the two engines measure different typefaces and every number
                 is nonsense)
        port:    dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
                     tools/parity-probes/<probe> <out dir> --files <probe>.ly

    The ctypes probes have their own self-checks and must pass them first,
    e.g. `python3 hb_advance.py selfcheck [font.otf]` in parity17 and
    pango_desc.py's selfcheck in pango-size. They drive SYSTEM libraries
    (libharfbuzz, libpango, FreeType) and the oracle's own font files.

NOTES
    * Nothing here is wired into the build, any test project, or the
      regression harness, and nothing here runs on its own.
    * Two standing rules bind anything run from here: the upstream checkout
      and the oracle are READ-ONLY reference (rule 7); and a probe that
      overrides a property with a default changes what it measures on BOTH
      engines (trap 29) -- use ly:message, not display, because BatchDriver
      routes the output port elsewhere.
    * The .ly probes are the port's own, written to isolate a behaviour; where
      one was derived from a corpus file its header says so. The captured
      outputs (oracle-out, port-fixed, port-prefix, .svg and metric dumps) are
      program output and do not inherit the producing program's licence.
    * The per-probe READMEs were written when the probes lived at
      ~/ClaudeHome/lilyport-probe-*; their "port:" command lines still name
      that location. Substitute tools/parity-probes/<probe>.
    * Not in any solution; nothing here is a project.


================================================================================
tools/abcprobe
================================================================================

WHAT IT IS
    The ABC importer's oracle harness. Two halves, neither of which ships:

      gen-abc-fixtures.py   runs LilyPond's OWN abc2ly (from the pinned oracle
                            installation, so @TOPLEVEL_VERSION@ is substituted
                            into the tagline) over a corpus and records what
                            it produced as JSON fixtures
      AbcProbe/             AbcProbe.csproj (net10.0 console, references
                            src/CodeBrix.LilyPort) -- replays those fixtures
                            through CodeBrix.LilyPort.Importers.AbcImporter and
                            prints the first differing line of anything that
                            does not match
      probes/               .abc files written for this port, for what
                            upstream's own eight regression files do not
                            exercise (the diagnostic paths: unknown mode,
                            repeat-bar warnings, ignored articulations, the
                            "Huh?  Don't understand" report)
      DIVERGENCES.txt       the five places the port's abc2ly deliberately
                            differs from upstream's -- each an upstream defect
                            proved by MEASUREMENT against the oracle (decision
                            D64), with the site, the symptom and what the port
                            does instead

    Fixtures land in tests/CodeBrix.LilyPort.Tests/fixtures/abc/ and are
    replayed as assertions by AbcImporterParityTests. The suite is the gate;
    this tool is for when you are working on the converter and need to see
    WHAT differs.

HOW TO RUN
        PYTHONDONTWRITEBYTECODE=1 python3 tools/abcprobe/gen-abc-fixtures.py [<checkout>] [<oracle-bin>]
        dotnet run --project tools/abcprobe/AbcProbe -c Release
        dotnet run --project tools/abcprobe/AbcProbe -c Release -- --accept

    Generator defaults: ~/GitHome/lilypond for the checkout (upstream's eight
    input/regression/abc2ly files are read from it) and
    ~/ClaudeHome/oracle/lilypond-2.27.2/bin for the oracle. Both are read at
    REGENERATION time only. Regenerate when the pinned LilyPond moves, when a
    probe is added, or when CompatibleWithVersion changes.

NOTES
    * Not in CodeBrix.LilyPort.slnx; built on its own by the dotnet run above.
    * Every corpus file is recorded twice, with and without --beams.
    * The oracle is run with -q, deliberately: abc2ly's --quiet suppresses
      exactly the driver's identification and progress lines and leaves
      warnings and errors alone, which is the split the port's
      ImportResult.Messages makes. An EMPTY line can be a message (the echoed
      remainder of an offending line).
    * A fixture always keeps the ORACLE's own text in `output` (standing rule
      33: nothing is ever recorded from the port). A diverging case ALSO
      carries `port_output`, `port_messages` and `divergences`; AbcProbe
      compares against port_output where present and output otherwise, byte
      for byte either way. `--accept` is the ONLY thing that writes a port
      baseline, and it refuses any case not named in AbcProbe's
      DeclaredDivergences table.
    * The frozen `\version "2.24.0"` line is NOT a divergence: abc2ly freezes
      it on purpose and decision D63 has the port write the same number.
    * The README's count of probes ("ten") predates the four defect-*.abc
      files now in probes/; the directory holds fourteen and the fixture
      directory 44 cases.


================================================================================
tools/midi2lyprobe
================================================================================

WHAT IT IS
    The MIDI importer's oracle harness, the same shape as tools/abcprobe (read
    that README first; the midi2ly README documents only what differs):

      gen-midi2ly-fixtures.py   runs LilyPond's OWN midi2ly over the corpus
                                and records what it produced
      MidiProbe/                MidiProbe.csproj (net10.0 console, references
                                src/CodeBrix.LilyPort) -- replays the fixtures
                                through CodeBrix.LilyPort.Importers.MidiImporter

    THE CORPUS IS THIS REPOSITORY'S OWN ROUND TRIP: the 90 .midi files in
    tools/regression-harness/reference-midi/midi, which the pinned LilyPond
    engraved from the MIDI regression subsuite. A case is therefore
    .ly -> engine -> .midi -> midi2ly -> .ly, on music the port already
    engraves to the byte. All ninety are recorded with the defaults, then nine
    representative files under each of the nine options that change the
    output: 171 cases. Fixtures land in
    tests/CodeBrix.LilyPort.Tests/fixtures/midi2ly/ and are replayed by
    MidiImporterParityTests.

HOW TO RUN
        PYTHONDONTWRITEBYTECODE=1 python3 tools/midi2lyprobe/gen-midi2ly-fixtures.py [<oracle-bin>]
        dotnet run --project tools/midi2lyprobe/MidiProbe -c Release

    The generator needs tools/regression-harness/reference-midi/midi to exist;
    run tools/regression-harness/generate-midi-reference.sh first if it does
    not.

NOTES
    * Not in CodeBrix.LilyPort.slnx; built on its own.
    * The MIDI bytes are EMBEDDED in each fixture (base64, 29 KB across the
      corpus) so a replay does not depend on the state of a regenerated
      directory.
    * The oracle is run WITHOUT -q -- the opposite of abc2ly -- because
      midi2ly's --quiet also suppresses the excess-voices warning, which a
      caller needs. The one progress line ("LY output to `...'...") is dropped
      by name in the generator.
    * Nothing diverges: all 171 cases are byte-identical to upstream,
      including the frozen `\version "2.14.0"` (decision D63). MidiProbe has no
      --accept switch.


================================================================================
tools/musicxml2lyprobe
================================================================================

WHAT IT IS
    The MusicXML importer's oracle harness, again the abcprobe shape:

      gen-musicxml-fixtures.py   runs LilyPond's OWN musicxml2ly over the
                                 corpus and records what it produced
      MusicXmlProbe/             MusicXmlProbe.csproj (net10.0 console,
                                 references src/CodeBrix.LilyPort) -- replays
                                 the fixtures through
                                 CodeBrix.LilyPort.Importers.MusicXmlImporter
      probes/                    MusicXML files written for this port. EMPTY
                                 (a README.txt holds the directory): the
                                 corpus IS a test suite and already reaches 24
                                 distinct diagnostics. Three probes are owed,
                                 named in DIVERGENCES.txt
      DIVERGENCES.txt            candidate divergences found by READING the
                                 python; none is ruled, because decision D64
                                 accepts only measurement as proof

    The corpus is LilyPond's copy of the unofficial MusicXML Test Suite
    (input/regression/musicxml/ at v2.27.2): 165 .xml + 1 .mxl, MIT-licensed
    (Reinhold Kainhofer), converted with the defaults, plus 40 option
    variants -- 206 cases. Fixtures land in
    tests/CodeBrix.LilyPort.Tests/fixtures/musicxml/ as inputs/ (one copy of
    each input, MANIFEST.txt and the corpus's LICENSE) and cases/ (one JSON per
    case naming its input), replayed by MusicXmlImporterParityTests.

HOW TO RUN
        PYTHONDONTWRITEBYTECODE=1 python3 tools/musicxml2lyprobe/gen-musicxml-fixtures.py [<checkout>] [<oracle-bin>]
        dotnet run --project tools/musicxml2lyprobe/MusicXmlProbe -c Release
        dotnet run --project tools/musicxml2lyprobe/MusicXmlProbe -c Release -- --only 33d

    Generator defaults are ~/GitHome/lilypond and
    ~/ClaudeHome/oracle/lilypond-2.27.2/bin; it takes about half a minute and
    rewrites both inputs/ and cases/ from scratch. Regenerate when the pinned
    LilyPond moves, when a probe or variant is added, or when
    CompatibleWithVersion changes.

NOTES
    * Not in CodeBrix.LilyPort.slnx; built on its own.
    * Status recorded in the README (2026-08-26): 206 MATCH of 206, no
      declared divergence, DeclaredDivergences empty. `--accept` exists but
      refuses every case until one is declared.
    * The oracle is run with --loglevel=WARN (musicxml2ly has no -q), which
      drops exactly the driver's progress writes.
    * Unlike abc2ly, musicxml2ly's `\version` line IS the substituted release,
      so the port writes LilyVersion.CompatibleWithVersion and the fixture
      records "2.27.2"; the two agree by construction.
    * One corpus case (71c-ChordsFrets-fretboards) crashes upstream with an
      IndexError; the fixture records the exception line alone in
      `oracle_crash` (a traceback names absolute paths on the generating
      machine) and the port matches it by ending the import with no text.
    * An exception the importer did not turn into a diagnostic is reported as
      a DIFFERS with its first port frame, so one broken case cannot hide the
      state of the other two hundred.


================================================================================
tools/convertlyprobe
================================================================================

WHAT IT IS
    gen-convertly-fixtures.py, the convert-ly fixture generator (no csproj; the
    replay is ConvertLyParityTests in tests/CodeBrix.LilyPort.Tests). The
    oracle is UPSTREAM'S OWN CODE run in place: python/convertrules.py imports
    nothing but the standard library and a gettext marker, so the script
    imports the module from the upstream checkout and calls its rules
    directly, reproducing convert-ly.py's driver semantics (which rules run,
    what version is written back) in the script itself.

    Every rule is exercised: each corpus file is converted from 1.2.3 (before
    the first rule) to the newest version any rule targets, so all 326 rules
    run over real LilyPond text; each file is ALSO converted from the version
    it declares, which is what a user does. The corpus is a 150-file sample of
    the regression suite (files up to 8 KB). Output goes to
    tests/CodeBrix.LilyPort.Tests/fixtures/convertly/ (150 .convertly.json
    files plus manifest.json).

HOW TO RUN
        PYTHONDONTWRITEBYTECODE=1 python3 tools/convertlyprobe/gen-convertly-fixtures.py [<lilypond-checkout>]

    Default checkout: ~/GitHome/lilypond, read-only, read at regeneration time
    only (standing rule 3 in the script's own words). Python 3, standard
    library only.

NOTES
    * Not a project; nothing here is built or run by the solution.
    * Some of upstream's own patterns never finish: rule 2.15.18's nested
      alternations backtrack catastrophically on real input and python's `re`
      has no timeout. The generator gives the oracle 20 seconds per file; a
      file it cannot finish is SKIPPED AND NAMED in manifest.json, never
      silently dropped, and the port's answer for such a file is its own match
      timeout rather than a hang.
    * The __pycache__/ beside the script is a python artefact, not content.


================================================================================
tools/convertrules-port
================================================================================

WHAT IT IS
    gen-convert-rules.py: converts LilyPond's python/convertrules.py (5,706
    lines, 326 rules) into the C# the ConvertLy component runs, at
    src/CodeBrix.LilyPort/ConvertLy/ConvertRules.g.cs. The rules ARE their
    regular expressions -- 282 of them are one or more re.sub calls -- so the
    generator carries every pattern and replacement VERBATIM: it reads the
    python STRING VALUE with ast.literal_eval and writes that same value as a
    C# literal, and PythonRegex translates python's spelling to .NET's at run
    time. No regex is ever re-authored.

HOW TO RUN
        PYTHONDONTWRITEBYTECODE=1 python3 tools/convertrules-port/gen-convert-rules.py [<lilypond-checkout>]

    Default checkout: ~/GitHome/lilypond, read-only. Python 3, standard
    library only. Run by hand when the reference moves; the generated file is
    committed source.

NOTES
    * Not a project; no build step runs it.
    * WHAT IT REFUSES TO DO: anything it cannot translate with certainty is
      listed in its report as HAND, and a hand-written counterpart is expected
      in ConvertRules.Manual.cs. The generated table names every rule in
      version order either way, so a missing hand port is a compile error
      rather than a silently absent rule.
    * Its output is graded by tools/convertlyprobe's fixtures.


================================================================================
tools/mutopia-probe
================================================================================

WHAT IT IS
    Opens every entry point of a locally downloaded Mutopia corpus with the
    port, produces a PDF and a MIDI from each, and grades them against the PDF
    and MIDI Mutopia published for the same source. One `dotnet run`, one row
    of results.tsv per entry point. It exists to produce the table an
    OBSERVATIONS document is written from. Built and calibrated 2026-08-27.

      MutopiaProbe/   MutopiaProbe.csproj (net10.0 console). References
                      src/CodeBrix.LilyPort and src/CodeBrix.LilyPort.Engine
                      in-repo, PLUS the CodeBrix.PdfDocCreate.Html2Pdf and
                      CodeBrix.PdfRasterizer packages -- TOOL dependencies
                      under decision D52(a), which CodeBrix.LilyPort itself
                      must never take.
      summarize.py    tallies a results.tsv and lists its worst rows
                      (standard library only)

    READ THIS FIRST: IT IS NOT A FIDELITY ORACLE. Mutopia's references were
    produced by the LilyPond named in each piece's own \version (2.4 to 2.19
    across the corpus) and Ghostscript; the port is 2.27.2 and first runs the
    sources through its own convert-ly. A difference is one of (a) a port
    defect, (b) a convert-ly gap, (c) version drift upstream 2.27.2 would also
    show, or (d) inconclusive -- and the tool cannot tell which. Sorting rows
    into those is the OBSERVATIONS document's job. The regression harness is
    the fidelity oracle.

HOW TO RUN
        cd tools/mutopia-probe/MutopiaProbe
        dotnet run -c Release -- ~/ClaudeHome/Mutopia/pieces ~/ClaudeHome/mutopia-probe-<date> \
            [--files key1,key2,...] [--limit N] [--resume] [--retry-hung] \
            [--timeout-seconds N] [--dpi N] [--no-ink] \
            [--oracle [PATH]] [--oracle-timeout-seconds N] [--regrade RUN_DIR]
        python3 ../summarize.py ~/ClaudeHome/mutopia-probe-<date>/results.tsv [--by declared_version]

    --oracle is what turns the tool from a Mutopia comparison into a FIDELITY
    one: it engraves each entry point with the pinned 2.27.2 binary as well
    (default ~/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond, with its own
    fonts) and adds the o_* columns, which grade the port against upstream on
    the SAME converted source.  Those are the columns a PORT-GAP verdict is
    cut on; without them the caveat below is the whole story.  Give it
    --oracle-timeout-seconds 1800 for a full run -- the Mendelssohn Octet full
    score needs ~395 s of oracle time on its own.
    --regrade RUN_DIR re-runs only the GRADING over an existing run's output,
    which is how a threshold change is measured without re-engraving.

    The corpus root and the output directory are both command-line arguments
    and both must be OUTSIDE the repository: the corpus is a local download
    (100 pieces mirroring the server layout, with an ENTRY_POINTS.tsv) that is
    never to be copied in, and the output tree holds copies of it.

    Host requirement: pdftotext (Debian: poppler-utils) for the text grade;
    without it the text column reads TEXT-UNAVAILABLE and everything else
    still runs.

NOTES
    * Not in CodeBrix.LilyPort.slnx and in no solution; built on its own.
    * One entry point goes through: CONVERT (the port's DocumentConverter over
      every .ly/.ily of the piece, once per piece), ENGRAVE (BatchRunner from
      an emptied scratch directory; the RAW source is tried too if the
      converted one produced no page), PDF (the SVG pages through Html2Pdf
      with vector placement and the engine's embedded text faces), GRADE THE
      PDF (page count, page size, text, ink -- both PDFs rasterised through
      PDFium and compared as an 8-column ink-density grid, the number the
      verdict is cut on), GRADE THE MIDI (compare-midi.py's vocabulary and
      normalisations, plus note-level and channel-level verdicts because a
      2.10-era reference differs from a 2.27 engine at event 0 of every
      track).
    * The engine starts once (~20 s) and every entry point runs in that
      session. results.tsv is appended and flushed per row, so a killed run
      loses nothing and --resume continues after the last row. A file the
      engine never returns from leaves a STARTED marker; the next --resume
      records it as HUNG (cancellation is honoured only at the runner's own
      boundaries) and --retry-hung tries it again.
    * The PDF route is COPIED from Fresco.Brix's export (see the
      `//was previously:` lines under Pdf/); this tool must not reference the
      Fresco.Brix folder.
    * The README's CALIBRATION section records, per grade, why the first
      run's numbers were wrong and the measurement that fixed each rule; read
      it before changing a threshold.


================================================================================
tools/Lily.Docs
================================================================================

WHAT IT IS
    Generates the port's nineteen documentation files (by running the vendored
    ly/generate-documentation.ly through the engine, exactly as DocsDriver
    does) and renders MANUALS from them -- print-shaped HTML and PDF -- through
    the published CodeBrix.Texinfo2Html and CodeBrix.Texinfo2Pdf packages,
    with every @lilypond snippet engraved by the port's OWN engine. Nine
    manuals (decision D48): internals (the Internals Reference, the one
    generated file that is a complete standalone manual), notation (corpus
    prose whose eighteen appendices are the other generated files), learning,
    usage, extending, essay, changes, music-glossary and contributor. Phase 5
    (LilyDocs) of the port.

    A REPO TOOL in its own solution (decision D52): CodeBrix.LilyPort must NOT
    acquire the Texinfo -> Html2Pdf -> MarkupParse/StyleSheetParse/font-package
    chain, so Lily.Docs.slnx is separate from CodeBrix.LilyPort.slnx and no
    packable project references anything here.

      Lily.Docs.slnx               the tool, its tests, and the five engine
                                   projects (listed so a Release build IS one)
      src/Lily.Docs/               Lily.Docs.csproj (net10.0 console;
                                   references src/CodeBrix.LilyPort and
                                   src/CodeBrix.LilyPort.Engine; packages
                                   Texinfo2Html + Texinfo2Pdf, pinned at ONE
                                   version and moved together)
      tests/Lily.Docs.Tests/       the render gates (xunit.v3, VSTest dialect)
      assets/en/                   three GFDL macro files, byte-identical to
                                   Documentation/en/ and fenced as such
      assets/bib/                  five bibliographies translated ONCE by the
                                   BibTeX oracle plus lily-bib.bst (never
                                   executed); hash-fenced (decision D57)
      assets/staged/               ROADMAP and code-review-checklist.md, which
                                   the Contributor's Guide prints verbatim;
                                   hash-fenced (decision D57)
      expected-warnings/           the frozen baselines: <manual>.tsv (warning
                                   category -> count), <manual>-pdf.tsv (pages,
                                   PDF warnings, page size, SVG placement, one
                                   DROP row per dropped code point),
                                   <manual>-snippets.tsv (asked / engraved /
                                   pictures / failed / declined)
      svg-dialect/                 the SVG dialect the engine emits for
                                   snippets, measured over the notation
                                   manual's 2,557 pictures and FROZEN:
                                   inventory.tsv plus reference copies of the
                                   scanner and its gate. The specification a
                                   downstream renderer implements against
      composed-reference/          what the ORACLE's lilypond-book composed
                                   for 28 option cases (probe.itely,
                                   cases.tsv, one .ly per case) -- the fence
                                   that the snippet composer matches
                                   lilypond-book

HOW TO RUN
        cd tools/Lily.Docs
        dotnet run --project src/Lily.Docs -c Release -- internals --html --pdf \
            -o /tmp/lilydocs-out

        Lily.Docs MANUAL [--html] [--pdf] [-o DIR] [--generated DIR] [--baseline]
                  [--warnings] [--no-snippets]

        --html / --pdf     which formats; with neither, both (ONE render
                           feeding both outputs, not two)
        -o DIR             output directory (default ./lilydocs-out)
        --generated DIR    reuse the nineteen files a previous run wrote
                           (~40 s saved); point it at the `en` directory
                           INSIDE the output, not its parent
        --warnings         print every warning message, not just the counts
        --baseline         freeze the expected-warnings baselines from this run
        --no-snippets      the CONTROL run: no engraver, seconds not minutes

        dotnet test Lily.Docs.slnx -c Release

    Generation is about forty seconds warm; the notation manual's render is
    another five minutes or so (two and a half thousand engravings, sequential
    because the engine is process-global). The same capability is reachable
    from Lily.Shell's `docs` command without building this tool separately.

NOTES
    * Not in CodeBrix.LilyPort.slnx; has its own Lily.Docs.slnx.
    * Fully managed: since the 2026-08-26 package bump the Texinfo2Pdf ->
      Html2Pdf chain places the engraved SVG as PDF VECTOR content through
      CodeBrix.Imaging.Drawing.NoSkia, with no native library. Lily.Docs owns
      no rasterizer and hands the same SVG to both outputs.
    * Every snippet that fails to engrave has its COMPOSED SOURCE written to
      <output>/failed-snippets/, because the engine is handed the composed
      text in memory and a failure message names a line in a file that was
      never on disk.
    * Generation happens ONCE PER PROCESS. The second call returns in a tenth
      of a second, writes nothing, reports all nineteen files missing, and
      does NOT throw. The test suite generates once for the whole assembly
      (GeneratedDocumentation.EnsureGenerated) and runs serially for the same
      reason -- the engine is process-global and both generation and
      engraving change the working directory.
    * A baseline is FROZEN FROM A MEASURED RUN THAT WAS THEN READ and asserted
      exactly, in both directions. Re-freeze with --baseline only for a
      deliberate change, read the diff, and say in the commit what moved.
    * The ENGRAVED SVGs ARE NOT SHAREABLE: they are derived from the GFDL
      corpus mirror and this is a GPL-3 repository. A renderer's own
      repository must not take them as test fixtures; svg-dialect/README.txt
      is the deliverable instead, stated in words and numbers so a renderer
      author can write synthetic SVGs. Verification against the real
      pictures happens HERE, in Lily.Docs' gates.
    * Two include search paths are built in RenderPaths and conflating them
      costs engravings rather than errors: the Texinfo renderer's (generated
      directory first, then its parent, the corpus, assets) and the ENGINE's
      snippet include path (upstream's LILYPOND_BOOK_INCLUDE_DIRS). Without
      the second the notation manual lost 76 engravings, reported as syntax
      errors and not as missing files.
    * version.itexi is generated at render time from
      LilyPortInfo.CompatibleWithVersion, never vendored.
    * The README's closing PACKAGE PINS table names an older version than
      src/Lily.Docs/Lily.Docs.csproj carries; the csproj is the authority.


================================================================================
tools/table-extract
================================================================================

WHAT IT IS
    Three Python scripts that recover Internals Reference metadata that exists
    upstream only at C++ compile time, as committed TSV tables the engine
    embeds:

      cxxscan.py                        just enough C++ scanning to read
                                        LilyPond's registration macros: raw
                                        string literals with non-empty
                                        delimiters, adjacent literals,
                                        parenthesised argument expressions.
                                        Deliberately not a C++ parser.
      extract-translator-descriptions.py  every translator's ADD_TRANSLATOR
                                        text blocks (description, grobs
                                        created, properties read/written) and
                                        its listener declarations (events
                                        accepted) -- what
                                        ly:translator-description answers
      extract-entry-point-docs.py       every LY_DEFINE (and the setter and
                                        callback macro forms) docstring and
                                        argument list, stringified the way the
                                        C preprocessor would -- what
                                        ly:get-all-function-documentation
                                        answers

    Their outputs are the committed tables under
    src/CodeBrix.LilyPort.Engine/Scheme/ (translator-descriptions.tsv,
    entry-point-docs.tsv, beside grob-interfaces.tsv, which the same mechanism
    already recovered).

HOW TO RUN
        python3 tools/table-extract/extract-translator-descriptions.py LILYPOND_SRC OUT_TSV
        python3 tools/table-extract/extract-entry-point-docs.py LILYPOND_SRC OUT_TSV

    LILYPOND_SRC is the pinned read-only reference checkout. Python 3,
    standard library only; cxxscan.py is imported by the other two and must
    sit beside them.

NOTES
    * Not a project. Nothing in the build reads the checkout; the scripts are
      run by hand when the reference moves and their output is committed.
    * The docs run in tools/regression-harness (G8) is what grades these
      tables: the port's generated internals.texi must match the oracle's byte
      for byte, and five entry points once printed "delim(" and ")delim"
      because the scanner read only the empty raw-literal delimiter.
    * The __pycache__/ beside the scripts is a python artefact.


================================================================================
tools/parser-baseline
================================================================================

WHAT IT IS
    THE ONE-TIME FIDELITY ANCHOR for the parser: what real GNU Bison 3.8.2
    makes of parser-mirror/parser.yy at the pinned v2.27.2, generated
    2026-08-03 and COMMITTED as data, exactly as tools/font-build commits the
    outputs of the one-time font build.

      parser.output         Bison's full report: grammar, symbol tables, all
                            913 automaton states with their actions
      productions.tsv       the 617 numbered productions, in Bison's numbering
      automaton-facts.tsv   the figures the in-repo generator must reproduce

    The port does not vendor Bison's generated parser.cc (decision O7 rejected
    that); src/CodeBrix.LilyPort.Parsing reads parser.yy and constructs the
    LALR(1) tables itself, and this baseline is what its automaton is diffed
    against. It is also ground truth for the grammar reader, which it
    corrected immediately: seventeen `{ action } %prec X` rules had been read
    as mid-rule actions. The verified figures (616 productions, 130 terminals,
    204 nonterminals, 15 mid-rule actions, 39 empty productions) are asserted
    in tests/CodeBrix.LilyPort.Parsing.Tests/GrammarInventory.cs.

HOW TO RUN
    Only on a deliberate upstream re-sync of parser-mirror/parser.yy. Not
    needed to build, test or consume the package.

        cd tools/parser-baseline
        bison --report=all --report-file=parser.output \
              -o /tmp/parser.cc ../../parser-mirror/parser.yy
        # then regenerate productions.tsv and automaton-facts.tsv from it, and
        # update the figures in GrammarInventory.cs to match.

NOTES
    * Requires GNU Bison (3.8.2 was used). Zero shift/reduce and zero
      reduce/reduce conflicts at the pin. If Bison reports ANY conflict where
      this baseline records none, stop: upstream has changed what the parser
      accepts.
    * Not a project; nothing downstream needs Bison, and neither does a
      re-sync.


================================================================================
tools/Lily.Shell
================================================================================

WHAT IT IS
    The interactive shell for the port: a CodeBrix.Platform application whose
    window is a terminal, hosting the LilyPond engine IN PROCESS -- parse a
    file, engrave it, talk to the Scheme layer, convert or import, render a
    manual, without a twenty-second engine start-up between each. Decision D14
    settled that there is no public `lilypond`-style console CLI; this is the
    user-facing surface instead. A REPO TOOL: nothing here ships, and no
    packaging step reaches this directory.

      Lily.Shell.slnx                   its own solution (generated by
                                        CodeBrix Develop); with global.json
                                        beside it selecting the
                                        Microsoft.Testing.Platform test runner
      src/Lily.Shell.UI/                the shared XAML (a .shproj): App +
                                        MainPage
      src/Lily.Shell.Core/              LilyPortHost, the commands, the view
                                        model, window chrome. References all
                                        four engine projects (the facade packs
                                        them PrivateAssets="all", so an in-repo
                                        consumer must name each) and
                                        tools/Lily.Docs for the `docs`
                                        command. Copies assets/fonts/otf to
                                        <appdir>/fonts/otf, where the engine's
                                        font layer probes
      src/Lily.Shell.<head>/            one Program.cs per platform head, each
                                        with EXACTLY ONE platform-runtime
                                        package:
                                          Lily.Shell.LinuxX11          net10.0
                                          Lily.Shell.LinuxWayland      net10.0
                                          Lily.Shell.LinuxFrameBuffer  net10.0
                                          Lily.Shell.MacOS             net10.0
                                          Lily.Shell.Win32Skia         net10.0
                                          Lily.Shell.WinWpfSkia   net10.0-windows
      src/libs/Lily.Shell.Kernel/       VT input tokenizer, line editor,
                                        command registry, sub-interpreter
                                        stack, ShellSession. No UI, no engine
      src/libs/Lily.Shell.TerminalView/ an EMPTY PASS-THROUGH project: the
                                        terminal control graduated into the
                                        platform family as the
                                        CodeBrix.Platform.TerminalView add-in,
                                        and this csproj only flows that
                                        package to the app and the tests
      tests/Lily.Shell.Core.Tests/      the `docs` command surface and the
                                        once-per-process generation contract
      tests/libs/Lily.Shell.Kernel.Tests/, tests/libs/Lily.Shell.TerminalView.Tests/

    Commands: help, clear, version, usage, parse, engrave, demo, include,
    scheme (the LilyScheme REPL), docs (renders one of the nine manuals to
    HTML and PDF, default output /tmp/lily-shell-docs/), convert-ly, import
    (abc | midi | musicxml, switches named after each upstream script's long
    options), exit.

HOW TO RUN
        cd tools/Lily.Shell
        dotnet run --project src/Lily.Shell.LinuxX11 -c Release

    Substitute the head for the platform in use. Tests:

        dotnet test --solution Lily.Shell.slnx -c Release      # 86 tests

NOTES
    * Not in CodeBrix.LilyPort.slnx; has its own Lily.Shell.slnx.
    * THE ENGINE LOADS IN THE BACKGROUND AND THE FIRST LOAD IS THE SLOW ONE:
      about 20 s warm, roughly 67 s the first time after a build (JIT). The
      window title is the progress bar. Commands that need the engine wait
      for it and say so; convert-ly and import do not need it.
    * THIS SOLUTION IS THE Microsoft.Testing.Platform DIALECT of xunit.v3: on
      the .NET 10 SDK a plain `dotnet test` is refused, `--solution X.slnx` /
      `--project x.csproj` is the syntax, and `--logger trx` is ignored. The
      main solution and tools/Lily.Docs are the OTHER dialect (VSTest). Both
      live here on purpose; do not "fix" one into the other.
    * `docs` here renders; it does not freeze -- there is no --baseline, and a
      test asserts there is not. Generation is cached for the session because
      it works once per process (see tools/Lily.Docs). The eight corpus
      manuals need the repository (the mirror is found by walking up to
      CodeBrix.LilyPort.slnx); `internals` does not.
    * Lily.Shell carries the Texinfo -> Html2Pdf chain that decision D52
      refuses for the package. Since the 2026-08-26 bump that chain is fully
      managed (its SVG engine is CodeBrix.Imaging.Drawing.NoSkia); the
      SkiaSharp the desktop heads carry comes from CodeBrix.Platform's own
      runtime, as in every Platform application. The README's "Texinfo ->
      Html2Pdf -> SkiaSharp chain" paragraph predates that bump; the comment
      in src/Lily.Shell.Core/Lily.Shell.Core.csproj is current.
    * Neither convert-ly nor import rewrites a file in place; converted text
      is printed unless -o says where to put it, and warnings print first.
    * MIDI playback is out of scope for LilyPort entirely (decision D27).
    * X11 automation traps (from the README, learned the hard way): `xdotool
      search --name "Lily.Shell"` matches other windows (the dot is regex-any)
      and the window carries no _NET_WM_PID; match the TITLE anchored and
      dot-escaped (^Lily\.Shell) plus uniqueness, and re-check the window
      name before every keystroke.
    * TestResults/ under this directory is a test-run artefact, excluded by
      the root .gitignore.


================================================================================
tools/font-build
================================================================================

WHAT IT IS
    Everything needed to rebuild the Emmentaler music fonts from the Metafont
    sources in mf/ on a clean Debian-based Linux machine, without a LilyPond
    source tree and without autoconf. A ONE-TIME build whose outputs are
    committed under assets/fonts/; it is NOT part of `dotnet build`, and
    nobody needs the toolchain to build or use the package -- only to
    regenerate the fonts when the port moves to a new LilyPond.

      build-fonts.sh     the standalone driver, replacing the font-producing
                         subset of mf/GNUmakefile: (1) mf2pt1.mem, (2) 57
                         Metafont runs to Type 1 (.mf -> .pfb + .tfm + .log),
                         (3) mf-to-table.py turns the log annotations into
                         Scheme metadata (.lisp, .global-lisp), (4) FontForge
                         merges six subfonts per design size into
                         emmentaler-<size>.otf and attaches the LILC (zlib)
                         and LILY (raw) tables, (5) the brace font
      compare-fonts.sh   verify a build against an official release
      compare_fonts.py   the structural comparison: glyph names, advance
                         widths, bounding boxes, and the LILC/LILY tables
                         decompressed and compared exactly
      mf2pt1.pl          VENDORED from lilypond/scripts/build -- Scott Pakin,
                         LPPL-1.3c-or-later, NOT LilyPond code and NOT GPL;
                         do not edit in place (LPPL requires renaming)
      mf-to-table.py     VENDORED from lilypond/scripts/build -- LilyPond's,
                         GPL-3.0-or-later
      out/               the working build (generated; gitignored)

HOW TO RUN
    Install first (Debian / Ubuntu / LMDE / Mint):

        sudo apt install texlive-binaries texlive-base texlive-metapost \
                         fontforge-nox python3-fontforge

    python3-fontforge is version-locked to the system python3 and is not
    pip-installable; installing the fontforge binary alone is NOT enough. Also
    perl and python3. Verify all of `mf --version`, `mpost --version`,
    `fontforge --version` and `python3 -c "import fontforge"` before
    building; the script checks them itself.

        cd tools/font-build
        ./build-fonts.sh [/path/to/output/dir]         # default ./out
        LILYPOND_VERSION=2.27.3 ./build-fonts.sh       # stamp another version

        ./compare-fonts.sh ./out /path/to/reference/otf

    Several minutes; stage 2 dominates and is cached (a .pfb newer than its
    .mf is not rebuilt). Delete the output directory to force a full rebuild.
    The reference set for the comparison is extracted from the official
    lilypond-2.27.2-linux-x86_64 tarball (the README gives the tar command).

NOTES
    * TOOLCHAIN VERSIONS ARE LOAD-BEARING. Different FontForge or Metafont
      versions produce different glyph OUTLINES (a different number of curve
      segments between the same endpoints). LILC, LILY, glyph inventory and
      advance widths are byte-identical to the official 2.27.2 fonts; bounding
      boxes differ on ~18% of glyphs by 1-3 units of a 1000-unit em; and the
      one code path that reads the OUTLINE -- skyline computation -- decides
      eleven regression-corpus rows. Ruling R19: the port builds its own
      fonts permanently; do not re-propose shipping the release binaries.
    * If you rebuild on a different toolchain, IN THE SAME SESSION re-run the
      harness's font-parity pair and rewrite font-delta-ledger.tsv with a
      reasoned record, re-run generate-glyph-identity.py (the committed index
      is built from the port's own fonts), and re-run GlyphOutlineSkylineTests.
    * The build is BYTE-REPRODUCIBLE (verified across two independent runs)
      because build-fonts.sh sets SOURCE_DATE_EPOCH to the commit timestamp of
      the LilyPond tag the sources came from, and sets USER/LOGNAME to the
      font's actual designers so FontForge's SVG metadata does not credit
      whoever ran the build. Re-derive SOURCE_DATE_EPOCH on a re-sync.
    * Do not use cmp or diff on the fonts; build metadata and CFF charstring
      optimisation are not bit-stable across FontForge releases.
    * Licensing: everything under mf/ is dual-licensed (GPL-3.0-or-later with
      the LilyPond Font Exception, or SIL OFL 1.1) and both options are
      conveyed onward. "Emmentaler" and "Feta" are OFL Reserved Font Names;
      that binds MODIFIED fonts only, so editing any .mf file changes what the
      output may be called. Record any such decision in
      THIRD-PARTY-NOTICES.txt section 3.
    * tools/font-build/README.txt is listed among the Solution Items of
      CodeBrix.LilyPort.slnx.


================================================================================
Documentation/
================================================================================

WHAT IT IS
    A BYTE-IDENTICAL PARTIAL MIRROR of LilyPond's documentation SOURCES at the
    pinned v2.27.2 -- 690 files, 5.20 MB, every sha256 recorded in
    MANIFEST.sha256. It is the INPUT tools/Lily.Docs renders the manuals
    from: the nine manuals' @include closure and everything they name.

      en/            91 files: the nine manuals' Texinfo closure (notation,
                     learning, usage, extending, essay, changes,
                     music-glossary, contributor, snippets) plus en/included/
                     (25 .ly files named by @lilypondfile)
      snippets/      533 files: @lilypondfile targets -- PUBLIC DOMAIN, carved
                     out of both the GPL and the FDL by upstream's own
                     exception list
      ly-examples/   18 image targets of the @image macros
      bib/           5 bibliography sources (the translated .itexi files live
                     in tools/Lily.Docs/assets/bib)
      pictures/      43 files, all reached through the @sourceimage macro or
                     from inside a music snippet; the remaining 242 upstream
                     pictures belong to excluded manuals and are deliberately
                     not here

    Licence: GNU Free Documentation License (COPYING.FDL at the repository
    root; THIRD-PARTY-NOTICES.txt section 4), EXCEPT snippets/, which is
    public domain (section 5). Copyright (C) 1996--2026 the LilyPond authors.

HOW TO RUN
    Nothing runs here. tools/Lily.Docs reads it; the render gates ALWAYS run
    because the inputs are in the repository (a gate that skipped would now
    be a defect). The mirror does NOT contain the port's nineteen generated
    files, which are build products here as upstream, and a test asserts
    that.

NOTES
    * NEVER EDIT ANYTHING IN THIS DIRECTORY. Same rule as parser-mirror/,
      book-mirror/ and mf/: an edited input silently changes what a manual
      MEANS, and every warning baseline under tools/Lily.Docs/expected-
      warnings is frozen against these exact bytes.
    * THE FDL TREE IS KEPT CLEARLY SEPARATE FROM GPL SOURCE. FDL-licensed text
      must never be copied into source files or XML doc comments under src/,
      and the SVGs Lily.Docs engraves from this corpus are derived from GFDL
      material in a GPL-3 repository: they are never committed here and must
      never become test fixtures elsewhere (tools/Lily.Docs/svg-dialect/
      README.txt states the rule and offers the written dialect specification
      in their place).
    * THE ORACLE DOES NOT READ THIS DIRECTORY. texi2any oracle runs are
      pointed at the upstream checkout directly, so a copy defect shows up AS
      a difference rather than hiding inside an agreement. Oracle runs are
      not build or test steps.
    * Re-syncing: recompute the closure against the new checkout, copy the
      file set over, regenerate MANIFEST.sha256 and diff it (that diff IS the
      list of documentation changes), re-render every manual, and review each
      baseline movement deliberately.
    * Two known non-resolving includes are upstream BUILD PRODUCTS, not
      defects: essay's pictures/pdf/* twins (the .pdf variants are excluded
      by design) and learning's pictures/context-example.png (upstream ships
      only the .eps, which the notation manual reaches through \epsfile
      inside a snippet, where the ENGINE reads it).


================================================================================
book-mirror/
================================================================================

WHAT IT IS
    A BYTE-IDENTICAL MIRROR of LilyPond's lilypond-book sources at v2.27.2 --
    FOUR files, with their sha256 recorded in README.txt:
    lilypond-book.py (scripts/), book_snippets.py, book_base.py and
    book_texinfo.py (python/). This is the FAITHFULNESS AUTHORITY for the
    snippet-engraving seam in tools/Lily.Docs -- the code that composes the
    source text for each @lilypond snippet the way lilypond-book composes it
    (decision D49(c)). Ported logic carries a `//was previously:
    python/book_snippets.py` provenance note. GPL-3.0-or-later, Copyright (C)
    1998--2026 Han-Wen Nienhuys and Jan Nieuwenhuizen; THIRD-PARTY-NOTICES.txt
    section 1.

HOW TO RUN
    THESE FILES ARE NOT EXECUTED. Nothing in the repository runs them and no
    build or test step reads them; the port has no Python runtime dependency.
    BookMirrorTests fences all four against their sha256s so an edit fails a
    test instead of quietly becoming the authority.

NOTES
    * NEVER EDIT ANYTHING IN THIS DIRECTORY.
    * THE CLEAN-ROOM BOUNDARY: Phase 5 treats texi2any and lilypond-book as
      GPL ORACLES -- run them, read their OUTPUT, never their source.
      lilypond-book is the ONE NAMED EXCEPTION. texi2any's source must NEVER
      be mirrored here or read; Texinfo rendering is done by the published
      CodeBrix.Texinfo packages (decision D28). Adding anything else here
      means re-opening the boundary first.
    * Decision D49(c) originally named TWO files; the authority turned out to
      be four, because compose_ly's defaults come from book_base.py and
      book_texinfo.py. The README's re-sync step 1 still says "the two files
      here"; copy all four.
    * The lilypond-book BINARY from the oracle installation is what produced
      tools/Lily.Docs/composed-reference/; that is an oracle run against its
      output, which the rule permits.


================================================================================
parser-mirror/
================================================================================

WHAT IT IS
    A BYTE-IDENTICAL MIRROR of LilyPond's grammar and lexer sources at
    v2.27.2: parser.yy (4,935 lines) and lexer.ll (1,354 lines), sha256s in
    README.txt. GPL-3.0-or-later, Copyright (C) 1997--2026 Han-Wen Nienhuys
    and Jan Nieuwenhuizen; THIRD-PARTY-NOTICES.txt section 1.

    The port does not translate these by hand; it READS them. A repo-owned
    tool (src/CodeBrix.LilyPort.Parsing) reads parser.yy -- token
    declarations, 36 precedence declarations, the rules, each action body as
    opaque text keyed by rule identity -- and constructs the LALR(1) tables
    itself (decision O7, option c2: vendor the SOURCE, generate the tables
    in-repo, so no external toolchain is ever needed again, not even on
    re-sync). The rule ACTIONS are hand-ported C#, and a fence test asserts
    every rule the reader found is either implemented or on a recorded
    not-yet list. The lexer is hand-ported as a modal scanner; lexer.ll stays
    mirrored so each sync's delta is a mechanical diff.

HOW TO RUN
    Nothing runs here. The one-time fidelity anchor produced from parser.yy
    with real Bison lives in tools/parser-baseline.

NOTES
    * NEVER EDIT ANYTHING IN THIS DIRECTORY (same rule as mf/; decision D10).
    * Re-sync: copy the new lily/parser.yy and lily/lexer.ll over, diff
      against the previous mirror, run the in-repo generator (it hard-fails
      on any Bison feature it does not support), hand-port exactly the action
      bodies it names as changed, and let the rule fence and the regression
      suite catch the rest.
    * parser-mirror/src/CodeBrix.LilyPort.Parsing and parser-mirror/tests/
      CodeBrix.LilyPort.Parsing.Tests are a LEFTOVER SCAFFOLD from the day the
      parsing project was first created (2026-08-03): a first-draft csproj, an
      InternalsVisibleTo.cs, an empty Grammar/ folder and stale bin/obj
      output, plus a bare test csproj. No solution or project references
      them and nothing reads them. The live projects are
      src/CodeBrix.LilyPort.Parsing and tests/CodeBrix.LilyPort.Parsing.Tests
      at the repository root; treat the copies under parser-mirror/ as
      clutter awaiting a maintainer's decision, not as content.
    * parser-mirror/README.txt is listed among the Solution Items of
      CodeBrix.LilyPort.slnx.


================================================================================
mf/
================================================================================

WHAT IT IS
    A BYTE-IDENTICAL MIRROR of lilypond/mf at v2.27.2: 115 files -- 103 .mf
    Metafont sources (the Feta and Parmesan glyph programs at every design
    size, the braces, the autometric and parameter macros), the three
    fontconfig .conf files, emmentaler_codes/features/kerning.py and the two
    FontForge generator scripts, invoke-mf2pt1.sh, mf2pt1.mp (Scott Pakin,
    LPPL), GNUmakefile, and upstream's own README.md (which documents the
    Metafont conventions, glyph-name rules, the log format mf-to-table.py
    parses, and how the LILY and LILC tables are built). These are the INPUT
    tools/font-build turns into the fonts under assets/fonts/.

HOW TO RUN
    Nothing runs here directly; tools/font-build/build-fonts.sh drives it.

NOTES
    * NEVER EDIT ANYTHING IN THIS DIRECTORY. Beyond the mirror rule, editing
      a .mf file makes the output a MODIFIED font, which may not carry the
      OFL Reserved Font Names "Emmentaler" and "Feta" -- see the licensing
      notes under tools/font-build and assets/.
    * Dual-licensed: GPL-3.0-or-later with the LilyPond Font Exception, or
      SIL OFL 1.1 (LICENSE.OFL at the repository root); THIRD-PARTY-NOTICES.txt
      section 3. mf2pt1.mp is LPPL-1.3c-or-later and not LilyPond's.
    * Re-sync: `rm -rf mf/ && cp -a <lilypond>/mf mf/` then `diff -r` to
      confirm byte identity; check whether STAFF_SIZES or BRACES changed in
      mf/GNUmakefile and update build-fonts.sh to match.


================================================================================
assets/
================================================================================

WHAT IT IS
    The fonts the package ships and renders with. assets/ holds only fonts/.

      fonts/otf/    the nine Emmentaler OpenType fonts (emmentaler-11, -13,
                    -14, -16, -18, -20, -23, -26 and -brace; 892,500 bytes)
                    -- the port's OWN BUILD from mf/ by tools/font-build,
                    committed deliberately as BUILD OUTPUTS, version stamp
                    2.27.2, byte-reproducible. Design sizes are staff sizes
                    in points; the engraver picks the nearest. Glyphs are
                    addressed BY NAME through the CFF charset (the post table
                    is format 3.0 and carries no names), and the engraver
                    takes glyph dimensions from the custom LILC table (zlib-
                    compressed) and global metadata from LILY (stored raw),
                    not from the outlines.
      fonts/svg/    the same nine faces as SVG 1.1 fonts (2,690,421 bytes),
                    emitted by the same FontForge pass -- what the SVG backend
                    draws glyph outlines from. They carry NO LILC/LILY data.
      fonts/text/   the 24 text faces LilyPond 2.27.2 ships and measures text
                    with -- six families x four styles: URW C059 (serif),
                    Nimbus Sans (sans), Nimbus Mono PS (typewriter) and their
                    TeX Gyre fallbacks Schola, Heros, Cursor. UNLIKE the
                    music fonts these are PREBUILT BINARIES vendored byte for
                    byte from the official 2.27.2 distribution (decision D13),
                    because parity needs metrics byte-identical to the oracle
                    that produced the regression references. Their sha256
                    manifest is in text/README.txt; licences travel with them
                    in text/licenses/ (URW: AGPL-3.0 with a font-embedding
                    exception; TeX Gyre: GUST Font License).

HOW TO RUN
    Nothing runs here. To regenerate the music fonts, see tools/font-build;
    to re-verify them against an official release:

        cd tools/font-build
        ./compare-fonts.sh ../../assets/fonts/otf /path/to/reference/otf

NOTES
    * Do not replace the music fonts with files copied out of a LilyPond
      release (ruling R19). LilyPond's own binaries exist in the repository
      only as test fixtures under tests/fixtures/lilypond-fonts, and a test
      asserts they are not in the package.
    * THE TEXT FONTS ARE ASSETS FOREVER -- NEVER EDIT. Subsetting, converting
      or editing any of them creates a DERIVED font: the GUST rename request
      wakes up and AGPL source obligations attach. A face that must change
      gets a new name and a new decision.
    * The port's text chain STOPS at the TeX Gyre face (decision D23): there
      is no system-font fallback, and a code point no vendored face covers
      renders missing-glyph tofu by design. A family name none of the 24
      faces provides resolves to TeX Gyre Schola (ruling R14).
    * Music fonts: dual-licensed GPL-3.0-or-later with the LilyPond Font
      Exception, or SIL OFL 1.1; the Reserved Font Names bind modified fonts
      only, and these are built from unmodified sources. Full record in
      THIRD-PARTY-NOTICES.txt (sections 3 and 10).
    * assets/fonts/README.txt is listed among the Solution Items of
      CodeBrix.LilyPort.slnx.


================================================================================
tests/fixtures/ and other test data
================================================================================

WHAT IT IS
    Test material that is not a test project. Three sets, in three places:

    tests/fixtures/lilypond-fonts/
        LILYPOND'S OWN EMMENTALER BINARIES -- the nine .otf files as the
        official 2.27.2 distribution built them, with SHA256SUMS.txt. The port
        does not engrave with them. They exist so the regression corpus can
        be run BOTH ways: with LilyPond's fonts (the GATE -- any divergence
        from the reference is the ENGINE) and with the port's own fonts (any
        remaining divergence is the FONT BUILD, priced in
        tools/regression-harness/font-delta-ledger.tsv). Substituted at run
        time with `BatchDriver ... --fonts tests/fixtures/lilypond-fonts`; no
        rebuild needed. THEY MUST NEVER SHIP IN THE PACKAGE (ruling R19),
        which PackagedFontTests fences by opening the built .nupkg. The
        matching .svg fonts are DELIBERATELY NOT HERE -- substituting them
        breaks the comparator's named-glyph identity (121 of 2,316 MATCH
        instead of 2,304, measured). Same dual licence as the shipped
        music fonts; unmodified copies keep the Reserved Font Name.

    tests/regression/
        LilyPond's regression corpus, vendored verbatim from input/regression
        at v2.27.2: 2,146 .ly inputs, 29 .ily includes (without them 113
        inputs fail on "cannot find file"), the image and text files a few
        markup tests reference (lilypond.eps, lilypond.png,
        lilypond-uppercase-ext.PNG, verbatim.txt), and midi/ -- the 73-file
        MIDI subsuite. GPL-3.0-or-later; THIRD-PARTY-NOTICES.txt section 1.
        This is the suite tools/regression-harness grades; the harness is
        meaningless without it.

    tests/CodeBrix.LilyPort.Tests/fixtures/
        The importer and converter fixture sets, each GENERATED by a tool in
        this file and replayed by a parity test in the same project:
          abc/         44 fixture files from tools/abcprobe
                       (AbcImporterParityTests)
          midi2ly/     171 cases from tools/midi2lyprobe
                       (MidiImporterParityTests)
          musicxml/    inputs/ (166 MIT-licensed MusicXML Test Suite files
                       with LICENSE and MANIFEST.txt) and cases/ (206 JSON
                       cases) from tools/musicxml2lyprobe
                       (MusicXmlImporterParityTests)
          convertly/   150 cases plus manifest.json from tools/convertlyprobe
                       (ConvertLyParityTests)
        Every fixture records the ORACLE's output, never the port's (standing
        rule 33); a deliberately diverging case carries a reviewed port
        baseline beside it. Regenerate through the generating tool, never by
        hand.

HOW TO RUN
    The fixture sets are consumed by `dotnet test CodeBrix.LilyPort.slnx`
    (see MAINTAINER-README.txt) and by the tools named above.

NOTES
    * The test projects themselves (tests/CodeBrix.LilyPort*.Tests, five of
      them, all in the main solution) are not extras; they are the
      executable specification of the package and are described in
      MAINTAINER-README.txt.
    * input/regression/musicxml/ is NOT under tests/regression; the port
      keeps that MIT-licensed corpus only under the MusicXML fixtures, with
      its licence beside it.


================================================================================
box.eps
================================================================================

WHAT IT IS
    A 205-byte EPS file (a stroked box with a red "LL" in Helvetica) at the
    repository root. It is not an asset and nothing reads it from there: it
    is the SIDE-FILE that tests/regression/markup-eps.ly WRITES -- the test
    opens "box.eps" for output, generates exactly this content, and then
    embeds it with \epsfile. Before BatchDriver gave every input its own
    scratch working directory, sweeps launched from the repository root
    landed this file here; tools/regression-harness/README.txt records it as
    one of the two side-files the whole suite produces ("markup-eps
    SIDE-FILE box.eps 205 bytes").

HOW TO RUN
    Nothing. It is the residue of an earlier sweep.

NOTES
    * A fresh sweep no longer writes it to the repository: each input now
      runs from an emptied per-file directory under the system temp folder,
      and anything it wrote is reported on a SIDE-FILE line and left there.
    * The file is harmless and regenerable; whether to keep it in the tree is
      a maintainer's call, not a consumer's concern.

================================================================================
END
================================================================================
