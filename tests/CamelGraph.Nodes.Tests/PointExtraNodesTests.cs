using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Types;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// PointExtraNodes: Midpoint, Lerp, Centroid, Distance2D and Round.
/// </summary>
public class PointExtraNodesTests
{
    private static CamelGraphPoint P(double x, double y, double z) => new CamelGraphPoint(x, y, z);

    private static CamelGraphVector V(double x, double y, double z) => new CamelGraphVector(x, y, z);

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    /// <summary>A stand-in for a host point type (like Navisworks' Point3D) that only the type converters can read.</summary>
    private sealed class HostPoint
    {
        public HostPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }
    }

    // ---------------------------------------------------------- Point.Midpoint

    [Fact]
    public void Midpoint_IsHalfway()
    {
        Assert.Equal(P(2, 3, 4), PointExtraNodes.Midpoint(P(0, 0, 0), P(4, 6, 8)));
        Assert.Equal(P(0, 0, 0), PointExtraNodes.Midpoint(P(-5, -5, -5), P(5, 5, 5)));
        Assert.Equal(P(3, 3, 3), PointExtraNodes.Midpoint(P(3, 3, 3), P(3, 3, 3)));
    }

    [Fact]
    public void Midpoint_IsSymmetric_AndEquidistantFromBothEnds()
    {
        var a = P(1, 7, -3);
        var b = P(9, -2, 4);
        var mid = PointExtraNodes.Midpoint(a, b);

        Assert.Equal(mid, PointExtraNodes.Midpoint(b, a));
        Assert.Equal(GeometryNodes.PointDistanceTo(a, mid), GeometryNodes.PointDistanceTo(mid, b), 12);
    }

    [Fact]
    public void Midpoint_NullArguments_Throw()
    {
        var error = Assert.Throws<ArgumentNullException>(() => PointExtraNodes.Midpoint(null!, P(0, 0, 0)));
        Assert.Contains("Point.Midpoint", error.Message);
        Assert.Contains("'a'", error.Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => PointExtraNodes.Midpoint(P(0, 0, 0), null!)).Message);
    }

    // -------------------------------------------------------------- Point.Lerp

    [Fact]
    public void Lerp_EndpointsAndFractions()
    {
        var a = P(0, 0, 0);
        var b = P(10, 20, 30);

        Assert.Equal(a, PointExtraNodes.Lerp(a, b, 0));
        Assert.Equal(b, PointExtraNodes.Lerp(a, b, 1));
        Assert.Equal(P(5, 10, 15), PointExtraNodes.Lerp(a, b, 0.5));
        Assert.Equal(P(2.5, 5, 7.5), PointExtraNodes.Lerp(a, b, 0.25));
    }

    [Fact]
    public void Lerp_IsNotClamped_ExtrapolatesBothWays()
    {
        var a = P(0, 0, 0);
        var b = P(10, 20, 30);

        Assert.Equal(P(20, 40, 60), PointExtraNodes.Lerp(a, b, 2));
        Assert.Equal(P(-10, -20, -30), PointExtraNodes.Lerp(a, b, -1));
        Assert.Equal(P(15, 30, 45), PointExtraNodes.Lerp(a, b, 1.5));
    }

    [Fact]
    public void Lerp_EndpointsAreExact_EvenForAwkwardFloats()
    {
        var a = P(0.1, 0.7, -3.3);
        var b = P(0.3, 0.9, 1e-3);

        Assert.Equal(a, PointExtraNodes.Lerp(a, b, 0));
        Assert.Equal(b, PointExtraNodes.Lerp(a, b, 1));
    }

    [Fact]
    public void Lerp_AtOneHalf_IsTheMidpoint()
    {
        var a = P(1, 7, -3);
        var b = P(9, -2, 4);
        Assert.Equal(PointExtraNodes.Midpoint(a, b), PointExtraNodes.Lerp(a, b, 0.5));
    }

    [Fact]
    public void Lerp_NullArguments_Throw()
    {
        Assert.Contains("Point.Lerp", Assert.Throws<ArgumentNullException>(() => PointExtraNodes.Lerp(null!, P(0, 0, 0), 0.5)).Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => PointExtraNodes.Lerp(P(0, 0, 0), null!, 0.5)).Message);
    }

    // ---------------------------------------------------------- Point.Centroid

    [Fact]
    public void Centroid_IsTheMeanOfThePoints()
    {
        var square = new List<object?> { P(0, 0, 0), P(4, 0, 0), P(4, 4, 0), P(0, 4, 0) };
        Assert.Equal(P(2, 2, 0), PointExtraNodes.Centroid(square));

        var triangle = new List<object?> { P(0, 0, 0), P(6, 0, 0), P(0, 9, 3) };
        Assert.Equal(P(2, 3, 1), PointExtraNodes.Centroid(triangle));
    }

    [Fact]
    public void Centroid_OfOnePoint_IsThatPoint()
    {
        Assert.Equal(P(1.5, -2, 8), PointExtraNodes.Centroid(new List<object?> { P(1.5, -2, 8) }));
    }

    [Fact]
    public void Centroid_OfTwoPoints_IsTheMidpoint()
    {
        var a = P(2, 4, 6);
        var b = P(-4, 0, 10);
        Assert.Equal(PointExtraNodes.Midpoint(a, b), PointExtraNodes.Centroid(new List<object?> { a, b }));
    }

    [Fact]
    public void Centroid_OfBoxCorners_IsTheBoxCenter()
    {
        var box = new CamelGraphBoundingBox(P(-3, 2, 0), P(7, 8, 5));
        var corners = BoundingBoxExtraNodes.Corners(box).Cast<object?>().ToList();

        Assert.Equal(box.Center, PointExtraNodes.Centroid(corners));
    }

    [Fact]
    public void Centroid_NullItem_NamesTheIndex()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            PointExtraNodes.Centroid(new List<object?> { P(0, 0, 0), P(1, 1, 1), null, P(2, 2, 2) }));

        Assert.Contains("Point.Centroid", error.Message);
        Assert.Contains("item 2", error.Message);
        Assert.Contains("null", error.Message);
    }

    [Fact]
    public void Centroid_NonPointItem_NamesTheIndexAndTheType()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            PointExtraNodes.Centroid(new List<object?> { P(0, 0, 0), 42.5, P(2, 2, 2) }));

        Assert.Contains("item 1", error.Message);
        Assert.Contains("Double", error.Message);
        Assert.Contains("42.5", error.Message);

        var text = Assert.Throws<ArgumentException>(() => PointExtraNodes.Centroid(new List<object?> { "oops" }));
        Assert.Contains("item 0", text.Message);
        Assert.Contains("\"oops\"", text.Message);
    }

    [Fact]
    public void Centroid_NestedList_SuggestsFlattening()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            PointExtraNodes.Centroid(new List<object?> { P(0, 0, 0), new List<object?> { P(1, 1, 1) } }));

        Assert.Contains("item 1", error.Message);
        Assert.Contains("List.Flatten", error.Message);
    }

    [Fact]
    public void Centroid_EmptyList_IsAnError()
    {
        var error = Assert.Throws<ArgumentException>(() => PointExtraNodes.Centroid(new List<object?>()));
        Assert.Contains("Point.Centroid", error.Message);
        Assert.Contains("at least one point", error.Message);
    }

    [Fact]
    public void Centroid_NullList_IsAnError()
    {
        var error = Assert.Throws<ArgumentNullException>(() => PointExtraNodes.Centroid(null!));
        Assert.Contains("Point.Centroid", error.Message);
        Assert.Contains("'points'", error.Message);
    }

    [Fact]
    public void Centroid_ErrorIndexIsInvariantAcrossCultures()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            odd.NumberFormat.NumberDecimalSeparator = ",";
            odd.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = odd;

            var error = Assert.Throws<ArgumentException>(() => PointExtraNodes.Centroid(new List<object?> { P(0, 0, 0), -7.5 }));
            Assert.Contains("item 1", error.Message);
            Assert.Contains("-7.5", error.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Centroid_AcceptsHostPointsThroughTheRegisteredConverters()
    {
        TypeCoercion.RegisterConverter(typeof(HostPoint), typeof(CamelGraphPoint), o =>
        {
            var host = (HostPoint)o;
            return new CamelGraphPoint(host.X, host.Y, host.Z);
        });

        try
        {
            var centroid = PointExtraNodes.Centroid(new List<object?> { new HostPoint(0, 0, 0), P(4, 6, 8) });
            Assert.Equal(P(2, 3, 4), centroid);
        }
        finally
        {
            TypeCoercion.UnregisterConverter(typeof(HostPoint), typeof(CamelGraphPoint));
        }
    }

    // ------------------------------------------------------- Point.Distance2D

    [Fact]
    public void Distance2D_IgnoresZ()
    {
        Assert.Equal(5d, PointExtraNodes.Distance2D(P(0, 0, 0), P(3, 4, 0)), 12);
        Assert.Equal(5d, PointExtraNodes.Distance2D(P(0, 0, 0), P(3, 4, 100)), 12);
        Assert.Equal(5d, PointExtraNodes.Distance2D(P(0, 0, -50), P(3, 4, 100)), 12);
        Assert.Equal(0d, PointExtraNodes.Distance2D(P(1, 2, 3), P(1, 2, 99)), 12);
    }

    [Fact]
    public void Distance2D_IsSymmetric_AndNeverMoreThanTheThreeDimensionalDistance()
    {
        var a = P(1, 2, 3);
        var b = P(-4, 6, 20);

        Assert.Equal(PointExtraNodes.Distance2D(a, b), PointExtraNodes.Distance2D(b, a));
        Assert.True(PointExtraNodes.Distance2D(a, b) < GeometryNodes.PointDistanceTo(a, b));
        Assert.Equal(
            GeometryNodes.PointDistanceTo(P(1, 2, 7), P(4, 6, 7)),
            PointExtraNodes.Distance2D(P(1, 2, 7), P(4, 6, 7)),
            12);
    }

    [Fact]
    public void Distance2D_NullArguments_Throw()
    {
        Assert.Contains("Point.Distance2D", Assert.Throws<ArgumentNullException>(() => PointExtraNodes.Distance2D(null!, P(0, 0, 0))).Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => PointExtraNodes.Distance2D(P(0, 0, 0), null!)).Message);
    }

    // ------------------------------------------------------------ Point.Round

    [Fact]
    public void Round_DefaultsToThreeDigits()
    {
        Assert.Equal(P(1.235, 2.346, -3.457), PointExtraNodes.Round(P(1.23456, 2.34567, -3.45678)));
    }

    [Theory]
    [InlineData(0, 1, 3, -4)]      // -3.5 is a midpoint, so it rounds away from zero
    [InlineData(1, 1.2, 3.5, -3.5)]
    [InlineData(2, 1.23, 3.46, -3.5)]
    [InlineData(3, 1.235, 3.457, -3.5)]
    public void Round_ToTheRequestedDigits(int digits, double x, double y, double z)
    {
        var rounded = PointExtraNodes.Round(P(1.2346, 3.4567, -3.5), digits);

        Assert.Equal(x, rounded.X, 12);
        Assert.Equal(y, rounded.Y, 12);
        Assert.Equal(z, rounded.Z, 12);
    }

    [Fact]
    public void Round_MidpointsRoundAwayFromZero()
    {
        var rounded = PointExtraNodes.Round(P(0.5, -0.5, 2.5), 0);
        Assert.Equal(P(1, -1, 3), rounded);
    }

    [Fact]
    public void Round_NegativeZeroBecomesPlainZero()
    {
        var rounded = PointExtraNodes.Round(P(-0.0004, 0.0004, -0.0001), 3);

        Assert.Equal(0L, Bits(rounded.X));
        Assert.Equal(0L, Bits(rounded.Y));
        Assert.Equal(0L, Bits(rounded.Z));
    }

    [Fact]
    public void Round_CleansFloatingPointNoise()
    {
        var noisy = P(0.1 + 0.2, 1 / 3d, 2.0000000000004);
        Assert.Equal(P(0.3, 0.333, 2), PointExtraNodes.Round(noisy, 3));
    }

    [Fact]
    public void Round_FifteenDigitsIsAllowed_AndKeepsTheValue()
    {
        Assert.Equal(P(1.5, 2.25, 3), PointExtraNodes.Round(P(1.5, 2.25, 3), 15));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void Round_DigitsOutsideZeroToFifteen_Throws(int digits)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => PointExtraNodes.Round(P(1, 1, 1), digits));

        Assert.Contains("Point.Round", error.Message);
        Assert.Contains("0 to 15", error.Message);
        Assert.Contains(digits.ToString(CultureInfo.InvariantCulture), error.Message);
    }

    [Fact]
    public void Round_NullPoint_Throws()
    {
        var error = Assert.Throws<ArgumentNullException>(() => PointExtraNodes.Round(null!));
        Assert.Contains("Point.Round", error.Message);
        Assert.Contains("'point'", error.Message);
    }

    // ---------------------------------------------- registration and engine

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static NodeDefinition Definition(NodeRegistry registry, string name) =>
        registry.Definitions.Single(d => d.Name == name);

    [Fact]
    public void Registration_EveryNodeIsImported_WithDescriptionAndTags_InTheGeometryCategory()
    {
        var registry = CreateRegistry();

        foreach (var name in new[] { "Point.Midpoint", "Point.Lerp", "Point.Centroid", "Point.Distance2D", "Point.Round" })
        {
            var definition = Definition(registry, name);   // Single: the name is unique in the whole library
            Assert.Equal("Geometry", definition.Category);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), name + " has no description");
            Assert.NotEmpty(definition.SearchTags);
        }
    }

    [Fact]
    public void Registration_Distance2DIsInfo_TheRestAreCreate()
    {
        var registry = CreateRegistry();

        Assert.Equal(NodeFunction.Info, Definition(registry, "Point.Distance2D").Function);
        foreach (var name in new[] { "Point.Midpoint", "Point.Lerp", "Point.Centroid", "Point.Round" })
        {
            Assert.Equal(NodeFunction.Create, Definition(registry, name).Function);
        }
    }

    [Fact]
    public void Registration_RoundExposesItsDigitsRangeAndDefault()
    {
        var digits = Definition(CreateRegistry(), "Point.Round").Inputs.Single(i => i.Name == "digits");

        Assert.True(digits.HasDefault);
        Assert.Equal(3, digits.DefaultValue);
        Assert.NotNull(digits.Range);
        Assert.Equal(0d, digits.Range!.Min);
        Assert.Equal(15d, digits.Range.Max);
    }

    [Fact]
    public void Engine_CentroidOfListedPoints_ThenRound()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var list = new ListCreateNode();
        var ten = new NumberInputNode { Value = 10 };
        var p1 = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var p2 = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var p3 = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var centroid = new ZeroTouchNodeModel(Definition(registry, "Point.Centroid"));
        var round = new ZeroTouchNodeModel(Definition(registry, "Point.Round"));
        var parts = new ZeroTouchNodeModel(Definition(registry, "Point.Components"));
        foreach (var node in new NodeModel[] { list, ten, p1, p2, p3, centroid, round, parts })
        {
            graph.AddNode(node);
        }

        list.AddItemPort();
        list.AddItemPort();
        Assert.True(graph.Connect(ten.OutPorts[0], p2.InPorts[0]).Success);   // p2 = (10, 0, 0)
        Assert.True(graph.Connect(ten.OutPorts[0], p3.InPorts[1]).Success);   // p3 = (0, 10, 0)
        Assert.True(graph.Connect(p1.OutPorts[0], list.InPorts[0]).Success);
        Assert.True(graph.Connect(p2.OutPorts[0], list.InPorts[1]).Success);
        Assert.True(graph.Connect(p3.OutPorts[0], list.InPorts[2]).Success);
        Assert.True(graph.Connect(list.OutPorts[0], centroid.InPorts[0]).Success);
        Assert.True(graph.Connect(centroid.OutPorts[0], round.InPorts[0]).Success);
        Assert.True(graph.Connect(round.OutPorts[0], parts.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, centroid.State);
        Assert.Equal(10d / 3d, ((CamelGraphPoint)centroid.OutPorts[0].Value!).X, 12);
        Assert.Equal(3.333, (double)parts.OutPorts[0].Value!, 12);
        Assert.Equal(3.333, (double)parts.OutPorts[1].Value!, 12);
        Assert.Equal(0d, (double)parts.OutPorts[2].Value!, 12);
    }

    [Fact]
    public void Engine_LerpReplicatesOverAListOfParameters()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var ts = new ListCreateNode();
        var zero = new NumberInputNode { Value = 0 };
        var half = new NumberInputNode { Value = 0.5 };
        var ten = new NumberInputNode { Value = 10 };
        var origin = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var far = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var lerp = new ZeroTouchNodeModel(Definition(registry, "Point.Lerp"));
        foreach (var node in new NodeModel[] { ts, zero, half, ten, origin, far, lerp })
        {
            graph.AddNode(node);
        }

        ts.AddItemPort();
        Assert.True(graph.Connect(ten.OutPorts[0], far.InPorts[0]).Success);
        Assert.True(graph.Connect(zero.OutPorts[0], ts.InPorts[0]).Success);
        Assert.True(graph.Connect(half.OutPorts[0], ts.InPorts[1]).Success);
        Assert.True(graph.Connect(origin.OutPorts[0], lerp.InPorts[0]).Success);
        Assert.True(graph.Connect(far.OutPorts[0], lerp.InPorts[1]).Success);
        Assert.True(graph.Connect(ts.OutPorts[0], lerp.InPorts[2]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, lerp.State);
        var points = Assert.IsAssignableFrom<IEnumerable<object?>>(lerp.OutPorts[0].Value).ToArray();
        Assert.Equal(new object?[] { P(0, 0, 0), P(5, 0, 0) }, points);
    }

    [Fact]
    public void Engine_CentroidOfAnEmptyList_BecomesAnErrorNode()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var list = new ListCreateNode();
        var centroid = new ZeroTouchNodeModel(Definition(registry, "Point.Centroid"));
        graph.AddNode(list);
        graph.AddNode(centroid);
        Assert.True(graph.Connect(list.OutPorts[0], centroid.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, centroid.State);
        Assert.Contains("Point.Centroid", centroid.StateMessage);
    }
}
