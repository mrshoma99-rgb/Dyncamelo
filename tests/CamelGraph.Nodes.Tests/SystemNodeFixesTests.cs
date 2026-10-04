using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CamelGraph.Core.Files;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>The node-audit fixes of the System, Web and Flow.Wait nodes (SYS-17, SYS-20, SYS-21, SYS-37).</summary>
[Collection("GraphContext")]
public class SystemNodeFixesTests : IDisposable
{
    private readonly string _directory;
    private readonly List<TinyHttpServer> _servers = new List<TinyHttpServer>();

    public SystemNodeFixesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "CamelGraphSystemFixes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private TinyHttpServer Serve(Func<RecordedRequest, ServerReply> handler)
    {
        var server = new TinyHttpServer(handler);
        _servers.Add(server);
        return server;
    }

    private static NodeDefinition Definition(string name)
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry.Definitions.Single(d => d.Name == name);
    }

    private static PortDescriptor Input(string node, string input) => Definition(node).Inputs.Single(i => i.Name == input);

    // ------------------------------------------------------------- Graph.Folder (SYS-17)

    [Fact]
    public void GraphFolder_IsTheFolderRelativePathsStartIn()
    {
        using (GraphContext.Use(_directory))
        {
            Assert.Equal(_directory, SystemNodes.GraphFolder());
            Assert.Equal(Path.Combine(_directory, "report.csv"), PathNodes.Join(new List<object?> { SystemNodes.GraphFolder(), "report.csv" }));
        }

        using (GraphContext.Use(null))
        {
            Assert.Equal(Path.GetFullPath(Directory.GetCurrentDirectory()), SystemNodes.GraphFolder());
        }
    }

    [Fact]
    public void GraphFolder_IsALiveStateNode_SoASaveAsIsSeenOnTheNextRun()
    {
        var definition = Definition("Graph.Folder");
        Assert.True(definition.IsLiveState);
        Assert.Equal("System", definition.Category);
        Assert.Equal(NodeFunction.Info, definition.Function);
        Assert.Empty(definition.Inputs);
    }

    [Fact]
    public void TheOtherFolderDescriptions_SayWhichFolderTheyMean()
    {
        Assert.Contains("graph's folder", Definition("Path.GetFullPath").Description);
        Assert.Contains("Graph.Folder", Definition("System.Environment").Description);
    }

    // --------------------------------------------------------- System.Run (SYS-17, SYS-20, SYS-21)

    [Fact]
    public void SystemRun_HasAnAdvancedPanel_AndASliderThatStopsAtFiveMinutes()
    {
        Assert.Equal(string.Empty, Input("System.Run", "executable").Panel ?? string.Empty);
        Assert.Equal("Advanced", Input("System.Run", "workingDirectory").Panel);
        Assert.Equal("Advanced", Input("System.Run", "timeoutSeconds").Panel);
        var range = Input("System.Run", "timeoutSeconds").Range!;
        Assert.Equal(3600d, range.Max);
        Assert.Equal(300d, range.SoftMax);
        Assert.Equal(NodePathMode.Open, Input("System.Run", "executable").PathMode);
        Assert.Equal(NodePathMode.Folder, Input("System.Run", "workingDirectory").PathMode);
        Assert.Contains("Programs", Input("System.Run", "executable").PathFilter);
    }

    [Fact]
    public void SystemRun_ARelativeWorkingDirectory_StartsInTheGraphFolder()
    {
        if (!PathNodes.IsWindows)
        {
            var sub = Path.Combine(_directory, "work");
            Directory.CreateDirectory(sub);
            using (GraphContext.Use(_directory))
            {
                var result = SystemNodes.RunProcess("pwd", string.Empty, "work", TimeSpan.FromSeconds(30));
                Assert.Equal(sub, ((string)result["output"]!).Trim());
            }
        }
    }

    [Fact]
    public void SystemRun_ABareProgramNameIsStillSearchedOnThePath_NotInTheGraphFolder()
    {
        if (!PathNodes.IsWindows)
        {
            using (GraphContext.Use(_directory))
            {
                var result = SystemNodes.RunProcess("echo", "hello", string.Empty, TimeSpan.FromSeconds(30));
                Assert.Equal(0, result["exitCode"]);
                Assert.Equal("hello", ((string)result["output"]!).Trim());
            }
        }
    }

    // -------------------------------------------------------------- Web nodes (SYS-20, SYS-21)

    [Fact]
    public void WebNodes_KeepTheRarelyUsedInputsInTheAdvancedPanel_AndOfferTheUsualContentTypes()
    {
        Assert.Equal(string.Empty, Input("Web.Get", "url").Panel ?? string.Empty);
        Assert.Equal("Advanced", Input("Web.Get", "headers").Panel);
        Assert.Equal("Advanced", Input("Web.Get", "timeoutSeconds").Panel);
        Assert.Equal("Advanced", Input("Web.Post", "contentType").Panel);
        Assert.Equal("Advanced", Input("Web.Post", "headers").Panel);
        Assert.Equal("Advanced", Input("Web.Post", "timeoutSeconds").Panel);
        Assert.Equal(string.Empty, Input("Web.Post", "body").Panel ?? string.Empty);

        var choices = Input("Web.Post", "contentType").Choices!;
        foreach (var type in new[] { "application/json", "text/plain", "text/csv", "application/x-www-form-urlencoded" })
        {
            Assert.Contains(type, choices);
        }

        Assert.Equal("application/json", Input("Web.Post", "contentType").DefaultValue);
        foreach (var node in new[] { "Web.Get", "Web.Post" })
        {
            var range = Input(node, "timeoutSeconds").Range!;
            Assert.Equal(600d, range.Max);
            Assert.Equal(120d, range.SoftMax);
        }
    }

    // ------------------------------------------------------------------ Web.Download (SYS-37)

    [Fact]
    public void WebDownload_SavesTheBytesUntouched_AndReportsWhatItDid()
    {
        var payload = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).Concat(new byte[] { 0xFF, 0x00, 0xC3 }).ToArray();
        var server = Serve(_ => new ServerReply { ContentType = "application/octet-stream", Body = payload });
        var target = Path.Combine(_directory, "sub", "model.ifc");

        var result = SystemNodes.Download(server.Url("/model.ifc"), target);

        Assert.Equal(target, result["path"]);
        Assert.Equal(200, result["status"]);
        Assert.Equal(true, result["ok"]);
        Assert.Equal((double)payload.Length, result["sizeBytes"]);
        Assert.Equal(payload, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(Path.Combine(_directory, "sub")));
    }

    [Fact]
    public void WebDownload_AnErrorStatus_WritesNothing_AndKeepsAnExistingFile()
    {
        var server = Serve(_ => ServerReply.Text("gone", 404, "Not Found"));
        var target = Path.Combine(_directory, "keep.bin");
        File.WriteAllText(target, "old");

        var result = SystemNodes.Download(server.Url(), target, overwrite: true);

        Assert.Null(result["path"]);
        Assert.Equal(404, result["status"]);
        Assert.Equal(false, result["ok"]);
        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(_directory));
    }

    [Fact]
    public void WebDownload_RefusesToReplaceAFile_UnlessOverwriteIsTrue()
    {
        var server = Serve(_ => ServerReply.Text("fresh"));
        var target = Path.Combine(_directory, "have.txt");
        File.WriteAllText(target, "have");

        var ex = Assert.Throws<IOException>(() => SystemNodes.Download(server.Url(), target));
        Assert.Contains("overwrite", ex.Message);
        Assert.Equal("have", File.ReadAllText(target));

        SystemNodes.Download(server.Url(), target, overwrite: true);
        Assert.Equal("fresh", File.ReadAllText(target));
    }

    [Fact]
    public void WebDownload_ARelativePathStartsInTheGraphFolder_AndHeadersAreSent()
    {
        var server = Serve(_ => ServerReply.Text("x"));
        using (GraphContext.Use(_directory))
        {
            var result = SystemNodes.Download(server.Url(), "dl/x.txt", false, new Dictionary<string, string> { ["X-Token"] = "abc" });
            Assert.Equal(Path.Combine(_directory, "dl", "x.txt"), result["path"]);
        }

        Assert.Equal("abc", server.Last.Headers["X-Token"]);
    }

    [Fact]
    public void WebDownload_ATooSlowAnswer_IsATimeout_AndLeavesNoTemporaryFile()
    {
        var server = Serve(_ => new ServerReply { Body = new byte[] { 1 }, Delay = TimeSpan.FromSeconds(30) });
        var target = Path.Combine(_directory, "late.bin");

        var ex = Assert.Throws<TimeoutException>(() => SystemNodes.DownloadFile("Web.Download", server.Url(), target, false, null, TimeSpan.FromMilliseconds(300)));

        Assert.Contains("Web.Download", ex.Message);
        Assert.Contains("nothing was saved", ex.Message);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void WebDownload_BadInputs_AreExplainedInPlainWords()
    {
        Assert.Contains("http", Assert.Throws<ArgumentException>(() => SystemNodes.Download("ftp://x/y", Path.Combine(_directory, "a"))).Message);
        Assert.Contains("'path'", Assert.Throws<ArgumentException>(() => SystemNodes.Download("http://127.0.0.1:1/", " ")).Message);
        Assert.Contains("timeoutSeconds", Assert.Throws<ArgumentOutOfRangeException>(() => SystemNodes.Download("http://127.0.0.1:1/", "a", false, null, 0)).Message);
    }

    [Fact]
    public void WebDownload_DeclaresThatItUsesTheNetworkAndWritesFiles()
    {
        var definition = Definition("Web.Download");
        Assert.Equal(NodeEffects.UsesNetwork | NodeEffects.WritesFiles, definition.Effects);
        Assert.Equal(NodePathMode.Save, Input("Web.Download", "path").PathMode);
        Assert.Equal("Advanced", Input("Web.Download", "timeoutSeconds").Panel);
        Assert.Contains("Web.Download", Definition("Web.Get").Description);
    }
}
