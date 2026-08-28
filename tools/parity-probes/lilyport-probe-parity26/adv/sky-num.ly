\version "2.21.0"
%% Prints a TextScript's own vertical skyline NUMERICALLY, at full precision, on
%% both engines.  The -ddebug-skylines DRAWING rounds to four decimals, which is
%% exactly the precision the question is about; this does not.
%% Runs UNCHANGED on both engines and its answer is a list of numbers.
\layout { indent = 0 }
{ \override TextScript.after-line-breaking =
    #(lambda (grob)
       (let* ((pair (ly:grob-property grob 'vertical-skylines))
              (up (car pair))
              (down (cdr pair)))
         (ly:message (format #f "MAXUP ~s" (ly:skyline-max-height up)))
         (ly:message (format #f "MAXUPPOS ~s" (ly:skyline-max-height-position up)))
         (ly:message (format #f "MAXDOWN ~s" (ly:skyline-max-height down)))
         (let loop ((pts (ly:skyline->points up 0)) (i 0))
           (if (and (pair? pts) (< i 400))
               (begin
                 (ly:message (format #f "UP ~s ~s" i (car pts)))
                 (loop (cdr pts) (+ i 1)))))))
  a'' ^\markup "bb" }
