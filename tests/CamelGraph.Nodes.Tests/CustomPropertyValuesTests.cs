using System;
using System.Collections.Generic;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Properties.SetCustom stamps one set of names and values on every item. A value that is a list (a table row that lost its @L2
/// badge, see the how-to for writing Excel data onto items) used to reach the COM bridge and was written as the text of its .NET
/// type, "System.Collections.Generic.List`1[System.Object]", into every property of every item, without a message.
/// </summary>
public class CustomPropertyValuesTests
{
    private enum Level
    {
        One,
    }

    private sealed class Pretend
    {
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text")]
    [InlineData("")]
    [InlineData('c')]
    [InlineData(true)]
    [InlineData(3)]
    [InlineData(3L)]
    [InlineData((short)3)]
    [InlineData((byte)3)]
    [InlineData(3u)]
    [InlineData(3.5)]
    [InlineData(3.5f)]
    public void TextNumbersAndBooleansAreStorable(object? value)
    {
        Assert.True(CustomPropertyValues.IsStorable(value));
    }

    [Fact]
    public void DatesGuidsChoicesAndDecimalsAreStorable()
    {
        Assert.True(CustomPropertyValues.IsStorable(new DateTime(2026, 5, 1)));
        Assert.True(CustomPropertyValues.IsStorable(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(CustomPropertyValues.IsStorable(TimeSpan.FromMinutes(3)));
        Assert.True(CustomPropertyValues.IsStorable(Guid.NewGuid()));
        Assert.True(CustomPropertyValues.IsStorable(Level.One));
        Assert.True(CustomPropertyValues.IsStorable(12.5m));
        Assert.True(CustomPropertyValues.IsStorable(new Uri("https://example.org")));
    }

    [Fact]
    public void AListIsNotStorable()
    {
        Assert.False(CustomPropertyValues.IsStorable(new List<object?> { "L1", 3.5 }));
        Assert.False(CustomPropertyValues.IsStorable(new object?[] { 1, 2 }));
        Assert.False(CustomPropertyValues.IsStorable(new List<List<object?>>()));
    }

    [Fact]
    public void ADictionaryOrAnotherObjectIsNotStorable()
    {
        Assert.False(CustomPropertyValues.IsStorable(new Dictionary<string, object?> { ["a"] = 1 }));
        Assert.False(CustomPropertyValues.IsStorable(new Pretend()));
    }

    [Fact]
    public void ARefusedListIsDescribedInWordsWithItsLength()
    {
        Assert.Equal("a list of 3 values", CustomPropertyValues.Describe(new List<object?> { 1, 2, 3 }));
        Assert.Equal("a list of 1 value", CustomPropertyValues.Describe(new List<object?> { 1 }));
        Assert.Equal("a dictionary of 2 entries", CustomPropertyValues.Describe(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 }));
        Assert.Equal("a Pretend", CustomPropertyValues.Describe(new Pretend()));
    }

    [Fact]
    public void TheMessageNamesThePropertyAndNeverTheDotNetTypeName()
    {
        var error = Assert.Throws<ArgumentException>(
            () => CustomPropertyValues.Require("Properties.SetCustom", "Level", new List<object?> { "L1", "L2" }));

        Assert.Contains("'Level'", error.Message);
        Assert.Contains("a list of 2 values", error.Message);
        Assert.Contains("Properties.SetCustomFromTable", error.Message);
        Assert.Contains("@L2", error.Message);
        Assert.DoesNotContain("System.Collections", error.Message);
        Assert.DoesNotContain("`1", error.Message);
    }

    [Fact]
    public void ADictionaryIsNotToldToUseListLevels()
    {
        var error = Assert.Throws<ArgumentException>(
            () => CustomPropertyValues.Require("Properties.SetCustom", "Data", new Dictionary<string, object?>()));

        Assert.Contains("a dictionary of 0 entries", error.Message);
        Assert.DoesNotContain("@L2", error.Message);
    }

    [Fact]
    public void AStorableValueIsAccepted()
    {
        CustomPropertyValues.Require("Properties.SetCustom", "Level", "L1");
        CustomPropertyValues.Require("Properties.SetCustom", "Level", null);
        CustomPropertyValues.Require("Properties.SetCustom", "Level", 4.5);
    }
}

/// <summary>The wiring of the check: it runs before any item is touched, and the COM bridge no longer writes a type name.</summary>
public class CustomPropertyWiringTests
{
    [Fact]
    public void SetCustomChecksEveryValueBeforeItWritesToAnItem()
    {
        var pairs = NavisworksMethodText.Body("CustomPropertyNodes.cs", "ZipNameValuePairs");
        var body = NavisworksMethodText.Body("CustomPropertyNodes.cs", "SetCustom");

        Assert.Contains("CustomPropertyValues.Require(", pairs);
        Assert.True(body.IndexOf("ZipNameValuePairs(", StringComparison.Ordinal) < body.IndexOf("SetUserDefinedTab(", StringComparison.Ordinal));
    }

    [Fact]
    public void TheComBridgeRefusesAListInsteadOfWritingItsTypeName()
    {
        var body = NavisworksMethodText.Body("Internal/ComBridge.cs", "ToComValue");

        Assert.Contains("CustomPropertyValues.IsStorable(value)", body);
        Assert.Contains("throw new ArgumentException(", body);
        // The text conversion is only left for simple values (a character, a GUID, a choice), after the check.
        Assert.True(body.IndexOf("IsStorable", StringComparison.Ordinal) < body.IndexOf("Convert.ToString(value", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTabNamesOfTheCustomTabNodesCanBePickedFromTheItemsTabs()
    {
        foreach (var method in new[] { "SetCustom", "RemoveCustomTab", "RenameCustomTab" })
        {
            var declaration = NavisworksMethodText.Declaration("CustomPropertyNodes.cs", method);
            Assert.Contains("[NodeTabChoice(\"modelItems\")] string tabName", declaration);
            Assert.Contains("[NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]", declaration);
        }
    }

    [Fact]
    public void TheMergeOptionOfSetCustomSitsInTheAdvancedPanel()
    {
        Assert.Contains("[NodePanel(\"Advanced\")] bool merge = true", NavisworksMethodText.Declaration("CustomPropertyNodes.cs", "SetCustom"));
    }
}
