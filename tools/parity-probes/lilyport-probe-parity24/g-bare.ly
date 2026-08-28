\version "2.19.16"
#(set-global-staff-size 5)

\score {
  <<
    \context Staff = "a" \with { \omit Clef \omit TimeSignature } { s1 \bar ":|." }
    \context Staff = "b" \with { \omit Clef \omit TimeSignature } { s1 \bar ":|." }
  >>

  \layout {
    #(layout-set-staff-size 30)
  }
}
