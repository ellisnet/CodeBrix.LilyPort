#!/usr/bin/env python3
"""CodeBrix.LilyPort repo tool (ships nothing): records what LilyPond's own abc2ly
does to a corpus of ABC files, as the fixtures AbcImporterParityTests replays.

THE ORACLE IS UPSTREAM'S OWN SCRIPT, RUN HERE -- the installed abc2ly from the
pinned LilyPond 2.27.2, so that @TOPLEVEL_VERSION@ is substituted the way a user's
copy substitutes it. Nothing is ever recorded from the port's own output (rule 33).

The oracle is run with -q. That is not a convenience: --quiet suppresses exactly
the identification and progress writes that belong to the command-line driver and
leaves the warnings and errors behind, which is precisely the split the port's
ImportResult.Messages makes. What -q leaves on stderr IS the expected message list.

TWO CORPORA:
  * input/regression/abc2ly/ -- upstream's own eight files, replayed byte for byte.
  * probes/ -- written here for what those eight do not exercise (D59): voice
    overlays, multi-voice staves, broken rhythms, decorations, header fields,
    guitar chords, an unknown mode, the repeat-bar warnings, broken rhythm inside
    a chord, and the inline-field/comment escapes.

THE ONE DECLARED DIVERGENCE. abc2ly writes a FROZEN `\\version "2.24.0"' line
under a comment saying it deliberately does not substitute its own release. D58
rules that every \\version an importer emits reads the port's compatible-with
constant instead, so the fixture stores that first line as a placeholder token and
the parity test fills it in. The raw line upstream wrote is kept beside it, so the
divergence is one recorded fact rather than a silence.

Reads the READ-ONLY LilyPond checkout and the pinned oracle (standing rule 7);
writes tests/CodeBrix.LilyPort.Tests/fixtures/abc/.

Usage: python3 gen-abc-fixtures.py [<lilypond-checkout>] [<oracle-bin-dir>]
"""
import json
import os
import subprocess
import sys
import tempfile

CHECKOUT = os.path.expanduser(
    sys.argv[1] if len(sys.argv) > 1 else '~/GitHome/lilypond')
ORACLE_BIN = os.path.expanduser(
    sys.argv[2] if len(sys.argv) > 2
    else '~/ClaudeHome/oracle/lilypond-2.27.2/bin')
ABC2LY = os.path.join(ORACLE_BIN, 'abc2ly')

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(
    HERE, '..', '..', 'tests', 'CodeBrix.LilyPort.Tests', 'fixtures', 'abc'))

CORPUS = os.path.join(CHECKOUT, 'input', 'regression', 'abc2ly')
PROBES = os.path.join(HERE, 'probes')

# abc2ly's two switches, as the port names them. A case may ask for either; every
# case is recorded with the defaults as well, under its own name.
VARIANTS = (
    ('', []),
    ('-beams', ['-b']),
)

# A run that will not finish is named, never silently dropped (board trap 23).
ORACLE_TIMEOUT_SECONDS = 30


def run_oracle(path, extra):
    """Run the pinned abc2ly over one file and give back (text, stderr).

    The input is COPIED into the working directory and named by its basename
    before the oracle sees it, because abc2ly prints the path it was handed into
    the one diagnostic that names a location. A fixture carrying this machine's
    absolute path would be a recording of this machine; carrying the basename, it
    is a recording of the file, and the port reproduces it by being told the same
    name through AbcImportOptions.SourceName.
    """
    with tempfile.TemporaryDirectory() as tmp:
        local = os.path.basename(path)
        with open(path, 'rb') as src, open(os.path.join(tmp, local), 'wb') as dst:
            dst.write(src.read())
        out = os.path.join(tmp, 'out.ly')
        try:
            done = subprocess.run(
                [ABC2LY, '-q', '-o', out] + extra + [local],
                capture_output=True, timeout=ORACLE_TIMEOUT_SECONDS, cwd=tmp)
        except subprocess.TimeoutExpired:
            return None, None
        if not os.path.exists(out):
            return None, done.stderr.decode('utf-8')
        with open(out, encoding='utf-8') as f:
            return f.read(), done.stderr.decode('utf-8')


def find_version_line(text):
    """Read back the frozen \version line, so a fence can assert on it."""
    for line in text.split('\n'):
        if line.startswith('\\version "'):
            return line
    return None


def split_stderr(err):
    """Cut a captured stderr stream into messages the way the port cuts it.

    ⚠ AN EMPTY LINE IS A MESSAGE. abc2ly's "Huh?  Don't understand" report ends
    with the remainder of the offending line, which still carries its own newline,
    so the script writes a blank line after it -- and the port, which cuts on the
    newlines the script writes, reports that blank. Dropping every empty entry
    here would have made the fixture disagree with a faithful port; only the empty
    left by the stream's own terminating newline is dropped.
    """
    if not err:
        return []
    parts = err.split('\n')
    return parts[:-1] if err.endswith('\n') else parts


def record(name, path, source):
    for suffix, extra in VARIANTS:
        text, err = run_oracle(path, extra)
        case = name + suffix
        if text is None and err is None:
            print('SKIPPED (oracle did not finish): %s' % case)
            continue
        with open(path, encoding='utf-8') as f:
            abc = f.read()
        raw_version = find_version_line(text) if text is not None else None
        payload = {
            'name': case,
            'source': source,
            'options': {'beams': '-b' in extra},
            'source_name': os.path.basename(path),
            'input': abc,
            'output': text,
            'frozen_version_line': raw_version,
            'messages': split_stderr(err),
        }
        with open(os.path.join(OUT, case + '.abc.json'), 'w',
                  encoding='utf-8') as f:
            json.dump(payload, f, ensure_ascii=False, indent=1, sort_keys=True)
        print('%-40s %s' % (case, 'no output' if text is None
                            else '%d bytes' % len(text)))


def main():
    if not os.path.exists(ABC2LY):
        sys.exit('no oracle abc2ly at %s' % ABC2LY)
    os.makedirs(OUT, exist_ok=True)
    for stale in os.listdir(OUT):
        if stale.endswith('.abc.json'):
            os.remove(os.path.join(OUT, stale))

    for directory, source in ((CORPUS, 'upstream'), (PROBES, 'probe')):
        for entry in sorted(os.listdir(directory)):
            if entry.endswith('.abc'):
                record(entry[:-4], os.path.join(directory, entry), source)


if __name__ == '__main__':
    main()
