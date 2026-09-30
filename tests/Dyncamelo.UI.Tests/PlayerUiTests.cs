using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Player;
using Dyncamelo.Core.Serialization;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>The Script Player and its author-side commands in the editor.</summary>
public class PlayerUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dyc-player-ui-" + Guid.NewGuid().ToString("N"));
    private string SettingsFile => Path.Combine(_root, "settings.json");
    private string Scripts => Path.Combine(_root, "scripts");

    public PlayerUiTests()
    {
        Directory.CreateDirectory(Scripts);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestSum", () => new SumNode());
        registry.RegisterNodeType("TestModifying", () => new ModifyingUiNode());
        return registry;
    }

    // Width (number input) -> Sum.a ; Sum -> Watch
    private string SaveSumScript(string name = "sum.dyc", string subfolder = "", double width = 2)
    {
        var graph = new GraphModel { Name = "Sum of two", Description = "Adds the width to two." };
        var number = new NumberInputNode { Name = "Width", Value = width, X = 0 };
        var sum = new SumNode { X = 200 };
        var watch = new WatchNode { Name = "Total", X = 400 };
        graph.AddNode(number);
        graph.AddNode(sum);
        graph.AddNode(watch);
        Assert.True(graph.Connect(number.OutPorts[0], sum.InPorts[0]).Success);
        Assert.True(graph.Connect(sum.OutPorts[0], watch.InPorts[0]).Success);
        var folder = Path.Combine(Scripts, subfolder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        new GraphSerializer(Registry()).SaveToFile(graph, path);
        return path;
    }

    private PlayerViewModel NewPlayer(ScriptedDialogs? dialogs = null, UiSettingsService? settings = null)
    {
        var s = settings ?? new UiSettingsService(SettingsFile);
        s.SetPlayerFolders(new[] { Scripts });
        return new PlayerViewModel(Registry(), dialogs ?? new ScriptedDialogs(), s) { CancelPoll = () => false };
    }

    [Fact]
    public void ThePlayerListsTheScriptsOfItsFoldersAndFiltersThem()
    {
        StaHost.Run(() =>
        {
            SaveSumScript("alpha.dyc");
            SaveSumScript("beta.dyc", "Clash");
            var player = NewPlayer();

            player.Refresh();

            Assert.Equal(new[] { "alpha", "beta" }, player.Scripts.Select(s => s.Name).OrderBy(n => n).ToArray());
            Assert.Contains(player.Scripts, s => s.Folder.EndsWith("Clash", StringComparison.Ordinal) && s.Folder.Contains("▸"));

            player.SearchText = "bet";
            Assert.Equal("beta", Assert.Single(player.Scripts).Name);
            player.SearchText = "nothing like this";
            Assert.True(player.IsListEmpty);
            Assert.Contains("nothing like this", player.EmptyListText);
        });
    }

    [Fact]
    public void AnEmptyPlayerExplainsWhereScriptsGo()
    {
        StaHost.Run(() =>
        {
            var player = NewPlayer();
            player.Refresh();

            Assert.True(player.IsListEmpty);
            Assert.Contains(UiSettingsService.DefaultScriptsFolder, player.EmptyListText);
        });
    }

    [Fact]
    public void SelectingAScriptShowsItsFormAndRunningGivesTheResult()
    {
        StaHost.Run(() =>
        {
            SaveSumScript();
            var player = NewPlayer();
            player.Refresh();

            player.SelectedScript = player.Scripts.Single();

            Assert.True(player.HasScript);
            Assert.Equal("Sum of two", player.Title);
            Assert.Equal("Adds the width to two.", player.Description);
            var field = Assert.Single(player.Fields);
            Assert.Equal("Width", field.Label);
            Assert.Equal(PortEditorKind.Number, field.Connector.EditorKind);
            Assert.True(field.Connector.ShowEditor);
            Assert.Equal(2d, field.Connector.NumberValue);
            Assert.False(field.IsChanged);

            field.Connector.NumberValue = 10;
            Assert.True(field.IsChanged);
            Assert.True(player.Run());

            Assert.True(player.HasResult);
            Assert.True(player.ResultSucceeded);
            var output = Assert.Single(player.Outputs);
            Assert.Equal("Total", output.Label);
            Assert.Equal("12", output.Text);   // 10 + the sum node's second input, 2
            Assert.StartsWith("Finished in", player.ResultSummary);
            Assert.Empty(player.Problems);
        });
    }

    [Fact]
    public void ValuesAreRememberedForNextTimeAndResetBringsBackTheScriptsOwn()
    {
        StaHost.Run(() =>
        {
            var path = SaveSumScript();
            var settings = new UiSettingsService(SettingsFile);
            var first = NewPlayer(settings: settings);
            first.Refresh();
            first.SelectedScript = first.Scripts.Single();
            first.Fields[0].Connector.NumberValue = 7;
            first.Run();

            var second = NewPlayer(settings: new UiSettingsService(SettingsFile));
            second.Refresh();
            second.SelectedScript = second.Scripts.Single();

            Assert.Equal(7d, second.Fields[0].Connector.NumberValue);
            Assert.True(second.Fields[0].IsChanged);

            second.ResetCommand.Execute(null);
            Assert.Equal(2d, second.Fields[0].Connector.NumberValue);
            Assert.False(second.Fields[0].IsChanged);
            Assert.Equal(path, second.LastScriptPath);
        });
    }

    [Fact]
    public void AScriptThatChangesTheModelAsksOnceAndAgainOnlyWhenItsFileChanges()
    {
        StaHost.Run(() =>
        {
            var path = SaveModifyingScript();
            var dialogs = new ScriptedDialogs { ConfirmAnswer = false };
            var player = NewPlayer(dialogs);
            player.Refresh();
            player.SelectedScript = player.Scripts.Single();
            Assert.True(player.HasModifies);
            Assert.Contains("Isolate Everything", player.ModifiesText);

            Assert.False(player.Run());                 // declined
            Assert.Equal(1, dialogs.ConfirmQuestions);
            Assert.Contains("Isolate Everything", dialogs.LastConfirm);
            Assert.False(player.HasResult);

            dialogs.ConfirmAnswer = true;
            Assert.True(player.Run());
            Assert.Equal(2, dialogs.ConfirmQuestions);
            Assert.True(player.HasResult);

            Assert.True(player.Run());                   // same file: no further question
            Assert.Equal(2, dialogs.ConfirmQuestions);

            File.AppendAllText(path, " ");               // the file changed
            player.Refresh();
            player.SelectedScript = player.Scripts.Single();
            Assert.True(player.Run());
            Assert.Equal(3, dialogs.ConfirmQuestions);
        });
    }

    private string SaveModifyingScript()
    {
        var graph = new GraphModel { Name = "Changer" };
        graph.AddNode(new ModifyingUiNode { Name = "Isolate Everything" });
        var path = Path.Combine(Scripts, "changer.dyc");
        new GraphSerializer(Registry()).SaveToFile(graph, path);
        return path;
    }

    [Fact]
    public void AScriptWithMissingNodesIsNotRun()
    {
        StaHost.Run(() =>
        {
            var path = SaveSumScript();
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"NodeType\": \"Watch\"", "\"NodeType\": \"NotInstalled\""));
            var dialogs = new ScriptedDialogs();
            var player = NewPlayer(dialogs);
            player.Refresh();
            player.SelectedScript = player.Scripts.Single();

            Assert.True(player.HasMissing);
            Assert.False(player.Run());
            Assert.Contains("not installed", dialogs.LastError);
            Assert.False(player.HasResult);
        });
    }

    [Fact]
    public void AnUnreadableScriptShowsWhyInsteadOfThrowing()
    {
        StaHost.Run(() =>
        {
            File.WriteAllText(Path.Combine(Scripts, "broken.dyc"), "{ not json");
            var player = NewPlayer();
            player.Refresh();

            player.SelectedScript = player.Scripts.Single();

            Assert.True(player.HasLoadError);
            Assert.False(player.HasScript);
            Assert.False(player.RunCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AFailingNodeIsListedAndTheSummaryIsMarkedAsFailed()
    {
        StaHost.Run(() =>
        {
            var graph = new GraphModel { Name = "Breaks" };
            graph.AddNode(new ThrowNode { Name = "Thrower" });
            new GraphSerializer(Registry()).SaveToFile(graph, Path.Combine(Scripts, "breaks.dyc"));
            var registry = Registry();
            registry.RegisterNodeType("TestThrow", () => new ThrowNode());
            var settings = new UiSettingsService(SettingsFile);
            settings.SetPlayerFolders(new[] { Scripts });
            var player = new PlayerViewModel(registry, new ScriptedDialogs(), settings) { CancelPoll = () => false };
            player.Refresh();
            player.SelectedScript = player.Scripts.Single();

            Assert.True(player.Run());

            Assert.False(player.ResultSucceeded);
            var problem = Assert.Single(player.Problems);
            Assert.True(problem.IsError);
            Assert.Equal("Thrower", problem.Node);
            Assert.Contains("it broke", problem.Text);
            Assert.Contains("error", player.ResultSummary);
        });
    }

    [Fact]
    public void EscStopsARunAndTheSummarySaysSo()
    {
        StaHost.Run(() =>
        {
            SaveSumScript();
            var player = NewPlayer();
            player.CancelPoll = () => true;
            player.Refresh();
            player.SelectedScript = player.Scripts.Single();

            player.Run();

            Assert.StartsWith("Stopped after", player.ResultSummary);
            Assert.False(player.ResultSucceeded);
            Assert.False(player.IsRunning);
        });
    }

    [Fact]
    public void RunLastSelectsTheScriptRunBeforeAndRunsItAgain()
    {
        StaHost.Run(() =>
        {
            SaveSumScript("first.dyc");
            var second = SaveSumScript("second.dyc");
            var settings = new UiSettingsService(SettingsFile);
            var player = NewPlayer(settings: settings);
            Assert.False(player.CanRunLast);
            Assert.False(player.RunLast());

            player.Refresh();
            player.SelectedScript = player.Scripts.Single(s => s.Name == "second");
            player.Run();

            var again = NewPlayer(settings: new UiSettingsService(SettingsFile));
            Assert.True(again.CanRunLast);
            Assert.True(again.RunLast());
            Assert.Equal(second, again.SelectedScript!.Path);
            Assert.True(again.HasResult);
        });
    }

    [Fact]
    public void AScriptCanBeRunByItsPathAlone()
    {
        StaHost.Run(() =>
        {
            var path = SaveSumScript();
            var player = NewPlayer();

            Assert.True(player.RunScript(path));
            Assert.Equal("Total", player.Outputs.Single().Label);
            Assert.Equal("12", player.Outputs.Single().Text);
            Assert.False(player.RunScript(Path.Combine(Scripts, "missing.dyc")));
            Assert.Contains("does not exist", player.StatusText);
        });
    }

    [Fact]
    public void FoldersCanBeAddedAndRemovedButNotTheBuiltInOne()
    {
        StaHost.Run(() =>
        {
            var other = Path.Combine(_root, "other");
            Directory.CreateDirectory(other);
            File.Copy(SaveSumScript(), Path.Combine(other, "elsewhere.dyc"));
            var dialogs = new ScriptedDialogs { FolderAnswer = other };
            var settings = new UiSettingsService(SettingsFile);
            var player = new PlayerViewModel(Registry(), dialogs, settings) { CancelPoll = () => false };
            Assert.Single(player.Folders);
            Assert.False(player.Folders[0].CanRemove);
            Assert.Equal(UiSettingsService.DefaultScriptsFolder, player.Folders[0].Path);

            player.AddFolderCommand.Execute(null);

            Assert.Equal(2, player.Folders.Count);
            Assert.Contains(player.Scripts, s => s.Name == "elsewhere");
            Assert.Contains(other, new UiSettingsService(SettingsFile).PlayerFolders);

            player.Folders[1].RemoveCommand.Execute(null);

            Assert.Single(player.Folders);
            Assert.DoesNotContain(player.Scripts, s => s.Name == "elsewhere");
        });
    }

    [Fact]
    public void EditAsksTheHostToOpenTheScript()
    {
        StaHost.Run(() =>
        {
            var path = SaveSumScript();
            var player = NewPlayer();
            player.Refresh();
            var asked = new List<string>();
            player.OpenInEditorRequested += (_, p) => asked.Add(p);
            Assert.False(player.OpenInEditorCommand.CanExecute(null));

            player.SelectedScript = player.Scripts.Single();
            player.OpenInEditorCommand.Execute(null);

            Assert.Equal(new[] { path }, asked.ToArray());
        });
    }

    // ----- the form on screen --------------------------------------------------------------------

    [Fact]
    public void TheFormShowsTheSameEditorsTheNodesUse()
    {
        Window? window = null;
        PlayerViewModel? player = null;
        StaHost.Run(() =>
        {
            var graph = new GraphModel { Name = "Form" };
            graph.AddNode(new NumberSliderNode { Name = "Gap", Min = 0, Max = 10, Step = 0.5, Value = 2, Y = 0 });
            graph.AddNode(new BooleanToggleNode { Name = "Strict", Value = true, Y = 100 });
            graph.AddNode(new StringInputNode { Name = "Test name", Value = "Floors", Y = 200 });
            new GraphSerializer(Registry()).SaveToFile(graph, Path.Combine(Scripts, "form.dyc"));
            player = NewPlayer();
            player.Refresh();
            window = new Window
            {
                Width = 420,
                Height = 760,
                Content = new PlayerControl { ViewModel = player },
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };
            window.Show();
            player.SelectedScript = player.Scripts.Single();
        });
        StaHost.Flush();

        try
        {
            StaHost.Run(() =>
            {
                Assert.Equal(3, player!.Fields.Count);
                Assert.Single(Descendants<ScrubNumberBox>(window!));
                Assert.Single(Descendants<System.Windows.Controls.CheckBox>(window!));
                Assert.Contains(Descendants<System.Windows.Controls.TextBox>(window!), t => t.Text == "Floors");
            });
        }
        finally
        {
            StaHost.Run(() => window!.Close());
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }

    // ----- the author's side in the editor -------------------------------------------------------

    private static GraphEditorViewModel NewEditor(ScriptedDialogs? dialogs = null)
    {
        var registry = Registry();
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        return new GraphEditorViewModel(registry, dialogs ?? new ScriptedDialogs(), settings);
    }

    private static NodeViewModel Vm(GraphEditorViewModel vm, NodeModel node) => vm.Items.OfType<NodeViewModel>().Single(n => n.Model == node);

    [Fact]
    public void ShowInPlayerTogglesTheSelectedNodesAndCanBeUndone()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var sum = new SumNode();
            var number = new NumberInputNode { Name = "Width" };
            vm.Graph.AddNode(sum);
            vm.Graph.AddNode(number);
            vm.SelectedItems.Clear();
            vm.SelectedItems.Add(Vm(vm, sum));
            vm.SelectedItems.Add(Vm(vm, number));

            vm.TogglePlayerNodeCommand.Execute(null);   // the sum node is not shown, the number is: mixed -> show all

            Assert.True(sum.PlayerExposed);
            Assert.Null(number.PlayerExposed);          // already shown by default: nothing stored
            Assert.True(Vm(vm, sum).InPlayer);
            Assert.False(Vm(vm, number).InPlayer);

            vm.TogglePlayerNodeCommand.Execute(null);   // all shown now -> hide all

            Assert.Null(sum.PlayerExposed);
            Assert.False(number.PlayerExposed);

            vm.UndoCommand.Execute(null);
            Assert.True(sum.PlayerExposed);
            Assert.Null(number.PlayerExposed);
        });
    }

    [Fact]
    public void ShowInputsOffersTheUnwiredInputsWithAnEditorAndASocketCanBeToggledOnItsOwn()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var first = new SumNode { X = 0 };
            var second = new SumNode { X = 300 };
            vm.Graph.AddNode(first);
            vm.Graph.AddNode(second);
            Assert.True(vm.Graph.Connect(first.OutPorts[0], second.InPorts[0]).Success);
            vm.SelectedItems.Clear();
            vm.SelectedItems.Add(Vm(vm, second));

            vm.TogglePlayerInputsCommand.Execute(null);

            Assert.False(second.InPorts[0].PlayerExposed);   // wired
            Assert.True(second.InPorts[1].PlayerExposed);
            Assert.True(second.InPorts[2].PlayerExposed);
            Assert.True(Vm(vm, second).InPlayer);

            var socket = Vm(vm, second).Inputs[1];
            Assert.True(socket.IsPlayerExposed);
            vm.TogglePlayerInput(socket);
            Assert.False(second.InPorts[1].PlayerExposed);
            Assert.True(Vm(vm, second).InPlayer);            // the third input still is

            vm.TogglePlayerInput(Vm(vm, second).Inputs[0]);  // wired: refused
            Assert.False(second.InPorts[0].PlayerExposed);
            Assert.Contains("wire", vm.StatusMessage);
        });
    }

    [Fact]
    public void TheScriptDescriptionIsAskedForAndMarksTheGraphModified()
    {
        StaHost.Run(() =>
        {
            var dialogs = new ScriptedDialogs { PromptAnswer = "  Checks the walls.  " };
            var vm = NewEditor(dialogs);
            Assert.False(vm.IsModified);

            vm.DescribeGraphCommand.Execute(null);

            Assert.Equal("Checks the walls.", vm.DocumentGraph.Description);
            Assert.True(vm.IsModified);

            dialogs.PromptAnswer = null;     // cancelled: unchanged
            vm.DescribeGraphCommand.Execute(null);
            Assert.Equal("Checks the walls.", vm.DocumentGraph.Description);
        });
    }

    [Fact]
    public void TheEditorCanAskForThePlayerToBeOpened()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var raised = 0;
            vm.OpenPlayerRequested += (_, _) => raised++;

            vm.OpenPlayerCommand.Execute(null);

            Assert.Equal(1, raised);
        });
    }
}

/// <summary>A node that changes the model (its function is Modify).</summary>
internal sealed class ModifyingUiNode : NodeModel
{
    public ModifyingUiNode()
    {
        Name = "Isolate";
        AddInput("in", typeof(object), (object?)null);
        AddOutput("out", typeof(object));
    }

    public override string NodeType => "TestModifying";

    public override NodeFunction Function => NodeFunction.Modify;

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new[] { inputs[0] };
}
