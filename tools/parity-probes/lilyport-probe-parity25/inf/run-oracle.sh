#!/bin/bash
# Runs one probe on the PINNED ORACLE, under the corpus's own font pinning.
#
# Trap 8b: a probe is only honest if its environment matches the corpus's.  A
# probe run without FONTCONFIG_FILE resolves "serif" through the HOST's
# fontconfig and answers Noto Serif where the port uses C059, so the two runs
# measure different typefaces and every number is nonsense.  PARITY 4's
# text-metric table was taken that way and had to be withdrawn.
#
# Usage:  ./run-oracle.sh <probe>.ly [out-dir]

set -e

PROBE_DIR="$(cd "$(dirname "$0")" && pwd)"
PROBE="$1"
OUT="${2:-/tmp/p16-oracle}"

ORACLE=~/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond
HARNESS=~/GitHome/CodeBrix.LilyPort/tools/regression-harness
FONTDIR=~/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/2.27.2/fonts/otf

mkdir -p "$OUT"
sed "s|@FONTDIR@|${FONTDIR}|g" "${HARNESS}/reference-fonts.conf.in" > "${OUT}/reference-fonts.conf"

cd "$OUT"
FONTCONFIG_FILE="${OUT}/reference-fonts.conf" \
FONTCONFIG_PATH="${OUT}" \
    "$ORACLE" -dbackend=svg -dpoint-and-click=#f "${PROBE_DIR}/${PROBE}"
