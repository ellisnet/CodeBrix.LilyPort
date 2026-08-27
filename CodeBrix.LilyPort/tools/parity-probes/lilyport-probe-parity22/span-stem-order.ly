\version "2.16.0"

%% PARITY 22 probe 2 -- WHEN is each Stem stencil computed, and in what ORDER
%% relative to the Span_stem grobs that hide some of them?
%%
%% `stem-span-stencil' (scm/music-functions.scm) hides its member stems by SETTING
%% their `stencil' property to #f as a SIDE EFFECT of computing the span's own
%% stencil.  That only removes ink if the span's stencil is computed before the
%% drawing loop consumes the stems.  This probe wraps Stem.stencil in a printing
%% pass-through, so both engines report their own order and the runs diff line for
%% line.  The wrapper calls the SAME `ly:stem::print', so it measures the engine
%% rather than the override (trap 32b); the control is that the page still renders
%% the same ink it does without the probe.

#(define stem-counter 0)

#(define (probe-stem-print grob)
   (set! stem-counter (1+ stem-counter))
   (ly:warning "STEMPRINT seq=~a cross-staff=~a"
               stem-counter
               (ly:grob-property grob 'cross-staff))
   (ly:stem::print grob))

\layout {
  \context {
    \PianoStaff
    \consists #Span_stem_engraver
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
