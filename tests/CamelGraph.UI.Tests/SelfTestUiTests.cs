using System;
using System.IO;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Loader;
using CamelGraph.Core.SelfTest;
using CamelGraph.Nodes;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>
/// Help > Run Self-Test as the editor wires it. The Navisworks nodes are not loaded in the test process, so this proves the plumbing:
/// the command exists, the run reports plainly what it could not do, skipped cases are counted, and the result reaches the status bar
/// and, for problems, a message. What the checks find inside Navisworks is for a person with Navisworks to see.
/// </summary>
public class SelfTestUiTests
{
    private static GraphEditorViewModel NewEditor(RecordingDialogs dialogs)
    {
        GraphEditorViewModel? vm = null;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-selftest-" + Guid.NewGuid().ToString("N") + ".json"));
            vm = new GraphEditorViewModel(registry, dialogs, settings);
        });
        return vm!;
    }

    [Fact]
    public void WithoutNavisworksNodesTheRunSaysSoPlainlyAndSkipsWhatItCannotTry()
    {
        var dialogs = new RecordingDialogs();
        var vm = NewEditor(dialogs);
        vm.ModelAvailableProvider = () => false;
        SelfTestReport? report = null;

        StaHost.Run(() => report = vm.RunSelfTest());

        var all = SelfTestCatalog.All;
        var notRunnable = all.Count(c => c.NeedsModel || c.NeedsEdition != null);
        Assert.Equal(all.Count, report!.Results.Count);
        Assert.Equal(notRunnable, report.Skipped);
        Assert.Equal(all.Count - notRunnable, report.Failed);
        Assert.Equal(0, report.Passed);
        Assert.Same(report, vm.LastSelfTest);

        string status = string.Empty;
        StaHost.Run(() => status = vm.StatusMessage);
        Assert.StartsWith("Self-test: 0 passed, " + report.Failed + " failed, " + report.Skipped + " skipped.", status);
        Assert.Contains(dialogs.Errors, e => e.Contains("not in the node library") && e.Contains("The full report is on the clipboard"));
        Assert.All(report.Results.Where(r => r.Outcome == SelfTestOutcome.Failed), r => Assert.Contains("not in the node library", r.Message));
    }

    [Fact]
    public void TheCommandsAreInTheCatalogueAndTheSelfTestCommandRuns()
    {
        Assert.NotNull(CommandCatalog.Find("help.selftest"));
        Assert.NotNull(CommandCatalog.Find("help.diagnostics"));

        var vm = NewEditor(new RecordingDialogs());
        vm.ModelAvailableProvider = () => false;
        StaHost.Run(() => vm.RunSelfTestCommand.Execute(null));

        Assert.NotNull(vm.LastSelfTest);
    }
}
