using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// Counts items per group for Model.Statistics: how many items, how many of them carry geometry, and each group's
/// share of all items. Pure (no Navisworks types), so the counting and the rounding are unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class GroupStatistics
{
    /// <summary>The group name used for an item with no value for the grouping.</summary>
    public const string NoGroup = "(none)";

    private readonly Dictionary<string, Counts> _groups = new Dictionary<string, Counts>(StringComparer.Ordinal);

    /// <summary>How many items were added in all.</summary>
    public int Total { get; private set; }

    /// <summary>How many groups there are.</summary>
    public int GroupCount => _groups.Count;

    /// <summary>Counts one item.</summary>
    /// <param name="group">The group name (null or empty counts under "(none)").</param>
    /// <param name="hasGeometry">Whether the item carries geometry.</param>
    public void Add(string? group, bool hasGeometry)
    {
        var name = string.IsNullOrEmpty(group) ? NoGroup : group!;
        if (!_groups.TryGetValue(name, out var counts))
        {
            counts = new Counts();
            _groups[name] = counts;
        }

        counts.Items++;
        if (hasGeometry)
        {
            counts.WithGeometry++;
        }

        Total++;
    }

    /// <summary>
    /// The table with the columns Group, Items, WithGeometry and Share (percent of all items, one decimal), the
    /// biggest group first and groups of equal size in name order (case-insensitive).
    /// </summary>
    /// <returns>The table.</returns>
    public CamelGraphTable ToTable()
    {
        var names = new List<string>(_groups.Keys);
        names.Sort((a, b) =>
        {
            var byItems = _groups[b].Items.CompareTo(_groups[a].Items);
            if (byItems != 0)
            {
                return byItems;
            }

            var byName = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a, b);
        });

        var rows = new List<IReadOnlyList<object?>>(names.Count);
        foreach (var name in names)
        {
            var counts = _groups[name];
            rows.Add(new object?[] { name, (double)counts.Items, (double)counts.WithGeometry, Share(counts.Items, Total) });
        }

        return new CamelGraphTable(new[] { "Group", "Items", "WithGeometry", "Share" }, rows);
    }

    /// <summary>A part as a percentage of the whole, rounded to one decimal (0 when the whole is 0).</summary>
    /// <param name="part">The part.</param>
    /// <param name="whole">The whole.</param>
    /// <returns>The percentage, for example 33.3.</returns>
    public static double Share(int part, int whole)
    {
        return whole <= 0 ? 0d : Math.Round(part * 100.0 / whole, 1, MidpointRounding.AwayFromZero);
    }

    private sealed class Counts
    {
        internal int Items;
        internal int WithGeometry;
    }
}
