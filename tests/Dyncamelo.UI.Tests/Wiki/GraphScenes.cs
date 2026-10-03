using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Serialization;
using Dyncamelo.UI.ViewModels;

namespace Dyncamelo.UI.Tests.Wiki;

/// <summary>
/// The pictures of whole graphs: one for every file in <c>samples/</c> (<c>wiki-sample-&lt;kebab-name&gt;</c>) and one for every how-to graph
/// (<c>wiki-graph-&lt;name&gt;</c>, files in <c>docs/wiki-src/graphs</c>). Each is the node canvas of the real editor, fitted to the graph at a readable zoom in
/// a picture whose shape follows the graph's; the graph is as it opens, without a model, unless it is a sample made of general nodes only, which is run.
/// </summary>
internal static class GraphScenes
{
    /// <summary>The zoom the pictures aim at (nodes are drawn in full from 0.6 up).</summary>
    private const double TargetZoom = 0.9d;

    private const double MinCanvasWidth = 760d;
    private const double MaxCanvasWidth = 2300d;
    private const double MinCanvasHeight = 380d;
    private const double MaxCanvasHeight = 1500d;
    private const double CanvasPadding = 36d;

    public static IEnumerable<WikiScene> Samples()
    {
        var folder = WikiPaths.SamplesDirectory();
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var path in Directory.GetFiles(folder, "*.dyc").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var file = path;
            var name = Path.GetFileNameWithoutExtension(path);
            yield return new WikiScene(Dyncamelo.TestSupport.Wiki.Manifest.SampleImageId(name), "sample " + name, ctx => DrawFile(ctx, file, runIfGeneral: true));
        }
    }

    public static IEnumerable<WikiScene> HowTos()
    {
        var folder = WikiPaths.GraphsDirectory();
        var names = new List<string>(WikiScenes.ManifestGraphNames());
        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.GetFiles(folder, "*.dyc").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        foreach (var name in names)
        {
            var graphName = name;
            yield return new WikiScene("wiki-graph-" + graphName, "graph " + graphName, ctx =>
            {
                var path = Path.Combine(folder, graphName + ".dyc");
                if (!File.Exists(path))
                {
                    ctx.Skip("docs/wiki-src/graphs/" + graphName + ".dyc does not exist (yet)");
                }

                return DrawFile(ctx, path, runIfGeneral: false);
            });
        }
    }

    /// <summary>True when the graph uses a node that needs Navisworks (a Navisworks definition or the Captured Selection input).</summary>
    public static bool NeedsNavisworks(GraphModel graph)
    {
        return graph.Nodes.Any(n =>
            (n is ZeroTouchNodeModel zero && zero.Definition.AssemblyName == "Dyncamelo.Navisworks") ||
            n.NodeType == "CapturedSelection");
    }

    /// <summary>Names the nodes a graph file could not bring in (the picture would show red placeholders).</summary>
    public static IReadOnlyList<string> Unresolved(GraphModel graph)
    {
        return graph.Nodes.OfType<MissingNodeModel>().Select(n => "'" + n.Name + "': " + n.Reason).ToList();
    }

    /// <summary>Draws the canvas of a graph file.</summary>
    public static WikiPictures DrawFile(WikiContext context, string path, bool runIfGeneral)
    {
        var graph = EditorRig.ReadGraph(context, path, out var warnings);
        var fileName = Path.GetFileName(path);
        foreach (var problem in Unresolved(graph))
        {
            context.Problems.Add(fileName + " has a node that did not resolve: " + problem);
        }

        foreach (var warning in warnings)
        {
            context.Problems.Add(fileName + ": " + warning);
        }

        var run = runIfGeneral && !NeedsNavisworks(graph);

        // First a roomy editor, to measure the graph (after the run, which makes watch nodes taller); then one of the right shape.
        Rect bounds;
        double chromeWidth;
        double chromeHeight;
        using (var probe = new WikiContext())
        {
            var probeRig = EditorRig.Open(probe, 1300d, 800d, EditorRig.ReadGraph(probe, path, out _), vm => vm.MinimapMode = "off");
            if (run)
            {
                RunInQuietFolder(probe, probeRig);
            }

            bounds = probeRig.GraphBounds();
            chromeWidth = probeRig.Width - probeRig.CanvasRect.Width;
            chromeHeight = probeRig.Height - probeRig.CanvasRect.Height;
        }

        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException(fileName + " has no nodes to draw.");
        }

        var canvasWidth = Clamp(Math.Ceiling(bounds.Width * TargetZoom + 2d * CanvasPadding), MinCanvasWidth, MaxCanvasWidth);
        var canvasHeight = Clamp(Math.Ceiling(bounds.Height * TargetZoom + 2d * CanvasPadding), MinCanvasHeight, MaxCanvasHeight);

        var rig = EditorRig.Open(context, canvasWidth + chromeWidth, canvasHeight + chromeHeight, graph, vm => vm.MinimapMode = "off");
        if (run)
        {
            RunInQuietFolder(context, rig);
        }

        rig.FitAll(0d, 1.25d);
        rig.ClearSelection();
        return context.Both(rig, () => rig.CaptureCanvas());
    }

    /// <summary>Runs the graph with a folder of its own as the working directory (the CSV sample writes a file next to it).</summary>
    public static void RunInQuietFolder(WikiContext context, EditorRig rig)
    {
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = context.TempFolder;
            rig.Run();
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
