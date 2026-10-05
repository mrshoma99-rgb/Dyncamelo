using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks;
using CamelGraph.Nodes;

namespace CamelGraph.App;

/// <summary>
/// Session-wide services for the add-in: the node registry (built-in interactive
/// nodes + CamelGraph.Nodes + CamelGraph.Navisworks + third-party packs from the
/// Packages folder) and the evaluation-context factory that injects the
/// Navisworks document provider into every run. Everything is created lazily on
/// first use, on the Navisworks main thread.
/// </summary>
internal static class CamelGraphHost
{
    private static readonly object SyncRoot = new object();
    private static NodeRegistry? _registry;
    private static HostDocumentService? _documentService;
    private static CamelGraph.UI.Services.UiSettingsService? _settings;
    private static CamelGraph.UI.ViewModels.PlayerViewModel? _player;

    [ThreadStatic]
    private static bool _resolvingHostAssembly;

    static CamelGraphHost()
    {
        // CamelGraph.Navisworks compiles against the Chuongmep 2023.0.7 Timeliner
        // reference assembly, which IS strong-named (Autodesk.Navisworks.Timeliner,
        // Version=20.0.1399.50, PublicKeyToken=d85e58fa5af9b484). Navisworks
        // Manage 2024 ships a 21.0.x copy, and .NET Framework fusion enforces an
        // exact version match for strong-named references, so without help the
        // reference fails with FileLoadException and every TimeLiner node silently
        // disappears from the library. Redirect by simple name to the host's
        // already-loaded 2024 assembly (the standard fix for Navisworks add-in
        // version mismatches). Registered in the static constructor so it is in
        // place before any node loading or graph deserialization reflects over
        // CamelGraph.Navisworks.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveHostAssembly;

        // A failure that closes Navisworks leaves only its outer exception in the crash dialog; keep the whole of it, when it is ours.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CamelGraph.UI.Services.CrashGuard.LogFatal(e.ExceptionObject);
    }

    /// <summary>
    /// Redirects strong-named references to Navisworks host assemblies (currently
    /// Autodesk.Navisworks.Timeliner) to whatever version the running host has
    /// loaded, ignoring the version baked in at compile time.
    /// </summary>
    private static Assembly? ResolveHostAssembly(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);
        if (!string.Equals(requested.Name, "Autodesk.Navisworks.Timeliner", StringComparison.OrdinalIgnoreCase))
        {
            return LoadFromAddInFolder(requested.Name);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase))
            {
                return assembly;
            }
        }

        // Not loaded yet (e.g. the TimeLiner module has not spun up): ask fusion
        // for the host's copy by simple name. Guard against re-entrancy — a
        // failed simple-name load raises AssemblyResolve again on this thread.
        if (_resolvingHostAssembly)
        {
            return null;
        }

        _resolvingHostAssembly = true;
        try
        {
            return Assembly.Load(new AssemblyName(requested.Name));
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _resolvingHostAssembly = false;
        }
    }

    /// <summary>
    /// Last resort for a reference the host could not resolve: an assembly that ships beside this add-in. WPF resolves the
    /// <c>pack://application:,,,/Name;component/…</c> URIs of the theme by simple name, which the host's probing paths do not
    /// cover for an add-in folder.
    /// </summary>
    private static Assembly? LoadFromAddInFolder(string? simpleName)
    {
        if (string.IsNullOrEmpty(simpleName) || simpleName!.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var folder = Path.GetDirectoryName(typeof(CamelGraphHost).Assembly.Location);
            if (string.IsNullOrEmpty(folder))
            {
                return null;
            }

            var candidate = Path.Combine(folder, simpleName + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The document provider handed to node code.</summary>
    public static HostDocumentService DocumentService
    {
        get
        {
            lock (SyncRoot)
            {
                return _documentService ?? (_documentService = new HostDocumentService());
            }
        }
    }

    /// <summary>
    /// The one settings store of the session, shared by the editor and the Player. Two stores each holding their own copy
    /// would overwrite each other's changes when they save.
    /// </summary>
    public static CamelGraph.UI.Services.UiSettingsService Settings
    {
        get
        {
            lock (SyncRoot)
            {
                return _settings ?? (_settings = new CamelGraph.UI.Services.UiSettingsService());
            }
        }
    }

    /// <summary>
    /// The Script Player of the session. It lives here, not in its pane, so the ribbon can run the last script without the pane
    /// having been opened yet.
    /// </summary>
    public static CamelGraph.UI.ViewModels.PlayerViewModel Player
    {
        get
        {
            lock (SyncRoot)
            {
                return _player ?? (_player = CreatePlayer());
            }
        }
    }

    /// <summary>True once the Player exists (so a ribbon state check does not build it).</summary>
    public static bool PlayerCreated
    {
        get
        {
            lock (SyncRoot)
            {
                return _player != null;
            }
        }
    }

    private static CamelGraph.UI.ViewModels.PlayerViewModel CreatePlayer()
    {
        NavisworksContext.HostService = DocumentService;
        return new CamelGraph.UI.ViewModels.PlayerViewModel(Registry, settings: Settings)
        {
            EvaluationContextFactory = CreateEvaluationContext,
        };
    }

    /// <summary>The editor of the session, once its pane has been created (the Player hands scripts to it).</summary>
    public static CamelGraph.UI.ViewModels.GraphEditorViewModel? Editor { get; set; }

    /// <summary>A script the Player asked to edit before the editor pane existed; the editor opens it as it starts.</summary>
    public static string? PendingEditorPath { get; set; }

    /// <summary>The fully populated node registry (built lazily once per session).</summary>
    public static NodeRegistry Registry
    {
        get
        {
            lock (SyncRoot)
            {
                return _registry ?? (_registry = BuildRegistry());
            }
        }
    }

    /// <summary>
    /// The running Navisworks product and API version, for the diagnostics report ("Autodesk Navisworks Manage 2024 (API 21.0)").
    /// Uses the same members as the Application.Version node.
    /// </summary>
    public static string DescribeHost()
    {
        var version = Autodesk.Navisworks.Api.Application.Version;
        return version.RuntimeProductName + " (API " + version.ApiMajor + "." + version.ApiMinor + ")";
    }

    /// <summary>
    /// Creates the per-run <see cref="EvaluationContext"/> with the Navisworks
    /// document provider registered. Also (re)publishes the provider for
    /// zero-touch static nodes, which cannot see the context.
    /// </summary>
    public static EvaluationContext CreateEvaluationContext()
    {
        NavisworksContext.HostService = DocumentService;
        var context = new EvaluationContext();
        context.RegisterService<IHostDocumentService>(DocumentService);
        return context;
    }

    private static NodeRegistry BuildRegistry()
    {
        var registry = NodeRegistry.CreateDefault();

        // Lets model-element inputs on nodes read, describe and re-select the live Navisworks selection.
        CamelGraph.Core.Editing.ModelPickerHost.Current = new CamelGraph.Navisworks.NavisworksModelPicker();
        CamelGraph.Core.Editing.ModelPropertyHost.Current = new CamelGraph.Navisworks.NavisworksPropertyCatalog();

        // General-purpose nodes (math/logic/string/list/... plus List.Create,
        // Watch List and Color Picker interactive nodes).
        NodeLibrary.RegisterAll(registry);

        // Navisworks zero-touch nodes + the interactive Captured Selection node.
        try
        {
            registry.RegisterNodeType(
                CamelGraph.Navisworks.CapturedSelectionNode.TypeName,
                () => new CamelGraph.Navisworks.CapturedSelectionNode());
            registry.RegisterAssembly(typeof(NavisworksContext).Assembly);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("CamelGraph: failed to load Navisworks nodes: " + ex.Message);
        }

        LoadPackages(registry);
        return registry;
    }

    /// <summary>
    /// Loads the third-party zero-touch node packs: first those in the per-user folder (%APPDATA%\CamelGraph\Packages, which an update
    /// leaves alone), then those in the "Packages" folder next to the plugin DLL. A pack that fails to load is recorded in
    /// <see cref="NodePacks.Last"/> and skipped; it never prevents the editor from starting.
    /// </summary>
    private static void LoadPackages(NodeRegistry registry)
    {
        string? pluginDirectory = null;
        try
        {
            pluginDirectory = Path.GetDirectoryName(typeof(CamelGraphHost).Assembly.Location);
        }
        catch (Exception)
        {
            // Only the per-user folder is searched then.
        }

        try
        {
            NodePacks.Last = NodePacks.Load(registry, NodePacks.Folders(pluginDirectory), Assembly.LoadFrom);
            foreach (var line in NodePacks.Last.Lines())
            {
                Debug.WriteLine("CamelGraph: node pack " + line);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("CamelGraph: node packs were not scanned: " + ex.Message);
        }
    }
}
