\version "2.27.2"

%% PARITY 14 / D38 -- the segno-coda pair spacing.
%%
%% format-sign-with-number concatenates N sign markups with \concat
%% (word-space 0), so the gap between two signs IS the first sign's
%% X-extent.  \segno scales its musicglyph by 0.6, \coda by 0.8
%% (define-markup-commands.scm).  If ly:stencil-scale does not scale the
%% EXTENT, the concatenated pair spreads while its centre stays put --
%% which is exactly the reported signature.
%%
%% Each case is \box'd so the extent is READABLE as a rect on both
%% engines.  Cases 1 and 4 are the CONTROLS: an unscaled musicglyph,
%% whose extent no scale is applied to, must agree between the engines
%% even if the scaled cases do not.

\markup \column {
  \box \musicglyph "scripts.segno"
  \box \segno
  \box \concat { \segno \segno }
  \box \musicglyph "scripts.coda"
  \box \coda
  \box \concat { \coda \coda }
}
