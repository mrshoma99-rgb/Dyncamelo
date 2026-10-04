using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace CamelGraph.UI.Tests.Wiki;

/// <summary>One picture of the wiki: its id (the file name without extension), where its content comes from, and how to draw it.</summary>
internal sealed class WikiScene
{
    public WikiScene(string id, string source, Func<WikiContext, WikiPictures> draw)
    {
        Id = id;
        Source = source;
        Draw = draw;
    }

    /// <summary>The image id of <c>tools/wiki/image-manifest.md</c>: <c>docs/images/&lt;id&gt;.png</c> and <c>&lt;id&gt;-light.png</c>.</summary>
    public string Id { get; }

    /// <summary>The Source column of the manifest ("code", "sample list-lacing, run", ...), for the log.</summary>
    public string Source { get; }

    /// <summary>Draws the pictures (dark and light) on the WPF thread.</summary>
    public Func<WikiContext, WikiPictures> Draw { get; }

    public override string ToString() => Id;

    /// <summary>The file name of a picture in the dark palette (<c>wiki-x.png</c>) or the light one (<c>wiki-x-light.png</c>).</summary>
    public string FileName(bool light) => Id + (light ? "-light" : string.Empty) + ".png";
}

/// <summary>The pictures the wiki shows: the ones named in the manifest, one for every sample graph and one for every how-to graph.</summary>
internal static class WikiScenes
{
    /// <summary>Every scene, in the order they are drawn.</summary>
    public static IReadOnlyList<WikiScene> All()
    {
        var scenes = new List<WikiScene>();
        scenes.AddRange(EditorScenes.Fixed());
        scenes.AddRange(PlayerScenes.Fixed());
        scenes.AddRange(GraphScenes.Samples());
        scenes.AddRange(GraphScenes.HowTos());
        return scenes;
    }

    /// <summary>The image ids the manifest lists in its table of scenes (the generic sample and graph rows are left out).</summary>
    public static IReadOnlyList<string> ManifestIds() => CamelGraph.TestSupport.Wiki.Manifest.ImageIds();

    /// <summary>The names of the how-to graphs in the manifest's table.</summary>
    public static IReadOnlyList<string> ManifestGraphNames() => CamelGraph.TestSupport.Wiki.Manifest.GraphNames();
}
