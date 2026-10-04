using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>IFC.IsGlobalId and IFC.Normalize (audit SYS-36): real columns mix 36-character GUIDs and 22-character GlobalIds.</summary>
public class IfcIdentityNodesTests
{
    private const string Guid1 = "3f81e10a-25b0-49ff-9520-63f2a763150a";
    private const string GlobalId1 = "0$WU4A9R19$vKWO$AdOnKA";

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

        public override string NodeType => "TestConstIfc";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new[] { _value };
    }

    private static ZeroTouchNodeModel Run(string name, object? first, string? form = null)
    {
        var graph = new GraphModel();
        var node = Registry.CreateZeroTouchNode(Registry.Definitions.Single(d => d.Name == name).Id)!;
        var source = new ConstNode(first);
        graph.AddNode(node);
        graph.AddNode(source);
        Assert.True(graph.Connect(source.OutPorts[0], node.InPorts[0]).Success);
        if (form != null)
        {
            node.InPorts[1].SetUserValue(form);
        }

        new GraphEngine().Run(graph);
        return node;
    }

    // ------------------------------------------------------------------------------------------------ IFC.IsGlobalId

    [Theory]
    [InlineData(GlobalId1, true)]
    [InlineData("  0$WU4A9R19$vKWO$AdOnKA  ", true)]
    [InlineData(Guid1, false)]
    [InlineData("{3F81E10A-25B0-49FF-9520-63F2A763150A}", false)]
    [InlineData("3f81e10a25b049ff952063f2a763150a", false)]
    [InlineData("0$WU4A9R19$vKWO$AdOnK", false)]
    [InlineData("0$WU4A9R19$vKWO$AdOnK!", false)]
    [InlineData("4$WU4A9R19$vKWO$AdOnKA", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsGlobalIdAnswersForEveryKindOfText(string? text, bool expected)
    {
        Assert.Equal(expected, IfcGuidNodes.IsGlobalId(text));
    }

    [Fact]
    public void IsGlobalIdSplitsAMixedColumnAndAnswersForEmptyCellsToo()
    {
        var node = Run("IFC.IsGlobalId", new List<object?> { GlobalId1, Guid1, null, "", "junk" });

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
        Assert.Equal(new object?[] { true, false, false, false, false }, ((IEnumerable<object?>)node.OutPorts[0].Value!).ToArray());
    }

    [Fact]
    public void IsGlobalIdIsAnInfoNodeInTheIfcCategoryWithSearchWords()
    {
        var definition = Registry.Definitions.Single(d => d.Name == "IFC.IsGlobalId");

        Assert.Equal("IFC", definition.Category);
        Assert.Equal(CamelGraph.Core.Graph.NodeFunction.Info, definition.Function);
        Assert.True(definition.Inputs[0].AcceptsNull);
        Assert.Contains("guid", definition.SearchTags);
        Assert.Contains("validate", definition.SearchTags);
    }

    // ------------------------------------------------------------------------------------------------ IFC.Normalize

    [Theory]
    [InlineData(Guid1, "globalId", GlobalId1)]
    [InlineData(GlobalId1, "globalId", GlobalId1)]
    [InlineData(GlobalId1, "guid", Guid1)]
    [InlineData(Guid1, "guid", Guid1)]
    [InlineData("{3F81E10A-25B0-49FF-9520-63F2A763150A}", "guid", Guid1)]
    [InlineData("3F81E10A25B049FF952063F2A763150A", "globalId", GlobalId1)]
    [InlineData("  0$WU4A9R19$vKWO$AdOnKA ", "GUID", Guid1)]
    [InlineData(Guid1, "GlobalId", GlobalId1)]
    public void NormalizeWritesEitherFormInTheRequestedOne(string id, string form, string expected)
    {
        Assert.Equal(expected, IfcGuidNodes.Normalize(id, form));
    }

    [Fact]
    public void NormalizeDefaultsToTheGlobalIdForm()
    {
        Assert.Equal(GlobalId1, IfcGuidNodes.Normalize(Guid1));
    }

    [Fact]
    public void NormalizeAgreesWithEncodeAndDecodeOnRandomGuids()
    {
        var random = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            var guid = new Guid(bytes);
            var text = guid.ToString("D");

            Assert.Equal(IfcGuidNodes.GuidEncode(text), IfcGuidNodes.Normalize(text));
            Assert.Equal(IfcGuidNodes.GuidEncode(text), IfcGuidNodes.Normalize(IfcGuidNodes.GuidEncode(text)));
            Assert.Equal(text, IfcGuidNodes.Normalize(IfcGuidNodes.GuidEncode(text), "guid"));
        }
    }

    [Fact]
    public void NormalizeMakesAMixedColumnUniformAndReportsTheBadRowOnce()
    {
        var node = Run("IFC.Normalize", new List<object?> { Guid1, GlobalId1, "not an id", "" });

        // The two good rows are GlobalIds, the two bad ones are empty and one warning says why (the audit's "null per row").
        Assert.Equal(NodeState.Warning, node.State);
        var result = ((IEnumerable<object?>)node.OutPorts[0].Value!).ToArray();
        Assert.Equal(new object?[] { GlobalId1, GlobalId1, null, null }, result);
        Assert.Contains("2 of 4 laced calls failed", node.Messages.Single().Text);
    }

    [Theory]
    [InlineData("not an id", "which is not a hexadecimal digit")]
    [InlineData("0$WU4A9R19$vKWO$AdOnK!", "contains the character '!'")]
    [InlineData("4$WU4A9R19$vKWO$AdOnKA", "first character of an IFC GlobalId can only be 0, 1, 2 or 3")]
    [InlineData("3f81e10a-25b0-49ff-9520-63f2a763150", "hexadecimal digit(s)")]
    public void NormalizeSaysWhatIsWrongWithATextThatIsNeitherForm(string id, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => IfcGuidNodes.Normalize(id));

        Assert.StartsWith("IFC.Normalize: ", ex.Message);
        Assert.Contains(reason, ex.Message);
        Assert.Contains("Wire an IFC GlobalId (22 characters) or a GUID (32 hexadecimal digits).", ex.Message);
    }

    [Fact]
    public void NormalizeRefusesAnEmptyIdAndAnUnknownFormInPlainWords()
    {
        Assert.Contains("the id is empty", Assert.Throws<ArgumentException>(() => IfcGuidNodes.Normalize("  ")).Message);
        Assert.Contains("form must be 'globalId' or 'guid', not 'text'", Assert.Throws<ArgumentException>(() => IfcGuidNodes.Normalize(Guid1, "text")).Message);
        Assert.Contains("requires an IFC GlobalId or a GUID", Assert.Throws<ArgumentNullException>(() => IfcGuidNodes.Normalize(null!)).Message);
    }

    [Fact]
    public void NormalizeIsAChoiceOfTwoFormsInTheIfcCategory()
    {
        var definition = Registry.Definitions.Single(d => d.Name == "IFC.Normalize");

        Assert.Equal("IFC", definition.Category);
        Assert.Equal(new[] { "id", "form" }, definition.Inputs.Select(i => i.Name));
        Assert.Equal(new[] { "globalId", "guid" }, definition.Inputs[1].Choices);
        Assert.Equal("globalId", definition.Inputs[1].DefaultValue);
        Assert.Contains("normalize", definition.SearchTags);
        Assert.Contains("normalise", definition.SearchTags);
    }
}
