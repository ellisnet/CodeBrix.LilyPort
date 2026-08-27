\version "2.25.4"
%% v1: v0 with ONE input changed -- proportionalNotationDuration switched off.
%% If the bar-line divergence disappears here it is the proportional spacing path.
\paper { indent = 0 ragged-right = ##f }
\score { \new ChordGrid \chordmode { c1 c2:6 c2 g4 g4 c2 c2 g4 g4 }
  \layout { \context { \ChordGridScore proportionalNotationDuration = ##f } } }
