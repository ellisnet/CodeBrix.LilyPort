================================================================================
CodeBrix.LilyPort -- tools/abcprobe/
================================================================================

THE ABC IMPORTER'S ORACLE HARNESS. Two halves, and neither ships anything:

    gen-abc-fixtures.py    runs LilyPond's OWN abc2ly over a corpus and records
                           what it produced, as JSON fixtures
    AbcProbe/              replays those fixtures through the port's
                           CodeBrix.LilyPort.Importers.AbcImporter and PRINTS
                           the first differing line of anything that does not
                           match
    probes/                fourteen .abc files written for this port, for what
                           upstream's own eight do not exercise

The fixtures land in tests/CodeBrix.LilyPort.Tests/fixtures/abc/ and are also
replayed as assertions by AbcImporterParityTests. Use the suite for a gate and
this tool when you are actually working on the converter -- a failing assertion
tells you that something differs, and this tells you what.

--------------------------------------------------------------------------------
THE ORACLE
--------------------------------------------------------------------------------

The INSTALLED abc2ly from the pinned oracle, ~/ClaudeHome/oracle/lilypond-2.27.2,
rather than the one in the read-only checkout: the installed copy has had
@TOPLEVEL_VERSION@ substituted, and that string is written into every document's
tagline. A checkout copy answers "(unknown version)" and would record a fixture
no user could ever reproduce.

Nothing is EVER recorded from the port's own output. That is standing rule 33,
and it is the whole value of the corpus.

--------------------------------------------------------------------------------
WHY -q
--------------------------------------------------------------------------------

The oracle is run with -q, and that is not for tidiness. abc2ly's --quiet
suppresses exactly its identification and progress writes -- "abc2ly from
LilyPond 2.27.2", "Parsing `...'", the "Line ..." counter and "LilyPond output
to: ..." -- and leaves its warnings and errors alone. That split is precisely
the one the port's ImportResult.Messages makes, because the progress half is the
command-line driver's and the port has no driver. So what -q leaves on stderr IS
the expected message list, with no filtering to argue about.

⚠ AN EMPTY LINE IS A MESSAGE. abc2ly's "Huh?  Don't understand" report ends by
echoing the remainder of the offending line, which still carries its own
newline, so the script writes a blank line after it. The port cuts messages on
the newlines the script writes and therefore reports that blank. Only the empty
left by the stream's own terminating newline is dropped here.

--------------------------------------------------------------------------------
WHERE THE PORT DELIBERATELY DIFFERS
--------------------------------------------------------------------------------

D64 (2026-08-26) amends the faithfulness rule for the text converters: where
upstream is broken, the port FIXES it and says so at the site. abc2ly has five
such defects, all in its output path, and three of them make LilyPond refuse the
file outright while a fourth produces no file at all.

DIVERGENCES.txt is the record -- five entries, each with the upstream site, the
MEASUREMENT that proved it a defect, what the port does instead, and which cases
show it. Read it before adding a sixth.

How the gate stays strict while diverging:

  * A fixture always keeps the ORACLE's own text in `output' (standing rule 33),
    so the before and the after both live in the file.
  * A diverging case ALSO carries `port_output', `port_messages' and
    `divergences' -- a frozen, reviewed baseline and the reason ids for it.
  * AbcProbe compares against `port_output' where it exists and `output'
    otherwise. Either way it is strict byte equality, so an UNINTENDED change to
    a diverging case fails like any other.
  * `AbcProbe --accept' is the only thing that writes a port baseline, and it
    REFUSES any case not named in its DeclaredDivergences table. A fix that
    quietly changed a case nobody reasoned about cannot be baselined away.
  * AbcImporterParityTests asserts baseline and reason go together, and that
    every reason id is one of the five.

⚠ AND THE \version LINE IS NOT ONE OF THEM. abc2ly writes a frozen
`\version "2.24.0"' under a comment saying it deliberately does not substitute
its own release, and D63 ruled that the port writes the same frozen number: it is
not the ported release, so rule 16 never governed it. That line is byte-identical.

--------------------------------------------------------------------------------
THE CORPORA
--------------------------------------------------------------------------------

    input/regression/abc2ly/    upstream's own eight: chords, clefs, grace,
                                kirchentonarten, lyrics, repeats, tempo,
                                tuplet-slur
    probes/                     voice-overlay, multi-voice-stave,
                                broken-rhythm, decorations, header-fields,
                                guitar-chords, unknown-key, bar-warnings,
                                chord-brokenrhythm, escapes-and-comments,
                                defect-escape-k, defect-history,
                                defect-lyric-underscore, defect-open-repeat

The probes are D59's list. The eight upstream files convert without a single
warning, so every diagnostic path in the converter would be untested without
them; the probes reach the unknown-mode warning, the four repeat-bar warnings,
the two ignored-articulation warnings, the two ignored-broken-rhythm-in-chord
warnings, and the "Huh?  Don't understand" report with its echoed line.

Each file is recorded twice, with and without --beams: 44 cases.

--------------------------------------------------------------------------------
RUNNING IT
--------------------------------------------------------------------------------

    PYTHONDONTWRITEBYTECODE=1 python3 gen-abc-fixtures.py [<checkout>] [<oracle-bin>]
    dotnet run --project tools/abcprobe/AbcProbe -c Release

Defaults are ~/GitHome/lilypond and ~/ClaudeHome/oracle/lilypond-2.27.2/bin. The
generator reads the read-only checkout and the pinned oracle at REGENERATION time
only; nothing in a build or test step touches either (standing rule 7).

Regenerate when the pinned LilyPond moves, when a probe is added, or when
CompatibleWithVersion changes -- the tagline carries that constant, so a fixture
recorded against a different oracle release will differ on that line too.
