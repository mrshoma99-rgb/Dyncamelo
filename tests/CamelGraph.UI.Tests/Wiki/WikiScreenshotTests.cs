using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using CamelGraph.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace CamelGraph.UI.Tests.Wiki;

/// <summary>
/// What a run of the wiki picture tests shares: the folder the pictures go to, the host stubs a Navisworks session would have, and the report
/// written next to the pictures at the end (<c>wiki/_report.txt</c>: what was drawn, what was skipped and why).
/// </summary>
public sealed class WikiRun : IDisposable
{
    private readonly object _gate = new object();
    private readonly List<string> _lines = new List<string>();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IModelPicker? _previousPicker;
    private bool _installedPicker;
    private int _drawn;
    private int _skipped;
    private int _notDrawn;
    private int _problems;

    public WikiRun()
    {
        Folder = WikiPaths.OutputFolder();
        if (Folder == null)
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        StaHost.Run(() =>
        {
            _previousPicker = ModelPickerHost.Current;
            ModelPickerHost.Current = new WikiPicker();
            _installedPicker = true;
        });
    }

    /// <summary>The folder the pictures go to (<c>wiki</c> inside CAMELGRAPH_SCREENSHOT_DIR), or null when no pictures are wanted.</summary>
    public string? Folder { get; }

    internal void Note(string status, WikiScene scene, string detail)
    {
        lock (_gate)
        {
            _lines.Add(status.PadRight(8) + scene.Id.PadRight(46) + " " + detail + "  [" + scene.Source + "]");
            if (status == "OK")
            {
                _drawn++;
            }
            else if (status == "SKIPPED")
            {
                _skipped++;
            }
            else if (status == "NOTDRAWN")
            {
                _notDrawn++;
            }
            else
            {
                _problems++;
            }
        }
    }

    public void Dispose()
    {
        if (Folder == null)
        {
            return;
        }

        if (_installedPicker)
        {
            StaHost.Run(() => ModelPickerHost.Current = _previousPicker);
        }

        var text = new StringBuilder();
        text.AppendLine("Wiki pictures drawn by tests/CamelGraph.UI.Tests/Wiki (" + _drawn + " scenes drawn, " + _skipped + " skipped, " + _notDrawn + " not drawn, " + _problems + " with a wrong source, " +
                        _clock.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s):");
        text.AppendLine();
        lock (_gate)
        {
            foreach (var line in _lines.OrderBy(l => l, StringComparer.Ordinal))
            {
                text.AppendLine(line);
            }
        }

        File.WriteAllText(Path.Combine(Folder, "_report.txt"), text.ToString(), Encoding.UTF8);
    }
}

/// <summary>
/// Draws the pictures of the wiki (user guide, how-tos) from the real editor: one PNG per scene of <c>tools/wiki/image-manifest.md</c> in the dark
/// palette (<c>wiki/&lt;id&gt;.png</c>) and in the light one (<c>wiki/&lt;id&gt;-light.png</c>), inside the folder named by CAMELGRAPH_SCREENSHOT_DIR, which the
/// Windows build keeps as the artifact <c>editor-screenshots</c>. Does nothing without that variable. Every scene is a test case of its own, so the
/// others are still drawn whatever happens to one. A picture that is missing never turns the build red: a scene whose source graph does not exist
/// yet is skipped, and one that cannot be drawn is marked NOTDRAWN, each with its message in <c>wiki/_report.txt</c>. Only a wrong source fails a
/// test (a node of the graph that did not resolve, a warning on loading it), because that would put a wrong graph into the wiki.
/// </summary>
public class WikiScreenshotTests : IClassFixture<WikiRun>
{
    private static readonly Lazy<IReadOnlyList<WikiScene>> Scenes = new Lazy<IReadOnlyList<WikiScene>>(WikiScenes.All);

    private readonly WikiRun _run;
    private readonly ITestOutputHelper _output;

    public WikiScreenshotTests(WikiRun run, ITestOutputHelper output)
    {
        _run = run;
        _output = output;
    }

    /// <summary>The ids of all the scenes, one test case each.</summary>
    public static IEnumerable<object[]> SceneIds()
    {
        IReadOnlyList<WikiScene> scenes;
        try
        {
            scenes = Scenes.Value;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException || ex is IOException)
        {
            return new List<object[]>();
        }

        return scenes.Select(s => new object[] { s.Id }).ToList();
    }

    [Theory]
    [MemberData(nameof(SceneIds))]
    public void DrawTheScene(string id)
    {
        var folder = _run.Folder;
        if (folder == null)
        {
            return;
        }

        var scene = Scenes.Value.Single(s => s.Id == id);
        var watch = Stopwatch.StartNew();
        var sizes = new List<string>();
        var problems = new List<string>();
        try
        {
            StaHost.Run(() => DrawAndSave(scene, folder, sizes, problems));
        }
        catch (WikiSkipException ex)
        {
            _run.Note("SKIPPED", scene, ex.Message);
            _output.WriteLine("skipped: " + ex.Message);
            FailOnProblems(scene, problems, "was skipped");
            return;
        }
        catch (Exception ex)
        {
            // A picture that cannot be drawn is a gap in the artifact, not a red build: the report says which and why, and the other scenes go on.
            // Only a source that is wrong (below) fails the test, since that would put a wrong graph into the wiki.
            var why = ex.GetType().Name + ": " + FirstLine(ex.Message);
            _run.Note("NOTDRAWN", scene, why);
            _output.WriteLine("not drawn: " + why);
            _output.WriteLine(ex.ToString());
            FailOnProblems(scene, problems, "could not be drawn");
            return;
        }

        var detail = string.Join(", ", sizes) + "  " + watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        if (problems.Count > 0)
        {
            _run.Note("FAILED", scene, detail + "  " + string.Join(" | ", problems));
            Assert.Fail(scene.Id + " was drawn, but its source has problems:\n" + string.Join("\n", problems));
        }

        _run.Note("OK", scene, detail);
        _output.WriteLine(detail);
    }

    /// <summary>A scene that is not drawn still says when its graph is wrong (a node that did not resolve), which does fail the test.</summary>
    private void FailOnProblems(WikiScene scene, List<string> problems, string outcome)
    {
        if (problems.Count == 0)
        {
            return;
        }

        _run.Note("FAILED", scene, "its source has problems: " + string.Join(" | ", problems));
        Assert.Fail(scene.Id + " " + outcome + ", and its source has problems:\n" + string.Join("\n", problems));
    }

    // Runs on the WPF thread.
    private static void DrawAndSave(WikiScene scene, string folder, List<string> sizes, List<string> problems)
    {
        using (var context = new WikiContext())
        {
            WikiPictures? pictures = null;
            try
            {
                WikiUi.WithCulture(() => pictures = scene.Draw(context));
            }
            finally
            {
                // Kept even when the drawing then goes wrong: a graph that did not load is worth saying whatever else happens.
                problems.AddRange(context.Problems);
            }

            if (pictures == null)
            {
                throw new InvalidOperationException("The scene produced no pictures.");
            }

            Save(pictures.Dark, Path.Combine(folder, scene.FileName(false)));
            Save(pictures.Light, Path.Combine(folder, scene.FileName(true)));
            sizes.Add("dark " + pictures.Dark.PixelWidth + "x" + pictures.Dark.PixelHeight);
            sizes.Add("light " + pictures.Light.PixelWidth + "x" + pictures.Light.PixelHeight);
        }
    }

    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        if (new FileInfo(path).Length < 2000)
        {
            throw new InvalidOperationException("The picture is empty: " + path);
        }
    }

    private static string FirstLine(string text)
    {
        var end = text.IndexOf('\n');
        return end < 0 ? text : text.Substring(0, end).TrimEnd('\r');
    }

    [Fact]
    public void EverySceneOfTheManifestHasAPictureToDraw()
    {
        var ids = Scenes.Value.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        var manifest = WikiScenes.ManifestIds().Where(id => !id.StartsWith("wiki-installer-", StringComparison.Ordinal)).ToList();
        if (manifest.Count == 0)
        {
            return;     // the manifest is not part of this checkout
        }

        var missing = manifest.Where(id => !ids.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "No scene draws: " + string.Join(", ", missing));

        // Every sample graph has its own picture.
        foreach (var path in Directory.GetFiles(WikiPaths.SamplesDirectory(), "*.dyc"))
        {
            Assert.Contains(CamelGraph.TestSupport.Wiki.Manifest.SampleImageId(Path.GetFileNameWithoutExtension(path)), ids);
        }
    }

    [Fact]
    public void TheNavisworksStandInsLoadOnTheBuildMachine()
    {
        // The dynamic assembly is made by Reflection.Emit, which behaves a little differently on .NET Framework than where it is also tested.
        var standIns = WikiWorld.StandIns;
        Assert.Empty(standIns.Problems);
        Assert.True(standIns.Definitions.Count > 200, "only " + standIns.Definitions.Count + " Navisworks stand-ins");
        var search = WikiWorld.Definition("Search.ByProperty");
        Assert.Equal("CamelGraph.Navisworks", search.AssemblyName);
        Assert.Equal(new[] { "equals", "contains", "wildcard", ">", ">=", "<", "<=", "exists" }, search.Inputs.Single(i => i.Name == "mode").Choices);
        Assert.NotNull(WikiWorld.Registry.CreateNode("CapturedSelection"));
    }
}
