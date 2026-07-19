using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace Dyncamelo.App;

/// <summary>
/// Merges duplicate "BIMCamel" ribbon tabs into one.
///
/// Every BIMCamel plug-in (Dyncamelo, the IFC exporter, …) declares the same ribbon tab id
/// (ID_Tab_BIMCamel) in its RibbonLayout xaml, but Navisworks composes each plug-in's layout
/// independently and does NOT merge tabs that share an id — with two BIMCamel tools installed
/// the user gets two "BIMCamel" tabs. This helper finds the AdWindows ribbon after composition,
/// moves the panels of every duplicate tab into the first one, and removes the emptied tabs.
///
/// Discovery is multi-strategy because Navisworks is not AutoCAD: ComponentManager.Ribbon (the
/// AutoCAD-era static) is tried first, then a visual-tree walk over every live WPF presentation
/// source hunting for the Autodesk.Windows.RibbonControl instance. Duplicate detection is
/// likewise tolerant: exact tab id, a namespaced id ending in the shared id (hosts may prefix
/// plug-in tab ids), or the visible "BIMCamel" title.
///
/// Both plug-ins carry an identical copy and both install it (idempotent — whichever runs last
/// does the merge; with a single tool installed it finds one tab and does nothing). AdWindows is
/// accessed via reflection on the host's already-loaded assembly: the per-year Navisworks NuGet
/// reference packages don't include AdWindows.dll, and the ribbon object model we touch
/// (RibbonControl → Tabs → Panels, all ObservableCollections) is stable across 2024-2026.
/// Runs on Application.Idle, so it executes on the UI thread after the ribbon exists, and keeps
/// watching cheaply (guarded by a tab-count check) so a ribbon rebuild — workspace/user-profile
/// load — gets re-merged too. Any failure unhooks and leaves the ribbon untouched: worst case
/// is the current two-tab behavior, never a crash.
/// </summary>
internal static class RibbonTabMerger
{
    private const string TabId = "ID_Tab_BIMCamel";
    private const string TabTitle = "BIMCamel";

    private static bool _installed;
    private static int _lastCount = -1;

    /// <summary>Starts watching for duplicate tabs. Safe to call repeatedly.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        System.Windows.Forms.Application.Idle += OnIdle;
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        try
        {
            Merge();
        }
        catch
        {
            // Reflection surface changed (future Navisworks) — stop trying for this session.
            System.Windows.Forms.Application.Idle -= OnIdle;
        }
    }

    private static void Merge()
    {
        var tabs = FindTabsViaComponentManager() ?? FindTabsViaPresentationSources();
        if (tabs == null)
        {
            return;
        }

        if (tabs.Count == _lastCount)
        {
            return; // nothing new composed since last look
        }

        _lastCount = tabs.Count;

        object? primary = null;
        var duplicates = new List<object>();
        foreach (var tab in tabs)
        {
            if (tab == null || !IsBimCamelTab(tab))
            {
                continue;
            }

            if (primary == null)
            {
                primary = tab;
            }
            else
            {
                duplicates.Add(tab);
            }
        }

        if (primary == null || duplicates.Count == 0)
        {
            return;
        }

        if (!(GetProp<object>(primary, "Panels") is IList primaryPanels))
        {
            return;
        }

        foreach (var dup in duplicates)
        {
            if (GetProp<object>(dup, "Panels") is IList panels)
            {
                foreach (var panel in panels.Cast<object>().ToList())
                {
                    panels.Remove(panel);
                    primaryPanels.Add(panel);
                }
            }

            tabs.Remove(dup);
        }

        _lastCount = tabs.Count;
    }

    // A BIMCamel tab is recognised by exact id, by a host-namespaced id that still ends with
    // the shared id, or — last resort — by its visible title.
    private static bool IsBimCamelTab(object tab)
    {
        var id = GetProp<string>(tab, "Id");
        if (id != null &&
            (id.Equals(TabId, StringComparison.Ordinal) || id.EndsWith(TabId, StringComparison.Ordinal)))
        {
            return true;
        }

        var title = GetProp<string>(tab, "Title");
        return title != null && title.Equals(TabTitle, StringComparison.Ordinal);
    }

    // ── ribbon discovery ────────────────────────────────────────────────────

    /// <summary>AutoCAD-style static registry; may be unpopulated in Navisworks.</summary>
    private static IList? FindTabsViaComponentManager()
    {
        var adWindows = AdWindowsAssembly();
        var componentManager = adWindows?.GetType("Autodesk.Windows.ComponentManager");
        var ribbon = componentManager?.GetProperty("Ribbon", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return ribbon == null ? null : GetProp<object>(ribbon, "Tabs") as IList;
    }

    /// <summary>
    /// Walks every live WPF visual tree on this thread looking for the RibbonControl instance
    /// (the Navisworks ribbon is AdWindows WPF hosted in the main frame).
    /// </summary>
    private static IList? FindTabsViaPresentationSources()
    {
        var ribbonType = AdWindowsAssembly()?.GetType("Autodesk.Windows.RibbonControl");
        if (ribbonType == null)
        {
            return null;
        }

        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (!(source.RootVisual is DependencyObject root))
            {
                continue;
            }

            var ribbon = FindDescendantOfType(root, ribbonType);
            if (ribbon != null)
            {
                return GetProp<object>(ribbon, "Tabs") as IList;
            }
        }

        return null;
    }

    /// <summary>Breadth-first, budget-capped visual-tree search (the ribbon sits near the top).</summary>
    private static object? FindDescendantOfType(DependencyObject root, Type type)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited < 5000)
        {
            var node = queue.Dequeue();
            visited++;
            if (type.IsInstanceOfType(node))
            {
                return node;
            }

            var children = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < children; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
        }

        return null;
    }

    private static Assembly? AdWindowsAssembly() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "AdWindows", StringComparison.OrdinalIgnoreCase));

    private static T? GetProp<T>(object obj, string name) where T : class
        => obj.GetType().GetProperty(name)?.GetValue(obj) as T;
}

/// <summary>
/// Loaded automatically when Navisworks starts (EventWatcherPlugins are not lazy, unlike
/// command handlers), which is what lets the tab merge happen before the user ever clicks
/// anything. Also the earliest safe place to start it: the ribbon composes during startup and
/// the merger waits on Idle for it.
/// </summary>
[Autodesk.Navisworks.Api.Plugins.Plugin("Dyncamelo.Startup", "DYNC",
    DisplayName = "Dyncamelo startup")]
public class DyncameloStartup : Autodesk.Navisworks.Api.Plugins.EventWatcherPlugin
{
    /// <inheritdoc />
    public override void OnLoaded() => RibbonTabMerger.Install();

    /// <inheritdoc />
    public override void OnUnloading()
    {
    }
}
