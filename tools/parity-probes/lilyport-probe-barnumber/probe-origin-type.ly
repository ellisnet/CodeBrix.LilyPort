\version "2.27.2"
% PROBE ORIGIN -- runs UNCHANGED on BOTH engines. An `origin' property must satisfy
% ly:input-location?, which is what Music::origin and every point-and-click and
% diagnostic path depends on. The BARE line is the control: ly:make-music stamps no
% origin, so the predicate must answer N there or it is answering Y to everything.
#(define (loc? m) (if (ly:input-location? (ly:music-property m 'origin)) "Y" "N"))
seq = { c'1 }
chd = { <c' e'>1 }
#(let* ((note (car (ly:music-property seq 'elements)))
        (ch (car (ly:music-property chd 'elements))))
   (format #t "ORIGINPROBE seq=~a note=~a\n" (loc? seq) (loc? note))
   (format #t "ORIGINPROBE chordwrap=~a name=~a\n" (loc? ch) (ly:music-property ch 'name))
   (for-each
     (lambda (e) (format #t "ORIGINPROBE chord-elt ~a = ~a\n" (ly:music-property e 'name) (loc? e)))
     (ly:music-property ch 'elements))
   (format #t "ORIGINPROBE bare=~a\n" (loc? (ly:make-music 'NoteEvent))))
