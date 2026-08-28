\version "2.27.2"
% PROBE TEXT PER-GLYPH -- runs UNCHANGED on BOTH engines and prints its measurements,
% so the two runs diff line for line.  Written at PARITY 5 for D10.
%
% The point: a SINGLE-character markup's X extent is (0 . advance) on both engines --
% upstream's logical rect starts at x=0 and ends at the glyph's advance -- so a
% one-char line measures ONE glyph's advance and ONE glyph's ink box with nothing
% else folded in.  The repeated-character lines then show whether the X error grows
% per GLYPH (advance quantization) or per RUN (a single end correction), and the
% pairs isolate kerning.
#(define (te-probe grob)
   (let ((st (ly:grob-property grob 'stencil)))
     (format #t "PROBE |~a| X=~a Y=~a\n"
             (ly:grob-property grob 'text)
             (ly:stencil-extent st X) (ly:stencil-extent st Y))
     (ly:self-alignment-interface::y-aligned-on-self grob)))

\score {
  \new Staff {
    % --- one glyph at a time: advance and ink box, nothing else ---
    c'1^\markup "H"
    c'1^\markup "x"
    c'1^\markup "g"
    c'1^\markup "1"
    c'1^\markup "7"
    c'1^\markup "o"
    c'1^\markup "."
    c'1^\markup "i"
    % --- the same glyph repeated: does the X error grow per glyph? ---
    c'1^\markup "HH"
    c'1^\markup "HHH"
    c'1^\markup "HHHH"
    c'1^\markup "HHHHHHHH"
    c'1^\markup "oo"
    c'1^\markup "oooo"
    % --- pairs that kern, against pairs that do not ---
    c'1^\markup "17"
    c'1^\markup "Hx"
    c'1^\markup "Hxg"
    % --- size dependence: is the quantization a fixed DEVICE step? ---
    c'1^\markup \small "H"
    c'1^\markup \tiny "H"
    c'1^\markup \large "H"
    c'1^\markup \huge "H"
  }
  \layout { \context { \Score \override TextScript.Y-offset = #te-probe } }
}

% --- kerning pairs, added at PARITY 5: does either engine apply GPOS kerning, and
%     is the pair adjustment on the pixel grid or under it? ---
\score {
  \new Staff {
    c'1^\markup "A"
    c'1^\markup "V"
    c'1^\markup "T"
    c'1^\markup "AV"
    c'1^\markup "VA"
    c'1^\markup "To"
    c'1^\markup "AVAVAVAV"
  }
  \layout { \context { \Score \override TextScript.Y-offset = #te-probe } }
}
