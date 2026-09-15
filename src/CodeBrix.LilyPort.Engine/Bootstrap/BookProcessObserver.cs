// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The one place a host is told that a book went through <c>ly:book-process</c> or
/// <c>ly:book-process-to-systems</c> — the two entry points UPSTREAM writes output from.
/// </summary>
/// <remarks>
/// <para>
/// THIS EXISTS BECAUSE A DOCUMENT MAY INSTALL ITS OWN TOPLEVEL BOOK HANDLER, and
/// <c>ly/init.ly</c> honours it. <c>ly/lilypond-book-preamble.ly</c> is the one every
/// lilypond-book document includes, and it points
/// <c>default-toplevel-book-handler</c> at <c>print-book-with-defaults-as-systems</c> and
/// <c>toplevel-book-handler</c> at <c>print-book-with-defaults</c>. Both routes reach
/// <see cref="PageBreakingCallbacks"/>'s two primitives instead of the batch runner's own
/// collector, and upstream's next step —
/// <c>Paper_book::output</c> / <c>Paper_book::classic_output</c> — is where the FILES get
/// written. The port writes its files in the caller instead, so without this hook the
/// port computed those books and DISCARDED them: the run reported success with zero
/// errors and wrote nothing at all.
/// </para>
/// <para>
/// The hook carries the book, the paper book it produced, and WHICH of the two entry
/// points was called, because that is the one thing the two differ by: the page path
/// writes one file per PAGE and the systems path one file per SYSTEM.
/// </para>
/// <para>
/// ⚠ IT IS PROCESS-GLOBAL AND MUST BE TREATED AS A LEAK UNTIL PUT BACK. The batch runner
/// installs it for the span of ONE run, under the same lock everything else in a run is
/// serialised by, and restores the previous value in a <c>finally</c>; a session that
/// installs nothing is unaffected, because an unset hook costs one null test.
/// </para>
/// </remarks>
public static class BookProcessObserver
{
    /// <summary>
    /// The callback shape <see cref="Current"/> takes.
    /// </summary>
    /// <param name="book">The book that was processed.</param>
    /// <param name="paperBook">The paper book it produced, never <see langword="null"/>.</param>
    /// <param name="toSystems">
    /// <see langword="true"/> when the caller was <c>ly:book-process-to-systems</c> —
    /// upstream's <c>classic_output</c>, one file per system — and
    /// <see langword="false"/> for <c>ly:book-process</c>, one file per page.
    /// </param>
    public delegate void ProcessedBook(Book book, PaperBook paperBook, bool toSystems);

    /// <summary>
    /// Gets or sets the callback told about every book the two <c>ly:book-process</c>
    /// entry points process, or <see langword="null"/> when nobody is listening.
    /// </summary>
    public static ProcessedBook Current { get; set; }
}
