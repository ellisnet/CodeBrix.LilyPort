\version "2.19.16"
#(set-global-staff-size 5)

\score {
  <<
    \context Staff = "a" { s1 \bar ":|." }
    \context Staff = "b" { s1 \bar ":|." }
  >>
}
