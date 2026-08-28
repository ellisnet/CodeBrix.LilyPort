// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <c>check_value_type</c> and the rest of <c>ly_set_option</c>'s sequence
/// (<c>lily/program-option-scheme.cc:355-390</c>, <c>:455-519</c>).
/// <para>
/// Every expectation here was READ OFF THE PINNED ORACLE before a line of it was
/// written — <c>lilypond -dbackend=svg</c> over a file that sets the same options the
/// same ways — and each claim is PAIRED WITH A CONTROL that must come out differently,
/// because "the value did not change" is also what a binding that never ran produces.
/// The doubled quotation marks are upstream's own and not a typo: it renders the value
/// with <c>scm_object_to_string</c>, which WRITES it, then wraps that in quotes of its
/// own, so a string value reports <c>-dopt=""abc""</c>.
/// </para>
/// <para>
/// The options are declared here rather than taken from <c>scm/lily.scm</c>'s table so
/// that the class costs no boot; the END-TO-END half, which proves the real table
/// carries its declared types, is <c>ProgramOptionTypeCheckEndToEndTests</c> in the
/// facade's suite.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class ProgramOptionTypeCheckTests
{
    /// <summary>What one probe run saw: the last value written, and every diagnostic.</summary>
    private sealed class Probe
    {
        /// <summary>Gets or sets the written form of the last expression's value.</summary>
        public string Result { get; set; }

        /// <summary>Gets or sets the diagnostics the run produced, in order.</summary>
        public IReadOnlyList<string> Warnings { get; set; }
    }

    /// <summary>
    /// Boots an engine interpreter — primitives and stubs, no scm layer, and a fresh
    /// option store per boot — evaluates every source in turn and reports the last value
    /// with the diagnostics it produced.
    /// </summary>
    /// <param name="sources">The expressions to evaluate, in order.</param>
    /// <returns>The run's result and diagnostics.</returns>
    private static Probe Eval(params string[] sources)
    {
        Probe probe = new Probe();

        Interpreter ambientBefore = LilyPondScheme.Current;
        bool recordedBefore = Warn.RecordMessages;
        Warn.ClearMessages();
        Warn.RecordMessages = true;

        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                foreach (string source in sources)
                {
                    probe.Result = Printer.Write(interpreter.EvalString(source, "<test>"));
                }
            });

            probe.Warnings = Warn.Messages.ToList();
        }
        finally
        {
            Warn.RecordMessages = recordedBefore;
            Warn.ClearMessages();
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        return probe;
    }

    [Fact]
    public void a_predicate_typed_option_refuses_a_wrong_value_and_keeps_the_one_it_had()
    {
        //Arrange
        // Upstream's own `resolution' is `#:type ,positive-number?'; number? is the same
        // SHAPE (a predicate procedure) out of Guile, so the class needs no scm layer.
        //Act
        Probe refused = Eval(
            "(ly:add-option 'probe-resolution 101 \"Set resolution.\" #:type number?)",
            "(ly:set-option 'probe-resolution \"abc\")",
            "(ly:get-option 'probe-resolution)");

        // THE CONTROL: a value the same predicate accepts must be stored, or "101" below
        // would also be what a set that never happened looks like.
        Probe accepted = Eval(
            "(ly:add-option 'probe-resolution 101 \"Set resolution.\" #:type number?)",
            "(ly:set-option 'probe-resolution 300)",
            "(ly:get-option 'probe-resolution)");

        //Assert
        refused.Result.Should().Be("101");
        refused.Warnings.Should().ContainSingle()
            .Which.Should().Be(
                "warning: ignoring option -dprobe-resolution=\"\"abc\"\": value has wrong type");

        accepted.Result.Should().Be("300");
        accepted.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void a_list_typed_option_refuses_a_value_outside_the_list_and_names_the_list()
    {
        //Arrange & Act
        Probe refused = Eval(
            "(ly:add-option 'probe-backend 'svg \"Select backend.\" #:type '(ps cairo svg))",
            "(ly:set-option 'probe-backend 'nosuchbackend)",
            "(ly:get-option 'probe-backend)");

        // THE CONTROL: a member of the very same list goes in.
        Probe accepted = Eval(
            "(ly:add-option 'probe-backend 'svg \"Select backend.\" #:type '(ps cairo svg))",
            "(ly:set-option 'probe-backend 'ps)",
            "(ly:get-option 'probe-backend)");

        //Assert
        refused.Result.Should().Be("svg");
        refused.Warnings.Should().ContainSingle()
            .Which.Should().Be(
                "warning: ignoring option -dprobe-backend=\"nosuchbackend\":\n"
                + "  invalid value; possible values are (ps cairo svg)");

        accepted.Result.Should().Be("ps");
        accepted.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void an_option_declared_with_no_type_is_a_boolean_option()
    {
        //Arrange
        // The half that decides whether check_value_type does anything at all for most
        // of the table: upstream stores the boolean? predicate for an absent #:type
        // (program-option-scheme.cc:281-282) rather than leaving the property unset.
        //Act
        Probe refused = Eval(
            "(ly:add-option 'probe-flag #f \"For internal use.\")",
            "(ly:set-option 'probe-flag 'yes)",
            "(ly:get-option 'probe-flag)");

        // THE CONTROL: a boolean is what it wants, and goes in.
        Probe accepted = Eval(
            "(ly:add-option 'probe-flag #f \"For internal use.\")",
            "(ly:set-option 'probe-flag #t)",
            "(ly:get-option 'probe-flag)");

        //Assert
        refused.Result.Should().Be("#f");
        refused.Warnings.Should().ContainSingle()
            .Which.Should().Be(
                "warning: ignoring option -dprobe-flag=\"yes\": value has wrong type");

        accepted.Result.Should().Be("#t");
        accepted.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void a_type_symbol_describes_reading_and_checks_no_value()
    {
        //Arrange
        // string / string-or-boolean / string-or-false say how to read -d TEXT, not what
        // a value may be, so check_value_type passes everything for them. Measured on the
        // oracle, whose `font-export-dir' is #:type string-or-false: setting it to
        // "somewhere" produces no diagnostic at all.
        //Act
        Probe typed = Eval(
            "(ly:add-option 'probe-file #f \"Directory for exporting fonts.\""
            + " #:type 'string-or-false)",
            "(ly:set-option 'probe-file \"somewhere\")",
            "(ly:get-option 'probe-file)");

        // THE CONTROL: the same string, into an option whose type is a PREDICATE, is
        // refused — so the pass above is the type symbol's doing and not a check that
        // never ran.
        Probe predicateTyped = Eval(
            "(ly:add-option 'probe-file #f \"Directory for exporting fonts.\")",
            "(ly:set-option 'probe-file \"somewhere\")",
            "(ly:get-option 'probe-file)");

        //Assert
        typed.Result.Should().Be("\"somewhere\"");
        typed.Warnings.Should().BeEmpty();

        predicateTyped.Result.Should().Be("#f");
        predicateTyped.Warnings.Should().ContainSingle();
    }

    [Fact]
    public void an_undeclared_option_is_warned_about_and_set_anyway()
    {
        //Arrange
        // Measured: the oracle warns and then answers #t for (ly:get-option
        // 'nosuchoption). It warns the same way for Frescobaldi's undeclared
        // -ddebug-voices, which is how that has always behaved upstream.
        //Act
        Probe unknown = Eval(
            "(ly:set-option 'probe-unknown #t)",
            "(ly:get-option 'probe-unknown)");

        // THE CONTROL: a declared name earns no such diagnostic.
        Probe known = Eval(
            "(ly:add-option 'probe-unknown #f \"For internal use.\")",
            "(ly:set-option 'probe-unknown #t)",
            "(ly:get-option 'probe-unknown)");

        //Assert
        unknown.Result.Should().Be("#t");
        unknown.Warnings.Should().ContainSingle()
            .Which.Should().Be("warning: no such program option: probe-unknown");

        known.Result.Should().Be("#t");
        known.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void the_no_prefix_negates_exactly_true_and_nothing_else()
    {
        //Arrange
        // from_scm<bool> defaults its fallback to false, which makes it
        // scm_is_eq (s, SCM_BOOL_T) — so `no-' applied to a NON-boolean yields #t, not
        // #f. MEASURED: the oracle reports `ignoring option -dresolution="#t"' for
        // (ly:set-option 'no-resolution 5). //was previously: the port computed
        // scm_is_true here and produced #f, which a number-typed option would have
        // accepted in silence.
        //Act
        Probe negated = Eval(
            "(ly:add-option 'probe-resolution 101 \"Set resolution.\" #:type number?)",
            "(ly:set-option 'no-probe-resolution 5)",
            "(ly:get-option 'probe-resolution)");

        // THE CONTROL, and the case both readings agree on: `no-' over a boolean option
        // stores the negation, so the prefix is demonstrably working in the case above
        // rather than being ignored.
        Probe flag = Eval(
            "(ly:add-option 'probe-flag #t \"For internal use.\")",
            "(ly:set-option 'no-probe-flag)",
            "(ly:get-option 'probe-flag)");

        //Assert
        negated.Result.Should().Be("101");
        negated.Warnings.Should().ContainSingle()
            .Which.Should().Be(
                "warning: ignoring option -dprobe-resolution=\"#t\": value has wrong type");

        flag.Result.Should().Be("#f");
        flag.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void append_to_an_undeclared_option_earns_one_diagnostic_and_not_two()
    {
        //Arrange
        // Upstream looks the handle up FIRST and returns (:501-508), so the accumulative
        // complaint never reaches an undeclared name. Measured on the oracle, which
        // prints exactly one line for it. //was previously: the port asked
        // IsAccumulative first and printed both.
        //Act
        Probe unknown = Eval("(ly:append-to-option 'probe-unknown 1)");

        // THE CONTROL: a DECLARED but non-accumulative option earns the other diagnostic
        // AND still gets the value consed on — upstream's own improper-list outcome.
        Probe declared = Eval(
            "(ly:add-option 'probe-resolution 101 \"Set resolution.\" #:type number?)",
            "(ly:append-to-option 'probe-resolution 7)",
            "(ly:get-option 'probe-resolution)");

        //Assert
        unknown.Warnings.Should().ContainSingle()
            .Which.Should().Be("warning: no such program option: probe-unknown");

        declared.Result.Should().Be("(7 . 101)");
        declared.Warnings.Should().ContainSingle()
            .Which.Should().Be(
                "warning: option probe-resolution is not accumulative; use ly:set-option"
                + " instead of ly:add-to-option");
    }

    [Fact]
    public void append_checks_the_value_type_as_well()
    {
        //Arrange
        // The oracle's own reading: (ly:append-to-option 'resolution "x") prints BOTH the
        // accumulative complaint and the wrong-type refusal, and leaves the value alone.
        //Act
        Probe refused = Eval(
            "(ly:add-option 'probe-resolution 101 \"Set resolution.\" #:type number?)",
            "(ly:append-to-option 'probe-resolution \"x\")",
            "(ly:get-option 'probe-resolution)");

        // THE CONTROL is the accepted append in the test above: same call, same option,
        // a value the predicate likes, and the value DOES change. Here it must not.
        //Assert
        refused.Result.Should().Be("101");
        refused.Warnings.Should().HaveCount(2);
        refused.Warnings[1].Should().Be(
            "warning: ignoring option -dprobe-resolution=\"\"x\"\": value has wrong type");
    }

    [Fact]
    public void an_accumulative_option_gathers_values_and_refuses_ly_set_option()
    {
        //Arrange
        // The fourth diagnostic in this family, and the one that had been going to the
        // store's own bare sink rather than through `warning' — so it could not be seen
        // by the diagnostics gate and -dwarning-as-error could not act on it. Measured:
        // the oracle prints it with the `warning: ' tag like every other one here.
        //Act
        Probe gathered = Eval(
            "(ly:add-option 'probe-settings '() \"Include file.\""
            + " #:type 'string #:accumulative? #t)",
            "(ly:append-to-option 'probe-settings \"aaa\")",
            "(ly:append-to-option 'probe-settings \"bbb\")",
            "(ly:get-option 'probe-settings)");

        Probe refused = Eval(
            "(ly:add-option 'probe-settings '() \"Include file.\""
            + " #:type 'string #:accumulative? #t)",
            "(ly:set-option 'probe-settings \"aaa\")",
            "(ly:get-option 'probe-settings)");

        //Assert
        gathered.Result.Should().Be("(\"aaa\" \"bbb\")");
        gathered.Warnings.Should().BeEmpty();

        refused.Result.Should().Be("()");
        refused.Warnings.Should().ContainSingle()
            .Which.Should().Be(
                "warning: option probe-settings is accumulative; use ly:append-to-option"
                + " instead of ly:set-option");
    }
}
