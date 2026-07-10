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

    [Fact]
    public void Keys_ReturnsAllKeys()
    {
        var dictionary = new Dictionary<string, object?> { ["name"] = "Wall", ["height"] = 3.5 };
        Assert.Equal(new[] { "name", "height" }, DictionaryNodes.Keys(dictionary));
        Assert.Empty(DictionaryNodes.Keys(new Dictionary<string, object?>()));
    }

    [Fact]
    public void Values_ReturnsAllValues()
    {
        var dictionary = new Dictionary<string, object?> { ["name"] = "Wall", ["height"] = 3.5, ["extra"] = null };
        Assert.Equal(new object?[] { "Wall", 3.5, null }, DictionaryNodes.Values(dictionary));
    }

    [Fact]
    public void KeysAndValues_WorkWithMultiReturnStyleDictionaries()
    {
        var multi = new Dictionary<string, object> { ["in"] = 1, ["out"] = 2 };
        Assert.Equal(new[] { "in", "out" }, DictionaryNodes.Keys(multi));
        Assert.Equal(new object?[] { 1, 2 }, DictionaryNodes.Values(multi));
    }

    [Fact]
    public void SetValueAtKey_AddsNewKey_WithoutMutatingInput()
    {
        var input = new Dictionary<string, object?> { ["a"] = 1 };
        var result = DictionaryNodes.SetValueAtKey(input, "b", 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["a"]);
        Assert.Equal(2, result["b"]);
        Assert.Single(input);
        Assert.False(input.ContainsKey("b"));
    }

    [Fact]
    public void SetValueAtKey_OverwritesExistingKey()
    {
        var input = new Dictionary<string, object?> { ["a"] = 1 };
        var result = DictionaryNodes.SetValueAtKey(input, "a", "updated");
        Assert.Equal("updated", result["a"]);
        Assert.Equal(1, input["a"]);
    }

    [Fact]
    public void DictionaryNodes_NullInputs_ThrowHelpfulMessages()
    {
        Assert.Contains("Dictionary.Keys", Assert.Throws<ArgumentNullException>(() => DictionaryNodes.Keys(null!)).Message);
        Assert.Contains("Dictionary.Values", Assert.Throws<ArgumentNullException>(() => DictionaryNodes.Values(null!)).Message);
        Assert.Contains("Dictionary.SetValueAtKey", Assert.Throws<ArgumentNullException>(
            () => DictionaryNodes.SetValueAtKey(null!, "k", 1)).Message);
        Assert.Contains("key", Assert.Throws<ArgumentNullException>(
            () => DictionaryNodes.SetValueAtKey(new Dictionary<string, object?>(), null!, 1)).Message);
    }
}
