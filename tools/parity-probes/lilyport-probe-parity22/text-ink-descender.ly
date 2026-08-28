\version "2.21.0"

%% PARITY 22 probe 8 -- the INK box of a text stencil, ascender AND descender.
%%
%% A text label above the staff is placed by its own skyline, and a text stencil's Y
%% extent is the INK rectangle (trap 15a).  Probe 6 measured strings with no descender
%% and found the two engines agreeing to 1e-6.  These strings HAVE descenders, which is
%% what the residue's labels have ("Upper text", "very excentric staff", "Art. down").
%% If the port's ink bottom sits higher, its label needs less clearance and lands lower
%% by exactly that much -- which is the shape of merge-rests-engraver's 0.0139.

#(define (probe-text-print grob)
   (let ((s (ly:text-interface::print grob)))
     (ly:warning "INK ~s Y=~a" (ly:grob-property grob 'text) (ly:stencil-extent s Y))
     s))

\layout {
  ragged-right = ##t
  \context {
    \Score
    \override TextScript.stencil = #probe-text-print
  }
}

{
  c'1^"Upper text"
  c'1^"very excentric staff"
  c'1^"BB"
  c'1^"pqgjy"
  c'1^"Hxyz"
}
