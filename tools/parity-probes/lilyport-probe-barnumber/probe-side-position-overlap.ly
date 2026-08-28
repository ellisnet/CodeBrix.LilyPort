\version "2.27.2"
% PROBE E -- runs on BOTH engines. Prints the X extents of the BarNumber and of its
% StaffSymbol support in their common refpoint (which is what decides whether the two
% skylines overlap at all, and so whether Skyline::distance can answer anything finite),
% and repeats the measurement with horizon-padding raised from 0.05 to 5.0.
#(define (bn-probe grob)
   (let* ((sup (ly:grob-object grob 'side-support-elements))
          (lst (if (ly:grob-array? sup) (ly:grob-array->list sup) '()))
          (off (ly:side-position-interface::y-aligned-side grob)))
     (for-each
      (lambda (g)
        (let ((common (ly:grob-common-refpoint grob g X)))
          (format #t "PROBE hp=~a me-X=~a ~a-X=~a offset=~a\n"
                  (ly:grob-property grob 'horizon-padding)
                  (ly:grob-extent grob common X)
                  (grob::name g)
                  (ly:grob-extent g common X)
                  off)))
      lst)
     off))
\book {
  \score {
    \new Staff \relative c' { \repeat unfold 24 { c4 d e f } \break }
    \layout {
      indent = 0 ragged-right = ##f
      \context { \Score \override BarNumber.Y-offset = #bn-probe }
    }
  }
  \score {
    \new Staff \relative c' { \repeat unfold 24 { c4 d e f } \break }
    \layout {
      indent = 0 ragged-right = ##f
      \context {
        \Score
        \override BarNumber.Y-offset = #bn-probe
        \override BarNumber.horizon-padding = #5.0
      }
    }
  }
}
