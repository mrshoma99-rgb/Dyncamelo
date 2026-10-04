using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>
/// What the regrouping nodes (Clash.GroupResultsByStatus, ...ByGridIntersection, ...BySameItem, ...ByProximity, ...ByLevel) tell the
/// user about what they did to the test's existing result tree: they dissolve the groups that were there, and a bucket with a
/// single result stays loose. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ClashRegroupNotes
{
    private const int NamesShown = 3;

    /// <summary>The sentences to show as warnings; empty when the regrouping changed nothing the user could be surprised by.</summary>
    /// <param name="dissolvedGroups">How many result groups the test had before (they are all dissolved by the regrouping).</param>
    /// <param name="singleBucketNames">The names of the buckets that held a single result and therefore got no group.</param>
    /// <param name="looseResults">How many results stay ungrouped.</param>
    public static IReadOnlyList<string> Build(int dissolvedGroups, IReadOnlyList<string> singleBucketNames, int looseResults)
    {
        if (singleBucketNames == null)
        {
            throw new ArgumentNullException(nameof(singleBucketNames));
        }

        var notes = new List<string>();
        if (dissolvedGroups > 0)
        {
            notes.Add(
                "The test already had " + Count(dissolvedGroups, "group", "groups") +
                "; they were dissolved and the results regrouped (a group's own status, assignee and comments are not kept).");
        }

        if (looseResults > 0)
        {
            var shown = new List<string>();
            for (var i = 0; i < singleBucketNames.Count && i < NamesShown; i++)
            {
                shown.Add("'" + singleBucketNames[i] + "'");
            }

            var more = singleBucketNames.Count > NamesShown ? " and " + (singleBucketNames.Count - NamesShown).ToString(CultureInfo.InvariantCulture) + " more" : string.Empty;
            notes.Add(
                Count(looseResults, "result", "results") + " (" + (shown.Count > 0 ? string.Join(", ", shown) + more : "no bucket name") +
                ") " + (looseResults == 1 ? "was" : "were") +
                " alone in " + (looseResults == 1 ? "its bucket" : "their buckets") +
                " and " + (looseResults == 1 ? "stays" : "stay") + " ungrouped: a group needs two or more results.");
        }

        return notes;
    }

    private static string Count(int number, string one, string many) =>
        number.ToString(CultureInfo.InvariantCulture) + " " + (number == 1 ? one : many);
}
