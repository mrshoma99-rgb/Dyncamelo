using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Groups;
using Dyncamelo.Core.Nodes;
using Dyncamelo.TestSupport.Wiki;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;

namespace Dyncamelo.UI.Tests.Wiki;

/// <summary>The pictures of the editor itself: whole windows and close-ups, made from the sample graphs or from small graphs built for the purpose.</summary>
internal static class EditorScenes
{
    private const double WindowWidth = 1280d;
    private const double WindowHeight = 720d;

    private static readonly string[] TableSummaryResultNodes =
    {
        "Table.GroupBy", "Sort by", "Largest total first", "Largest first", "Totals by category", "Table.ToText", "As Markdown",
    };

    private static readonly string[] LacingFirstBranch =
    {
        "Start A", "End A", "Range A", "Start B", "End B", "Step B", "Range B", "Add (Shortest)", "Shortest",
    };

    private static readonly string[] LacingModeNodes =
    {
        "Range A", "Range B", "Add (Shortest)", "Add (Longest)", "Add (Cross Product)", "Shortest", "Longest", "Cross Product",
    };

    public static IEnumerable<WikiScene> Fixed()
    {
        yield return new WikiScene("wiki-start-screen", "existing test, start screen", StartScreen);
        yield return new WikiScene("wiki-editor-overview", "sample Table Summary from Text, run", EditorOverview);
        yield return new WikiScene("wiki-node-anatomy", "code", NodeAnatomy);
        yield return new WikiScene("wiki-socket-kinds", "code", SocketKinds);
        yield return new WikiScene("wiki-socket-shapes", "code", SocketShapes);
        yield return new WikiScene("wiki-replication", "sample list-lacing, run", Replication);
        yield return new WikiScene("wiki-lacing-modes", "sample list-lacing, run", LacingModes);
        yield return new WikiScene("wiki-first-script", "graph first-script", FirstScript);
        yield return new WikiScene("wiki-first-script-search", "graph first-script, cropped", FirstScriptSearch);
        yield return new WikiScene("wiki-magnifier", "code, stand-in catalogue", Magnifier);
        yield return new WikiScene("wiki-library-search", "code", LibrarySearch);
        yield return new WikiScene("wiki-errors-and-warnings", "code, run", ErrorsAndWarnings);
        yield return new WikiScene("wiki-problems-list", "code, run", ProblemsList);
        yield return new WikiScene("wiki-mute-freeze", "code", MuteAndFreeze);
        yield return new WikiScene("wiki-node-group-instance", "code", NodeGroupInstance);
        yield return new WikiScene("wiki-node-group-open", "code", NodeGroupOpen);
        yield return new WikiScene("wiki-run-progress", "code", RunProgressOverlay);
        yield return new WikiScene("wiki-undo-history", "code", UndoHistory);
        yield return new WikiScene("wiki-minimap", "code", Minimap);
        yield return new WikiScene("wiki-watch-table", "sample Table Summary from Text, run, cropped", WatchTable);
        yield return new WikiScene("wiki-colour-popup", "code", ColourPopup);
        foreach (var section in new[] { "Appearance", "Canvas", "Editing", "Privacy" })
        {
            var name = section;
            yield return new WikiScene("wiki-settings-" + name.ToLowerInvariant(), "code", ctx => SettingsPage(ctx, name));
        }

        yield return new WikiScene("wiki-shortcuts", "existing test, F1 sheet", ctx => OverviewWith(ctx, vm => vm.IsHelpOpen = true));
        yield return new WikiScene("wiki-command-palette", "existing test, command palette", ctx => OverviewWith(ctx, vm =>
        {
            vm.OpenPalette();
            vm.PaletteQuery = "arrange";
        }));
        yield return new WikiScene("wiki-quick-search", "existing test, Space quick search", ctx => OverviewWith(ctx, vm =>
        {
            vm.OpenQuickSearch(new Point(0, 0));
            vm.QuickSearchText = "table";
        }));
    }

    // ----- helpers --------------------------------------------------------------------------------

    private static string Sample(string name) => Path.Combine(WikiPaths.SamplesDirectory(), name + ".dyc");

    private static EditorRig OpenSample(WikiContext ctx, string sampleName, double width, double height, Action<GraphEditorViewModel>? prepare = null)
    {
        var path = Sample(sampleName);
        if (!File.Exists(path))
        {
            ctx.Skip("samples/" + sampleName + ".dyc does not exist");
        }

        var graph = EditorRig.ReadGraph(ctx, path, out var warnings);
        foreach (var warning in warnings)
        {
            ctx.Problems.Add(sampleName + ": " + warning);
        }

        foreach (var missing in GraphScenes.Unresolved(graph))
        {
            ctx.Problems.Add(sampleName + " has a node that did not resolve: " + missing);
        }

        return EditorRig.Open(ctx, width, height, graph, prepare);
    }

    private static void NoMinimap(GraphEditorViewModel vm) => vm.MinimapMode = "off";

    private static List<CanvasItemViewModel> Items(EditorRig rig, IEnumerable<string> names) => names.Select(n => (CanvasItemViewModel)rig.Node(n)).ToList();

    /// <summary>The nodes in the first few rows of the many-nodes graph: a part of it, so the minimap shows more than the window does.</summary>
    private static List<CanvasItemViewModel> FirstRows(EditorRig rig) =>
        rig.Nodes.Where(n => n.Model.Y < 3 * 190d).Cast<CanvasItemViewModel>().ToList();

    // ----- whole windows --------------------------------------------------------------------------

    /// <summary>The start screen of an empty canvas: New, recent scripts, the examples, the version chip and the update notice.</summary>
    private static WikiPictures StartScreen(WikiContext ctx)
    {
        var scripts = ctx.TempPath("Clash Coordination");
        Directory.CreateDirectory(scripts);
        var settings = new UiSettingsService(ctx.TempPath("start-settings.json"));
        foreach (var name in new[] { "csv-roundtrip", "Table Summary from Text", "string-report" })
        {
            var source = Sample(name);
            if (!File.Exists(source))
            {
                ctx.Skip("samples/" + name + ".dyc does not exist");
            }

            var copy = Path.Combine(scripts, name + ".dyc");
            File.Copy(source, copy);
            settings.AddRecentFile(copy);
        }

        // The notice names the release after the one that is running, so the picture never shows a version that is not newer.
        var version = typeof(GraphEditorViewModel).Assembly.GetName().Version ?? new Version(0, 1, 0);
        var newer = new Version(version.Major, version.Minor + 1, 0).ToString(3);
        var rig = EditorRig.Open(ctx, WindowWidth, WindowHeight, null, vm =>
        {
            vm.SamplesDirectoryOverride = WikiPaths.SamplesDirectory();
            vm.RefreshSampleGraphs();
            vm.SetAvailableUpdate(newer, "https://github.com/mrshoma99-rgb/dyncamelo/releases/latest");
        }, settings);
        return ctx.Both(rig, () => rig.Capture());
    }

    /// <summary>The Table Summary from Text sample after a run, with the library open: the base of several pictures.</summary>
    private static EditorRig TableSummaryRun(WikiContext ctx, double width, double height)
    {
        var rig = OpenSample(ctx, "Table Summary from Text", width, height);
        GraphScenes.RunInQuietFolder(ctx, rig);
        return rig;
    }

    private static WikiPictures EditorOverview(WikiContext ctx)
    {
        var rig = TableSummaryRun(ctx, WindowWidth, WindowHeight);
        rig.FitNodes(0d, 1.0d, TableSummaryResultNodes);
        rig.ClearSelection();
        return ctx.Both(rig, () => rig.Capture());
    }

    /// <summary>The overview with a panel opened over it (the F1 sheet, the command palette, the quick search).</summary>
    private static WikiPictures OverviewWith(WikiContext ctx, Action<GraphEditorViewModel> open)
    {
        var rig = TableSummaryRun(ctx, WindowWidth, WindowHeight);
        rig.FitNodes(0d, 1.0d, TableSummaryResultNodes);
        rig.ClearSelection();
        open(rig.Vm);
        rig.Settle();
        return ctx.Both(rig, () => rig.Capture());
    }

    /// <summary>One page of the settings, over a small graph (the page is not quite opaque).</summary>
    private static WikiPictures SettingsPage(WikiContext ctx, string section)
    {
        var rig = OpenSample(ctx, "hello-math", WindowWidth, WindowHeight, NoMinimap);
        rig.FitAll(0d, 1.0d);
        rig.Vm.OpenSettings(null);
        rig.Vm.SettingsSection = section;
        rig.Settle();
        return ctx.Both(rig, () => rig.CaptureCanvas());
    }

    // ----- one node, close up ---------------------------------------------------------------------

    private static WikiPictures NodeAnatomy(WikiContext ctx)
    {
        var rig = EditorRig.Open(ctx, 900d, 760d, SceneGraphs.Anatomy(ctx.Registry), NoMinimap);
        var node = rig.Nodes.Single();
        rig.Fit(rig.GraphBounds(new[] { (CanvasItemViewModel)node }), 0d, 1.5d);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(node), 28d));
    }

    private static WikiPictures SocketKinds(WikiContext ctx)
    {
        var rig = EditorRig.Open(ctx, 1000d, 560d, SceneGraphs.SocketKinds(), vm =>
        {
            NoMinimap(vm);
            vm.ColourBlindGlyphs = true;
        });
        rig.FitAll(0d, 1.3d);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(rig.Vm.Items), 30d));
    }

    private static WikiPictures SocketShapes(WikiContext ctx)
    {
        var rig = EditorRig.Open(ctx, 1100d, 640d, SceneGraphs.SocketShapes(ctx.Registry), NoMinimap);
        rig.Run();
        rig.FitAll(0d, 1.3d);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(rig.Vm.Items), 30d));
    }

    // ----- the lacing sample -----------------------------------------------------------------------

    private static WikiPictures Replication(WikiContext ctx)
    {
        var rig = OpenSample(ctx, "list-lacing", 1400d, 760d, NoMinimap);
        GraphScenes.RunInQuietFolder(ctx, rig);
        rig.FitNodes(0d, 1.3d, LacingFirstBranch);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(Items(rig, LacingFirstBranch)), 24d));
    }

    private static WikiPictures LacingModes(WikiContext ctx)
    {
        var rig = OpenSample(ctx, "list-lacing", 1500d, 860d, NoMinimap);
        GraphScenes.RunInQuietFolder(ctx, rig);
        rig.FitNodes(0d, 1.2d, LacingModeNodes);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(Items(rig, LacingModeNodes)), 24d));
    }

    // ----- the first script ------------------------------------------------------------------------

    private static string FirstScriptPath(WikiContext ctx)
    {
        var path = Path.Combine(WikiPaths.GraphsDirectory(), "first-script.dyc");
        if (!File.Exists(path))
        {
            ctx.Skip("docs/wiki-src/graphs/first-script.dyc does not exist (yet)");
        }

        return path;
    }

    private static WikiPictures FirstScript(WikiContext ctx) => GraphScenes.DrawFile(ctx, FirstScriptPath(ctx), runIfGeneral: false);

    private static WikiPictures FirstScriptSearch(WikiContext ctx)
    {
        var path = FirstScriptPath(ctx);
        var graph = EditorRig.ReadGraph(ctx, path, out _);
        foreach (var missing in GraphScenes.Unresolved(graph))
        {
            ctx.Problems.Add("first-script.dyc has a node that did not resolve: " + missing);
        }

        var rig = EditorRig.Open(ctx, 1000d, 700d, graph, NoMinimap);
        var search = rig.Nodes.FirstOrDefault(n => n.Model is Dyncamelo.Core.Loader.ZeroTouchNodeModel z && z.Definition.Name == "Search.ByProperty");
        if (search == null)
        {
            throw new InvalidOperationException("first-script.dyc has no Search.ByProperty node.");
        }

        rig.Fit(rig.GraphBounds(new[] { (CanvasItemViewModel)search }), 0d, 1.5d);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(search), 28d));
    }

    // ----- the magnifier -----------------------------------------------------------------------------

    /// <summary>What the host's property catalogue lists for a tab search: a few tab names, as Navisworks would for a wall.</summary>
    private sealed class SampleTabs : IModelPropertyCatalog
    {
        public ModelDataListing List(object? source, ModelDataChoice choice, string? tab)
        {
            var names = choice.Kind == ModelDataKind.Tab
                ? new[] { "Element", "Element ID", "Item", "Material" }
                : new[] { "Category", "Family", "Level", "Type" };
            return ModelDataListing.Union(new[] { names.AsEnumerable() }, 1);
        }
    }

    /// <summary>A picker that has "a wall" selected, so a model input shows what it holds.</summary>
    private sealed class OneWallPicker : IModelPicker
    {
        public string? CaptureSelection(bool single, out int count)
        {
            count = 1;
            return "nw:0:1";
        }

        public string Describe(string? value) => string.IsNullOrEmpty(value) ? string.Empty : "Basic Wall: Concrete 200mm";

        public bool Reveal(string? value) => true;
    }

    private static WikiPictures Magnifier(WikiContext ctx)
    {
        var previousPicker = ModelPickerHost.Current;
        var previousCatalog = ModelPropertyHost.Current;
        try
        {
            ModelPickerHost.Current = new OneWallPicker();
            ModelPropertyHost.Current = new SampleTabs();
            var rig = EditorRig.Open(ctx, 900d, 600d, SceneGraphs.PropertyValue(ctx.Registry), NoMinimap);
            var node = rig.Nodes.Single();
            var item = node.Inputs.Single(c => c.Port.Name == "item");
            var tab = node.Inputs.Single(c => c.Port.Name == "categoryName");
            item.Port.SetUserValue("nw:0:1");
            rig.Place(rig.GraphBounds(new[] { (CanvasItemViewModel)node }), 1.4d, new Point(60d, 40d));
            rig.Settle();

            // The button reads the tabs of the node's element and fills the list the popup shows; the list is drawn in the editor,
            // because a popup is a window of its own that a picture of the editor does not contain.
            tab.SearchDataCommand.Execute(null);
            tab.IsDataListOpen = false;
            rig.Settle();

            var button = WikiUi.Descendants<Button>(rig.Control)
                .FirstOrDefault(b => b.ToolTip is string tip && tip.StartsWith("Choose from the ", StringComparison.Ordinal));
            if (button == null)
            {
                throw new InvalidOperationException("The search button of the tab input was not found.");
            }

            var field = WikiUi.AncestorOfType<DockPanel>(button) ?? throw new InvalidOperationException("The field of the tab input was not found.");
            var fieldRect = field.TransformToAncestor(rig.Control).TransformBounds(new Rect(0d, 0d, field.ActualWidth, field.ActualHeight));
            var list = new ContentControl
            {
                Content = tab,
                ContentTemplate = (DataTemplate)rig.Control.FindResource("Editor.DataChoiceList"),
                Margin = new Thickness(0d, 2d, 8d, 8d),
                Focusable = false,
            };
            rig.AddOverlay(list, new Point(fieldRect.Left, fieldRect.Bottom));
            rig.ClearSelection();

            return ctx.Both(rig, () =>
            {
                var listRect = list.TransformToAncestor(rig.Control).TransformBounds(new Rect(0d, 0d, list.ActualWidth, list.ActualHeight));
                return EditorRig.Crop(rig.Capture(), Rect.Union(rig.RectOf(node), listRect), 24d);
            });
        }
        finally
        {
            ModelPickerHost.Current = previousPicker;
            ModelPropertyHost.Current = previousCatalog;
        }
    }

    // ----- the library ---------------------------------------------------------------------------------

    /// <summary>The library panel twice, side by side: the categories with their icons, and the results of searching for "clash".</summary>
    private static WikiPictures LibrarySearch(WikiContext ctx)
    {
        var rig = EditorRig.Open(ctx, WindowWidth, WindowHeight, null);
        var panel = (FrameworkElement)rig.Control.FindName("LibraryPanel");
        rig.Settle();
        var panelRect = panel.TransformToAncestor(rig.Control).TransformBounds(new Rect(0d, 0d, panel.ActualWidth, panel.ActualHeight));

        return ctx.Both(rig, () =>
        {
            rig.Vm.Library.SearchText = string.Empty;
            rig.Settle();
            var tree = EditorRig.Crop(rig.Capture(), panelRect, 0d);

            rig.Vm.Library.SearchText = "clash";
            WikiUi.Pump(450);          // the search starts a moment after the last key
            rig.Settle();
            var results = EditorRig.Crop(rig.Capture(), panelRect, 0d);

            var background = (Brush)rig.Control.FindResource("Dyc.CanvasBrush");
            return Compose.SideBySide(tree, results, 18d, background);
        });
    }

    // ----- errors, warnings, problems ---------------------------------------------------------------------

    private static EditorRig ErrorsRig(WikiContext ctx)
    {
        var rig = EditorRig.Open(ctx, WindowWidth, WindowHeight, SceneGraphs.ErrorsAndWarnings(ctx.Registry), NoMinimap);
        rig.Run();
        rig.FitAll(0d, 1.1d);
        rig.ClearSelection();
        return rig;
    }

    private static WikiPictures ErrorsAndWarnings(WikiContext ctx)
    {
        var rig = ErrorsRig(ctx);
        return ctx.Both(rig, () => rig.Capture());
    }

    private static WikiPictures ProblemsList(WikiContext ctx)
    {
        var rig = ErrorsRig(ctx);
        rig.Vm.ToggleProblemsCommand.Execute(null);
        rig.Settle();
        return ctx.Both(rig, () => rig.Capture());
    }

    // ----- mute and freeze ----------------------------------------------------------------------------------

    private static WikiPictures MuteAndFreeze(WikiContext ctx)
    {
        var rig = EditorRig.Open(ctx, 1280d, 600d, SceneGraphs.MuteAndFreeze(ctx.Registry), NoMinimap);
        rig.Run();
        rig.Node("Side (frozen)").IsFrozen = true;
        rig.Settle();
        rig.FitAll(0d, 1.2d);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(rig.Vm.Items), 24d));
    }

    // ----- node groups -----------------------------------------------------------------------------------------

    private static EditorRig GroupRig(WikiContext ctx, out GroupInstanceNode instance)
    {
        var graph = SceneGraphs.NodeGroup(ctx.Registry, "Add allowance", out instance);
        var rig = EditorRig.Open(ctx, 1280d, 560d, graph, NoMinimap);
        rig.Run();
        return rig;
    }

    private static WikiPictures NodeGroupInstance(WikiContext ctx)
    {
        var rig = GroupRig(ctx, out _);
        rig.FitAll(0d, 1.2d);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(rig.Vm.Items), 24d));
    }

    private static WikiPictures NodeGroupOpen(WikiContext ctx)
    {
        var rig = GroupRig(ctx, out var instance);
        var instanceView = rig.Vm.Items.OfType<NodeViewModel>().Single(n => n.Model == instance);
        if (!rig.Vm.EnterGroup(instanceView))
        {
            throw new InvalidOperationException("The node group could not be opened.");
        }

        rig.Settle();
        WikiUi.Pump(150);            // the view fits the group's body a moment after it opens
        rig.Settle();
        rig.ClearSelection();
        return ctx.Both(rig, () => rig.CaptureCanvas());
    }

    // ----- a run in progress ----------------------------------------------------------------------------------------

    private static WikiPictures RunProgressOverlay(WikiContext ctx)
    {
        var graph = SceneGraphs.ManyNodes(ctx.Registry);
        var rig = EditorRig.Open(ctx, WindowWidth, WindowHeight, graph);
        rig.Fit(rig.GraphBounds(FirstRows(rig)), 0d, 1.0d);
        rig.ClearSelection();

        // A run blocks the window, so the picture is taken in the state a run puts the editor in: the overlay with its progress line.
        var progress = new RunProgress(11, graph.Nodes.Count, "Scale 3", new string[0]);
        try
        {
            EditorRig.SetPrivate(rig.Vm, nameof(GraphEditorViewModel.RunProgressText), "Running " + progress.Describe());
            EditorRig.SetPrivate(rig.Vm, nameof(GraphEditorViewModel.IsRunning), true);
            rig.Settle();
            return ctx.Both(rig, () => rig.Capture());
        }
        finally
        {
            EditorRig.SetPrivate(rig.Vm, nameof(GraphEditorViewModel.IsRunning), false);
        }
    }

    // ----- undo history ---------------------------------------------------------------------------------------------

    private static WikiPictures UndoHistory(WikiContext ctx)
    {
        var rig = EditorRig.Open(ctx, WindowWidth, WindowHeight, null, vm => vm.History.CoalesceWindow = TimeSpan.Zero);
        var vm2 = rig.Vm;
        vm2.History.Clear();

        var number = vm2.AddNode("NumberInput", new Point(0d, 0d)) ?? throw new InvalidOperationException("The Number node was not added.");
        var add = vm2.AddNode(WikiWorld.Definition("Add").Id, new Point(330d, 0d)) ?? throw new InvalidOperationException("The Add node was not added.");
        Connect(vm2, number.Model.OutPorts[0], add.Model.InPorts.Single(p => p.Name == "a"));
        add.Inputs.Single(c => c.Port.Name == "b").NumberValue = 10d;
        var watch = vm2.AddNode(WatchNode.TypeName, new Point(660d, 0d)) ?? throw new InvalidOperationException("The Watch node was not added.");
        Connect(vm2, add.Model.OutPorts[0], watch.Model.InPorts[0]);

        // Two steps back, so the last two stand dimmed in the list: they can still be brought back.
        vm2.UndoCommand.Execute(null);
        vm2.UndoCommand.Execute(null);
        vm2.ToggleHistoryCommand.Execute(null);
        rig.Settle();
        rig.FitAll(0d, 1.0d);
        rig.ClearSelection();
        return ctx.Both(rig, () => rig.Capture());
    }

    private static void Connect(GraphEditorViewModel vm, Dyncamelo.Core.Graph.PortModel from, Dyncamelo.Core.Graph.PortModel to)
    {
        var result = vm.Graph.Connect(from, to);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    // ----- the minimap ----------------------------------------------------------------------------------------------------

    private static WikiPictures Minimap(WikiContext ctx)
    {
        var rig = EditorRig.Open(ctx, WindowWidth, WindowHeight, SceneGraphs.ManyNodes(ctx.Registry));
        rig.Run();
        rig.Fit(rig.GraphBounds(FirstRows(rig)), 0d, 1.0d);
        rig.ClearSelection();
        return ctx.Both(rig, () => rig.CaptureCanvas());
    }

    // ----- a watch table, close up ---------------------------------------------------------------------------------------------

    private static WikiPictures WatchTable(WikiContext ctx)
    {
        var rig = TableSummaryRun(ctx, 1200d, 760d);
        var table = rig.Node("Totals by category");
        rig.Fit(rig.GraphBounds(new[] { (CanvasItemViewModel)table }), 0d, 1.4d);
        rig.ClearSelection();
        return ctx.Both(rig, () => EditorRig.Crop(rig.Capture(), rig.RectOf(table), 20d));
    }

    // ----- the colour popup ----------------------------------------------------------------------------------------------------------

    private static WikiPictures ColourPopup(WikiContext ctx)
    {
        var graph = EditorRig.GraphOf(ctx, "Colour", sketch =>
        {
            var node = sketch.Library("Appearance.OverrideColor", 0, 0);
            sketch.Set(node, "color", "#FFE63C3C");
        });
        var rig = EditorRig.Open(ctx, 900d, 720d, graph, NoMinimap);
        var onCanvas = rig.Nodes.Single();
        rig.Place(rig.GraphBounds(new[] { (CanvasItemViewModel)onCanvas }), 1.4d, new Point(60d, 40d));
        rig.ClearSelection();

        // The popup is a window of its own that a picture of the editor does not contain: its content is shown in the editor instead.
        var picker = WikiUi.Descendants<ColorSwatchPicker>(rig.Control).FirstOrDefault()
            ?? throw new InvalidOperationException("The colour input was not found on the node.");
        var swatchRect = picker.TransformToAncestor(rig.Control).TransformBounds(new Rect(0d, 0d, picker.ActualWidth, picker.ActualHeight));
        var frame = Popups.TakeColourPickerFrame(picker);
        rig.AddOverlay(frame, new Point(swatchRect.Left, swatchRect.Bottom));

        return ctx.Both(rig, () =>
        {
            var frameRect = frame.TransformToAncestor(rig.Control).TransformBounds(new Rect(0d, 0d, frame.ActualWidth, frame.ActualHeight));
            return EditorRig.Crop(rig.Capture(), Rect.Union(rig.RectOf(onCanvas), frameRect), 24d);
        });
    }
}
