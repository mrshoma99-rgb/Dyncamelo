using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Geometry nodes after the audit (SYS-21, SYS-22, SYS-25, SYS-28, ENG-14): sensible sliders, several wires into the nodes that
/// take many things, one port name for "the box", FromPoints folded into Union, and a warning for a number that is not a number.
/// </summary>
public class GeometryAuditTests
{
    private static readonly NodeRegistry Registry = CreateRegistry();

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private sealed class ConstNode : NodeModel
    {
        private readonly object? _value;

        public ConstNode(object? value)
        {
            _value = value;
            Name = "Const";
            AddOutput("value", typeof(object));
        }

        public override string NodeType => "TestConstGeometry";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new[] { _value };
    }

    private static NodeDefinition Definition(string name) => Registry.Definitions.Single(d => d.Name == name);

    private static ZeroTouchNodeModel Create(string name) => Registry.CreateZeroTouchNode(Definition(name).Id)!;

    /// <summary>Runs one node with a constant on each of its inputs (a null leaves the input to its default).</summary>
    private static ZeroTouchNodeModel Run(string name, params object?[] inputs)
    {
        var graph = new GraphModel();
        var node = Create(name);
        graph.AddNode(node);
        for (int i = 0; i < inputs.Length; i++)
        {
            if (inputs[i] == null)
            {
                continue;
            }

            var source = new ConstNode(inputs[i]);
            graph.AddNode(source);
            Assert.True(graph.Connect(source.OutPorts[0], node.InPorts[i]).Success);
        }

        new GraphEngine().Run(graph);
        return node;
    }

    private static CamelGraphPoint P(double x, double y, double z) => new CamelGraphPoint(x, y, z);

    private static CamelGraphBoundingBox Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new CamelGraphBoundingBox(P(x0, y0, z0), P(x1, y1, z1));

    // ----------------------------------------------------------------------------------------------- SYS-21: ranges

    [Fact]
    public void LerpTHasASoftRangeOfZeroToOneAndNoRealLimit()
    {
        var range = Definition("Point.Lerp").Inputs.Single(i => i.Name == "t").Range!;

        Assert.Equal(0, range.SoftMin);
        Assert.Equal(1, range.SoftMax);
        Assert.Equal(0.05, range.Step);
        Assert.True(range.Min <= -1e9 && range.Max >= 1e9, "t is deliberately not clamped");
        Assert.Equal(P(20, 0, 0), ((CamelGraphPoint)Run("Point.Lerp", P(0, 0, 0), P(10, 0, 0), 2.0).OutPorts[0].Value!));
    }

    [Fact]
    public void BoundingBoxScaleFactorCannotBeDraggedToZeroAndSlidesFromTenthToFive()
    {
        var range = Definition("BoundingBox.Scale").Inputs.Single(i => i.Name == "factor").Range!;

        Assert.True(range.Min > 0);
        Assert.Equal(0.1, range.SoftMin);
        Assert.Equal(5, range.SoftMax);
        Assert.Equal(0.1, range.Step);
    }

    [Theory]
    [InlineData("Vector.IsParallel")]
    [InlineData("Vector.IsPerpendicular")]
    public void TolerancesSlideFromZeroAndStepInThousandths(string node)
    {
        var range = Definition(node).Inputs.Single(i => i.Name == "tolerance").Range!;

        Assert.Equal(0, range.Min);
        Assert.Equal(1, range.Max);
        Assert.Equal(0, range.SoftMin);
        Assert.Equal(0.1, range.SoftMax);
        Assert.Equal(0.001, range.Step);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    public void BoundingBoxScaleRefusesAFactorThatIsNotAPositiveNumberInPlainWords(double factor)
    {
        var node = Run("BoundingBox.Scale", Box(0, 0, 0, 1, 1, 1), factor);

        Assert.Equal(NodeState.Error, node.State);
        Assert.Contains("BoundingBox.Scale needs a scale factor above 0 and finite", node.StateMessage);
        Assert.Contains("'factor'", node.StateMessage);
    }

    // ------------------------------------------------------------------------------- SYS-22 and SYS-28: many wires

    [Fact]
    public void UnionCentroidAndFromPointsTakeSeveralWires()
    {
        Assert.True(Definition("BoundingBox.Union").Inputs.Single().MultiInput);
        Assert.True(Definition("Point.Centroid").Inputs.Single().MultiInput);
        Assert.True(Definition("BoundingBox.FromPoints").Inputs.Single().MultiInput);
    }

    private static ZeroTouchNodeModel RunWithWires(string name, params object?[] wires)
    {
        var graph = new GraphModel();
        var node = Create(name);
        graph.AddNode(node);
        foreach (var wire in wires)
        {
            var source = new ConstNode(wire);
            graph.AddNode(source);
            Assert.True(graph.Connect(source.OutPorts[0], node.InPorts[0]).Success);
        }

        new GraphEngine().Run(graph);
        return node;
    }

    [Fact]
    public void UnionOfABoxAndAPointFromTwoWiresFitsBoth()
    {
        var node = RunWithWires("BoundingBox.Union", Box(0, 0, 0, 1, 1, 1), P(5, -2, 3));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(Box(0, -2, 0, 5, 1, 3), node.OutPorts[0].Value);
    }

    [Fact]
    public void UnionOfThreeWiresWithListsOfListsFitsEverything()
    {
        var node = RunWithWires(
            "BoundingBox.Union",
            new List<object?> { new List<object?> { P(0, 0, 0), P(1, 1, 1) }, new List<object?> { Box(2, 2, 2, 3, 3, 3) } },
            P(-1, 0, 0),
            new List<object?> { new List<object?> { 10.0, 20.0, 30.0 } });

        Assert.Equal(Box(-1, 0, 0, 10, 20, 30), node.OutPorts[0].Value);
    }

    [Fact]
    public void UnionOfOneWiredListIsUntouchedAsBefore()
    {
        var node = RunWithWires("BoundingBox.Union", new List<object?> { P(0, 0, 0), new List<object?> { 1.0, 2.0, 3.0 }, Box(-1, -1, -1, 0, 0, 0) });

        Assert.Equal(Box(-1, -1, -1, 1, 2, 3), node.OutPorts[0].Value);
    }

    [Fact]
    public void CentroidOfPointsFromTwoWiresIsTheirAverage()
    {
        var node = RunWithWires("Point.Centroid", P(0, 0, 0), new List<object?> { P(2, 0, 0), P(4, 6, 0) });

        Assert.Equal(P(2, 2, 0), node.OutPorts[0].Value);
    }

    [Fact]
    public void FromPointsIsRetiredIntoUnionButStillLoadsAndRuns()
    {
        var old = Definition("BoundingBox.FromPoints");
        var union = Definition("BoundingBox.Union");

        Assert.True(old.IsDeprecated);
        Assert.Equal("Use BoundingBox.Union", old.Replacement);
        Assert.False(union.IsDeprecated);
        Assert.Equal("CamelGraph.Nodes.BoundingBoxExtraNodes.FromPoints@System.Collections.Generic.IList<object>", old.Id);

        // An old graph's node is created from its id and still works.
        var node = Registry.CreateZeroTouchNode(old.Id)!;
        var graph = new GraphModel();
        graph.AddNode(node);
        foreach (var point in new[] { P(0, 0, 0), P(2, 3, 4) })
        {
            var source = new ConstNode(point);
            graph.AddNode(source);
            Assert.True(graph.Connect(source.OutPorts[0], node.InPorts[0]).Success);
        }

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(Box(0, 0, 0, 2, 3, 4), node.OutPorts[0].Value);
    }

    [Fact]
    public void UnionCarriesEverySearchWordOfFromPoints()
    {
        var union = Definition("BoundingBox.Union").SearchTags;

        foreach (var tag in Definition("BoundingBox.FromPoints").SearchTags)
        {
            Assert.Contains(tag, union);
        }
    }

    // ------------------------------------------------------------------------------------------- SYS-25: port names

    [Theory]
    [InlineData("BoundingBox.Volume")]
    [InlineData("BoundingBox.SurfaceArea")]
    [InlineData("BoundingBox.Footprint")]
    [InlineData("BoundingBox.Corners")]
    [InlineData("BoundingBox.Expand")]
    [InlineData("BoundingBox.Translate")]
    public void TheBoxInputIsCalledBoundingBoxEverywhereAndTheOldNameStillResolves(string node)
    {
        var port = Definition(node).Inputs[0];

        Assert.Equal("boundingBox", port.Name);
        Assert.Equal(new[] { "box" }, port.Aliases);
        Assert.Equal("boundingBox", Create(node).FindInPort("box")!.Name);
    }

    [Fact]
    public void EveryBoundingBoxNodeNowSaysBoundingBoxForItsFirstBoxInput()
    {
        foreach (var definition in Registry.Definitions.Where(d => d.Name.StartsWith("BoundingBox.", StringComparison.Ordinal) && !d.IsDeprecated))
        {
            foreach (var input in definition.Inputs.Where(i => i.Type == typeof(CamelGraphBoundingBox)))
            {
                Assert.True(
                    input.Name != "box",
                    definition.Name + " still calls its box input 'box'.");
            }
        }
    }

    [Fact]
    public void ByCornersCallsItsCornersCornerAAndCornerBAndTheOldNamesStillResolve()
    {
        var definition = Definition("BoundingBox.ByCorners");

        Assert.Equal(new[] { "cornerA", "cornerB" }, definition.Inputs.Select(i => i.Name));
        Assert.Equal(new[] { "min" }, definition.Inputs[0].Aliases);
        Assert.Equal(new[] { "max" }, definition.Inputs[1].Aliases);
        Assert.Contains("any order", definition.Description);
        Assert.Equal(Box(0, 0, 0, 1, 1, 1), Run("BoundingBox.ByCorners", P(1, 1, 1), P(0, 0, 0)).OutPorts[0].Value);
    }

    [Fact]
    public void DistanceToUsesTheSamePairNamesAsTheOtherTwoPointNodes()
    {
        var definition = Definition("Point.DistanceTo");

        Assert.Equal(new[] { "a", "b" }, definition.Inputs.Select(i => i.Name));
        Assert.Equal(new[] { "point" }, definition.Inputs[0].Aliases);
        Assert.Equal(new[] { "other" }, definition.Inputs[1].Aliases);
        Assert.Equal(5.0, Run("Point.DistanceTo", P(0, 0, 0), P(3, 4, 0)).OutPorts[0].Value);
    }

    [Fact]
    public void AGraphSavedWithTheOldPortNamesLoadsWithItsWiresAndNoWarning()
    {
        var graph = new GraphModel();
        var a = Create("Point.ByCoordinates");
        var b = Create("Point.ByCoordinates");
        b.InPorts[0].SetUserValue(4.0);
        b.InPorts[1].SetUserValue(5.0);
        b.InPorts[2].SetUserValue(6.0);
        var corners = Create("BoundingBox.ByCorners");
        var volume = Create("BoundingBox.Volume");
        var distance = Create("Point.DistanceTo");
        foreach (var node in new NodeModel[] { a, b, corners, volume, distance })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(a.OutPorts[0], corners.InPorts[0]).Success);
        Assert.True(graph.Connect(b.OutPorts[0], corners.InPorts[1]).Success);
        Assert.True(graph.Connect(corners.OutPorts[0], volume.InPorts[0]).Success);
        Assert.True(graph.Connect(a.OutPorts[0], distance.InPorts[0]).Success);
        Assert.True(graph.Connect(b.OutPorts[0], distance.InPorts[1]).Success);

        var serializer = new GraphSerializer(Registry);
        var json = serializer.Serialize(graph)
            .Replace("\"ToPort\": \"cornerA\"", "\"ToPort\": \"min\"").Replace("\"ToPort\": \"cornerB\"", "\"ToPort\": \"max\"")
            .Replace("\"ToPort\": \"boundingBox\"", "\"ToPort\": \"box\"")
            .Replace("\"ToPort\": \"a\"", "\"ToPort\": \"point\"").Replace("\"ToPort\": \"b\"", "\"ToPort\": \"other\"");
        Assert.Contains("\"ToPort\": \"min\"", json);
        Assert.Contains("\"ToPort\": \"box\"", json);
        Assert.Contains("\"ToPort\": \"other\"", json);

        var loaded = serializer.Deserialize(json);

        Assert.Empty(serializer.LoadWarnings);
        var loadedVolume = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == "BoundingBox.Volume");
        Assert.NotNull(loaded.FindConnectionInto(loadedVolume.InPorts[0]));
        var loadedDistance = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == "Point.DistanceTo");
        Assert.NotNull(loaded.FindConnectionInto(loadedDistance.InPorts[0]));
        Assert.NotNull(loaded.FindConnectionInto(loadedDistance.InPorts[1]));
        var loadedCorners = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == "BoundingBox.ByCorners");
        Assert.NotNull(loaded.FindConnectionInto(loadedCorners.InPorts[0]));
        Assert.NotNull(loaded.FindConnectionInto(loadedCorners.InPorts[1]));
        Assert.True(new GraphEngine().Run(loaded).Success);
        Assert.Equal(4.0 * 5.0 * 6.0, loadedVolume.OutPorts[0].Value);
    }

    [Fact]
    public void ErrorMessagesNameTheNewPortName()
    {
        var node = Run("BoundingBox.Volume");

        Assert.Contains("'boundingBox'", node.StateMessage);
    }

    // ------------------------------------------------------------------------------------ ENG-14: not a finite number

    [Fact]
    public void VectorScaleByNaNGivesAWarningThatNamesTheInputAndKeepsItsResult()
    {
        var node = Run("Vector.Scale", new CamelGraphVector(1, 2, 3), double.NaN);

        Assert.Equal(NodeState.Warning, node.State);
        var message = Assert.Single(node.Messages).Text;
        Assert.Contains("Vector.Scale", message);
        Assert.Contains("'factor' is not a number (NaN)", message);
        Assert.Contains("Check the numbers wired into this node", message);
        var result = Assert.IsType<CamelGraphVector>(node.OutPorts[0].Value);
        Assert.True(double.IsNaN(result.X));
    }

    [Fact]
    public void VectorScaleByInfinityGivesAWarning()
    {
        var node = Run("Vector.Scale", new CamelGraphVector(1, 2, 3), double.PositiveInfinity);

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("'factor' is infinite", node.Messages.Single().Text);
    }

    [Fact]
    public void VectorScaleByAnOrdinaryNumberHasNoWarning()
    {
        var node = Run("Vector.Scale", new CamelGraphVector(1, 2, 3), 2.0);

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
        Assert.Equal(new CamelGraphVector(2, 4, 6), node.OutPorts[0].Value);
    }

    [Fact]
    public void LerpWithNaNTWarns_AndPointAndVectorByCoordinatesWarnForTheirNumbers()
    {
        var lerp = Run("Point.Lerp", P(0, 0, 0), P(1, 1, 1), double.NaN);
        Assert.Equal(NodeState.Warning, lerp.State);
        Assert.Contains("'t' is not a number", lerp.Messages.Single().Text);

        var point = Run("Point.ByCoordinates", double.NaN, 1.0, double.PositiveInfinity);
        Assert.Equal(NodeState.Warning, point.State);
        var text = point.Messages.Single().Text;
        Assert.Contains("'x' is not a number (NaN)", text);
        Assert.Contains("'z' is infinite", text);

        var vector = Run("Vector.ByCoordinates", 0.0, double.NaN, 0.0);
        Assert.Equal(NodeState.Warning, vector.State);
        Assert.Contains("'y' is not a number", vector.Messages.Single().Text);

        Assert.Equal(NodeState.Executed, Run("Point.ByCoordinates", 1.0, 2.0, 3.0).State);
    }

    [Fact]
    public void DistanceBetweenPointsWithANaNCoordinateWarns()
    {
        var node = Run("Point.DistanceTo", P(0, 0, 0), P(double.NaN, 0, 0));

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("The distance is not a finite number (NaN)", node.Messages.Single().Text);
        Assert.Contains("check the points wired in", node.Messages.Single().Text);
    }
}
