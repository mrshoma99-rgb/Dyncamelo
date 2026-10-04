using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>
/// The shape of the per-test clash summary (Clash.SummaryTable): one row per test with the total and one count per status.
/// Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ClashSummary
{
    /// <summary>The column names: Test, Total, then one per status in the order given.</summary>
    /// <param name="statusNames">The status names, in column order.</param>
    public static List<string> Headers(IEnumerable<string> statusNames)
    {
        if (statusNames == null)
        {
            throw new ArgumentNullException(nameof(statusNames));
        }

        var headers = new List<string> { "Test", "Total" };
        headers.AddRange(statusNames);
        return headers;
    }

    /// <summary>One row: the test's name, how many results it has, and how many of them have each status.</summary>
    /// <param name="testName">The test's display name.</param>
    /// <param name="resultStatuses">The status name of every result of the test.</param>
    /// <param name="statusNames">The status names, in column order.</param>
    public static List<object?> Row(string testName, IReadOnlyList<string> resultStatuses, IReadOnlyList<string> statusNames)
    {
        if (resultStatuses == null)
        {
            throw new ArgumentNullException(nameof(resultStatuses));
        }

        if (statusNames == null)
        {
            throw new ArgumentNullException(nameof(statusNames));
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var status in resultStatuses)
        {
            counts.TryGetValue(status, out var count);
            counts[status] = count + 1;
        }

        var row = new List<object?> { testName, resultStatuses.Count };
        foreach (var name in statusNames)
        {
            counts.TryGetValue(name, out var count);
            row.Add(count);
        }

        return row;
    }
}
