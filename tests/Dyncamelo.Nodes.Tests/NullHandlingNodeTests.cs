using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Pins List.Clean and the IsNull / IsNullOrEmpty checks — the mop-up half of
/// the engine's null-propagating lacing.
/// </summary>
public class NullHandlingNodeTests
{
    private static List<object?> L(params object?[] items) => new List<object?>(items);

    // ------------------------------------------------------------ List.Clean

    [Fact]
    public void Clean_RemovesNulls_AtEveryLevel()
    {
        var cleaned = ListNodes.Clean(L("a", null, L(1.0, null, 2.0), null, "b"));
        Assert.Equal(3, cleaned.Count);
        Assert.Equal("a", cleaned[0]);
        Assert.Equal(new List<object?> { 1.0, 2.0 }, cleaned[1]);
        Assert.Equal("b", cleaned[2]);
    }

    [Fact]
    public void Clean_DropsListsLeftEmpty_ByDefault()
    {
        var cleaned = ListNodes.Clean(L(L(null, null), "x", L()));
        Assert.Equal(new List<object?> { "x" }, cleaned);
    }

    [Fact]
    public void Clean_KeepsEmptyLists_WhenAskedTo()
    {
        var cleaned = ListNodes.Clean(L(L((object?)null), "x"), removeEmptyLists: false);
        Assert.Equal(2, cleaned.Count);
        Assert.Empty((List<object?>)cleaned[0]!);
        Assert.Equal("x", cleaned[1]);
    }

    [Fact]
    public void Clean_TreatsStringsAsScalars_NotAsCharLists()
    {
        var cleaned = ListNodes.Clean(L("keep", null));
        Assert.Equal(new List<object?> { "keep" }, cleaned);
    }

    // ------------------------------------------------------- IsNull / IsNullOrEmpty

    [Fact]
    public void IsNull_TrueOnlyForNull()
    {
        Assert.True(LogicNodes.IsNull(null));
        Assert.False(LogicNodes.IsNull(""));
        Assert.False(LogicNodes.IsNull(0.0));
        Assert.False(LogicNodes.IsNull(L()));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", false)]   // whitespace is content, not emptiness
    [InlineData("x", false)]
    [InlineData(0.0, false)]
    [InlineData(false, false)]
    public void IsNullOrEmpty_Scalars(object? value, bool expected)
    {
        Assert.Equal(expected, LogicNodes.IsNullOrEmpty(value));
    }

    [Fact]
    public void IsNullOrEmpty_ListsAndDictionaries()
    {
        Assert.True(LogicNodes.IsNullOrEmpty(L()));
        Assert.False(LogicNodes.IsNullOrEmpty(L(1.0)));
        Assert.True(LogicNodes.IsNullOrEmpty(new Dictionary<string, object?>()));
        Assert.False(LogicNodes.IsNullOrEmpty(new Dictionary<string, object?> { ["k"] = 1 }));
    }
}
