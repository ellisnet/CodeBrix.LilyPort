\version "2.27.2"
% PROBE G -- runs on BOTH engines. The BarNumber's OWN X-extent, its stencil's X-extent
% and its stored vertical-skyline span, all in the grob's own coordinates. Upstream's
% simple-vertical-skylines-from-extents builds the skyline out of (ly:grob-extent me me X),
% so on a faithful port these three must agree.
#(define (span pts)
   (if (< (length pts) 4) '()
       (cons (car (list-ref pts 1)) (car (list-ref pts 3)))))
#(define (bn-probe grob)
   (let* ((sk (ly:grob-property grob 'vertical-skylines))
          (st (ly:grob-property grob 'stencil)))
     (format #t "PROBE own-X=~a stencil-X=~a skyline-car-span=~a skyline-cdr-span=~a\n"
             (ly:grob-extent grob grob X)
             (if (ly:stencil? st) (ly:stencil-extent st X) 'none)
             (span (ly:skyline->points (car sk) X))
             (span (ly:skyline->points (cdr sk) X)))
     (ly:side-position-interface::y-aligned-side grob)))
\book {
  \score {
    \new Staff \relative c' { \repeat unfold 24 { c4 d e f } \break }
    \layout {
      indent = 0 ragged-right = ##f
      \context { \Score \override BarNumber.Y-offset = #bn-probe }
    }
  }
}
