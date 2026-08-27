\version "2.27.2"

%% D32 probe V2 -- V0 with the bar number's LAMBDA STENCIL replaced by the plain
%% callback plus a static text override.  The drawn result is identical to V0's;
%% only the way the stencil is asked for changes.  One input changed.
%%
%% The A/B this answers: if the two engines agree here and disagree in V0, the
%% differing input is the SCHEME-PROCEDURE stencil callback -- the fixture's
%% lambda sets a property from inside the stencil callback, and a placement pass
%% that reads the stencil before that lambda has run (or that refuses to call a
%% bare procedure at all -- trap 24) would see a different grob than the oracle.

\paper { ragged-right = ##t }

\layout {
  \context {
    \Score
    barNumberVisibility = #all-bar-numbers-visible
    \override BarNumber.break-align-symbols = #'(staff-bar left-edge)
    \override BarNumber.break-visibility = #all-visible
    \override BarNumber.self-alignment-X = #CENTER
    \override BarNumber.text = "V"
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
