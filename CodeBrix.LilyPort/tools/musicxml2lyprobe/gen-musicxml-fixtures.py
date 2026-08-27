#!/usr/bin/env python3
"""CodeBrix.LilyPort repo tool (ships nothing): records what LilyPond's own
musicxml2ly does to the MusicXML test suite, as the fixtures
MusicXmlImporterParityTests replays.

THE ORACLE IS UPSTREAM'S OWN SCRIPT, RUN HERE -- the installed musicxml2ly from
the pinned LilyPond 2.27.2, so that @TOPLEVEL_VERSION@ is substituted the way a
user's copy substitutes it. Nothing is ever recorded from the port's own output
(rule 33).

⚠ UNLIKE abc2ly, musicxml2ly's `\\version' line IS the substituted release: it
calls printer.dump_version(lilypond_version) with "@TOPLEVEL_VERSION@" resolved.
So D63's frozen-version case does NOT apply here and rule 16 does: the port
writes LilyVersion.CompatibleWithVersion, the fixture records "2.27.2", and the
two agree by construction. Regenerate if either ever moves.

WHY --loglevel=WARN. musicxml2ly has no -q; its log levels come from lilylib,
where PROGRESS is the default and WARN is one step below it. Dropping to WARN
suppresses exactly the identification and progress writes that belong to the
command-line driver ("Reading MusicXML from ...", "Output to `...'") and leaves
the warnings and errors behind -- which is precisely the split the port's
ImportResult.Messages makes. What --loglevel=WARN leaves on stderr IS the
expected message list, with no filtering to argue about.

THE CORPUS IS VENDORED, NOT INLINED. musicxml2ly's inputs are 1.5 MB of XML; a
fixture per case carrying its own copy would be several times that for no gain.
The suite's inputs/ directory holds ONE copy of each file (MIT, Reinhold
Kainhofer -- LICENSE is copied in beside them, which is all that licence asks),
and a case names the input it replays. Option variants therefore cost only their
own output.

Reads the READ-ONLY LilyPond checkout and the pinned oracle (standing rule 7);
writes tests/CodeBrix.LilyPort.Tests/fixtures/musicxml/.

Usage: python3 gen-musicxml-fixtures.py [<lilypond-checkout>] [<oracle-bin-dir>]
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile

CHECKOUT = os.path.expanduser(
    sys.argv[1] if len(sys.argv) > 1 else '~/GitHome/lilypond')
ORACLE_BIN = os.path.expanduser(
    sys.argv[2] if len(sys.argv) > 2
    else '~/ClaudeHome/oracle/lilypond-2.27.2/bin')
MUSICXML2LY = os.path.join(ORACLE_BIN, 'musicxml2ly')

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(
    HERE, '..', '..', 'tests', 'CodeBrix.LilyPort.Tests', 'fixtures', 'musicxml'))
INPUTS = os.path.join(OUT, 'inputs')
CASES = os.path.join(OUT, 'cases')

CORPUS = os.path.join(CHECKOUT, 'input', 'regression', 'musicxml')
PROBES = os.path.join(HERE, 'probes')

# A run that will not finish is named, never silently dropped (board trap 23).
ORACLE_TIMEOUT_SECONDS = 60

# The option variants, as (suffix, argv, [files it is recorded over]). Upstream's
# corpus is a MusicXML test suite and exercises the converter's INPUT surface; it
# says nothing about the option surface, every file of it being converted with the
# defaults. These are what reach the other twenty-two options, each over files
# whose content the option actually bites on -- a variant recorded over a file it
# cannot change is a fixture that proves nothing.
VARIANTS = (
    ('-absolute', ['-a'],
     ['01a-Pitches-Pitches', '23f-Tuplets-DurationButNoBracket',
      '32a-Notations']),
    ('-language-deutsch', ['-l', 'deutsch'],
     ['01a-Pitches-Pitches', '01d-Pitches-Microtones']),
    ('-language-catalan', ['-l', 'catalan'],
     ['01a-Pitches-Pitches']),
    ('-no-beaming', ['--nb'],
     ['33b-Spanners-Tie', '32a-Notations', '03a-Rhythm-Durations']),
    ('-no-stem-directions', ['--nsd'],
     ['32a-Notations', '24a-GraceNotes']),
    ('-no-rest-positions', ['--nrp'],
     ['02b-Rests-PitchedRests']),
    ('-no-articulation-directions', ['--nd'],
     ['32a-Notations', '32b-Articulations-Texts']),
    ('-no-page-layout', ['--npl'],
     ['52a-PageLayout', '52b-Breaks']),
    ('-no-system-breaks', ['--nsb'],
     ['52b-Breaks']),
    ('-no-page-breaks', ['--npb'],
     ['52b-Breaks']),
    ('-no-page-margins', ['--npm'],
     ['52a-PageLayout']),
    ('-midi', ['-m'],
     ['01a-Pitches-Pitches']),
    ('-book', ['--book'],
     ['01a-Pitches-Pitches']),
    ('-no-tagline', ['--nt'],
     ['01a-Pitches-Pitches', '52a-PageLayout']),
    ('-fretboards', ['--fb'],
     ['71c-ChordsFrets', '71d-ChordsFrets-Multistaff']),
    ('-string-numbers-false', ['--sn', 'f'],
     ['71e-TabStaves']),
    ('-tab-clef-moderntab', ['--tc', 'moderntab'],
     ['71e-TabStaves']),
    ('-transpose-d', ['--transpose', 'd'],
     ['01a-Pitches-Pitches', '13a-KeySignatures']),
    ('-shift-durations-1', ['--sd', '1'],
     ['03a-Rhythm-Durations']),
    ('-shift-durations-minus1', ['--sd', '-1'],
     ['03a-Rhythm-Durations']),
    ('-dynamics-scale-2.5', ['--ds', '2.5'],
     ['31a-Directions']),
    ('-dynamics-scale-0', ['--ds', '0'],
     ['31a-Directions']),
    ('-dynamics-scale-negative', ['--ds', '-1'],
     ['31a-Directions']),
    ('-absolute-font-sizes', ['--afs'],
     ['31a-Directions', '51b-Header-Quotes']),
    ('-credit-page-2', ['--cp', '2'],
     ['51a-Header-Credits']),
    ('-ottavas-end-early', ['--oe', 't'],
     ['33da-Spanners-OctaveShifts-before',
      '33db-Spanners-OctaveShifts-after']),
    ('-ottavas-end-late', ['--oe', 'f'],
     ['33db-Spanners-OctaveShifts-after']),
)


def run_oracle(local_name, source_path, extra, workdir):
    """Run the pinned musicxml2ly over one file and give back (text, stderr).

    The input is COPIED into the working directory and named by its basename
    before the oracle sees it, because musicxml2ly prints the path it was handed
    into the output itself ("% automatically converted by musicxml2ly from
    FILE"). A fixture carrying this machine's absolute path would be a recording
    of this machine; carrying the basename, it is a recording of the file, and
    the port reproduces it by being told the same name through
    MusicXmlImportOptions.SourceName.
    """
    local = os.path.join(workdir, local_name)
    shutil.copyfile(source_path, local)
    out = os.path.join(workdir, 'out.ly')
    if os.path.exists(out):
        os.remove(out)
    try:
        done = subprocess.run(
            [MUSICXML2LY, '--loglevel=WARN', '-o', out] + extra + [local_name],
            capture_output=True, timeout=ORACLE_TIMEOUT_SECONDS, cwd=workdir)
    except subprocess.TimeoutExpired:
        return None, None
    err = done.stderr.decode('utf-8')
    if not os.path.exists(out):
        return None, err
    with open(out, encoding='utf-8') as f:
        return f.read(), err


def crash_summary(messages):
    """Recognise a python traceback and reduce it to the exception it ended on.

    ⚠ A TRACEBACK IS A RECORDING OF THIS MACHINE, not of the converter: every
    frame names an absolute path under the oracle installation. Where upstream
    dies of an uncaught exception the fixture therefore keeps the exception line
    alone -- which is the whole of what the defect actually says -- and marks the
    case so the port is not asked to reproduce a stack it does not have.

    A library cannot exit the process, so a crash here is a D64 candidate by
    construction: the port either fixes the defect and declares the divergence,
    or ends the import with no text the way `sys.exit` left no file on disk.
    """
    start = None
    for i, line in enumerate(messages):
        if line.startswith('Traceback (most recent'):
            start = i
            break
    if start is None:
        return None, messages
    #⚠ Warnings written BEFORE the crash are real messages and are kept; only
    #the frames and the exception line are cut away.
    for i in range(len(messages) - 1, start, -1):
        line = messages[i]
        if line and not line.startswith(' '):
            return line, messages[:start]
    return None, messages


def split_stderr(err):
    """Cut a captured stderr stream into messages the way the port cuts it.

    Every lilylib message ends in its own newline, so only the empty left by the
    stream's own terminating newline is dropped -- exactly as the abc harness
    does it, and for the same reason: an empty line upstream actually wrote is a
    message, and dropping every empty entry here would make the fixture disagree
    with a faithful port.
    """
    if not err:
        return []
    parts = err.split('\n')
    return parts[:-1] if err.endswith('\n') else parts


def find_version_line(text):
    """Read back the \\version line, so a fence can assert rule 16 on it."""
    for line in text.split('\n'):
        if line.startswith('\\version "'):
            return line
    return None


def record(case, input_name, source, extra, workdir, options_note):
    text, err = run_oracle(input_name, os.path.join(INPUTS, input_name),
                           extra, workdir)
    if text is None and err is None:
        print('SKIPPED (oracle did not finish): %s' % case)
        return False
    messages = split_stderr(err)
    crash, messages = crash_summary(messages)
    payload = {
        'name': case,
        'source': source,
        'input_file': input_name,
        'source_name': input_name,
        'arguments': extra,
        'options': options_note,
        'output': text,
        'version_line': find_version_line(text) if text is not None else None,
        'messages': messages,
        'oracle_crash': crash,
    }
    with open(os.path.join(CASES, case + '.mxml.json'), 'w',
              encoding='utf-8') as f:
        json.dump(payload, f, ensure_ascii=False, indent=1, sort_keys=True)
    print('%-52s %s' % (case,
                        ('CRASHED: ' + crash) if crash
                        else 'no output' if text is None
                        else '%d bytes, %d message(s)'
                        % (len(text), len(messages))))
    return True


def vendor_inputs():
    """Copy the corpus in once, with the provenance rule 7 asks for."""
    names = []
    for entry in sorted(os.listdir(CORPUS)):
        if entry.endswith('.xml') or entry.endswith('.mxl'):
            shutil.copyfile(os.path.join(CORPUS, entry),
                            os.path.join(INPUTS, entry))
            names.append(entry)
    shutil.copyfile(os.path.join(CORPUS, 'LICENSE'),
                    os.path.join(INPUTS, 'LICENSE'))
    probes = []
    if os.path.isdir(PROBES):
        for entry in sorted(os.listdir(PROBES)):
            if entry.endswith('.xml') or entry.endswith('.mxl'):
                shutil.copyfile(os.path.join(PROBES, entry),
                                os.path.join(INPUTS, entry))
                probes.append(entry)
    with open(os.path.join(INPUTS, 'MANIFEST.txt'), 'w',
              encoding='utf-8') as f:
        f.write('These files are INPUTS to the musicxml2ly parity suite.\n\n')
        f.write('%d of them are LilyPond\'s copy of the unofficial MusicXML\n'
                'Test Suite, input/regression/musicxml/ at v2.27.2, copied\n'
                'verbatim. That suite is MIT-licensed (Copyright (c) 2008--2026\n'
                'Reinhold Kainhofer); LICENSE beside this file is its own, kept\n'
                'intact, which is the whole of what that licence asks.\n\n'
                % len(names))
        f.write('%d are probes written for this port, for what the suite does\n'
                'not exercise; they carry no upstream provenance.\n\n'
                % len(probes))
        for n in names:
            f.write('upstream  %s\n' % n)
        for n in probes:
            f.write('probe     %s\n' % n)
    return names, probes


def main():
    if not os.path.exists(MUSICXML2LY):
        sys.exit('no oracle musicxml2ly at %s' % MUSICXML2LY)
    for d in (INPUTS, CASES):
        os.makedirs(d, exist_ok=True)
    for stale in os.listdir(CASES):
        if stale.endswith('.mxml.json'):
            os.remove(os.path.join(CASES, stale))
    for stale in os.listdir(INPUTS):
        os.remove(os.path.join(INPUTS, stale))

    names, probes = vendor_inputs()
    recorded = 0
    with tempfile.TemporaryDirectory() as workdir:
        for entry in names + probes:
            base = os.path.splitext(entry)[0]
            source = 'probe' if entry in probes else 'upstream'
            if record(base, entry, source, [], workdir, 'defaults'):
                recorded += 1

        by_base = {os.path.splitext(e)[0]: e for e in names + probes}
        for suffix, extra, files in VARIANTS:
            for base in files:
                entry = by_base.get(base)
                if entry is None:
                    print('MISSING corpus file for variant: %s%s'
                          % (base, suffix))
                    continue
                if record(base + suffix, entry, 'variant', extra, workdir,
                          ' '.join(extra)):
                    recorded += 1

    print('\n%d case(s) recorded over %d input(s).'
          % (recorded, len(names) + len(probes)))


if __name__ == '__main__':
    main()
