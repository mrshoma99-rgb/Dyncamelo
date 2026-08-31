using System;
using System.Collections.Generic;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// Tidy-up layout for a set of nodes: lays them out left-to-right in dependency
/// columns so nothing overlaps and the wires read in the direction the data
/// flows. Pure geometry — no view types — so the arrangement is unit-testable
/// independently of the editor.
/// </summary>
public static class GraphLayout
{
    /// <summary>One node to place: an opaque key plus the space it occupies.</summary>
    public readonly struct LayoutItem
    {
        /// <summary>Creates a layout item.</summary>
        /// <param name="key">Caller's identity for the node (returned in the result).</param>
        /// <param name="width">The node's width on the canvas.</param>
        /// <param name="height">The node's height on the canvas.</param>
        public LayoutItem(object key, double width, double height)
        {
            Key = key;
            Width = width <= 0 ? 1 : width;
            Height = height <= 0 ? 1 : height;
        }

        /// <summary>Caller's identity for the node.</summary>
        public object Key { get; }

        /// <summary>The node's width on the canvas.</summary>
        public double Width { get; }

        /// <summary>The node's height on the canvas.</summary>
        public double Height { get; }
    }

    /// <summary>
    /// Arranges <paramref name="items"/> into dependency columns anchored at
    /// (<paramref name="originX"/>, <paramref name="originY"/>).
    /// </summary>
    /// <param name="items">The nodes to place, in the order they should stack within a column (callers usually sort by current Y, so the existing reading order survives).</param>
    /// <param name="edges">Dependency pairs (fromKey → toKey). Edges naming keys outside <paramref name="items"/> are ignored; cycles are broken deterministically.</param>
    /// <param name="originX">Left edge of the arranged block.</param>
    /// <param name="originY">Vertical centre line of the arranged block.</param>
    /// <param name="columnGap">Horizontal gap between columns.</param>
    /// <param name="rowGap">Vertical gap between nodes in a column.</param>
    /// <returns>The new top-left position of every item, keyed as supplied.</returns>
    public static Dictionary<object, (double X, double Y)> Arrange(
        IReadOnlyList<LayoutItem> items,
        IReadOnlyCollection<(object From, object To)> edges,
        double originX,
        double originY,
        double columnGap = 80.0,
        double rowGap = 40.0)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var result = new Dictionary<object, (double X, double Y)>();
        if (items.Count == 0)
        {
            return result;
        }

        var index = new Dictionary<object, int>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Key != null)
            {
                index[items[i].Key] = i;
            }
        }

        // Keep only edges whose both ends are being arranged, ignoring self-loops.
        var incoming = new List<List<int>>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            incoming.Add(new List<int>());
        }

        if (edges != null)
        {
            foreach (var edge in edges)
            {
                if (edge.From == null || edge.To == null ||
                    !index.TryGetValue(edge.From, out var from) ||
                    !index.TryGetValue(edge.To, out var to) ||
                    from == to)
                {
                    continue;
                }

                incoming[to].Add(from);
            }
        }

        // Longest-path layering: a node sits one column right of its deepest
        // input. The sweep is capped at items.Count passes, so a cycle settles
        // instead of spinning (its members simply share a column).
        var column = new int[items.Count];
        for (int pass = 0; pass < items.Count; pass++)
        {
            bool changed = false;
            for (int i = 0; i < items.Count; i++)
            {
                foreach (var source in incoming[i])
                {
                    if (column[source] + 1 > column[i])
                    {
                        column[i] = column[source] + 1;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var columns = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < items.Count; i++)
        {
            if (!columns.TryGetValue(column[i], out var bucket))
            {
                bucket = new List<int>();
                columns[column[i]] = bucket;
            }

            bucket.Add(i); // input order = the caller's chosen stacking order
        }

        // Column heights decide the vertical centring, so measure first.
        var heights = new Dictionary<int, double>();
        double tallest = 0;
        foreach (var pair in columns)
        {
            double height = 0;
            for (int k = 0; k < pair.Value.Count; k++)
            {
                height += items[pair.Value[k]].Height;
                if (k < pair.Value.Count - 1)
                {
                    height += rowGap;
                }
            }

            heights[pair.Key] = height;
            if (height > tallest)
            {
                tallest = height;
            }
        }

        double x = originX;
        foreach (var pair in columns)
        {
            double widest = 0;
            foreach (var i in pair.Value)
            {
                if (items[i].Width > widest)
                {
                    widest = items[i].Width;
                }
            }

            // Each column is centred on the same line, so wires run roughly level.
            double y = originY - (heights[pair.Key] / 2.0);
            foreach (var i in pair.Value)
            {
                result[items[i].Key] = (x, y);
                y += items[i].Height + rowGap;
            }

            x += widest + columnGap;
        }

        return result;
    }
}
