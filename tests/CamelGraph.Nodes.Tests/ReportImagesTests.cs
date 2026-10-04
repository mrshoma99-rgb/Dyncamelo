using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CamelGraph.Core.Files;
using CamelGraph.Core.Graph;
using Xunit;
using static CamelGraph.Nodes.Tests.Val2Harness;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Pictures in reports (audit VAL-37): a text line that is the path of an existing image file becomes the picture in Report.Html and
/// an image link in Report.Markdown; a path that is not found stays text and the node warns.
/// </summary>
[Collection("GraphContext")]
public class ReportImagesTests : IDisposable
{
    // A real 1 x 1 pixel PNG.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "camelgraph-report-" + Guid.NewGuid().ToString("N"));
    private readonly string? _folderBefore = GraphContext.Folder;

    public ReportImagesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        GraphContext.Folder = _folderBefore;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Picture(string name, byte[]? bytes = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes ?? TinyPng);
        return path;
    }

    private static IList<object?> S(params object?[] items) => new List<object?>(items);

    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes);

    // ── Report.Html ─────────────────────────────────────────────────────────

    [Fact]
    public void Html_APathToAnImageBecomesTheEmbeddedPicture()
    {
        var path = Picture("heat map.png");

        var html = ReportNodes.Html("T", S("# Plan", path));

        Assert.Contains("<img src=\"data:image/png;base64," + Base64(TinyPng) + "\" alt=\"heat map.png\">", html);
        Assert.DoesNotContain("<p>" + path, html);
        Assert.Contains("img{max-width:100%", html);
    }

    [Theory]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.JPEG", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.bmp", "image/bmp")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.svg", "image/svg+xml")]
    public void Html_EveryImageExtensionGetsItsOwnMediaType(string name, string mime)
    {
        var html = ReportNodes.Html("T", S(Picture(name)));

        Assert.Contains("<img src=\"data:" + mime + ";base64,", html);
    }

    [Fact]
    public void Html_APathPastedFromExplorerWithQuotesWorks()
    {
        var html = ReportNodes.Html("T", S("\"" + Picture("quoted.png") + "\""));

        Assert.Contains("<img src=\"data:image/png;base64,", html);
    }

    [Fact]
    public void Html_AnImageLineInsideALongerTextOrAListOfTextsBecomesAPictureToo()
    {
        var one = Picture("one.png");
        var two = Picture("two.png");

        var multiLine = ReportNodes.Html("T", S("Before\r\n" + one + "\r\nAfter"));
        Assert.Contains("<p>Before</p>", multiLine);
        Assert.Contains("alt=\"one.png\"", multiLine);
        Assert.Contains("<p>After</p>", multiLine);

        var list = ReportNodes.Html("T", S(S(one, "between", two)));
        Assert.Contains("alt=\"one.png\"", list);
        Assert.Contains("<p>between</p>", list);
        Assert.Contains("alt=\"two.png\"", list);
    }

    [Fact]
    public void Html_ARelativePathStartsInTheGraphsFolder()
    {
        Picture("relative.png");
        GraphContext.Folder = _root;

        var html = ReportNodes.Html("T", S("relative.png"));

        Assert.Contains("alt=\"relative.png\"", html);
    }

    [Fact]
    public void Html_AFileThatIsNotAnImageStaysText()
    {
        var path = Path.Combine(_root, "notes.txt");
        File.WriteAllText(path, "hello");

        var html = ReportNodes.Html("T", S(path));

        Assert.DoesNotContain("<img", html);
        Assert.Contains("<p>" + System.Net.WebUtility.HtmlEncode(path) + "</p>", html);
    }

    [Fact]
    public void Html_AMissingImageStaysTextAndTheNodeWarns()
    {
        var missing = Path.Combine(_root, "missing.png");

        var node = Run(Create(Registry(), "Report.Html"), "T", S(missing, "logo.png"));

        var html = (string)node.OutPorts[0].Value!;
        Assert.DoesNotContain("<img", html);
        Assert.Contains("<p>" + System.Net.WebUtility.HtmlEncode(missing) + "</p>", html);
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains(node.Messages, m => m.Text.Contains("was not found") && m.Text.Contains("missing.png"));
        // A bare name that does not exist is just a word of the text: no second warning.
        Assert.DoesNotContain(node.Messages, m => m.Text.Contains("logo.png"));
    }

    [Fact]
    public void Html_AnExistingImageGivesNoWarning()
    {
        var node = Run(Create(Registry(), "Report.Html"), "T", S(Picture("fine.png")));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
    }

    [Fact]
    public void Html_AnImageOverTenMegabytesIsLeftOutWithAWarning()
    {
        var big = Picture("big.png", new byte[10 * 1024 * 1024 + 1]);

        var node = Run(Create(Registry(), "Report.Html"), "T", S(big));

        Assert.DoesNotContain("<img", (string)node.OutPorts[0].Value!);
        Assert.Contains(node.Messages, m => m.Text.Contains("larger than 10 MB"));
    }

    // ── Report.Markdown ─────────────────────────────────────────────────────

    [Fact]
    public void Markdown_APathToAnImageBecomesAnImageLink()
    {
        var path = Picture("heat map (1).png");

        var text = ReportNodes.Markdown("T", S("# Plan", path, "after")).Replace("\r\n", "\n");

        var link = path.Replace('\\', '/').Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");
        Assert.Contains("![heat map (1).png](" + link + ")", text);
        Assert.Contains("%20", link);
        Assert.Contains("# Plan", text);
        Assert.Contains("after", text);
        Assert.DoesNotContain("base64", text);
    }

    [Fact]
    public void Markdown_TextWithoutAnImagePathIsWrittenAsItCame()
    {
        var text = ReportNodes.Markdown("T", S("line one\r\nline two", "# Heading")).Replace("\r\n", "\n");

        Assert.Contains("line one\nline two", text);
        Assert.Contains("# Heading", text);
    }

    [Fact]
    public void Markdown_AMissingImageStaysTextAndTheNodeWarns()
    {
        var missing = Path.Combine(_root, "missing.png");

        var node = Run(Create(Registry(), "Report.Markdown"), "T", S(missing));

        Assert.Contains(missing, (string)node.OutPorts[0].Value!);
        Assert.DoesNotContain("![", (string)node.OutPorts[0].Value!);
        Assert.Contains(node.Messages, m => m.Text.Contains("Report.Markdown") && m.Text.Contains("was not found"));
    }

    // ── The manual ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Report.Html")]
    [InlineData("Report.Markdown")]
    public void TheDescriptionAndTheTagsMentionPictures(string name)
    {
        var node = Create(Registry(), name);

        Assert.Contains("image file", node.Description);
        Assert.Contains("picture", node.SearchTags);
        Assert.Contains("image", node.SearchTags);
    }
}
