\version "2.23.13"
{ \clef bass
  \set Staff.shapeNoteStyles = ##(do re mi fa #f la ti)
  \override Staff.NoteCollision.fa-merge-direction = #UP
  <<{ f2 }\\{ f2 }>> \bar "|" <<{ f2 }\\{ f2 }>> }
