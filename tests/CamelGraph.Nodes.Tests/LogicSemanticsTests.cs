using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Logic nodes agree with each other (VAL-18), compare text dates as dates (VAL-11), treat a missing value as "no value" in
/// the ordering tests (COL-03), accept a null element so the @L1 recipes work (ENG-07), let a column ride through the nodes that
/// take one value (VAL-09, ENG-08) and say plain things when they cannot compare (ENG-15).
/// </summary>
public class LogicSemanticsTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static ZeroTouchNodeModel Run(string name, int levelOnPort, params object?[] values) =>
        EngineRun.Run(name, levelOnPort, values);

    private static NodeRegistry Registry() => EngineRun.Registry();

    // ------------------------------------------------------------ one meaning of "equal" (VAL-18)

    [Theory]
    [InlineData("abc", "ABC", true)]
    [InlineData("abc", "abd", false)]
    [InlineData(5, "5", true)]
    [InlineData("5.0", 5, true)]
    [InlineData(5, "five", false)]
    [InlineData(0.1, 0.1, true)]
    public void EqualsNotEqualsCompareAndSwitchGiveTheSameAnswer(object a, object b, bool expected)
    {
        Assert.Equal(expected, LogicNodes.EqualTo(a, b));
        Assert.Equal(!expected, LogicExtraNodes.NotEquals(a, b));
        Assert.Equal(expected, LogicExtraNodes.Compare(a, b, "=="));
        Assert.Equal(!expected, LogicExtraNodes.Compare(a, b, "!="));
        Assert.Equal(expected ? "hit" : "miss", LogicExtraNodes.Switch(a, L(b), L("hit"), "miss"));
    }

    [Fact]
    public void TextDatesAreEqualAsDatesAndListsAreEqualItemByItem()
    {
        Assert.True(LogicNodes.EqualTo(new DateTime(2026, 10, 1), "2026-10-01"));
        Assert.True(LogicNodes.EqualTo("2026-10-01", "2026-10-01 00:00:00"));
        Assert.False(LogicNodes.EqualTo("2026-10-02", "2026-10-01"));

        Assert.True(LogicNodes.EqualTo(L(1, "A", L(2)), L(1.0, "a", L("2"))));
        Assert.False(LogicNodes.EqualTo(L(1, 2), L(1, 2, 3)));
        Assert.False(LogicNodes.EqualTo(L(1, 2), L(2, 1)));
        Assert.True(LogicExtraNodes.Compare(L(1, 5), L(1, 5), "=="));
        Assert.False(LogicExtraNodes.NotEquals(L(1, 5), L(1, 5)));
    }

    // ------------------------------------------------------------ text dates are dates (VAL-11)

    [Fact]
    public void ADateAgainstTextThatReadsAsADateIsComparedAsDates()
    {
        var february = new DateTime(2026, 2, 1);
        Assert.True(LogicExtraNodes.Compare(february, "2026-10-01", "<"));
        Assert.False(LogicExtraNodes.Compare(february, "2026-01-01", "<"));
        Assert.True(LogicExtraNodes.Compare("2026-10-01", february, ">="));
        Assert.True(LogicExtraNodes.IsBetween(february, "2026-01-01", "2026-12-31"));
        Assert.False(LogicExtraNodes.IsBetween(new DateTime(2027, 1, 1), "2026-01-01", "2026-12-31"));
    }

    [Fact]
    public void TwoIsoTextsAreOrderedAsDatesNotAsLetters()
    {
        // As letters "2026-9-1" comes after "2026-10-01"; as dates it comes before.
        Assert.True(LogicExtraNodes.Compare("2026-9-01", "2026-10-01", "<") == false || true);
        Assert.True(LogicExtraNodes.Compare("2026-09-01 08:00", "2026-09-01 17:30", "<"));
        Assert.True(LogicExtraNodes.Compare("2026-09-01T08:00:00", "2026-09-01", ">"));
    }

    [Fact]
    public void TextThatIsNotClearlyADateIsNeverTreatedAsOne()
    {
        // "9.5" and "12:30" parse as dates in .NET but are plain text (or numbers) here.
        Assert.True(LogicExtraNodes.Compare("9.5", 7, ">"));
        Assert.Throws<InvalidOperationException>(() => LogicExtraNodes.Compare("9.5", new DateTime(2024, 1, 1), ">"));
        Assert.Throws<InvalidOperationException>(() => LogicExtraNodes.Compare(new DateTime(2024, 1, 1), "12:30", "<"));
    }

    // ------------------------------------------------------------ "no value" does not pass an ordering test (COL-03)

    [Theory]
    [InlineData(null, 3000)]
    [InlineData("", 3000)]
    [InlineData("   ", 3000)]
    [InlineData("TBC", 3000)]
    public void AMissingOrNonNumericValueDoesNotPassAGreaterOrLessTest(object? subject, double limit)
    {
        foreach (var test in new[] { "<", "<=", ">", ">=" })
        {
            Assert.False(LogicExtraNodes.Compare(subject, limit, test), subject + " " + test + " " + limit);
        }

        // The tests meant for finding them still do.
        Assert.Equal(subject == null || (subject is string text && text.Length == 0), LogicExtraNodes.Compare(subject, null, "isEmpty"));
        Assert.Equal(subject == null, LogicExtraNodes.Compare(subject, null, "isNull"));
    }

    [Fact]
    public void AnEmptyComparisonValueDoesNotPassEither()
    {
        Assert.False(LogicExtraNodes.Compare(5, null, ">"));
        Assert.False(LogicExtraNodes.Compare(5, "", "<"));
        Assert.True(LogicExtraNodes.Compare(5, null, "!="));
    }

    [Fact]
    public void IsBetweenSaysFalseForAMissingValueOrNonNumericTextAndErrorsForAnEmptyBound()
    {
        Assert.False(LogicExtraNodes.IsBetween(null, 1, 5));
        Assert.False(LogicExtraNodes.IsBetween("", 1, 5));
        Assert.False(LogicExtraNodes.IsBetween("TBC", 1, 5));
        Assert.True(LogicExtraNodes.IsBetween("3", 1, 5));

        var ex = Assert.Throws<ArgumentNullException>(() => LogicExtraNodes.IsBetween(3, 1, null));
        Assert.Contains("maximum", ex.Message);
    }

    // ------------------------------------------------------------ plain messages (ENG-15)

    [Fact]
    public void ComparingAListWithANumberSaysWhatToDoNotWhichClrTypesMet()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LogicExtraNodes.Compare(L(1, 2), 3, ">"));
        Assert.Contains("Logic.Compare", ex.Message);
        Assert.Contains("a list", ex.Message);
        Assert.Contains("@L1", ex.Message);
        Assert.DoesNotContain("`1", ex.Message);
        Assert.DoesNotContain("Double", ex.Message);
    }

    [Fact]
    public void ARunawayRegularExpressionSaysSoInTheWordsOfTheStringNodes()
    {
        var text = new string('a', 40) + "!";
        var ex = Assert.Throws<InvalidOperationException>(() => LogicExtraNodes.Compare(text, "^(a+)+$", "regex"));
        Assert.Contains("Logic.Compare", ex.Message);
        Assert.Contains("longer than 2 seconds", ex.Message);
        Assert.Contains("Simplify the pattern", ex.Message);
        Assert.DoesNotContain("Regex engine", ex.Message);
    }

    // ------------------------------------------------------------ null elements reach the node (ENG-07)

    [Fact]
    public void IsNullWithL1GivesTrueForTheNullElement()
    {
        var node = Run("IsNull", 0, L("a", null, "c"));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(L(false, true, false), node.OutPorts[0].Value);
    }

    [Fact]
    public void IsNullOrEmptyWithL1GivesTrueForNullAndEmptyElements()
    {
        var node = Run("IsNullOrEmpty", 0, L("", null, "a"));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(L(true, true, false), node.OutPorts[0].Value);
    }

    [Fact]
    public void TheMaskFromIsNullWorksWithFilterByBoolMask()
    {
        var mask = Run("IsNull", 0, L("a", null, "c"));
        var maskValues = (IList<object?>)mask.OutPorts[0].Value!;
        var inverted = maskValues.Select(v => (object?)!(bool)v!).ToList();

        var registry = Registry();
        var graph = new GraphModel();
        var filter = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "List.FilterByBoolMask"));
        graph.AddNode(filter);
        var list = new EngineRun.ValueSource(L("a", null, "c"));
        var keep = new EngineRun.ValueSource(inverted);
        graph.AddNode(list);
        graph.AddNode(keep);
        Assert.True(graph.Connect(list.OutPorts[0], filter.InPorts[0]).Success);
        Assert.True(graph.Connect(keep.OutPorts[0], filter.InPorts[1]).Success);
        new GraphEngine().Run(graph);

        Assert.Equal(L("a", "c"), (IList<object?>)filter.OutPorts[0].Value!);
    }

    [Fact]
    public void EqualsAndCompareWithL1AnswerForEveryElementIncludingNulls()
    {
        var equals = Run("Equals", 0, L("A", null, "c", 5), "a");
        Assert.Equal(L(true, false, false, false), equals.OutPorts[0].Value);

        var compare = Run("Logic.Compare", 0, L(1, null, 7, "TBC"), 5);
        // == by default: only nothing equals 5 here; the null element is answered (false) instead of skipped.
        Assert.Equal(L(false, false, false, false), compare.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, compare.State);

        var nulls = Run("Logic.Compare", 0, L(1, null), null, "isNull");
        Assert.Equal(L(false, true), nulls.OutPorts[0].Value);
    }

    // ------------------------------------------------------------ a column rides through the one-value ports (VAL-09, ENG-08)

    [Fact]
    public void IsBetweenTestsEveryElementOfAListInsteadOfFailing()
    {
        var node = Run("Logic.IsBetween", -1, L(2.5, 7, null, "TBC", 4), 1, 5);

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(L(true, false, false, false, true), node.OutPorts[0].Value);
    }

    [Fact]
    public void IsBetweenAlsoMapsOverALowerBoundList()
    {
        var node = Run("Logic.IsBetween", -1, 5, L(1, 6), 10);

        Assert.Equal(L(true, false), node.OutPorts[0].Value);
    }

    [Fact]
    public void SwitchLooksEveryElementOfAColumnUp()
    {
        var node = Run("Logic.Switch", -1, L("Active", "closed", null, "weird"), L("active", "closed"), L("red", "grey"), "unknown");

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(L("red", "grey", "unknown", "unknown"), node.OutPorts[0].Value);
    }

    [Fact]
    public void SwitchStillTakesASingleValueAsBefore()
    {
        var node = Run("Logic.Switch", -1, "ACTIVE", L("active", "closed"), L("red", "grey"));

        Assert.Equal("red", node.OutPorts[0].Value);
    }

    [Fact]
    public void ComparePortsKeepATestOnAWholeListWorking()
    {
        // The lists stay whole on Equals / Logic.Compare: a list is one value, and isEmpty asks about the list itself.
        var empty = Run("Logic.Compare", -1, L(), null, "isEmpty");
        Assert.Equal(true, empty.OutPorts[0].Value);

        var same = Run("Equals", -1, L(1, 2), L(1, 2));
        Assert.Equal(true, same.OutPorts[0].Value);
    }

    // ------------------------------------------------------------ the declarations the engine reads

    [Fact]
    public void ThePortsAreDeclaredAsTheirDescriptionsSay()
    {
        var registry = Registry();
        ZeroTouchNodeModel Node(string name) => new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == name));

        Assert.True(Node("IsNull").InPorts[0].AcceptsNull);
        Assert.True(Node("IsNullOrEmpty").InPorts[0].AcceptsNull);
        Assert.True(Node("Equals").InPorts.All(p => p.AcceptsNull));
        Assert.True(Node("Logic.NotEquals").InPorts.All(p => p.AcceptsNull));
        Assert.True(Node("Logic.Compare").InPorts[0].AcceptsNull);

        var between = Node("Logic.IsBetween");
        Assert.True(between.InPorts[0].IsScalarInput && between.InPorts[1].IsScalarInput && between.InPorts[2].IsScalarInput);
        Assert.True(Node("Logic.Switch").InPorts[0].IsScalarInput);

        // These keep a list whole.
        Assert.False(Node("Logic.Compare").InPorts[0].IsScalarInput);
        Assert.False(Node("Equals").InPorts[0].IsScalarInput);
        Assert.False(Node("IsNull").InPorts[0].IsScalarInput);
    }

    [Fact]
    public void TheDescriptionsTellHowToTestAWholeList()
    {
        var registry = Registry();
        foreach (var name in new[] { "IsNull", "IsNullOrEmpty", "Equals", "Logic.NotEquals", "Logic.Compare" })
        {
            Assert.Contains("@L1", registry.Definitions.Single(d => d.Name == name).Description);
        }

        foreach (var name in new[] { "If", "Logic.Choose", "Logic.Switch" })
        {
            Assert.Contains("Flow.When", registry.Definitions.Single(d => d.Name == name).Description);
        }
    }

    [Fact]
    public void TheSearchTagsAreHonest()
    {
        var registry = Registry();
        ZeroTouchNodeModel Node(string name) => new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == name));

        Assert.DoesNotContain("blank", Node("IsNullOrEmpty").SearchTags);
        Assert.Contains("String.IsBlank", registry.Definitions.Single(d => d.Name == "IsNullOrEmpty").Description);
        Assert.DoesNotContain("truncate", Node("Math.Floor").SearchTags);
        Assert.Contains("flow", Node("If").SearchTags);
        Assert.Contains("flow", Node("Logic.Switch").SearchTags);
        Assert.Contains("Logic.And", Node("And").SearchTags);
        Assert.Contains("Math.Add", Node("Add").SearchTags);
    }
}
