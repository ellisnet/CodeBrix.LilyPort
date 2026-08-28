\version "2.21.0"
%% Prints the glyph-string expression of a TextScript's stencil, which is where
%% `add_glyph_string_segments' reads its per-glyph widths and offsets.  Runs
%% UNCHANGED on both engines, and its answer is a list of numbers, so two runs
%% diff line for line.
#(define (dump-glyph-strings expr)
   (cond
    ((and (pair? expr) (eq? (car expr) 'glyph-string))
     (let ((whxy (list-ref expr 5)))
       (ly:message "GLYPH-STRING")
       (let loop ((g whxy))
         (if (pair? g)
             (let ((e (car g)))
               (ly:message
                 (format #f "  w=~s h=~s xo=~s yo=~s idx=~s"
                         (list-ref e 0) (list-ref e 1)
                         (list-ref e 2) (list-ref e 3) (list-ref e 4)))
               (loop (cdr g)))))))
    ((pair? expr)
     (dump-glyph-strings (car expr))
     (dump-glyph-strings (cdr expr)))
    (else #t)))

\layout { indent = 0 }
{ \override TextScript.after-line-breaking =
    #(lambda (grob)
       (dump-glyph-strings (ly:stencil-expr (ly:grob-property grob 'stencil))))
  a'' ^\markup "bb" }
