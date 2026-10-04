using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Serialization;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;
using Nodify;

namespace CamelGraph.UI.Tests.Wiki;

/// <summary>
/// The real editor control in a real window, at a fixed size on a canvas (the same way the other screenshot tests set it up), with the
/// helpers the scenes need: loading a graph without running it, running one, framing, finding a node's rectangle, taking and cropping the picture.
/// </summary>
internal sealed class EditorRig
{
    /// <summary>The run time shown for a run, so a picture never depends on how fast the machine was.</summary>
    private const double FixedRunMilliseconds = 12d;

    private EditorRig(GraphEditorViewModel vm, CamelGraphEditorControl control, Window window, double width, double height)
    {
        Vm = vm;
        Control = control;
        Window = window;
        Width = width;
        Height = height;
    }

    public GraphEditorViewModel Vm { get; }

    public CamelGraphEditorControl Control { get; }

    public Window Window { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>The node canvas itself (Nodify's editor, without the library, the menus and the status bar).</summary>
    public NodifyEditor Editor => (NodifyEditor)Control.FindName("Editor");

    /// <summary>
    /// Opens an editor of this size. The graph, when given, is loaded as it opens: set to run by hand (so nothing runs by itself) and
    /// with no file behind it (so a run never asks whether to trust the file).
    /// </summary>
    public static EditorRig Open(
        WikiContext context,
        double width,
        double height,
        GraphModel? graph = null,
        Action<GraphEditorViewModel>? prepare = null,
        UiSettingsService? settingsToUse = null)
    {
        var settings = settingsToUse ?? new UiSettingsService(context.TempPath("settings-" + Guid.NewGuid().ToString("N") + ".json"));
        settings.SetPaletteId(context.Palette);
        var vm = new GraphEditorViewModel(context.Registry, new StubDialogs(), settings) { PaletteId = context.Palette };
        vm.CancelPoll = () => false;
        if (graph != null)
        {
            graph.RunType = RunType.Manual;
            vm.LoadGraph(graph);
        }

        prepare?.Invoke(vm);

        var control = new CamelGraphEditorControl { ViewModel = vm, Width = width, Height = height };
        var canvas = new Canvas();
        canvas.Children.Add(control);
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = canvas,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        context.Track(window);
        context.OnDispose(() => vm.EndSession());       // stops the autosave timer of the editor
        window.Show();
        var rig = new EditorRig(vm, control, window, width, height);
        rig.Settle();
        return rig;
    }

    /// <summary>A small graph made on the spot from library nodes.</summary>
    public static GraphModel GraphOf(WikiContext context, string name, Action<CamelGraph.TestSupport.Wiki.Sketch> build)
    {
        var sketch = new CamelGraph.TestSupport.Wiki.Sketch(context.Registry, name);
        build(sketch);
        return sketch.Graph;
    }

    /// <summary>Reads a graph file into a model, the way the editor does, without any node running.</summary>
    public static GraphModel ReadGraph(WikiContext context, string path, out IReadOnlyList<string> warnings)
    {
        var serializer = new GraphSerializer(context.Registry);
        var graph = serializer.Deserialize(File.ReadAllText(path));
        warnings = serializer.LoadWarnings.ToList();
        return graph;
    }

    /// <summary>Switches the editor to another colour palette, as the settings page does.</summary>
    public void UsePalette(string paletteId)
    {
        Vm.PaletteId = paletteId;
        Settle();
    }

    /// <summary>Waits for layout, bindings and rendering.</summary>
    public void Settle()
    {
        WikiUi.Settle();
        Control.UpdateLayout();
        WikiUi.Settle();
    }

    // ----- running -------------------------------------------------------------------------------

    /// <summary>Runs the graph and replaces the figures that depend on the machine (how long it took) with fixed ones.</summary>
    public void Run()
    {
        Vm.IsAutoRun = false;
        Vm.RunGraph();
        FixClock();
        Settle();
    }

    /// <summary>The status bar's "Last run" and the run summary in its message name how long the run took; both are set to a fixed time.</summary>
    public void FixClock()
    {
        SetPrivate(Vm, nameof(GraphEditorViewModel.LastRunMilliseconds), FixedRunMilliseconds);
        var message = Vm.StatusMessage;
        if (message.StartsWith("Run finished", StringComparison.Ordinal))
        {
            var fixedText = Regex.Replace(message, @" in [\d.,]+ ms", " in " + FixedRunMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms");
            fixedText = Regex.Replace(fixedText, @" — slowest:.*$", string.Empty);
            SetPrivate(Vm, nameof(GraphEditorViewModel.StatusMessage), fixedText);
        }
    }

    /// <summary>Sets a property whose setter is private (the status bar text and figures are set by the editor itself).</summary>
    public static void SetPrivate(object target, string property, object value)
    {
        var info = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var setter = info?.GetSetMethod(true);
        if (setter == null)
        {
            throw new InvalidOperationException("The property '" + property + "' of " + target.GetType().Name + " has no setter to use.");
        }

        setter.Invoke(target, new[] { value });
    }

    // ----- finding things -------------------------------------------------------------------------

    /// <summary>The node with this display name (the first one when several share it).</summary>
    public NodeViewModel Node(string name)
    {
        var found = Vm.Items.OfType<NodeViewModel>().FirstOrDefault(n => n.Model.Name == name);
        if (found == null)
        {
            throw new InvalidOperationException("The graph has no node named '" + name + "'.");
        }

        return found;
    }

    public IReadOnlyList<NodeViewModel> Nodes => Vm.Items.OfType<NodeViewModel>().ToList();

    /// <summary>Where a canvas item is in the control's own coordinates, or empty before it has been laid out.</summary>
    public Rect RectOf(CanvasItemViewModel item)
    {
        var container = Editor.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
        if (container == null || container.ActualWidth <= 0d)
        {
            return Rect.Empty;
        }

        return container.TransformToAncestor(Control).TransformBounds(new Rect(0d, 0d, container.ActualWidth, container.ActualHeight));
    }

    /// <summary>The node canvas in the control's own coordinates.</summary>
    public Rect CanvasRect
    {
        get
        {
            var editor = Editor;
            return editor.TransformToAncestor(Control).TransformBounds(new Rect(0d, 0d, editor.ActualWidth, editor.ActualHeight));
        }
    }

    /// <summary>Union of the rectangles (in the control's coordinates) of these items.</summary>
    public Rect RectOf(IEnumerable<CanvasItemViewModel> items)
    {
        var union = Rect.Empty;
        foreach (var item in items)
        {
            var rect = RectOf(item);
            if (!rect.IsEmpty)
            {
                union = Rect.Union(union, rect);
            }
        }

        return union;
    }

    /// <summary>Union of the graph-space rectangles of the items (location and rendered size), for choosing a picture size.</summary>
    public Rect GraphBounds(IEnumerable<CanvasItemViewModel>? items = null)
    {
        var union = Rect.Empty;
        foreach (var item in items ?? Vm.Items)
        {
            var size = item.Size;
            if (size.Width <= 0d || size.Height <= 0d)
            {
                continue;
            }

            union = Rect.Union(union, new Rect(item.Location, size));
        }

        return union;
    }

    // ----- framing --------------------------------------------------------------------------------

    /// <summary>
    /// Fits a rectangle of the graph into the canvas (never closer than <paramref name="maxZoom"/>). Nodify already leaves a margin around
    /// the area; <paramref name="padding"/> adds to it.
    /// </summary>
    public void Fit(Rect graphBounds, double padding = 0d, double maxZoom = 1.25d)
    {
        if (graphBounds.IsEmpty)
        {
            return;
        }

        var area = graphBounds;
        area.Inflate(padding, padding);
        var editor = Editor;
        editor.FitToScreen(area);
        if (editor.ViewportZoom > maxZoom)
        {
            editor.ViewportZoom = maxZoom;
        }

        // Centre without the animation, then give any animation that FitToScreen started time to end before the picture is taken.
        editor.BringIntoView(new Point(area.X + area.Width / 2d, area.Y + area.Height / 2d), false);
        WikiUi.Pump(300);
        Settle();
    }

    /// <summary>
    /// Shows the canvas at a given zoom with the top-left corner of a rectangle of the graph at a given distance from the top-left corner of the
    /// canvas (room below for something that opens under a node).
    /// </summary>
    public void Place(Rect graphBounds, double zoom, Point screenOffset)
    {
        var editor = Editor;
        editor.ViewportZoom = zoom;
        editor.ViewportLocation = new Point(graphBounds.X - screenOffset.X / zoom, graphBounds.Y - screenOffset.Y / zoom);
        WikiUi.Pump(100);
        Settle();
    }

    /// <summary>Fits everything on the canvas.</summary>
    public void FitAll(double padding = 0d, double maxZoom = 1.25d) => Fit(GraphBounds(), padding, maxZoom);

    /// <summary>Fits the nodes with these names.</summary>
    public void FitNodes(double padding, double maxZoom, params string[] names)
    {
        var items = names.Select(n => (CanvasItemViewModel)Node(n)).ToList();
        Fit(GraphBounds(items), padding, maxZoom);
    }

    /// <summary>Clears the selection, which would otherwise draw a highlight on the nodes.</summary>
    public void ClearSelection()
    {
        Vm.SelectedItems.Clear();
        Settle();
    }

    // ----- the picture ----------------------------------------------------------------------------

    /// <summary>The whole control, as a bitmap.</summary>
    public BitmapSource Capture()
    {
        Control.UpdateLayout();
        var width = (int)Math.Ceiling(Control.ActualWidth);
        var height = (int)Math.Ceiling(Control.ActualHeight);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The editor was not laid out (" + width + " x " + height + ").");
        }

        var bitmap = new RenderTargetBitmap(width, height, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(Control);
        return bitmap;
    }

    /// <summary>The node canvas only (no library, menus or status bar).</summary>
    public BitmapSource CaptureCanvas() => Crop(Capture(), CanvasRect, 0d);

    /// <summary>A rectangle of the picture, grown by a margin and kept inside it.</summary>
    public static BitmapSource Crop(BitmapSource source, Rect area, double margin)
    {
        if (area.IsEmpty)
        {
            throw new InvalidOperationException("There is nothing to keep of the picture: the part to crop to has not been laid out.");
        }

        var grown = area;
        grown.Inflate(margin, margin);
        var x = Math.Max(0, (int)Math.Floor(grown.X));
        var y = Math.Max(0, (int)Math.Floor(grown.Y));
        var right = Math.Min(source.PixelWidth, (int)Math.Ceiling(grown.Right));
        var bottom = Math.Min(source.PixelHeight, (int)Math.Ceiling(grown.Bottom));
        if (right <= x || bottom <= y)
        {
            throw new InvalidOperationException("The part of the picture to keep is empty (" + area + ").");
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, right - x, bottom - y));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>Puts an element over the canvas (inside the control, so it has the theme), at a place given in the control's own coordinates.</summary>
    public void AddOverlay(FrameworkElement element, Point controlPoint)
    {
        var host = (Grid)VisualTreeHelper.GetParent(Editor);
        var overlay = host.Children.OfType<Canvas>().FirstOrDefault(c => c.Name == "WikiOverlay");
        if (overlay == null)
        {
            overlay = new Canvas { Name = "WikiOverlay", IsHitTestVisible = false };
            Grid.SetColumnSpan(overlay, 3);
            Panel.SetZIndex(overlay, 5000);
            host.Children.Add(overlay);
        }

        var local = Control.TranslatePoint(controlPoint, host);
        Canvas.SetLeft(element, local.X);
        Canvas.SetTop(element, local.Y);
        overlay.Children.Add(element);
        Settle();
    }
}
