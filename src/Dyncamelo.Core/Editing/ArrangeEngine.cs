using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Editing;

/// <summary>Where the nodes go, and which engine decided.</summary>
public sealed class ArrangeResult
{
    /// <summary>Creates a result.</summary>
    public ArrangeResult(Dictionary<object, (double X, double Y)> positions, string engine, string? note)
    {
        Positions = positions;
        Engine = engine;
        Note = note;
    }

    /// <summary>New top-left corner of every item, keyed as supplied.</summary>
    public Dictionary<object, (double X, double Y)> Positions { get; }

    /// <summary>"MSAGL" for the layered engine, "columns" for the built-in dependency-column layout.</summary>
    public string Engine { get; }

    /// <summary>Why the built-in layout was used instead of MSAGL (null when MSAGL laid the nodes out).</summary>
    public string? Note { get; }
}

/// <summary>
/// Arrange: the MSAGL layered layout when it works, the built-in <see cref="GraphLayout"/> otherwise (too few
/// nodes, engine not installed, took longer than the budget, non-finite or overlapping output). The result is
/// anchored like the built-in layout so the block does not jump: its left edge stays and its vertical centre stays.
/// </summary>
public static class ArrangeEngine
{
    /// <summary>How long MSAGL may run before the built-in layout takes over.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(3);

    private const int OverlapCheckLimit = 600;

    /// <summary>Arranges <paramref name="items"/>; see <see cref="GraphLayout.Arrange"/> for the parameters.</summary>
    /// <param name="items">Nodes to place, in preferred stacking order.</param>
    /// <param name="edges">Dependency pairs (from → to).</param>
    /// <param name="originX">Left edge of the arranged block.</param>
    /// <param name="originY">Vertical centre line of the arranged block.</param>
    /// <param name="columnGap">Horizontal gap between columns.</param>
    /// <param name="rowGap">Vertical gap between nodes.</param>
    /// <param name="budget">Time MSAGL may take (default <see cref="DefaultBudget"/>).</param>
    /// <param name="useMsagl">False to force the built-in layout.</param>
    public static ArrangeResult Arrange(
        IReadOnlyList<GraphLayout.LayoutItem> items,
        IReadOnlyCollection<(object From, object To)> edges,
        double originX,
        double originY,
        double columnGap = 80d,
        double rowGap = 40d,
        TimeSpan? budget = null,
        bool useMsagl = true)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        edges = edges ?? new List<(object, object)>();
        var columns = new Func<string?, ArrangeResult>(note =>
            new ArrangeResult(GraphLayout.Arrange(items, edges, originX, originY, columnGap, rowGap), "columns", note));

        if (!useMsagl)
        {
            return columns("MSAGL layout switched off.");
        }

        if (items.Count < 3)
        {
            return columns("Too few nodes for the layered layout.");
        }

        if (!IsFinite(originX) || !IsFinite(originY))
        {
            originX = IsFinite(originX) ? originX : 0d;
            originY = IsFinite(originY) ? originY : 0d;
        }

        columnGap = IsFinite(columnGap) && columnGap >= 0d ? columnGap : 80d;
        rowGap = IsFinite(rowGap) && rowGap >= 0d ? rowGap : 40d;

        Dictionary<object, (double X, double Y)>? raw;
        try
        {
            raw = RunMsagl(items, edges, columnGap, rowGap, budget ?? DefaultBudget);
        }
        catch (Exception ex) when (!(ex is OutOfMemoryException))
        {
            // Includes a missing or unloadable AutomaticGraphLayout.dll and any failure inside the engine.
            return columns("MSAGL unavailable (" + ex.GetType().Name + ").");
        }

        if (raw == null)
        {
            return columns("MSAGL took longer than " + (budget ?? DefaultBudget).TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s.");
        }

        if (!AllUsable(items, raw))
        {
            return columns("MSAGL returned unusable positions.");
        }

        if (items.Count <= OverlapCheckLimit && HasOverlap(items, raw, Math.Min(rowGap, columnGap) / 4d))
        {
            return columns("MSAGL positions overlapped.");
        }

        return new ArrangeResult(Anchor(items, raw, originX, originY), "MSAGL", null);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Dictionary<object, (double X, double Y)>? RunMsagl(
        IReadOnlyList<GraphLayout.LayoutItem> items,
        IReadOnlyCollection<(object From, object To)> edges,
        double columnGap,
        double rowGap,
        TimeSpan budget)
    {
        return MsaglLayout.TryRun(items, edges, columnGap, rowGap, budget);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool AllUsable(IReadOnlyList<GraphLayout.LayoutItem> items, Dictionary<object, (double X, double Y)> positions)
    {
        foreach (var item in items)
        {
            if (item.Key == null || !positions.TryGetValue(item.Key, out var p) || !IsFinite(p.X) || !IsFinite(p.Y))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasOverlap(IReadOnlyList<GraphLayout.LayoutItem> items, Dictionary<object, (double X, double Y)> positions, double tolerance)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var a = positions[items[i].Key];
            for (var j = i + 1; j < items.Count; j++)
            {
                var b = positions[items[j].Key];
                var overlapX = Math.Min(a.X + items[i].Width, b.X + items[j].Width) - Math.Max(a.X, b.X);
                var overlapY = Math.Min(a.Y + items[i].Height, b.Y + items[j].Height) - Math.Max(a.Y, b.Y);
                if (overlapX > tolerance && overlapY > tolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Dictionary<object, (double X, double Y)> Anchor(
        IReadOnlyList<GraphLayout.LayoutItem> items,
        Dictionary<object, (double X, double Y)> raw,
        double originX,
        double originY)
    {
        double left = double.MaxValue, top = double.MaxValue, bottom = double.MinValue;
        foreach (var item in items)
        {
            var p = raw[item.Key];
            left = Math.Min(left, p.X);
            top = Math.Min(top, p.Y);
            bottom = Math.Max(bottom, p.Y + item.Height);
        }

        var dx = originX - left;
        var dy = originY - (top + bottom) / 2d;
        var placed = new Dictionary<object, (double X, double Y)>();
        foreach (var item in items)
        {
            var p = raw[item.Key];
            placed[item.Key] = (p.X + dx, p.Y + dy);
        }

        return placed;
    }
}
