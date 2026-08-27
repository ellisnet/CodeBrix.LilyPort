\version "2.16.0"

%% PARITY 22 probe 3 -- the ORDER in which stem stencils and SPAN stencils are
%% computed, on either engine, from one file.
%%
%% `Span_stem_engraver' is re-implemented here out of PUBLIC bindings only, a
%% faithful copy of scm/music-functions.scm's (make-stem-spans! / make-stem-span! /
%% stem-span-stencil / stem-connectable? / stem-is-root?), with two ly:warning lines
%% added.  The real engraver is NOT consisted, so nothing runs twice.  Both engines
%% execute the same Scheme, so the two logs diff line for line.

#(define stem-seq 0)
#(define span-seq 0)

#(define (probe-stem-print grob)
   (set! stem-seq (1+ stem-seq))
   (ly:warning "STEMPRINT seq=~a" stem-seq)
   (ly:stem::print grob))

#(define (my-close-enough? a b) (< (abs (- a b)) 0.0001))

#(define ((my-connectable? ref root) stem)
   (or (eq? root stem)
       (and (my-close-enough? (car (ly:grob-extent root ref X))
                              (car (ly:grob-extent stem ref X)))
            (positive? (* (ly:grob-property root 'direction)
                          (- (car (ly:grob-extent stem ref Y))
                             (car (ly:grob-extent root ref Y))))))))

#(define (my-extent-combine extents)
   (if (pair? (cdr extents))
       (let ((a (car extents)) (b (my-extent-combine (cdr extents))))
         (cons (min (car a) (car b)) (max (cdr a) (cdr b))))
       (car extents)))

#(define (my-span-stencil span)
   (set! span-seq (1+ span-seq))
   (let* ((system (ly:grob-system span))
          (root (ly:grob-parent span X))
          (all (ly:grob-object span 'stems))
          (stems (filter (my-connectable? system root) all)))
     (ly:warning "SPANSTENCIL seq=~a all=~a connectable=~a yparent=~a xparent=~a extra=~a"
                 span-seq (length all) (length stems)
                 (let ((p (ly:grob-parent span Y)))
                   (if (ly:grob? p)
                       (list (assoc-get 'name (ly:grob-property p 'meta))
                             (length (ly:grob-array->list (ly:grob-object p 'elements))))
                       'NONE))
                 (let ((p (ly:grob-parent span X)))
                   (if (ly:grob? p)
                       (assoc-get 'name (ly:grob-property p 'meta))
                       'NONE))
                 (list (ly:grob-property span 'cross-staff)
                       (ly:grob-property span 'outside-staff-priority)))
     (if (<= 2 (length stems))
         (let* ((yextents (map (lambda (st) (ly:grob-extent st system Y)) stems))
                (yextent (my-extent-combine yextents))
                (layout (ly:grob-layout root))
                (blot (ly:output-def-lookup layout 'blot-diameter)))
           (for-each (lambda (st)
                       (set! (ly:grob-property st 'stencil) #f)
                       (ly:warning "  HIDE readback=~a"
                                   (ly:grob-property st 'stencil)))
                     stems)
           (ly:round-filled-box (ly:grob-extent root root X) yextent blot))
         #f)))

#(define ((my-make-stem-span! stems trans) root)
   (let ((span (ly:engraver-make-grob trans 'Stem '())))
     (ly:grob-set-parent! span X root)
     (set! (ly:grob-object span 'stems) stems)
     (set! (ly:grob-property span 'X-offset) 0)
     (set! (ly:grob-property span 'stencil) my-span-stencil)))

#(define (my-stem-is-root? stem)
   (eq? cross-staff-connect (ly:grob-property-data stem 'cross-staff)))

#(define (my-make-stem-spans! ctx stems trans)
   (if (<= 2 (length stems))
       (for-each (my-make-stem-span! stems trans)
                 (filter my-stem-is-root? stems))))

#(define (My_span_stem_engraver ctx)
   (let ((stems '()))
     (make-engraver
      (acknowledgers
       ((stem-interface trans grob source)
        (set! stems (cons grob stems))))
      ((process-acknowledged trans)
       (my-make-stem-spans! ctx stems trans)
       (set! stems '())))))

\layout {
  \context {
    \PianoStaff
    \consists #My_span_stem_engraver
  }
  \context {
    \Voice
    \override Stem.stencil = #probe-stem-print
  }
}

{
  \new PianoStaff <<
    \new Staff {
      r4 e'8 f' <b d'>8\> r \tuplet 3/2 { e'8. f'16 g'8 } |
      g r\!
    }
   \new Staff {
     \clef bass
      \stemUp
      c8 d \crossStaff { e f <e g>8 r \tuplet 3/2 { e8. f16 g8 } |
      c8 } d
    }
  >>
}
