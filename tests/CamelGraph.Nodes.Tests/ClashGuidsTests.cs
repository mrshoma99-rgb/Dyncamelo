using System;
using System.Collections.Generic;
using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>Finding clash results again from the GUIDs that BCF topics and snapshots carry (ClashResult.ByGuid, NVC-37).</summary>
public class ClashGuidsTests
{
    private sealed class Thing
    {
        public Thing(string name) => Name = name;

        public string Name { get; }
    }

    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", true)]
    [InlineData("  11111111-1111-1111-1111-111111111111  ", true)]
    [InlineData("{11111111-1111-1111-1111-111111111111}", true)]
    [InlineData("AAAAAAAA-1111-1111-1111-111111111111", true)]
    [InlineData("11111111111111111111111111111111", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("Clash12", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AGuidIsReadWithOrWithoutBracesInAnyCase(string? text, bool expected)
    {
        Assert.Equal(expected, ClashGuids.TryParse(text, out _));
    }

    [Fact]
    public void FoundThingsComeOutInTheOrderOfTheGuidsAndTheRestIsMissing()
    {
        var a = new Thing("a");
        var b = new Thing("b");
        var candidates = new List<KeyValuePair<Guid, Thing>>
        {
            new KeyValuePair<Guid, Thing>(A, a),
            new KeyValuePair<Guid, Thing>(B, b),
        };

        ClashGuids.Match(
            new[] { B.ToString(), "not a guid", Guid.NewGuid().ToString(), "{" + A.ToString().ToUpperInvariant() + "}" },
            candidates,
            out var found,
            out var missing);

        Assert.Equal(new[] { b, a }, found);
        Assert.Equal(2, missing.Count);
        Assert.Equal("not a guid", missing[0]);
    }

    [Fact]
    public void AGuidAskedForTwiceIsFoundTwice()
    {
        var a = new Thing("a");

        ClashGuids.Match(
            new[] { A.ToString(), A.ToString() },
            new[] { new KeyValuePair<Guid, Thing>(A, a) },
            out var found,
            out var missing);

        Assert.Equal(new[] { a, a }, found);
        Assert.Empty(missing);
    }

    [Fact]
    public void ACandidateWithoutAGuidIsNeverMatched()
    {
        ClashGuids.Match(
            new[] { Guid.Empty.ToString() },
            new[] { new KeyValuePair<Guid, Thing>(Guid.Empty, new Thing("x")) },
            out var found,
            out var missing);

        Assert.Empty(found);
        Assert.Single(missing);
    }

    [Fact]
    public void EmptyEntriesAreMissingAsEmptyText()
    {
        ClashGuids.Match(new string?[] { null }, new List<KeyValuePair<Guid, Thing>>(), out _, out var missing);

        Assert.Equal(new[] { string.Empty }, missing);
    }
}
