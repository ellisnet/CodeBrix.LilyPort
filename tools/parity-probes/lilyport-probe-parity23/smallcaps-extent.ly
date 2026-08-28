\version "2.27.0"

%% PARITY 23 probe 1 -- what a SMALL-CAPS run measures, on either engine.
%%
%% markup-commands.svg puts everything after `\large \bold \fontCaps { normal ... }'
%% 1.2292 to the RIGHT, and font-features.svg stacks its later lines 0.033-0.045
%% LOWER -- both over byte-identical drawn text.  \fontCaps sets font-variant to
%% small-caps (define-markup-commands.scm:4105) and the SVG backend writes that
%% straight into the <text> element, so the DRAWING is identical on both engines and
%% only the MEASUREMENT can differ.  This prints the measurement.
%%
%% Runs unchanged on both engines; the wrapper returns upstream's own stencil, so it
%% measures the engine and not the override (trap 32b).

#(define (probe-text-print grob)
   (let ((s (ly:text-interface::print grob)))
     (ly:warning "SCEXT text=~s X=~a Y=~a"
                 (ly:grob-property grob 'text)
                 (ly:stencil-extent s X)
                 (ly:stencil-extent s Y))
     s))

\layout {
  ragged-right = ##t
  indent = #0
  \context {
    \Score
    \override TextScript.stencil = #probe-text-print
  }
}

{
  c'1^\markup { normal }
  c'1^\markup { \bold normal }
  c'1^\markup { \fontCaps normal }
  c'1^\markup { \bold \fontCaps normal }
  c'1^\markup { \large \bold \fontCaps normal }
  c'1^\markup { Hello }
  c'1^\markup { \fontCaps Hello }
  c'1^\markup { \override #'(font-features . ("smcp")) Hello }
  c'1^\markup { \override #'(font-features . ("onum")) 0123456789 }
  c'1^\markup { 0123456789 }
}
