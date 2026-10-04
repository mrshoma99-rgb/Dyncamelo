using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using CamelGraph.Core.Editing;
using CamelGraph.Navisworks;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace CamelGraph.App;

/// <summary>
/// The dockable CamelGraph editor pane. Navisworks dock panes host WinForms
/// controls, so the WPF <see cref="CamelGraphEditorControl"/> is wrapped in an
/// <see cref="ElementHost"/>. Plugin id: "CamelGraph.DockPane.DYNC".
/// </summary>
[Plugin("CamelGraph.DockPane", "DYNC",
    DisplayName = "CamelGraph",
    ToolTip = "CamelGraph visual programming for Navisworks")]
[DockPanePlugin(1000, 700, AutoScroll = false, FixedSize = false, MinimumWidth = 480, MinimumHeight = 360)]
public class CamelGraphDockPanePlugin : DockPanePlugin
{
    /// <summary>Plugin id used with SetDockPanePluginVisibility.</summary>
    public const string PluginId = "CamelGraph.DockPane.DYNC";

    private static readonly string InstallFolder =
        System.IO.Path.GetDirectoryName(typeof(CamelGraphDockPanePlugin).Assembly.Location) ?? string.Empty;

    private GraphEditorViewModel? _viewModel;
    private PaneKeyGuard? _keyGuard;
    private Document? _subscribedDocument;

    /// <inheritdoc />
    public override Control CreateControlPane()
    {
        // Publish the document provider for zero-touch nodes before anything runs.
        NavisworksContext.HostService = CamelGraphHost.DocumentService;

        _viewModel = new GraphEditorViewModel(CamelGraphHost.Registry, settings: CamelGraphHost.Settings, preview: new NavisworksPreviewService())
        {
            EvaluationContextFactory = CamelGraphHost.CreateEvaluationContext,
            HostDescriptionProvider = CamelGraphHost.DescribeHost,
            ModelAvailableProvider = () =>
            {
                var document = GetActiveDocument();
                return document != null && document.Models.Count > 0;
            },
        };
        CamelGraphHost.Editor = _viewModel;
        _viewModel.OpenPlayerRequested += (_, _) => CamelGraphPlayerDockPanePlugin.Show();

        var editor = new CamelGraphEditorControl { ViewModel = _viewModel };

        // A failing command or handler must not take Roamer down with it: report it in the editor and log it.
        CamelGraph.UI.Services.CrashGuard.Install(_viewModel, editor.Dispatcher);

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

            // WinForms only forwards drag-and-drop from Explorer (a .dyc dropped on the canvas) when the host allows it.
            AllowDrop = true,
        };
        host.CreateControl();

        // Navisworks binds Ctrl+Z, Ctrl+Y, Delete, F1 … itself; while the pane has the keyboard focus CamelGraph's keys must win.
        _keyGuard?.Dispose();
        _keyGuard = new PaneKeyGuard(host, editor);

        // A script the Player asked to edit before this pane existed.
        var pending = CamelGraphHost.PendingEditorPath;
        CamelGraphHost.PendingEditorPath = null;
        if (pending != null)
        {
            editor.Dispatcher.BeginInvoke(new Action(() => _viewModel?.OpenDroppedFiles(new[] { pending })));
        }

        // Which edition this install is (the Autodesk App Store package carries a marker file); the start screen and Help say so.
        _viewModel?.SetDistribution(DistributionChannel.Detect(InstallFolder));

        // Non-blocking, once-a-day update check; prompts on the UI thread if a newer release exists. A copy installed from the
        // Autodesk App Store is updated by the store, so it never offers the GitHub download.
        // The start screen of the empty editor shows the newer version too, so the prompt is not the only place it is mentioned.
        UpdateCheck.Run(
            action => editor.Dispatcher.BeginInvoke(action),
            () => !DistributionChannel.IsAppStore(InstallFolder) && (_viewModel?.CheckForUpdates ?? true),
            (version, url) => _viewModel?.SetAvailableUpdate(version.ToString(3), url));

        return host;
    }

    /// <inheritdoc />
    public override void DestroyControlPane(Control pane)
    {
        NavisApplication.ActiveDocumentChanged -= OnActiveDocumentChanged;
        SubscribeDocumentEvents(null);
        _keyGuard?.Dispose();
        _keyGuard = null;

        // Closing the pane cannot ask questions: unsaved work is left as an autosave and offered at the next start.
        try
        {
            _viewModel?.EndSession();
        }
        catch (Exception)
        {
            // Shutting down must never fail because of a full disk or a locked folder.
        }

        if (ReferenceEquals(CamelGraphHost.Editor, _viewModel))
        {
            CamelGraphHost.Editor = null;
        }

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
