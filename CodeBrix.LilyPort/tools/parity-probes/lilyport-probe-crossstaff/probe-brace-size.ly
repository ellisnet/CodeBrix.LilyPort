\version "2.25.35"

%% Probe: the SIZE (in points) that System_start_delimiter::staff_brace hands to
%% \left-brace, and the scaled-size the markup command derives from it.  Shadows
%% make-left-brace-markup so the number is read where it is actually produced.
#(define probe-old-left-brace make-left-brace-markup)
#(set! make-left-brace-markup
   (lambda (size)
     (ly:warning "PROBE make-left-brace-markup size ~a pt" size)
     (probe-old-left-brace size)))

#(define-markup-command (bracescaled layout props size) (number?)
   (let* ((scale (ly:output-def-lookup layout 'output-scale)))
     (ly:warning "PROBE for size ~a pt: ly:pt ~a, output-scale ~a, scaled-size ~a"
                 size (ly:pt size) scale (/ (ly:pt size) scale))
     empty-stencil))

\score {
  \new PianoStaff <<
    \new Staff { c'1 }
    \new Staff { \clef bass c1 }
  >>
}

\markup \bracescaled #63.2
