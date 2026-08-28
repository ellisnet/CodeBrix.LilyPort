\version "2.27.2"

%% D6 probe, second attempt -- reports through \contextPropertyCheck instead of
%% ly:message, because the port prints NOTHING for ly:message (trap 1b's shape
%% again: PARITY 7 opened the sink for ly:warning, and ly:message still goes
%% nowhere).  A failed \contextPropertyCheck is a WARNING, which both engines do
%% print, so this probe is gradeable on both.
%%
%% The claim under test: inside \context Staff = "A", with Timing per staff,
%% \measureRemainder has set that Staff's measureLength to 1/2.  The oracle says
%% it has (measured: measureLength=1/2 inside A, 1 after).  A port that issues
%% the timing event in the OUTER context before descending will warn here.
%%
%% Expected on the oracle: SILENCE.  Any warning is the defect.

\layout {
  \enablePerStaffTiming
}

\fixed c' <<
  {
    \measureRemainder {
      \context Staff = "A" \with { instrumentName = "A" }
      { \contextPropertyCheck Timing.measureLength #1/2 a2 }
    }
    \contextPropertyCheck Timing.measureLength 1
    1 |
  }
>>
