using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CamelGraph.Core.Editing;
using Xunit;

namespace CamelGraph.Core.Tests;

public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dyc-recovery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }

    private static void WriteText(string path) => File.WriteAllText(path, "{}");

    // A process id nothing is running under.
    private static int DeadProcessId()
    {
        for (var pid = 2_000_000_000; ; pid++)
        {
            try
            {
                using (Process.GetProcessById(pid))
                {
                }
            }
            catch (ArgumentException)
            {
                return pid;
            }
        }
    }

    [Fact]
    public void WriteCreatesTheGraphAndItsNoteAndClearRemovesThem()
    {
        using var store = new RecoveryStore(_directory);

        var written = store.Write(WriteText, @"C:\work\plan.dyc", "Plan");

        Assert.True(written);
        Assert.True(store.HasAutosave);
        Assert.Single(Directory.GetFiles(_directory, "*.json"));

        store.Clear();

        Assert.False(store.HasAutosave);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void AWriteThatFailsReportsFalseAndLeavesThePreviousCopy()
    {
        using var store = new RecoveryStore(_directory);
        Assert.True(store.Write(WriteText, null, "First"));
        var before = File.ReadAllText(store.GraphPath);

        var written = store.Write(path => throw new InvalidOperationException("boom"), null, "Second");

        Assert.False(written);
        Assert.Equal(before, File.ReadAllText(store.GraphPath));
    }

    [Fact]
    public void ARewriteReplacesTheCopyWithoutLeavingTemporaryFiles()
    {
        using var store = new RecoveryStore(_directory);
        Assert.True(store.Write(path => File.WriteAllText(path, "one"), null, "A"));
        Assert.True(store.Write(path => File.WriteAllText(path, "two"), null, "A"));

        Assert.Equal("two", File.ReadAllText(store.GraphPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void ASessionDoesNotOfferItsOwnAutosave()
    {
        using var store = new RecoveryStore(_directory);
        store.Write(WriteText, null, "Mine");

        Assert.Empty(store.FindOrphans());
    }

    [Fact]
    public void AnAutosaveLeftByAnEndedSessionOfThisProcessIsOffered()
    {
        var first = new RecoveryStore(_directory);
        first.Write(WriteText, @"C:\work\plan.dyc", "Plan");
        first.Dispose();   // the pane closed; the files stay because the work was unsaved

        using var second = new RecoveryStore(_directory);
        var orphans = second.FindOrphans();

        var candidate = Assert.Single(orphans);
        Assert.Equal(@"C:\work\plan.dyc", candidate.OriginalPath);
        Assert.Equal("Plan", candidate.GraphName);
        Assert.InRange(candidate.SavedAtUtc, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
        Assert.Contains("Plan", candidate.Describe());
        Assert.Contains("plan.dyc", candidate.Describe());
    }

    [Fact]
    public void AnAutosaveOfADeadProcessIsOfferedNewestFirstAndDiscardRemovesIt()
    {
        Directory.CreateDirectory(_directory);
        var dead = DeadProcessId();
        var older = Path.Combine(_directory, "autosave-" + dead + "-aaaa.dyc");
        var newer = Path.Combine(_directory, "autosave-" + dead + "-bbbb.dyc");
        WriteText(older);
        WriteText(newer);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddHours(-1));

        using var store = new RecoveryStore(_directory);
        var orphans = store.FindOrphans();

        Assert.Equal(new[] { newer, older }, orphans.Select(o => o.GraphPath).ToArray());

        store.Discard(orphans[0]);

        Assert.False(File.Exists(newer));
        Assert.Equal(older, Assert.Single(store.FindOrphans()).GraphPath);
    }

    [Fact]
    public void AnAutosaveOfAnotherRunningProcessIsLeftAlone()
    {
        Directory.CreateDirectory(_directory);
        var parent = Process.GetCurrentProcess().Id;
        using var child = StartSleepingProcess();
        WriteText(Path.Combine(_directory, "autosave-" + child.Id + "-cccc.dyc"));

        using var store = new RecoveryStore(_directory);

        Assert.Empty(store.FindOrphans());
        Assert.NotEqual(parent, child.Id);
        child.Kill();
    }

    [Fact]
    public void AMissingFolderOffersNothing()
    {
        using var store = new RecoveryStore(Path.Combine(_directory, "nowhere"));

        Assert.Empty(store.FindOrphans());
        Assert.False(store.HasAutosave);
    }

    [Fact]
    public void ADamagedNoteStillOffersTheGraph()
    {
        Directory.CreateDirectory(_directory);
        var dead = DeadProcessId();
        WriteText(Path.Combine(_directory, "autosave-" + dead + "-dddd.dyc"));
        File.WriteAllText(Path.Combine(_directory, "autosave-" + dead + "-dddd.json"), "{ not json");

        using var store = new RecoveryStore(_directory);
        var candidate = Assert.Single(store.FindOrphans());

        Assert.Null(candidate.OriginalPath);
        Assert.Equal(string.Empty, candidate.GraphName);
    }

    private static Process StartSleepingProcess()
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping", "-n 30 127.0.0.1")
            : new ProcessStartInfo("sleep", "30");
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.CreateNoWindow = true;
        return Process.Start(info)!;
    }
}
