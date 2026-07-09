using System;
using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class StringNodesTests
{
    [Fact]
    public void Concat_JoinsTwoStrings()
    {
        Assert.Equal("Hello World", StringNodes.Concat("Hello ", "World"));
    }

    [Fact]
    public void Concat_TreatsNullAsEmpty()
    {
        Assert.Equal("a", StringNodes.Concat("a", null!));
        Assert.Equal("b", StringNodes.Concat(null!, "b"));
    }

    [Fact]
    public void Join_FormatsElementsInvariantly()
    {
        var list = new List<object?> { 1.5, "a", true };
        Assert.Equal("1.5, a, True", StringNodes.Join(", ", list));
    }

    [Fact]
    public void Contains_IsOrdinal_WithOptionalIgnoreCase()
    {
        Assert.True(StringNodes.Contains("Hello World", "World"));
        Assert.False(StringNodes.Contains("Hello World", "world"));
        Assert.True(StringNodes.Contains("Hello World", "world", ignoreCase: true));
    }

    [Fact]
    public void Split_SplitsAndPreservesEmptySegments()
    {
        Assert.Equal(new[] { "a", "", "b" }, StringNodes.Split("a,,b", ","));
    }

    [Fact]
    public void Split_EmptySeparator_ReturnsWholeString()
    {
        Assert.Equal(new[] { "abc" }, StringNodes.Split("abc", ""));
    }

    [Fact]
    public void Split_SupportsMultiCharacterSeparators()
    {
        Assert.Equal(new[] { "a", "b" }, StringNodes.Split("a--b", "--"));
    }

    [Fact]
    public void Replace_ReplacesAllOccurrences()
    {
        Assert.Equal("b.b.b", StringNodes.Replace("a.a.a", "a", "b"));
    }

    [Fact]
    public void Replace_EmptySearch_Throws()
    {
        Assert.Throws<ArgumentException>(() => StringNodes.Replace("abc", "", "x"));
    }

    [Fact]
    public void Length_CountsCharacters()
    {
        Assert.Equal(0, StringNodes.Length(""));
        Assert.Equal(5, StringNodes.Length("hello"));
    }

    [Fact]
    public void ToNumber_ParsesInvariantCulture()
    {
        Assert.Equal(3.14, StringNodes.ToNumber("3.14"), 12);
        Assert.Equal(-2, StringNodes.ToNumber(" -2 "), 12);
        Assert.Equal(1500, StringNodes.ToNumber("1.5e3"), 12);
    }

    [Fact]
    public void ToNumber_InvalidText_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<FormatException>(() => StringNodes.ToNumber("not a number"));
        Assert.Contains("not a number", ex.Message);
    }

    [Fact]
    public void FromObject_FormatsScalarsListsAndNull()
    {
        Assert.Equal("1.5", StringNodes.FromObject(1.5));
        Assert.Equal("null", StringNodes.FromObject(null));
        Assert.Equal("[1, 2, [3]]", StringNodes.FromObject(
            new List<object?> { 1, 2, new List<object?> { 3 } }));
    }
}
