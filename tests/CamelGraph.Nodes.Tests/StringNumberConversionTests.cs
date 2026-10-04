using System;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Serialization;
using Xunit;
using static CamelGraph.Nodes.Tests.Val2Harness;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Text to number and number to text: String.ToNumber reads a decimal comma and the first number of a text with a unit (audit
/// VAL-35); Number.Format is now String.FromNumber, the String tab's counterpart of String.ToNumber (VAL-27).
/// </summary>
public class StringNumberConversionTests
{
    private const string OldToNumberId = "CamelGraph.Nodes.StringNodes.ToNumber@string";
    private const string OldNumberFormatId = "CamelGraph.Nodes.StringExtraNodes.NumberFormat@double,int,bool,string,string";

    // ── Strict mode (the default) is what it always was ─────────────────────

    [Theory]
    [InlineData("3.14", 3.14)]
    [InlineData(" -2 ", -2)]
    [InlineData("1.5e3", 1500)]
    public void TheDefaultStaysStrictAndInvariant(string text, double expected)
    {
        Assert.Equal(expected, StringNodes.ToNumber(text), 12);
    }

    [Theory]
    [InlineData("12.5 mm")]
    [InlineData("1,5")]
    [InlineData("n/a")]
    [InlineData("")]
    public void TheDefaultStillRefusesUnitsCommasAndWords(string text)
    {
        var ex = Assert.Throws<FormatException>(() => StringNodes.ToNumber(text));
        Assert.Contains("Cannot convert", ex.Message);
    }

    [Fact]
    public void TheDefaultErrorKeepsItsWording_TheDescriptionNamesTheTwoOptions()
    {
        // The wiki pictures show this message (tests/Shared/Wiki/SceneGraphs.cs), so it stays word for word.
        var ex = Assert.Throws<FormatException>(() => StringNodes.ToNumber("12 pcs"));
        Assert.Equal("Cannot convert '12 pcs' to a number. Expected an invariant-culture numeric string such as \"3.14\".", ex.Message);

        var description = Create(Registry(), "String.ToNumber").Description;
        Assert.Contains("decimalSeparator", description);
        Assert.Contains("ignoreUnits", description);
    }

    // ── decimalSeparator ────────────────────────────────────────────────────

    [Theory]
    [InlineData("1,5", 1.5)]
    [InlineData(" -2,25 ", -2.25)]
    [InlineData(",5", 0.5)]
    [InlineData("1,5e3", 1500)]
    [InlineData("42", 42)]
    public void ADecimalCommaIsRead(string text, double expected)
    {
        Assert.Equal(expected, StringNodes.ToNumber(text, ","), 12);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1,2,3")]
    [InlineData("12,5 mm")]
    [InlineData("n/a")]
    public void ADecimalCommaModeRefusesWhatIsNotADecimalCommaNumber(string text)
    {
        Assert.Throws<FormatException>(() => StringNodes.ToNumber(text, ","));
    }

    [Fact]
    public void ADecimalSeparatorThatIsNeitherDotNorCommaIsAClearError()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringNodes.ToNumber("1", ";"));
        Assert.Contains("decimalSeparator must be", ex.Message);
        Assert.Equal(1d, StringNodes.ToNumber("1", ""), 12);
    }

    // ── ignoreUnits ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("12.5 mm", 12.5)]
    [InlineData("12.5mm", 12.5)]
    [InlineData("approx. -3.5 m", -3.5)]
    [InlineData("EUR12.5", 12.5)]
    [InlineData("Level 3", 3)]
    [InlineData("A-12", 12)]
    [InlineData("Rev.2", 2)]
    [InlineData("(.5 in)", 0.5)]
    [InlineData("1.5e3 kg", 1500)]
    [InlineData("5 em", 5)]
    [InlineData("1,234.5 m", 1234.5)]
    [InlineData("1,234,567 m", 1234567)]
    [InlineData("1234 m2", 1234)]
    [InlineData("42", 42)]
    public void IgnoreUnits_ReadsTheFirstNumberWithADecimalPoint(string text, double expected)
    {
        Assert.Equal(expected, StringNodes.ToNumber(text, ".", true), 12);
    }

    [Theory]
    [InlineData("12,5 mm", 12.5)]
    [InlineData("ca. -3,5 m", -3.5)]
    [InlineData("1.234,5 m", 1234.5)]
    [InlineData("1.234 m", 1234)]
    [InlineData("Ebene 3", 3)]
    public void IgnoreUnits_ReadsTheFirstNumberWithADecimalComma(string text, double expected)
    {
        Assert.Equal(expected, StringNodes.ToNumber(text, ",", true), 12);
    }

    [Fact]
    public void IgnoreUnits_DoesNotGuessWhenTheTextUsesTheOtherSeparator()
    {
        // "1,5 m" read with a decimal point must not silently become 1.
        var dot = Assert.Throws<FormatException>(() => StringNodes.ToNumber("1,5 m", ".", true));
        Assert.Contains("decimalSeparator", dot.Message);

        var comma = Assert.Throws<FormatException>(() => StringNodes.ToNumber("1.5 m", ",", true));
        Assert.Contains("decimalSeparator", comma.Message);
    }

    [Theory]
    [InlineData("n/a")]
    [InlineData("")]
    [InlineData("mm")]
    public void IgnoreUnits_ATextWithoutANumberStillFails(string text)
    {
        var ex = Assert.Throws<FormatException>(() => StringNodes.ToNumber(text, ".", true));
        Assert.Contains("Cannot find a number", ex.Message);
    }

    [Fact]
    public void ToNumber_OverAColumn_GivesTheNumbersAndWarnsForTheRowsWithoutOne()
    {
        var node = Create(Registry(), "String.ToNumber");
        node.InPorts.Single(p => p.Name == "ignoreUnits").SetUserValue(true);
        Run(node, L("12.5 mm", "3 m", "n/a"));

        Assert.Equal(L(12.5, 3.0, null), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
    }

    // ── The old id and the new options ──────────────────────────────────────

    [Fact]
    public void ToNumber_OldIdStillLoadsAndKeepsTheStrictBehaviour()
    {
        var registry = Registry();
        Assert.True(registry.TryGetDefinition(OldToNumberId, out var definition));
        Assert.Equal("String.ToNumber", definition!.Name);

        var node = Run(registry.CreateZeroTouchNode(OldToNumberId)!, "3.5");
        Assert.Equal(3.5, node.OutPorts[0].Value);

        var strict = Run(registry.CreateZeroTouchNode(OldToNumberId)!, "3.5 m");
        Assert.Equal(NodeState.Error, strict.State);
    }

    [Fact]
    public void ToNumber_TheOptionsAreInputsWithADropdownAndTheOldMeaningAsDefault()
    {
        var node = Create(Registry(), "String.ToNumber");
        var separator = node.InPorts.Single(p => p.Name == "decimalSeparator");

        Assert.Equal(".", separator.DefaultValue);
        Assert.Equal(new[] { ".", "," }, separator.Choices);
        Assert.Equal(false, node.InPorts.Single(p => p.Name == "ignoreUnits").DefaultValue);
    }

    // ── String.FromNumber (formerly Number.Format) ──────────────────────────

    [Fact]
    public void FromNumber_IsInTheStringTabAndFoundUnderTheOldName()
    {
        var node = Create(Registry(), "String.FromNumber");

        Assert.Equal("String", node.Definition.Category);
        Assert.Contains("number.format", node.SearchTags);
        Assert.Contains("number format", node.SearchTags);
        Assert.Contains("Formerly called Number.Format", node.Description);
    }

    [Fact]
    public void FromNumber_NoNodeCalledNumberFormatIsLeftInTheLibrary()
    {
        Assert.DoesNotContain(Registry().Definitions, d => d.Name == "Number.Format");
    }

    [Fact]
    public void FromNumber_OldIdStillLoadsAndRuns()
    {
        var registry = Registry();
        Assert.True(registry.TryGetDefinition(OldNumberFormatId, out var definition));
        Assert.Equal("String.FromNumber", definition!.Name);

        var node = Run(registry.CreateZeroTouchNode(OldNumberFormatId)!, 1234.5, 1, true, "EUR ");

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal("EUR 1,234.5", node.OutPorts[0].Value);
    }

    [Theory]
    [InlineData("String.ToNumber", OldToNumberId)]
    [InlineData("String.FromNumber", OldNumberFormatId)]
    public void AGraphFileSavedWithTheOldIdOpensAndFindsTheNode(string name, string oldId)
    {
        var registry = Registry();
        var graph = new GraphModel();
        var node = Create(registry, name);
        graph.AddNode(node);
        var serializer = new GraphSerializer(registry);

        var loaded = serializer.Deserialize(serializer.Serialize(graph).Replace(node.Definition.Id, oldId));

        var loadedNode = Assert.IsType<CamelGraph.Core.Loader.ZeroTouchNodeModel>(Assert.Single(loaded.Nodes));
        Assert.Equal(name, loadedNode.Name);
        Assert.Equal(node.Definition.Id, loadedNode.Definition.Id);
        Assert.Empty(serializer.LoadWarnings);
    }

    [Fact]
    public void FromNumber_DecimalsMessageNamesTheNodeByItsNewName()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.FromNumber(1, 16));

        Assert.Contains("String.FromNumber", ex.Message);
    }
}
