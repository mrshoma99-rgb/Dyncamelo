using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Serialization;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>Nodes whose ports were renamed or that were retired, as a node author would write them.</summary>
public static class EvolvedFixtures
{
    [NodeDeprecated("Evolved.Area")]
    public static double OldArea(double width, double depth) => width * depth;

    [NodeName("Evolved.Area")]
    [PortAlias("x", "width")]
    [PortAlias("y", "depth")]
    [PortAlias("result", "area")]
    [return: NodeName("area")]
    public static double Area(double width, double depth) => width * depth;

    [NodeName("Evolved.Both")]
    [PortAlias("add", "sum")]
    [MultiReturn("sum", "product")]
    public static Dictionary<string, object> Both(double a, double b) =>
        new Dictionary<string, object> { ["sum"] = a + b, ["product"] = a * b };

    [NodeName("Evolved.Sink")]
    public static double Sink(double value) => value;
}

public class NodeEvolutionTests
{
    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterAssembly(typeof(EvolvedFixtures).Assembly);
        return registry;
    }

    private static NodeDefinition Definition(NodeRegistry registry, string name) =>
        registry.Definitions.Single(d => d.Name == name);

    private static ZeroTouchNodeModel Create(NodeRegistry registry, string name) =>
        registry.CreateZeroTouchNode(Definition(registry, name).Id)!;

    [Fact]
    public void ARetiredNodeStaysRegisteredAndSaysWhatToUseInstead()
    {
        var registry = Registry();
        var old = registry.Definitions.Single(d => d.Method.Name == nameof(EvolvedFixtures.OldArea));

        Assert.True(old.IsDeprecated);
        Assert.Equal("Evolved.Area", old.Replacement);
        Assert.StartsWith("Retired — use Evolved.Area instead", old.Description);
        Assert.False(Definition(registry, "Evolved.Area").IsDeprecated);

        // It still loads and runs.
        var node = registry.CreateZeroTouchNode(old.Id)!;
        node.InPorts[0].SetUserValue(3d);
        node.InPorts[1].SetUserValue(4d);
        var graph = new GraphModel();
        graph.AddNode(node);
        new Dyncamelo.Core.Execution.GraphEngine().Run(graph, new EvaluationContext());
        Assert.Equal(12d, node.OutPorts[0].Value);
    }

    [Fact]
    public void ARenamedInputKeepsItsWireAndItsValueThroughTheAlias()
    {
        var registry = Registry();
        var graph = new GraphModel();
        var feeder = Create(registry, "Evolved.Sink");
        var area = Create(registry, "Evolved.Area");
        graph.AddNode(feeder);
        graph.AddNode(area);
        Assert.True(graph.Connect(feeder.OutPorts[0], area.InPorts[0]).Success);
        area.InPorts[1].SetUserValue(5d);
        var serializer = new GraphSerializer(registry);

        // The file was written when the ports were still called x and y.
        var json = serializer.Serialize(graph).Replace("\"width\"", "\"x\"").Replace("\"depth\"", "\"y\"");
        var loaded = serializer.Deserialize(json);

        var loadedArea = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == "Evolved.Area");
        Assert.NotNull(loaded.FindConnectionInto(loadedArea.InPorts[0]));
        Assert.Equal(5d, loadedArea.InPorts[1].UserValue);
        Assert.Empty(serializer.LoadWarnings);
    }

    [Fact]
    public void ARenamedOutputKeepsItsWireThroughTheAlias()
    {
        var registry = Registry();
        var graph = new GraphModel();
        var both = Create(registry, "Evolved.Both");
        var sink = Create(registry, "Evolved.Sink");
        graph.AddNode(both);
        graph.AddNode(sink);
        Assert.True(graph.Connect(both.OutPorts[0], sink.InPorts[0]).Success);
        var serializer = new GraphSerializer(registry);

        var json = serializer.Serialize(graph).Replace("\"FromPort\": \"sum\"", "\"FromPort\": \"add\"");
        Assert.Contains("\"FromPort\": \"add\"", json);
        var loaded = serializer.Deserialize(json);

        var loadedSink = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == "Evolved.Sink");
        var wire = loaded.FindConnectionInto(loadedSink.InPorts[0]);
        Assert.NotNull(wire);
        Assert.Equal("sum", wire!.Source.Name);
        Assert.Empty(serializer.LoadWarnings);
    }

    [Fact]
    public void AWireOrValueForAPortThatIsGoneIsReportedNotSilentlyDropped()
    {
        var registry = Registry();
        var graph = new GraphModel();
        var feeder = Create(registry, "Evolved.Sink");
        var area = Create(registry, "Evolved.Area");
        graph.AddNode(feeder);
        graph.AddNode(area);
        Assert.True(graph.Connect(feeder.OutPorts[0], area.InPorts[0]).Success);
        area.InPorts[1].SetUserValue(5d);
        var serializer = new GraphSerializer(registry);

        var json = serializer.Serialize(graph).Replace("\"width\"", "\"gone1\"").Replace("\"depth\"", "\"gone2\"");
        var loaded = serializer.Deserialize(json);

        Assert.Null(loaded.FindConnectionInto(loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == "Evolved.Area").InPorts[0]));
        Assert.Equal(2, serializer.LoadWarnings.Count);
        Assert.Contains(serializer.LoadWarnings, w => w.Contains("Evolved.Area") && w.Contains("gone1") && w.Contains("connection"));
        Assert.Contains(serializer.LoadWarnings, w => w.Contains("gone2") && w.Contains("value"));
    }

    [Fact]
    public void ACleanLoadHasNoWarningsAndTheListIsClearedForTheNextOne()
    {
        var registry = Registry();
        var graph = new GraphModel();
        var feeder = Create(registry, "Evolved.Sink");
        var area = Create(registry, "Evolved.Area");
        graph.AddNode(feeder);
        graph.AddNode(area);
        Assert.True(graph.Connect(feeder.OutPorts[0], area.InPorts[0]).Success);
        var serializer = new GraphSerializer(registry);

        serializer.Deserialize(serializer.Serialize(graph).Replace("\"width\"", "\"gone\""));
        Assert.NotEmpty(serializer.LoadWarnings);

        serializer.Deserialize(serializer.Serialize(graph));
        Assert.Empty(serializer.LoadWarnings);
    }

    [Fact]
    public void AnExactNameWinsOverAnAliasOfAnotherPort()
    {
        var registry = Registry();
        var area = Create(registry, "Evolved.Area");

        Assert.Same(area.InPorts[0], area.FindInPort("width"));
        Assert.Same(area.InPorts[0], area.FindInPort("x"));
        Assert.Same(area.InPorts[1], area.FindInPort("depth"));
        Assert.Null(area.FindInPort("nothing"));
        Assert.Null(area.FindInPort(null));
        Assert.Same(area.OutPorts[0], area.FindOutPort("result"));
    }
}
