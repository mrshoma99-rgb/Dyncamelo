using System;
using System.IO;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Loader;
using CamelGraph.Nodes;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>
/// Help > Node Packs…: where your own nodes go, what the scan at startup found, and a button to the folder. The scan itself is tested in
/// Core (NodePacksTests); this proves the command, the wording a person reads, and that the folder is made and opened only when asked.
/// </summary>
public class NodePacksUiTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dyc-nodepacks-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        NodePacks.Last = NodePackReport.Empty;
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }

    private GraphEditorViewModel NewEditor(RecordingDialogs dialogs)
    {
        GraphEditorViewModel? vm = null;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-nodepacks-settings-" + Guid.NewGuid().ToString("N") + ".json"));
            vm = new GraphEditorViewModel(registry, dialogs, settings) { NodePackFolder = Path.Combine(_folder, "Packages") };
        });
        return vm!;
    }

    [Fact]
    public void TheCommandIsInTheHelpMenuAndTheCatalogue()
    {
        var command = CommandCatalog.Find("help.nodepacks");

        Assert.NotNull(command);
        Assert.Equal("Help", command!.Category);
        Assert.Contains("custom", command.Keywords);
    }

    [Fact]
    public void TheDialogNamesTheFolderSaysToRestartAndListsWhatLoadedAndWhatDidNot()
    {
        var report = new NodePackReport(
            new[] { "packs" },
            new[]
            {
                new NodePackResult(Path.Combine("packs", "Rebar.dll"), NodePackStatus.Loaded, 3, null),
                new NodePackResult(Path.Combine("packs", "Old.dll"), NodePackStatus.Failed, 0, "BadImageFormatException: built for another platform"),
            });

        var text = GraphEditorViewModel.DescribeNodePacks(@"C:\Users\me\AppData\Roaming\CamelGraph\Packages", report);

        Assert.Contains(@"C:\Users\me\AppData\Roaming\CamelGraph\Packages", text);
        Assert.Contains("Restart Navisworks", text);
        Assert.Contains("Updating CamelGraph leaves this folder alone", text);
        Assert.Contains("1 node pack loaded (3 nodes), 1 could not be loaded.", text);
        Assert.Contains("Rebar.dll: 3 nodes", text);
        Assert.Contains("Old.dll: NOT LOADED. BadImageFormatException", text);
        Assert.Contains("code that runs with your rights", text);
    }

    [Fact]
    public void AnsweringYesCreatesTheFolderAndOpensIt()
    {
        var dialogs = new RecordingDialogs { Answer = true };
        var vm = NewEditor(dialogs);
        string? opened = null;
        vm.FolderOpener = path => { opened = path; return true; };

        StaHost.Run(() => vm.ShowNodePacksCommand.Execute(null));

        var packs = Path.Combine(_folder, "Packages");
        Assert.Single(dialogs.Confirmations);
        Assert.Contains(packs, dialogs.Confirmations[0]);
        Assert.True(Directory.Exists(packs));
        Assert.Equal(packs, opened);
    }

    [Fact]
    public void AnsweringNoLeavesTheDiskAlone()
    {
        var dialogs = new RecordingDialogs { Answer = false };
        var vm = NewEditor(dialogs);
        var openedAnything = false;
        vm.FolderOpener = path => { openedAnything = true; return true; };

        StaHost.Run(() => vm.ShowNodePacksCommand.Execute(null));

        Assert.False(openedAnything);
        Assert.False(Directory.Exists(Path.Combine(_folder, "Packages")));
    }

    [Fact]
    public void AFolderThatWillNotOpenIsSaidInTheStatusLine()
    {
        var vm = NewEditor(new RecordingDialogs { Answer = true });
        vm.FolderOpener = path => false;
        string status = string.Empty;

        StaHost.Run(() =>
        {
            vm.ShowNodePacksCommand.Execute(null);
            status = vm.StatusMessage;
        });

        Assert.StartsWith("Could not open ", status);
        Assert.Contains("File Explorer", status);
    }

    [Fact]
    public void TheDiagnosticsListTheNodePacks()
    {
        NodePacks.Last = new NodePackReport(
            new[] { "packs" },
            new[] { new NodePackResult(Path.Combine("packs", "Rebar.dll"), NodePackStatus.Loaded, 3, null) });
        var vm = NewEditor(new RecordingDialogs());
        string text = string.Empty;

        StaHost.Run(() => text = vm.BuildDiagnostics());

        Assert.Contains("Node packs", text);
        Assert.Contains("Rebar.dll: 3 nodes", text);
    }
}
