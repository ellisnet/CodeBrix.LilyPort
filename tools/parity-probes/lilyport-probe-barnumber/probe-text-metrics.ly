\version "2.27.2"
% PROBE TEXTEXT -- runs on BOTH engines. The Y- and X-extent a TextScript's own stencil
% reports, for a one-line markup and for the two-line column of staff-ledger-positions.
% Nothing about placement: this is the text metric itself.
#(define (te-probe grob)
   (let ((st (ly:grob-property grob 'stencil)))
     (format #t "PROBE ~a Y=~a X=~a\n"
             (ly:grob-property grob 'text)
             (ly:stencil-extent st Y) (ly:stencil-extent st X))
     (ly:self-alignment-interface::y-aligned-on-self grob)))
\score {
  \new Staff {
    c'1^\markup "Hxg"
    c'1^\markup \small "Hxg"
    c'1^\markup "17"
    c'1^\markup \small \column { "line-positions = #(8 7 3 -1 -2 -6)" "ledger-positions = #(-6 (-2 3) -1)" }
  }
  \layout { \context { \Score \override TextScript.Y-offset = #te-probe } }
}
