\version "2.25.35"
%% A/B on INPUT: one axis changed per file, read straight out of the SVG.
\new PianoStaff <<
  \new Staff = "up" \relative { cis''8 fis, bes4 <a cis>8 f bis4 }
  \new Staff = "down" { \clef bass \relative { <fis a cis>8[ <fis a cis> cis' cis <fis, a> <fis a>] dis'4 } }
>>
