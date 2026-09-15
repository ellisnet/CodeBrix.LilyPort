// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <see cref="OutputDef.RemoveVariable"/> — Guile's <c>module-remove!</c> over one
/// definition's own scope.
/// </summary>
/// <remarks>
/// <para>
/// Upstream has no such call and needs none: every <c>Output_def</c> dies with the
/// process that made it, one per input file. A BATCH runner has to put a shared
/// <c>$defaultpaper</c> back to what a fresh process would hand it, and re-setting the
/// variables it started with cannot reach a variable the FILE ADDED —
/// <c>\bookOutputName</c> adds exactly one, so one file's chosen output name renamed
/// every file engraved after it.
/// </para>
/// <para>
/// The two facts are a pair on purpose. Removing must make the variable UNSET rather
/// than set-to-false, because <c>get-current-filename</c> and every other reader
/// distinguishes "never set" from "set to nothing"; and it must reach only the
/// definition's OWN scope, or removing a book's local override would take the paper's
/// default with it.
/// </para>
/// </remarks>
public class OutputDefVariableRemovalTests
{
    [Fact]
    public void removing_a_variable_leaves_it_unset_rather_than_false()
    {
        //Arrange
        OutputDef definition = new OutputDef();
        definition.SetVariable("output-filename", new MutableString("renamed"));

        //Act
        definition.RemoveVariable(Symbol.Intern("output-filename"));

        //Assert
        definition.CVariable("output-filename").Should().BeNull();
        definition.Variables().ContainsKey(Symbol.Intern("output-filename"))
            .Should().BeFalse();
    }

    [Fact]
    public void removing_a_childs_variable_leaves_the_parents_standing()
    {
        //Arrange
        OutputDef parent = new OutputDef();
        parent.SetVariable("output-filename", new MutableString("from-paper"));
        OutputDef child = new OutputDef { Parent = parent };
        child.SetVariable("output-filename", new MutableString("from-book"));

        //Act
        child.RemoveVariable(Symbol.Intern("output-filename"));

        //Assert
        child.CVariable("output-filename").Should().NotBeNull();
        child.CVariable("output-filename").ToString().Should().Be("from-paper");
    }

    [Fact]
    public void removing_a_name_that_was_never_set_is_not_an_error()
    {
        //Arrange
        OutputDef definition = new OutputDef();

        //Act
        Action removing = () => definition.RemoveVariable(Symbol.Intern("never-set"));

        //Assert
        removing.Should().NotThrow();
    }

    [Fact]
    public void removing_nothing_is_an_argument_error()
    {
        //Arrange
        OutputDef definition = new OutputDef();

        //Act
        Action removing = () => definition.RemoveVariable(null);

        //Assert
        removing.Should().Throw<ArgumentNullException>();
    }
}
