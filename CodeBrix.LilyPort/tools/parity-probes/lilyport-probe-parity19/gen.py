base = r'''\version "2.19.16"
#(set-global-staff-size 5)
\score {
  <<
    \context Staff = "s1" \with {
      %S1%
    } {
      s1 \bar ":|."
    }
    \context Staff = "s2" \with {
      %S2%
    } {
      s1 \bar ":|."
    }
    \context Staff = "s3" {
      s1 \bar ":|."
    }
  >>
}
'''
variants = {
 'probe-rs-v0': ('\\override StaffSymbol.staff-space = #0.7', "\\override StaffSymbol.line-positions = #'(-4 -2 0 2)"),
 'probe-rs-v1': ('', "\\override StaffSymbol.line-positions = #'(-4 -2 0 2)"),
 'probe-rs-v2': ('\\override StaffSymbol.staff-space = #0.7', ''),
 'probe-rs-v3': ('', ''),
}
for name,(s1,s2) in variants.items():
    open(name+'.ly','w').write(base.replace('%S1%', s1).replace('%S2%', s2))
print("written")
