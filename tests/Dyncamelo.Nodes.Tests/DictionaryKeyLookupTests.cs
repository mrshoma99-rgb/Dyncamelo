using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// How Dictionary.ContainsKey and Dictionary.ValueOrDefault find a key: the dictionary's own lookup first (so its comparer decides
/// what the same text means), then, only in a dictionary that can hold keys that are not strings, the key whose text is the key.
/// A miss is the normal answer of both nodes and must not walk a dictionary that holds nothing but string keys.
/// </summary>
public class DictionaryKeyLookupTests
{
    // A string-keyed dictionary that counts how often anything walks over its entries.
    private sealed class WalkCounter : IDictionary<string, object?>, IDictionary
    {
        private readonly Dictionary<string, object?> _inner = new Dictionary<string, object?>(StringComparer.Ordinal);

        public int Walks { get; private set; }

        public object? this[string key] { get => _inner[key]; set => _inner[key] = value; }

        public object? this[object key] { get => _inner[(string)key]; set => _inner[(string)key] = value; }

        public ICollection<string> Keys { get { Walks++; return _inner.Keys; } }

        public ICollection<object?> Values { get { Walks++; return _inner.Values; } }

        ICollection IDictionary.Keys { get { Walks++; return _inner.Keys; } }

        ICollection IDictionary.Values { get { Walks++; return _inner.Values; } }

        public int Count => _inner.Count;

        public bool IsReadOnly => false;

        public bool IsFixedSize => false;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public void Add(string key, object? value) => _inner.Add(key, value);

        public void Add(object key, object? value) => _inner.Add((string)key, value);

        public void Add(KeyValuePair<string, object?> item) => _inner.Add(item.Key, item.Value);

        public void Clear() => _inner.Clear();

        public bool Contains(KeyValuePair<string, object?> item) => ((ICollection<KeyValuePair<string, object?>>)_inner).Contains(item);

        public bool Contains(object key) => key is string text && _inner.ContainsKey(text);

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object?>>)_inner).CopyTo(array, arrayIndex);

        public void CopyTo(Array array, int index) => ((ICollection)_inner).CopyTo(array, index);

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            Walks++;
            return _inner.GetEnumerator();
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            Walks++;
            return ((IDictionary)_inner).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            Walks++;
            return _inner.GetEnumerator();
        }

        public bool Remove(string key) => _inner.Remove(key);

        public bool Remove(KeyValuePair<string, object?> item) => ((ICollection<KeyValuePair<string, object?>>)_inner).Remove(item);

        public void Remove(object key) => _inner.Remove((string)key);

        public bool TryGetValue(string key, out object? value) => _inner.TryGetValue(key, out value);
    }

    [Fact]
    public void AMissInAStringKeyedDictionaryDoesNotWalkIt()
    {
        var counting = new WalkCounter { ["a"] = 1, ["b"] = null };

        Assert.False(DictionaryExtraNodes.ContainsKey(counting, "missing"));
        Assert.Equal("fallback", DictionaryExtraNodes.ValueOrDefault(counting, "missing", "fallback"));
        Assert.Null(DictionaryExtraNodes.ValueOrDefault(counting, "missing"));
        Assert.Equal(0, counting.Walks);

        // A hit and a key with a null value do not walk it either.
        Assert.True(DictionaryExtraNodes.ContainsKey(counting, "b"));
        Assert.Equal(1, DictionaryExtraNodes.ValueOrDefault(counting, "a", "fallback"));
        Assert.Null(DictionaryExtraNodes.ValueOrDefault(counting, "b", "fallback"));
        Assert.Equal(0, counting.Walks);
    }

    [Fact]
    public void TheCommonStringKeyedDictionariesAreAllHitsAndMissesWithoutAWalk()
    {
        IDictionary[] dictionaries =
        {
            new Dictionary<string, object?> { ["Name"] = "wall" },
            new Dictionary<string, int> { ["Name"] = 7 },
            new SortedDictionary<string, string> { ["Name"] = "wall" },
            new SortedList<string, string> { ["Name"] = "wall" },
            new ConcurrentDictionary<string, string>(new[] { new KeyValuePair<string, string>("Name", "wall") }),
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["Name"] = "wall" }),
        };

        foreach (var dictionary in dictionaries)
        {
            Assert.True(DictionaryExtraNodes.ContainsKey(dictionary, "Name"), dictionary.GetType().Name);
            Assert.False(DictionaryExtraNodes.ContainsKey(dictionary, "name"), dictionary.GetType().Name);
            Assert.False(DictionaryExtraNodes.ContainsKey(dictionary, "1"), dictionary.GetType().Name);
            Assert.Equal("dflt", DictionaryExtraNodes.ValueOrDefault(dictionary, "other", "dflt"));
            Assert.NotEqual("dflt", DictionaryExtraNodes.ValueOrDefault(dictionary, "Name", "dflt"));
        }
    }

    [Fact]
    public void ADictionarysOwnComparerStillDecidesWhatTheSameKeyIs()
    {
        var insensitive = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "wall" };

        Assert.True(DictionaryExtraNodes.ContainsKey(insensitive, "NAME"));
        Assert.Equal("wall", DictionaryExtraNodes.ValueOrDefault(insensitive, "name", "dflt"));
        Assert.False(DictionaryExtraNodes.ContainsKey(insensitive, "Names"));
    }

    [Fact]
    public void KeysThatAreNotStringsAreFoundByTheirInvariantText()
    {
        var numbered = new Dictionary<int, string> { [1] = "one", [25] = "twenty-five" };
        Assert.True(DictionaryExtraNodes.ContainsKey(numbered, "1"));
        Assert.Equal("twenty-five", DictionaryExtraNodes.ValueOrDefault(numbered, "25", "dflt"));
        Assert.False(DictionaryExtraNodes.ContainsKey(numbered, "01"));
        Assert.False(DictionaryExtraNodes.ContainsKey(numbered, "2"));
        Assert.Equal("dflt", DictionaryExtraNodes.ValueOrDefault(numbered, "2", "dflt"));

        var table = new Hashtable { [true] = "yes", [1.5] = "x", [new DateTime(2024, 1, 2)] = "day" };
        Assert.Equal("yes", DictionaryExtraNodes.ValueOrDefault(table, "True", "dflt"));
        Assert.Equal("x", DictionaryExtraNodes.ValueOrDefault(table, "1.5", "dflt"));
        Assert.Equal("day", DictionaryExtraNodes.ValueOrDefault(table, "01/02/2024 00:00:00", "dflt"));
        Assert.False(DictionaryExtraNodes.ContainsKey(table, "true"));
    }

    [Fact]
    public void AnObjectKeyedDictionaryMixesStringAndOtherKeys()
    {
        // A string key is matched by the dictionary (so "A" is not "a"), a key of another type by its text; the key that is a
        // string wins over a key of another type with the same text, and of several other keys with the same text the first wins.
        var mixed = new Dictionary<object, object?>
        {
            ["a"] = "string a",
            ["1"] = "string one",
            [1] = "int one",
            [1L] = "long one",
            [2] = "int two",
            [2.5] = "double",
        };

        Assert.Equal("string a", DictionaryExtraNodes.ValueOrDefault(mixed, "a", "dflt"));
        Assert.Equal("dflt", DictionaryExtraNodes.ValueOrDefault(mixed, "A", "dflt"));
        Assert.Equal("string one", DictionaryExtraNodes.ValueOrDefault(mixed, "1", "dflt"));
        Assert.Equal("int two", DictionaryExtraNodes.ValueOrDefault(mixed, "2", "dflt"));
        Assert.Equal("double", DictionaryExtraNodes.ValueOrDefault(mixed, "2.5", "dflt"));
        Assert.True(DictionaryExtraNodes.ContainsKey(mixed, "2.5"));
        Assert.False(DictionaryExtraNodes.ContainsKey(mixed, "3"));

        var withoutTheString = new Dictionary<object, object?> { [1] = "int one", [1L] = "long one", [1.0] = "double one" };
        Assert.Equal("int one", DictionaryExtraNodes.ValueOrDefault(withoutTheString, "1", "dflt"));
        var otherOrder = new Dictionary<object, object?> { [1L] = "long one", [1] = "int one" };
        Assert.Equal("long one", DictionaryExtraNodes.ValueOrDefault(otherOrder, "1", "dflt"));
    }

    [Fact]
    public void ANullKeyOrDictionaryStillThrows()
    {
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.ContainsKey(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.ValueOrDefault(new Dictionary<string, object?>(), null!));
    }

    [Fact]
    public void ABigStringKeyedDictionaryAnswersManyMissesQuickly()
    {
        // 20 000 misses in a dictionary of 20 000 keys: a walk per miss would be 400 million steps.
        var big = new Dictionary<string, object?>();
        for (int i = 0; i < 20000; i++)
        {
            big["key " + i] = i;
        }

        var misses = 0;
        for (int i = 0; i < 20000; i++)
        {
            if (!DictionaryExtraNodes.ContainsKey(big, "other " + i))
            {
                misses++;
            }
        }

        Assert.Equal(20000, misses);
        Assert.Equal(19999, DictionaryExtraNodes.ValueOrDefault(big, "key 19999", -1));
    }

    [Fact]
    public void TheNodesStayRegisteredWithTheSameSockets()
    {
        // The change is inside the lookup: the nodes' inputs and outputs are what they were.
        var registry = Dyncamelo.Core.Loader.NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var contains = registry.Definitions.Single(d => d.Name == "Dictionary.ContainsKey");
        Assert.Equal(new[] { "dictionary", "key" }, contains.Inputs.Select(i => i.Name));
        Assert.Equal(new[] { "hasKey" }, contains.Outputs.Select(o => o.Name));
        var orDefault = registry.Definitions.Single(d => d.Name == "Dictionary.ValueOrDefault");
        Assert.Equal(new[] { "dictionary", "key", "defaultValue" }, orDefault.Inputs.Select(i => i.Name));
        Assert.Equal(new[] { "value" }, orDefault.Outputs.Select(o => o.Name));
    }
}
