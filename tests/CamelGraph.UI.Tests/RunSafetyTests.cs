using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>A node that says it starts a program. It does nothing: it only counts how often it ran.</summary>
public sealed class RiskyNode : NodeModel
{
    public static int Runs;

    public RiskyNode()
    {
        Name = "Launch Program";
        Category = "Test";
        AddOutput("result", typeof(double));
    }

    public override string NodeType => "TestRisky";

    public override NodeEffects Effects => NodeEffects.RunsPrograms;

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        Interlocked.Increment(ref Runs);
        return new object?[] { 1d };
    }
}

internal sealed class RecordingDialogs : IDialogService
{
    public readonly List<string> Confirmations = new List<string>();
    public readonly List<string> Errors = new List<string>();
    public bool Answer = true;
    public string? SavePath;

    public string? ShowOpenFile(string filter, string title) => null;

    public string? ShowSaveFile(string filter, string title, string defaultFileName) => SavePath;

    public bool Confirm(string message, string title)
    {
        Confirmations.Add(message);
        return Answer;
    }

    public SaveChoice AskSaveChanges(string message, string title) => SaveChoice.DontSave;

    public void ShowError(string message, string title)
    {
        Errors.Add(message);
    }

    public string? Prompt(string message, string title, string defaultValue) => null;

    public string? PickFolder(string title, string initialFolder) => null;
}

/// <summary>
/// A graph file can come from anybody. One that holds a node which starts a program is not run until the user has been told, once per
/// file; graphs made in the editor never ask, and neither does the auto-run that fires when a file opens.
/// </summary>
public class RunSafetyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dyc-runsafety-" + Guid.NewGuid().ToString("N"));
    private readonly string _settingsPath;

    public RunSafetyTests()
    {
        Directory.CreateDirectory(_folder);
        _settingsPath = Path.Combine(_folder, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
            // Left behind in the temp folder.
        }
    }

    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestRisky", () => new RiskyNode());
        return registry;
    }

    // A graph file holding one risky node (muted if asked), made the way the editor saves it.
    private string WriteGraph(string name, bool muted = false, bool automatic = false)
    {
        var graph = new GraphModel { Name = name, RunType = automatic ? RunType.Automatic : RunType.Manual };
        graph.AddNode(new RiskyNode { IsMuted = muted });
        var path = Path.Combine(_folder, name + ".dyc");
        new GraphSerializer(Registry()).SaveToFile(graph, path);
        return path;
    }

    private GraphEditorViewModel NewEditor(RecordingDialogs dialogs)
    {
        GraphEditorViewModel? vm = null;
        StaHost.Run(() =>
        {
            vm = new GraphEditorViewModel(Registry(), dialogs, new UiSettingsService(_settingsPath));
            vm.IsAutoRun = false;   // a new graph runs by itself otherwise, which would make the counts below depend on timing
        });
        return vm!;
    }

    private static int RunOnce(GraphEditorViewModel vm)
    {
        var before = RiskyNode.Runs;
        StaHost.Run(() =>
        {
            // The engine skips a node that is up to date; muting and unmuting makes every node run again.
            foreach (var node in vm.DocumentGraph.Nodes.ToList())
            {
                var wasMuted = node.IsMuted;
                node.IsMuted = !wasMuted;
                node.IsMuted = wasMuted;
            }

            vm.RunGraph();
        });
        return RiskyNode.Runs - before;
    }

    [Fact]
    public void AFileWithARiskyNodeAsksOnceThenRemembersTheFile()
    {
        var path = WriteGraph("asks-once");
        var dialogs = new RecordingDialogs();
        var vm = NewEditor(dialogs);
        StaHost.Run(() => Assert.True(vm.OpenFromPath(path)));

        Assert.Equal(1, RunOnce(vm));
        Assert.Single(dialogs.Confirmations);
        Assert.Contains("Launch Program", dialogs.Confirmations[0]);
        Assert.Contains("runs programs", dialogs.Confirmations[0]);

        // Same editor, later run, and a fresh editor opening the same unchanged file: no more questions.
        Assert.Equal(1, RunOnce(vm));
        var again = NewEditor(dialogs);
        StaHost.Run(() => Assert.True(again.OpenFromPath(path)));
        Assert.Equal(1, RunOnce(again));
        Assert.Single(dialogs.Confirmations);
    }

    [Fact]
    public void DecliningMeansTheNodeDoesNotRun()
    {
        var path = WriteGraph("declined");
        var dialogs = new RecordingDialogs { Answer = false };
        var vm = NewEditor(dialogs);
        StaHost.Run(() => Assert.True(vm.OpenFromPath(path)));

        Assert.Equal(0, RunOnce(vm));
        Assert.Single(dialogs.Confirmations);

        // It asks again next time, since nothing was agreed.
        Assert.Equal(0, RunOnce(vm));
        Assert.Equal(2, dialogs.Confirmations.Count);
    }

    [Fact]
    public void AChangedFileAsksAgain()
    {
        var path = WriteGraph("changes");
        var dialogs = new RecordingDialogs();
        var vm = NewEditor(dialogs);
        StaHost.Run(() => Assert.True(vm.OpenFromPath(path)));
        Assert.Equal(1, RunOnce(vm));

        // Somebody edits the file on disk (here: a second node is added).
        var graph = new GraphSerializer(Registry()).LoadFromFile(path);
        graph.AddNode(new RiskyNode());
        new GraphSerializer(Registry()).SaveToFile(graph, path);

        var reopened = NewEditor(dialogs);
        StaHost.Run(() => Assert.True(reopened.OpenFromPath(path)));
        Assert.Equal(2, RunOnce(reopened));
        Assert.Equal(2, dialogs.Confirmations.Count);
    }

    [Fact]
    public void AutoRunWhenAFileOpensNeitherRunsNorAsks()
    {
        var path = WriteGraph("auto", automatic: true);
        var dialogs = new RecordingDialogs();
        var vm = NewEditor(dialogs);
        var before = RiskyNode.Runs;

        StaHost.Run(() => Assert.True(vm.OpenFromPath(path)));
        Thread.Sleep(800);
        StaHost.Flush();

        Assert.Equal(before, RiskyNode.Runs);
        Assert.Empty(dialogs.Confirmations);
        string status = string.Empty;
        StaHost.Run(() => status = vm.StatusMessage);
        Assert.Contains("Not run automatically", status);
    }

    [Fact]
    public void AGraphMadeInTheEditorNeverAsks()
    {
        var dialogs = new RecordingDialogs();
        var vm = NewEditor(dialogs);
        StaHost.Run(() => vm.Graph.AddNode(new RiskyNode()));

        Assert.Equal(1, RunOnce(vm));
        Assert.Empty(dialogs.Confirmations);
    }

    [Fact]
    public void AGraphTheUserSavedIsTrustedWhenItIsOpenedAgain()
    {
        var dialogs = new RecordingDialogs { SavePath = Path.Combine(_folder, "mine.dyc") };
        var vm = NewEditor(dialogs);
        StaHost.Run(() =>
        {
            vm.Graph.AddNode(new RiskyNode());
            vm.SaveCommand.Execute(null);
        });
        Assert.True(File.Exists(dialogs.SavePath));

        var reopened = NewEditor(dialogs);
        StaHost.Run(() => Assert.True(reopened.OpenFromPath(dialogs.SavePath!)));
        Assert.Equal(1, RunOnce(reopened));
        Assert.Empty(dialogs.Confirmations);
    }

    [Fact]
    public void SavingSomebodyElsesFileDoesNotMakeItTrusted()
    {
        var path = WriteGraph("theirs");
        var dialogs = new RecordingDialogs { Answer = false, SavePath = Path.Combine(_folder, "copy.dyc") };
        var vm = NewEditor(dialogs);
        StaHost.Run(() =>
        {
            Assert.True(vm.OpenFromPath(path));
            vm.SaveAsCommand.Execute(null);
        });

        // Opened, saved under another name, never agreed to: running it still asks (and here the answer is no).
        Assert.Equal(0, RunOnce(vm));
        Assert.Single(dialogs.Confirmations);

        var reopened = NewEditor(dialogs);
        StaHost.Run(() => Assert.True(reopened.OpenFromPath(dialogs.SavePath!)));
        Assert.Equal(0, RunOnce(reopened));
        Assert.Equal(2, dialogs.Confirmations.Count);
    }

    [Fact]
    public void ATurnedOffSettingAndAMutedNodeNeverAsk()
    {
        var path = WriteGraph("muted", muted: true);
        var dialogs = new RecordingDialogs();
        var vm = NewEditor(dialogs);
        StaHost.Run(() => Assert.True(vm.OpenFromPath(path)));
        RunOnce(vm);
        Assert.Empty(dialogs.Confirmations);

        var loud = WriteGraph("loud");
        var quiet = NewEditor(dialogs);
        StaHost.Run(() =>
        {
            quiet.ConfirmUntrustedRuns = false;
            Assert.True(quiet.OpenFromPath(loud));
        });
        Assert.Equal(1, RunOnce(quiet));
        Assert.Empty(dialogs.Confirmations);
    }
}
