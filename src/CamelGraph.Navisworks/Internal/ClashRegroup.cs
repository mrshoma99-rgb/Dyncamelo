using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Execution;
using CamelGraph.Nodes.Coordination;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// The one place where the "regroup a whole test" nodes (Clash.GroupResultsByStatus, ...ByGridIntersection,
/// ...BySameItem, ...ByProximity, ...ByLevel) rebuild a stored test's result tree and commit it. Every existing group of the
/// test is dissolved by the rebuild and a bucket of fewer than two results stays loose; both are reported as warnings
/// (<see cref="ClashRegroupNotes"/>). Internal — never surfaced as a node.
/// </summary>
internal static class ClashRegroup
{
    /// <summary>
    /// Rebuilds a stored test's result tree from a partition and commits it in one
    /// <see cref="ClashHelpers.CommitTestTree"/> edit (a document transaction around <c>TestsReplaceWithCopy</c>; the
    /// older <c>TestsEditTestFromCopy</c> ignores the children and changed nothing): buckets of two or more become
    /// named <see cref="ClashResultGroup"/>s, singletons stay ungrouped.
    /// </summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="document">The document (null = the active document).</param>
    /// <param name="partition">Buckets the test's results: a name and the results in it, first-seen order.</param>
    /// <returns>The "test" (re-located after the commit) and "groupCount" outputs the grouping nodes return.</returns>
    internal static Dictionary<string, object?> Commit(
        ClashTest test,
        Document? document,
        Func<List<ClashResult>, List<KeyValuePair<string, List<ClashResult>>>> partition)
    {
        ClashTest stored;
        try
        {
            stored = ClashHelpers.RequireStoredTest(test);
        }
        catch (Exception ex) when (ClashHelpers.IsDisposed(ex))
        {
            throw ClashHelpers.StaleInputError("clash test", ex);
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        var copy = (ClashTest)stored.CreateCopy();
        var flattened = ClashHelpers.FlattenResults(copy);

        // Detach every result from the copy's tree before rebuilding it —
        // Children.Clear() below destroys the originals.
        var detached = new List<ClashResult>(flattened.Count);
        foreach (var result in flattened)
        {
            detached.Add((ClashResult)result.CreateCopy());
        }

        var buckets = partition(detached);
        var layout = ClashGroupLayout.Build(buckets);

        // Say what the regrouping does to the tree that is there: it dissolves every existing group, and a bucket of one stays loose.
        var dissolvedGroups = 0;
        foreach (var child in copy.Children)
        {
            if (child is ClashResultGroup)
            {
                dissolvedGroups++;
            }
        }

        var singleBucketNames = new List<string>();
        foreach (var bucket in buckets)
        {
            if (bucket.Value.Count < 2)
            {
                singleBucketNames.Add(bucket.Key);
            }
        }

        foreach (var note in ClashRegroupNotes.Build(dissolvedGroups, singleBucketNames, layout.Singles.Count))
        {
            NodeWarnings.Add(note);
        }

        copy.Children.Clear();
        foreach (var bucket in layout.Groups)
        {
            var group = new ClashResultGroup { DisplayName = bucket.Key };
            foreach (var result in bucket.Value)
            {
                group.Children.Add(result);
            }

            copy.Children.Add(group);
        }

        foreach (var result in layout.Singles)
        {
            copy.Children.Add(result);
        }

        // TestsEditTestFromCopy ignores the children tree — the tree must be
        // committed with TestsReplaceWithCopy (see ClashHelpers.CommitTestTree).
        var refreshed = ClashHelpers.CommitTestTree(doc, clash, stored, copy, "Group clash results");
        return new Dictionary<string, object?>
        {
            ["test"] = refreshed,
            ["groupCount"] = layout.Groups.Count,
        };
    }
}
