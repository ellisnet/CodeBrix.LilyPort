\version "2.19.16"
#(set-global-staff-size 5)

\score {
  <<
    \context Staff = "a" \with { \omit TimeSignature \omit BarLine } { s1 }
    \context Staff = "b" \with { \omit TimeSignature \omit BarLine } { s1 }
  >>

  \layout {
    #(layout-set-staff-size 30)
  }
}
