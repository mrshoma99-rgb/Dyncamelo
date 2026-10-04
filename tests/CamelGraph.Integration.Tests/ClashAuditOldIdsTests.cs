using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using CamelGraph.Nodes;
using CamelGraph.TestSupport.StandIns;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// The clash audit changed the signature of some Navisworks clash nodes (a port took a wider type, an optional input was added), which
/// changes the definition id a saved graph refers to. Each earlier id is kept with <c>[NodeAliases]</c>; this loads a graph written with
/// the OLD id (through the Navisworks stand-ins, built from the source and the node catalogue) and checks that the node comes back as
/// the current node with the value that was typed into it.
/// </summary>
public class ClashAuditOldIdsTests
{
    private static readonly Lazy<NodeRegistry> Rig = new Lazy<NodeRegistry>(() =>
    {
        var registry = Pipeline.CreateRegistry();
        var standIns = StandInCatalogue.Create(NodeCatalogue.Load(), SourceScan.ReadNavisworks(), registry);
        standIns.RegisterInto(registry);
        return registry;
    });

    private const string Doc = "Autodesk.Navisworks.Api.Document";
    private const string Result = "Autodesk.Navisworks.Api.Clash.ClashResult";

    /// <summary>The id as it was shipped, the node's name now, a text input that a graph may have filled in, and its value.</summary>
    public static IEnumerable<object[]> MovedNodes()
    {
        yield return new object[] { "CamelGraph.Navisworks.ClashNodes.SetStatus@" + Result + ",string," + Doc, "ClashResult.SetStatus", "status", "Approved" };
        yield return new object[] { "CamelGraph.Navisworks.ClashNodes.Assign@" + Result + ",string," + Doc, "ClashResult.Assign", "assignedTo", "MEP" };
        yield return new object[] { "CamelGraph.Navisworks.ClashNodes.SetDescription@" + Result + ",string," + Doc, "ClashResult.SetDescription", "description", "Check with the structural engineer" };
    }

    [Theory]
    [MemberData(nameof(MovedNodes))]
    public void AGraphSavedWithTheEarlierIdStillLoadsAsTheCurrentNodeWithItsTypedValue(string oldId, string name, string port, string value)
    {
        var registry = Rig.Value;
        Assert.True(registry.TryGetDefinition(oldId, out var definition), "The earlier id '" + oldId + "' no longer resolves.");
        Assert.Equal(name, definition!.Name);
        Assert.NotEqual(oldId, definition.Id);

        // Write the graph with the current node, then change its id to the earlier one, the way an older version wrote it.
        var graph = new GraphModel();
        var node = Pipeline.ZeroTouch(registry, name);
        node.InPorts.Single(p => p.Name == port).SetUserValue(value);
        graph.AddNode(node);
        var serializer = new GraphSerializer(registry);
        var json = serializer.Serialize(graph).Replace(definition.Id, oldId);
        Assert.Contains(oldId, json);

        var loaded = serializer.Deserialize(json);

        Assert.Empty(serializer.LoadWarnings);
        var back = Assert.Single(loaded.Nodes);
        Assert.IsNotType<MissingNodeModel>(back);
        Assert.Equal(definition.Id, Assert.IsType<ZeroTouchNodeModel>(back).Definition.Id);
        Assert.Equal(value, back.InPorts.Single(p => p.Name == port).UserValue);
    }
}
