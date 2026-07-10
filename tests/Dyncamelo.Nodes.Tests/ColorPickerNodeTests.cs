using System;
using System.Collections.Generic;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Regression tests for the interactive Color Picker node: editing a channel
/// must raise PropertyChanged with the CHANNEL's name (the UI swatch and slider
/// bindings listen for "A"/"R"/"G"/"B", not the internal helper name), must mark
/// the node and its downstream dirty (so AutoRun re-executes and a manual Run
/// picks up the change), and must raise the graph's Modified event that the
/// editor's AutoRun listens to.
/// </summary>
public class ColorPickerNodeTests
{
    [Fact]
    public void ChangingChannel_RaisesPropertyChangedWithChannelName_AndValue()
    {
        var node = new ColorPickerNode();
        var raised = new List<string>();
        node.PropertyChanged += (s, e) => raised.Add(e.PropertyName ?? string.Empty);

        node.R = 128;

        Assert.Contains("R", raised);
        Assert.Contains("Value", raised);
        Assert.DoesNotContain("SetChannel", raised);
    }

    [Fact]
    public void EachChannel_RaisesItsOwnPropertyName()
    {
        var node = new ColorPickerNode();
        var raised = new List<string>();
        node.PropertyChanged += (s, e) => raised.Add(e.PropertyName ?? string.Empty);

        node.A = 1;
        node.R = 2;
        node.G = 3;
        node.B = 4;

        Assert.Contains("A", raised);
        Assert.Contains("R", raised);
        Assert.Contains("G", raised);
        Assert.Contains("B", raised);
    }

    [Fact]
    public void ChangingChannel_MarksNodeAndDownstreamDirty_AndRaisesGraphModified()
    {
        var graph = new GraphModel();
        var picker = new ColorPickerNode();
        var watch = new Dyncamelo.Core.Nodes.WatchNode();
        graph.AddNode(picker);
        graph.AddNode(watch);
        Assert.True(graph.Connect(picker.OutPorts[0], watch.InPorts[0]).Success);

        var engine = new GraphEngine();
        engine.Run(graph);
        Assert.False(picker.IsDirty);
        Assert.False(watch.IsDirty);

        var modified = false;
        graph.Modified += (s, e) => modified = true;

        picker.G = 99;

        Assert.True(modified, "graph.Modified must fire so the editor's AutoRun re-runs");
        Assert.True(picker.IsDirty);
        Assert.True(watch.IsDirty, "downstream nodes must be dirtied for the next run");
    }

    [Fact]
    public void ChangingChannel_ReexecutesDownstreamOnNextRun()
    {
        var graph = new GraphModel();
        var picker = new ColorPickerNode();
        var watch = new Dyncamelo.Core.Nodes.WatchNode();
        graph.AddNode(picker);
        graph.AddNode(watch);
        graph.Connect(picker.OutPorts[0], watch.InPorts[0]);

        var engine = new GraphEngine();
        engine.Run(graph);
        Assert.Equal(new DyncameloColor(255, 0, 0, 0), watch.OutPorts[0].Value);

        picker.R = 200;
        picker.B = 50;
        var result = engine.Run(graph);

        Assert.Contains(watch, result.ExecutedNodes);
        Assert.Equal(new DyncameloColor(255, 200, 0, 50), watch.OutPorts[0].Value);
    }

    [Fact]
    public void SettingSameChannelValue_DoesNotDirtyOrRaiseModified()
    {
        var graph = new GraphModel();
        var picker = new ColorPickerNode();
        graph.AddNode(picker);
        picker.R = 10;

        var engine = new GraphEngine();
        engine.Run(graph);
        var modified = false;
        graph.Modified += (s, e) => modified = true;

        picker.R = 10;

        Assert.False(modified);
        Assert.False(picker.IsDirty);
    }

    [Fact]
    public void ChannelsClamp_AndValueReflectsChannels()
    {
        var node = new ColorPickerNode { A = 300, R = -5, G = 256, B = 128 };
        Assert.Equal(255, node.A);
        Assert.Equal(0, node.R);
        Assert.Equal(255, node.G);
        Assert.Equal(128, node.B);
        Assert.Equal(new DyncameloColor(255, 0, 255, 128), node.Value);

        var outputs = node.Evaluate(Array.Empty<object?>(), new EvaluationContext());
        Assert.Equal(node.Value, outputs[0]);
    }
}
