using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class DictionaryExtraNodesTests
{
    private static Dictionary<string, object?> Wall() => new Dictionary<string, object?>
    {
        ["name"] = "Wall",
        ["height"] = 3.5,
        ["tag"] = null,
    };

    private static List<object?> Items(params object?[] items) => new List<object?>(items);

    // ── ContainsKey ─────────────────────────────────────────────────────────

    [Fact]
    public void ContainsKey_FindsExistingKeys_CaseSensitively()
    {
        Assert.True(DictionaryExtraNodes.ContainsKey(Wall(), "name"));
        Assert.False(DictionaryExtraNodes.ContainsKey(Wall(), "Name"));
        Assert.False(DictionaryExtraNodes.ContainsKey(Wall(), "missing"));
    }

    [Fact]
    public void ContainsKey_AKeyWithANullValueIsStillThere()
    {
        Assert.True(DictionaryExtraNodes.ContainsKey(Wall(), "tag"));
    }

    [Fact]
    public void ContainsKey_NonStringKeysMatchByTheirText()
    {
        var dictionary = new Dictionary<int, string> { [5] = "five" };
        Assert.True(DictionaryExtraNodes.ContainsKey(dictionary, "5"));
        Assert.False(DictionaryExtraNodes.ContainsKey(dictionary, "6"));
        Assert.True(DictionaryExtraNodes.ContainsKey(new Hashtable { [1.5] = "x" }, "1.5"));
    }

    [Fact]
    public void ContainsKey_EmptyDictionaryAndEmptyKey()
    {
        Assert.False(DictionaryExtraNodes.ContainsKey(new Dictionary<string, object?>(), "a"));
        Assert.True(DictionaryExtraNodes.ContainsKey(new Dictionary<string, object?> { [""] = 1 }, ""));
    }

    [Fact]
    public void ContainsKey_NullInputsThrow()
    {
        var dictionary = Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.ContainsKey(null!, "a"));
        Assert.Contains("Dictionary.ContainsKey requires a dictionary", dictionary.Message);
        var key = Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.ContainsKey(Wall(), null!));
        Assert.Contains("Dictionary.ContainsKey requires a key", key.Message);
    }

    // ── RemoveKey ───────────────────────────────────────────────────────────

    [Fact]
    public void RemoveKey_ReturnsACopyWithoutTheKey()
    {
        var original = Wall();
        var result = DictionaryExtraNodes.RemoveKey(original, "height");

        Assert.Equal(new[] { "name", "tag" }, result.Keys);
        Assert.Equal("Wall", result["name"]);
        Assert.Equal(3, original.Count);
        Assert.Equal(3.5, original["height"]);
        Assert.NotSame(original, result);
    }

    [Fact]
    public void RemoveKey_MissingKeyIsFine()
    {
        var result = DictionaryExtraNodes.RemoveKey(Wall(), "nope");
        Assert.Equal(3, result.Count);
        Assert.Empty(DictionaryExtraNodes.RemoveKey(new Dictionary<string, object?>(), "nope"));
    }

    [Fact]
    public void RemoveKey_IsCaseSensitive_AndWorksOnNonStringKeys()
    {
        Assert.Equal(3, DictionaryExtraNodes.RemoveKey(Wall(), "NAME").Count);

        var numbered = new Dictionary<int, string> { [1] = "a", [2] = "b" };
        var result = DictionaryExtraNodes.RemoveKey(numbered, "1");
        Assert.Equal(new[] { "2" }, result.Keys);
        Assert.Equal(2, numbered.Count);
    }

    [Fact]
    public void RemoveKey_NullInputsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.RemoveKey(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.RemoveKey(Wall(), null!));
    }

    // ── Merge ───────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_LaterDictionariesWin()
    {
        var first = new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 };
        var second = new Dictionary<string, object?> { ["b"] = 20, ["c"] = 30 };
        var third = new Dictionary<string, object?> { ["c"] = 300 };

        var merged = DictionaryExtraNodes.Merge(Items(first, second, third));

        Assert.Equal(3, merged.Count);
        Assert.Equal(1, merged["a"]);
        Assert.Equal(20, merged["b"]);
        Assert.Equal(300, merged["c"]);
    }

    [Fact]
    public void Merge_LeavesTheInputsUntouched()
    {
        var first = new Dictionary<string, object?> { ["a"] = 1 };
        var second = new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 };

        var merged = DictionaryExtraNodes.Merge(Items(first, second));

        Assert.Equal(1, first["a"]);
        Assert.Single(first);
        Assert.Equal(2, second.Count);
        Assert.NotSame(first, merged);
    }

    [Fact]
    public void Merge_EmptyOrSingle()
    {
        Assert.Empty(DictionaryExtraNodes.Merge(Items()));
        var only = DictionaryExtraNodes.Merge(Items(Wall()));
        Assert.Equal(3, only.Count);
        Assert.Equal("Wall", only["name"]);
    }

    [Fact]
    public void Merge_MixesDictionaryTypes_AndTextsTheKeys()
    {
        var merged = DictionaryExtraNodes.Merge(Items(
            new Dictionary<int, string> { [1] = "one" },
            new Hashtable { ["1"] = "uno", ["x"] = "y" }));

        Assert.Equal("uno", merged["1"]);
        Assert.Equal("y", merged["x"]);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Merge_NonDictionaryItem_NamesTheIndex()
    {
        var ex = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.Merge(Items(Wall(), 5.0)));
        Assert.Contains("item 1", ex.Message);
        Assert.Contains("Double", ex.Message);
        Assert.Contains("not a dictionary", ex.Message);

        var text = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.Merge(Items(Wall(), Wall(), "oops")));
        Assert.Contains("item 2", text.Message);
        Assert.Contains("String", text.Message);
    }

    [Fact]
    public void Merge_NullItemAndNullList()
    {
        var item = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.Merge(Items((object?)null)));
        Assert.Contains("item 0 is null", item.Message);
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.Merge(null!));
    }

    // ── Count ───────────────────────────────────────────────────────────────

    [Fact]
    public void Count_ReturnsTheNumberOfEntries()
    {
        Assert.Equal(3, DictionaryExtraNodes.Count(Wall()));
        Assert.Equal(0, DictionaryExtraNodes.Count(new Dictionary<string, object?>()));
        Assert.Equal(2, DictionaryExtraNodes.Count(new Hashtable { [1] = 1, [2] = 2 }));
    }

    [Fact]
    public void Count_NullThrows()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.Count(null!));
        Assert.Contains("Dictionary.Count requires a dictionary", ex.Message);
    }

    // ── FromRows / ToRows ───────────────────────────────────────────────────

    [Fact]
    public void FromRows_BuildsFromKeyValuePairs()
    {
        var result = DictionaryExtraNodes.FromRows(Items(Items("a", 1), Items("b", "two")));

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["a"]);
        Assert.Equal("two", result["b"]);
    }

    [Fact]
    public void FromRows_KeysAreTextInvariant_ExtraCellsIgnored_LastDuplicateWins()
    {
        var result = DictionaryExtraNodes.FromRows(Items(
            Items(1.5, "x", "ignored"),
            Items(true, "y"),
            Items("k", 1),
            new object?[] { "k", 2 }));

        Assert.Equal("x", result["1.5"]);
        Assert.Equal("y", result["True"]);
        Assert.Equal(2, result["k"]);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void FromRows_ANullValueCellIsKept()
    {
        var result = DictionaryExtraNodes.FromRows(Items(Items("a", null)));
        Assert.True(result.ContainsKey("a"));
        Assert.Null(result["a"]);
    }

    [Fact]
    public void FromRows_Empty()
    {
        Assert.Empty(DictionaryExtraNodes.FromRows(Items()));
    }

    [Fact]
    public void FromRows_ShortRow_NamesTheRowIndex()
    {
        var ex = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.FromRows(Items(Items("a", 1), Items("lonely"))));
        Assert.Contains("row 1", ex.Message);
        Assert.Contains("1 cell(s)", ex.Message);

        var empty = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.FromRows(Items(Items())));
        Assert.Contains("row 0", empty.Message);
    }

    [Fact]
    public void FromRows_NotARow_NamesTheRowIndex()
    {
        var text = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.FromRows(Items(Items("a", 1), "oops")));
        Assert.Contains("row 1", text.Message);
        Assert.Contains("String", text.Message);

        var nothing = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.FromRows(Items((object?)null)));
        Assert.Contains("row 0", nothing.Message);
        Assert.Contains("null", nothing.Message);
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.FromRows(null!));
    }

    [Fact]
    public void ToRows_ListsKeyValuePairsInOrder()
    {
        var rows = DictionaryExtraNodes.ToRows(new Dictionary<string, object?> { ["b"] = 2, ["a"] = 1 });

        Assert.Equal(2, rows.Count);
        Assert.Equal(new object?[] { "b", 2 }, Assert.IsAssignableFrom<IList<object?>>(rows[0]).ToArray());
        Assert.Equal(new object?[] { "a", 1 }, Assert.IsAssignableFrom<IList<object?>>(rows[1]).ToArray());
    }

    [Fact]
    public void ToRows_KeysBecomeText_AndEmptyGivesNoRows()
    {
        var rows = DictionaryExtraNodes.ToRows(new Dictionary<int, string> { [7] = "seven" });
        Assert.Equal(new object?[] { "7", "seven" }, Assert.IsAssignableFrom<IList<object?>>(rows[0]).ToArray());
        Assert.Empty(DictionaryExtraNodes.ToRows(new Dictionary<string, object?>()));
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.ToRows(null!));
    }

    [Fact]
    public void ToRows_ThenFromRows_RoundTrips()
    {
        var original = Wall();
        var again = DictionaryExtraNodes.FromRows(DictionaryExtraNodes.ToRows(original));

        Assert.Equal(original.Keys, again.Keys);
        foreach (var key in original.Keys)
        {
            Assert.Equal(original[key], again[key]);
        }
    }

    // ── ValueOrDefault ──────────────────────────────────────────────────────

    [Fact]
    public void ValueOrDefault_ReturnsTheStoredValue()
    {
        Assert.Equal("Wall", DictionaryExtraNodes.ValueOrDefault(Wall(), "name", "fallback"));
        Assert.Equal(3.5, DictionaryExtraNodes.ValueOrDefault(Wall(), "height", 0.0));
    }

    [Fact]
    public void ValueOrDefault_MissingKeyReturnsTheDefault()
    {
        Assert.Equal("fallback", DictionaryExtraNodes.ValueOrDefault(Wall(), "missing", "fallback"));
        Assert.Equal(42, DictionaryExtraNodes.ValueOrDefault(Wall(), "NAME", 42));
        Assert.Null(DictionaryExtraNodes.ValueOrDefault(Wall(), "missing", null));
        Assert.Null(DictionaryExtraNodes.ValueOrDefault(Wall(), "missing"));
    }

    [Fact]
    public void ValueOrDefault_AStoredNullIsNotReplacedByTheDefault()
    {
        Assert.Null(DictionaryExtraNodes.ValueOrDefault(Wall(), "tag", "fallback"));
    }

    [Fact]
    public void ValueOrDefault_NonStringKeys_AndNullInputs()
    {
        Assert.Equal("five", DictionaryExtraNodes.ValueOrDefault(new Dictionary<int, string> { [5] = "five" }, "5", "x"));
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.ValueOrDefault(null!, "a", 1));
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.ValueOrDefault(Wall(), null!, 1));
    }

    // ── Invert ──────────────────────────────────────────────────────────────

    [Fact]
    public void Invert_SwapsKeysAndValues()
    {
        var result = DictionaryExtraNodes.Invert(new Dictionary<string, object?> { ["a"] = "x", ["b"] = "y" });

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result["x"]);
        Assert.Equal("b", result["y"]);
    }

    [Fact]
    public void Invert_ValuesBecomeInvariantText_AndNonStringKeysBecomeTextValues()
    {
        var result = DictionaryExtraNodes.Invert(new Dictionary<int, object?> { [1] = 1.5, [2] = true, [3] = null });

        Assert.Equal("1", result["1.5"]);
        Assert.Equal("2", result["True"]);
        Assert.Equal("3", result["null"]);
    }

    [Fact]
    public void Invert_DuplicateValuesLastWins()
    {
        var result = DictionaryExtraNodes.Invert(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 1, ["c"] = 2 });

        Assert.Equal(2, result.Count);
        Assert.Equal("b", result["1"]);
        Assert.Equal("c", result["2"]);
    }

    [Fact]
    public void Invert_EmptyAndNull()
    {
        Assert.Empty(DictionaryExtraNodes.Invert(new Dictionary<string, object?>()));
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.Invert(null!));
    }

    [Fact]
    public void Invert_TwiceRestoresAUniqueTextDictionary()
    {
        var original = new Dictionary<string, object?> { ["a"] = "x", ["b"] = "y" };
        var twice = DictionaryExtraNodes.Invert(DictionaryExtraNodes.Invert(original));
        Assert.Equal(original.OrderBy(p => p.Key), twice.OrderBy(p => p.Key));
    }

    // ── Roles and the engine ────────────────────────────────────────────────

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static ZeroTouchNodeModel Create(NodeRegistry registry, string nodeName)
    {
        return new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == nodeName));
    }

    private static void Wire(GraphModel graph, NodeModel from, NodeModel to, string inPort)
    {
        var target = to.InPorts.First(p => p.Name == inPort);
        Assert.True(graph.Connect(from.OutPorts[0], target).Success, "could not wire into " + to.Name + "." + inPort);
    }

    [Fact]
    public void RegisterAll_ImportsEveryDictionaryExtraNode()
    {
        var names = CreateRegistry().Definitions.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[]
        {
            "Dictionary.ContainsKey", "Dictionary.RemoveKey", "Dictionary.Merge", "Dictionary.Count",
            "Dictionary.FromRows", "Dictionary.ToRows", "Dictionary.ValueOrDefault", "Dictionary.Invert",
        })
        {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void Roles_TestsAndCountsAreInfo_AndBuildersAreCreate()
    {
        var definitions = CreateRegistry().Definitions.ToDictionary(d => d.Name, StringComparer.Ordinal);

        Assert.Equal(NodeFunction.Info, definitions["Dictionary.ContainsKey"].Function);
        Assert.Equal(NodeFunction.Info, definitions["Dictionary.Count"].Function);
        foreach (var name in new[] { "Dictionary.RemoveKey", "Dictionary.Merge", "Dictionary.FromRows", "Dictionary.ToRows", "Dictionary.Invert" })
        {
            Assert.Equal(NodeFunction.Create, definitions[name].Function);
        }
    }

    [Fact]
    public void Definitions_MergeIsMultiInput_AndTheDefaultIsOptional()
    {
        var definitions = CreateRegistry().Definitions.ToDictionary(d => d.Name, StringComparer.Ordinal);

        Assert.True(definitions["Dictionary.Merge"].Inputs.Single().MultiInput);
        Assert.True(definitions["Dictionary.ValueOrDefault"].Inputs.Single(i => i.Name == "defaultValue").HasDefault);
    }

    [Fact]
    public void Engine_MergeTakesSeveralWires_ThenCount()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var keysA = new ListCreateNode();
        var valuesA = new ListCreateNode();
        var keysB = new ListCreateNode();
        var valuesB = new ListCreateNode();
        var a1 = new StringInputNode { Value = "a" };
        var a2 = new StringInputNode { Value = "b" };
        var av1 = new NumberInputNode { Value = 1 };
        var av2 = new NumberInputNode { Value = 2 };
        var b1 = new StringInputNode { Value = "b" };
        var bv1 = new NumberInputNode { Value = 20 };
        var first = Create(registry, "Dictionary.ByKeysValues");
        var second = Create(registry, "Dictionary.ByKeysValues");
        var merge = Create(registry, "Dictionary.Merge");
        var count = Create(registry, "Dictionary.Count");
        var lookup = Create(registry, "Dictionary.ValueOrDefault");
        var fallbackKey = new StringInputNode { Value = "zzz" };
        var fallback = new NumberInputNode { Value = -1 };
        var existing = new StringInputNode { Value = "b" };
        var lookupExisting = Create(registry, "Dictionary.ValueOrDefault");
        var all = new NodeModel[]
        {
            keysA, valuesA, keysB, valuesB, a1, a2, av1, av2, b1, bv1, first, second, merge, count, lookup,
            fallbackKey, fallback, existing, lookupExisting,
        };
        foreach (var node in all)
        {
            graph.AddNode(node);
        }

        keysA.AddItemPort();
        valuesA.AddItemPort();
        Assert.True(graph.Connect(a1.OutPorts[0], keysA.InPorts[0]).Success);
        Assert.True(graph.Connect(a2.OutPorts[0], keysA.InPorts[1]).Success);
        Assert.True(graph.Connect(av1.OutPorts[0], valuesA.InPorts[0]).Success);
        Assert.True(graph.Connect(av2.OutPorts[0], valuesA.InPorts[1]).Success);
        Assert.True(graph.Connect(b1.OutPorts[0], keysB.InPorts[0]).Success);
        Assert.True(graph.Connect(bv1.OutPorts[0], valuesB.InPorts[0]).Success);
        Wire(graph, keysA, first, "keys");
        Wire(graph, valuesA, first, "values");
        Wire(graph, keysB, second, "keys");
        Wire(graph, valuesB, second, "values");
        Wire(graph, first, merge, "dictionaries");
        Wire(graph, second, merge, "dictionaries");
        Wire(graph, merge, count, "dictionary");
        Wire(graph, merge, lookup, "dictionary");
        Wire(graph, fallbackKey, lookup, "key");
        Wire(graph, fallback, lookup, "defaultValue");
        Wire(graph, merge, lookupExisting, "dictionary");
        Wire(graph, existing, lookupExisting, "key");
        Wire(graph, fallback, lookupExisting, "defaultValue");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(2, count.OutPorts[0].Value);
        Assert.Equal(-1d, lookup.OutPorts[0].Value);
        Assert.Equal(20d, lookupExisting.OutPorts[0].Value);
    }

    [Fact]
    public void Engine_NonDictionaryWireIntoMergeBecomesARedNode()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var number = new NumberInputNode { Value = 5 };
        var merge = Create(registry, "Dictionary.Merge");
        graph.AddNode(number);
        graph.AddNode(merge);
        Wire(graph, number, merge, "dictionaries");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success); // the run completes; the failure is on the node
        Assert.Equal(NodeState.Error, merge.State);
        Assert.Contains("not a dictionary", merge.StateMessage);
    }
}
