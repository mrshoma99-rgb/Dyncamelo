using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class SnapshotNodesTests
{
    private static Dictionary<string, object?> Dict(params (string Key, object? Value)[] entries)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }

    private static List<string> Keys(Dictionary<string, object> result, string port) =>
        Assert.IsType<List<string>>(result[port]);

    // ---------------------------------------------------------------- Basics

    [Fact]
    public void Diff_SplitsAddedRemovedChanged()
    {
        var oldValue = Dict(("kept", 1.0), ("gone", 2.0), ("edited", "a"));
        var newValue = Dict(("kept", 1.0), ("edited", "b"), ("fresh", 3.0));

        var result = SnapshotNodes.Diff(oldValue, newValue);

        Assert.Equal(new[] { "fresh" }, Keys(result, "addedKeys"));
        Assert.Equal(new[] { "gone" }, Keys(result, "removedKeys"));
        Assert.Equal(new[] { "edited" }, Keys(result, "changedKeys"));
    }

    [Fact]
    public void Diff_IdenticalDictionaries_ReportNothing()
    {
        var value = Dict(("a", 1.0), ("b", Dict(("x", true))));
        var same = Dict(("a", 1.0), ("b", Dict(("x", true))));

        var result = SnapshotNodes.Diff(value, same);

        Assert.Empty(Keys(result, "addedKeys"));
        Assert.Empty(Keys(result, "removedKeys"));
        Assert.Empty(Keys(result, "changedKeys"));
    }

    [Fact]
    public void Diff_EmptyDictionaries_ReportNothing()
    {
        var result = SnapshotNodes.Diff(Dict(), Dict());

        Assert.Empty(Keys(result, "addedKeys"));
        Assert.Empty(Keys(result, "removedKeys"));
        Assert.Empty(Keys(result, "changedKeys"));
    }

    [Fact]
    public void Diff_OutputsKeepDictionaryOrder()
    {
        var oldValue = Dict(("r2", 1.0), ("r1", 1.0), ("c2", 1.0), ("c1", 1.0));
        var newValue = Dict(("c2", 9.0), ("c1", 9.0), ("a2", 1.0), ("a1", 1.0));

        var result = SnapshotNodes.Diff(oldValue, newValue);

        Assert.Equal(new[] { "a2", "a1" }, Keys(result, "addedKeys"));
        Assert.Equal(new[] { "r2", "r1" }, Keys(result, "removedKeys"));
        Assert.Equal(new[] { "c2", "c1" }, Keys(result, "changedKeys"));
    }

    // ------------------------------------------------------- Value equality

    [Fact]
    public void Diff_NestedDictionaryKeyOrder_DoesNotCountAsChange()
    {
        var oldValue = Dict(("guid", Dict(("a", 1.0), ("b", 2.0))));
        var newValue = Dict(("guid", Dict(("b", 2.0), ("a", 1.0))));

        var result = SnapshotNodes.Diff(oldValue, newValue);

        Assert.Empty(Keys(result, "changedKeys"));
    }

    [Fact]
    public void Diff_ListOrderMatters()
    {
        var oldValue = Dict(("guid", new List<object?> { 1.0, 2.0 }));
        var newValue = Dict(("guid", new List<object?> { 2.0, 1.0 }));

        var result = SnapshotNodes.Diff(oldValue, newValue);

        Assert.Equal(new[] { "guid" }, Keys(result, "changedKeys"));
    }

    [Fact]
    public void Diff_TypeChangeCountsAsChange()
    {
        var result = SnapshotNodes.Diff(Dict(("k", "1")), Dict(("k", 1.0)));

        Assert.Equal(new[] { "k" }, Keys(result, "changedKeys"));
    }

    [Fact]
    public void Diff_NullValues_CompareByJson()
    {
        var unchanged = SnapshotNodes.Diff(Dict(("k", null)), Dict(("k", null)));
        Assert.Empty(Keys(unchanged, "changedKeys"));

        var changed = SnapshotNodes.Diff(Dict(("k", null)), Dict(("k", 0.0)));
        Assert.Equal(new[] { "k" }, Keys(changed, "changedKeys"));
    }

    [Fact]
    public void Diff_DeepNestedChange_IsDetected()
    {
        var oldValue = Dict(("guid", Dict(("props", new List<object?> { Dict(("v", 1.0)) }))));
        var newValue = Dict(("guid", Dict(("props", new List<object?> { Dict(("v", 2.0)) }))));

        var result = SnapshotNodes.Diff(oldValue, newValue);

        Assert.Equal(new[] { "guid" }, Keys(result, "changedKeys"));
    }

    // ------------------------------------------------------------ Integration

    [Fact]
    public void Diff_SurvivesJsonRoundTrip_NoFalseChanges()
    {
        // The intended workflow: snapshot → JSON.Stringify (write) → JSON.Parse
        // (read back on the next run) → Snapshot.Diff. A round-tripped snapshot
        // must not report phantom changes.
        var snapshot = Dict(
            ("g1", Dict(("Name", "Wall"), ("Volume", 1.25), ("Fire Rated", true))),
            ("g2", new List<object?> { "a", 2.0, false, null }));

        var roundTripped = Assert.IsAssignableFrom<IDictionary>(
            FileNodes.ParseJson(FileNodes.StringifyJson(snapshot)));

        var result = SnapshotNodes.Diff(snapshot, roundTripped);

        Assert.Empty(Keys(result, "addedKeys"));
        Assert.Empty(Keys(result, "removedKeys"));
        Assert.Empty(Keys(result, "changedKeys"));
    }

    [Fact]
    public void Diff_NonStringKeys_AreFormattedInvariantly()
    {
        var oldValue = new Hashtable { [42] = "a" };
        var newValue = new Hashtable { [42.0] = "b" };

        var result = SnapshotNodes.Diff(oldValue, newValue);

        Assert.Equal(new[] { "42" }, Keys(result, "changedKeys"));
        Assert.Empty(Keys(result, "addedKeys"));
        Assert.Empty(Keys(result, "removedKeys"));
    }

    // --------------------------------------------------------------- Errors

    [Fact]
    public void Diff_NullArguments_ThrowWithNodeName()
    {
        Assert.Contains("Snapshot.Diff", Assert.Throws<ArgumentNullException>(
            () => SnapshotNodes.Diff(null!, Dict())).Message);
        Assert.Contains("Snapshot.Diff", Assert.Throws<ArgumentNullException>(
            () => SnapshotNodes.Diff(Dict(), null!)).Message);
    }
}
