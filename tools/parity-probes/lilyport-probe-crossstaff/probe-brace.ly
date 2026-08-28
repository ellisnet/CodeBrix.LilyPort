\version "2.25.35"

%% Probe: \left-brace's binary search over the fetaBraces font, run in the very
%% environment left-brace runs in (a markup command, so `layout` and `props` are
%% the real ones).  Prints the glyph count the search is bounded by, the scaled
%% size it searches for, and the measured height of a spread of brace glyphs.
#(define-markup-command (bracestats layout props) ()
   (let* ((font (ly:paper-get-font layout
                                   (cons '((font-encoding . fetaBraces)
                                           (font-name . #f))
                                         props)))
          (glyph-count (1- (ly:otf-glyph-count font)))
          (scale (ly:output-def-lookup layout 'output-scale)))
     (ly:warning "PROBE glyph-count ~a" glyph-count)
     (ly:warning "PROBE output-scale ~a" scale)
     (ly:warning "PROBE font-name ~a" (ly:font-name font))
     (for-each
      (lambda (size)
        (ly:warning "PROBE scaled-size for ~a pt = ~a" size (/ (ly:pt size) scale)))
      '(10 20 35 45 80))
     (for-each
      (lambda (n)
        (let ((st (ly:font-get-glyph font (string-append "brace" (number->string n)))))
          (ly:warning "PROBE brace~a height ~a"
                      n
                      (if (null? (ly:stencil-expr st))
                          'EMPTY
                          (interval-length (ly:stencil-extent st Y))))))
      '(0 1 100 177 178 200 272 290 295 370 405))
     empty-stencil))

\markup \bracestats
