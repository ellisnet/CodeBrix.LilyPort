\version "2.19.21"
% PROBE -- staff-ledger-positions verbatim, with RehearsalMark's Y-offset wrapped so it
% prints what side-position-interface is working from before answering. Runs UNCHANGED
% on both engines, and on a port build with the SkylinePair copy fix reverted.
#(define (mk-probe grob)
   (let* ((sup (ly:grob-object grob 'side-support-elements))
          (lst (if (ly:grob-array? sup) (ly:grob-array->list sup) '()))
          (off (ly:side-position-interface::y-aligned-side grob)))
     (format #t "PROBE own-Y=~a support-Y=~a padding=~a offset=~a\n"
             (ly:grob-extent grob grob Y)
             (map (lambda (g) (ly:grob-extent g g Y)) lst)
             (ly:grob-property grob 'padding)
             off)
     off))
\layout { \context { \Score \override RehearsalMark.Y-offset = #mk-probe } }
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
\new Staff \relative { \notes #'(8 7 3 -1 -2 -6) #'(-6 (-2 3) -1) }
\new Staff \relative { \notes #'(8 7 3 -1 -2 -6) #'(-1 -6 (3 -2)) }
\new Staff \relative { \notes #'(-6 -2 -1 3 7 8) #'(-6 (-2 3) -1) }
\new Staff \relative { \notes #'(-6 -2 -1 3 7 8) #'(-1 -6 (3 -2)) }
