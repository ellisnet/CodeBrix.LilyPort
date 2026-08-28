\version "2.23.7"

%% PARITY 22 probe 5 -- what does side-position see when a grob's skyline is removed?
%%
%% `skyline-removed.ly' tweaks `vertical-skylines' to ##f on the FIRST TextScript and
%% stacks a second one above it.  This prints what each script's Y-offset comes out
%% as and what its own vertical-skylines property answers, on either engine.

#(define (probe-y-offset grob)
   (let ((v (ly:grob-property grob 'vertical-skylines))
         (y (ly:side-position-interface::y-aligned-side grob)))
     (ly:warning "SKYPROBE text=~a skylines=~a y=~a"
                 (ly:grob-property grob 'text)
                 (if (ly:skyline-pair? v) 'PAIR v)
                 y)
     y))

\layout {
  \context {
    \Voice
    \override TextScript.Y-offset = #probe-y-offset
  }
}

{
  c'\tweak vertical-skylines ##f ^"foo" ^"bar"
}
