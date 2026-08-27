#!/usr/bin/env python3
"""CodeBrix.LilyPort repo tool (ships nothing): records what LilyPond's own midi2ly
does to a corpus of MIDI files, as the fixtures MidiImporterParityTests replays.

THE ORACLE IS UPSTREAM'S OWN SCRIPT, RUN HERE -- the installed midi2ly from the
pinned LilyPond 2.27.2. Nothing is ever recorded from the port's own output
(rule 33).

THE CORPUS IS THE PORT'S OWN ROUND TRIP. tools/regression-harness/reference-midi
holds the ninety .midi files the pinned LilyPond engraved from this repo's
regression suite, so a case here is `.ly' -> engine -> `.midi' -> midi2ly -> `.ly'
-- a wider corpus than the seventy-five files upstream ships for midi2ly, and one
made of music this port already engraves. The bytes are embedded in each fixture so
that a replay does not depend on a corpus that is regenerated.

Every file is recorded with the defaults. A representative handful is recorded
again under each option that changes the output, so the option surface is covered
without ninety copies of it.

STDERR. midi2ly is run WITHOUT -q, because --quiet suppresses the excess-voices
warning along with the progress line, and the warning is material the port reports.
The ONE progress line ("LY output to `...'...") is dropped here, and named, because
it is the driver's own and the port has no driver.

THE ONE DECLARED DIVERGENCE. midi2ly writes a FROZEN `\\version "2.14.0"' line. D58
rules that every \\version an importer emits reads the port's compatible-with
constant instead, so the fixture stores that line as a placeholder token and the
parity test fills it in; the raw line is kept beside it.

Reads the READ-ONLY pinned oracle and this repo's own reference MIDI (standing rule
7); writes tests/CodeBrix.LilyPort.Tests/fixtures/midi2ly/.

Usage: python3 gen-midi2ly-fixtures.py [<oracle-bin-dir>]
"""
import base64
import json
import os
import subprocess
import sys
import tempfile

ORACLE_BIN = os.path.expanduser(
    sys.argv[1] if len(sys.argv) > 1
    else '~/ClaudeHome/oracle/lilypond-2.27.2/bin')
MIDI2LY = os.path.join(ORACLE_BIN, 'midi2ly')

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.normpath(os.path.join(
    HERE, '..', 'regression-harness', 'reference-midi', 'midi'))
OUT = os.path.normpath(os.path.join(
    HERE, '..', '..', 'tests', 'CodeBrix.LilyPort.Tests', 'fixtures', 'midi2ly'))

PROGRESS_PREFIX = 'LY output to '

# The option variants, as (suffix, argv, the port's option settings).
VARIANTS = (
    ('', [], {}),
    ('-absolute', ['-a'], {'absolutePitches': True}),
    ('-explicit', ['-e'], {'explicitDurations': True}),
    ('-skip', ['-S'], {'skip': True}),
    ('-textlyrics', ['-x'], {'textLyrics': True}),
    ('-preview', ['-p'], {'preview': True}),
    ('-key', ['-k', '-2:1'], {'key': '-2:1'}),
    ('-durationquant', ['-d', '32'], {'durationQuant': 32}),
    ('-startquant', ['-s', '32'], {'startQuant': 32}),
    ('-tuplets', ['-t', '4*2/3', '-t', '2*4/3'],
     {'allowTuplet': ['4*2/3', '2*4/3']}),
)

# The files the option variants are recorded over: lyrics, several voices, a
# fractional tempo, key and time signatures, overlapping notes, quantisation and a
# metronome-derived beat structure.
VARIANT_FILES = (
    'lyrics-addlyrics',
    'voice-5',
    'tempo-accel-granularity',
    'key-option',
    'midi-polymeter',
    'midi-overlapping-notes',
    'quantize-duration',
    'quantize-start',
    'beat-structure-by-metronome-12-8',
)

ORACLE_TIMEOUT_SECONDS = 60


def run_oracle(path, extra):
    """Run the pinned midi2ly over one file and give back (text, stderr).

    The input is COPIED in under its basename because midi2ly writes the path it
    was handed into the first line of the document it produces; a fixture carrying
    this machine's absolute path would be a recording of this machine.
    """
    with tempfile.TemporaryDirectory() as tmp:
        local = os.path.basename(path)
        with open(path, 'rb') as src, open(os.path.join(tmp, local), 'wb') as dst:
            dst.write(src.read())
        out = os.path.join(tmp, 'out.ly')
        try:
            done = subprocess.run(
                [MIDI2LY, '-o', out] + extra + [local],
                capture_output=True, timeout=ORACLE_TIMEOUT_SECONDS, cwd=tmp)
        except subprocess.TimeoutExpired:
            return None, None
        err = done.stderr.decode('utf-8')
        if not os.path.exists(out):
            return None, err
        with open(out, encoding='utf-8') as f:
            return f.read(), err


def split_stderr(err):
    """Cut a captured stderr stream into the messages the port reports.

    An empty line IS a message (the port cuts on the newlines the script writes),
    so only the empty left by the stream's own terminating newline is dropped. The
    progress line is the driver's and is dropped by name.
    """
    if not err:
        return []
    parts = err.split('\n')
    if err.endswith('\n'):
        parts = parts[:-1]
    return [ln for ln in parts if not ln.startswith(PROGRESS_PREFIX)]


def find_version_line(text):
    """Read back the frozen \version line, so a fence can assert on it."""
    for line in text.split('\n'):
        if line.startswith('\\version "'):
            return line
    return None


def record(name, path, suffix, extra, settings):
    text, err = run_oracle(path, extra)
    case = name + suffix
    if text is None and err is None:
        print('SKIPPED (oracle did not finish): %s' % case)
        return
    with open(path, 'rb') as f:
        data = f.read()
    raw_version = find_version_line(text) if text is not None else None
    payload = {
        'name': case,
        'options': settings,
        'source_name': os.path.basename(path),
        'midi_base64': base64.b64encode(data).decode('ascii'),
        'output': text,
        'frozen_version_line': raw_version,
        'messages': split_stderr(err),
    }
    with open(os.path.join(OUT, case + '.midi2ly.json'), 'w',
              encoding='utf-8') as f:
        json.dump(payload, f, ensure_ascii=False, indent=1, sort_keys=True)
    print('%-52s %s' % (case, 'no output' if text is None
                        else '%d bytes' % len(text)))


def main():
    if not os.path.exists(MIDI2LY):
        sys.exit('no oracle midi2ly at %s' % MIDI2LY)
    if not os.path.isdir(CORPUS):
        sys.exit('no reference MIDI corpus at %s -- run '
                 'tools/regression-harness/generate-midi-reference.sh first'
                 % CORPUS)
    os.makedirs(OUT, exist_ok=True)
    for stale in os.listdir(OUT):
        if stale.endswith('.midi2ly.json'):
            os.remove(os.path.join(OUT, stale))

    names = sorted(e[:-5] for e in os.listdir(CORPUS) if e.endswith('.midi'))
    for name in names:
        path = os.path.join(CORPUS, name + '.midi')
        record(name, path, '', [], {})

    for name in VARIANT_FILES:
        path = os.path.join(CORPUS, name + '.midi')
        if not os.path.exists(path):
            print('MISSING variant file: %s' % name)
            continue
        for suffix, extra, settings in VARIANTS:
            if not suffix:
                continue
            record(name, path, suffix, extra, settings)


if __name__ == '__main__':
    main()
