using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Loader;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>Answers every prompt with a fixed text (renaming needs a name from somewhere).</summary>
internal sealed class NamingDialogs : IDialogService
{
    private readonly string? _answer;

    public NamingDialogs(string? answer) => _answer = answer;

    public string? ShowOpenFile(string filter, string title) => null;

    public string? ShowSaveFile(string filter, string title, string defaultFileName) => null;

    public bool Confirm(string message, string title) => true;

    public SaveChoice AskSaveChanges(string message, string title) => SaveChoice.DontSave;

    public void ShowError(string message, string title)
    {
    }

    public string? Prompt(string message, string title, string defaultValue) => _answer;

    public string? PickFolder(string title, string initialFolder) => null;
}

/// <summary>Node groups in the editor: making one, opening it, editing its interface, the library and the keys.</summary>
public class NodeGroupUiTests
{
    // a -> b -> c, each a SumNode (result = first input + second input, the second defaulting to 2).
    private sealed class Chain
    {
        public GraphEditorViewModel Vm = null!;
        public SumNode A = null!;
        public SumNode B = null!;
        public SumNode C = null!;

        public double Result => (double)C.OutPorts[0].Value!;
    }

    private static GraphEditorViewModel NewEditor(IDialogService? dialogs = null)
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestSum", () => new SumNode());
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        return new GraphEditorViewModel(registry, dialogs ?? new StubDialogs(), settings);
    }

    private static Chain BuildChain(IDialogService? dialogs = null)
    {
        var chain = new Chain { Vm = NewEditor(dialogs) };
        chain.A = new SumNode { X = 0, Y = 0 };
        chain.B = new SumNode { X = 300, Y = 0 };
        chain.C = new SumNode { X = 600, Y = 0 };
        chain.Vm.Graph.AddNode(chain.A);
        chain.Vm.Graph.AddNode(chain.B);
        chain.Vm.Graph.AddNode(chain.C);
        Assert.True(chain.Vm.Graph.Connect(chain.A.OutPorts[0], chain.B.InPorts[0]).Success);
        Assert.True(chain.Vm.Graph.Connect(chain.B.OutPorts[0], chain.C.InPorts[0]).Success);
        chain.Vm.RunGraph();
        return chain;
    }

    private static NodeViewModel Vm(GraphEditorViewModel vm, NodeModel model) =>
        vm.Items.OfType<NodeViewModel>().Single(n => n.Model == model);

    private static void Select(GraphEditorViewModel vm, params NodeModel[] nodes)
    {
        vm.SelectedItems.Clear();
        foreach (var node in nodes)
        {
            vm.SelectedItems.Add(Vm(vm, node));
        }
    }

    private static GroupInstanceNode MakeGroupOfB(Chain chain)
    {
        Select(chain.Vm, chain.B);
        chain.Vm.MakeGroupCommand.Execute(null);
        return chain.Vm.Graph.Nodes.OfType<GroupInstanceNode>().Single();
    }

    [Fact]
    public void MakingAGroupReplacesTheSelectionWithAnInstanceTheLibraryLists()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            Assert.Equal(7.0, chain.Result);

            var instance = MakeGroupOfB(chain);

            Assert.Equal(3, chain.Vm.Items.OfType<NodeViewModel>().Count());    // a, c and the instance
            Assert.True(Vm(chain.Vm, instance).IsGroupInstance);
            Assert.Contains(Vm(chain.Vm, instance), chain.Vm.SelectedItems);
            Assert.Contains("Made node group", chain.Vm.StatusMessage);
            Assert.False(Vm(chain.Vm, instance).HasBody);   // an instance is an ordinary card, no body row

            var entry = Assert.Single(chain.Vm.Library.AllEntries, e => e.Category == "Node Groups");
            Assert.Equal(GraphEditorViewModel.GroupLibraryPrefix + instance.Definition!.Id.ToString("N"), entry.Id);
            Assert.Contains("a: number", entry.Signature);

            chain.Vm.RunGraph();
            Assert.Equal(7.0, chain.Result);

            chain.Vm.UndoCommand.Execute(null);
            Assert.Equal(3, chain.Vm.Items.OfType<NodeViewModel>().Count());
            Assert.Contains(chain.Vm.Items.OfType<NodeViewModel>(), n => n.Model == chain.B);
            Assert.DoesNotContain(chain.Vm.Library.AllEntries, e => e.Category == "Node Groups");
        });
    }

    [Fact]
    public void TabOpensTheSelectedGroupAndEachLevelKeepsItsOwnHistory()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var instance = MakeGroupOfB(chain);
            var group = instance.Definition!;
            var documentHistory = chain.Vm.History;
            Assert.Equal("Make node group", documentHistory.UndoLabel);

            chain.Vm.ToggleGroupEditCommand.Execute(null);

            Assert.True(chain.Vm.IsInsideGroup);
            Assert.Equal(1, chain.Vm.GroupDepth);
            Assert.Same(group, chain.Vm.CurrentGroup);
            Assert.Same(group.Graph, chain.Vm.Graph);
            Assert.NotSame(group.Graph, chain.Vm.DocumentGraph);
            Assert.Equal(3, chain.Vm.Items.OfType<NodeViewModel>().Count());   // b, Group Input, Group Output
            Assert.Equal(new[] { "Untitled", group.Name }, chain.Vm.Breadcrumb.Select(b => b.Title).ToArray());
            Assert.True(chain.Vm.Breadcrumb.Last().IsCurrent);
            Assert.Equal(0, chain.Vm.History.UndoCount);
            Assert.NotSame(documentHistory, chain.Vm.History);

            // An edit inside is undone inside, and never touches the graph outside.
            group.Graph.AddNode(new SumNode { X = 100, Y = 200 });
            Assert.Equal(1, chain.Vm.History.UndoCount);
            chain.Vm.UndoCommand.Execute(null);
            Assert.Equal(3, group.Graph.Nodes.Count);
            Assert.Equal(3, chain.Vm.Items.OfType<NodeViewModel>().Count());

            chain.Vm.ExitGroupCommand.Execute(null);

            Assert.False(chain.Vm.IsInsideGroup);
            Assert.Same(chain.Vm.DocumentGraph, chain.Vm.Graph);
            Assert.Same(documentHistory, chain.Vm.History);
            Assert.Empty(chain.Vm.Breadcrumb);
            Assert.Contains(Vm(chain.Vm, instance), chain.Vm.SelectedItems);   // back on the group it came from
            Assert.Equal(3, chain.Vm.Items.OfType<NodeViewModel>().Count());
        });
    }

    [Fact]
    public void TabWithNothingSelectedClosesTheOpenGroupAndOtherwiseExplains()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            chain.Vm.SelectedItems.Clear();
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            Assert.False(chain.Vm.IsInsideGroup);
            Assert.Contains("Select a node group", chain.Vm.StatusMessage);

            MakeGroupOfB(chain);
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            Assert.True(chain.Vm.IsInsideGroup);
            chain.Vm.SelectedItems.Clear();
            chain.Vm.ToggleGroupEditCommand.Execute(null);   // nothing selected inside: Tab goes back out
            Assert.False(chain.Vm.IsInsideGroup);
        });
    }

    [Fact]
    public void RunningInsideAGroupRunsTheWholeGraphAndAnEditThereChangesTheResult()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var instance = MakeGroupOfB(chain);
            var inner = instance.Definition!.Graph.Nodes.OfType<SumNode>().Single();
            chain.Vm.RunGraph();
            Assert.Equal(7.0, chain.Result);

            chain.Vm.ToggleGroupEditCommand.Execute(null);
            inner.InPorts[1].SetUserValue(10.0);    // inside the group: a + 10 instead of a + 2
            Assert.True(instance.IsDirty);           // the edit reached the instance outside
            chain.Vm.RunGraph();

            Assert.True(chain.Vm.IsInsideGroup);
            Assert.Equal(15.0, chain.Result);        // (1 + 2 = 3) + 10 = 13, then c adds 2
            Assert.Contains("Run finished", chain.Vm.StatusMessage);
        });
    }

    [Fact]
    public void OpeningAGroupIsNotAChangeButEditingInsideItMakesTheDocumentModified()
    {
        StaHost.Run(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "dyc-groupdirty-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var chain = BuildChain(new ScriptedDialogs { SavePath = Path.Combine(folder, "plan.dyc") });
                MakeGroupOfB(chain);
                chain.Vm.SaveCommand.Execute(null);
                Assert.False(chain.Vm.IsModified);

                chain.Vm.ToggleGroupEditCommand.Execute(null);
                Assert.True(chain.Vm.IsInsideGroup);
                Assert.False(chain.Vm.IsModified);

                chain.Vm.Graph.AddNode(new SumNode { X = 900, Y = 0 });

                Assert.True(chain.Vm.IsModified);
                chain.Vm.ExitGroupCommand.Execute(null);
                Assert.Contains("*", chain.Vm.Title);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        });
    }

    [Fact]
    public void DroppingAWireOnTheGroupOutputNodeAddsASocketAndConnectsIt()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var group = MakeGroupOfB(chain).Definition!;
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            var extra = new SumNode { X = 300, Y = 400 };
            chain.Vm.Graph.AddNode(extra);
            var outputVm = Vm(chain.Vm, group.OutputNode);
            outputVm.Size = new Size(200, 100);
            var before = group.Outputs.Count;

            var dragged = Vm(chain.Vm, extra).Outputs[0];
            var handled = chain.Vm.TryCreateGroupSocketFromDrop(dragged, new Point(outputVm.Location.X + 20, outputVm.Location.Y + 20));

            Assert.True(handled);
            Assert.Equal(before + 1, group.Outputs.Count);
            Assert.StartsWith("result", group.Outputs[group.Outputs.Count - 1].Name);
            Assert.Contains(chain.Vm.Graph.Connections, c => c.SourceNode == extra && c.TargetNode == group.OutputNode);

            chain.Vm.UndoCommand.Execute(null);
            Assert.Equal(before, group.Outputs.Count);
            Assert.DoesNotContain(chain.Vm.Graph.Connections, c => c.SourceNode == extra && c.TargetNode == group.OutputNode);
        });
    }

    [Fact]
    public void DroppingAnUnwiredInputOnTheGroupInputNodeAddsAnInputSocketFeedingIt()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var group = MakeGroupOfB(chain).Definition!;
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            var extra = new SumNode { X = 300, Y = 400 };
            chain.Vm.Graph.AddNode(extra);
            var inputVm = Vm(chain.Vm, group.InputNode);
            inputVm.Size = new Size(200, 100);
            var before = group.Inputs.Count;

            var dragged = Vm(chain.Vm, extra).Inputs[2];   // "c": nothing wired to it
            var handled = chain.Vm.TryCreateGroupSocketFromDrop(dragged, new Point(inputVm.Location.X + 20, inputVm.Location.Y + 20));

            Assert.True(handled);
            Assert.Equal(before + 1, group.Inputs.Count);
            Assert.StartsWith("c", group.Inputs[group.Inputs.Count - 1].Name);
            Assert.Contains(chain.Vm.Graph.Connections, c => c.SourceNode == group.InputNode && c.TargetNode == extra);
        });
    }

    [Fact]
    public void ADropAnywhereElseOrOnTheWrongSideIsLeftToTheOrdinaryRules()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var group = MakeGroupOfB(chain).Definition!;
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            var extra = new SumNode { X = 300, Y = 400 };
            chain.Vm.Graph.AddNode(extra);
            var inputVm = Vm(chain.Vm, group.InputNode);
            inputVm.Size = new Size(200, 100);
            var outputVm = Vm(chain.Vm, group.OutputNode);
            outputVm.Size = new Size(200, 100);
            var socketCount = group.Inputs.Count + group.Outputs.Count;

            // An output socket dropped on the Group Input node, and a drop on empty canvas: nothing is made.
            Assert.False(chain.Vm.TryCreateGroupSocketFromDrop(Vm(chain.Vm, extra).Outputs[0], new Point(inputVm.Location.X + 20, inputVm.Location.Y + 20)));
            Assert.False(chain.Vm.TryCreateGroupSocketFromDrop(Vm(chain.Vm, extra).Outputs[0], new Point(-5000, -5000)));
            Assert.Equal(socketCount, group.Inputs.Count + group.Outputs.Count);
        });
    }

    [Fact]
    public void TheTitleAndTheDocumentStayThoseOfTheFileWhileAGroupIsOpen()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            chain.Vm.DocumentGraph.Name = "Plan";
            MakeGroupOfB(chain);
            var titleAtRoot = chain.Vm.Title;
            var document = chain.Vm.DocumentGraph;

            chain.Vm.ToggleGroupEditCommand.Execute(null);

            Assert.Equal(titleAtRoot, chain.Vm.Title);
            Assert.Same(document, chain.Vm.DocumentGraph);
            Assert.Equal("Plan", chain.Vm.Breadcrumb[0].Title);
        });
    }

    [Fact]
    public void AddingAGroupFromTheLibraryMakesAnInstanceAndAGroupCannotHoldItself()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var group = MakeGroupOfB(chain).Definition!;
            var id = GraphEditorViewModel.GroupLibraryPrefix + group.Id.ToString("N");

            var added = chain.Vm.AddNode(id, new Point(400, 300));

            Assert.NotNull(added);
            Assert.True(added!.IsGroupInstance);
            Assert.Same(group, ((GroupInstanceNode)added.Model).Definition);
            Assert.Equal(2, chain.Vm.Graph.Nodes.OfType<GroupInstanceNode>().Count());

            chain.Vm.SelectedItems.Clear();
            chain.Vm.SelectedItems.Add(added);
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            Assert.Null(chain.Vm.AddNode(id, new Point(0, 0)));
            Assert.Contains("cannot be used inside", chain.Vm.StatusMessage);

            Assert.Null(chain.Vm.AddNode("NodeGroup", new Point(0, 0)));   // the raw type is not a node you can add
            Assert.Null(chain.Vm.AddNode(GraphEditorViewModel.GroupLibraryPrefix + Guid.NewGuid().ToString("N"), new Point(0, 0)));
        });
    }

    [Fact]
    public void TheInterfaceNodesCannotBeDeletedButTheirSocketsCanBeEdited()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var instance = MakeGroupOfB(chain);
            var group = instance.Definition!;
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            var inputVm = Vm(chain.Vm, group.InputNode);

            Select(chain.Vm, group.InputNode);
            chain.Vm.DeleteSelectionCommand.Execute(null);
            Assert.Contains(group.InputNode, chain.Vm.Graph.Nodes);
            Assert.Contains("cannot be deleted", chain.Vm.StatusMessage);

            // "+" adds a socket and the node grows a connector for it.
            Assert.Single(inputVm.Outputs);
            inputVm.AddGroupSocketCommand.Execute(null);
            Assert.Equal(2, group.Inputs.Count);
            Assert.Equal(2, inputVm.Outputs.Count);
            Assert.Equal(group.Inputs[1].Name, inputVm.Outputs[1].Title);

            // A socket can be typed, moved and removed from its own menu; each is undoable.
            var second = inputVm.Outputs[1];
            Assert.True(second.IsGroupSocket);
            second.SetSocketKindCommand.Execute("text");
            Assert.Equal("text", group.Inputs[1].Kind);
            Assert.Equal(PortFamily.Text, second.Family);
            second.MoveSocketUpCommand.Execute(null);
            Assert.Equal(second.Port.Id, group.Inputs[0].Id);
            Assert.Same(second, inputVm.Outputs[0]);      // the connector followed its port to the top
            second.RemoveSocketCommand.Execute(null);
            Assert.Single(group.Inputs);
            chain.Vm.UndoCommand.Execute(null);
            Assert.Equal(2, group.Inputs.Count);
            Assert.Equal(2, inputVm.Outputs.Count);
        });
    }

    [Fact]
    public void RenamingASocketRelabelsItAndTheInstanceOutside()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain(new NamingDialogs("amount"));
            var instance = MakeGroupOfB(chain);
            var group = instance.Definition!;
            var instanceVm = Vm(chain.Vm, instance);
            Assert.Equal("a", instanceVm.Inputs[0].Title);
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            var inputVm = Vm(chain.Vm, group.InputNode);

            inputVm.Outputs[0].RenameSocketCommand.Execute(null);

            Assert.Equal("amount", inputVm.Outputs[0].Title);
            Assert.Equal("amount", group.Inputs[0].Name);
            chain.Vm.ExitGroupCommand.Execute(null);
            Assert.Equal("amount", Vm(chain.Vm, instance).Inputs[0].Title);   // a new card, read from the renamed port
            Assert.NotNull(chain.Vm.Graph.FindConnectionInto(instance.InPorts[0]));    // and still wired
        });
    }

    [Fact]
    public void RenamingTheGroupRenamesTheLibraryEntryAndTheInstance()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain(new NamingDialogs("Doubler"));
            var instance = MakeGroupOfB(chain);

            Vm(chain.Vm, instance).RenameGroupCommand.Execute(null);

            Assert.Equal("Doubler", instance.Definition!.Name);
            Assert.Equal("Doubler", instance.Name);
            Assert.Equal("Doubler", Vm(chain.Vm, instance).Title);
            Assert.Contains(chain.Vm.Library.AllEntries, e => e.Category == "Node Groups" && e.Name == "Doubler");
        });
    }

    [Fact]
    public void DuplicatingAnInstanceSharesTheDefinitionAndUngroupingBringsTheNodesBack()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var instance = MakeGroupOfB(chain);
            chain.Vm.DuplicateSelectionCommand.Execute(null);
            var instances = chain.Vm.Graph.Nodes.OfType<GroupInstanceNode>().ToList();
            Assert.Equal(2, instances.Count);
            Assert.Same(instances[0].Definition, instances[1].Definition);
            Assert.Single(chain.Vm.Library.AllEntries, e => e.Category == "Node Groups");

            Select(chain.Vm, instance);
            chain.Vm.UngroupNodeGroupCommand.Execute(null);
            Assert.DoesNotContain(instance, chain.Vm.Graph.Nodes);
            Assert.Equal(1, chain.Vm.Graph.Nodes.OfType<GroupInstanceNode>().Count());
            Assert.Equal(3, chain.Vm.Graph.Nodes.Count(n => n is SumNode));   // a, c and the copy of b that the ungrouped instance left
        });
    }

    [Fact]
    public void OpeningAnotherGraphClosesTheGroupAndRestoresTheDocumentHistory()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var documentHistory = chain.Vm.History;
            MakeGroupOfB(chain);
            chain.Vm.ToggleGroupEditCommand.Execute(null);
            Assert.True(chain.Vm.IsInsideGroup);

            chain.Vm.LoadGraph(new GraphModel { Name = "Other" });

            Assert.False(chain.Vm.IsInsideGroup);
            Assert.Empty(chain.Vm.Breadcrumb);
            Assert.Same(documentHistory, chain.Vm.History);
            Assert.Equal("Other", chain.Vm.Title);
            Assert.DoesNotContain(chain.Vm.Library.AllEntries, e => e.Category == "Node Groups");
        });
    }

    [Fact]
    public void UnusedGroupsCanBePurgedAndItUndoes()
    {
        StaHost.Run(() =>
        {
            var chain = BuildChain();
            var instance = MakeGroupOfB(chain);
            chain.Vm.PurgeNodeGroupsCommand.Execute(null);
            Assert.Contains("in use", chain.Vm.StatusMessage);

            Select(chain.Vm, instance);
            chain.Vm.DeleteSelectionCommand.Execute(null);
            chain.Vm.PurgeNodeGroupsCommand.Execute(null);
            Assert.Empty(chain.Vm.DocumentGraph.NodeGroups.Groups);
            Assert.DoesNotContain(chain.Vm.Library.AllEntries, e => e.Category == "Node Groups");

            chain.Vm.UndoCommand.Execute(null);
            Assert.Single(chain.Vm.DocumentGraph.NodeGroups.Groups);
        });
    }
}

/// <summary>The group commands as the real control routes and shows them.</summary>
public class NodeGroupViewTests
{
    private sealed class Host : IDisposable
    {
        public GraphEditorViewModel Vm = null!;
        public Window Window = null!;
        public CamelGraphEditorControl Control => (CamelGraphEditorControl)Window.Content;

        public void Dispose() => StaHost.Run(() => Window.Close());
    }

    private static Host Build(out SumNode b)
    {
        var host = new Host();
        SumNode node = null!;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            host.Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            var a = new SumNode { X = 0, Y = 0 };
            node = new SumNode { X = 300, Y = 0 };
            var c = new SumNode { X = 600, Y = 0 };
            host.Vm.Graph.AddNode(a);
            host.Vm.Graph.AddNode(node);
            host.Vm.Graph.AddNode(c);
            host.Vm.Graph.Connect(a.OutPorts[0], node.InPorts[0]);
            host.Vm.Graph.Connect(node.OutPorts[0], c.InPorts[0]);
            host.Window = new Window
            {
                Width = 1400,
                Height = 900,
                Content = new CamelGraphEditorControl { ViewModel = host.Vm },
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };
            host.Window.Show();
        });
        StaHost.Flush();
        b = node;
        return host;
    }

    private static List<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new List<T>();
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                found.Add(match);
            }

            found.AddRange(Descendants<T>(child));
        }

        return found;
    }

    [Fact]
    public void TabAndShiftTabOpenAndCloseTheSelectedGroupThroughTheHostKeyPath()
    {
        using var host = Build(out var b);
        StaHost.Run(() =>
        {
            host.Vm.SelectedItems.Add(host.Vm.Items.OfType<NodeViewModel>().Single(n => n.Model == b));
            host.Control.ModifierProvider = () => ModifierKeys.Control | ModifierKeys.Alt;
            Assert.True(host.Control.WantsHostKey(Key.G));
            Assert.True(host.Control.ProcessHostKey(Key.G));        // Ctrl+Alt+G makes the group
            Assert.Single(host.Vm.Graph.Nodes.OfType<GroupInstanceNode>());

            host.Control.ModifierProvider = () => ModifierKeys.None;
            Assert.True(host.Control.WantsHostKey(Key.Tab));
            Assert.True(host.Control.ProcessHostKey(Key.Tab));      // Tab opens it
            Assert.True(host.Vm.IsInsideGroup);

            host.Control.ModifierProvider = () => ModifierKeys.Shift;
            Assert.True(host.Control.ProcessHostKey(Key.Tab));      // Shift+Tab closes it
            Assert.False(host.Vm.IsInsideGroup);
        });
    }

    [Fact]
    public void TheNodeGroupsMenuListsTheCommandsWithTheirShortcuts()
    {
        using var host = Build(out _);
        StaHost.Run(() =>
        {
            var menu = (Menu)host.Control.FindName("HeaderMenu");
            var groups = menu.Items.OfType<MenuItem>().Single(m => (m.Header as string) == "Node Groups");
            var titles = groups.Items.OfType<MenuItem>().ToDictionary(m => ((TextBlock)m.Header).Text, m => m.InputGestureText);

            Assert.Equal("Ctrl+Alt+G", titles["Make Node Group"]);
            Assert.Equal("Tab", titles["Open / Close Node Group"]);
            Assert.Equal("Shift+Tab", titles["Close Node Group"]);
            Assert.True(titles.ContainsKey("Delete Unused Node Groups"));
        });
    }

    [Fact]
    public void TheBreadcrumbBarAndTheOpenButtonAppearOnlyWhereTheyBelong()
    {
        using var host = Build(out var b);
        StaHost.Run(() =>
        {
            var closeButton = Descendants<Button>(host.Window).Single(x => (x.Content as string) == "Close group");
            Assert.False(closeButton.IsVisible);

            host.Vm.SelectedItems.Add(host.Vm.Items.OfType<NodeViewModel>().Single(n => n.Model == b));
            host.Vm.MakeGroupCommand.Execute(null);
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var open = Descendants<Button>(host.Window).Where(x => (x.ToolTip as string ?? string.Empty).StartsWith("Open this node group")).ToList();
            Assert.Single(open.Where(x => x.IsVisible));        // only the instance's header shows it

            host.Vm.ToggleGroupEditCommand.Execute(null);
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var closeButton = Descendants<Button>(host.Window).Single(x => (x.Content as string) == "Close group");
            Assert.True(closeButton.IsVisible);
            var crumbs = Descendants<Button>(host.Window).Where(x => (x.ToolTip as string ?? string.Empty).StartsWith("Go back to this level")
                                                                      || (x.ToolTip as string ?? string.Empty).StartsWith("The node group you are editing")).ToList();
            Assert.Equal(2, crumbs.Count);

            // The group's interface nodes carry the add button, and group sockets have their extra menu items.
            Assert.Equal(2, Descendants<Button>(host.Window).Count(x => (x.ToolTip as string ?? string.Empty).StartsWith("Add a socket")));
        });
    }
}
