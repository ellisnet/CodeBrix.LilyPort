\version "2.27.2"
% PROBE MARK -- runs on BOTH engines. The RehearsalMark of staff-ledger-positions'
% first system: its own Y-extent, its support's, and the offset side-position gives it.
#(define (mk-probe grob)
   (let* ((sup (ly:grob-object grob 'side-support-elements))
          (lst (if (ly:grob-array? sup) (ly:grob-array->list sup) '()))
          (off (ly:side-position-interface::y-aligned-side grob)))
     (format #t "PROBE mark own-Y=~a supports=~a offset=~a padding=~a\n"
             (ly:grob-extent grob grob Y)
             (map (lambda (g) (list (grob::name g) (ly:grob-extent g g Y))) lst)
             off (ly:grob-property grob 'padding))
     off))
notes =
#(define-music-function (s l) (list? list?)
  #{ \override Staff.StaffSymbol.line-positions = #s
     \override Staff.StaffSymbol.ledger-positions = #l
     \override Staff.StaffSymbol.ledger-extra = #15
     \relative {
       \mark \markup \small \column {
         \concat { "line-positions = #" #(scm->string s) }
         \concat { "ledger-positions = #" #(scm->string l) } }
       g,4 c e b' |
       e4 c'' e g
    } #})
\score {
  \new Staff \relative { \notes #'(8 7 3 -1 -2 -6) #'(-6 (-2 3) -1) }
  \layout { \context { \Score \override RehearsalMark.Y-offset = #mk-probe } }
}
