using System;
using System.IO;
using System.Linq;
using System.Threading;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Player;
using CamelGraph.Core.Serialization;
using CamelGraph.Core.Tests.Fixtures;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>A node that changes the model (so a script holding it needs confirming).</summary>
/// <summary>Reads only (Info), but reaches out over the network: the Player must still ask about it.</summary>
public sealed class EffectsTestNode : NodeModel
{
    public EffectsTestNode()
    {
        Name = "Fetch Page";
        AddInput("in", typeof(object), (object?)null);
        AddOutput("out", typeof(object));
    }

    public override string NodeType => "TestEffects";

    public override NodeFunction Function => NodeFunction.Info;

    public override NodeEffects Effects => NodeEffects.UsesNetwork;

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new[] { inputs[0] };
}

public sealed class ModifyingTestNode : NodeModel
{
    public ModifyingTestNode()
    {
        Name = "Do Something";
        AddInput("in", typeof(object), (object?)null);
        AddOutput("out", typeof(object));
    }

    public override string NodeType => "TestModifying";

    public override NodeFunction Function => NodeFunction.Modify;

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new[] { inputs[0] };
}

// Running a script sets the process-wide graph folder for the run, as GraphContextTests do: they must not overlap.
[Xunit.Collection("GraphContext")]
public sealed class PlayerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dyc-player-" + Guid.NewGuid().ToString("N"));

    public PlayerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
    }

    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestModifying", () => new ModifyingTestNode());
        registry.RegisterNodeType("TestEffects", () => new EffectsTestNode());
        registry.RegisterAssembly(typeof(MathFixtures).Assembly);
        return registry;
    }

    private string Save(GraphModel graph, string name = "script.dyc")
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        new GraphSerializer(Registry()).SaveToFile(graph, path);
        return path;
    }

    private static ScriptSession Load(string path) => ScriptSession.Load(path, Registry());

    // number -> square root -> watch, plus a string and a toggle that nothing uses
    private static GraphModel Sample(out NumberInputNode number, out WatchNode watch)
    {
        var graph = new GraphModel { Name = "Root finder", Description = "Takes the square root." };
        number = new NumberInputNode { Name = "Number", Value = 16, X = 0, Y = 0 };
        var root = ZT.Node("Sqrt");
        root.X = 200;
        watch = new WatchNode { Name = "Result", X = 400 };
        graph.AddNode(number);
        graph.AddNode(root);
        graph.AddNode(watch);
        ZT.Wire(graph, number, 0, root, 0);
        ZT.Wire(graph, root, 0, watch, 0);
        return graph;
    }

    // ----- the flags ---------------------------------------------------------------------------

    [Fact]
    public void TheExposureFlagsSurviveSaveAndLoad()
    {
        var graph = new GraphModel();
        var node = ZT.Node("AddStep");
        graph.AddNode(node);
        node.InPorts[1].PlayerExposed = true;
        node.PlayerExposed = true;
        var hidden = new NumberInputNode();
        graph.AddNode(hidden);
        hidden.PlayerExposed = false;
        var plain = new StringInputNode();
        graph.AddNode(plain);
        var serializer = new GraphSerializer(Registry());

        var loaded = serializer.Deserialize(serializer.Serialize(graph));

        var loadedNode = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single();
        Assert.True(loadedNode.PlayerExposed);
        Assert.True(loadedNode.InPorts[1].PlayerExposed);
        Assert.False(loadedNode.InPorts[0].PlayerExposed);
        Assert.False(loaded.Nodes.OfType<NumberInputNode>().Single().PlayerExposed);
        Assert.Null(loaded.Nodes.OfType<StringInputNode>().Single().PlayerExposed);
    }

    [Fact]
    public void AGraphWithNoFlagsWritesNone()
    {
        var graph = new GraphModel();
        graph.AddNode(new NumberInputNode());

        Assert.DoesNotContain("Player", new GraphSerializer(Registry()).Serialize(graph));
    }

    [Fact]
    public void ExposingAnInputOrANodeCanBeUndone()
    {
        var graph = new GraphModel();
        var node = ZT.Node("AddStep");
        graph.AddNode(node);
        var undo = new UndoManager { CoalesceWindow = TimeSpan.Zero };
        using var recorder = new GraphRecorder(graph, undo);

        node.InPorts[1].PlayerExposed = true;
        node.PlayerExposed = true;

        undo.Undo();
        Assert.Null(node.PlayerExposed);
        Assert.True(node.InPorts[1].PlayerExposed);
        undo.Undo();
        Assert.False(node.InPorts[1].PlayerExposed);
        undo.Redo();
        Assert.True(node.InPorts[1].PlayerExposed);
    }

    // ----- choosing what the Player shows ------------------------------------------------------

    [Fact]
    public void InputAndWatchNodesAreShownByDefaultAndOtherNodesAreNot()
    {
        var plain = ZT.Node("AddStep");

        Assert.True(PlayerExposure.IsShown(new NumberInputNode()));
        Assert.True(PlayerExposure.IsShown(new WatchNode()));
        Assert.False(PlayerExposure.IsShown(plain));
        Assert.True(PlayerExposure.IsShownByDefault(new BooleanToggleNode()));
        Assert.False(PlayerExposure.IsShownByDefault(plain));
    }

    [Fact]
    public void HidingAnInputNodeStoresTheChoiceAndShowingItAgainRemovesIt()
    {
        var node = new NumberInputNode();

        PlayerExposure.SetShown(node, false);
        Assert.False(node.PlayerExposed);
        Assert.False(PlayerExposure.IsShown(node));

        PlayerExposure.SetShown(node, true);
        Assert.Null(node.PlayerExposed);
        Assert.True(PlayerExposure.IsShown(node));
    }

    [Fact]
    public void ShowingAnOrdinaryNodeStoresTheChoiceAndHidingItAgainRemovesIt()
    {
        var node = ZT.Node("AddStep");

        PlayerExposure.SetShown(node, true);
        Assert.True(node.PlayerExposed);
        Assert.True(PlayerExposure.IsShown(node));
        Assert.True(PlayerExposure.HasExplicitChoice(node));

        PlayerExposure.SetShown(node, false);
        Assert.Null(node.PlayerExposed);
        Assert.False(PlayerExposure.IsShown(node));
        Assert.False(PlayerExposure.HasExplicitChoice(node));
    }

    [Fact]
    public void OnlyUnwiredInputsWithAnEditorCanBeOffered()
    {
        var graph = new GraphModel();
        var seed = new NumberInputNode { Value = 3 };
        var add = ZT.Node("AddStep");
        graph.AddNode(seed);
        graph.AddNode(add);
        ZT.Wire(graph, seed, 0, add, 0);

        var offerable = PlayerExposure.OfferableInputs(add, p => graph.FindConnectionInto(p) != null);

        var port = Assert.Single(offerable);
        Assert.Equal("step", port.Name);
        Assert.True(PlayerExposure.CanOffer(port));
        add.InPorts[1].PlayerExposed = true;
        Assert.True(PlayerExposure.HasExplicitChoice(add));
    }

    [Fact]
    public void SetShownRejectsAMissingNode()
    {
        Assert.Throws<ArgumentNullException>(() => PlayerExposure.SetShown(null!, true));
    }

    // ----- the catalogue -----------------------------------------------------------------------

    [Fact]
    public void ScanFindsScriptsInFoldersAndSubfoldersAndSkipsWhatItShould()
    {
        File.WriteAllText(Path.Combine(_folder, "b script.dyc"), "{}");
        File.WriteAllText(Path.Combine(_folder, "A script.dyc"), "{}");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_folder, "~autosave.dyc"), "{}");
        Directory.CreateDirectory(Path.Combine(_folder, "Clash", "Weekly"));
        File.WriteAllText(Path.Combine(_folder, "Clash", "Weekly", "report.dyc"), "{}");
        Directory.CreateDirectory(Path.Combine(_folder, ".hidden"));
        File.WriteAllText(Path.Combine(_folder, ".hidden", "secret.dyc"), "{}");

        var entries = ScriptCatalog.Scan(new[] { _folder, Path.Combine(_folder, "missing"), "  ", _folder });

        var root = Path.GetFileName(_folder);
        Assert.Equal(
            new[] { root + "|A script", root + "|b script", root + " ▸ Clash ▸ Weekly|report" },
            entries.Select(e => e.Folder + "|" + e.Name).ToArray());
        Assert.All(entries, e => Assert.True(File.Exists(e.Path)));
    }

    [Fact]
    public void ScanStopsAtTheDepthLimit()
    {
        var deep = Path.Combine(_folder, "a", "b", "c", "d", "e");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "too deep.dyc"), "{}");
        File.WriteAllText(Path.Combine(_folder, "a", "b", "c", "d", "ok.dyc"), "{}");

        var names = ScriptCatalog.Scan(new[] { _folder }).Select(e => e.Name).ToList();

        Assert.Contains("ok", names);
        Assert.DoesNotContain("too deep", names);
    }

    // ----- the form and the results ------------------------------------------------------------

    [Fact]
    public void AScriptsInputNodesBecomeTheFormAndItsWatchNodeTheResult()
    {
        var graph = Sample(out _, out _);
        var path = Save(graph);

        var session = Load(path);

        Assert.Equal("Root finder", session.Name);
        Assert.Equal("Takes the square root.", session.Description);
        var field = Assert.Single(session.Fields);
        Assert.Equal("Number", field.Label);
        Assert.Equal(PortEditorKind.Number, PortEditors.Resolve(field.Port));
        Assert.Equal(16d, PortEditors.GetNumber(field.Port));
        Assert.False(field.IsChanged);
        Assert.Equal("Result", Assert.Single(session.OutputNodes).Name);
        Assert.Equal(64, session.Hash.Length);
        Assert.Equal(3, session.NodeCount);
        Assert.Empty(session.ModifyingNodes);
    }

    [Fact]
    public void EditingAFieldChangesTheNodeAndRunningGivesTheResult()
    {
        var path = Save(Sample(out _, out _));
        var session = Load(path);

        PortEditors.SetNumber(session.Fields[0].Port, 81);
        var result = session.Run(new EvaluationContext());

        Assert.True(session.Fields[0].IsChanged);
        Assert.Equal(81d, ((NumberInputNode)session.Fields[0].Node).Value);
        var output = Assert.Single(result.Outputs);
        Assert.Equal("Result", output.Label);
        Assert.Equal("9", output.Text);
        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Executed);
        Assert.StartsWith("Finished in", result.Summary);
        Assert.Contains("3 nodes", result.Summary);
    }

    [Fact]
    public void EveryRunRunsEveryNodeAgainBecauseTheModelMayHaveChanged()
    {
        var session = Load(Save(Sample(out _, out _)));

        Assert.Equal(3, session.Run(new EvaluationContext()).Executed);
        Assert.Equal(3, session.Run(new EvaluationContext()).Executed);
    }

    [Fact]
    public void ResettingBringsBackTheValuesSavedInTheScript()
    {
        var session = Load(Save(Sample(out _, out _)));
        PortEditors.SetNumber(session.Fields[0].Port, 25);
        Assert.Equal(25d, ((NumberInputNode)session.Fields[0].Node).Value);

        session.ResetValues();

        Assert.Equal(16d, ((NumberInputNode)session.Fields[0].Node).Value);
        Assert.False(session.Fields[0].IsChanged);
    }

    [Fact]
    public void ChangedValuesAreCapturedAndRestoredIntoAFreshSession()
    {
        var path = Save(Sample(out _, out _));
        var first = Load(path);
        PortEditors.SetNumber(first.Fields[0].Port, 49);
        var saved = first.CaptureValues();

        var second = Load(path);
        second.RestoreValues(saved);

        Assert.Equal(49d, ((NumberInputNode)second.Fields[0].Node).Value);
        Assert.Equal("7", second.Run(new EvaluationContext()).Outputs[0].Text);
        Assert.Empty(Load(path).CaptureValues());
    }

    [Fact]
    public void ARestoredValueThatNoLongerFitsIsIgnored()
    {
        var session = Load(Save(Sample(out _, out _)));
        var key = session.Fields[0].Key;

        session.RestoreValues(new JObject { [key] = "not a number", ["gone"] = 5 });

        Assert.Equal(16d, ((NumberInputNode)session.Fields[0].Node).Value);
    }

    [Fact]
    public void SlidersTogglesTextAndPathsAllGetTheirEditors()
    {
        var graph = new GraphModel();
        graph.AddNode(new NumberSliderNode { Name = "Gap", Min = 0, Max = 10, Step = 0.5, Value = 2, Y = 0 });
        graph.AddNode(new IntegerSliderNode { Name = "Count", Min = 1, Max = 9, Value = 3, Y = 100 });
        graph.AddNode(new BooleanToggleNode { Name = "Strict", Value = true, Y = 200 });
        graph.AddNode(new StringInputNode { Name = "Test name", Value = "Floors", Y = 300 });
        graph.AddNode(new FilePathNode { Name = "Report", Path = "C:\\r.html", Y = 400 });
        graph.AddNode(new DirectoryPathNode { Name = "Folder", Path = "C:\\out", Y = 500 });

        var session = Load(Save(graph));

        Assert.Equal(
            new[] { "Gap", "Count", "Strict", "Test name", "Report", "Folder" },
            session.Fields.Select(f => f.Label).ToArray());
        Assert.Equal(
            new[] { PortEditorKind.Number, PortEditorKind.Number, PortEditorKind.Toggle, PortEditorKind.Text, PortEditorKind.Path, PortEditorKind.Path },
            session.Fields.Select(f => PortEditors.Resolve(f.Port)).ToArray());
        var gap = NumberEditSpec.FromPort(session.Fields[0].Port);
        Assert.Equal(0d, gap.Min);
        Assert.Equal(10d, gap.Max);
        Assert.Equal(0.5d, gap.Step);
        Assert.True(NumberEditSpec.FromPort(session.Fields[1].Port).IsInteger);

        PortEditors.SetNumber(session.Fields[1].Port, 7);
        PortEditors.SetBool(session.Fields[2].Port, false);
        PortEditors.SetText(session.Fields[3].Port, "Doors");
        PortEditors.SetText(session.Fields[4].Port, "D:\\x.html");
        Assert.Equal(7L, ((IntegerSliderNode)session.Fields[1].Node).Value);
        Assert.False(((BooleanToggleNode)session.Fields[2].Node).Value);
        Assert.Equal("Doors", ((StringInputNode)session.Fields[3].Node).Value);
        Assert.Equal("D:\\x.html", ((FilePathNode)session.Fields[4].Node).Path);
    }

    [Fact]
    public void AnInputNodeCanBeHiddenFromTheForm()
    {
        var graph = Sample(out var number, out _);
        number.PlayerExposed = false;

        var session = Load(Save(graph));

        Assert.Empty(session.Fields);
    }

    [Fact]
    public void AnExposedUnwiredInputJoinsTheFormButAWiredOneDoesNot()
    {
        var graph = new GraphModel();
        var seed = new NumberInputNode { Value = 3, PlayerExposed = false };
        var add = ZT.Node("AddStep");
        graph.AddNode(seed);
        graph.AddNode(add);
        ZT.Wire(graph, seed, 0, add, 0);
        add.InPorts[0].PlayerExposed = true;   // wired: ignored
        add.InPorts[1].PlayerExposed = true;   // "step": unwired

        var session = Load(Save(graph));

        var field = Assert.Single(session.Fields);
        Assert.Equal("step", field.Label);
        Assert.Equal(add.Name, field.Detail);
        PortEditors.SetNumber(field.Port, 10);
        session.Run(new EvaluationContext());
        Assert.Equal(13d, session.Graph.Nodes.OfType<ZeroTouchNodeModel>().Single().OutPorts[0].Value);
        Assert.Contains(":step", session.CaptureValues().Properties().Single().Name);
    }

    [Fact]
    public void AnyNodeCanBeMarkedToShowItsResultAndAWatchNodeCanBeHidden()
    {
        var graph = Sample(out _, out var watch);
        var root = graph.Nodes.OfType<ZeroTouchNodeModel>().Single();
        root.PlayerExposed = true;
        watch.PlayerExposed = false;

        var session = Load(Save(graph));
        var result = session.Run(new EvaluationContext());

        var output = Assert.Single(result.Outputs);
        Assert.Equal(root.Name, output.Label);
        Assert.Equal("4", output.Text);
    }

    [Fact]
    public void LongResultsAreCutShort()
    {
        var formatted = string.Join("\n", Enumerable.Range(0, 1000).Select(i => "line " + i));
        var registry = Registry();
        registry.RegisterNodeType("TestFixedOutput", () => new FixedOutputNode(formatted));
        var graph = new GraphModel();
        graph.AddNode(new FixedOutputNode(formatted));
        var path = Path.Combine(_folder, "long.dyc");
        new GraphSerializer(registry).SaveToFile(graph, path);

        var text = ScriptSession.Load(path, registry).Run(new EvaluationContext()).Outputs[0].Text;

        Assert.EndsWith("… 700 more lines", text);
        Assert.Equal(ScriptSession.MaxOutputLines + 1, text.Split('\n').Length);
    }

    // ----- safety and problems -----------------------------------------------------------------

    [Fact]
    public void ScriptsThatChangeTheModelNameTheNodesThatDo()
    {
        var graph = new GraphModel();
        var doSomething = new ModifyingTestNode { Name = "Isolate Walls" };
        var muted = new ModifyingTestNode { Name = "Muted change", IsMuted = true };
        graph.AddNode(doSomething);
        graph.AddNode(muted);
        graph.AddNode(new ModifyingTestNode { Name = "Isolate Walls" });

        var session = Load(Save(graph));

        Assert.Equal(new[] { "Isolate Walls" }, session.ModifyingNodes.ToArray());
    }

    [Fact]
    public void AScriptThatOnlyReadsButUsesTheNetworkOrRunsProgramsIsAskedAboutToo()
    {
        var graph = new GraphModel();
        graph.AddNode(new EffectsTestNode { Name = "Fetch Page" });
        graph.AddNode(new EffectsTestNode { Name = "Muted fetch", IsMuted = true });

        var session = Load(Save(graph));

        Assert.Equal(new[] { "Fetch Page" }, session.ModifyingNodes.ToArray());
    }

    [Fact]
    public void AScriptThatWritesFilesOrChangesTheModelSaysSoInTheConsentTexts()
    {
        var definitions = AssemblyNodeLoader.LoadType(typeof(EffectFixtures));
        var graph = new GraphModel();
        graph.AddNode(new ZeroTouchNodeModel(definitions.Single(d => d.Method.Name == "WriteFile")));
        graph.AddNode(new ZeroTouchNodeModel(definitions.Single(d => d.Method.Name == "ChangeModel")));
        graph.AddNode(new ZeroTouchNodeModel(definitions.Single(d => d.Method.Name == "Harmless")));

        var session = Load(Save(graph));

        Assert.Equal(2, session.ModifyingNodes.Count);
        Assert.Contains(session.ModifyingNodes, n => n.Contains("WriteFile"));
        Assert.Contains(session.ModifyingNodes, n => n.Contains("ChangeModel"));
        Assert.Contains(session.EffectLines, l => l.StartsWith("writes files: ", StringComparison.Ordinal) && l.Contains("WriteFile"));
        Assert.Contains(session.EffectLines, l => l.StartsWith("changes the model: ", StringComparison.Ordinal) && l.Contains("ChangeModel"));
        Assert.Contains("Writes files: ", session.EffectSummary);
        Assert.Contains("changes the model: ", session.EffectSummary);
    }

    [Fact]
    public void AScriptThatOnlyReadsHasNoEffectLines()
    {
        var session = Load(Save(Sample(out _, out _)));

        Assert.Empty(session.EffectLines);
        Assert.Equal(string.Empty, session.EffectSummary);
    }

    [Fact]
    public void TheHashChangesWhenTheFileDoes()
    {
        var graph = Sample(out var number, out _);
        var path = Save(graph);
        var before = Load(path).Hash;
        Assert.Equal(before, Load(path).Hash);

        number.Value = 17;
        Save(graph);

        Assert.NotEqual(before, Load(path).Hash);
    }

    [Fact]
    public void ANodeTypeThatIsNotInstalledIsCounted()
    {
        var path = Save(Sample(out _, out _));
        var json = File.ReadAllText(path).Replace("\"NodeType\": \"Watch\"", "\"NodeType\": \"SomeMissingType\"");
        File.WriteAllText(path, json);

        Assert.Equal(1, Load(path).MissingNodes);
    }

    [Fact]
    public void FailuresAreReportedWithTheirNodeAndTheSummaryCountsThem()
    {
        var graph = new GraphModel();
        var seed = new NumberInputNode { PlayerExposed = false, Value = 4 };
        var fail = ZT.Node("Fail");
        var after = ZT.Node("AddStep");
        graph.AddNode(seed);
        graph.AddNode(fail);
        graph.AddNode(after);
        ZT.Wire(graph, seed, 0, fail, 0);
        ZT.Wire(graph, fail, 0, after, 0);
        var session = Load(Save(graph));

        var result = session.Run(new EvaluationContext());

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Contains("1 error", result.Summary);
        Assert.Contains("1 warning", result.Summary);
        Assert.StartsWith("Finished with errors", result.Summary);
        var report = result.ToReport(session.Name);
        Assert.Contains("Error in " + fail.Name + ": boom", report);
    }

    [Fact]
    public void ACancelledRunSaysSoAndKeepsWhatItDid()
    {
        var session = Load(Save(Sample(out _, out _)));
        using var source = new CancellationTokenSource();
        var context = new EvaluationContext();
        context.UseCancellation(source.Token);
        var seen = 0;
        context.Heartbeat = () =>
        {
            if (++seen == 2)
            {
                source.Cancel();
            }
        };

        var result = session.Run(context);

        Assert.True(result.Cancelled);
        Assert.False(result.Succeeded);
        Assert.StartsWith("Stopped after", result.Summary);
    }

    [Fact]
    public void ALoadOfSomethingThatIsNotAGraphFails()
    {
        var path = Path.Combine(_folder, "broken.dyc");
        File.WriteAllText(path, "{ not json");

        Assert.Throws<GraphFormatException>(() => Load(path));
    }
}

/// <summary>A node that outputs one fixed text (to test the result limit).</summary>
public sealed class FixedOutputNode : NodeModel, IPlayerOutputNode
{
    private readonly string _text;

    public FixedOutputNode(string text)
    {
        _text = text;
        Name = "Fixed";
        AddOutput("out", typeof(object));
    }

    public override string NodeType => "TestFixedOutput";

    public string PlayerText => _text;

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { _text };
}
