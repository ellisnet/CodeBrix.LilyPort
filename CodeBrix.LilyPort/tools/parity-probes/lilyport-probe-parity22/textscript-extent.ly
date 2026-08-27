\version "2.27.0"

%% PARITY 22 probe 6 -- a TextScript's own stencil extents, on either engine.
%%
%% Nine residue rows are a TEXT LABEL above the staff sitting a little lower in the
%% port than in the oracle, over material that is byte-identical on both sides.  The
%% label's placement comes from its own SKYLINE, and a text stencil's skyline is its
%% stencil's box -- X from the LOGICAL rectangle and Y from the INK rectangle
%% (pango_item_string_stencil, trap 15a).  This prints that box.  The wrapper calls
%% upstream's own ly:text-interface::print and returns its result unchanged, so it
%% measures the engine, not the override.

#(define (probe-text-print grob)
   (let ((s (ly:text-interface::print grob)))
     (ly:warning "TXTEXT text=~s X=~a Y=~a"
                 (ly:grob-property grob 'text)
                 (ly:stencil-extent s X)
                 (ly:stencil-extent s Y))
     s))

\layout {
  ragged-right = ##t
  indent = #0
  \context {
    \Score
    \override TextScript.stencil = #probe-text-print
    \override TextScript.padding = #3
  }
}

\context PetrucciStaff \with {
  \override StaffSymbol.line-count = 4
  \omit TimeSignature
} {
  \clef "petrucci-c5"
  \cadenzaOn
  \textLengthOn
  <>^"BB "  \[ a\breve g \]
  <>^"BB "  \[ a\breve g \]
  <>^"BB "  \[ a\breve g \]
  <>^"BB "  \[ a\breve g \]
}
