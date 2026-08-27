\version "2.19.21"

%% PARITY 22 probe 4 -- how many broken siblings does `offsetter' SEE?
%%
%% scm/music-functions.scm's `offsetter' distributes one offset per broken sibling
%% only when (>= total-found 2); below that it applies (car offsets) to every piece.
%% `total-found' is (length (ly:spanner-broken-into (ly:grob-original grob))).  This
%% prints exactly that number, from a `staff-padding' callback on the same grob and
%% at the same moment `offsetter' would run, and it prints the grob too so the two
%% pieces of a broken spanner are distinguishable.  Runs unchanged on both engines.

#(define (probe-staff-padding grob)
   (let* ((orig (ly:grob-original grob))
          (sibs (if (ly:spanner? grob) (ly:spanner-broken-into orig) '())))
     (ly:warning "OFFSETPROBE same-as-orig=~a siblings=~a has-system=~a"
                 (eq? orig grob)
                 (length sibs)
                 (ly:grob? (ly:grob-system grob)))
     0.5))

\layout {
  ragged-right = ##t
  indent = 0
}

\relative {
  c'4\startTextSpan d e f\stopTextSpan
  \once \override TextSpanner.staff-padding = #probe-staff-padding
  c4\startTextSpan d e f
  \break
  c4 d e f\stopTextSpan
  \bar "||"
}
