\version "2.19.16"
#(set-global-staff-size 5)
\score {
  <<
    \context Staff = "s1" \with {
      
    } {
      s1 \bar ":|."
    }
    \context Staff = "s2" \with {
      \override StaffSymbol.line-positions = #'(-4 -2 0 2)
    } {
      s1 \bar ":|."
    }
    \context Staff = "s3" {
      s1 \bar ":|."
    }
  >>
}
