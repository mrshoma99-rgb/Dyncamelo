using System;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Core.Tests.Fixtures;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class SerializationTests
{
    private static NodeRegistry FullRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);
        return registry;
    }

    private static GraphModel BuildSampleGraph()
    {
        var graph = new GraphModel
        {
            Name = "Sample",
            Description = "Round-trip sample",
            RunType = RunType.Manual,
        };

        var slider = new NumberSliderNode { Min = 0, Max = 50, Step = 0.5, Value = 16 };
        slider.X = 10;
        slider.Y = 20;
        graph.AddNode(slider);

        var number = new NumberInputNode { Value = 9 };
        graph.AddNode(number);

        var add = ZT.Node("Add");
        add.Lacing = LacingMode.Longest;
        add.X = 300;
        graph.AddNode(add);

        var watch = new WatchNode();
        graph.AddNode(watch);

        ZT.Wire(graph, slider, 0, add, 0);
        ZT.Wire(graph, number, 0, add, 1);
        ZT.Wire(graph, add, 0, watch, 0);

        graph.Notes.Add(new NoteModel { Text = "hello", X = 1, Y = 2 });
        return graph;
    }

    [Fact]
    public void RoundTrip_PreservesStructureMetadataAndPayloads()
    {
        var graph = BuildSampleGraph();
        var serializer = new GraphSerializer(FullRegistry());

        var json = serializer.Serialize(graph);
        var loaded = serializer.Deserialize(json);

        Assert.Equal(graph.Uuid, loaded.Uuid);
        Assert.Equal("Sample", loaded.Name);
        Assert.Equal("Round-trip sample", loaded.Description);
        Assert.Equal(RunType.Manual, loaded.RunType);
        Assert.Equal(4, loaded.Nodes.Count);
        Assert.Equal(3, loaded.Connections.Count);

        var slider = Assert.IsType<NumberSliderNode>(loaded.Nodes.Single(n => n.NodeType == NumberSliderNode.TypeName));
        Assert.Equal(16, slider.Value);
        Assert.Equal(50, slider.Max);
        Assert.Equal(0.5, slider.Step);
        Assert.Equal(10, slider.X);
        Assert.Equal(20, slider.Y);

        var add = Assert.IsType<ZeroTouchNodeModel>(loaded.Nodes.Single(n => n.NodeType == ZeroTouchNodeModel.TypeName));
        Assert.Equal(LacingMode.Longest, add.Lacing);
        Assert.Equal(300, add.X);
        Assert.Equal("Dyncamelo.Core.Tests.Fixtures.MathFixtures.Add@double,double", add.Definition.Id);

        var note = Assert.Single(loaded.Notes);
        Assert.Equal("hello", note.Text);

        // Identity survives.
        Assert.Equal(
            graph.Nodes.Select(n => n.Id).OrderBy(id => id),
            loaded.Nodes.Select(n => n.Id).OrderBy(id => id));
    }

    [Fact]
    public void RoundTrip_LoadedGraphRunsAndProducesSameResult()
    {
        var graph = BuildSampleGraph();
        var engine = new GraphEngine();
        engine.Run(graph);
        var originalWatch = (WatchNode)graph.Nodes.Single(n => n is WatchNode);

        var serializer = new GraphSerializer(FullRegistry());
        var loaded = serializer.Deserialize(serializer.Serialize(graph));

        Assert.All(loaded.Nodes, n => Assert.True(n.IsDirty)); // states/values are not persisted
        engine.Run(loaded);
        var loadedWatch = (WatchNode)loaded.Nodes.Single(n => n is WatchNode);
        Assert.Equal(originalWatch.FormattedValue, loadedWatch.FormattedValue);
        Assert.Equal(25.0, (double)loadedWatch.OutPorts[0].Value!);
    }

    [Fact]
    public void UsingDefaultValue_RoundTrips()
    {
        var graph = new GraphModel();
        var x = ZT.Value(graph, 1.0);
        var addStep = ZT.Node("AddStep");
        graph.AddNode(addStep);
        ZT.Wire(graph, x, 0, addStep, 0);
        addStep.InPorts[1].UsingDefaultValue = false; // user disabled the default

        var registry = FullRegistry();
        registry.RegisterNodeType("TestValue", () => new ValueNode());
        var serializer = new GraphSerializer(registry);
        var loaded = serializer.Deserialize(serializer.Serialize(graph));

        var loadedNode = (ZeroTouchNodeModel)loaded.Nodes.Single(n => n is ZeroTouchNodeModel);
        Assert.False(loadedNode.InPorts[1].UsingDefaultValue);
        Assert.True(loadedNode.InPorts[1].HasDefault);
    }

    [Fact]
    public void PinnedUserValue_RoundTrips()
    {
        var graph = new GraphModel();
        var pick = ZT.Node("Pick");
        graph.AddNode(pick);
        pick.InPorts.Single(p => p.Name == "option").SetUserValue("Gamma");

        var serializer = new GraphSerializer(FullRegistry());
        var loaded = serializer.Deserialize(serializer.Serialize(graph));

        var loadedPort = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single()
            .InPorts.Single(p => p.Name == "option");
        Assert.True(loadedPort.HasUserValue);
        Assert.Equal("Gamma", loadedPort.UserValue);
    }

    [Fact]
    public void UnpinnedPort_HasNoUserValueAfterRoundTrip()
    {
        var graph = new GraphModel();
        graph.AddNode(ZT.Node("Pick"));

        var serializer = new GraphSerializer(FullRegistry());
        var loaded = serializer.Deserialize(serializer.Serialize(graph));

        var loadedPort = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single()
            .InPorts.Single(p => p.Name == "option");
        Assert.False(loadedPort.HasUserValue);
    }

    [Fact]
    public void LegacyDefinitionId_LoadsViaAlias_KeepsOldBehavior_AndMigratesOnSave()
    {
        // Build a graph with the CURRENT Doubler(x, scale = 2), then rewrite its
        // serialized form into what a pre-change file looked like: the legacy
        // definition id and no "scale" input port.
        var graph = new GraphModel();
        var value = new NumberInputNode { Value = 3 };
        graph.AddNode(value);
        var doubler = ZT.Node("Doubler");
        graph.AddNode(doubler);
        var watch = new WatchNode();
        graph.AddNode(watch);
        ZT.Wire(graph, value, 0, doubler, 0);
        ZT.Wire(graph, doubler, 0, watch, 0);

        var serializer = new GraphSerializer(FullRegistry());

        var json = JObject.Parse(serializer.Serialize(graph));
        var nodeJson = (JObject)json["Nodes"]!
            .Single(n => n.Value<string>("DefinitionId") != null);
        nodeJson["DefinitionId"] = "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double";
        var inputPorts = (JArray)nodeJson["InputPorts"]!;
        inputPorts.Remove(inputPorts.Single(p => p.Value<string>("Name") == "scale"));

        // The legacy file loads as a LIVE node (not a MissingNodeModel)...
        var loaded = serializer.Deserialize(json.ToString());
        var loadedDoubler = Assert.IsType<ZeroTouchNodeModel>(loaded.Nodes.Single(n => n is ZeroTouchNodeModel));

        // ...the appended port exists and keeps its default, so behavior is the
        // pre-change behavior exactly...
        var scale = loadedDoubler.InPorts.Single(p => p.Name == "scale");
        Assert.True(scale.UsingDefaultValue);
        Assert.Equal(2, loaded.Connections.Count);
        new GraphEngine().Run(loaded);
        var loadedWatch = (WatchNode)loaded.Nodes.Single(n => n is WatchNode);
        Assert.Equal(6.0, (double)loadedWatch.OutPorts[0].Value!); // 3 * 2 (old semantics)

        // ...and re-saving migrates the file to the current id.
        var resaved = JObject.Parse(serializer.Serialize(loaded));
        Assert.Equal(
            "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double,double",
            resaved["Nodes"]!.Single(n => n.Value<string>("DefinitionId") != null).Value<string>("DefinitionId"));
    }

    [Fact]
    public void UnknownDefinition_LoadsAsMissingNode_PreservingPortsAndConnections()
    {
        var graph = BuildSampleGraph();
        var fullSerializer = new GraphSerializer(FullRegistry());
        var json = fullSerializer.Serialize(graph);

        // A registry that knows the built-ins but not the zero-touch fixture methods.
        var limited = new GraphSerializer(NodeRegistry.CreateDefault());
        var loaded = limited.Deserialize(json);

        var missing = Assert.IsType<MissingNodeModel>(
            loaded.Nodes.Single(n => n is MissingNodeModel));
        Assert.Equal(2, missing.InPorts.Count);
        Assert.Single(missing.OutPorts);
        Assert.Equal(NodeState.Error, missing.State);
        Assert.Equal(3, loaded.Connections.Count); // wires into/out of the placeholder survived
    }

    [Fact]
    public void MissingNode_RoundTripsItsOriginalJson_AndResolvesAgainstAFullRegistry()
    {
        var graph = BuildSampleGraph();
        var fullSerializer = new GraphSerializer(FullRegistry());
        var json = fullSerializer.Serialize(graph);

        // Load without definitions, re-save: nothing may be lost.
        var limited = new GraphSerializer(NodeRegistry.CreateDefault());
        var degraded = limited.Deserialize(json);
        var resaved = limited.Serialize(degraded);

        var resavedNode = JObject.Parse(resaved)["Nodes"]!
            .Single(n => n.Value<string>("NodeType") == ZeroTouchNodeModel.TypeName);
        Assert.Equal(
            "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Add@double,double",
            resavedNode.Value<string>("DefinitionId"));

        // Loading the re-saved file with the full registry restores a live node.
        var restored = fullSerializer.Deserialize(resaved);
        var add = Assert.IsType<ZeroTouchNodeModel>(restored.Nodes.Single(n => n is ZeroTouchNodeModel));
        Assert.Equal(LacingMode.Longest, add.Lacing);

        new GraphEngine().Run(restored);
        var watch = (WatchNode)restored.Nodes.Single(n => n is WatchNode);
        Assert.Equal(25.0, (double)watch.OutPorts[0].Value!);
    }

    [Fact]
    public void MissingNode_ReportsErrorOnRun_AndDownstreamWarns()
    {
        var graph = BuildSampleGraph();
        var json = new GraphSerializer(FullRegistry()).Serialize(graph);
        var loaded = new GraphSerializer(NodeRegistry.CreateDefault()).Deserialize(json);

        new GraphEngine().Run(loaded);

        var missing = (MissingNodeModel)loaded.Nodes.Single(n => n is MissingNodeModel);
        Assert.Equal(NodeState.Error, missing.State);
        var watch = (WatchNode)loaded.Nodes.Single(n => n is WatchNode);
        Assert.Equal(NodeState.Warning, watch.State);
        Assert.Contains("Upstream failure", watch.StateMessage);
    }

    [Fact]
    public void CorruptNodeDataPayload_DegradesToMissingNode_InsteadOfFailingTheOpen()
    {
        var graph = BuildSampleGraph();
        var serializer = new GraphSerializer(FullRegistry());
        var json = JObject.Parse(serializer.Serialize(graph));

        // Corrupt the slider's private payload: "Min" becomes an object token,
        // which NumberSliderNode.DeserializeData cannot read as a number.
        var sliderJson = (JObject)json["Nodes"]!
            .Single(n => n.Value<string>("NodeType") == NumberSliderNode.TypeName);
        sliderJson["Data"]!["Min"] = new JObject();

        var loaded = serializer.Deserialize(json.ToString());

        var missing = Assert.IsType<MissingNodeModel>(loaded.Nodes.Single(n => n is MissingNodeModel));
        Assert.Contains("could not be read", missing.Reason);
        Assert.Equal(4, loaded.Nodes.Count); // all other nodes loaded normally
        Assert.Equal(3, loaded.Connections.Count); // wires survive via the placeholder's ports
    }

    [Fact]
    public void MissingNode_UserEdits_SurviveResave()
    {
        var graph = BuildSampleGraph();
        var json = new GraphSerializer(FullRegistry()).Serialize(graph);

        // Load without the zero-touch definitions, edit the placeholder, re-save.
        var limited = new GraphSerializer(NodeRegistry.CreateDefault());
        var degraded = limited.Deserialize(json);
        var missing = (MissingNodeModel)degraded.Nodes.Single(n => n is MissingNodeModel);
        missing.Name = "renamed-by-user";
        missing.IsFrozen = true;
        var resaved = limited.Serialize(degraded);

        // Reloading with the full registry restores a live node with the edits intact.
        var restored = new GraphSerializer(FullRegistry()).Deserialize(resaved);
        var add = Assert.IsType<ZeroTouchNodeModel>(restored.Nodes.Single(n => n is ZeroTouchNodeModel));
        Assert.Equal("renamed-by-user", add.Name);
        Assert.True(add.IsFrozen);
    }

    [Fact]
    public void UnknownTopLevelAndNodeFields_AreIgnoredNotFatal()
    {
        var graph = new GraphModel();
        graph.AddNode(new NumberInputNode { Value = 7 });
        var json = JObject.Parse(new GraphSerializer(NodeRegistry.CreateDefault()).Serialize(graph));
        json["FutureFeature"] = "whatever";
        ((JObject)json["Nodes"]![0]!)["FutureNodeField"] = 123;

        var loaded = new GraphSerializer(NodeRegistry.CreateDefault()).Deserialize(json.ToString());

        var number = Assert.IsType<NumberInputNode>(Assert.Single(loaded.Nodes));
        Assert.Equal(7.0, number.Value);
    }

    [Fact]
    public void FutureMinReaderVersion_IsRejectedWithGraphFormatException()
    {
        var graph = new GraphModel();
        var json = JObject.Parse(new GraphSerializer(NodeRegistry.CreateDefault()).Serialize(graph));
        json["Dyncamelo"]!["FormatVersion"] = 99;
        json["Dyncamelo"]!["MinReaderVersion"] = 99;

        Assert.Throws<GraphFormatException>(
            () => new GraphSerializer(NodeRegistry.CreateDefault()).Deserialize(json.ToString()));
    }

    [Fact]
    public void NonJson_And_NonDycJson_AreRejected()
    {
        var serializer = new GraphSerializer(NodeRegistry.CreateDefault());
        Assert.Throws<GraphFormatException>(() => serializer.Deserialize("this is not json"));
        Assert.Throws<GraphFormatException>(() => serializer.Deserialize("{ \"foo\": 1 }"));
    }

    [Fact]
    public void SaveToFile_And_LoadFromFile_RoundTrip()
    {
        var graph = BuildSampleGraph();
        var serializer = new GraphSerializer(FullRegistry());
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dyc");
        try
        {
            serializer.SaveToFile(graph, path);
            var loaded = serializer.LoadFromFile(path);
            Assert.Equal(graph.Nodes.Count, loaded.Nodes.Count);
            Assert.Equal(graph.Connections.Count, loaded.Connections.Count);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
