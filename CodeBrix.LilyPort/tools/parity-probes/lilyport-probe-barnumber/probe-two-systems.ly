\version "2.27.2"
% PROBE A -- baseline: two systems, so a BarNumber is engraved at system 2's start.
\book {
  \score {
    \new Staff \relative c' { \repeat unfold 24 { c4 d e f } \break }
    \layout { indent = 0 ragged-right = ##f }
  }
}
