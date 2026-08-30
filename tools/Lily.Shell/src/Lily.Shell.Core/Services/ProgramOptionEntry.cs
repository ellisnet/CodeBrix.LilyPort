// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Lily.Shell.Services;

/// <summary>One program option, as the <c>set</c> command reports it.</summary>
public sealed class ProgramOptionEntry
{
    /// <summary>Creates the entry.</summary>
    /// <param name="name">The option's name.</param>
    /// <param name="value">Its current value, written the way Scheme writes it.</param>
    /// <param name="documentation">What <c>ly:add-option</c> declared it for.</param>
    public ProgramOptionEntry(string name, string value, string documentation)
    {
        Name = name;
        Value = value;
        Documentation = documentation;
    }

    /// <summary>The option's name, without any <c>no-</c> prefix.</summary>
    public string Name { get; }

    /// <summary>
    /// Its current value, written with <c>write</c> conventions — so a string keeps its
    /// quotes and <c>#t</c> is not confusable with the symbol <c>t</c>.
    /// </summary>
    public string Value { get; }

    /// <summary>The documentation string the engine declared the option with.</summary>
    public string Documentation { get; }
}
