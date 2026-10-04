using System;
using System.IO;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>Help > Copy Diagnostics: a report that is safe to post in public and says enough to start a bug investigation.</summary>
public class DiagnosticsUiTests
{
    private static GraphEditorViewModel NewEditor()
    {
        GraphEditorViewModel? vm = null;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-diag-" + Guid.NewGuid().ToString("N") + ".json"));
            vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
        });
        return vm!;
    }

    private static string Report(GraphEditorViewModel vm)
    {
        string text = string.Empty;
        StaHost.Run(() => text = vm.BuildDiagnostics());
        return text;
    }

    [Fact]
    public void TheReportNamesTheVersionsTheLibraryAndTheErrorLog()
    {
        var vm = NewEditor();
        vm.HostDescriptionProvider = () => "Test Host 9.9 (API 1.2)";

        var text = Report(vm);

        Assert.StartsWith("CamelGraph diagnostics", text);
        Assert.Matches(@"CamelGraph: \d+\.\d+\.\d+", text);
        Assert.Contains("Host:      Test Host 9.9 (API 1.2)", text);
        Assert.Contains("Windows:", text);
        Assert.Contains("Node library:", text);
        Assert.Contains("Ask before running graphs from files: on", text);
        Assert.Contains("End of errors.log", text);
        Assert.Contains("Dyncamelo.UI", text);   // the Dyncamelo libraries that are loaded
    }

    [Fact]
    public void ANameThatIdentifiesThePersonOrTheComputerIsNotInTheReport()
    {
        var text = Report(NewEditor());

        foreach (var name in new[] { Environment.UserName, Environment.MachineName })
        {
            if (!string.IsNullOrWhiteSpace(name) && name.Length >= 3)
            {
                Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AHostThatCannotBeReadDoesNotStopTheReport()
    {
        var vm = NewEditor();
        vm.HostDescriptionProvider = () => throw new InvalidOperationException("no host");

        var text = Report(vm);

        Assert.Contains("could not be read (InvalidOperationException)", text);
        Assert.Contains("Node library:", text);
    }

    [Fact]
    public void WithoutAHostTheReportSaysSo()
    {
        Assert.Contains("no host application reported", Report(NewEditor()));
    }
}
