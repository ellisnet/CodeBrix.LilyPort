// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace Lily.Shell.Services;

/// <summary>
/// What <see cref="LilyPortHost.DisplayMusicAsync"/> made of a music expression: what
/// the parser said about it, and why nothing was displayed when nothing was.
/// </summary>
public sealed class MusicDisplayOutcome
{
    /// <summary>Creates the outcome.</summary>
    /// <param name="diagnostics">What the parser reported, in order.</param>
    /// <param name="error">Why nothing was displayed, or null when it was.</param>
    public MusicDisplayOutcome(IReadOnlyList<string> diagnostics, string error)
    {
        Diagnostics = diagnostics ?? [];
        Error = error;
    }

    /// <summary>What the parser reported while reading the expression.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>
    /// Why nothing was displayed, or <see langword="null"/> when the displayer ran.
    /// </summary>
    /// <remarks>
    /// The displayers WRITE, they do not return text: their output goes to the
    /// interpreter's current output port, which is the terminal. So a successful
    /// display leaves nothing here to print.
    /// </remarks>
    public string Error { get; }
}
