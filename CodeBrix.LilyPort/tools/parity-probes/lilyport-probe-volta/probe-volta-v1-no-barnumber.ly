\version "2.27.2"

%% D32 probe V1 -- V0 with the BAR NUMBER REMOVED.  One input changed.
%%
%% The A/B this answers (trap 30b: the cheapest test for "does this rule fire?"
%% is an A/B on the INPUT): if the two engines AGREE here and disagree in V0,
%% the differing input is the bar number, and the defect is that the port's
%% volta bracket is not stacking above it.  If they still disagree, the bar
%% number is innocent and the bracket's own placement is wrong.

\paper { ragged-right = ##t }

\layout {
  \context {
    \Score
    \omit BarNumber
    measureBarType = #'()
    \override VoltaBracket.text = ""
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
