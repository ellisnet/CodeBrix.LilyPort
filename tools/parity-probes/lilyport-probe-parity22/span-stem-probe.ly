\version "2.16.0"

%% PARITY 22 probe -- runs UNCHANGED on the oracle and on the port, and PRINTS its
%% measurements through ly:warning (ly:message is discarded by the port -- see the
%% plan's OPEN FINDINGS), so two runs diff line for line.
%%
%% It OBSERVES only: an extra engraver that acknowledges the same stem-interface
%% Span_stem_engraver does and reports, per timestep, how many stems were seen and
%% how many of them are cross-staff ROOTS by upstream's own test.  It changes no
%% property, so trap 32b does not apply.

#(define (Probe_stem_engraver ctx)
   (let ((stems '()))
     (make-engraver
      (acknowledgers
       ((stem-interface trans grob source)
        (set! stems (cons grob stems))))
      ((process-acknowledged trans)
       (if (pair? stems)
           (ly:warning "SPANPROBE nstems=~a nroots=~a"
                       (length stems)
                       (length (filter
                                (lambda (s)
                                  (eq? cross-staff-connect
                                       (ly:grob-property-data s 'cross-staff)))
                                stems))))
       (set! stems '())))))

\layout {
  \context {
    \PianoStaff
    \consists #Span_stem_engraver
    \consists #Probe_stem_engraver
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
