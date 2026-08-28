================================================================================
CodeBrix.LilyPort -- tools/musicxml2lyprobe/
================================================================================

THE MUSICXML IMPORTER'S ORACLE HARNESS. Three parts, and none of them ships
anything:

    gen-musicxml-fixtures.py   runs LilyPond's OWN musicxml2ly over a corpus and
                               records what it produced, as JSON fixtures
    probes/                    MusicXML files written for this port, for what the
                               corpus does not exercise (EMPTY so far -- the
                               corpus IS a test suite and covers the input
                               surface; see THE CORPORA below)
    MusicXmlProbe/             replays those fixtures through the port's
                               CodeBrix.LilyPort.Importers.MusicXmlImporter and
                               PRINTS the first differing line of anything that
                               does not match

The fixtures land in tests/CodeBrix.LilyPort.Tests/fixtures/musicxml/ and are
also replayed as assertions by MusicXmlImporterParityTests. Use the suite for a
gate and this tool when you are actually working on the converter -- a failing
assertion tells you that something differs, and this tells you what.

This is the same shape as tools/abcprobe/, deliberately: read that README first
if you have not, because the reasoning behind the oracle, the message split and
the divergence discipline is written out there and only summarised here.

--------------------------------------------------------------------------------
STATUS (2026-08-26, evening): GREEN
--------------------------------------------------------------------------------

    206 MATCH / 0 ACCEPTED / 0 DIFFERS / 0 SKIPPED of 206, in half a second.

THE FIXTURES ARE COMPLETE -- 206 cases over 166 inputs, all recorded from the
pinned oracle -- and so is the port. Every case reproduces upstream BYTE FOR BYTE,
including the one where upstream crashes and writes nothing at all. There is no
declared divergence: MusicXmlProbe's DeclaredDivergences table is empty and no
fixture carries a frozen `port_output'.

Running it:

    dotnet run --project tools/musicxml2lyprobe/MusicXmlProbe -c Release
    dotnet run --project tools/musicxml2lyprobe/MusicXmlProbe -c Release -- \
        --only 33d          just the cases whose name contains that

⚠ AN EXCEPTION THE IMPORTER DID NOT TURN INTO A DIAGNOSTIC IS A PORT DEFECT, and
the probe reports it as a DIFFERS with its first port frame rather than dying. One
broken case must not be allowed to hide the state of the other two hundred; that
is worth knowing while porting, and it is why the tool is written this way.

Regenerating the fixtures is safe and cheap at any time and does NOT depend on
the port -- that is the point of an oracle.

--------------------------------------------------------------------------------
THE ORACLE
--------------------------------------------------------------------------------

The INSTALLED musicxml2ly from the pinned oracle, ~/ClaudeHome/oracle/lilypond-
2.27.2, rather than the one in the read-only checkout: the installed copy has had
@TOPLEVEL_VERSION@ substituted, and that string is written into every document's
\version line and its tagline. A checkout copy answers "(unknown version)" and
would record a fixture no user could ever reproduce.

Nothing is EVER recorded from the port's own output. That is standing rule 33,
and it is the whole value of the corpus.

⚠ THE \version LINE IS NOT LIKE abc2ly's. abc2ly freezes "2.24.0" under a comment
saying it deliberately does not substitute its own release, and D63 ruled that
the port writes that same frozen number because it is not the ported release.
musicxml2ly is the OTHER case: it calls dump_version(lilypond_version) with
@TOPLEVEL_VERSION@ resolved, so the line names the ported release and rule 16
governs it -- the port writes LilyVersion.CompatibleWithVersion and the fixture
records "2.27.2". The two agree by construction; regenerate if either moves.

--------------------------------------------------------------------------------
WHY --loglevel=WARN
--------------------------------------------------------------------------------

musicxml2ly has no -q. Its log levels come from lilylib, where PROGRESS is the
default and WARN is one step below it. Dropping to WARN suppresses exactly the
identification and progress writes that belong to the command-line driver --
"Reading MusicXML from ...", "Output to `...'" -- and leaves the warnings and
errors alone. That split is precisely the one the port's ImportResult.Messages
makes, because the progress half is the driver's and the port has no driver. So
what --loglevel=WARN leaves on stderr IS the expected message list, with no
filtering to argue about.

MEASURED over the whole corpus: 36 messages across 206 cases, 24 distinct shapes.
Every one of them is a `musicxml2ly: warning: ...' line.

--------------------------------------------------------------------------------
THE CORPUS IS VENDORED, NOT INLINED
--------------------------------------------------------------------------------

musicxml2ly's inputs are 1.5 MB of XML; a fixture per case carrying its own copy
would be several times that for no gain, and the option variants would each pay
for it again. So:

    fixtures/musicxml/inputs/    ONE copy of each input file, plus MANIFEST.txt
                                 and the corpus's own LICENSE
    fixtures/musicxml/cases/     one JSON file per case, naming the input it
                                 replays and holding the oracle's output and
                                 messages

The inputs are LilyPond's copy of the unofficial MusicXML Test Suite
(input/regression/musicxml/ at v2.27.2), which is MIT-licensed -- Copyright (c)
2008--2026 Reinhold Kainhofer. LICENSE is copied in beside them, which is the
whole of what that licence asks, and MIT into a GPL-3.0-only package is clean
under standing rule 17. They are TEST material: the tests project is not packed.

--------------------------------------------------------------------------------
THE CORPORA
--------------------------------------------------------------------------------

    input/regression/musicxml/   165 .xml + 1 .mxl. This is not a corpus of
                                 convenience: it IS a MusicXML test suite,
                                 organised by feature (pitches, rests, rhythm,
                                 tuplets, notations, directions, spanners, parts,
                                 layout, chord names, fretboards, percussion),
                                 and it is the corpus upstream itself uses to
                                 show what musicxml2ly does.

    variants                     40 further cases: the same inputs converted
                                 again with one option each.

⚠ THE BOARD SAID "215 FILES" AND THAT IS THE DIRECTORY COUNT, not the corpus.
input/regression/musicxml/ holds 215 entries: 165 .xml, 1 .mxl, 31 .itexi (the
manual text that documents each group), 15 .lybook, a GNUmakefile, the LICENSE
and one .png. What musicxml2ly converts is the 166. Corrected on the board.

Upstream's corpus exercises the INPUT surface and says nothing about the option
surface: every file of it is converted with the defaults. The 40 variants are
what reach the other twenty-two options, each recorded over files whose content
the option actually bites on -- a variant recorded over a file it cannot change
is a fixture that proves nothing. They are declared in VARIANTS at the top of
gen-musicxml-fixtures.py, with the files each one covers.

probes/ is empty. Unlike abc2ly -- whose eight upstream files convert without a
single warning, leaving every diagnostic path untested until D59's ten probes
were written -- this corpus already reaches 24 distinct diagnostics. Add probes
here when a path is found that the 206 cases do not reach, and say in this
README what it reaches.

--------------------------------------------------------------------------------
WHEN THE ORACLE CRASHES
--------------------------------------------------------------------------------

One case in the corpus makes upstream die of an uncaught exception:

    71c-ChordsFrets-fretboards      IndexError: list index out of range

and the fixture records that in `oracle_crash' rather than in `messages'.

⚠ A TRACEBACK IS A RECORDING OF THIS MACHINE, not of the converter: every frame
names an absolute path inside the oracle installation. So the generator keeps the
exception line alone -- which is the whole of what the defect actually says --
and marks the case, so that the port is never asked to reproduce a stack it does
not have. Warnings written BEFORE the crash are real messages and are kept.

That crash is a D64 candidate, and the port DOES THE SECOND OF THE TWO THINGS a
library can do about it: it ends the import with no text, the way an uncaught
exception left no file on disk, and writes nothing to ImportResult.Messages
because a traceback carries none of the converter's own diagnostic shape. So the
case MATCHES. Fixing the defect instead is still open and still not ruled -- see
DIVERGENCES.txt, which records it and three other candidates found by reading,
and says plainly that none is ruled.

--------------------------------------------------------------------------------
RUNNING IT
--------------------------------------------------------------------------------

    PYTHONDONTWRITEBYTECODE=1 python3 gen-musicxml-fixtures.py [<checkout>] [<oracle-bin>]

Defaults are ~/GitHome/lilypond and ~/ClaudeHome/oracle/lilypond-2.27.2/bin. The
generator reads the read-only checkout and the pinned oracle at REGENERATION time
only; nothing in a build or test step touches either (standing rule 7). It takes
about half a minute and rewrites both inputs/ and cases/ from scratch.

Regenerate when the pinned LilyPond moves, when a probe or a variant is added, or
when CompatibleWithVersion changes -- the \version line and the tagline both
carry that constant, so a fixture recorded against a different oracle release
will differ on those lines too.
