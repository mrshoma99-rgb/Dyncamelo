using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CamelGraph.Core.Loader;
using Newtonsoft.Json;

namespace CamelGraph.Nodes;

/// <summary>
/// Nodes that reach outside the graph: facts about this computer, opening files in their default application, running
/// another program, and calling web services. Reading nodes are Info; nodes that launch a process or post data are
/// Modify, so the Script Player asks for trust before running a script that contains them.
/// </summary>
[NodeCategory("System")]
public static class SystemNodes
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    // ----------------------------------------------------------------- System

    /// <summary>Reports who and where this graph is running: user, machine, operating system and well-known folders.</summary>
    /// <returns>Dictionary with "userName", "machineName", "osVersion", "currentDirectory", "tempPath", "documentsPath" and "appDataPath".</returns>
    [NodeName("System.Environment")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("userName", "machineName", "osVersion", "currentDirectory", "tempPath", "documentsPath", "appDataPath")]
    [PortKinds("text", "text", "text", "file", "file", "file", "file")]
    [NodeDescription("Reports the Windows user, computer name, operating system and the current, temp, Documents and AppData folders.")]
    [NodeSearchTags("user", "username", "machine", "computer", "os", "windows", "temp", "documents", "appdata", "folder", "whoami")]
    public static Dictionary<string, object?> GetEnvironment()
    {
        return new Dictionary<string, object?>
        {
            ["userName"] = Environment.UserName,
            ["machineName"] = Environment.MachineName,
            ["osVersion"] = DescribeOperatingSystem(),
            ["currentDirectory"] = Directory.GetCurrentDirectory(),
            ["tempPath"] = Path.GetTempPath(),
            ["documentsPath"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["appDataPath"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };
    }

    /// <summary>
    /// Opens a file or folder with its default application, or (reveal = true) shows it selected in the file manager.
    /// </summary>
    /// <param name="path">The file or folder to open.</param>
    /// <param name="reveal">True to show the item in Explorer (Windows) / open its containing folder (other systems) instead of opening it.</param>
    /// <returns>The path that was opened, for sequencing further nodes.</returns>
    [NodeName("System.OpenPath")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.RunsPrograms)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Opens a file or folder with its default application, or shows it selected in Explorer when reveal is true.")]
    [NodeSearchTags("open", "explorer", "reveal", "show", "launch", "folder", "default app", "finder")]
    public static string OpenPath(string path, bool reveal = false)
    {
        PathNodes.RequireText(path, "System.OpenPath", nameof(path), "a file or folder path");
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("System.OpenPath: '" + path + "' does not exist, so there is nothing to open.", path);
        }

        var startInfo = BuildOpenStartInfo(Path.GetFullPath(path), reveal, PathNodes.IsWindows);
        try
        {
            using (Process.Start(startInfo))
            {
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("System.OpenPath: could not open '" + path + "'. " + ex.Message, ex);
        }

        return path;
    }

    /// <summary>
    /// Runs a program and waits for it to finish, capturing what it prints. The whole process tree is stopped when the
    /// timeout expires.
    /// </summary>
    /// <param name="executable">The program to start: a full path, or a name found on the PATH.</param>
    /// <param name="arguments">The command-line arguments as one string.</param>
    /// <param name="workingDirectory">The folder to run in; empty uses the current folder.</param>
    /// <param name="timeoutSeconds">How long to wait before stopping the program (1 to 3600 seconds).</param>
    /// <returns>Dictionary with "exitCode", "output" (standard output) and "error" (standard error).</returns>
    [NodeName("System.Run")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.RunsPrograms)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [MultiReturn("exitCode", "output", "error")]
    [PortKinds("integer", "text", "text")]
    [NodeDescription("Runs a program with arguments, waits for it (stopped after the timeout) and returns its exit code, output and error text.")]
    [NodeSearchTags("run", "execute", "process", "command", "exe", "cmd", "shell", "script", "launch")]
    public static Dictionary<string, object?> Run(
        string executable,
        string arguments = "",
        string workingDirectory = "",
        [NodeRange(1, 3600)] int timeoutSeconds = 60)
    {
        if (timeoutSeconds < 1 || timeoutSeconds > 3600)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "System.Run: 'timeoutSeconds' must be between 1 and 3600.");
        }

        return RunProcess(executable, arguments, workingDirectory, TimeSpan.FromSeconds(timeoutSeconds));
    }

    // -------------------------------------------------------------------- Web

    /// <summary>Sends an HTTP GET request. Only http and https addresses are allowed; a non-2xx answer is returned, not thrown.</summary>
    /// <param name="url">The address, starting with http:// or https://.</param>
    /// <param name="headers">Optional request headers (name to value), e.g. an Authorization token.</param>
    /// <param name="timeoutSeconds">How long to wait for the answer (1 to 600 seconds).</param>
    /// <returns>Dictionary with "status" (HTTP code), "body" (response text) and "ok" (true for 2xx).</returns>
    [NodeName("Web.Get")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.UsesNetwork)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("status", "body", "ok")]
    [PortKinds("integer", "text", "boolean")]
    [NodeDescription("Downloads text from an http(s) address with GET; an error status such as 404 is returned (ok = false), only a network failure or timeout is an error.")]
    [NodeSearchTags("http", "https", "request", "download", "api", "rest", "url", "fetch", "json", "web")]
    public static Dictionary<string, object?> Get(
        string url,
        IDictionary? headers = null,
        [NodeRange(1, 600)] int timeoutSeconds = 30)
    {
        RequireTimeout(timeoutSeconds, "Web.Get");
        return SendRequest("Web.Get", HttpMethod.Get, url, null, null, headers, TimeSpan.FromSeconds(timeoutSeconds));
    }

    /// <summary>Sends an HTTP POST request with a body, e.g. JSON to a web hook. Non-text bodies are serialized as JSON when the content type says json.</summary>
    /// <param name="url">The address, starting with http:// or https://.</param>
    /// <param name="body">The data to send: text as it is; anything else becomes JSON when the content type contains "json". Nothing wired sends an empty body.</param>
    /// <param name="contentType">The Content-Type of the body; defaults to application/json.</param>
    /// <param name="headers">Optional request headers (name to value), e.g. an Authorization token.</param>
    /// <param name="timeoutSeconds">How long to wait for the answer (1 to 600 seconds).</param>
    /// <returns>Dictionary with "status" (HTTP code), "body" (response text) and "ok" (true for 2xx).</returns>
    [NodeName("Web.Post")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.UsesNetwork)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [MultiReturn("status", "body", "ok")]
    [PortKinds("integer", "text", "boolean")]
    [NodeDescription("Sends data to an http(s) address with POST (JSON by default, e.g. a Teams or Slack web hook); an error status is returned (ok = false), only a network failure or timeout is an error.")]
    [NodeSearchTags("http", "https", "post", "send", "webhook", "teams", "slack", "api", "rest", "json", "notify", "upload")]
    public static Dictionary<string, object?> Post(
        string url,
        object? body,
        string contentType = "application/json",
        IDictionary? headers = null,
        [NodeRange(1, 600)] int timeoutSeconds = 30)
    {
        RequireTimeout(timeoutSeconds, "Web.Post");
        var type = string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType.Trim();
        return SendRequest("Web.Post", HttpMethod.Post, url, BodyText(body, type), type, headers, TimeSpan.FromSeconds(timeoutSeconds));
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    private static string DescribeOperatingSystem()
    {
        try
        {
            var description = RuntimeInformation.OSDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException || ex is InvalidOperationException)
        {
            // fall through to the older API
        }

        return Environment.OSVersion.VersionString;
    }

    /// <summary>How to ask the shell to open a path, or to show it in the file manager.</summary>
    internal static ProcessStartInfo BuildOpenStartInfo(string fullPath, bool reveal, bool windows)
    {
        if (!reveal)
        {
            return new ProcessStartInfo(fullPath) { UseShellExecute = true };
        }

        if (windows)
        {
            var target = fullPath.Length > 3 ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : fullPath;
            return new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"") { UseShellExecute = true };
        }

        var containing = Path.GetDirectoryName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
        return new ProcessStartInfo(string.IsNullOrEmpty(containing) ? fullPath : containing) { UseShellExecute = true };
    }

    /// <summary>Starts a process, reads stdout / stderr without blocking, and stops the whole tree when the timeout expires.</summary>
    internal static Dictionary<string, object?> RunProcess(string executable, string? arguments, string? workingDirectory, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new ArgumentException("System.Run requires a program to start. Wire the program's name or full path into the 'executable' input.", nameof(executable));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException("System.Run: the working directory '" + workingDirectory + "' does not exist.");
            }

            startInfo.WorkingDirectory = workingDirectory;
        }

        var output = new List<string>();
        var error = new List<string>();
        using (var process = new Process { StartInfo = startInfo })
        {
            // Both streams are read asynchronously: reading one to the end while the other fills its pipe would deadlock.
            process.OutputDataReceived += (sender, e) => AddLine(output, e.Data);
            process.ErrorDataReceived += (sender, e) => AddLine(error, e.Data);

            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                throw new InvalidOperationException(
                    "System.Run: could not start '" + executable + "'. " + ex.Message + " Check the program name, or give its full path.", ex);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                // Nothing will ever be typed: a program that reads the keyboard sees the end of its input instead of waiting.
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // the program already exited
            }

            if (process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue)))
            {
                // WaitForExit(timeout) returns before the asynchronous readers have seen the end of the streams; the overload without
                // a timeout is the one that waits for them. A helper that keeps the pipes open must not hold the node up for ever.
                Task.Run(() => process.WaitForExit()).Wait(5000);
            }
            else
            {
                // A program that exited but left a helper holding the output pipes open also lands here; that is a finish, not a timeout.
                if (!process.HasExited)
                {
                    KillProcessTree(process);
                    throw new TimeoutException(
                        "System.Run: '" + executable + "' did not finish within " +
                        timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " second(s) and was stopped. Raise 'timeoutSeconds' if it needs longer.");
                }
            }

            return new Dictionary<string, object?>
            {
                ["exitCode"] = process.ExitCode,
                ["output"] = JoinLines(output),
                ["error"] = JoinLines(error),
            };
        }
    }

    private static void AddLine(List<string> lines, string? line)
    {
        if (line == null)
        {
            return;
        }

        lock (lines)
        {
            lines.Add(line);
        }
    }

    private static string JoinLines(List<string> lines)
    {
        lock (lines)
        {
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Stops a process and everything it started. netstandard2.0 has no Process.Kill(bool), so the runtime's own
    /// method is used when the host has it (.NET 5+), otherwise taskkill /T on Windows.
    /// </summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            var kill = typeof(Process).GetMethod("Kill", new[] { typeof(bool) });
            if (kill != null)
            {
                kill.Invoke(process, new object[] { true });
                process.WaitForExit(2000);
                return;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is InvalidOperationException || ex is Win32Exception)
        {
            // fall back to the portable ways below
        }

        if (PathNodes.IsWindows)
        {
            try
            {
                var taskkill = new ProcessStartInfo("taskkill", "/PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var killer = Process.Start(taskkill))
                {
                    killer?.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                // fall back to killing just the process
            }
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }

            process.WaitForExit(2000);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
        {
            // already gone
        }
    }

    private static void RequireTimeout(int timeoutSeconds, string nodeName)
    {
        if (timeoutSeconds < 1 || timeoutSeconds > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), nodeName + ": 'timeoutSeconds' must be between 1 and 600.");
        }
    }

    private static string BodyText(object? body, string contentType)
    {
        if (body == null)
        {
            return string.Empty;
        }

        if (body is string text)
        {
            return text;
        }

        if (contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return JsonConvert.SerializeObject(body, Formatting.None);
        }

        return CellText.Format(body);
    }

    private static Uri ParseHttpUrl(string? url, string nodeName)
    {
        if (!string.IsNullOrWhiteSpace(url) &&
            Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri;
        }

        throw new ArgumentException(
            nodeName + " requires an http:// or https:// address, e.g. \"https://example.com/api\". Wire it into the 'url' input.",
            nameof(url));
    }

    /// <summary>Sends one request and turns the answer into status / body / ok. Header values are never put into messages.</summary>
    internal static Dictionary<string, object?> SendRequest(
        string nodeName,
        HttpMethod method,
        string url,
        string? body,
        string? contentType,
        IDictionary? headers,
        TimeSpan timeout)
    {
        var uri = ParseHttpUrl(url, nodeName);
        using (var request = new HttpRequestMessage(method, uri))
        {
            if (body != null)
            {
                var content = new StringContent(body, Utf8NoBom);
                if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
                {
                    throw new ArgumentException(
                        nodeName + ": '" + contentType + "' is not a valid content type. Use something like application/json or text/plain.",
                        nameof(contentType));
                }

                if (string.IsNullOrEmpty(mediaType.CharSet))
                {
                    mediaType.CharSet = "utf-8";
                }

                content.Headers.ContentType = mediaType;
                request.Content = content;
            }

            ApplyHeaders(request, headers, nodeName);

            using (var cancellation = new CancellationTokenSource(timeout))
            {
                try
                {
                    // Run on the thread pool so a UI synchronization context can never deadlock the wait below.
                    var answer = Task.Run(() => ExchangeAsync(request, cancellation.Token)).GetAwaiter().GetResult();
                    return new Dictionary<string, object?>
                    {
                        ["status"] = answer.Status,
                        ["body"] = answer.Body,
                        ["ok"] = answer.Ok,
                    };
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        nodeName + ": the request to '" + url + "' timed out after " +
                        timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " second(s). Raise 'timeoutSeconds' or check the address.");
                }
                catch (HttpRequestException ex)
                {
                    throw new HttpRequestException(nodeName + ": the request to '" + url + "' failed: " + Innermost(ex).Message, ex);
                }
            }
        }
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException != null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    private static void ApplyHeaders(HttpRequestMessage request, IDictionary? headers, string nodeName)
    {
        if (headers == null)
        {
            return;
        }

        foreach (DictionaryEntry entry in headers)
        {
            var name = entry.Key == null ? string.Empty : CellText.Format(entry.Key).Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException(nodeName + ": every header needs a name (the key of the 'headers' dictionary).", nameof(headers));
            }

            if (entry.Value == null)
            {
                throw new ArgumentException(nodeName + ": the header '" + name + "' has no value.", nameof(headers));
            }

            // Only the NAME may appear in an error message: values are often tokens.
            var value = CellText.Format(entry.Value);
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Content == null)
                {
                    continue; // meaningless without a body, so a shared headers dictionary can serve GET and POST
                }

                request.Content.Headers.Remove("Content-Type");
                if (!request.Content.Headers.TryAddWithoutValidation("Content-Type", value))
                {
                    throw new ArgumentException(nodeName + ": the 'Content-Type' header is not valid.", nameof(headers));
                }

                continue;
            }

            if (request.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }

            if (request.Content != null && request.Content.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }

            if (request.Content == null && name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                continue; // an entity header (Content-Length, ...) has no meaning on a request without a body
            }

            throw new ArgumentException(nodeName + ": '" + name + "' is not a valid header name.", nameof(headers));
        }
    }

    private sealed class Answer
    {
        public Answer(int status, string body, bool ok)
        {
            Status = status;
            Body = body;
            Ok = ok;
        }

        public int Status { get; }

        public string Body { get; }

        public bool Ok { get; }
    }

    private static async Task<Answer> ExchangeAsync(HttpRequestMessage request, CancellationToken token)
    {
        using (var response = await Http.Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false))
        {
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new Answer((int)response.StatusCode, DecodeBody(bytes, response.Content.Headers.ContentType), response.IsSuccessStatusCode);
        }
    }

    /// <summary>Decodes a response body using its declared charset (UTF-8 when absent or unknown); a byte-order mark is dropped.</summary>
    private static string DecodeBody(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        Encoding encoding = Utf8NoBom;
        var charset = contentType?.CharSet?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // unknown charset name: UTF-8 is the best guess
            }
        }

        var skip = 0;
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0 && bytes.Length >= preamble.Length)
        {
            var matches = true;
            for (var i = 0; i < preamble.Length; i++)
            {
                if (bytes[i] != preamble[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                skip = preamble.Length;
            }
        }

        return encoding.GetString(bytes, skip, bytes.Length - skip);
    }

    /// <summary>The one HttpClient of the library: created on first use, shared by every call (per-call timeouts use a cancellation token).</summary>
    private static class Http
    {
        internal static readonly HttpClient Client = Create();

        private static HttpClient Create()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CamelGraph");
            return client;
        }
    }
}
