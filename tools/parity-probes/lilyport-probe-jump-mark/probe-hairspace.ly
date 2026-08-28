\version "2.27.2"

%% PARITY 14 / D38, second probe.
%%
%% The first probe showed \segno, \coda and the <=2 concat pair agree
%% EXACTLY between the engines, so the defect is not the sign markup.
%% segno-style.ly's failing mark is \segnoMark 96, which takes
%% format-sign-with-number's number>2 branch:
%%
%%   (list sign  HAIR-SPACE  number  HAIR-SPACE  sign)
%%
%% Two U+200A hair spaces sit between the two signs, the construct is
%% symmetric about the number, and the reported constant is HALF the
%% measured gap error -- which is what one hair space would be.
%%
%% Cases 1/2 isolate the hair space against a control that contains no
%% hair space; cases 3/4 do the same for a plain space, which must NOT
%% show the same error if the defect is specific to U+200A.

hair = #(ly:wide-char->utf-8 #x200a)
enq  = #(ly:wide-char->utf-8 #x2000)

\markup \column {
  \box \concat { "8" "8" }
  \box \concat { "8" \hair "8" }
  \box \concat { "8" " " "8" }
  \box \concat { "8" \enq "8" }
  \box \concat { \segno \hair "96" \hair \segno }
  \box \concat { \coda \hair "96" \hair \coda }
  \box "96"
}
