using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks;
using Dyncamelo.Nodes;

namespace Dyncamelo.App;

/// <summary>
/// Session-wide services for the add-in: the node registry (built-in interactive
/// nodes + Dyncamelo.Nodes + Dyncamelo.Navisworks + third-party packs from the
/// Packages folder) and the evaluation-context factory that injects the
/// Navisworks document provider into every run. Everything is created lazily on
/// first use, on the Navisworks main thread.
/// </summary>
internal static class DyncameloHost
{
    private static readonly object SyncRoot = new object();
    private static NodeRegistry? _registry;
    private static HostDocumentService? _documentService;

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

        // General-purpose nodes (math/logic/string/list/... plus List.Create,
        // Watch List and Color Picker interactive nodes).
        NodeLibrary.RegisterAll(registry);

        // Navisworks zero-touch nodes.
        try
        {
            registry.RegisterAssembly(typeof(NavisworksContext).Assembly);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Dyncamelo: failed to load Navisworks nodes: " + ex.Message);
        }

        LoadPackages(registry);
        return registry;
    }

    /// <summary>
    /// Scans the "Packages" folder next to the plugin DLL for third-party
    /// zero-touch node packs. A pack that fails to load is skipped; it never
    /// prevents the editor from starting.
    /// </summary>
    private static void LoadPackages(NodeRegistry registry)
    {
        string packagesDirectory;
        try
        {
            var pluginDirectory = Path.GetDirectoryName(typeof(DyncameloHost).Assembly.Location);
            if (pluginDirectory == null)
            {
                return;
            }

            packagesDirectory = Path.Combine(pluginDirectory, "Packages");
            if (!Directory.Exists(packagesDirectory))
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        foreach (var dllPath in Directory.GetFiles(packagesDirectory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var definitions = registry.RegisterAssembly(Assembly.LoadFrom(dllPath));
                Debug.WriteLine("Dyncamelo: loaded " + definitions.Count + " node(s) from " + dllPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Dyncamelo: skipped package '" + dllPath + "': " + ex.Message);
            }
        }
    }
}
