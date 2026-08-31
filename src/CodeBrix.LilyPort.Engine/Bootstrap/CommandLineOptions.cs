// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: lily/main.cc + scm/lily.scm;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Reads one <c>-d</c> option the way a <c>lilypond</c> command line does, and applies
/// it to the option store.
/// <para>
/// Upstream splits this across two files. <c>lily/main.cc:576-590</c> takes the text
/// after <c>-d</c>, splits it at the first <c>=</c>, defaults a missing value to the
/// text <c>#t</c>, and stacks the pair for <c>ly:command-line-options</c> to hand to
/// Scheme; <c>scm/lily.scm:497-543</c> then turns each pair's TEXT into a VALUE,
/// branching four ways on the option's declared type, warning on a text it cannot read,
/// and routing the result to <c>ly:append-to-option</c> or <c>ly:set-option</c>. This
/// type carries both halves.
/// </para>
/// <para>
/// ⚠ THE PORT HAS NO COMMAND LINE, AND THE VENDORED SCHEME HALF IS DEAD CODE. The
/// engine is a library in a host's process: <c>ly:command-line-options</c> answers the
/// empty list, so the <c>lily.scm</c> block above never applies anything. It is also
/// the wrong LIFETIME for a host — it runs once, when the Scheme layer loads, where a
/// host needs options that live for ONE run (a preview engrave wants point-and-click
/// anchors, the publish of the same document does not). Rather than reshape vendored
/// Scheme — which would shift the line numbers the documentation gate compares
/// byte-for-byte, for no behaviour that is wanted at load time — the decision table is
/// ported here and driven per run from <c>BatchRunOptions.Options</c>, after the
/// per-file restore that opens every run. The vendored block stays as upstream wrote
/// it.
/// </para>
/// </summary>
public static class CommandLineOptions
{
    /// <summary>
    /// <c>lily.scm:531</c>'s <c>err-regex</c>, verbatim. A read error names the string
    /// port it came from and where in it the reader stopped; upstream drops that and
    /// keeps the text, because the port is one it made itself out of the option's value
    /// and its coordinates mean nothing to whoever typed the option.
    /// </summary>
    private static readonly Regex UnknownPortPrefix =
        new Regex(@"#<unknown port>:\d+:\d+: (.*)$", RegexOptions.Compiled);

    /// <summary>
    /// <c>lily.scm:532</c>'s <c>eof-regex</c>, verbatim: what the reader calls the end of
    /// a FILE is, for a string port, the end of a STRING.
    /// </summary>
    private static readonly Regex EndOfFile = new Regex("end of file$", RegexOptions.Compiled);

    /// <summary>
    /// Applies one <c>-d</c> option, given as the text that FOLLOWS the <c>-d</c>:
    /// <c>debug-voices</c>, <c>no-point-and-click</c>,
    /// <c>include-settings=/path/to/formatter.ily</c>.
    /// </summary>
    /// <param name="options">The option store to apply it to.</param>
    /// <param name="argument">The option text, without its <c>-d</c> prefix.</param>
    /// <remarks>
    /// A null or blank argument is ignored rather than warned about: it is a host
    /// passing an empty entry, not a user mistyping an option.
    /// </remarks>
    public static void Apply(ProgramOptions options, string argument)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            return;
        }

        // main.cc:579-588. The FIRST '=' splits, so a value may contain more of them,
        // and a bare option means the text "#t" rather than the value #t -- the
        // difference matters, because a #:type string option then takes the STRING
        // "#t", exactly as it does upstream.
        int equals = argument.IndexOf('=');
        string key = equals < 0 ? argument : argument.Substring(0, equals);
        string text = equals < 0 ? "#t" : argument.Substring(equals + 1);

        // lily.scm:500. The type is looked up under the key AS WRITTEN -- a `no-'
        // prefixed name is not a declared option, so it takes the unknown-option arm,
        // and the prefix is dealt with later, by SetFromOptionName.
        object value;
        switch (options.ValueSyntaxOf(key))
        {
            case OptionValueSyntax.String:
                value = new MutableString(text);
                break;

            case OptionValueSyntax.StringOrFalse:
                value = text == "#f" ? (object)false : new MutableString(text);
                break;

            case OptionValueSyntax.StringOrBoolean:
                // Type #f means an option this engine has never declared, "probably
                // used privately by the user" -- so #t and #f work as expected and
                // anything else is handled as a string, since we do not know the type.
                value = text == "#t"
                    ? (object)true
                    : text == "#f" ? (object)false : new MutableString(text);
                break;

            default:
                if (!TryRead(text, out value, out string error))
                {
                    // lily.scm:536-540, and upstream's own wording. It warns and
                    // changes NOTHING -- a host that asks for something unreadable is
                    // told, not quietly obeyed with a value nobody chose.
                    Warn.Warning(
                        "Ignoring option -" + "d" + key + "=\"" + text
                        + "\" due to read error: " + error);
                    return;
                }

                break;
        }

        // lily.scm:541-543. An accumulative option GATHERS its values -- this is what
        // lets -dinclude-settings be passed several times -- and everything else is set.
        // Both arms go through the ported PRIMITIVE's own sequence rather than straight
        // at the store, because upstream's loop here calls ly:append-to-option and
        // ly:set-option, so the -d path is type-checked exactly as a document's own call
        // is. //was previously: options.AppendTo, the bare store operation, which
        // skipped the handle check, the accumulative complaint and check_value_type.
        if (options.IsAccumulative(key))
        {
            options.AppendToOption(key, value);
            return;
        }

        options.SetFromOptionName(key, value);
    }

    /// <summary>
    /// Reads the text as one Scheme datum, upstream's
    /// <c>(with-input-from-string str-val read)</c> under a <c>read-error</c> catch.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="value">The datum read.</param>
    /// <param name="error">Why it could not be read.</param>
    /// <returns>Whether a datum was read.</returns>
    /// <remarks>
    /// <c>Read</c> rather than <c>ReadDatum</c>, because upstream's <c>read</c> answers
    /// the EOF OBJECT for text with no datum in it (<c>-dfoo=</c>) and raises only for
    /// text it cannot finish (<c>-dfoo=(1 2</c>) — so an empty value sets the option to
    /// the eof object here exactly as it does upstream, and only the unfinishable text
    /// is warned about. Upstream strips the string port's name, line and column out of
    /// the message with <see cref="UnknownPortPrefix"/> and rewrites "end of file" as
    /// "end of string" with <see cref="EndOfFile"/>, and BOTH are ported here.
    /// <para>
    /// ⚠ THE MESSAGE IS TAKEN FROM <c>ReaderMessage</c>, NOT FROM <c>Message</c>.
    /// Upstream's handler is <c>(lambda (err-key . err-args) (cons #f (second
    /// err-args)))</c> — it keeps the condition's MESSAGE TEXT and nothing else, and it
    /// does not format the text against the condition's arguments, which is why an
    /// unterminated list reports a literal <c>~A</c> on both engines.
    /// <c>SchemeReaderException.ReaderMessage</c> is that text; <c>Message</c> is the
    /// whole <c>SchemeThrow</c> wrapping around it.
    /// </para>
    /// <para>
    /// A NON-read error is still reported through <c>Message</c>. Upstream catches
    /// <c>'read-error</c> alone and lets anything else escape; the port's wider catch
    /// predates this method and is left as it stands, because narrowing it is a
    /// behaviour change of its own and not part of reporting the message correctly.
    /// </para>
    /// </remarks>
    private static bool TryRead(string text, out object value, out string error)
    {
        value = null;
        error = null;

        try
        {
            value = new SchemeReader(text, null).Read();
            return true;
        }
        catch (Exception failure)
        {
            // lily.scm:536-540. //was previously: error = failure.Message; which spliced
            // the reader's position prefix -- and, since the reader became a SchemeThrow,
            // the whole condition -- into a warning upstream reports without either.
            string reported = failure is SchemeReaderException readFailure
                ? readFailure.ReaderMessage
                : failure.Message;

            error = EndOfFile.Replace(
                UnknownPortPrefix.Replace(reported, "$1"), "end of string");
            return false;
        }
    }
}
