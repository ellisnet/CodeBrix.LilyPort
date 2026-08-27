\version "2.27.0"

%% PARITY 23 probe 2 -- WHAT RULE Pango's synthetic small caps uses.
%%
%% Probe 1 established that the oracle's \fontCaps CHANGES the measurement while
%% C059 has no `smcp' in its GSUB, so the synthesis is Pango's own and not the
%% font's.  This prints the quantities a synthesis rule could be built from:
%% cap height, x-height, and the uppercased strings' own widths.

#(define (probe-text-print grob)
   (let ((s (ly:text-interface::print grob)))
     (ly:warning "SCRULE text=~s X=~a Y=~a"
                 (ly:grob-property grob 'text)
                 (ly:stencil-extent s X)
                 (ly:stencil-extent s Y))
     s))

\layout {
  ragged-right = ##t
  indent = #0
  \context { \Score \override TextScript.stencil = #probe-text-print }
}

{
  c'1^\markup { norma }
  c'1^\markup { NORMAL }
  c'1^\markup { NORMA }
  c'1^\markup { HELLO }
  c'1^\markup { ELLO }
  c'1^\markup { \fontCaps normal }
  c'1^\markup { \fontCaps NORMAL }
  c'1^\markup { \fontCaps Normal }
  c'1^\markup { \fontCaps 0123456789 }
  c'1^\markup { \fontCaps { normal NORMAL } }
}
