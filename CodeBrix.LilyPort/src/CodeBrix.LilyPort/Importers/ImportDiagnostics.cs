// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Text;

namespace CodeBrix.LilyPort.Importers;

/// <summary>
/// Stands in for the standard error stream the converters write to.
/// </summary>
/// <remarks>
/// Upstream writes a message in as many pieces as it likes — <c>lily_key</c> takes
/// three <c>sys.stderr.write</c> calls to say one thing — so a sink that made one
/// message per call would shred it. This buffers instead and cuts on the newlines the
/// scripts themselves write, which is exactly what a reader of that stream would see.
/// <para>
/// One instance per import: process-wide message state corrupts under parallel test
/// classes, and an importer must be safe to call from two threads at once.
/// </para>
/// </remarks>
internal sealed class ImportDiagnostics
{
    private readonly List<string> _messages = new List<string>();
    private readonly StringBuilder _pending = new StringBuilder();

    /// <summary>Gets how many errors were reported.</summary>
    internal int Errors { get; private set; }

    /// <summary>python's <c>sys.stderr.write</c>.</summary>
    /// <param name="text">The text written.</param>
    internal void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (char c in text)
        {
            if (c == '\n')
            {
                _messages.Add(_pending.ToString());
                _pending.Clear();
            }
            else
            {
                _pending.Append(c);
            }
        }
    }

    /// <summary>Counts an error, without writing anything of its own.</summary>
    internal void CountError() => Errors++;

    /// <summary>
    /// Finishes the stream, giving up whatever was written without a closing newline.
    /// </summary>
    /// <returns>Every message, in the order it was written.</returns>
    internal IReadOnlyList<string> Close()
    {
        if (_pending.Length > 0)
        {
            _messages.Add(_pending.ToString());
            _pending.Clear();
        }

        return _messages;
    }
}
