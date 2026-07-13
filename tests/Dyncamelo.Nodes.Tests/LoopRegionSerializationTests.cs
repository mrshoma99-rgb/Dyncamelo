using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// A loop region built from the real library nodes must survive a .dyc round
/// trip and still run — the loop boundaries and their connections are ordinary
/// serialized nodes/connectors, with no special payload.
/// </summary>
public class LoopRegionSerializationTests
{
    [Fact]
    public void LoopRegion_RoundTripsAndRunsWithRealNodes()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var serializer = new GraphSerializer(registry);

        var graph = new GraphModel();
        var numbers = new[] { 4.0, 16.0, 25.0 };
        var list = new ListCreateNode();
        graph.AddNode(list);
        list.AddItemPort();
        list.AddItemPort(); // three item ports

        for (int i = 0; i < numbers.Length; i++)
        {
            var input = new NumberInputNode { Value = numbers[i] };
            graph.AddNode(input);
            Assert.True(graph.Connect(input.OutPorts[0], list.InPorts[i]).Success);
        }

        var loop = new LoopItemNode();
        var sqrt = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "Math.Sqrt"));
        var collect = new LoopCollectNode();
        graph.AddNode(loop);
        graph.AddNode(sqrt);
        graph.AddNode(collect);

        Assert.True(graph.Connect(list.OutPorts[0], loop.InPorts[0]).Success);    // list -> items
        Assert.True(graph.Connect(loop.OutPorts[0], sqrt.InPorts[0]).Success);    // item -> sqrt
        Assert.True(graph.Connect(loop.OutPorts[3], collect.InPorts[0]).Success); // loop handle
        Assert.True(graph.Connect(sqrt.OutPorts[0], collect.InPorts[1]).Success); // sqrt -> value

        var restored = serializer.Deserialize(serializer.Serialize(graph));

        Assert.Single(restored.Nodes.OfType<LoopItemNode>());
        Assert.Single(restored.Nodes.OfType<LoopCollectNode>());

        new GraphEngine().Run(restored);

        var restoredCollect = restored.Nodes.OfType<LoopCollectNode>().Single();
        var results = Assert.IsAssignableFrom<IEnumerable<object?>>(restoredCollect.OutPorts[0].Value);
        Assert.Equal(new object?[] { 2.0, 4.0, 5.0 }, results.ToArray());
    }
}
