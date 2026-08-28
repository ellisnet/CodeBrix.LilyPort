\version "2.25.35"
%% CONTROL for v4: identical music, the \change Staff removed.
\new PianoStaff <<
  \new Staff = "up" \relative { c''1 }
  \new Staff = "down" { \clef bass \relative { c1 } }
>>
