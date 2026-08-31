// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Xunit;

namespace CodeBrix.LilyPort.Flower.Tests;

/// <summary>
/// Serialises the test classes that redirect or record <see cref="Warn"/>'s output.
/// <para>
/// <c>Warn.Output</c>, <c>Warn.RecordMessages</c> and the recorded-message list are
/// process-global. A class that turns recording ON owns that list for as long as it
/// runs, and it is asserting POSITIONALLY -- <c>Messages[0]</c>, <c>Messages.Count</c> --
/// so one diagnostic raised by anything running beside it shifts every index and the
/// assertion reads the wrong element. Suppressing output for the duration
/// (<c>Warn.Output = TextWriter.Null</c>) has the mirror-image effect on whatever else
/// was writing.
/// </para>
/// <para>
/// This is the same defect that took CommandLineOptionsTests in the Engine assembly:
/// there, SimpleSpacer's "ignoring weird minimum distance" arrived inside another
/// class's capture window. Measured here on 2026-08-31, WarnTests ran alongside another
/// class at 100 separate moments in one run -- BezierTests, PolynomialTests,
/// RationalTests and the rest. None of those has been seen to raise a diagnostic, so
/// this is a fence over a hazard rather than a fix for an observed failure; it costs
/// one assembly's worth of parallelism in a suite that runs in a fifth of a second.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WarnGlobalStateCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "warn-global-state";
}
