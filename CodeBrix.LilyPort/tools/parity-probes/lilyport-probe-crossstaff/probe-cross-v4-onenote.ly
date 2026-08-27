\version "2.25.35"
%% A/B rung: ONE note moved by \change Staff, no beam, no chord, no switch line.
\new PianoStaff <<
  \new Staff = "up" \relative { c''1 }
  \new Staff = "down" { \clef bass \relative { c1 \change Staff = up } }
>>
