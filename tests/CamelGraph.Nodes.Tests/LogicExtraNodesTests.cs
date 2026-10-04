using System;
using System.Collections.Generic;
using Xunit;

namespace CamelGraph.Nodes.Tests;

public class LogicExtraNodesTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    [Theory]
    [InlineData(3, 2, ">", true)]
    [InlineData(3, 3, ">", false)]
    [InlineData(3, 3, ">=", true)]
    [InlineData(2, 3, "<", true)]
    [InlineData(3, 3, "<=", true)]
    [InlineData(3, 3.0, "==", true)]
    [InlineData(3, 4, "!=", true)]
    public void Compare_Numbers(double a, double b, string test, bool expected)
    {
        Assert.Equal(expected, LogicExtraNodes.Compare(a, b, test));
    }

    [Fact]
    public void Compare_Dates()
    {
        var early = new DateTime(2026, 1, 1);
        var late = new DateTime(2026, 6, 1);
        Assert.True(LogicExtraNodes.Compare(early, late, "<"));
        Assert.True(LogicExtraNodes.Compare(late, early, ">="));
        Assert.True(LogicExtraNodes.Compare(early, new DateTime(2026, 1, 1), "=="));
    }

    [Fact]
    public void Compare_TextWords_AndWordFormsOfTheTests()
    {
        Assert.True(LogicExtraNodes.Compare("Wall-01", "wall", "contains"));
        Assert.True(LogicExtraNodes.Compare("Wall-01", "wall", "startsWith"));
        Assert.False(LogicExtraNodes.Compare("Wall-01", "wall", "contains", ignoreCase: false));
        Assert.True(LogicExtraNodes.Compare("Wall-01", "W*-0?", "matches"));
        Assert.True(LogicExtraNodes.Compare(5, 3, "greater"));
        Assert.True(LogicExtraNodes.Compare(5, 5, "equals"));
        Assert.True(LogicExtraNodes.Compare("a", "b", "notEquals"));
    }

    [Fact]
    public void Compare_ReadsNumericTextAsANumber_WhenTheOtherSideIsOne()
    {
        Assert.True(LogicExtraNodes.Compare("12", 9, ">"));
        Assert.True(LogicExtraNodes.Compare(12, "12.0", "=="));
        Assert.False(LogicExtraNodes.Compare("abc", 9, "=="));
    }

    [Fact]
    public void Compare_NullTests()
    {
        Assert.True(LogicExtraNodes.Compare(null, null, "isNull"));
        Assert.True(LogicExtraNodes.Compare("x", null, "notNull"));
        Assert.True(LogicExtraNodes.Compare("", null, "isEmpty"));
        Assert.False(LogicExtraNodes.Compare("x", null, "isEmpty"));
    }

    [Fact]
    public void Compare_OrderingIncomparableTypes_ExplainsWhat()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LogicExtraNodes.Compare(new DateTime(2026, 1, 1), true, "<"));
        Assert.Contains("Logic.Compare", ex.Message);
    }

    [Fact]
    public void Compare_RejectsAnUnknownTest()
    {
        var ex = Assert.Throws<ArgumentException>(() => LogicExtraNodes.Compare(1, 2, "roughly"));
        Assert.Contains("roughly", ex.Message);
    }

    [Fact]
    public void NotEqualsAndXor()
    {
        Assert.True(LogicExtraNodes.NotEquals(1, 2));
        Assert.False(LogicExtraNodes.NotEquals(2, 2.0));
        Assert.True(LogicExtraNodes.Xor(true, false));
        Assert.False(LogicExtraNodes.Xor(true, true));
        Assert.False(LogicExtraNodes.Xor(false, false));
    }

    [Theory]
    [InlineData(5, 1, 10, true, true)]
    [InlineData(1, 1, 10, true, true)]
    [InlineData(1, 1, 10, false, false)]
    [InlineData(10, 1, 10, true, true)]
    [InlineData(11, 1, 10, true, false)]
    [InlineData(0, 1, 10, true, false)]
    public void IsBetween_NumbersAndBounds(double value, double min, double max, bool inclusive, bool expected)
    {
        Assert.Equal(expected, LogicExtraNodes.IsBetween(value, min, max, inclusive));
    }

    [Fact]
    public void IsBetween_Dates_AndEmptyInputs()
    {
        Assert.True(LogicExtraNodes.IsBetween(new DateTime(2026, 3, 1), new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        // Wave D: a missing value is "no value", it is not between anything (it used to be an error); an empty bound still is one.
        Assert.False(LogicExtraNodes.IsBetween(null, 1, 2));
        Assert.Throws<ArgumentNullException>(() => LogicExtraNodes.IsBetween(5, null, 2));
        Assert.Throws<ArgumentNullException>(() => LogicExtraNodes.IsBetween(5, 1, null));
    }

    [Fact]
    public void Choose_PicksByPosition()
    {
        Assert.Equal("b", LogicExtraNodes.Choose(1, L("a", "b", "c")));
        Assert.Equal("a", LogicExtraNodes.Choose(0, L("a")));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Choose_NamesTheValidRange(int index)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => LogicExtraNodes.Choose(index, L("a", "b", "c")));
        Assert.Contains("3 option", ex.Message);
    }

    [Fact]
    public void Switch_GivesTheResultOfTheFirstMatchingCase_OrTheFallback()
    {
        var cases = L("New", "Active", "Resolved");
        var results = L("red", "amber", "green");
        Assert.Equal("amber", LogicExtraNodes.Switch("active", cases, results));
        Assert.Equal("grey", LogicExtraNodes.Switch("Closed", cases, results, "grey"));
        Assert.Null(LogicExtraNodes.Switch("Closed", cases, results));
        Assert.Equal("one", LogicExtraNodes.Switch(1.0, L(1, 2), L("one", "two")));
    }

    [Fact]
    public void Switch_NeedsCasesAndResultsOfTheSameLength()
    {
        var ex = Assert.Throws<ArgumentException>(() => LogicExtraNodes.Switch(1, L(1, 2), L("one")));
        Assert.Contains("same length", ex.Message);
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("hi", "text")]
    [InlineData(true, "boolean")]
    [InlineData(5, "number")]
    [InlineData(2.5, "number")]
    public void TypeOf_NamesTheKind(object? value, string expected)
    {
        Assert.Equal(expected, LogicExtraNodes.TypeOf(value));
    }

    [Fact]
    public void TypeOf_ListsDictionariesDatesAndObjects()
    {
        Assert.Equal("list", LogicExtraNodes.TypeOf(L(1)));
        Assert.Equal("dictionary", LogicExtraNodes.TypeOf(new Dictionary<string, object?>()));
        Assert.Equal("datetime", LogicExtraNodes.TypeOf(DateTime.Now));
        Assert.Equal("CamelGraphPoint", LogicExtraNodes.TypeOf(new CamelGraphPoint(0, 0, 0)));
    }
}
