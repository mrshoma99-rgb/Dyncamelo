using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>
/// The editor the way Navisworks hosts it: a WinForms ElementHost on a dock pane, with no WPF Application in the process (Roamer is
/// not a WPF application) and no WPF Window around the control. The ordinary UI tests share one STA thread that has an
/// Application, so a node that only fails in the host — as the Color Picker did, and then any node taken from the library — never
/// fails there. This runs the probe (<c>tests/Dyncamelo.UI.HostProbe</c>) as a process of its own: it picks in the node library, then
/// adds every node, and reports on its last line. A process of its own, because what WPF and WinForms leave behind when an AppDomain
/// holding them is unloaded can hang or kill the test host.
/// </summary>
public class HostedLikeNavisworksTests
{
    [Theory]
    [InlineData("DyncameloDark", false)]
    [InlineData("Light", true)]
    public void EveryNodeCanBeAddedFromTheLibraryInAnElementHostWithoutAnApplication(string palette, bool collapsed)
    {
        // CodeBase is where the assembly came from even when the test runner shadow-copied it.
        var folder = Path.GetDirectoryName(new Uri(typeof(HostedLikeNavisworksTests).Assembly.CodeBase).LocalPath)!;
        var probe = Path.Combine(folder, "Dyncamelo.UI.HostProbe.exe");
        Assert.True(File.Exists(probe), "The probe was not built next to the tests: " + probe);

        var output = new StringBuilder();
        var result = (string?)null;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(probe, palette + " " + (collapsed ? "true" : "false"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = folder,
            },
        };
        using var resultSeen = new ManualResetEventSlim();
        var lines = new object();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            lock (lines)
            {
                output.AppendLine(e.Data);
                if (e.Data.StartsWith("RESULT: ", StringComparison.Ordinal))
                {
                    result = e.Data.Substring("RESULT: ".Length);
                    resultSeen.Set();
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (lines)
                {
                    output.AppendLine("stderr: " + e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var finished = resultSeen.Wait(TimeSpan.FromMinutes(3)) || process.HasExited;
            if (!finished)
            {
                Assert.Fail("The hosted editor did not finish within 3 minutes (its message loop is stuck).\n" + output);
            }

            // The probe leaves as soon as it has reported; give it a moment, then end it whatever it is still doing.
            process.WaitForExit(15000);
            string text;
            lock (lines)
            {
                text = result ?? "(no result line)\n" + output;
            }

            Assert.True(text.StartsWith("OK", StringComparison.Ordinal), text);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }
    }
}
