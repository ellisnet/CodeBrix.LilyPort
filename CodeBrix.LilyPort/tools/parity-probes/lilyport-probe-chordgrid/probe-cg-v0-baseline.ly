\version "2.25.4"
%% v0: four measures of a chord grid, the shape chord-grid.ly's first system has.
%% Runs UNCHANGED on both engines; read the bar-line rect x positions out of the SVG.
\paper { indent = 0 ragged-right = ##f }
\score { \new ChordGrid \chordmode { c1 c2:6 c2 g4 g4 c2 c2 g4 g4 } \layout { } }
