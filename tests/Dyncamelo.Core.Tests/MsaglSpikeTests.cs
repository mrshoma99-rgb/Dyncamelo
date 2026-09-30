using System.Linq;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// Spike S7: the MSAGL layered (Sugiyama) engine — MIT, netstandard2.0 — loads
/// and lays out a small dependency graph left to right without overlap.
/// </summary>
public class MsaglSpikeTests
{
    [Fact]
    public void LayeredLayoutOrdersALeftToRightChainWithoutOverlap()
    {
        var graph = new GeometryGraph();
        var nodes = new Node[4];
        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new Node(CurveFactory.CreateRectangle(160, 60, new Microsoft.Msagl.Core.Geometry.Point()));
            graph.Nodes.Add(nodes[i]);
        }

        graph.Edges.Add(new Edge(nodes[0], nodes[1]));
        graph.Edges.Add(new Edge(nodes[0], nodes[2]));
        graph.Edges.Add(new Edge(nodes[1], nodes[3]));
        graph.Edges.Add(new Edge(nodes[2], nodes[3]));

        var settings = new SugiyamaLayoutSettings
        {
            Transformation = PlaneTransformation.Rotation(System.Math.PI / 2),
            NodeSeparation = 40,
            LayerSeparation = 90,
        };
        LayoutHelpers.CalculateLayout(graph, settings, null);

        Assert.All(nodes, n =>
        {
            Assert.False(double.IsNaN(n.Center.X) || double.IsNaN(n.Center.Y));
        });
        var xs = nodes.Select(n => n.Center.X).ToArray();
        var ys = nodes.Select(n => n.Center.Y).ToArray();
        // Layers run along one axis: source and sink are the extremes on it.
        var spreadX = xs.Max() - xs.Min();
        var spreadY = ys.Max() - ys.Min();
        var layerAxis = spreadX > spreadY ? xs : ys;
        var first = layerAxis[0];
        var last = layerAxis[3];
        Assert.True(System.Math.Abs(last - first) > 100, "source and sink should be separated by layers");
        Assert.True(layerAxis[1] > System.Math.Min(first, last) && layerAxis[1] < System.Math.Max(first, last));

        for (var i = 0; i < nodes.Length; i++)
        {
            for (var j = i + 1; j < nodes.Length; j++)
            {
                Assert.False(nodes[i].BoundingBox.Intersects(nodes[j].BoundingBox), $"nodes {i} and {j} overlap");
            }
        }
    }
}
