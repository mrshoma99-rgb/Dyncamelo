using System;
using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class InputNodesTests
{
    private static GraphModel RoundTrip(GraphModel graph)
    {
        var serializer = new GraphSerializer(NodeRegistry.CreateDefault());
        return serializer.Deserialize(serializer.Serialize(graph));
    }

    [Fact]
    public void AllThreeAreInTheDefaultRegistry_UnderInput()
    {
        var registry = NodeRegistry.CreateDefault();
        foreach (var type in new[] { IntegerInputNode.TypeName, DateInputNode.TypeName, ChoiceInputNode.TypeName })
        {
            var node = registry.CreateNode(type);
            Assert.NotNull(node);
            Assert.Equal("Input", node!.Category);
            Assert.Equal(NodeFunction.Create, node.Function);
        }
    }

    [Fact]
    public void Integer_GivesItsValue_AndSurvivesASaveAndLoad()
    {
        var graph = new GraphModel();
        graph.AddNode(new IntegerInputNode { Value = 42 });
        var loaded = RoundTrip(graph);
        var node = Assert.IsType<IntegerInputNode>(loaded.Nodes.Single());
        Assert.Equal(42, node.Value);

        new GraphEngine().Run(loaded);
        Assert.Equal(42L, node.OutPorts[0].Value);
    }

    [Fact]
    public void Integer_ThePlayerPortRoundsToAWholeNumber()
    {
        var node = new IntegerInputNode { Value = 3 };
        var port = node.CreatePlayerPort();
        port.SetUserValue(7.6);
        Assert.Equal(8, node.Value);
    }

    [Fact]
    public void Date_ReadsTextAsADate_WithOrWithoutATime()
    {
        var node = new DateInputNode { Text = "2026-10-01" };
        Assert.True(node.IsValid);
        Assert.False(node.IsInvalid);
        Assert.True(node.TryGetValue(out var date));
        Assert.Equal(new DateTime(2026, 10, 1), date);

        node.Text = "2026-10-01 14:30";
        Assert.True(node.TryGetValue(out date));
        Assert.Equal(new DateTime(2026, 10, 1, 14, 30, 0), date);
    }

    [Fact]
    public void Date_RunsAsADateTime_AndReportsTextThatIsNotADate()
    {
        var graph = new GraphModel();
        var good = new DateInputNode { Text = "2026-03-04" };
        var bad = new DateInputNode { Text = "next tuesday" };
        graph.AddNode(good);
        graph.AddNode(bad);

        new GraphEngine().Run(graph);

        Assert.Equal(new DateTime(2026, 3, 4), good.OutPorts[0].Value);
        Assert.Equal(NodeState.Error, bad.State);
        Assert.Contains("next tuesday", bad.StateMessage);
        Assert.Contains("2026-10-01", bad.StateMessage);
    }

    [Fact]
    public void Date_KeepsAHalfTypedValue_AndSavesIt()
    {
        var graph = new GraphModel();
        graph.AddNode(new DateInputNode { Text = "2026-1" });
        var loaded = RoundTrip(graph);
        Assert.Equal("2026-1", Assert.IsType<DateInputNode>(loaded.Nodes.Single()).Text);
    }

    [Fact]
    public void Date_StartsOnToday()
    {
        Assert.True(new DateInputNode().TryGetValue(out var date));
        Assert.Equal(DateTime.Today, date);
    }

    [Fact]
    public void Choice_OptionsAreTheNonBlankLines_TrimmedAndUnique()
    {
        var node = new ChoiceInputNode { OptionsText = "Architecture\r\n  MEP \n\nStructure\nMEP\n" };
        Assert.Equal(new[] { "Architecture", "MEP", "Structure" }, node.Options);
    }

    [Fact]
    public void Choice_GivesTheChosenTextAndItsPosition()
    {
        var graph = new GraphModel();
        var node = new ChoiceInputNode { OptionsText = "Architecture\nMEP\nStructure", Value = "MEP" };
        graph.AddNode(node);

        new GraphEngine().Run(graph);

        Assert.Equal("MEP", node.OutPorts[0].Value);
        Assert.Equal(1L, node.OutPorts[1].Value);
    }

    [Fact]
    public void Choice_WhenTheChosenOptionIsRemoved_FallsBackToTheFirst()
    {
        var node = new ChoiceInputNode { OptionsText = "A\nB\nC", Value = "C" };
        node.OptionsText = "A\nB";
        Assert.Equal("A", node.Value);

        node.OptionsText = string.Empty;
        Assert.Equal(string.Empty, node.Value);
    }

    [Fact]
    public void Choice_WithoutOptions_ReportsWhatToDo()
    {
        var graph = new GraphModel();
        var node = new ChoiceInputNode { OptionsText = "  \n " };
        graph.AddNode(node);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, node.State);
        Assert.Contains("one option per line", node.StateMessage);
    }

    [Fact]
    public void Choice_SurvivesASaveAndLoad_WithItsChoice()
    {
        var graph = new GraphModel();
        graph.AddNode(new ChoiceInputNode { OptionsText = "Low\nMedium\nHigh", Value = "High" });
        var loaded = RoundTrip(graph);
        var node = Assert.IsType<ChoiceInputNode>(loaded.Nodes.Single());
        Assert.Equal(new[] { "Low", "Medium", "High" }, node.Options);
        Assert.Equal("High", node.Value);
    }

    [Fact]
    public void Choice_ThePlayerPortOffersTheOptionsAsADropdown()
    {
        var node = new ChoiceInputNode { OptionsText = "One\nTwo\nThree\nFour", Value = "Two" };
        var port = node.CreatePlayerPort();
        Assert.Equal(new[] { "One", "Two", "Three", "Four" }, port.Choices);
        Assert.Equal("Two", port.DefaultValue);

        port.SetUserValue("Four");
        Assert.Equal("Four", node.Value);
        Assert.False(PortEditors.UseSegmentedChoices(port.Choices));
    }
}
