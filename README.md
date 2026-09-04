# CodeBrix.LilyPort

A managed, cross-platform music engraving engine for .NET: LilyPond `.ly` notation source in - a
file or a string - and SVG pages plus Standard MIDI Files out, entirely in process, with no native
libraries and no external program to install. CodeBrix.LilyPort is provided as a .NET 10 library and
associated `CodeBrix.LilyPort.GplLicenseForever` NuGet package.

CodeBrix.LilyPort supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.LilyPort.GplLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.LilyPort`:

* NuGet package ID: `CodeBrix.LilyPort.GplLicenseForever`
* Assembly and primary namespace: `CodeBrix.LilyPort` - i.e. `using CodeBrix.LilyPort;`

The package requires license acceptance when it is installed, and it is a GPL-3.0-only package - read
the License section below before referencing it.

XML documentation (IntelliSense) ships alongside the assemblies.

One package, five assemblies. Installing the package puts all five in the output directory, and there
is nothing else to reference:

* `CodeBrix.LilyPort` - the facade: `BatchRunner`, `LilyPondInit`, `LilyPortEngraver`, `LilyPortPerformer`, `LilyPortInfo`, the `ConvertLy` and `Importers` namespaces
* `CodeBrix.LilyPort.Engine` - the engine: music, contexts, engravers, grobs, layout, fonts, MIDI, and the embedded Scheme layer
* `CodeBrix.LilyPort.Backends` - `SvgBackend`
* `CodeBrix.LilyPort.Parsing` - the lexer and parser (`LilyParserSession`)
* `CodeBrix.LilyPort.Flower` - the utility layer: `Warn` and the diagnostics, `Rational`, `Interval`, `Offset`

The four sub-assemblies are bundled inside the one package rather than published separately, so a
single `PackageReference` gives a project all five compile-time references.

The package pulls in the following automatically; no version pinning is needed in the consuming project:

* `CodeBrix.LilyScheme.LgplLicenseForever` - the Scheme interpreter the engine's Scheme layer runs on. Note that this package is licensed under the LGPL rather than the GPL, and its namespaces appear in this API wherever the engine hands you a Scheme value or asks for the interpreter.

## CodeBrix.LilyPort supports:

* Engraving `.ly` notation source - a file on disk or an in-memory string - to SVG pages
* Standard MIDI File output, one file per performance
* The whole engraving pipeline in process: parser, contexts, engravers, grobs, spacing, line breaking and page breaking
* Multi-book and multi-page documents, written under the notation program's own output file naming rules
* Point-and-click anchors in the SVG, for an editor's click-to-source, switchable per run
* Per-run `-d` program options, including accumulating `include-settings` house-style files
* `convert-ly` in process: bringing an older document up to current syntax, with the rule list and the version comparisons exposed
* ABC, MIDI and MusicXML import to notation source, each with its own options class
* Embedded music fonts and text fonts - no fontconfig, no system font fallback, nothing to install
* A direct surface for one music expression at a time: `LilyPortEngraver`, `LilyPortPerformer` and `SvgBackend` over a parsed `MusicObject`
* Parse-only use for an editor's syntax check, with diagnostics carrying file, line and column
* A per-run message writer and cooperative cancellation
* An on-disk boot cache, so every process after the first one starts quickly
* Many more...

## Requirements

* Windows, Linux or macOS. There are no native libraries, no fontconfig and no external notation program to install: the music fonts, the text fonts and the Scheme layer are embedded resources inside the assemblies.
* The engine is process-global and long-lived by design. Boot it once and engrave many files through it, rather than starting a process per file; calls through `BatchRunner` are serialised for you. Read the `AGENT-README.txt` section on startup, lifetime and threading before writing a host.

## Sample Code

### Engrave notation source to SVG and MIDI

```csharp
using System;
using System.IO;
using CodeBrix.LilyPort;

string source = "\\relative { c'4 d e f | g1 }\n";
string outputDirectory = Path.Combine(Path.GetTempPath(), "lilyport-out");

BatchRunResult result = BatchRunner.RunText(
    source,            // the notation source text
    "first",           // output base name (no extension)
    null,              // directory that \include resolves against
    outputDirectory);  // where the .svg (and .midi) files land

foreach (string line in result.Diagnostics)
{
    Console.Error.WriteLine(line);
}

if (result.ErrorCount == 0 && result.SvgPath != null)
{
    Console.WriteLine("wrote " + result.SvgPath);        // .../first.svg
}
```

The first call boots the engine, which takes a while; every later call in the same process is quick.
The run writes files rather than returning strings - one SVG per page in `result.SvgPaths` and one
MIDI file per performance in `result.MidiPaths`. A document that carries no `\version` statement, as
this one does not, is engraved anyway and reports a warning in `result.Diagnostics`; `ErrorCount`
stays at zero.

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

The Scheme interpreter is a separate package, `CodeBrix.LilyScheme.LgplLicenseForever`; read that package's own `AGENT-README.txt` for the interpreter itself.

Additional sample code and usage examples are available in the `CodeBrix.LilyPort.Tests` project:
https://github.com/ellisnet/CodeBrix.LilyPort/tree/main/tests/CodeBrix.LilyPort.Tests

The repository also carries maintainer documentation - building, the test suites, the regression
harness and every tool in the tree - which is not part of the NuGet package. It is indexed by
[README-INDEX.txt](https://github.com/ellisnet/CodeBrix.LilyPort/blob/main/README-INDEX.txt).

## License

CodeBrix.LilyPort is licensed under the GNU General Public License version 3 only (GPL-3.0-only) - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.LilyPort/blob/main/LICENSE) file.

GPL-3.0-**only**, deliberately not "or later". The package includes material that is licensed under
GPL version 3 only; GPL-3-only and GPL-3-or-later material combine legally, but the combined work can
then be conveyed only under GPL version 3 exactly. The repository's own source files remain
GPL-3.0-or-later - both statements are true at once. Referencing this package makes the consuming
application a GPL-3.0 work when it is distributed; decide that before writing code against it.

[LICENSE.OFL](https://github.com/ellisnet/CodeBrix.LilyPort/blob/main/LICENSE.OFL) covers the
Emmentaler and Feta music fonts, which are embedded in the engine assembly under the SIL Open Font
License 1.1. `COPYING.FDL` covers the documentation sources mirrored in the repository under
`Documentation/`, under the GNU Free Documentation License 1.3; those sources are not part of the
NuGet package.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.LilyPort/blob/main/THIRD-PARTY-NOTICES.txt).
