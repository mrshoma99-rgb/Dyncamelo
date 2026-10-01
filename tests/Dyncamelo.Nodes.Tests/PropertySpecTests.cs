using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Nodes.Portable;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>How Properties.ToTable and Model.Snapshot read the property names the user types.</summary>
public class PropertySpecTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    // ------------------------------------------------------------- one name

    [Theory]
    [InlineData("Element.Category", "Element", "Category")]
    [InlineData("Item|Name", "Item", "Name")]
    [InlineData("  Element . Length  ", "Element", "Length")]
    [InlineData("Revit Type|Type Name", "Revit Type", "Type Name")]
    public void Parse_SplitsCategoryAndProperty(string text, string category, string property)
    {
        var spec = PropertySpec.Parse(text);

        Assert.Equal(PropertySpecKind.Property, spec.Kind);
        Assert.Equal(category, spec.Category);
        Assert.Equal(property, spec.Property);
        Assert.Equal(text.Trim(), spec.Header);
    }

    [Fact]
    public void Parse_UsesTheFirstBarWhenThereIsOne_SoDotsStayInTheNames()
    {
        var spec = PropertySpec.Parse("Dim.Group|Length.Net");

        Assert.Equal("Dim.Group", spec.Category);
        Assert.Equal("Length.Net", spec.Property);
    }

    [Fact]
    public void Parse_SplitsOnTheFirstDotOtherwise()
    {
        var spec = PropertySpec.Parse("Element.Length.Net");

        Assert.Equal("Element", spec.Category);
        Assert.Equal("Length.Net", spec.Property);
    }

    [Fact]
    public void Parse_ABareNameIsSearchedInEveryCategory()
    {
        var spec = PropertySpec.Parse("Level");

        Assert.Equal(PropertySpecKind.Property, spec.Kind);
        Assert.Null(spec.Category);
        Assert.Equal("Level", spec.Property);
        Assert.Equal("Level", spec.Header);
    }

    [Theory]
    [InlineData("@Name", PropertySpecKind.Name)]
    [InlineData("@name", PropertySpecKind.Name)]
    [InlineData("@PATH", PropertySpecKind.Path)]
    [InlineData("@Guid", PropertySpecKind.Guid)]
    [InlineData("  @guid ", PropertySpecKind.Guid)]
    public void Parse_KnowsTheThreeVirtualColumns(string text, PropertySpecKind kind)
    {
        var spec = PropertySpec.Parse(text);

        Assert.Equal(kind, spec.Kind);
        Assert.Null(spec.Category);
        Assert.Equal(string.Empty, spec.Property);
        Assert.Equal(text.Trim(), spec.Header);
    }

    [Fact]
    public void Parse_HeaderIsExactlyWhatTheUserWrote()
    {
        Assert.Equal("Item|Layer", PropertySpec.Parse("Item|Layer").Header);
        Assert.Equal("Element.Category", PropertySpec.Parse("Element.Category").Header);
        Assert.Equal("@name", PropertySpec.Parse("@name").Header);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_RejectsEmptyText(string? text)
    {
        var error = Assert.Throws<ArgumentException>(() => PropertySpec.Parse(text));
        Assert.Contains("empty", error.Message);
        Assert.Contains("@Name", error.Message);
    }

    [Fact]
    public void Parse_RejectsAnUnknownVirtualColumn()
    {
        var error = Assert.Throws<ArgumentException>(() => PropertySpec.Parse("@Layer"));
        Assert.Contains("'@Layer'", error.Message);
        Assert.Contains("@Name, @Path or @Guid", error.Message);
    }

    [Theory]
    [InlineData(".Length", '.')]
    [InlineData("Element.", '.')]
    [InlineData("|Length", '|')]
    [InlineData("Element|", '|')]
    [InlineData("Element.  ", '.')]
    public void Parse_RejectsAnEmptyCategoryOrProperty(string text, char separator)
    {
        var error = Assert.Throws<ArgumentException>(() => PropertySpec.Parse(text));
        Assert.Contains("both a category and a property", error.Message);
        Assert.Contains("'" + separator + "'", error.Message);
    }

    [Fact]
    public void TryParse_ReportsWithoutThrowing()
    {
        Assert.True(PropertySpec.TryParse("Item.Name", out var spec, out var problem));
        Assert.NotNull(spec);
        Assert.Null(problem);

        Assert.False(PropertySpec.TryParse("@x", out spec, out problem));
        Assert.Null(spec);
        Assert.NotNull(problem);
    }

    // ------------------------------------------------------------- a list

    [Fact]
    public void ParseList_KeepsTheOrderAndTheHeaders()
    {
        var specs = PropertySpec.ParseList(L("@Name", "Element.Category", "Item|Layer", "Level", "@Guid"), "Properties.ToTable");

        Assert.Equal(new[] { "@Name", "Element.Category", "Item|Layer", "Level", "@Guid" }, specs.Select(s => s.Header));
        Assert.Equal(
            new[] { PropertySpecKind.Name, PropertySpecKind.Property, PropertySpecKind.Property, PropertySpecKind.Property, PropertySpecKind.Guid },
            specs.Select(s => s.Kind));
    }

    [Fact]
    public void ParseList_RequiresTheList()
    {
        var error = Assert.Throws<ArgumentNullException>(() => PropertySpec.ParseList(null!, "Model.Snapshot"));
        Assert.Contains("Model.Snapshot requires the properties", error.Message);
        Assert.Contains("'properties' input", error.Message);
    }

    [Fact]
    public void ParseList_RequiresAtLeastOneName()
    {
        var error = Assert.Throws<ArgumentException>(() => PropertySpec.ParseList(L(), "Properties.ToTable"));
        Assert.Contains("Properties.ToTable requires at least one property name", error.Message);
    }

    [Fact]
    public void ParseList_NamesThePositionOfABadEntry()
    {
        var error = Assert.Throws<ArgumentException>(() => PropertySpec.ParseList(L("@Name", "Item.Name", "@Nope"), "Properties.ToTable"));
        Assert.Contains("Properties.ToTable", error.Message);
        Assert.Contains("index 2", error.Message);
        Assert.Contains("'@Nope'", error.Message);
    }

    [Fact]
    public void ParseList_NamesThePositionOfANullEntry()
    {
        var error = Assert.Throws<ArgumentException>(() => PropertySpec.ParseList(L("Item.Name", null), "Model.Snapshot"));
        Assert.Contains("index 1", error.Message);
        Assert.Contains("empty", error.Message);
    }

    [Fact]
    public void ParseList_ReadsNonTextEntriesAsTheirText()
    {
        var specs = PropertySpec.ParseList(L(42), "Properties.ToTable");

        Assert.Equal("42", specs[0].Header);
        Assert.Null(specs[0].Category);
    }
}
