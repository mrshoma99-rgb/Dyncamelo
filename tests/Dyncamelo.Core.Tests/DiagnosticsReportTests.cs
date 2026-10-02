using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Editing;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class DiagnosticsReportTests
{
    private static readonly DateTime When = new DateTime(2026, 10, 2, 8, 30, 0);

    private static DiagnosticsInfo Info()
    {
        var info = new DiagnosticsInfo
        {
            DyncameloVersion = "0.45.1",
            Host = "Autodesk Navisworks Manage 2024 (API 21.0)",
            Process = "Roamer, 64-bit",
            OperatingSystem = "Windows 10.0.26100",
            Runtime = ".NET Framework 4.8.9347.0",
            ErrorLogTail = "2026-10-01 14:00:00  a command\nSystem.InvalidOperationException: boom",
        };
        info.InstalledBundles.Add("Dyncamelo.bundle");
        info.InstalledBundles.Add("BIMCamel.bundle");
        info.LoadedLibraries.Add("Nodify 7.3.0.0 - C:\\Users\\sam\\AppData\\Roaming\\Autodesk\\ApplicationPlugins\\Dyncamelo.bundle\\2024\\Nodify.dll");
        info.Editor.Add("Node library: 580 nodes");
        return info;
    }

    [Fact]
    public void TheReportHasEverySectionAndTheFacts()
    {
        var text = DiagnosticsReport.Build(Info(), When);

        Assert.StartsWith("Dyncamelo diagnostics", text);
        Assert.Contains("Made 2026-10-02 08:30 (local time)", text);
        Assert.Contains("Dyncamelo: 0.45.1", text);
        Assert.Contains("Host:      Autodesk Navisworks Manage 2024 (API 21.0)", text);
        Assert.Contains("  BIMCamel.bundle", text);
        Assert.Contains("  Nodify 7.3.0.0", text);
        Assert.Contains("  Node library: 580 nodes", text);
        Assert.Contains("End of errors.log", text);
        Assert.Contains("  System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void MissingPiecesSayWhatTheyAreInsteadOfBeingBlank()
    {
        var text = DiagnosticsReport.Build(new DiagnosticsInfo { ErrorLogMissing = true }, When);

        Assert.Contains("Dyncamelo: unknown", text);
        Assert.Contains("(none found)", text);
        Assert.Contains("(not available)", text);
        Assert.Contains("(no errors.log: nothing has been logged)", text);

        Assert.Contains("(empty)", DiagnosticsReport.Build(new DiagnosticsInfo(), When));
    }

    [Fact]
    public void RedactionRemovesTheProfileTheUserNameAndTheComputerNameIgnoringCase()
    {
        var text = "C:\\Users\\Sam\\AppData\\Roaming\\x.dll on ITMIL107 by sam, and c:\\users\\SAM again";
        var cleaned = DiagnosticsReport.Redact(text, new[]
        {
            new KeyValuePair<string, string>("C:\\Users\\Sam", "%USERPROFILE%"),
            new KeyValuePair<string, string>("ITMIL107", "<computer>"),
            new KeyValuePair<string, string>("sam", "<user>"),
        });

        Assert.Equal("%USERPROFILE%\\AppData\\Roaming\\x.dll on <computer> by <user>, and %USERPROFILE% again", cleaned);
    }

    [Fact]
    public void ShortNeedlesAreIgnoredSoOrdinaryWordsSurvive()
    {
        Assert.Equal("a to of", DiagnosticsReport.Redact("a to of", new[] { new KeyValuePair<string, string>("to", "<user>"), new KeyValuePair<string, string>("", "x") }));
    }

    [Fact]
    public void TheTailKeepsTheLastLinesAndSaysWhenItCut()
    {
        var text = string.Join("\n", Enumerable.Range(1, 10).Select(i => "line " + i));

        Assert.Equal("line 1\nline 2", DiagnosticsReport.Tail("line 1\nline 2\n", 5, 1000));
        var tail = DiagnosticsReport.Tail(text, 3, 1000);
        Assert.Equal("(earlier lines left out)\nline 8\nline 9\nline 10", tail);
        Assert.StartsWith("(earlier lines left out)\n", DiagnosticsReport.Tail(text, 100, 20));
        Assert.Equal(string.Empty, DiagnosticsReport.Tail(string.Empty, 3, 10));
    }
}
