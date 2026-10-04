using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The colour nodes cope with "nothing" (VAL-12), refuse a count that would use all the memory (ENG-13), name the colour entry
/// they cannot read (ENG-15), warn about a t that is not a number (ENG-14), scrub and default sensibly (VAL-28) and the Color
/// Picker sits with the other inputs (VAL-27).
/// </summary>
public class ColorWaveTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static string Warnings(NodeModel node) =>
        string.Join(" | ", node.Messages.Where(m => m.Severity == MessageSeverity.Warning).Select(m => m.Text));

    // ------------------------------------------------------------ empty is a result (VAL-12)

    [Fact]
    public void ANodeAfterASearchThatFoundNothingStaysGreen()
    {
        var byValues = EngineRun.Run("Color.ByValues", -1, L());
        Assert.Equal(NodeState.Executed, byValues.State);
        Assert.Empty((IList<CamelGraphColor>)byValues.OutPorts[0].Value!);
        Assert.Empty((IList<object?>)byValues.OutPorts[1].Value!);
        Assert.Empty((IList<CamelGraphColor>)byValues.OutPorts[2].Value!);

        var gradient = EngineRun.Run("Color.Gradient", -1, 0);
        Assert.Equal(NodeState.Executed, gradient.State);
        Assert.Empty((IList<CamelGraphColor>)gradient.OutPorts[0].Value!);

        var random = EngineRun.Run("Color.RandomList", -1, 0);
        Assert.Equal(NodeState.Executed, random.State);
        Assert.Empty((IList<CamelGraphColor>)random.OutPorts[0].Value!);
    }

    // ------------------------------------------------------------ the size cap (ENG-13)

    [Theory]
    [InlineData("Color.Gradient")]
    [InlineData("Color.RandomList")]
    public void AWrongHugeCountIsAnErrorOnThatNodeAndTheLimitIsNamed(string name)
    {
        var node = EngineRun.Run(name, -1, int.MaxValue);

        Assert.Equal(NodeState.Error, node.State);
        Assert.Contains("at most 1,000,000 colors", node.StateMessage);
        Assert.Contains("2147483647", node.StateMessage);
        Assert.Contains("memory", node.StateMessage);
    }

    [Fact]
    public void TheLargestAllowedCountStillWorks()
    {
        Assert.Equal(1000000, ColorNodes.RandomList(1000000).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorNodes.RandomList(1000001));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorNodes.Gradient(1000001));
    }

    // ------------------------------------------------------------ plain messages (ENG-15)

    [Fact]
    public void AColorListThatHoldsAListNamesTheItemInsteadOfClrTypes()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ColorNodes.ByValues(L("a", "b", "c"), L(L(3, 1), L(2), L())));

        Assert.Contains("Color.ByValues: item 1 of 'colors'", ex.Message);
        Assert.Contains("[3, 1]", ex.Message);
        Assert.DoesNotContain("System.Collections", ex.Message);
        Assert.DoesNotContain("`1", ex.Message);
    }

    [Fact]
    public void AHexEntryThatIsNotAColorNamesTheItemAndTheFormat()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ColorNodes.ByValues(L("a", "b"), L("#FF0000", "not a color")));

        Assert.Contains("item 2 of 'colors'", ex.Message);
        Assert.Contains("not a color", ex.Message);
        Assert.Contains("#RRGGBB", ex.Message);
        Assert.NotNull(ColorNodes.ByValues(L("a", "b"), L("#FF0000", "#00FF00")));
    }

    // ------------------------------------------------------------ not a number (ENG-14)

    [Fact]
    public void LerpWithATThatIsNotANumberGivesTheStartColorAndAWarning()
    {
        var start = new CamelGraphColor(255, 10, 20, 30);
        var end = new CamelGraphColor(255, 200, 100, 50);

        var node = EngineRun.Run("Color.Lerp", -1, start, end, double.NaN);

        Assert.Equal(start, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("not a number", Warnings(node));
        Assert.Empty(EngineRun.Run("Color.Lerp", -1, start, end, 0.5).Messages);
        Assert.Equal(end, ColorNodes.Lerp(start, end, double.PositiveInfinity));
    }

    // ------------------------------------------------------------ ranges and defaults (VAL-28)

    [Fact]
    public void TheColourNodesRunWithNothingWiredAndScrubWithinTheirRanges()
    {
        var registry = EngineRun.Registry();
        ZeroTouchNodeModel Node(string name) => EngineRun.Create(registry, name);

        var hsv = Node("Color.ByHSV");
        Assert.Equal(new object?[] { 0d, 1d, 1d, 255 }, hsv.InPorts.Select(p => p.DefaultValue).ToArray());
        Assert.Equal(360d, hsv.InPorts[0].Range!.Max);
        Assert.Equal(1d, hsv.InPorts[1].Range!.Max);
        Assert.Equal(1d, hsv.InPorts[2].Range!.Max);
        Assert.Equal(0.05, hsv.InPorts[1].Range!.Step);

        foreach (var name in new[] { "Color.Lighten", "Color.Darken" })
        {
            var range = Node(name).InPorts[1].Range;
            Assert.NotNull(range);
            Assert.Equal(0d, range!.Min);
            Assert.Equal(1d, range.Max);
        }

        Assert.Equal(255, Node("Color.WithAlpha").InPorts[1].DefaultValue);
        Assert.Equal(5, Node("Color.Gradient").InPorts[0].DefaultValue);
        Assert.Equal(5, Node("Color.RandomList").InPorts[0].DefaultValue);
        Assert.Equal(1000000d, Node("Color.Gradient").InPorts[0].Range!.Max);
    }

    [Fact]
    public void HsvWithNothingWiredIsPureRed_AndAGradientHasFiveColors()
    {
        var hsv = EngineRun.Create(EngineRun.Registry(), "Color.ByHSV");
        var graph = new GraphModel();
        graph.AddNode(hsv);
        new CamelGraph.Core.Execution.GraphEngine().Run(graph);
        Assert.Equal(NodeState.Executed, hsv.State);
        Assert.Equal(new CamelGraphColor(255, 255, 0, 0), hsv.OutPorts[0].Value);

        var gradient = EngineRun.Create(EngineRun.Registry(), "Color.Gradient");
        var other = new GraphModel();
        other.AddNode(gradient);
        new CamelGraph.Core.Execution.GraphEngine().Run(other);
        Assert.Equal(5, ((IList<CamelGraphColor>)gradient.OutPorts[0].Value!).Count);
    }

    // ------------------------------------------------------------ the Color Picker is an input (VAL-27)

    [Fact]
    public void TheColorPickerLivesWithTheOtherInputsAndIsStillFoundAsColour()
    {
        var picker = new ColorPickerNode();

        Assert.Equal("Input", picker.Category);
        foreach (var word in new[] { "colour", "swatch", "palette" })
        {
            Assert.Contains(word, picker.SearchTags);
        }

        // Every other input node is in the same tab.
        var registry = EngineRun.Registry();
        Assert.Equal("Input", registry.CreateNode("NumberInput")!.Category);
    }
}
