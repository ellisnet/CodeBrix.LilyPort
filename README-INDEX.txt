================================================================================
README-INDEX: CodeBrix.LilyPort
Map of the README files in this repository
================================================================================

If you are an AI coding agent: find the NuGet package you are consuming below and
read its AGENT-README file in full. Read MAINTAINER-README.txt only if you are
changing this repository itself.

AGENT-README FILES (consumer documentation, one per NuGet package)
------------------------------------------------------------------
  AGENT-README.txt
      CodeBrix.LilyPort.GplLicenseForever — a managed, cross-platform music
      engraving engine for .NET 10, ported from GNU LilyPond 2.27.2: parser,
      Scheme layer (on CodeBrix.LilyScheme), engravers, page layout, embedded
      music fonts, SVG and MIDI output, convert-ly, and ABC / MIDI / MusicXML
      import. One package bundling five assemblies; GPL-3.0-only.

MAINTAINER AND EXTRAS
---------------------
  MAINTAINER-README.txt
      Building, testing (the five xunit suites, the regression harness with its
      ratchet manifest and the LilyPond 2.27.2 oracle, the docs comparison),
      packaging (sub-assembly bundling, the LilyScheme dependency rule,
      versioning), provenance of the vendored and ported LilyPond material,
      and coding conventions for maintainers.
  EXTRAS-README.txt
      The tools and other non-package content in this repository: the
      regression harness and parity probes, the importer/converter probes,
      Lily.Docs (renders LilyPond's manuals through the port), Lily.Shell (the
      CodeBrix.Platform engine shell), the font build, and the mirrored
      LilyPond documentation, Metafont and parser sources.

GENERAL
-------
  README.md
      Human-facing overview shown on GitHub and nuget.org.
  README-INDEX.txt
      This file.
