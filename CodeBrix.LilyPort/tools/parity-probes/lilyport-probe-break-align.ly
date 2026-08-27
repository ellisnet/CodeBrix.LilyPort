\version "2.27.2"

%% Diagnostic probe for loose end 10, class 4 ("No spacing entry from X to `Y'").
%% Runs identically on the oracle and the port; prints, for every BreakAlignment,
%% the order list actually in force and the break-align-symbol of every element
%% in the group list the ordering pass is given.  The comparison of interest is
%% ORACLE vs PORT on the same input, not the values themselves.

#(define (probe-break-alignment grob)
   (let* ((orders (ly:grob-property grob 'break-align-orders))
          (dir (ly:item-break-dir grob))
          (elements (ly:grob-array->list (ly:grob-object grob 'elements))))
     (display "PROBE break-dir=")
     (display dir)
     (display " orders-vector?=")
     (display (vector? orders))
     (display " raw-group-symbols=")
     (display
      (map (lambda (group)
             (let ((inner (ly:grob-object group 'elements #f)))
               (if (ly:grob-array? inner)
                   (map (lambda (g) (ly:grob-property g 'break-align-symbol))
                        (ly:grob-array->list inner))
                   'no-elements)))
           elements))
     (newline))
   #f)

\score {
  \new Staff {
    \override Score.BreakAlignment.before-line-breaking = #probe-break-alignment
    \key g \major
    \time 4/4
    c'1
    \breathe
    \bar "||"
    \key f \major
    \time 3/4
    c'2.
  }
  \layout { }
}
