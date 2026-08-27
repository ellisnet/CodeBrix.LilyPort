\version "2.27.2"
% PROBE BRACE -- runs on BOTH engines. The whole input to \left-brace's glyph choice:
% the fetaBraces font's glyph COUNT, the layout's output-scale, ly:pt, the scaled-size
% the binary search is given, the Y extent of a spread of brace glyphs, and the index
% the search actually returns. GLYPHS-DIFFER shows the oracle drawing brace177 where the
% port draws brace49 at a ~2.85x larger transform, so exactly one of these numbers is
% the cause; this prints all of them so the two runs diff line for line.
#(define-markup-command (brace-probe layout props size) (number?)
   (let* ((font (ly:paper-get-font layout
                                   (cons '((font-encoding . fetaBraces)
                                           (font-name . #f))
                                         props)))
          (glyph-count (1- (ly:otf-glyph-count font)))
          (scale (ly:output-def-lookup layout 'output-scale))
          (scaled-size (/ (ly:pt size) scale))
          (glyph (lambda (n)
                   (ly:font-get-glyph font (string-append "brace" (number->string n)))))
          (gy (lambda (n) (interval-length (ly:stencil-extent (glyph n) Y)))))
     (format #t "PROBE size=~a glyph-count=~a output-scale=~a ly:pt(size)=~a scaled-size=~a\n"
             size glyph-count scale (ly:pt size) scaled-size)
     (for-each
      (lambda (n)
        (format #t "PROBE brace~a Yextent=~a Ylen=~a\n"
                n (ly:stencil-extent (glyph n) Y) (gy n)))
      (list 0 1 10 49 100 177 200 300 400 500 glyph-count))
     (format #t "PROBE binary-search=~a\n" (binary-search 0 glyph-count gy scaled-size))
     empty-stencil))

\markup \brace-probe #35
\markup \brace-probe #45

% And the real thing: a GrandStaff, whose SystemStartBrace goes through
% System_start_delimiter::staff_brace -> make-left-brace-markup.
\score {
  \new GrandStaff <<
    \new Staff { c'1 }
    \new Staff { c1 }
  >>
}
