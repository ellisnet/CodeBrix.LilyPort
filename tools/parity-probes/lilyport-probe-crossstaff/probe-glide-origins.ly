\version "2.25.19"

%% Probe: what origin does each \glide post-event carry, and are two \glide
%% events on ONE note distinguishable by it?  finger-key-glide partitions the
%% glide stream events by (equal? origin origin), so two glides sharing one
%% origin make the first partition swallow both and the second call (car '()).
#(define (loc->string o)
   (if (ly:input-location? o)
       (let ((flcc (ly:input-file-line-char-column o)))
         (ly:format "~a:~a:~a" (cadr flcc) (caddr flcc) (cadddr flcc)))
       (ly:format "NOT-A-LOCATION ~a" o)))

#(define (probe-note m)
   (if (memq 'note-event (ly:music-property m 'types))
       (let ((arts (ly:music-property m 'articulations)))
         (ly:warning "PROBE note with ~a articulation(s)" (length arts))
         (for-each
          (lambda (a)
            (ly:warning "PROBE   art ~a origin ~a"
                        (ly:music-property a 'name)
                        (loc->string (ly:music-property a 'origin))))
          arts)))
   m)

probe = #(define-music-function (m) (ly:music?) (music-map probe-note m))

\score {
  \probe {
    b \glide ^1 \glide _\finger \markup \bold "3"
  }
}
