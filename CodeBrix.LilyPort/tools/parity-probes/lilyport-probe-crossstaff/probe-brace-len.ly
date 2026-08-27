\version "2.25.35"

%% Probe: the extent System_start_delimiter::print unites, which is the `len` it
%% hands to staff_brace and therefore what the brace-glyph search is asked for.
%% Prints, per delimiter, every element it considers, whether that element's LEFT
%% bound is the delimiter's own, and the extent it contributes -- so the two
%% engines diff line for line.
#(define (probe-delim grob)
   (let* ((elts (ly:grob-object grob 'elements))
          (n (if (ly:grob-array? elts) (ly:grob-array-length elts) -1))
          (common (ly:grob-common-refpoint-of-array grob elts Y)))
     (ly:warning "PROBE delimiter with ~a element(s)" n)
     (let loop ((i 0) (ext '(+inf.0 . -inf.0)))
       (if (< i n)
           (let* ((e (ly:grob-array-ref elts i))
                  (same (equal? (ly:spanner-bound e LEFT)
                                (ly:spanner-bound grob LEFT)))
                  (dims (ly:grob-extent e common Y)))
             (ly:warning "PROBE   elt ~a same-bound ~a ext ~a"
                         (grob::name e) same dims)
             (loop (1+ i)
                   (if (and same (not (interval-empty? dims)))
                       (interval-union ext dims)
                       ext)))
           (ly:warning "PROBE   united ext ~a  len ~a"
                       ext
                       (if (interval-empty? ext) 'EMPTY (interval-length ext)))))))

\score {
  \new PianoStaff <<
    \new Staff { c'1 }
    \new Staff { \clef bass c1 }
  >>
  \layout {
    \context {
      \Score
      \override SystemStartBrace.after-line-breaking = #probe-delim
    }
  }
}
