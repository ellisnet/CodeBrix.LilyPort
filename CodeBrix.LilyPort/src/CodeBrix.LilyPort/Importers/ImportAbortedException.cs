// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace CodeBrix.LilyPort.Importers;

/// <summary>
/// Raised where upstream's converter would have stopped dead: a python exception it
/// never catches, or the <c>sys.exit</c> its strict mode takes.
/// </summary>
/// <remarks>
/// The converters are scripts, and a script's answer to something it cannot read is to
/// end the process without writing a file. A library cannot do that, so the stop is
/// carried out here and turned into an <see cref="ImportResult"/> with no text — which
/// is the same outcome a caller of the script would have seen on disk.
/// <para>
/// Never public: nothing outside the importers throws or catches it.
/// </para>
/// </remarks>
internal sealed class ImportAbortedException : Exception
{
    internal ImportAbortedException(string message)
        : base(message)
    {
    }

    private ImportAbortedException()
    {
    }

    /// <summary>
    /// Gets whether the reason has already been written to
    /// <see cref="ImportDiagnostics"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ <see cref="Exception.Message"/> CANNOT ANSWER THIS. Passing null gives the
    /// runtime's own "Exception of type ... was thrown", which reads as a message and
    /// would be reported a second time; the strict stop is the case, because
    /// <c>error()</c> has already written the line and counted it.
    /// </remarks>
    internal bool AlreadyReported { get; private init; }

    /// <summary>
    /// The stop <c>--strict</c> takes: upstream's <c>sys.exit(1)</c>, after the message
    /// was written.
    /// </summary>
    /// <returns>The exception.</returns>
    internal static ImportAbortedException Reported()
        => new ImportAbortedException { AlreadyReported = true };

    /// <summary>
    /// The stop an unhandled python exception takes: the interpreter prints a TRACEBACK
    /// and the script ends, writing no file.
    /// </summary>
    /// <param name="reason">
    /// What python would have printed on the traceback's last line, for a reader of this
    /// port's own source; it deliberately reaches no diagnostic.
    /// </param>
    /// <returns>The exception.</returns>
    /// <remarks>
    /// ⚠ Nothing is written to <see cref="ImportDiagnostics"/> for this, and that is the
    /// faithful answer: a traceback carries none of the converter's own diagnostic shape,
    /// so a caller filtering the script's stderr for its messages sees nothing new. What
    /// the caller does see is <see cref="ImportResult.Text"/> answering nothing, which is
    /// exactly what the absent file on disk says.
    /// </remarks>
    internal static ImportAbortedException PythonTraceback(string reason)
        => new ImportAbortedException(reason) { AlreadyReported = true };
}
