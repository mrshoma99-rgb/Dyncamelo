using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Autodesk.Navisworks.Api;
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
    private Document? _subscribedDocument;

    /// <inheritdoc />
    public override Control CreateControlPane()
    {
        // Publish the document provider for zero-touch nodes before anything runs.
        NavisworksContext.HostService = DyncameloHost.DocumentService;

        _viewModel = new GraphEditorViewModel(DyncameloHost.Registry, preview: new NavisworksPreviewService())
        {
            EvaluationContextFactory = DyncameloHost.CreateEvaluationContext,
        };

        var editor = new DyncameloEditorControl { ViewModel = _viewModel };

        // Cached ModelItem handles die with the document: force a full re-run
        // whenever the active document changes.
        NavisApplication.ActiveDocumentChanged += OnActiveDocumentChanged;

        // Plan gate I3: Document.Open/AppendFiles/Refresh/Merge and Model.Remove
        // mutate the contents of the SAME active document without changing its
        // identity, so ActiveDocumentChanged never fires for them — yet every
        // cached ModelItem handle from earlier runs is now dead. Subscribe the
        // document's own content-mutation events and flush node output caches.
        SubscribeDocumentEvents(GetActiveDocument());

        var host = new ElementHost
        {
            Child = editor,
            Dock = DockStyle.Fill,
        };
        host.CreateControl();

        // Non-blocking, once-a-day update check; prompts on the UI thread if a newer release exists.
        UpdateCheck.Run(action => editor.Dispatcher.BeginInvoke(action));

        return host;
    }

    /// <inheritdoc />
    public override void DestroyControlPane(Control pane)
    {
        NavisApplication.ActiveDocumentChanged -= OnActiveDocumentChanged;
        SubscribeDocumentEvents(null);
        _viewModel = null;
        pane.Dispose();
    }

    private void OnActiveDocumentChanged(object? sender, EventArgs e)
    {
        SubscribeDocumentEvents(GetActiveDocument());
        _viewModel?.InvalidateAllNodes();
    }

    /// <summary>
    /// Fires on Models.CollectionChanged (open/append/remove/merge),
    /// Models.SceneLoaded and Document.FilesUpdated (refresh). All are raised
    /// synchronously on the main thread — when one of the lifecycle nodes
    /// triggers them mid-run, marking everything dirty here is safe: the engine
    /// clears the executing node's dirty flag AFTER it returns, so the mutating
    /// node itself ends the run clean and an auto-run cannot re-mutate in a loop.
    /// </summary>
    private void OnDocumentContentsChanged(object? sender, EventArgs e)
    {
        _viewModel?.InvalidateAllNodes();
    }

    /// <summary>
    /// Moves the content-mutation subscriptions to <paramref name="document"/>
    /// (null unsubscribes only). Idempotent per document.
    /// </summary>
    private void SubscribeDocumentEvents(Document? document)
    {
        if (ReferenceEquals(_subscribedDocument, document))
        {
            return;
        }

        if (_subscribedDocument != null)
        {
            try
            {
                _subscribedDocument.Models.CollectionChanged -= OnDocumentContentsChanged;
                _subscribedDocument.Models.SceneLoaded -= OnDocumentContentsChanged;
                _subscribedDocument.FilesUpdated -= OnDocumentContentsChanged;
            }
            catch (Exception)
            {
                // The old document may already be disposed during shutdown.
            }

            _subscribedDocument = null;
        }

        if (document != null)
        {
            document.Models.CollectionChanged += OnDocumentContentsChanged;
            document.Models.SceneLoaded += OnDocumentContentsChanged;
            document.FilesUpdated += OnDocumentContentsChanged;
            _subscribedDocument = document;
        }
    }

    private static Document? GetActiveDocument()
    {
        try
        {
            return NavisApplication.ActiveDocument;
        }
        catch (Exception)
        {
            // Outside a fully initialized Navisworks session there is no document.
            return null;
        }
    }
}
