MusicXML files written for this port, for what upstream's own corpus does not
exercise. EMPTY, and deliberately so.

abc2ly needed ten probes (D59) because its eight upstream regression files
convert without a single warning, which left every diagnostic path in that
converter untested. musicxml2ly's corpus is the opposite case: it IS a MusicXML
test suite, organised by feature, and the 206 recorded cases already reach 24
distinct diagnostics.

Add a probe here when a path is found that those 206 cases do not reach, name it
in ../README.txt under THE CORPORA, and say what it reaches. The generator picks
up any .xml or .mxl in this directory automatically and records it with
source='probe'.

⚠ THREE PROBES ARE ALREADY OWED, and DIVERGENCES.txt says what each must contain:
a fret diagram converted under --transpose (candidate 2), a dotted rest whose
<dot> names a font size (candidate 3), and a <figure> carrying both a <prefix>
and a <suffix> (candidate 4). Each is a defect found by READING the python, which
D64(a) refuses to accept as proof; the probe is what asks the oracle. Writing them
does not change the port -- it decides whether three candidates become ruled
divergences or get struck.

This file exists so the directory is tracked; delete it when the first real probe
lands.
