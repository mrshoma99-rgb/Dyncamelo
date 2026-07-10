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

    [Fact]
    public void StartsWith_IgnoresCaseByDefault()
    {
        Assert.True(StringNodes.StartsWith("Basic Wall", "basic"));
        Assert.False(StringNodes.StartsWith("Basic Wall", "basic", ignoreCase: false));
        Assert.False(StringNodes.StartsWith("Basic Wall", "Wall"));
        Assert.True(StringNodes.StartsWith("anything", ""));
    }

    [Fact]
    public void EndsWith_IgnoresCaseByDefault()
    {
        Assert.True(StringNodes.EndsWith("report.CSV", ".csv"));
        Assert.False(StringNodes.EndsWith("report.CSV", ".csv", ignoreCase: false));
        Assert.False(StringNodes.EndsWith("report.CSV", "report"));
    }

    [Fact]
    public void StartsWith_NullText_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => StringNodes.StartsWith(null!, "a"));
        Assert.Contains("String.StartsWith", ex.Message);
    }

    [Fact]
    public void Substring_DefaultLength_TakesToEnd()
    {
        Assert.Equal("llo", StringNodes.Substring("hello", 2));
        Assert.Equal("", StringNodes.Substring("hello", 5));
    }

    [Fact]
    public void Substring_ExplicitLength_TakesSlice()
    {
        Assert.Equal("ell", StringNodes.Substring("hello", 1, 3));
        Assert.Equal("", StringNodes.Substring("hello", 1, 0));
    }

    [Fact]
    public void Substring_OutOfRange_ThrowsWithClearMessage()
    {
        var startEx = Assert.Throws<ArgumentOutOfRangeException>(() => StringNodes.Substring("abc", 4));
        Assert.Contains("start index 4", startEx.Message);
        Assert.Contains("3 character", startEx.Message);

        var lengthEx = Assert.Throws<ArgumentOutOfRangeException>(() => StringNodes.Substring("abc", 1, 5));
        Assert.Contains("length 5", lengthEx.Message);
    }

    [Fact]
    public void ToUpper_ToLower_UseInvariantCasing()
    {
        Assert.Equal("WALL-01", StringNodes.ToUpper("Wall-01"));
        Assert.Equal("wall-01", StringNodes.ToLower("Wall-01"));
        Assert.Equal("", StringNodes.ToUpper(""));
    }

    [Fact]
    public void Trim_StripsSurroundingWhitespace()
    {
        Assert.Equal("value", StringNodes.Trim("  value\t\n"));
        Assert.Equal("a  b", StringNodes.Trim(" a  b "));
        Assert.Equal("", StringNodes.Trim("   "));
    }

    [Fact]
    public void CaseAndTrimNodes_NullText_ThrowHelpfulMessages()
    {
        Assert.Contains("String.ToUpper", Assert.Throws<ArgumentNullException>(() => StringNodes.ToUpper(null!)).Message);
        Assert.Contains("String.ToLower", Assert.Throws<ArgumentNullException>(() => StringNodes.ToLower(null!)).Message);
        Assert.Contains("String.Trim", Assert.Throws<ArgumentNullException>(() => StringNodes.Trim(null!)).Message);
        Assert.Contains("String.Substring", Assert.Throws<ArgumentNullException>(() => StringNodes.Substring(null!, 0)).Message);
    }
}
