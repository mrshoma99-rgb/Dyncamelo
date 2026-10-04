using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Player;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>Watch List, Watch Table and Watch Image: size limits, clearing when the node does not run, a list of images.</summary>
public class WatchNodesLimitsTests
{
    private static EvaluationContext Context => new EvaluationContext();

    private static List<object?> Numbers(int count) => Enumerable.Range(0, count).Select(i => (object?)i).ToList();

    // ------------------------------------------------------------ Watch List

    [Fact]
    public void WatchList_AVeryLongListIsCutAndTheRestCounted()
    {
        var node = new WatchListNode();

        var result = node.Evaluate(new object?[] { Numbers(2500) }, Context);

        Assert.Equal(WatchListNode.MaxEntries + 1, node.Entries.Count);
        Assert.Equal("1999", node.Entries[1999].Index);
        var last = node.Entries[WatchListNode.MaxEntries];
        Assert.False(last.HasIndex);
        Assert.Equal("… 500 more items (2,500 in all)", last.Text);
        Assert.Equal(WatchListNode.MaxEntries + 1, node.Lines.Count);
        Assert.Equal(2500, ((IList<object?>)result[0]!).Count);   // the value itself passes through whole
    }

    [Fact]
    public void WatchList_AListWithinTheLimitIsUnchanged()
    {
        var node = new WatchListNode();

        node.Evaluate(new object?[] { new List<object?> { "a", "b" } }, Context);

        Assert.Equal("0 : a\n1 : b", node.FormattedValue);
    }

    [Fact]
    public void WatchList_IsClearedWhenTheNodeDoesNotRun()
    {
        var node = new WatchListNode();
        node.Evaluate(new object?[] { Numbers(3) }, Context);

        node.OnNotRun();

        Assert.Empty(node.Entries);
        Assert.Empty(node.Lines);
        Assert.Equal(string.Empty, node.PlayerText);
    }

    // ----------------------------------------------------------- Watch Table

    private static List<object?> Rows(int count) =>
        Enumerable.Range(0, count).Select(i => (object?)new List<object?> { i, "row " + i }).ToList();

    [Fact]
    public void WatchTable_ThePlayerTextIsCutToWhatThePlayerKeeps()
    {
        var node = new WatchTableNode();

        node.Evaluate(new object?[] { Rows(1000) }, Context);

        var lines = node.PlayerText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Where(l => l.Length > 0).ToList();
        Assert.True(lines.Count <= ScriptSession.MaxOutputLines);
        Assert.StartsWith("|", lines[0]);
        Assert.Equal("… 703 more rows", lines[lines.Count - 1]);
        Assert.Equal(1000, node.Rows.Count);               // the grid is untouched
        Assert.Contains("1,000", node.Summary.Replace("1000", "1,000"));
    }

    [Fact]
    public void WatchTable_ASmallTableKeepsItsWholePlayerText()
    {
        var node = new WatchTableNode();

        node.Evaluate(new object?[] { Rows(3) }, Context);

        var lines = node.PlayerText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);                     // header, separator, three rows
        Assert.DoesNotContain("more rows", node.PlayerText);
    }

    [Fact]
    public void WatchTable_IsClearedWhenTheNodeDoesNotRun()
    {
        var node = new WatchTableNode();
        node.Evaluate(new object?[] { Rows(3) }, Context);

        node.OnNotRun();

        Assert.Empty(node.Headers);
        Assert.Empty(node.Rows);
        Assert.Equal(string.Empty, node.Summary);
        Assert.Equal(string.Empty, node.PlayerText);
    }

    // ----------------------------------------------------------- Watch Image

    private static string TempImage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    [Fact]
    public void WatchImage_AListOfPathsShowsTheFirstImageAndTheCount()
    {
        var first = TempImage();
        var second = TempImage();
        try
        {
            var node = new WatchImageNode();

            var result = node.Evaluate(new object?[] { new List<object?> { first, second, "missing.png" } }, Context);

            Assert.Equal(first, node.ImagePath);
            Assert.True(node.HasImage);
            Assert.Equal(3, node.ImageCount);
            Assert.Equal(Path.GetFileName(first) + " (1 of 3)", node.FileName);
            Assert.Equal("3 images\n" + first + "\n" + second + "\nmissing.png", node.PlayerText);
            Assert.IsType<List<object?>>(result[0]);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void WatchImage_NestedListsNullsAndBlanksAreSkipped()
    {
        var path = TempImage();
        try
        {
            var node = new WatchImageNode();

            node.Evaluate(new object?[] { new List<object?> { null, "  ", new List<object?> { path } } }, Context);

            Assert.Equal(path, node.ImagePath);
            Assert.Equal(1, node.ImageCount);
            Assert.Equal(Path.GetFileName(path), node.FileName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WatchImage_HasImageIsDecidedWhenTheNodeRunsNotOnEveryRead()
    {
        var path = TempImage();
        var node = new WatchImageNode();
        node.Evaluate(new object?[] { path }, Context);

        File.Delete(path);

        // The getter does not touch the disk: it reports what the run saw.
        Assert.True(node.HasImage);
        node.Evaluate(new object?[] { path }, Context);
        Assert.False(node.HasImage);
    }

    [Fact]
    public void WatchImage_IsClearedWhenTheNodeDoesNotRun()
    {
        var path = TempImage();
        try
        {
            var node = new WatchImageNode();
            node.Evaluate(new object?[] { path }, Context);
            var version = node.ImageVersion;

            node.OnNotRun();

            Assert.Equal(string.Empty, node.ImagePath);
            Assert.False(node.HasImage);
            Assert.Equal(0, node.ImageCount);
            Assert.Equal(string.Empty, node.PlayerText);
            Assert.True(node.ImageVersion > version);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
