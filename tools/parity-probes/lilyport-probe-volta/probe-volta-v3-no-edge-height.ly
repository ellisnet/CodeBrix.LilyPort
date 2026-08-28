\version "2.27.2"

%% D32 probe V3 -- V1 (no bar number) with the bracket's EDGE HOOKS removed.
%% One input changed from V1.
%%
%% Measured on the oracle: V0 parks the bracket 4.877 above the top staff line
%% and V1 3.130, so the bar number is worth 1.747 of the gap and the bracket's
%% own clearance is 3.130.  The bracket's hooks hang 2.0 down from its line
%% (edge-height), so a placement that measures the WHOLE bracket clears
%% 2.0 + padding, and one that measures only the line clears padding.  The port
%% parks it at exactly 1.0 -- VoltaBracketSpanner's padding, and nothing else.
%%
%% If the oracle's gap falls by about 2.0 here and the port's does not move,
%% the port is placing the bracket by its LINE rather than by its extent.

\paper { ragged-right = ##t }

\layout {
  \context {
    \Score
    \omit BarNumber
    measureBarType = #'()
    \override VoltaBracket.text = ""
    \override VoltaBracket.edge-height = #'(0 . 0)
  }
}

staff = \new Staff \fixed c' {
  \repeat volta 2 {
    \bar ""
    \alternative {
      \volta 1 { R1 \bar "" }
      \volta 2 { R1 \bar "" }
    }
  }
}

\score {
  \new PianoStaff \with { instrumentName = "" } << \staff \staff >>
}
