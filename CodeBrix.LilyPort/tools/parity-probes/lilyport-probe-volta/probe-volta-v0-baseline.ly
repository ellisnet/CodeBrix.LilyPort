\version "2.27.2"

%% D32 probe V0 -- the bar-line-built-in fixture reduced to ONE PianoStaff and
%% one system, with nothing changed.  This is the BASELINE: the oracle parks the
%% volta bracket 3.9200 higher than the port does, and every other file in this
%% directory changes exactly one input away from here.
%%
%% Nothing is instrumented.  Read the bracket's group translate straight out of
%% the SVG, exactly as PARITY 4's probe-two-systems.ly does.

\paper { ragged-right = ##t }

\layout {
  \context {
    \Score
    barNumberVisibility = #all-bar-numbers-visible
    \override BarNumber.break-align-symbols = #'(staff-bar left-edge)
    \override BarNumber.break-visibility = #all-visible
    \override BarNumber.self-alignment-X = #CENTER
    \override BarNumber.stencil = #(lambda (grob)
                                    (ly:grob-set-property! grob 'text "V")
                                    (ly:text-interface::print grob))
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
