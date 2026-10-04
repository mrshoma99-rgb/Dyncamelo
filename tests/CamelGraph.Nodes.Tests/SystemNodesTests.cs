using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using Xunit;
using GraphFunction = CamelGraph.Core.Graph.NodeFunction;

namespace CamelGraph.Nodes.Tests;

/// <summary>One request as the test server received it.</summary>
internal sealed class RecordedRequest
{
    public string Method { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public byte[] Body { get; set; } = Array.Empty<byte>();

    public string BodyText => Encoding.UTF8.GetString(Body);
}

/// <summary>What the test server answers.</summary>
internal sealed class ServerReply
{
    public int Status { get; set; } = 200;

    public string Reason { get; set; } = "OK";

    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>Null omits the Content-Type header.</summary>
    public string? ContentType { get; set; } = "text/plain; charset=utf-8";

    public Dictionary<string, string> Extra { get; } = new Dictionary<string, string>();

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public static ServerReply Text(string body, int status = 200, string reason = "OK")
    {
        return new ServerReply { Status = status, Reason = reason, Body = new UTF8Encoding(false).GetBytes(body) };
    }
}

/// <summary>
/// A deliberately tiny HTTP/1.1 server on 127.0.0.1 and an ephemeral port (TcpListener, not HttpListener, so it needs
/// no URL reservation and behaves the same on Windows and Linux). It answers every request through a handler and keeps a
/// record of what it was sent.
/// </summary>
internal sealed class TinyHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private readonly Func<RecordedRequest, ServerReply> _handler;

    public TinyHttpServer(Func<RecordedRequest, ServerReply> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new ConcurrentQueue<RecordedRequest>();

    public RecordedRequest Last => Requests.Last();

    public string Url(string target = "/") => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + target;

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                if (request == null)
                {
                    return;
                }

                Requests.Enqueue(request);
                var reply = _handler(request);
                if (reply.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(reply.Delay, _stop.Token).ConfigureAwait(false);
                }

                var head = new StringBuilder();
                head.Append("HTTP/1.1 ").Append(reply.Status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reply.Reason).Append("\r\n");
                if (reply.ContentType != null)
                {
                    head.Append("Content-Type: ").Append(reply.ContentType).Append("\r\n");
                }

                foreach (var extra in reply.Extra)
                {
                    head.Append(extra.Key).Append(": ").Append(extra.Value).Append("\r\n");
                }

                head.Append("Content-Length: ").Append(reply.Body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                head.Append("Connection: close\r\n\r\n");
                var headBytes = Encoding.ASCII.GetBytes(head.ToString());
                await stream.WriteAsync(headBytes, 0, headBytes.Length).ConfigureAwait(false);
                await stream.WriteAsync(reply.Body, 0, reply.Body.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException || ex is SocketException)
            {
                // the client went away (a timeout test does that on purpose) or the server was disposed
            }
        }
    }

    private static async Task<RecordedRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) == 0)
            {
                return null;
            }

            head.Add(one[0]);
            var n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n')
            {
                break;
            }
        }

        var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var requestLine = lines[0].Split(' ');
        var request = new RecordedRequest { Method = requestLine[0], Target = requestLine[1] };
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            request.Headers[line.Substring(0, colon)] = line.Substring(colon + 1).Trim();
        }

        if (request.Headers.TryGetValue("Expect", out var expect) && expect.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var go = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
            await stream.WriteAsync(go, 0, go.Length).ConfigureAwait(false);
        }

        if (request.Headers.TryGetValue("Content-Length", out var lengthText))
        {
            var length = int.Parse(lengthText, CultureInfo.InvariantCulture);
            var body = new byte[length];
            var read = 0;
            while (read < length)
            {
                var got = await stream.ReadAsync(body, read, length - read).ConfigureAwait(false);
                if (got == 0)
                {
                    break;
                }

                read += got;
            }

            request.Body = body;
        }

        return request;
    }
}

/// <summary>System.* and Web.* nodes.</summary>
public class SystemNodesTests : IDisposable
{
    private readonly string _directory;
    private readonly List<TinyHttpServer> _servers = new List<TinyHttpServer>();

    public SystemNodesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "camelgraph-" + Guid.NewGuid().ToString("N"));
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
        catch (UnauthorizedAccessException)
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

    private TinyHttpServer ServeText(string body, int status = 200, string reason = "OK")
    {
        return Serve(_ => ServerReply.Text(body, status, reason));
    }

    private static string UnusedLocalUrl()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";
    }

    private static Dictionary<string, object?> Headers(params (string Name, object? Value)[] headers)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var (name, value) in headers)
        {
            dictionary[name] = value;
        }

        return dictionary;
    }

    // ------------------------------------------------------- System.Environment

    [Fact]
    public void Environment_ReportsTheFactsOfThisComputer()
    {
        var env = SystemNodes.GetEnvironment();

        Assert.Equal(
            new[] { "userName", "machineName", "osVersion", "currentDirectory", "tempPath", "documentsPath", "appDataPath" },
            env.Keys);
        Assert.Equal(System.Environment.UserName, env["userName"]);
        Assert.Equal(System.Environment.MachineName, env["machineName"]);
        Assert.Equal(Directory.GetCurrentDirectory(), env["currentDirectory"]);
        Assert.Equal(Path.GetTempPath(), env["tempPath"]);
        Assert.Equal(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), env["documentsPath"]);
        Assert.Equal(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), env["appDataPath"]);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(env["osVersion"])));
    }

    [Fact]
    public void Environment_NoValueIsNull_SoNoOutputSocketStaysEmpty()
    {
        Assert.All(SystemNodes.GetEnvironment().Values, value => Assert.NotNull(value));
    }

    // ----------------------------------------------------------- System.OpenPath

    [Fact]
    public void OpenPath_MissingPath_ThrowsAClearErrorWithThePath()
    {
        var missing = Path.Combine(_directory, "ghost.nwd");
        var ex = Assert.Throws<FileNotFoundException>(() => SystemNodes.OpenPath(missing));
        Assert.Contains(missing, ex.Message);
        Assert.Contains("System.OpenPath", ex.Message);
        Assert.Throws<FileNotFoundException>(() => SystemNodes.OpenPath(missing, reveal: true));
    }

    [Fact]
    public void OpenPath_BlankPath_ThrowsNamingTheNode()
    {
        var ex = Assert.Throws<ArgumentException>(() => SystemNodes.OpenPath(" "));
        Assert.Contains("System.OpenPath", ex.Message);
        Assert.Contains("'path'", ex.Message);
    }

    [Fact]
    public void BuildOpenStartInfo_Open_UsesTheShellOnTheFullPath()
    {
        var info = SystemNodes.BuildOpenStartInfo(Path.Combine(_directory, "a.nwd"), reveal: false, windows: true);
        Assert.Equal(Path.Combine(_directory, "a.nwd"), info.FileName);
        Assert.True(info.UseShellExecute);
        Assert.Equal(string.Empty, info.Arguments);
    }

    [Fact]
    public void BuildOpenStartInfo_RevealOnWindows_SelectsTheItemInExplorer()
    {
        var file = Path.Combine(_directory, "my model.nwd");
        var info = SystemNodes.BuildOpenStartInfo(file, reveal: true, windows: true);

        Assert.Equal("explorer.exe", info.FileName);
        Assert.Equal("/select,\"" + file + "\"", info.Arguments);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public void BuildOpenStartInfo_RevealOnWindows_TrimsATrailingSeparatorOfAFolder()
    {
        var folder = _directory + Path.DirectorySeparatorChar;
        var info = SystemNodes.BuildOpenStartInfo(folder, reveal: true, windows: true);
        Assert.Equal("/select,\"" + _directory + "\"", info.Arguments);
    }

    [Fact]
    public void BuildOpenStartInfo_RevealElsewhere_OpensTheContainingFolder()
    {
        var file = Path.Combine(_directory, "a.nwd");
        var info = SystemNodes.BuildOpenStartInfo(file, reveal: true, windows: false);

        Assert.Equal(_directory, info.FileName);
        Assert.True(info.UseShellExecute);
        Assert.Equal(string.Empty, info.Arguments);
    }

    // ---------------------------------------------------------------- System.Run

    /// <summary>A command line that behaves the same on Windows (cmd.exe) and Unix (sh), given one script for each.</summary>
    private static (string Executable, string Arguments) Shell(string unixScript, string windowsScript)
    {
        return PathNodes.IsWindows
            ? ("cmd.exe", "/c " + windowsScript)
            : ("/bin/sh", "-c \"" + unixScript + "\"");
    }

    private static string[] Lines(object? text)
    {
        return Assert.IsType<string>(text).Split('\n').Select(l => l.TrimEnd()).ToArray();
    }

    [Fact]
    public void Run_CapturesExitCodeOutputAndError()
    {
        var (exe, args) = Shell("echo out; echo err 1>&2; exit 3", "\"echo out&echo err 1>&2&exit /b 3\"");

        var result = SystemNodes.Run(exe, args);

        Assert.Equal(3, result["exitCode"]);
        Assert.Equal(new[] { "out" }, Lines(result["output"]));
        Assert.Equal(new[] { "err" }, Lines(result["error"]));
    }

    [Fact]
    public void Run_SuccessfulProgram_ExitsWithZero_AndKeepsMultipleLinesTogether()
    {
        var (exe, args) = Shell("echo a; echo b; echo c", "\"echo a&echo b&echo c\"");

        var result = SystemNodes.Run(exe, args);

        Assert.Equal(0, result["exitCode"]);
        Assert.Equal(new[] { "a", "b", "c" }, Lines(result["output"]));
        Assert.Equal(string.Empty, result["error"]);
    }

    [Fact]
    public void Run_DoesNotDeadlock_WhenBothStreamsOverflowThePipeBuffer()
    {
        // 300 000 characters per stream is far beyond a 64 KB pipe; a reader that drains one stream at a time hangs here.
        var (exe, args) = Shell(
            "i=0; while [ $i -lt 3000 ]; do echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789; echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789 1>&2; i=$((i+1)); done",
            "\"for /L %i in (1,1,3000) do @(echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789&echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789 1>&2)\"");

        var result = SystemNodes.RunProcess(exe, args, null, TimeSpan.FromSeconds(60));

        Assert.Equal(0, result["exitCode"]);
        Assert.Equal(3000, Lines(result["output"]).Length);
        Assert.Equal(3000, Lines(result["error"]).Length);
    }

    [Fact]
    public void Run_ProgramThatReadsInput_SeesEndOfInput_InsteadOfWaiting()
    {
        var exe = PathNodes.IsWindows ? "cmd.exe" : "/bin/cat";
        var args = PathNodes.IsWindows ? "/c sort" : string.Empty;

        var result = SystemNodes.RunProcess(exe, args, null, TimeSpan.FromSeconds(20));

        Assert.Equal(0, result["exitCode"]);
        Assert.Equal(string.Empty, result["output"]);
    }

    [Fact]
    public void Run_UsesTheWorkingDirectory()
    {
        var folder = Path.Combine(_directory, "where-am-i");
        Directory.CreateDirectory(folder);
        var (exe, args) = Shell("pwd", "cd");

        var result = SystemNodes.Run(exe, args, folder);

        Assert.EndsWith("where-am-i", Assert.IsType<string>(result["output"]).Trim());
    }

    [Fact]
    public void Run_WorksWithAProgramFoundOnThePath_ThroughTheDotnetHost()
    {
        // The test host's own runtime directory leads to the dotnet executable on every platform.
        var runtimeDirectory = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent?.FullName ?? string.Empty;
        var dotnet = Path.Combine(dotnetRoot, PathNodes.IsWindows ? "dotnet.exe" : "dotnet");
        Assert.True(File.Exists(dotnet), "could not locate the dotnet host at " + dotnet);

        var result = SystemNodes.Run(dotnet, "--list-runtimes", string.Empty, 60);

        Assert.Equal(0, result["exitCode"]);
        Assert.Contains("Microsoft.NETCore.App", Assert.IsType<string>(result["output"]));
    }

    [Fact]
    public void Run_Timeout_StopsTheProgram_AndSaysSo()
    {
        var (exe, args) = Shell("sleep 30; echo done", "\"ping -n 30 127.0.0.1 > nul\"");
        var clock = Stopwatch.StartNew();

        var ex = Assert.Throws<TimeoutException>(() => SystemNodes.RunProcess(exe, args, null, TimeSpan.FromMilliseconds(300)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "the wait must end at the timeout, not when the program ends (" + clock.Elapsed + ")");
        Assert.Contains("System.Run", ex.Message);
        Assert.Contains(exe, ex.Message);
        Assert.Contains("0.3 second", ex.Message);
    }

    [Fact]
    public void Run_Timeout_KillsTheWholeProcessTree()
    {
        var marker = Path.Combine(_directory, "grandchild-survived.txt");
        // A background sub-shell (a grandchild of this test) would write the marker after 1.5 s if only the direct child were killed.
        var (exe, args) = Shell(
            "(sleep 1.5; echo alive > '" + marker + "') & wait",
            "\"(ping -n 3 127.0.0.1 > nul & echo alive> \"" + marker + "\") & ping -n 30 127.0.0.1 > nul\"");

        Assert.Throws<TimeoutException>(() => SystemNodes.RunProcess(exe, args, null, TimeSpan.FromMilliseconds(300)));
        Thread.Sleep(2500);

        Assert.False(File.Exists(marker), "a process started by the program outlived the timeout");
    }

    [Fact]
    public void Run_PublicNode_TimeoutIsInWholeSeconds()
    {
        var (exe, args) = Shell("sleep 30; echo done", "\"ping -n 30 127.0.0.1 > nul\"");
        var clock = Stopwatch.StartNew();

        var ex = Assert.Throws<TimeoutException>(() => SystemNodes.Run(exe, args, string.Empty, 1));

        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(15));
        Assert.Contains("1 second", ex.Message);
    }

    [Fact]
    public void Run_ProgramNotFound_NamesItAndSuggestsAFix()
    {
        var name = "definitely-not-a-program-" + Guid.NewGuid().ToString("N");
        var ex = Assert.Throws<InvalidOperationException>(() => SystemNodes.Run(name));
        Assert.Contains(name, ex.Message);
        Assert.Contains("System.Run", ex.Message);
    }

    [Fact]
    public void Run_InvalidInputs_NameTheProblem()
    {
        Assert.Contains("'executable'", Assert.Throws<ArgumentException>(() => SystemNodes.Run(" ")).Message);

        var missing = Path.Combine(_directory, "no-such-folder");
        var ex = Assert.Throws<DirectoryNotFoundException>(() => SystemNodes.Run(PathNodes.IsWindows ? "cmd.exe" : "/bin/sh", "", missing));
        Assert.Contains(missing, ex.Message);

        Assert.Throws<ArgumentOutOfRangeException>(() => SystemNodes.Run("x", "", "", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SystemNodes.Run("x", "", "", 3601));
    }

    [Fact]
    public void Run_DoesNotPutArgumentsIntoErrorMessages()
    {
        var name = "definitely-not-a-program-" + Guid.NewGuid().ToString("N");
        var ex = Assert.Throws<InvalidOperationException>(() => SystemNodes.Run(name, "--password hunter2"));
        Assert.DoesNotContain("hunter2", ex.ToString());
    }

    // ------------------------------------------------------------------- Web.Get

    [Fact]
    public void Get_ReturnsStatusBodyAndOk_AndSendsAPlainGet()
    {
        var server = ServeText("hello");

        var result = SystemNodes.Get(server.Url("/greeting?name=Zoë&n=1"));

        Assert.Equal(200, result["status"]);
        Assert.Equal("hello", result["body"]);
        Assert.Equal(true, result["ok"]);
        var request = server.Last;
        Assert.Equal("GET", request.Method);
        Assert.Equal("/greeting?name=Zo%C3%AB&n=1", request.Target);
        Assert.Empty(request.Body);
        Assert.Equal("CamelGraph", request.Headers["User-Agent"]);
    }

    [Fact]
    public void Get_SendsTheGivenHeaders_FormattingValuesInvariantly()
    {
        var server = ServeText("ok");
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = comma;
            SystemNodes.Get(server.Url(), Headers(("Authorization", "Bearer abc.def"), ("X-Custom", "value one"), ("X-Count", 1234.5)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var headers = server.Last.Headers;
        Assert.Equal("Bearer abc.def", headers["Authorization"]);
        Assert.Equal("value one", headers["X-Custom"]);
        Assert.Equal("1234.5", headers["X-Count"]);
    }

    [Fact]
    public void Get_AUserAgentHeaderReplacesTheDefault()
    {
        var server = ServeText("ok");
        SystemNodes.Get(server.Url(), Headers(("User-Agent", "MyScript/2")));
        Assert.Equal("MyScript/2", server.Last.Headers["User-Agent"]);
    }

    [Fact]
    public void Get_AnyDictionaryTypeWorksForHeaders()
    {
        var server = ServeText("ok");
        SystemNodes.Get(server.Url(), new Dictionary<string, string> { ["X-Typed"] = "yes" });
        Assert.Equal("yes", server.Last.Headers["X-Typed"]);
    }

    [Fact]
    public void Get_ContentTypeHeaderIsIgnored_SoOneHeadersDictionaryServesGetAndPost()
    {
        var server = ServeText("ok");
        var result = SystemNodes.Get(server.Url(), Headers(("Content-Type", "application/json"), ("Accept", "application/json")));
        Assert.Equal(200, result["status"]);
        Assert.False(server.Last.Headers.ContainsKey("Content-Type"));
        Assert.Equal("application/json", server.Last.Headers["Accept"]);
    }

    [Theory]
    [InlineData(200, "OK", true)]
    [InlineData(201, "Created", true)]
    [InlineData(299, "Custom", true)]
    [InlineData(400, "Bad Request", false)]
    [InlineData(404, "Not Found", false)]
    [InlineData(500, "Internal Server Error", false)]
    public void Get_NonSuccessStatus_IsNotAnException_AndTheBodyIsKept(int status, string reason, bool expectedOk)
    {
        var server = ServeText("details for " + status, status, reason);

        var result = SystemNodes.Get(server.Url());

        Assert.Equal(status, result["status"]);
        Assert.Equal("details for " + status, result["body"]);
        Assert.Equal(expectedOk, result["ok"]);
    }

    [Fact]
    public void Get_EmptyResponse_GivesAnEmptyBody()
    {
        var server = Serve(_ => new ServerReply { Status = 204, Reason = "No Content", ContentType = null });
        var result = SystemNodes.Get(server.Url());
        Assert.Equal(204, result["status"]);
        Assert.Equal(string.Empty, result["body"]);
        Assert.Equal(true, result["ok"]);
    }

    [Fact]
    public void Get_DecodesUtf8_WithOrWithoutACharset_AndDropsTheByteOrderMark()
    {
        const string text = "Größe ✓ 日本";
        var utf8 = new UTF8Encoding(false);
        var withoutCharset = Serve(_ => new ServerReply { Body = utf8.GetBytes(text), ContentType = "application/json" });
        var withBom = Serve(_ => new ServerReply { Body = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(utf8.GetBytes(text)).ToArray() });

        Assert.Equal(text, SystemNodes.Get(withoutCharset.Url())["body"]);
        Assert.Equal(text, SystemNodes.Get(withBom.Url())["body"]);
    }

    [Fact]
    public void Get_HonoursADeclaredCharset_AndFallsBackToUtf8ForUnknownOnes()
    {
        var latin1 = Serve(_ => new ServerReply { Body = new byte[] { (byte)'G', 0xF6, 0xDF }, ContentType = "text/plain; charset=iso-8859-1" });
        var unknown = Serve(_ => new ServerReply { Body = Encoding.UTF8.GetBytes("plain"), ContentType = "text/plain; charset=made-up-9000" });

        Assert.Equal("Gößler".Substring(0, 1) + "öß", SystemNodes.Get(latin1.Url())["body"]);
        Assert.Equal("plain", SystemNodes.Get(unknown.Url())["body"]);
    }

    [Fact]
    public void Get_UnzipsCompressedResponses()
    {
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes("zipped payload");
            gzip.Write(bytes, 0, bytes.Length);
        }

        var server = Serve(_ =>
        {
            var reply = new ServerReply { Body = compressed.ToArray() };
            reply.Extra["Content-Encoding"] = "gzip";
            return reply;
        });

        Assert.Equal("zipped payload", SystemNodes.Get(server.Url())["body"]);
    }

    [Fact]
    public void Get_FollowsRedirects()
    {
        var server = Serve(request => request.Target == "/final"
            ? ServerReply.Text("arrived")
            : CreateRedirect("/final"));

        Assert.Equal("arrived", SystemNodes.Get(server.Url("/start"))["body"]);
    }

    private static ServerReply CreateRedirect(string location)
    {
        var reply = ServerReply.Text(string.Empty, 302, "Found");
        reply.Extra["Location"] = location;
        return reply;
    }

    [Fact]
    public void Get_SlowServer_TimesOut_NamingTheUrl_AndLeavesTheSharedClientUsable()
    {
        var slow = Serve(_ => new ServerReply { Delay = TimeSpan.FromSeconds(20), Body = Encoding.UTF8.GetBytes("late") });
        var fast = ServeText("quick");
        var url = slow.Url("/slow");
        var clock = Stopwatch.StartNew();

        var ex = Assert.Throws<TimeoutException>(
            () => SystemNodes.SendRequest("Web.Get", HttpMethod.Get, url, null, null, null, TimeSpan.FromMilliseconds(300)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "timed out after " + clock.Elapsed);
        Assert.Contains(url, ex.Message);
        Assert.Contains("Web.Get", ex.Message);
        Assert.Contains("timed out", ex.Message);

        // The per-call token must not have poisoned the shared client.
        Assert.Equal("quick", SystemNodes.Get(fast.Url())["body"]);
    }

    [Fact]
    public void Get_ConnectionRefused_IsAnExceptionWithTheUrl()
    {
        var url = UnusedLocalUrl();
        var ex = Assert.Throws<HttpRequestException>(() => SystemNodes.Get(url));
        Assert.Contains(url, ex.Message);
        Assert.Contains("Web.Get", ex.Message);
        Assert.Contains("failed", ex.Message);
    }

    [Fact]
    public void Get_UnknownHost_IsAnExceptionWithTheUrl()
    {
        // ".invalid" is reserved (RFC 2606) and never resolves.
        var url = "http://no-such-host.invalid/";
        var ex = Assert.Throws<HttpRequestException>(() => SystemNodes.Get(url, null, 5));
        Assert.Contains(url, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com/path")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("file:///etc/passwd")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url at all")]
    public void Get_OnlyHttpAndHttpsAddressesAreAllowed(string? url)
    {
        var ex = Assert.Throws<ArgumentException>(() => SystemNodes.Get(url!));
        Assert.Contains("Web.Get", ex.Message);
        Assert.Contains("http", ex.Message);
        Assert.Contains("'url'", ex.Message);
    }

    [Fact]
    public void Get_HttpsAddressesAreAccepted_ReachingTheNetworkLayer()
    {
        // Nothing listens there: a connection error (not a URL error) proves the https scheme passed validation.
        var url = UnusedLocalUrl().Replace("http://", "https://");
        Assert.Throws<HttpRequestException>(() => SystemNodes.Get(url, null, 5));
    }

    [Fact]
    public void Get_InvalidHeaders_AreRefusedWithoutEchoingTheirValues()
    {
        var server = ServeText("ok");
        var secret = "tok-5ecret-value";

        var badName = Assert.Throws<ArgumentException>(() => SystemNodes.Get(server.Url(), Headers(("Bad Name", secret))));
        Assert.Contains("Bad Name", badName.Message);
        Assert.DoesNotContain(secret, badName.Message);

        var noValue = Assert.Throws<ArgumentException>(() => SystemNodes.Get(server.Url(), Headers(("Authorization", null))));
        Assert.Contains("Authorization", noValue.Message);

        var noName = Assert.Throws<ArgumentException>(() => SystemNodes.Get(server.Url(), Headers((" ", secret))));
        Assert.DoesNotContain(secret, noName.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public void Get_HeaderValuesNeverAppearInErrors()
    {
        var secret = "Bearer tok-5ecret-value";
        var refused = Assert.Throws<HttpRequestException>(() => SystemNodes.Get(UnusedLocalUrl(), Headers(("Authorization", secret))));
        Assert.DoesNotContain("5ecret", refused.ToString().Replace(secret, string.Empty));
        Assert.DoesNotContain(secret, refused.Message);

        var slow = Serve(_ => new ServerReply { Delay = TimeSpan.FromSeconds(20) });
        var timeout = Assert.Throws<TimeoutException>(
            () => SystemNodes.SendRequest("Web.Get", HttpMethod.Get, slow.Url(), null, null, Headers(("Authorization", secret)), TimeSpan.FromMilliseconds(200)));
        Assert.DoesNotContain("5ecret", timeout.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(601)]
    public void Get_TimeoutMustBeBetweenOneAndSixHundredSeconds(int seconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => SystemNodes.Get("http://127.0.0.1:1/", null, seconds));
        Assert.Contains("timeoutSeconds", ex.Message);
    }

    [Fact]
    public void Get_WorksEvenWhenCalledOnAThreadWhoseSynchronizationContextNeverRuns()
    {
        // A UI thread blocks on the node; if the request awaited back onto that context it would never complete.
        var server = ServeText("pong");
        var call = Task.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NeverRunningContext());
            return SystemNodes.Get(server.Url());
        });

        Assert.True(call.Wait(TimeSpan.FromSeconds(30)), "Web.Get deadlocked on the caller's synchronization context");
        Assert.Equal("pong", call.Result["body"]);
    }

    private sealed class NeverRunningContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // swallow: nothing ever pumps this context
        }
    }

    // ------------------------------------------------------------------ Web.Post

    [Fact]
    public void Post_SerializesNonTextBodiesAsJson_ByDefault()
    {
        var server = ServeText("{\"id\":7}", 201, "Created");
        var body = new Dictionary<string, object?>
        {
            ["name"] = "wall",
            ["count"] = 3d,
            ["tags"] = new List<object?> { "a", "b" },
            ["nothing"] = null,
        };

        var result = SystemNodes.Post(server.Url("/items"), body);

        Assert.Equal(201, result["status"]);
        Assert.Equal("{\"id\":7}", result["body"]);
        Assert.Equal(true, result["ok"]);
        var request = server.Last;
        Assert.Equal("POST", request.Method);
        Assert.Equal("/items", request.Target);
        Assert.Equal("application/json; charset=utf-8", request.Headers["Content-Type"]);
        Assert.Equal("{\"name\":\"wall\",\"count\":3.0,\"tags\":[\"a\",\"b\"],\"nothing\":null}", request.BodyText);
        Assert.Equal(request.Body.Length.ToString(CultureInfo.InvariantCulture), request.Headers["Content-Length"]);
    }

    [Fact]
    public void Post_ListBodies_AreSerializedAsJsonArrays()
    {
        var server = ServeText("ok");
        SystemNodes.Post(server.Url(), new List<object?> { 1d, 2.5, "x" });
        Assert.Equal("[1.0,2.5,\"x\"]", server.Last.BodyText);
    }

    [Fact]
    public void Post_TextBodiesAreSentAsTheyAre_NotQuotedAgain()
    {
        var server = ServeText("ok");
        SystemNodes.Post(server.Url(), "{\"already\":\"json\"}");
        Assert.Equal("{\"already\":\"json\"}", server.Last.BodyText);
    }

    [Theory]
    [InlineData("application/vnd.api+json")]
    [InlineData("APPLICATION/JSON")]
    [InlineData("text/json")]
    public void Post_AnyContentTypeContainingJson_SerializesAsJson(string contentType)
    {
        var server = ServeText("ok");
        SystemNodes.Post(server.Url(), new Dictionary<string, object?> { ["a"] = 1d }, contentType);
        Assert.Equal("{\"a\":1.0}", server.Last.BodyText);
        Assert.Contains(contentType.ToLowerInvariant().Split('/')[1], server.Last.Headers["Content-Type"].ToLowerInvariant());
    }

    [Fact]
    public void Post_NonJsonContentType_SendsTheValueAsInvariantText()
    {
        var server = ServeText("ok");
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = comma;
            SystemNodes.Post(server.Url(), 1234.5, "text/plain");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal("1234.5", server.Last.BodyText);
        Assert.Equal("text/plain; charset=utf-8", server.Last.Headers["Content-Type"]);
    }

    [Fact]
    public void Post_KeepsACharsetTheCallerChose()
    {
        var server = ServeText("ok");
        SystemNodes.Post(server.Url(), "abc", "text/plain; charset=utf-8");
        Assert.Equal("text/plain; charset=utf-8", server.Last.Headers["Content-Type"]);
    }

    [Fact]
    public void Post_NullBody_SendsAnEmptyBody()
    {
        var server = ServeText("ok");
        SystemNodes.Post(server.Url(), null);
        Assert.Empty(server.Last.Body);
        Assert.Equal("0", server.Last.Headers["Content-Length"]);
    }

    [Fact]
    public void Post_BlankContentType_FallsBackToJson()
    {
        var server = ServeText("ok");
        SystemNodes.Post(server.Url(), new Dictionary<string, object?> { ["a"] = "b" }, "  ");
        Assert.Equal("application/json; charset=utf-8", server.Last.Headers["Content-Type"]);
        Assert.Equal("{\"a\":\"b\"}", server.Last.BodyText);
    }

    [Fact]
    public void Post_Utf8WithoutByteOrderMark()
    {
        var server = ServeText("ok");
        SystemNodes.Post(server.Url(), "Größe ✓ 日本", "text/plain");
        var bytes = server.Last.Body;
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("Größe ✓ 日本", server.Last.BodyText);
    }

    [Fact]
    public void Post_SendsHeaders_AndAContentTypeHeaderOverridesTheParameter()
    {
        var server = ServeText("ok");

        SystemNodes.Post(
            server.Url(),
            "a=1&b=2",
            "text/plain",
            Headers(("Authorization", "Bearer abc"), ("X-Source", "camelgraph"), ("Content-Type", "application/x-www-form-urlencoded")));

        var headers = server.Last.Headers;
        Assert.Equal("Bearer abc", headers["Authorization"]);
        Assert.Equal("camelgraph", headers["X-Source"]);
        Assert.Equal("application/x-www-form-urlencoded", headers["Content-Type"]);
        Assert.Equal("a=1&b=2", server.Last.BodyText);
    }

    [Fact]
    public void Post_ErrorStatus_IsReturnedNotThrown()
    {
        var server = ServeText("{\"error\":\"bad\"}", 400, "Bad Request");
        var result = SystemNodes.Post(server.Url(), new Dictionary<string, object?> { ["x"] = 1d });
        Assert.Equal(400, result["status"]);
        Assert.Equal("{\"error\":\"bad\"}", result["body"]);
        Assert.Equal(false, result["ok"]);
    }

    [Fact]
    public void Post_SlowServer_TimesOut()
    {
        var slow = Serve(_ => new ServerReply { Delay = TimeSpan.FromSeconds(20) });
        var url = slow.Url();
        var ex = Assert.Throws<TimeoutException>(
            () => SystemNodes.SendRequest("Web.Post", HttpMethod.Post, url, "{}", "application/json", null, TimeSpan.FromMilliseconds(300)));
        Assert.Contains(url, ex.Message);
        Assert.Contains("Web.Post", ex.Message);
    }

    [Fact]
    public void Post_ConnectionRefused_IsAnExceptionWithTheUrl()
    {
        var url = UnusedLocalUrl();
        var ex = Assert.Throws<HttpRequestException>(() => SystemNodes.Post(url, "x"));
        Assert.Contains(url, ex.Message);
        Assert.Contains("Web.Post", ex.Message);
    }

    [Fact]
    public void Post_Validation_NamesTheNodeAndTheInput()
    {
        Assert.Contains("Web.Post", Assert.Throws<ArgumentException>(() => SystemNodes.Post("ftp://x/y", "b")).Message);
        Assert.Contains("'url'", Assert.Throws<ArgumentException>(() => SystemNodes.Post(" ", "b")).Message);
        Assert.Contains("content type", Assert.Throws<ArgumentException>(() => SystemNodes.Post("http://127.0.0.1:1/", "b", "not a type")).Message);
        Assert.Contains("timeoutSeconds", Assert.Throws<ArgumentOutOfRangeException>(() => SystemNodes.Post("http://127.0.0.1:1/", "b", "text/plain", null, 0)).Message);
        Assert.Contains("timeoutSeconds", Assert.Throws<ArgumentOutOfRangeException>(() => SystemNodes.Post("http://127.0.0.1:1/", "b", "text/plain", null, 601)).Message);
    }

    [Fact]
    public void Post_InvalidHeaders_AreRefusedBeforeAnythingIsSent()
    {
        var server = ServeText("ok");
        var secret = "tok-5ecret-value";
        var ex = Assert.Throws<ArgumentException>(() => SystemNodes.Post(server.Url(), "b", "text/plain", Headers(("Bad Name", secret))));
        Assert.DoesNotContain(secret, ex.Message);
        Assert.Empty(server.Requests);
    }

    // --------------------------------------------------- registration & engine

    [Fact]
    public void Registered_InTheSystemCategory_WithExplicitRoles_AndPortKinds()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var expected = new (string Name, GraphFunction Role, string[]? Keys, string[]? Kinds)[]
        {
            ("System.Environment", GraphFunction.Info, new[] { "userName", "machineName", "osVersion", "currentDirectory", "tempPath", "documentsPath", "appDataPath" }, new[] { "text", "text", "text", "file", "file", "file", "file" }),
            ("Graph.Folder", GraphFunction.Info, null, null),
            ("System.OpenPath", GraphFunction.Modify, null, null),
            ("System.Run", GraphFunction.Modify, new[] { "exitCode", "output", "error" }, new[] { "integer", "text", "text" }),
            ("Web.Download", GraphFunction.Modify, new[] { "path", "status", "ok", "sizeBytes" }, new[] { "file", "integer", "boolean", "number" }),
            ("Web.Get", GraphFunction.Info, new[] { "status", "body", "ok" }, new[] { "integer", "text", "boolean" }),
            ("Web.Post", GraphFunction.Modify, new[] { "status", "body", "ok" }, new[] { "integer", "text", "boolean" }),
        };

        var own = registry.Definitions.Where(d => d.Id.StartsWith("CamelGraph.Nodes.SystemNodes.", StringComparison.Ordinal)).ToList();
        Assert.Equal(expected.Length, own.Count);

        foreach (var (name, role, keys, kinds) in expected)
        {
            var definition = Assert.Single(own, d => d.Name == name);
            Assert.Equal("System", definition.Category);
            Assert.Equal(role, definition.Function);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), name);
            if (keys != null)
            {
                Assert.Equal(keys, definition.Outputs.Select(o => o.Name));
                Assert.Equal(kinds, definition.Outputs.Select(o => o.Kind));
            }
        }
    }

    [Fact]
    public void Registered_OptionalInputs_HaveTheDocumentedDefaultsAndRanges()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var run = registry.Definitions.Single(d => d.Name == "System.Run");
        Assert.Equal(new[] { "executable", "arguments", "workingDirectory", "timeoutSeconds" }, run.Inputs.Select(i => i.Name));
        Assert.False(run.Inputs[0].HasDefault);
        Assert.Equal(string.Empty, run.Inputs[1].DefaultValue);
        Assert.Equal(60, run.Inputs[3].DefaultValue);
        Assert.Equal(1d, run.Inputs[3].Range!.Min);
        Assert.Equal(3600d, run.Inputs[3].Range!.Max);

        var get = registry.Definitions.Single(d => d.Name == "Web.Get");
        Assert.Equal(new[] { "url", "headers", "timeoutSeconds" }, get.Inputs.Select(i => i.Name));
        Assert.Equal(30, get.Inputs[2].DefaultValue);
        Assert.Equal(600d, get.Inputs[2].Range!.Max);

        var post = registry.Definitions.Single(d => d.Name == "Web.Post");
        Assert.Equal(new[] { "url", "body", "contentType", "headers", "timeoutSeconds" }, post.Inputs.Select(i => i.Name));
        Assert.Equal("application/json", post.Inputs[2].DefaultValue);
        Assert.False(post.Inputs[1].HasDefault);

        var open = registry.Definitions.Single(d => d.Name == "System.OpenPath");
        Assert.Equal(new[] { "path", "reveal" }, open.Inputs.Select(i => i.Name));
        Assert.Equal(false, open.Inputs[1].DefaultValue);
    }

    [Fact]
    public void Engine_RunsWebGet_AndSplitsTheThreeOutputs()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var server = ServeText("pong", 202, "Accepted");
        var graph = new GraphModel();
        var url = new StringInputNode { Value = server.Url("/ping") };
        var get = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "Web.Get"));
        graph.AddNode(url);
        graph.AddNode(get);
        Assert.True(graph.Connect(url.OutPorts[0], get.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, get.State);
        Assert.Equal(new[] { "status", "body", "ok" }, get.OutPorts.Select(p => p.Name));
        Assert.Equal(202, get.OutPorts[0].Value);
        Assert.Equal("pong", get.OutPorts[1].Value);
        Assert.Equal(true, get.OutPorts[2].Value);
    }

    [Fact]
    public void Engine_RunsSystemEnvironment_WithoutAnyInput()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var graph = new GraphModel();
        var environment = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "System.Environment"));
        graph.AddNode(environment);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, environment.State);
        Assert.Equal(7, environment.OutPorts.Count);
        Assert.Equal(Path.GetTempPath(), environment.OutPorts[4].Value);
    }

    [Fact]
    public void Engine_ABadUrl_BecomesAnErrorNode_NotACrash()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var graph = new GraphModel();
        var url = new StringInputNode { Value = "ftp://nope/" };
        var get = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "Web.Get"));
        graph.AddNode(url);
        graph.AddNode(get);
        Assert.True(graph.Connect(url.OutPorts[0], get.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, get.State);
        Assert.Contains("http", get.StateMessage);
    }
}
