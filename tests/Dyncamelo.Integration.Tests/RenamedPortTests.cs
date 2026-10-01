using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// The String nodes' text input used to be called "str". A graph saved before the rename must still come back wired — and run —
/// with the real library, not just with the test fixtures.
/// </summary>
public class RenamedPortTests
{
    [Fact]
    public void AGraphSavedWithTheOldStrInputStillLoadsWiredAndRuns()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel();
        var text = new StringInputNode { Name = "Text", Value = "a b c" };
        var sep = new StringInputNode { Name = "Sep", Value = " " };
        var split = Pipeline.ZeroTouch(registry, "String.Split", "Split");
        var count = Pipeline.ZeroTouch(registry, "List.Count", "Count");
        graph.AddNode(text);
        graph.AddNode(sep);
        graph.AddNode(split);
        graph.AddNode(count);
        Pipeline.Connect(graph, text, "value", split, "text");
        Pipeline.Connect(graph, sep, "value", split, "separator");
        Pipeline.Connect(graph, split, "list", count, "list");
        var serializer = new GraphSerializer(registry);

        // The file as an older version wrote it: the Split node's first input is called "str".
        var json = serializer.Serialize(graph).Replace("\"text\"", "\"str\"");
        Assert.Contains("\"ToPort\": \"str\"", json);
        var loaded = serializer.Deserialize(json);

        Assert.Empty(serializer.LoadWarnings);
        var run = new GraphEngine().Run(loaded);
        var loadedCount = loaded.Nodes.Single(n => n.Name == "Count");
        Assert.Equal(3, System.Convert.ToInt32(loadedCount.OutPorts[0].Value));
        Assert.True(run.ExecutedNodes.Count > 0);
    }
}
