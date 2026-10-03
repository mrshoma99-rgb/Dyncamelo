using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;
using Dyncamelo.TestSupport.StandIns;

namespace Dyncamelo.UI.Tests.Wiki;

/// <summary>Thrown by a scene that cannot be drawn on this machine or yet (a graph file that does not exist, a window the build does not have).</summary>
internal sealed class WikiSkipException : Exception
{
    public WikiSkipException(string message)
        : base(message)
    {
    }
}

/// <summary>Where the pictures go and where their sources are.</summary>
internal static class WikiPaths
{
    /// <summary>The screenshot folder the build machine sets (null when the pictures are not wanted).</summary>
    public static string? OutputFolder()
    {
        var folder = Environment.GetEnvironmentVariable("DYNCAMELO_SCREENSHOT_DIR");
        return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, "wiki");
    }

    public static string RepoRoot() => RepoLocator.Root();

    public static string SamplesDirectory() => Path.Combine(RepoRoot(), "samples");

    /// <summary>The how-to graphs, <c>docs/wiki-src/graphs</c>.</summary>
    public static string GraphsDirectory() => Path.Combine(RepoRoot(), "docs", "wiki-src", "graphs");

}

/// <summary>What every scene draws with: the node registry (general nodes plus the Navisworks stand-ins) and the host stubs a Navisworks session would have.</summary>
internal static class WikiWorld
{
    private static readonly object Gate = new object();
    private static NodeRegistry? _registry;
    private static StandInCatalogue? _standIns;

    /// <summary>The registry of a Navisworks session: Dyncamelo.Nodes plus the stand-ins of the Navisworks nodes.</summary>
    public static NodeRegistry Registry
    {
        get
        {
            Build();
            return _registry!;
        }
    }

    public static StandInCatalogue StandIns
    {
        get
        {
            Build();
            return _standIns!;
        }
    }

    /// <summary>The definition of a library node by its display name (<c>Search.ByProperty</c>).</summary>
    public static NodeDefinition Definition(string name)
    {
        var found = Registry.Definitions.FirstOrDefault(d => d.Name == name);
        if (found == null)
        {
            throw new InvalidOperationException("The node '" + name + "' is not in the library.");
        }

        return found;
    }

    private static void Build()
    {
        lock (Gate)
        {
            if (_registry != null)
            {
                return;
            }

            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var standIns = StandInCatalogue.Create(registry);
            standIns.RegisterInto(registry);
            _standIns = standIns;
            _registry = registry;
        }
    }
}

/// <summary>
/// The model-element picker Navisworks gives the editor, with nothing selected: with one installed the model inputs draw their
/// "Pick from selection" field as they do in Navisworks (without one they are dimmed).
/// </summary>
internal sealed class WikiPicker : IModelPicker
{
    public string? CaptureSelection(bool single, out int count)
    {
        count = 0;
        return null;
    }

    public string Describe(string? value) => string.Empty;

    public bool Reveal(string? value) => false;
}

/// <summary>The two pictures of a scene: in the dark palette and in the light one.</summary>
internal sealed class WikiPictures
{
    public WikiPictures(BitmapSource dark, BitmapSource light)
    {
        Dark = dark;
        Light = light;
    }

    public BitmapSource Dark { get; }

    public BitmapSource Light { get; }
}

/// <summary>
/// Everything one scene needs, and the windows it opened (closed when the scene ends). A scene is built in the dark palette; the light
/// picture is taken from the same window after the palette is switched, the way a user switches it in the settings (so the two
/// pictures differ in colour only).
/// </summary>
internal sealed class WikiContext : IDisposable
{
    /// <summary>The palette ids, dark first.</summary>
    public const string DarkPalette = "DyncameloDark";

    public const string LightPalette = "Light";

    private readonly List<Window> _windows = new List<Window>();
    private readonly List<Action> _cleanups = new List<Action>();
    private readonly string _temp;

    public WikiContext(string palette = DarkPalette)
    {
        Palette = palette;
        _temp = Path.Combine(Path.GetTempPath(), "dyc-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    /// <summary>The palette the scene is built in: <c>DyncameloDark</c>.</summary>
    public string Palette { get; }

    public NodeRegistry Registry => WikiWorld.Registry;

    /// <summary>Things a scene found wrong with its sources (an unresolved node in a graph file); the picture is still drawn, the test then fails with the list.</summary>
    public List<string> Problems { get; } = new List<string>();

    /// <summary>A folder of this scene's own, deleted with it.</summary>
    public string TempFolder => _temp;

    /// <summary>A path inside <see cref="TempFolder"/>.</summary>
    public string TempPath(string name) => Path.Combine(_temp, name);

    /// <summary>Remembers a window so it is closed when the scene ends.</summary>
    public void Track(Window window) => _windows.Add(window);

    /// <summary>Remembers something to do when the scene ends (before its windows are closed).</summary>
    public void OnDispose(Action cleanup) => _cleanups.Add(cleanup);

    /// <summary>Takes the picture in the dark palette, switches the editor to the light one, takes it again and switches back.</summary>
    public WikiPictures Both(EditorRig rig, Func<BitmapSource> take) => Both(id => rig.UsePalette(id), take);

    /// <summary>The same for anything that can switch its palette: <paramref name="usePalette"/> is called with the palette id.</summary>
    public WikiPictures Both(Action<string> usePalette, Func<BitmapSource> take)
    {
        var dark = take();
        usePalette(LightPalette);
        BitmapSource light;
        try
        {
            light = take();
        }
        finally
        {
            usePalette(DarkPalette);
        }

        return new WikiPictures(dark, light);
    }

    /// <summary>Gives up on the scene with a message (it is listed, but is not a failure).</summary>
    public void Skip(string message) => throw new WikiSkipException(message);

    public void Dispose()
    {
        foreach (var cleanup in _cleanups)
        {
            try
            {
                cleanup();
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // A scene is over; a failing clean-up must not hide the scene's own result.
            }
        }

        _cleanups.Clear();
        foreach (var window in _windows)
        {
            try
            {
                window.Close();
            }
            catch (InvalidOperationException)
            {
                // Already closed.
            }
        }

        _windows.Clear();
        try
        {
            Directory.Delete(_temp, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Waiting for WPF from code that is already running on its thread.</summary>
internal static class WikiUi
{
    /// <summary>Lets pending layout, binding, rendering and idle work finish.</summary>
    public static void Settle(int rounds = 3)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        for (var i = 0; i < rounds; i++)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    /// <summary>Keeps the dispatcher running for a while (for the debounce timers of the editor).</summary>
    public static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Settle(1);
    }

    /// <summary>Runs an action with the culture the pictures are drawn in.</summary>
    public static void WithCulture(Action action)
    {
        var thread = System.Threading.Thread.CurrentThread;
        var culture = thread.CurrentCulture;
        var uiCulture = thread.CurrentUICulture;
        try
        {
            thread.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            thread.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            action();
        }
        finally
        {
            thread.CurrentCulture = culture;
            thread.CurrentUICulture = uiCulture;
        }
    }

    /// <summary>All visual descendants of a given type.</summary>
    public static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>The nearest visual ancestor of a given type, or null.</summary>
    public static T? AncestorOfType<T>(DependencyObject start)
        where T : DependencyObject
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(start);
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
