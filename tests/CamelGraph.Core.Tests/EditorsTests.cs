using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests
{
    public class PortEditorTests
    {
        private static PortModel Input(string name, Type type, bool hasDefault = false, object? def = null, NodeRangeAttribute? range = null)
        {
            var node = new ValueNode();
            var port = new PortModel(node, name, type, PortDirection.Input) { HasDefault = hasDefault, DefaultValue = def, Range = range };
            return port;
        }

        [Theory]
        [InlineData(typeof(double), PortEditorKind.Number)]
        [InlineData(typeof(int), PortEditorKind.Number)]
        [InlineData(typeof(bool), PortEditorKind.Toggle)]
        [InlineData(typeof(string), PortEditorKind.Text)]
        [InlineData(typeof(DateTime), PortEditorKind.Text)]
        [InlineData(typeof(object), PortEditorKind.None)]
        [InlineData(typeof(double[]), PortEditorKind.None)]
        [InlineData(typeof(IList<string>), PortEditorKind.None)]
        public void EditorKindFollowsTheDeclaredType(Type type, PortEditorKind expected)
        {
            Assert.Equal(expected, PortEditors.Resolve(Input("value", type)));
        }

        [Fact]
        public void ChoicesAndEnumsGetADropdownOutputsGetNothing()
        {
            var choice = Input("mode", typeof(string));
            choice.Choices = new[] { "A", "B" };
            Assert.Equal(PortEditorKind.Choice, PortEditors.Resolve(choice));

            var output = new PortModel(new ValueNode(), "out", typeof(double), PortDirection.Output);
            Assert.Equal(PortEditorKind.None, PortEditors.Resolve(output));
        }

        [Fact]
        public void PathNamedStringsGetThePathEditorAndFolderDetection()
        {
            var file = Input("outputPath", typeof(string));
            Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(file));
            Assert.False(PortEditors.IsFolder(file));
            Assert.True(PortEditors.IsFolder(Input("exportFolder", typeof(string))));
            Assert.True(PortEditors.IsFolder(Input("outputDirectory", typeof(string))));
        }

        [Fact]
        public void CamelGraphColourTypesAreRecognisedAsColour()
        {
            Assert.Equal(PortEditorKind.Colour, PortEditors.Resolve(Input("color", typeof(Fixtures.Fake.CamelGraphColor))));
            Assert.Equal(PortFamily.Colour, PortKinds.FromType(typeof(Fixtures.Fake.CamelGraphColor)).Family);
        }

        [Theory]
        [InlineData(new[] { "A", "B" }, true)]
        [InlineData(new[] { "Alpha", "Beta", "Gamma" }, true)]
        [InlineData(new[] { "A", "B", "C", "D" }, false)]
        [InlineData(new[] { "AVeryLongChoiceName", "AnotherLongChoiceName" }, false)]
        [InlineData(new[] { "Only" }, false)]
        public void SegmentedChoicesOnlyWhenFewAndShort(string[] choices, bool expected)
        {
            Assert.Equal(expected, PortEditors.UseSegmentedChoices(choices));
        }

        [Fact]
        public void NumbersAreStoredInThePortsOwnType()
        {
            var i = Input("count", typeof(int), true, 3);
            PortEditors.SetNumber(i, 7.6);
            Assert.IsType<int>(i.UserValue);
            Assert.Equal(8, i.UserValue);

            var d = Input("scale", typeof(double), true, 1.0);
            PortEditors.SetNumber(d, 2.5);
            Assert.IsType<double>(d.UserValue);

            var f = Input("f", typeof(float), true, 1f);
            PortEditors.SetNumber(f, 0.5);
            Assert.IsType<float>(f.UserValue);

            var l = Input("big", typeof(long), true, 1L);
            PortEditors.SetNumber(l, 5_000_000_000d);
            Assert.Equal(5_000_000_000L, l.UserValue);
        }

        [Fact]
        public void EditingBackToTheDefaultClearsThePin()
        {
            var p = Input("scale", typeof(double), true, 1.0);
            PortEditors.SetNumber(p, 2.0);
            Assert.True(p.HasUserValue);
            PortEditors.SetNumber(p, 1.0);
            Assert.False(p.HasUserValue);
        }

        [Fact]
        public void RangeClampsTypedAndCodedValues()
        {
            var p = Input("opacity", typeof(double), true, 1.0, new NodeRangeAttribute(0, 1));
            PortEditors.SetNumber(p, 5);
            Assert.Equal(1.0, PortEditors.GetNumber(p));
            PortEditors.SetNumber(p, -3);
            Assert.Equal(0.0, PortEditors.GetNumber(p));
            PortEditors.SetNumber(p, double.NaN);
            Assert.True(double.IsFinite(PortEditors.GetNumber(p)));
        }

        [Fact]
        public void RequiredInputWithoutDefaultCanBeFilledInline()
        {
            var p = Input("x", typeof(double));
            Assert.True(PortEditors.IsUnset(p));
            PortEditors.SetNumber(p, 4);
            Assert.False(PortEditors.IsUnset(p));
            Assert.Equal(4.0, PortEditors.GetNumber(p));
        }

        [Fact]
        public void BooleanAndTextEditorsPinAndReset()
        {
            var b = Input("flag", typeof(bool), true, false);
            PortEditors.SetBool(b, true);
            Assert.True(PortEditors.GetBool(b));
            PortEditors.SetBool(b, false);
            Assert.False(b.HasUserValue);

            var t = Input("name", typeof(string), true, "x");
            PortEditors.SetText(t, "hello");
            Assert.Equal("hello", PortEditors.GetText(t));
            PortEditors.SetText(t, "x");
            Assert.False(t.HasUserValue);

            var req = Input("req", typeof(string));
            PortEditors.SetText(req, "");
            Assert.False(req.HasUserValue);
        }

        [Theory]
        [InlineData("#FF8800", true, 255, 255, 136, 0)]
        [InlineData("80FF0000", true, 128, 255, 0, 0)]
        [InlineData(" #00ff00 ", true, 255, 0, 255, 0)]
        [InlineData("#12345", false, 0, 0, 0, 0)]
        [InlineData("nothex", false, 0, 0, 0, 0)]
        [InlineData("", false, 0, 0, 0, 0)]
        public void HexColoursParse(string hex, bool ok, int a, int r, int g, int b)
        {
            Assert.Equal(ok, PortEditors.TryParseHex(hex, out var pa, out var pr, out var pg, out var pb));
            if (ok)
            {
                Assert.Equal(new[] { a, r, g, b }, new[] { (int)pa, pr, pg, pb });
            }
        }

        [Fact]
        public void ColoursRoundTripAsHex()
        {
            var p = Input("color", typeof(Fixtures.Fake.CamelGraphColor));
            PortEditors.SetColour(p, 255, 10, 20, 30);
            Assert.Equal("#FF0A141E", p.UserValue);
            Assert.Equal("#FF0A141E", PortEditors.GetColourHex(p));
            Assert.Equal(string.Empty, PortEditors.GetColourHex(Input("c", typeof(Fixtures.Fake.CamelGraphColor))));
        }
    }

    public class ScrubMathTests
    {
        private static NumberEditSpec Real(double min = double.MinValue, double max = double.MaxValue, double step = 0.1) =>
            new NumberEditSpec { Min = min, Max = max, Step = step };

        [Fact]
        public void UnboundedDragMovesEightPixelsPerStep()
        {
            Assert.Equal(1.0 + 0.5, ScrubMath.Scrub(1.0, 40, Real(step: 0.1), 100, fine: false, snap: false), 9);
        }

        [Fact]
        public void ShiftIsATenthAsSensitive()
        {
            var coarse = ScrubMath.Scrub(0, 80, Real(step: 1), 100, false, false);
            var fine = ScrubMath.Scrub(0, 80, Real(step: 1), 100, true, false);
            Assert.Equal(10.0, coarse, 9);
            Assert.Equal(1.0, fine, 9);
        }

        [Fact]
        public void CtrlSnapsToStepMultiples()
        {
            var v = ScrubMath.Scrub(0, 23, Real(step: 0.5), 100, false, snap: true);
            Assert.Equal(0d, v % 0.5, 9);
        }

        [Fact]
        public void FiniteSoftRangeSweepsTheRangeAcrossTheField()
        {
            var spec = new NumberEditSpec { Min = 0, Max = 100, SoftMin = 0, SoftMax = 50, Step = 1 };
            Assert.True(spec.HasSoftRange);
            Assert.Equal(25.0, ScrubMath.Scrub(0, 100, spec, 200, false, false), 9); // half the field = half the range
        }

        [Fact]
        public void HardRangeClampsAScrub()
        {
            var spec = new NumberEditSpec { Min = 0, Max = 1, Step = 0.01 };
            Assert.Equal(1.0, ScrubMath.Scrub(0.9, 10_000, spec, 100, false, false));
            Assert.Equal(0.0, ScrubMath.Scrub(0.1, -10_000, spec, 100, false, false));
        }

        [Fact]
        public void IntegerFieldsRound()
        {
            var spec = new NumberEditSpec { IsInteger = true, Step = 1 };
            Assert.Equal(3.0, ScrubMath.Scrub(0, 26, spec, 100, false, false)); // 3.25 -> 3
            Assert.Equal(5.0, ScrubMath.StepBy(4, 1, spec));
            Assert.Equal(3.0, ScrubMath.StepBy(4, -1, spec));
        }

        [Theory]
        [InlineData("2*3+1", 7.0)]
        [InlineData("  12.5 ", 12.5)]
        [InlineData("2.6 m", 2.6)]
        [InlineData("50%", 50.0)]
        public void TypedTextIsEvaluatedAndUnitStripped(string text, double expected)
        {
            var spec = Real();
            spec.Unit = text.Contains("%") ? "%" : "m";
            Assert.True(ScrubMath.TryParse(text, spec, out var v));
            Assert.Equal(expected, v, 9);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("1/0")]
        [InlineData("1e999")]
        public void BadTypedTextIsRejected(string text)
        {
            Assert.False(ScrubMath.TryParse(text, Real(), out _));
        }

        [Fact]
        public void TypedValuesBeyondTheSoftRangeAreKeptButHardRangeClamps()
        {
            var spec = new NumberEditSpec { Min = 0, Max = 100, SoftMin = 0, SoftMax = 10, Step = 1 };
            Assert.True(ScrubMath.TryParse("40", spec, out var v));
            Assert.Equal(40.0, v);
            Assert.True(ScrubMath.TryParse("400", spec, out v));
            Assert.Equal(100.0, v);
        }

        [Fact]
        public void SpecComesFromTheRangeAttributeOrANiceStepFromTheDefault()
        {
            var node = new ValueNode();
            var ranged = new PortModel(node, "opacity", typeof(double), PortDirection.Input)
            {
                HasDefault = true,
                DefaultValue = 0.5,
                Range = new NodeRangeAttribute(0, 1) { Step = 0.05, Unit = "" },
            };
            var spec = NumberEditSpec.FromPort(ranged);
            Assert.Equal(0.05, spec.Step);
            Assert.Equal(1.0, spec.Max);

            var plain = new PortModel(node, "size", typeof(double), PortDirection.Input) { HasDefault = true, DefaultValue = 50.0 };
            Assert.Equal(1.0, NumberEditSpec.FromPort(plain).Step);

            var integer = new PortModel(node, "n", typeof(int), PortDirection.Input) { HasDefault = true, DefaultValue = 3 };
            var intSpec = NumberEditSpec.FromPort(integer);
            Assert.True(intSpec.IsInteger);
            Assert.Equal(1.0, intSpec.Step);
        }
    }
}

namespace CamelGraph.Core.Tests
{
    public class InfiniteDefaultTests
    {
        [Fact]
        public void AnUnboundedMaximumShowsAsInfinityAndCanBeReplacedByATypedNumber()
        {
            var port = new PortModel(new ValueNode(), "maxDepth", typeof(double), PortDirection.Input)
            {
                HasDefault = true,
                DefaultValue = double.PositiveInfinity,
            };
            Assert.True(double.IsPositiveInfinity(PortEditors.GetNumber(port)));
            Assert.Equal("∞", NumberFormat.Format(PortEditors.GetNumber(port), 0.1));
            Assert.Equal("-∞", NumberFormat.Format(double.NegativeInfinity, 0.1));

            PortEditors.SetNumber(port, 12.5);
            Assert.Equal(12.5, port.UserValue);
            port.ClearUserValue();
            Assert.True(double.IsPositiveInfinity(PortEditors.GetNumber(port)));
        }
    }
}

public class ColourMathTests
{
    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(12, 200, 99)]
    [InlineData(128, 128, 128)]
    public void RgbSurvivesTheRoundTripThroughHsv(int r, int g, int b)
    {
        ColourMath.RgbToHsv((byte)r, (byte)g, (byte)b, out var h, out var s, out var v);
        ColourMath.HsvToRgb(h, s, v, out var r2, out var g2, out var b2);
        Assert.Equal((r, g, b), ((int)r2, (int)g2, (int)b2));
    }

    [Fact]
    public void HueWrapsAndSaturationClamps()
    {
        ColourMath.HsvToRgb(-120, 1, 1, out var r, out var g, out var b);
        Assert.Equal((0, 0, 255), ((int)r, (int)g, (int)b));
        ColourMath.HsvToRgb(480, 5, 1, out r, out g, out b);
        Assert.Equal((0, 255, 0), ((int)r, (int)g, (int)b));
    }
}

// Stand-ins named like the Navisworks types so declared-type detection sees model elements.
public sealed class ModelItem
{
}

public sealed class ModelItemCollection : System.Collections.Generic.List<ModelItem>
{
}

public sealed class ModelPortsNode : NodeModel
{
    public ModelPortsNode()
    {
        Name = "ModelPorts";
        AddInput("item", typeof(ModelItem), null);
        AddInput("items", typeof(IEnumerable<ModelItem>), null);
        AddInput("list", typeof(List<ModelItem>), null);
        AddInput("collection", typeof(ModelItemCollection), null);
        AddInput("other", typeof(IEnumerable<string>), null);
        AddOutput("out", typeof(object));
    }

    public override string NodeType => "TestModelPorts";

    public override object?[] Evaluate(object?[] inputs, CamelGraph.Core.Execution.EvaluationContext context) => new object?[] { null };
}

public class ModelPickerTests
{
    [Fact]
    public void ModelElementPortsGetThePicker()
    {
        var node = new ModelPortsNode();
        Assert.Equal(PortEditorKind.Model, PortEditors.Resolve(node.InPorts[0]));
        Assert.Equal(PortEditorKind.Model, PortEditors.Resolve(node.InPorts[1]));
        Assert.Equal(PortEditorKind.Model, PortEditors.Resolve(node.InPorts[2]));
        Assert.Equal(PortEditorKind.Model, PortEditors.Resolve(node.InPorts[3]));
        Assert.Equal(PortEditorKind.None, PortEditors.Resolve(node.InPorts[4]));
    }

    [Fact]
    public void OnlyAnItemPortIsSingle()
    {
        var node = new ModelPortsNode();
        Assert.True(PortEditors.IsSingleModelItem(node.InPorts[0]));
        Assert.False(PortEditors.IsSingleModelItem(node.InPorts[1]));
        Assert.False(PortEditors.IsSingleModelItem(node.InPorts[3]));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("hello", 0)]
    [InlineData("nw:", 0)]
    [InlineData("nw:0:1/2", 1)]
    [InlineData("nw:0:1/2;0:4/5;1:", 3)]
    public void StoredValuesAreCounted(string? value, int expected)
    {
        Assert.Equal(expected, ModelPickerHost.CountOf(value));
    }

    [Fact]
    public void ThePinnedValueRoundTripsThroughTheGraphFile()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestModelPorts", () => new ModelPortsNode());
        var graph = new GraphModel();
        var node = new ModelPortsNode();
        graph.AddNode(node);
        node.InPorts[1].SetUserValue("nw:0:1/2;0:3");

        var json = new CamelGraph.Core.Serialization.GraphSerializer(registry).Serialize(graph);
        var loaded = new CamelGraph.Core.Serialization.GraphSerializer(registry).Deserialize(json);

        Assert.Equal("nw:0:1/2;0:3", loaded.Nodes.Single().InPorts[1].UserValue);
    }
}
