// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// Serialises every test class in THIS assembly that touches the engine's
/// process-global state, exactly as
/// <c>CodeBrix.LilyPort.Engine.Tests.EngineGlobalStateCollection</c> does for that one.
/// <para>
/// ⚠ A COLLECTION DEFINITION IS RESOLVED PER ASSEMBLY. Forty-seven classes here already
/// carried <c>[Collection("engine-global-state")]</c>, but the only
/// <c>[CollectionDefinition]</c> for that name lived in the Engine test assembly, and
/// xUnit does not look across assemblies for one. What it built instead was an UNTYPED
/// collection with the DEFAULT behaviour -- which is to run in parallel with everything
/// else. The attributes read as though the fence were up while it was not.
/// </para>
/// <para>
/// Measured on 2026-08-31 by reading xUnit's own resolved state at run time rather than
/// inferring it: here the collection reported
/// <c>CollectionDefinition=&lt;null&gt;, DisableParallelization=False</c>, and over one
/// run there were 761 moments when a member of it was executing alongside a
/// non-member -- OutputFileNamingEndToEndTests against the ABC, convert-ly and MusicXML
/// parity suites, among others. The same probe against the Engine assembly, where the
/// definition exists, reported <c>DisableParallelization=True</c> and ZERO such moments
/// while still peaking at 24 concurrent tests. This file is the difference between
/// those two numbers.
/// </para>
/// <para>
/// The name is spelled as a literal because that is how the forty-seven classes spell
/// it; <see cref="Name"/> is here for anything written after this file.
/// </para>
/// </summary>
[CollectionDefinition("engine-global-state", DisableParallelization = true)]
public sealed class EngineGlobalStateCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "engine-global-state";
}
