# CodeBrix.LilyPort

A managed port of [GNU LilyPond](https://lilypond.org) 2.27.2, the music engraving program, to
.NET 10. It renders `.ly` input to SVG and MIDI through ported C++ engine code together with
LilyPond's own vendored Scheme layer, which runs on `CodeBrix.LilyScheme` (a separate,
LGPL-licensed repository and NuGet package).

## The NuGet package

`CodeBrix.LilyPort.GplLicenseForever` — one package that bundles five assemblies
(`CodeBrix.LilyPort` plus `.Engine`, `.Backends`, `.Parsing` and `.Flower`) and depends on
`CodeBrix.LilyScheme.LgplLicenseForever`. It requires .NET 10.

The package is licensed **GPL-3.0-only**, as LilyPond's own `articulate.ly` (which it includes)
is GPL-3-only; the repository's own source files remain GPL-3.0-or-later. Both statements are
true at once — see `THIRD-PARTY-NOTICES.txt` section 2. Referencing the package makes the
consuming application a GPL-3.0 work.

## Documentation

`README-INDEX.txt` maps every documentation file in this repository. Consumers of the package
should read `AGENT-README.txt` (API reference, examples, pitfalls); `MAINTAINER-README.txt`
covers building, testing, packaging and provenance; `EXTRAS-README.txt` covers the tools that
ship in the repository but not in the package (the regression harness, the Lily.Shell engine
shell, the Lily.Docs manual renderer, the importer probes and the font build).

## Verification

The port is verified against a pinned LilyPond 2.27.2 binary used as an oracle. A regression
harness renders the upstream test corpus and compares the result page by page, a committed
ratchet manifest keeps any file from getting worse, and a second comparison runs LilyPond's own
documentation generator and checks its nineteen output files byte for byte. See
`tools/regression-harness/README.txt` for the harness, and
`src/CodeBrix.LilyPort.Engine/PORT-COVERAGE.txt` for every deliberate divergence from upstream
with its reason.

## License

GPL-3.0 — the licence text is in `LICENSE` at the repository root (the package is conveyed
GPL-3.0-only; see above). `LICENSE.OFL` covers the Emmentaler and Feta music fonts, embedded in
the engine assembly under the SIL Open Font License 1.1. `COPYING.FDL` covers the LilyPond
documentation sources mirrored under `Documentation/` (GNU FDL 1.3; the `snippets/` input files
are public domain). Attribution and compliance records for every third-party component are in
`THIRD-PARTY-NOTICES.txt`.
