using System;
using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class DictionaryNodesTests
{
    [Fact]
    public void ByKeysValues_PairsKeysWithValues()
    {
        var dictionary = DictionaryNodes.ByKeysValues(
            new List<object?> { "name", "height" },
            new List<object?> { "Wall", 3.5 });

        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Wall", dictionary["name"]);
        Assert.Equal(3.5, dictionary["height"]);
    }

    [Fact]
    public void ByKeysValues_NonStringKeys_AreFormattedInvariantly()
    {
        var dictionary = DictionaryNodes.ByKeysValues(
            new List<object?> { 1.5, true },
            new List<object?> { "a", "b" });

        Assert.Equal("a", dictionary["1.5"]);
        Assert.Equal("b", dictionary["True"]);
    }

    [Fact]
    public void ByKeysValues_DuplicateKeys_LastValueWins()
    {
        var dictionary = DictionaryNodes.ByKeysValues(
            new List<object?> { "k", "k" },
            new List<object?> { 1, 2 });

        Assert.Single(dictionary);
        Assert.Equal(2, dictionary["k"]);
    }

    [Fact]
    public void ByKeysValues_LengthMismatch_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => DictionaryNodes.ByKeysValues(
            new List<object?> { "a" },
            new List<object?> { 1, 2 }));
        Assert.Contains("same number", ex.Message);
    }

    [Fact]
    public void ValueAtKey_ReturnsStoredValue()
    {
        var dictionary = new Dictionary<string, object?> { ["key"] = 42.0 };
        Assert.Equal(42.0, DictionaryNodes.ValueAtKey(dictionary, "key"));
    }

    [Fact]
    public void ValueAtKey_MissingKey_ThrowsWithKeyName()
    {
        var dictionary = new Dictionary<string, object?> { ["present"] = 1 };
        var ex = Assert.Throws<KeyNotFoundException>(() => DictionaryNodes.ValueAtKey(dictionary, "absent"));
        Assert.Contains("absent", ex.Message);
    }

    [Fact]
    public void ValueAtKey_WorksWithMultiReturnStyleDictionaries()
    {
        var multi = new Dictionary<string, object> { ["in"] = new List<object?> { 1 } };
        var value = DictionaryNodes.ValueAtKey(multi, "in");
        Assert.IsAssignableFrom<IList<object?>>(value);
    }
}
