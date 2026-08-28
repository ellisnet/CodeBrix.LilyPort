\version "2.27.2"

%% D6 probe -- WHICH CONTEXT does \measureRemainder's MeasureLengthChangeEvent
%% reach?  Runs UNCHANGED on both engines and PRINTS, so the two runs diff line
%% for line.
%%
%% \enablePerStaffTiming moves Timing from Score to Staff.  The fixtures
%% irregular-measure-initial-context and measure-remainder-initial-context exist
%% to expose an engine that issues the timing event in SCORE before descending
%% into \context Staff: with Timing per-staff, an event sent to the outer
%% context never reaches the Staff whose measure is being shortened, the measure
%% never ends early, and the bar line that should close it is never drawn.
%%
%% Printed per probe point: the context the applyContext ran in, the Timing
%% context found from there, and that Timing context's measureLength.

#(define (probe-timing tag)
   #{
     \applyContext
       #(lambda (ctx)
          (let ((timing (ly:context-find ctx 'Timing)))
            (ly:message "PROBE ~a ctx=~a timing=~a measureLength=~a measurePosition=~a"
                        tag
                        (ly:context-name ctx)
                        (if timing (ly:context-name timing) 'NONE)
                        (if timing (ly:context-property timing 'measureLength) 'NONE)
                        (if timing (ly:context-property timing 'measurePosition) 'NONE))))
   #})

\layout {
  \enablePerStaffTiming
}

\fixed c' <<
  {
    \measureRemainder {
      \context Staff = "A" \with { instrumentName = "A" }
      { #(probe-timing "inside-A-before") a2 #(probe-timing "inside-A-after") }
    }
    #(probe-timing "outside-A")
    1 |
  }
>>
