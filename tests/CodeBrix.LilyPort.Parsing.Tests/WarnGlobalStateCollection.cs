// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// Serialises the test classes that redirect or record <c>Warn</c>'s output.
/// <para>
/// <c>Warn.RecordMessages</c> and the recorded-message list are process-global, and a
/// class that captures owns BOTH for as long as it runs: it turns recording on, calls
/// <c>ClearMessages</c>, points <c>Warn.Output</c> at <see cref="System.IO.TextWriter.Null"/>,
/// and then puts all three back. Every one of those is visible to anything running
/// beside it -- the clear discards what another capture had collected, and the null
/// writer swallows diagnostics that were meant for the console.
/// </para>
/// <para>
/// The exposure here is real but narrower than in the Engine assembly, and worth
/// recording honestly: the capture in RuleActionRag2Tests asserts with
/// <c>Messages.Any(...)</c> rather than by index, so an extra message does not break it,
/// and no other class in this assembly reads <c>Warn</c>. What the measurement DID show
/// on 2026-08-31 is that the capture is not running alone -- 121 moments in one run
/// alongside sibling RuleActionRag classes, which drive the parser and can therefore
/// raise real diagnostics into the list it has switched on. The fence makes the
/// invariant the capture already assumes into one the runner enforces.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WarnGlobalStateCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "warn-global-state";
}
