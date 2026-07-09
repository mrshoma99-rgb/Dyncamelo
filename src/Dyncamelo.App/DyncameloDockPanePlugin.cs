using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Autodesk.Navisworks.Api.Plugins;
using Dyncamelo.Navisworks;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace Dyncamelo.App;

/// <summary>
/// The dockable Dyncamelo editor pane. Navisworks dock panes host WinForms
/// controls, so the WPF <see cref="DyncameloEditorControl"/> is wrapped in an
/// <see cref="ElementHost"/>. Plugin id: "Dyncamelo.DockPane.DYNC".
/// </summary>
[Plugin("Dyncamelo.DockPane", "DYNC",
    DisplayName = "Dyncamelo",
    ToolTip = "Dyncamelo visual programming for Navisworks")]
[DockPanePlugin(1000, 700, AutoScroll = false, FixedSize = false, MinimumWidth = 480, MinimumHeight = 360)]
public class DyncameloDockPanePlugin : DockPanePlugin
{
    /// <summary>Plugin id used with SetDockPanePluginVisibility.</summary>
    public const string PluginId = "Dyncamelo.DockPane.DYNC";

    private GraphEditorViewModel? _viewModel;

    /// <inheritdoc />
    public override Control CreateControlPane()
    {
        // Publish the document provider for zero-touch nodes before anything runs.
        NavisworksContext.HostService = DyncameloHost.DocumentService;

        _viewModel = new GraphEditorViewModel(DyncameloHost.Registry)
        {
            EvaluationContextFactory = DyncameloHost.CreateEvaluationContext,
        };

        var editor = new DyncameloEditorControl { ViewModel = _viewModel };

        // Cached ModelItem handles die with the document: force a full re-run
        // whenever the active document changes.
        NavisApplication.ActiveDocumentChanged += OnActiveDocumentChanged;

        var host = new ElementHost
        {
            Child = editor,
            Dock = DockStyle.Fill,
        };
        host.CreateControl();
        return host;
    }

    /// <inheritdoc />
    public override void DestroyControlPane(Control pane)
    {
        NavisApplication.ActiveDocumentChanged -= OnActiveDocumentChanged;
        _viewModel = null;
        pane.Dispose();
    }

    private void OnActiveDocumentChanged(object? sender, EventArgs e)
    {
        _viewModel?.InvalidateAllNodes();
    }
}
