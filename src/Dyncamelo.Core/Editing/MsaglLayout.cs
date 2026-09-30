using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Msagl.Core;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Editing;

/// <summary>
/// The Microsoft Automatic Graph Layout (MSAGL, MIT) layered engine behind Arrange: crossing-minimised columns
/// that respect each node's real size. Kept in its own class so that a host without the MSAGL assembly only
/// fails when this class is first used, never when the rest of Core loads. Callers go through
/// <see cref="ArrangeEngine"/>, which also supplies the fallback.
/// </summary>
internal static class MsaglLayout
{
    /// <summary>Lays the items out left to right; returns top-left corners, or null when the run was cancelled or timed out.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Dictionary<object, (double X, double Y)>? TryRun(
        IReadOnlyList<GraphLayout.LayoutItem> items,
        IReadOnlyCollection<(object From, object To)> edges,
        double columnGap,
        double rowGap,
        TimeSpan budget)
    {
        var cancel = new CancelToken();
        var task = Task.Run(() => Run(items, edges, columnGap, rowGap, cancel));
        if (!task.Wait(budget))
        {
            cancel.Canceled = true;
            return null;
        }

        return task.Result;
    }

    private static Dictionary<object, (double X, double Y)> Run(
        IReadOnlyList<GraphLayout.LayoutItem> items,
        IReadOnlyCollection<(object From, object To)> edges,
        double columnGap,
        double rowGap,
        CancelToken cancel)
    {
        var graph = new GeometryGraph();
        var nodes = new Node[items.Count];
        var index = new Dictionary<object, int>();
        for (var i = 0; i < items.Count; i++)
        {
            var center = new Microsoft.Msagl.Core.Geometry.Point(0, 0);
            nodes[i] = new Node(CurveFactory.CreateRectangle(items[i].Width, items[i].Height, center));
            graph.Nodes.Add(nodes[i]);
            if (items[i].Key != null)
            {
                index[items[i].Key] = i;
            }
        }

        var seen = new HashSet<long>();
        foreach (var edge in edges)
        {
            if (edge.From == null || edge.To == null ||
                !index.TryGetValue(edge.From, out var from) || !index.TryGetValue(edge.To, out var to) || from == to)
            {
                continue;
            }

            if (seen.Add(((long)from << 32) | (uint)to))
            {
                graph.Edges.Add(new Edge(nodes[from], nodes[to]));
            }
        }

        var settings = new SugiyamaLayoutSettings
        {
            Transformation = PlaneTransformation.Rotation(Math.PI / 2),
            NodeSeparation = rowGap,
            LayerSeparation = columnGap,
        };
        settings.EdgeRoutingSettings.EdgeRoutingMode = EdgeRoutingMode.None;
        LayoutHelpers.CalculateLayout(graph, settings, cancel);

        var result = new Dictionary<object, (double X, double Y)>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Key == null)
            {
                continue;
            }

            // MSAGL's y axis points up; the canvas' points down.
            var c = nodes[i].Center;
            result[items[i].Key] = (c.X - items[i].Width / 2d, -c.Y - items[i].Height / 2d);
        }

        return result;
    }
}
