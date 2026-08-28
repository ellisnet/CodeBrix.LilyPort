\version "2.27.2"

%% PARITY 14 / D38.  Measures EVERY Unicode space character's advance on
%% both engines, by putting it between two music glyphs and reading the two
%% glyph origins out of the SVG.  Glyph origins are unambiguous where a
%% \box outline is not.  Row 1 is the CONTROL with no space at all, so
%% every other row is read as (gap - control).

\markup \column {
  \concat { \musicglyph "scripts.segno" \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x0020) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x00a0) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2000) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2001) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2002) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2003) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2004) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2005) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2006) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2007) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2008) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x2009) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x200a) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x202f) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x205f) \musicglyph "scripts.segno" }
  \concat { \musicglyph "scripts.segno" #(ly:wide-char->utf-8 #x3000) \musicglyph "scripts.segno" }
}
