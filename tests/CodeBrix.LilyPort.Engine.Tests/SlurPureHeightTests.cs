// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <c>Slur::pure_height</c> and the item pure-height cache it reads through.
/// <para>
/// THE MEASUREMENT THESE FENCE. On
/// <c>HallRB/TenthRegimentMarch/TenthRegClar2</c> the port broke 104 bars into 13
/// systems where 2.27.2 uses 12, because the page breaker's estimated rod height for
/// one line was 0.911 staff-spaces short. The line carries the two-bar slur
/// <c>g,4.( fis | e4. d)</c>, whose four note columns sit at column ranks 93, 95, 97
/// and 99 with pure Y extents measured IDENTICALLY on both engines:
/// </para>
/// <code>
///   rank 93  [-1.545, 2.50]      rank 97  [-2.545, 1.50]
///   rank 95  [-2.045, 2.00]      rank 99  [-3.045, 1.00]
/// </code>
/// <para>
/// 2.27.2 answers <c>[-3.545, -2.045]</c> for that slur in EVERY measure window it is
/// charged to; the port answered <c>[-2.545, -2.045]</c> in the window [92, 96],
/// because the two columns beyond the bar line had frozen an EMPTY pure height —
/// <c>Item::pure_y_extent</c> caches its first answer for good, and the first ask came
/// from a window that excluded them. The same defect emptied measure 13's own bucket
/// of its own note columns. See <see cref="ItemPureHeightCacheTests"/> for what the
/// cache is otherwise load-bearing for, and PORT-COVERAGE for the divergence record.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SlurPureHeightTests
{
    private static readonly Symbol YExtentSymbol = Symbol.Intern("Y-extent");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol NoteColumnsSymbol = Symbol.Intern("note-columns");

    /// <summary>The minimum basic-property alist a bare grob needs to exist.</summary>
    private static object Basics(string name)
        => Pair.List(
            new Pair(
                Symbol.Intern("meta"),
                Pair.List(
                    new Pair(Symbol.Intern("name"), Symbol.Intern(name)),
                    new Pair(Symbol.Intern("interfaces"), Nil.Instance))));

    /// <summary>An item standing in for one note column, at a given rank.</summary>
    private static Item Column(Grob parent, int rank, double bottom, double top)
    {
        Item column = new Item(Basics("NoteColumn"));
        PaperColumn paperColumn = new PaperColumn(Basics("PaperColumn"));
        paperColumn.Rank = rank;
        column.XParent = paperColumn;
        column.YParent = parent;
        column.SetProperty(YExtentSymbol, new Pair(bottom, top));
        return column;
    }

    private static Spanner Slur(Grob parent, Direction dir, params Grob[] columns)
    {
        Spanner slur = new Spanner(Basics("Slur"));
        slur.YParent = parent;
        slur.SetProperty(DirectionSymbol, (long)(int)dir);

        GrobArray array = new GrobArray();
        foreach (Grob column in columns)
        {
            array.Add(column);
        }

        slur.SetObject(NoteColumnsSymbol, array);
        return slur;
    }

    /// <summary>The four columns of TenthRegClar2's bar-13 slur, as 2.27.2 measures them.</summary>
    private static (Spanner Slur, Grob Parent) OracleSlur(Direction dir)
    {
        Spanner parent = new Spanner(Basics("VerticalAxisGroup"));
        Item c93 = Column(parent, 93, -1.545, 2.50);
        Item c95 = Column(parent, 95, -2.045, 2.00);
        Item c97 = Column(parent, 97, -2.545, 1.50);
        Item c99 = Column(parent, 99, -3.045, 1.00);
        return (Slur(parent, dir, c93, c95, c97, c99), parent);
    }

    [Fact]
    public void slur_pure_height_over_the_oracle_s_four_note_columns_is_the_oracle_s_interval()
    {
        //Arrange -- the expectation is 2.27.2's own answer for this slur, read off the
        // pinned oracle before any port value was looked at
        (Spanner slur, Grob _) = OracleSlur(Direction.Negative);

        //Act
        Interval height = Objects.Slur.PureHeight(slur, 92, 96);

        //Assert -- the lowest column decides, then the 0.5 staff-space attachment offset
        height.Left.Should().BeApproximately(-3.545, 1e-9);
        height.Right.Should().BeApproximately(-2.045, 1e-9);
    }

    [Fact]
    public void the_control_the_same_slur_pointing_up_is_measured_on_its_other_side()
    {
        //Arrange -- the direction chooses which end of each column's extent is read, so
        // an up slur over the same four columns must NOT give the same interval
        (Spanner slur, Grob _) = OracleSlur(Direction.Positive);

        //Act
        Interval height = Objects.Slur.PureHeight(slur, 92, 96);

        //Assert -- the tops are 2.50 / 2.00 / 1.50 / 1.00, and +0.5 is added upward
        height.Left.Should().BeApproximately(1.5, 1e-9);
        height.Right.Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void the_control_a_slur_whose_columns_hang_off_another_staff_is_ignored()
    {
        //Arrange -- upstream's cross-staff bail-out: when the columns' common refpoint is
        // not the slur's own Y parent the estimate is refused rather than guessed
        Spanner parent = new Spanner(Basics("VerticalAxisGroup"));
        Spanner elsewhere = new Spanner(Basics("VerticalAxisGroup"));
        Item strayColumn = Column(elsewhere, 93, -1.545, 2.50);
        Spanner slur = Slur(parent, Direction.Negative, strayColumn);

        //Act
        Interval height = Objects.Slur.PureHeight(slur, 92, 96);

        //Assert
        height.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void an_item_outside_the_query_window_is_measured_over_a_window_that_contains_it()
    {
        //Arrange -- an item at rank 97 whose Y-extent RECORDS the window it is measured
        // over, asked first from the neighbouring measure [92, 96], which excludes it
        string recorded = null;

        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();

                // CreateInterpreter no longer publishes the ambient one (audit §2.9,
                // ruled 2026-08-27); a fixture that wants a primitives-only interpreter
                // ambient says so here, which is what lets the Y-extent callback run.
                LilyPondScheme.RestoreAmbient(interpreter);

                object recorder = interpreter.EvalString(
                    "(begin (define l17-window '())"
                    + "       (lambda (g s e) (set! l17-window (cons s e)) (cons 0.0 1.0)))",
                    "<test>");

                Item item = new Item(Basics("NoteColumn"));
                PaperColumn column = new PaperColumn(Basics("PaperColumn"));
                column.Rank = 97;
                item.XParent = column;
                item.SetProperty(YExtentSymbol, new UnpurePureContainer(recorder, recorder));

                //Act
                _ = item.PureYExtent(item, 92, 96);
                recorded = Printer.Write(interpreter.EvalString("l17-window", "<test>"));
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert -- widened past rank 97 on BOTH sides, because an item sitting ON a bound
        // reads as a line boundary to PureFindVisiblePrebrokenPiece
        recorded.Should().Be("(92 . 98)");
    }

    [Fact]
    public void the_control_an_item_inside_the_query_window_is_measured_over_it_unchanged()
    {
        //Arrange -- the same recorder, on an item the window already contains
        string recorded = null;

        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();

                // CreateInterpreter no longer publishes the ambient one (audit §2.9,
                // ruled 2026-08-27); a fixture that wants a primitives-only interpreter
                // ambient says so here, which is what lets the Y-extent callback run.
                LilyPondScheme.RestoreAmbient(interpreter);

                object recorder = interpreter.EvalString(
                    "(begin (define l17-window '())"
                    + "       (lambda (g s e) (set! l17-window (cons s e)) (cons 0.0 1.0)))",
                    "<test>");

                Item item = new Item(Basics("NoteColumn"));
                PaperColumn column = new PaperColumn(Basics("PaperColumn"));
                column.Rank = 94;
                item.XParent = column;
                item.SetProperty(YExtentSymbol, new UnpurePureContainer(recorder, recorder));

                //Act
                _ = item.PureYExtent(item, 92, 96);
                recorded = Printer.Write(interpreter.EvalString("l17-window", "<test>"));
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert -- untouched, so the widening above is the item's rank and nothing else
        recorded.Should().Be("(92 . 96)");
    }
}
