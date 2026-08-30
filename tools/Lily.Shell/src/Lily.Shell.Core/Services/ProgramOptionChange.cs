// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace Lily.Shell.Services;

/// <summary>What one <c>set</c> did to the option table.</summary>
/// <remarks>
/// The value BEFORE is worth reporting because a good many of these options are
/// booleans that are already on: telling the user the value is now <c>#t</c> says
/// nothing about whether anything changed, and an option that refused the value it was
/// given (a wrong type, an accumulative option set rather than appended) reports the
/// same <c>#t</c> as one that took it.
/// </remarks>
public sealed class ProgramOptionChange
{
    /// <summary>Creates the record.</summary>
    /// <param name="name">The option actually affected, after any <c>no-</c> prefix.</param>
    /// <param name="before">Its value before, written the way Scheme writes it.</param>
    /// <param name="after">Its value after.</param>
    /// <param name="warnings">What the engine said while applying it.</param>
    public ProgramOptionChange(
        string name, string before, string after, IReadOnlyList<string> warnings)
    {
        Name = name;
        Before = before;
        After = after;
        Warnings = warnings ?? [];
    }

    /// <summary>The option affected, with any <c>no-</c> prefix already stripped.</summary>
    public string Name { get; }

    /// <summary>The value before the setting was applied.</summary>
    public string Before { get; }

    /// <summary>The value after it.</summary>
    public string After { get; }

    /// <summary>What the engine warned about, in order.</summary>
    public IReadOnlyList<string> Warnings { get; }
}
