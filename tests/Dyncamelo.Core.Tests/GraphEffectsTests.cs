using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Groups;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>Nodes of this fixture class declare what they can do outside the model and the graph.</summary>
public static class EffectFixtures
{
    [NodeEffects(NodeEffects.RunsPrograms)]
    public static double Launch(double a) => a;

    [NodeEffects(NodeEffects.UsesNetwork | NodeEffects.ChangesFiles)]
    public static double Sync(double a) => a;

    public static double Harmless(double a) => a;
}

/// <summary>What a graph from a file can do on this computer: found by the node attributes, listed for the warning.</summary>
public class GraphEffectsTests
{
    private static readonly System.Collections.Generic.List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(EffectFixtures));

    private static ZeroTouchNodeModel Node(string method) => new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));

    [Fact]
    public void TheLoaderReadsTheAttributeAndDefaultsToNone()
    {
        Assert.Equal(NodeEffects.RunsPrograms, Definitions.Single(d => d.Method.Name == "Launch").Effects);
        Assert.Equal(NodeEffects.UsesNetwork | NodeEffects.ChangesFiles, Definitions.Single(d => d.Method.Name == "Sync").Effects);
        Assert.Equal(NodeEffects.None, Definitions.Single(d => d.Method.Name == "Harmless").Effects);
        Assert.Equal(NodeEffects.RunsPrograms, Node("Launch").Effects);
    }

    [Fact]
    public void AGraphOfHarmlessNodesHasNoFindings()
    {
        var graph = new GraphModel();
        graph.AddNode(Node("Harmless"));
        graph.AddNode(new ValueNode { Value = 1.0 });

        Assert.Empty(GraphEffects.Survey(graph));
    }

    [Fact]
    public void EachEffectOfEachNodeIsAFinding()
    {
        var graph = new GraphModel();
        graph.AddNode(Node("Launch"));
        graph.AddNode(Node("Sync"));
        graph.AddNode(Node("Harmless"));

        var findings = GraphEffects.Survey(graph);

        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, f => f.Effect == NodeEffects.RunsPrograms);
        Assert.Contains(findings, f => f.Effect == NodeEffects.UsesNetwork);
        Assert.Contains(findings, f => f.Effect == NodeEffects.ChangesFiles);
    }

    [Fact]
    public void MutedAndFrozenNodesDoNotRunSoTheyAreNotReported()
    {
        var graph = new GraphModel();
        var muted = Node("Launch");
        muted.IsMuted = true;
        var frozen = Node("Sync");
        frozen.IsFrozen = true;
        graph.AddNode(muted);
        graph.AddNode(frozen);

        Assert.Empty(GraphEffects.Survey(graph));
    }

    [Fact]
    public void ANodeInsideAGroupIsFoundThroughTheInstance()
    {
        var graph = new GraphModel();
        var group = graph.NodeGroups.Create("Hidden");
        group.Graph.AddNode(Node("Launch"));
        graph.AddNode(new GroupInstanceNode(group));
        graph.AddNode(new GroupInstanceNode(group));

        var findings = GraphEffects.Survey(graph);

        Assert.Single(findings);
        Assert.Equal(NodeEffects.RunsPrograms, findings[0].Effect);
    }

    [Fact]
    public void TheSameNodeTwiceIsListedOnce()
    {
        var graph = new GraphModel();
        graph.AddNode(Node("Launch"));
        graph.AddNode(Node("Launch"));

        Assert.Single(GraphEffects.Survey(graph));
    }

    [Fact]
    public void TheWarningNamesWhatEachNodeDoes()
    {
        var graph = new GraphModel();
        graph.AddNode(Node("Launch"));
        graph.AddNode(Node("Sync"));

        var lines = GraphEffects.Describe(GraphEffects.Survey(graph));

        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, l => l.StartsWith("runs programs: ", System.StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("uses the network: ", System.StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("deletes, moves or overwrites files: ", System.StringComparison.Ordinal));
    }

    [Fact]
    public void AFileHashIsTheSha256InHexAndChangesWithTheContent()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FileFingerprint.Of(System.Text.Encoding.ASCII.GetBytes("abc")));
        Assert.NotEqual(FileFingerprint.Of(new byte[] { 1, 2, 3 }), FileFingerprint.Of(new byte[] { 1, 2, 4 }));
    }
}
