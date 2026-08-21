using System;
using Dyncamelo.Nodes.Text;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Pins the unordered property-pair matching behind Clash.FilterByItemProperty:
/// "Pipe vs Wall" reads the same whichever side the pipe landed on, empty
/// filters match anything, and missing values (null) match nothing.
/// </summary>
public class TextPairFilterTests
{
    [Fact]
    public void PairMatches_IsOrderInsensitive()
    {
        Assert.True(TextPairFilter.PairMatches("Pipe Types", "Basic Wall", "Pipe", "Wall", "contains", false));
        Assert.True(TextPairFilter.PairMatches("Basic Wall", "Pipe Types", "Pipe", "Wall", "contains", false));
        Assert.False(TextPairFilter.PairMatches("Pipe Types", "Floor Slab", "Pipe", "Wall", "contains", false));
    }

    [Fact]
    public void EmptyFilter_MatchesAnything_ForOneSidedRules()
    {
        Assert.True(TextPairFilter.PairMatches("Pipe Types", "Anything", "Pipe", "", "contains", false));
        Assert.True(TextPairFilter.PairMatches("Anything", "Pipe Types", "Pipe", null, "contains", false));
    }

    [Fact]
    public void NullText_MissingProperty_MatchesNothing_ButEmptyFilterStillPasses()
    {
        Assert.False(TextPairFilter.SideMatches(null, "Pipe", "contains", false));
        Assert.True(TextPairFilter.SideMatches(null, "", "contains", false));
        // Pair with one missing value: only satisfiable when the missing side gets the empty filter.
        Assert.True(TextPairFilter.PairMatches("Pipe Types", null, "Pipe", "", "contains", false));
        Assert.False(TextPairFilter.PairMatches("Pipe Types", null, "Pipe", "Wall", "contains", false));
    }

    [Theory]
    [InlineData("equals", "Wall", "Wall", true)]
    [InlineData("equals", "Basic Wall", "Wall", false)]
    [InlineData("starts with", "Wall-Ext-200", "Wall", true)]
    [InlineData("starts with", "Ext-Wall", "Wall", false)]
    [InlineData("ends with", "Type-Wall", "Wall", true)]
    [InlineData("ends with", "Wall-Type", "Wall", false)]
    [InlineData("contains", "Ext-Wall-200", "wall", true)]
    public void Modes_BehaveAsNamed(string mode, string text, string filter, bool expected)
    {
        Assert.Equal(expected, TextPairFilter.SideMatches(text, filter, mode, caseSensitive: false));
    }

    [Fact]
    public void CaseSensitivity_IsOptIn()
    {
        Assert.True(TextPairFilter.SideMatches("BASIC WALL", "wall", "contains", caseSensitive: false));
        Assert.False(TextPairFilter.SideMatches("BASIC WALL", "wall", "contains", caseSensitive: true));
    }

    [Fact]
    public void UnknownMode_Throws()
    {
        Assert.Throws<ArgumentException>(() => TextPairFilter.SideMatches("a", "a", "rhymes with", false));
    }
}
