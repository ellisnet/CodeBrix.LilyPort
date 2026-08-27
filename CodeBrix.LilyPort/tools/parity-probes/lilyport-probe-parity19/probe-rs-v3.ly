\version "2.19.16"
#(set-global-staff-size 5)
\score {
  <<
    \context Staff = "s1" \with {
      
    } {
      s1 \bar ":|."
    }
    \context Staff = "s2" \with {
      
    } {
      s1 \bar ":|."
    }
    \context Staff = "s3" {
      s1 \bar ":|."
    }
  >>
}
