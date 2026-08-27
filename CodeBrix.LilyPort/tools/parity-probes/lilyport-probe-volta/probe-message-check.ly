\version "2.27.2"

%% Control for the D6 probe: does ly:message reach the sweep log at all, and
%% does \applyContext run its procedure?  Trap 1b -- before concluding that a
%% message is not produced, check that it is not merely not printed.

#(ly:message "PROBE toplevel ly:message works")

\score {
  \new Staff \fixed c' {
    \applyContext
      #(lambda (ctx)
         (ly:message "PROBE applyContext ran ctx=~a" (ly:context-name ctx)))
    c1
  }
}
