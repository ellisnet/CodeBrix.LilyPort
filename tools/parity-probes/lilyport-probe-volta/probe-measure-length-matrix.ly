\version "2.27.2"

%% D6 probe, third attempt -- a 2x2 matrix that separates the two candidate
%% causes.  Each score checks the SAME claim (inside the measureRemainder, this
%% context's Timing has measureLength 1/2) under a different combination of
%%
%%    (a) Timing per staff, or Timing at Score as usual
%%    (b) the wrapped music changing context, or staying put
%%
%% Warnings carry line numbers, so the four cases are told apart by line.
%% Expected on the oracle: SILENCE from all four.  Whichever cases warn on the
%% port name the axis the defect lies on.

%% ---- A: per-staff timing, context change (the failing fixture's shape) ----
\score {
  \fixed c' <<
    { \measureRemainder { \context Staff = "A" \with { instrumentName = "A" }
                          { \contextPropertyCheck Timing.measureLength #1/2 a2 } }
      1 | }
  >>
  \layout { \enablePerStaffTiming }
}

%% ---- B: per-staff timing, NO context change ----
\score {
  \new Staff \fixed c' {
    \measureRemainder { \contextPropertyCheck Timing.measureLength #1/2 a2 }
    1 |
  }
  \layout { \enablePerStaffTiming }
}

%% ---- C: Timing at Score (default), context change ----
\score {
  \fixed c' <<
    { \measureRemainder { \context Staff = "C" \with { instrumentName = "C" }
                          { \contextPropertyCheck Timing.measureLength #1/2 a2 } }
      1 | }
  >>
}

%% ---- D: Timing at Score (default), NO context change ----
\score {
  \new Staff \fixed c' {
    \measureRemainder { \contextPropertyCheck Timing.measureLength #1/2 a2 }
    1 |
  }
}
