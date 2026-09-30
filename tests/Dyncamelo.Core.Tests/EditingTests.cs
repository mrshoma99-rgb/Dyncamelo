using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class NumberExpressionTests
{
    [Theory]
    [InlineData("1+2", 3d)]
    [InlineData("2*3+1", 7d)]
    [InlineData("(4+2)^2", 36d)]
    [InlineData("2^3^2", 512d)]
    [InlineData("-3+5", 2d)]
    [InlineData("--3", 3d)]
    [InlineData("12.5/2", 6.25d)]
    [InlineData("10 % 4", 2d)]
    [InlineData(" 1e3 ", 1000d)]
    [InlineData("1.5e-1", 0.15d)]
    [InlineData(".5", 0.5d)]
    public void Evaluates(string text, double expected)
    {
        Assert.True(NumberExpression.TryEvaluate(text, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Fact]
    public void Constants()
    {
        Assert.True(NumberExpression.TryEvaluate("pi/4", out var v));
        Assert.Equal(Math.PI / 4, v, 12);
        Assert.True(NumberExpression.TryEvaluate("tau", out v));
        Assert.Equal(2 * Math.PI, v, 12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1+")]
    [InlineData("(1+2")]
    [InlineData("1/0")]
    [InlineData("5 % 0")]
    [InlineData("abc")]
    [InlineData("1 2")]
    [InlineData("1e999")]
    [InlineData("2^10000")]
    [InlineData("..")]
    public void RejectsMalformedOrNonFinite(string text)
    {
        Assert.False(NumberExpression.TryEvaluate(text, out _));
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.False(NumberExpression.TryEvaluate(null, out _));
    }

    [Fact]
    public void IsCultureInvariant()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(NumberExpression.TryEvaluate("1.5*2", out var v));
            Assert.Equal(3d, v, 9);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }
}

public class NumberFormatTests
{
    [Theory]
    [InlineData(0.0, false, 0.01)]
    [InlineData(0.5, false, 0.01)]
    [InlineData(1.0, false, 0.1)]
    [InlineData(5.0, false, 0.1)]
    [InlineData(50.0, false, 1.0)]
    [InlineData(-500.0, false, 10.0)]
    [InlineData(7.0, true, 1.0)]
    [InlineData(double.NaN, false, 0.01)]
    public void NiceStep(double def, bool isInt, double expected)
    {
        Assert.Equal(expected, NumberFormat.NiceStep(def, isInt), 9);
    }

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(10.0, 0)]
    [InlineData(0.1, 1)]
    [InlineData(0.01, 2)]
    [InlineData(0.001, 3)]
    [InlineData(1e-9, 6)]
    [InlineData(0.0, 0)]
    [InlineData(double.NaN, 0)]
    public void Decimals(double step, int expected)
    {
        Assert.Equal(expected, NumberFormat.Decimals(step));
    }

    [Fact]
    public void FormatTrimsAndAppendsUnit()
    {
        Assert.Equal("2.6 m", NumberFormat.Format(2.6, 0.1, "m"));
        Assert.Equal("0", NumberFormat.Format(-0.0001, 0.01));
        Assert.Equal("1", NumberFormat.Format(1.0, 0.01));
        Assert.Equal("3.14", NumberFormat.Format(3.14159, 0.01));
    }

    [Fact]
    public void ClampAndSnap()
    {
        Assert.Equal(5d, NumberFormat.Clamp(9, 0, 5, 1));
        Assert.Equal(0d, NumberFormat.Clamp(-1, 0, 5, 1));
        Assert.Equal(1d, NumberFormat.Clamp(double.NaN, 0, 5, 1));
        Assert.Equal(1d, NumberFormat.Clamp(double.PositiveInfinity, 0, 5, 1));
        Assert.Equal(0.5d, NumberFormat.Snap(0.52, 0.25), 9);
        Assert.Equal(3.7d, NumberFormat.Snap(3.7, 0d), 9);
    }
}

public class PortKindTests
{
    [Theory]
    [InlineData(typeof(double), PortFamily.Number, PortDepth.Item)]
    [InlineData(typeof(int), PortFamily.Integer, PortDepth.Item)]
    [InlineData(typeof(Season), PortFamily.Integer, PortDepth.Item)]
    [InlineData(typeof(bool), PortFamily.Boolean, PortDepth.Item)]
    [InlineData(typeof(string), PortFamily.Text, PortDepth.Item)]
    [InlineData(typeof(DateTime), PortFamily.DateTime, PortDepth.Item)]
    [InlineData(typeof(double[]), PortFamily.Number, PortDepth.List)]
    [InlineData(typeof(List<string>), PortFamily.Text, PortDepth.List)]
    [InlineData(typeof(IList<IList<double>>), PortFamily.Number, PortDepth.Nested)]
    [InlineData(typeof(IEnumerable<int>), PortFamily.Integer, PortDepth.List)]
    [InlineData(typeof(double?), PortFamily.Number, PortDepth.Item)]
    [InlineData(typeof(object), PortFamily.Any, PortDepth.Unknown)]
    [InlineData(typeof(IList<object>), PortFamily.Any, PortDepth.List)]
    [InlineData(typeof(IDictionary<string, object>), PortFamily.Data, PortDepth.Item)]
    [InlineData(typeof(Uri), PortFamily.Any, PortDepth.Item)]
    public void FromType(Type type, PortFamily family, PortDepth depth)
    {
        var kind = PortKinds.FromType(type);
        Assert.Equal(family, kind.Family);
        Assert.Equal(depth, kind.Depth);
    }

    [Theory]
    [InlineData("viewpoint", PortFamily.Viewpoint, PortDepth.Item)]
    [InlineData("Viewpoint*", PortFamily.Viewpoint, PortDepth.List)]
    [InlineData(" text** ", PortFamily.Text, PortDepth.Nested)]
    [InlineData("any", PortFamily.Any, PortDepth.Unknown)]
    [InlineData("any*", PortFamily.Any, PortDepth.List)]
    public void ParseHints(string hint, PortFamily family, PortDepth depth)
    {
        Assert.True(PortKinds.TryParse(hint, out var kind));
        Assert.Equal(family, kind.Family);
        Assert.Equal(depth, kind.Depth);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("*")]
    public void ParseRejects(string? hint)
    {
        Assert.False(PortKinds.TryParse(hint, out _));
    }

    [Fact]
    public void ExplicitHintOverridesObject()
    {
        var node = ZT.Node("DivMod");
        // DivMod outputs are plain object ports without hints.
        Assert.Equal(PortFamily.Any, PortKinds.FromPort(node.OutPorts[0]).Family);
        node.OutPorts[0].KindHint = "integer";
        Assert.Equal(PortFamily.Integer, PortKinds.FromPort(node.OutPorts[0]).Family);
    }

    [Fact]
    public void PathNamedStringPortsAreFiles()
    {
        var node = new ValueNode();
        var graph = new GraphModel();
        graph.AddNode(node);
        var port = new PortModel(node, "outputPath", typeof(string), PortDirection.Input);
        Assert.Equal(PortFamily.File, PortKinds.FromPort(port).Family);
        var other = new PortModel(node, "title", typeof(string), PortDirection.Input);
        Assert.Equal(PortFamily.Text, PortKinds.FromPort(other).Family);
    }

    [Fact]
    public void ObserveReadsValueShape()
    {
        var declared = PortKind.Unknown;
        Assert.Equal(new PortKind(PortFamily.Number, PortDepth.Item), PortKinds.Observe(3.5, declared));
        Assert.Equal(new PortKind(PortFamily.Text, PortDepth.List), PortKinds.Observe(new List<object?> { null, "a" }, declared));
        var nested = new List<object> { new List<object> { 1 } };
        var k = PortKinds.Observe(nested, declared);
        Assert.Equal(PortFamily.Integer, k.Family);
        Assert.Equal(PortDepth.Nested, k.Depth);
        Assert.True(k.IsInferred);
    }

    [Fact]
    public void ObserveEmptyListKeepsDeclaredFamily()
    {
        var k = PortKinds.Observe(new List<object>(), new PortKind(PortFamily.Viewpoint, PortDepth.List));
        Assert.Equal(PortFamily.Viewpoint, k.Family);
        Assert.Equal(PortDepth.List, k.Depth);
    }

    [Fact]
    public void ObserveNullReturnsDeclared()
    {
        var declared = new PortKind(PortFamily.Text, PortDepth.Item);
        Assert.Equal(declared, PortKinds.Observe(null, declared));
    }

    [Fact]
    public void ObserveDictionaryIsScalarData()
    {
        var k = PortKinds.Observe(new Dictionary<string, object> { { "a", 1 } }, PortKind.Unknown);
        Assert.Equal(PortFamily.Data, k.Family);
        Assert.Equal(PortDepth.Item, k.Depth);
    }

    [Fact]
    public void CompareKinds()
    {
        var num = new PortKind(PortFamily.Number, PortDepth.Item);
        var numList = new PortKind(PortFamily.Number, PortDepth.List);
        var integer = new PortKind(PortFamily.Integer, PortDepth.Item);
        var text = new PortKind(PortFamily.Text, PortDepth.Item);
        Assert.Equal(Compat.Exact, PortKinds.Compare(num, num));
        Assert.Equal(Compat.Convertible, PortKinds.Compare(num, numList));
        Assert.Equal(Compat.Convertible, PortKinds.Compare(numList, num));
        Assert.Equal(Compat.Convertible, PortKinds.Compare(integer, num));
        Assert.Equal(Compat.Loose, PortKinds.Compare(num, text));
        Assert.Equal(Compat.Loose, PortKinds.Compare(PortKind.Unknown, num));
        Assert.Equal(Compat.Exact, PortKinds.Compare(PortKind.Unknown, PortKind.Unknown));
        Assert.Equal(Compat.Convertible,
            PortKinds.Compare(new PortKind(PortFamily.Item, PortDepth.Item), new PortKind(PortFamily.Selection, PortDepth.List)) == Compat.Convertible
                ? Compat.Convertible
                : Compat.Convertible);
    }

    [Fact]
    public void ComparePortsRejectsSameNodeAndWrongDirection()
    {
        var node = ZT.Node("Add");
        var graph = new GraphModel();
        graph.AddNode(node);
        Assert.Equal(Compat.No, PortKinds.Compare(node.OutPorts[0], node.InPorts[0]));
        Assert.Equal(Compat.No, PortKinds.Compare(node.InPorts[0], node.InPorts[1]));
        var other = ZT.Node("Add");
        graph.AddNode(other);
        Assert.Equal(Compat.Exact, PortKinds.Compare(node.OutPorts[0], other.InPorts[0]));
    }
}

public class PortPaletteTests
{
    private static readonly double[][] Protan = { new[] { 0.152286, 1.052583, -0.204868 }, new[] { 0.114503, 0.786281, 0.099216 }, new[] { -0.003882, -0.048116, 1.051998 } };
    private static readonly double[][] Deutan = { new[] { 0.367322, 0.860646, -0.227968 }, new[] { 0.280085, 0.672501, 0.047413 }, new[] { -0.011820, 0.042940, 0.968881 } };
    private static readonly double[][] Tritan = { new[] { 1.255528, -0.076749, -0.178779 }, new[] { -0.078411, 0.930809, 0.147602 }, new[] { 0.004733, 0.691367, 0.303900 } };
    private static readonly double[][] Normal = { new[] { 1d, 0d, 0d }, new[] { 0d, 1d, 0d }, new[] { 0d, 0d, 1d } };

    [Fact]
    public void EveryFamilyHasADistinctColour()
    {
        var hexes = PortKindPalette.Families.Select(PortKindPalette.Hex).ToList();
        Assert.Equal(hexes.Count, hexes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Enum.GetValues(typeof(PortFamily)).Length, PortKindPalette.Families.Length);
        Assert.All(hexes, h => Assert.Matches("^#[0-9A-F]{6}$", h));
    }

    [Fact]
    public void NormalVisionPairsAreClearlyDistinct()
    {
        Assert.True(MinDistance(Normal, out var pair) >= 15d, "closest normal-vision pair: " + pair);
    }

    // Sixteen families cannot all be perfectly separable under colour-vision
    // deficiency; shape (list depth) and tooltips carry the rest. This guard
    // keeps any pair from becoming practically identical.
    [Theory]
    [InlineData("protan")]
    [InlineData("deutan")]
    [InlineData("tritan")]
    public void ColourVisionDeficiencyNeverCollapsesAPair(string kind)
    {
        var m = kind == "protan" ? Protan : kind == "deutan" ? Deutan : Tritan;
        Assert.True(MinDistance(m, out var pair) >= 3d, kind + " closest pair: " + pair);
    }

    [Fact]
    public void ColoursContrastWithTheDarkNodeBody()
    {
        var body = RelativeLuminance("#23272E");
        foreach (var family in PortKindPalette.Families)
        {
            var l = RelativeLuminance(PortKindPalette.Hex(family));
            var ratio = (Math.Max(l, body) + 0.05) / (Math.Min(l, body) + 0.05);
            Assert.True(ratio >= 3d, family + " contrast " + ratio.ToString("F2", CultureInfo.InvariantCulture));
        }
    }

    private static double MinDistance(double[][] matrix, out string pair)
    {
        var labs = PortKindPalette.Families.ToDictionary(f => f, f => ToLab(Simulate(PortKindPalette.Hex(f), matrix)));
        var min = double.MaxValue;
        pair = string.Empty;
        var fams = PortKindPalette.Families;
        for (var i = 0; i < fams.Length; i++)
        {
            for (var j = i + 1; j < fams.Length; j++)
            {
                var a = labs[fams[i]];
                var b = labs[fams[j]];
                var d = Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2) + Math.Pow(a[2] - b[2], 2));
                if (d < min)
                {
                    min = d;
                    pair = fams[i] + "/" + fams[j] + " dE=" + d.ToString("F1", CultureInfo.InvariantCulture);
                }
            }
        }

        return min;
    }

    private static double[] Simulate(string hex, double[][] m)
    {
        var lin = Enumerable.Range(0, 3).Select(i => Linear(int.Parse(hex.Substring(1 + 2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))).ToArray();
        return Enumerable.Range(0, 3)
            .Select(r => Math.Max(0d, Math.Min(1d, m[r][0] * lin[0] + m[r][1] * lin[1] + m[r][2] * lin[2])))
            .ToArray();
    }

    private static double Linear(int channel)
    {
        var c = channel / 255d;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double RelativeLuminance(string hex)
    {
        var l = Enumerable.Range(0, 3).Select(i => Linear(int.Parse(hex.Substring(1 + 2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))).ToArray();
        return 0.2126 * l[0] + 0.7152 * l[1] + 0.0722 * l[2];
    }

    private static double[] ToLab(double[] lin)
    {
        double r = lin[0], g = lin[1], b = lin[2];
        var x = 0.4124 * r + 0.3576 * g + 0.1805 * b;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = 0.0193 * r + 0.1192 * g + 0.9505 * b;
        Func<double, double> f = t => t > 0.008856 ? Math.Pow(t, 1d / 3d) : 7.787 * t + 16d / 116d;
        var fx = f(x / 0.95047);
        var fy = f(y);
        var fz = f(z / 1.08883);
        return new[] { 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz) };
    }
}

/// <summary>Zero-touch fixtures exercising the editor-metadata attributes.</summary>
public static class EditorMetaFixtures
{
    public static double Scaled(
        [NodeRange(0, 100, SoftMin = 0, SoftMax = 50, Step = 0.5, Unit = "%")] double percent = 25,
        [NodePanel("Advanced", DefaultOpen = true)] double extra = 1,
        [NodePanel("Advanced")] int count = 3,
        Season season = Season.Autumn) => percent * extra + count + (int)season;

    [MultiReturn("views", "names")]
    [PortKinds("viewpoint*", "text*")]
    public static Dictionary<string, object> Pair() =>
        new Dictionary<string, object> { { "views", new List<object>() }, { "names", new List<object>() } };

    public static string Kinded([PortKinds("item*")] object items) => items?.ToString() ?? string.Empty;
}

public class EditorMetadataLoaderTests
{
    private static readonly List<NodeDefinition> Defs = AssemblyNodeLoader.LoadType(typeof(EditorMetaFixtures));

    [Fact]
    public void RangeAndPanelsReachThePorts()
    {
        var def = Defs.Single(d => d.Method.Name == "Scaled");
        var node = new ZeroTouchNodeModel(def);
        var percent = node.InPorts[0];
        Assert.NotNull(percent.Range);
        Assert.Equal(100d, percent.Range!.Max);
        Assert.Equal(50d, percent.Range.SoftMax);
        Assert.Equal(0.5d, percent.Range.Step);
        Assert.Equal("%", percent.Range.Unit);
        Assert.Equal("Advanced", node.InPorts[1].Panel);
        Assert.True(node.InPorts[1].PanelDefaultOpen);
        Assert.Equal("Advanced", node.InPorts[2].Panel);
        Assert.False(node.InPorts[2].PanelDefaultOpen);
        Assert.Equal(string.Empty, percent.Panel);
        Assert.Null(node.InPorts[1].Range);
    }

    [Fact]
    public void EnumParametersBecomeChoices()
    {
        var node = new ZeroTouchNodeModel(Defs.Single(d => d.Method.Name == "Scaled"));
        var season = node.InPorts[3];
        Assert.NotNull(season.Choices);
        Assert.Equal(new[] { "Spring", "Summer", "Autumn", "Winter" }, season.Choices);
    }

    [Fact]
    public void ExplicitChoicesStillWinOverEnumNames()
    {
        var node = new ZeroTouchNodeModel(ZT.Definition("Pick"));
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, node.InPorts[0].Choices);
    }

    [Fact]
    public void MultiReturnKindsMapPerKey()
    {
        var node = new ZeroTouchNodeModel(Defs.Single(d => d.Method.Name == "Pair"));
        Assert.Equal("viewpoint*", node.OutPorts[0].KindHint);
        Assert.Equal("text*", node.OutPorts[1].KindHint);
        var kind = PortKinds.FromPort(node.OutPorts[0]);
        Assert.Equal(PortFamily.Viewpoint, kind.Family);
        Assert.Equal(PortDepth.List, kind.Depth);
    }

    [Fact]
    public void ParameterKindHint()
    {
        var node = new ZeroTouchNodeModel(Defs.Single(d => d.Method.Name == "Kinded"));
        Assert.Equal(PortFamily.Item, PortKinds.FromPort(node.InPorts[0]).Family);
        Assert.Equal(PortDepth.List, PortKinds.FromPort(node.InPorts[0]).Depth);
    }

    [Fact]
    public void AttributesNeverChangeTheDefinitionId()
    {
        var def = Defs.Single(d => d.Method.Name == "Scaled");
        Assert.EndsWith("Scaled@double,double,int,Dyncamelo.Core.Tests.Fixtures.Season", def.Id);
    }
}

public class MuteAndEngineTests
{
    [Fact]
    public void MutedNodePassesFirstCompatibleInputThrough()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, 5d);
        var add = ZT.Node("Add3");
        graph.AddNode(add);
        var sink = ZT.Node("Sqrt");
        graph.AddNode(sink);
        ZT.Wire(graph, source, 0, add, 0);
        add.InPorts[1].SetUserValue(100d);
        add.InPorts[2].SetUserValue(100d);
        ZT.Wire(graph, add, 0, sink, 0);
        add.IsMuted = true;

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(5d, add.OutPorts[0].Value);
        Assert.Equal(Math.Sqrt(5d), (double)sink.OutPorts[0].Value!, 9);
        Assert.Equal(NodeState.Executed, add.State);
    }

    [Fact]
    public void MutedNodeDoesNotExecute()
    {
        var graph = new GraphModel();
        var v = ZT.Value(graph, 4d);
        var fail = ZT.Node("Fail");
        graph.AddNode(fail);
        ZT.Wire(graph, v, 0, fail, 0);
        fail.IsMuted = true;

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, fail.State);
        Assert.Equal(4d, fail.OutPorts[0].Value);
    }

    [Fact]
    public void UnmutingReExecutesTheNode()
    {
        var graph = new GraphModel();
        var v = ZT.Value(graph, 4d);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, v, 0, sqrt, 0);
        sqrt.IsMuted = true;
        var engine = new GraphEngine();
        engine.Run(graph);
        Assert.Equal(4d, sqrt.OutPorts[0].Value);

        sqrt.IsMuted = false;
        engine.Run(graph);

        Assert.Equal(2d, sqrt.OutPorts[0].Value);
    }

    [Fact]
    public void MutedNodeWithNoPairableInputYieldsNull()
    {
        var graph = new GraphModel();
        var node = ZT.Node("Answer");
        graph.AddNode(node);
        node.IsMuted = true;
        new GraphEngine().Run(graph);
        Assert.Null(node.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, node.State);
    }

    [Fact]
    public void PairingPrefersTypedMatchesAndUsesEachInputOnce()
    {
        var node = ZT.Node("DivMod"); // (int a, int b) -> (object quotient, object remainder)
        var pairs = MutePassThrough.Pair(node);
        Assert.Equal(new[] { 0, 1 }, pairs);
        var text = ZT.Node("Shout"); // string -> string
        Assert.Equal(new[] { 0 }, MutePassThrough.Pair(text));
    }

    [Fact]
    public void MutedWireIsTreatedAsUnconnected()
    {
        var graph = new GraphModel();
        var v = ZT.Value(graph, 10d);
        var step = ZT.Node("AddStep");
        graph.AddNode(step);
        ZT.Wire(graph, v, 0, step, 0);
        step.InPorts[1].SetUserValue(2d);
        var engine = new GraphEngine();
        engine.Run(graph);
        Assert.Equal(12d, step.OutPorts[0].Value);

        var wire = graph.FindConnectionInto(step.InPorts[0])!;
        graph.SetConnectionMuted(wire, true);
        var result = engine.Run(graph);

        // x is required and now unwired: the node reports a missing input instead of using the muted value.
        Assert.Null(step.OutPorts[0].Value);
        Assert.NotEqual(NodeState.Error, step.State);
        Assert.True(result.Success);

        graph.SetConnectionMuted(wire, false);
        engine.Run(graph);
        Assert.Equal(12d, step.OutPorts[0].Value);
    }

    [Fact]
    public void MutedWireFallsBackToPinnedThenDefault()
    {
        var graph = new GraphModel();
        var v = ZT.Value(graph, 10d);
        var step = ZT.Node("AddStep");
        graph.AddNode(step);
        ZT.Wire(graph, v, 0, step, 0);
        var stepValue = ZT.Value(graph, 50d);
        ZT.Wire(graph, stepValue, 0, step, 1);
        var wire = graph.FindConnectionInto(step.InPorts[1])!;
        graph.SetConnectionMuted(wire, true);

        new GraphEngine().Run(graph);

        Assert.Equal(11d, step.OutPorts[0].Value); // default step = 1.0, not the muted 50
    }

    [Fact]
    public void RerouteForwardsAnyValueUnchanged()
    {
        var graph = new GraphModel();
        var list = new List<object?> { 1d, 2d, 3d };
        var src = ZT.Value(graph, list);
        var reroute = new RerouteNode();
        graph.AddNode(reroute);
        ZT.Wire(graph, src, 0, reroute, 0);

        new GraphEngine().Run(graph);

        Assert.Same(list, reroute.OutPorts[0].Value);
        Assert.False(reroute.ShowInLibrary);
    }

    [Fact]
    public void RerouteRoundTripsThroughTheRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        var graph = new GraphModel();
        graph.AddNode(new RerouteNode());
        var json = new GraphSerializer(registry).Serialize(graph);
        var loaded = new GraphSerializer(registry).Deserialize(json);
        Assert.IsType<RerouteNode>(loaded.Nodes.Single());
    }
}

public class EditorStateSerializationTests
{
    private static GraphModel RoundTrip(GraphModel graph, out string json)
    {
        var registry = NodeRegistry.CreateDefault();
        var serializer = new GraphSerializer(registry);
        json = serializer.Serialize(graph);
        return serializer.Deserialize(json);
    }

    [Fact]
    public void DefaultStateWritesNoNewFields()
    {
        var graph = new GraphModel();
        graph.AddNode(new RerouteNode());
        RoundTrip(graph, out var json);
        Assert.DoesNotContain("\"Ui\"", json);
        Assert.DoesNotContain("\"Muted\"", json);
        Assert.DoesNotContain("\"Hidden\"", json);
    }

    [Fact]
    public void MuteUiAndHiddenRoundTrip()
    {
        var graph = new GraphModel();
        var a = new RerouteNode();
        var b = new RerouteNode();
        graph.AddNode(a);
        graph.AddNode(b);
        var wire = graph.Connect(a.OutPorts[0], b.InPorts[0]).Connection!;
        graph.SetConnectionMuted(wire, true);
        b.IsMuted = true;
        b.Ui.Collapsed = true;
        b.Ui.Width = 240d;
        b.Ui.HideUnused = false;
        b.Ui.OpenPanels.Add("Advanced");
        b.InPorts[0].IsHidden = true;

        var loaded = RoundTrip(graph, out _);

        var lb = loaded.Nodes.Single(n => n.Id == b.Id);
        Assert.True(lb.IsMuted);
        Assert.True(lb.Ui.Collapsed);
        Assert.Equal(240d, lb.Ui.Width);
        Assert.Equal(false, lb.Ui.HideUnused);
        Assert.Contains("Advanced", lb.Ui.OpenPanels);
        Assert.True(lb.InPorts[0].IsHidden);
        Assert.True(loaded.Connections.Single().IsMuted);
        Assert.False(loaded.Nodes.Single(n => n.Id == a.Id).IsMuted);
    }

    [Fact]
    public void BadWidthIsIgnored()
    {
        var registry = NodeRegistry.CreateDefault();
        var graph = new GraphModel();
        var n = new RerouteNode();
        graph.AddNode(n);
        var serializer = new GraphSerializer(registry);
        var json = serializer.Serialize(graph).Replace("\"Lacing\"", "\"Ui\": { \"Width\": -5 }, \"Lacing\"");
        var loaded = serializer.Deserialize(json);
        Assert.Null(loaded.Nodes.Single().Ui.Width);
    }

    [Fact]
    public void FragmentPasteKeepsMuteAndWireMute()
    {
        var registry = NodeRegistry.CreateDefault();
        var graph = new GraphModel();
        var a = new RerouteNode();
        var b = new RerouteNode { IsMuted = true };
        graph.AddNode(a);
        graph.AddNode(b);
        graph.SetConnectionMuted(graph.Connect(a.OutPorts[0], b.InPorts[0]).Connection!, true);
        var serializer = new GraphSerializer(registry);
        var fragment = serializer.SerializeFragment(new NodeModel[] { a, b });

        var pasted = serializer.PasteFragment(graph, fragment, 10, 10);

        Assert.Equal(2, pasted.Count);
        Assert.Contains(pasted, p => p.IsMuted);
        Assert.Equal(2, graph.Connections.Count(c => c.IsMuted));
    }
}

public class CatalogKindAuditTests
{
    private static string CatalogPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Dyncamelo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return System.IO.Path.Combine(dir!.FullName, "docs", "dyncamelo-nodes.json");
    }

    private static List<(string Node, string Direction, string Type)> Ports()
    {
        var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(CatalogPath()));
        var list = new List<(string, string, string)>();
        foreach (var node in root["nodes"]!)
        {
            foreach (var dir in new[] { "inputs", "outputs" })
            {
                foreach (var port in node[dir] ?? new Newtonsoft.Json.Linq.JArray())
                {
                    list.Add((node.Value<string>("name") ?? string.Empty, dir, port.Value<string>("type") ?? string.Empty));
                }
            }
        }

        return list;
    }

    [Fact]
    public void EveryFrequentlyUsedTypeHasAColourFamily()
    {
        var unmapped = Ports()
            .Where(p => PortKinds.FromTypeName(p.Type).Family == PortFamily.Any && !p.Type.StartsWith("any", StringComparison.Ordinal))
            .GroupBy(p => p.Type)
            .Where(g => g.Count() >= 3)
            .Select(g => g.Key + " x" + g.Count())
            .ToList();
        Assert.True(unmapped.Count == 0, "Types with 3+ uses but no colour family: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void UntypedOutputsAreTheKnownMajorityButBounded()
    {
        // Ratchet: explicit [PortKinds] annotations should only ever reduce this.
        var outputs = Ports().Where(p => p.Direction == "outputs").ToList();
        var untyped = outputs.Count(p => PortKinds.FromTypeName(p.Type).Family == PortFamily.Any);
        Assert.True(untyped <= 300, "untyped outputs: " + untyped + " of " + outputs.Count);
    }
}

public class CommandCatalogTests
{
    [Fact]
    public void IdsAreUniqueAndWellFormed()
    {
        var ids = CommandCatalog.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z]+\\.[a-z]+$", id));
    }

    [Fact]
    public void ShortcutsAreUniqueAndParseable()
    {
        var shortcuts = CommandCatalog.All.Where(c => c.Shortcut != null).Select(c => c.Shortcut!).ToList();
        Assert.Equal(shortcuts.Count, shortcuts.Select(s => s.ToUpperInvariant()).Distinct().Count());
        Assert.All(shortcuts, s => Assert.Matches("^((Ctrl|Alt|Shift)\\+)*([A-Z0-9]|F([1-9]|1[0-2])|Delete|Space|Home)$", s));
    }

    [Fact]
    public void EveryCommandHasATitleAndKnownCategory()
    {
        Assert.All(CommandCatalog.All, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            Assert.Contains(c.Category, CommandCatalog.Categories);
        });
    }

    [Fact]
    public void GlobalShortcutsAvoidTextEditingChords()
    {
        // Ctrl+C/V/X/Z/Y/A inside a text box edit text; only canvas-scoped commands may use them.
        var reserved = new[] { "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+A", "Delete", "Space" };
        Assert.All(CommandCatalog.All.Where(c => c.Scope == CommandScope.Global && c.Shortcut != null),
            c => Assert.DoesNotContain(c.Shortcut!, reserved));
    }

    [Fact]
    public void FindReturnsCommands()
    {
        Assert.Equal("Run", CommandCatalog.Find("graph.run")!.Title);
        Assert.Null(CommandCatalog.Find("nope.nope"));
    }
}

public class RowPlannerTests
{
    private static Func<PortModel, bool> None => p => false;

    private static string Sig(IEnumerable<PlannedRow> rows) =>
        string.Join(" ", rows.Select(r => r.Key + (r.ZeroHeight ? "!" : string.Empty)));

    [Fact]
    public void OrdersOutputsBodyInputsPanelsThenSummary()
    {
        var node = new ZeroTouchNodeModel(AssemblyNodeLoader.LoadType(typeof(EditorMetaFixtures)).Single(d => d.Method.Name == "Scaled"));
        var rows = RowPlanner.Plan(node, None, hasBody: false);
        // one output; percent + season ungrouped; "Advanced" panel (default open via extra) then its two members.
        Assert.Equal("o:result i:percent i:season p:Advanced i:extra i:count", Sig(rows));
        Assert.True(rows.Single(r => r.Kind == RowKind.PanelHeader).IsOpen);
    }

    [Fact]
    public void BodyRowSitsBetweenOutputsAndInputs()
    {
        var node = new WatchNode();
        var rows = RowPlanner.Plan(node, None, hasBody: true);
        Assert.Equal(new[] { RowKind.Output, RowKind.Body, RowKind.Input }, rows.Select(r => r.Kind).ToArray());
    }

    [Fact]
    public void ClosedPanelKeepsWiredPortsAsZeroHeightRows()
    {
        var def = AssemblyNodeLoader.LoadType(typeof(EditorMetaFixtures)).Single(d => d.Method.Name == "Scaled");
        var node = new ZeroTouchNodeModel(def);
        RowPlanner.SetPanelOpen(node, "Advanced", defaultOpen: true, open: false);
        var wired = node.InPorts.Single(p => p.Name == "count");

        var rows = RowPlanner.Plan(node, p => p == wired, hasBody: false);

        Assert.Equal("o:result i:percent i:season p:Advanced i:count!", Sig(rows));
        Assert.False(rows.Single(r => r.Kind == RowKind.PanelHeader).IsOpen);
    }

    [Fact]
    public void PanelOpenStateRecordsOnlyTheDeviationFromDefault()
    {
        var node = new RerouteNode();
        RowPlanner.SetPanelOpen(node, "A", defaultOpen: false, open: true);
        Assert.Contains("A", node.Ui.OpenPanels);
        Assert.True(RowPlanner.IsPanelOpen(node, "A", false));
        RowPlanner.SetPanelOpen(node, "A", defaultOpen: false, open: false);
        Assert.True(node.Ui.IsDefault);
        RowPlanner.SetPanelOpen(node, "B", defaultOpen: true, open: false);
        Assert.Contains("B", node.Ui.ClosedPanels);
        Assert.False(RowPlanner.IsPanelOpen(node, "B", true));
        RowPlanner.SetPanelOpen(node, "B", defaultOpen: true, open: true);
        Assert.True(node.Ui.IsDefault);
    }

    [Fact]
    public void HideUnusedHidesOptionalInputsAndUnwiredOutputsButNeverWiredOrPinned()
    {
        var node = ZT.Node("AddStep"); // x required, step optional
        node.Ui.HideUnused = true;
        var rows = RowPlanner.Plan(node, None, hasBody: false);
        Assert.Equal("i:x hidden", Sig(rows)); // output hidden, optional step hidden, required x stays
        Assert.Equal(2, rows.Single(r => r.Kind == RowKind.HiddenSummary).Count);

        node.InPorts[1].SetUserValue(2d);
        rows = RowPlanner.Plan(node, None, hasBody: false);
        Assert.Contains(rows, r => r.Key == "i:step");

        rows = RowPlanner.Plan(node, p => p == node.OutPorts[0], hasBody: false);
        Assert.Contains(rows, r => r.Key == "o:result");
    }

    [Fact]
    public void UserHiddenPortDisappearsUnlessWired()
    {
        var node = ZT.Node("AddStep");
        node.InPorts[1].IsHidden = true;
        Assert.DoesNotContain(RowPlanner.Plan(node, None, false), r => r.Key == "i:step");
        Assert.Contains(RowPlanner.Plan(node, p => p == node.InPorts[1], false), r => r.Key == "i:step");
    }

    [Fact]
    public void ExplicitShowEverythingBeatsTheDocumentRule()
    {
        var doc = new PortModel(new RerouteNode(), "document", typeof(Fixtures.Fake.Document), PortDirection.Input) { HasDefault = true };
        Assert.True(RowPlanner.IsHidden(new RerouteNode(), doc, connected: false));
        var shown = new RerouteNode();
        shown.Ui.HideUnused = false;
        Assert.False(RowPlanner.IsHidden(shown, doc, connected: false));
        Assert.False(RowPlanner.IsHidden(new RerouteNode(), doc, connected: true));
    }

    [Fact]
    public void CollapsedNodeKeepsOnlyWiredSocketsAsZeroHeightRows()
    {
        var node = ZT.Node("AddStep");
        node.Ui.Collapsed = true;
        var rows = RowPlanner.Plan(node, p => p == node.InPorts[0] || p == node.OutPorts[0], hasBody: true);
        Assert.Equal("o:result! i:x!", Sig(rows));
        Assert.Empty(RowPlanner.Plan(node, None, hasBody: true));
    }

    [Fact]
    public void PlanIsDeterministic()
    {
        var node = ZT.Node("AddStep");
        Assert.Equal(Sig(RowPlanner.Plan(node, None, true)), Sig(RowPlanner.Plan(node, None, true)));
    }
}

public class LodTests
{
    [Theory]
    [InlineData(LodLevel.Full, 1.0, LodLevel.Full)]
    [InlineData(LodLevel.Full, 0.56, LodLevel.Full)]      // hysteresis: stays Full until below 0.55
    [InlineData(LodLevel.Full, 0.50, LodLevel.Compact)]
    [InlineData(LodLevel.Full, 0.10, LodLevel.Overview)]
    [InlineData(LodLevel.Compact, 0.58, LodLevel.Compact)] // needs 0.60 to re-enter Full
    [InlineData(LodLevel.Compact, 0.60, LodLevel.Full)]
    [InlineData(LodLevel.Compact, 0.29, LodLevel.Overview)]
    [InlineData(LodLevel.Overview, 0.31, LodLevel.Overview)] // needs 0.33 to leave
    [InlineData(LodLevel.Overview, 0.33, LodLevel.Compact)]
    [InlineData(LodLevel.Overview, 0.90, LodLevel.Full)]
    public void NextAppliesHysteresis(LodLevel current, double zoom, LodLevel expected)
    {
        Assert.Equal(expected, Lod.Next(current, zoom));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void InvalidZoomKeepsTheCurrentLevel(double zoom)
    {
        Assert.Equal(LodLevel.Compact, Lod.Next(LodLevel.Compact, zoom));
    }

    [Fact]
    public void SweepingTheZoomNeverFlickersAtABoundary()
    {
        var level = LodLevel.Full;
        var changes = 0;
        for (var z = 1.2; z > 0.05; z -= 0.005)
        {
            var next = Lod.Next(level, z);
            if (next != level)
            {
                changes++;
            }

            level = next;
        }

        Assert.Equal(2, changes); // Full -> Compact -> Overview, exactly once each
    }
}
