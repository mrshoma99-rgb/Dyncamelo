using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.SelfTest;
using CamelGraph.UI.Mvvm;
using CamelGraph.UI.Services;

namespace CamelGraph.UI.ViewModels;

/// <summary>Help for reporting a problem: the diagnostics a bug report needs, and a self-test that tells whether the nodes work on this Navisworks.</summary>
public partial class GraphEditorViewModel
{
    private ICommand? _copyDiagnosticsCommand;
    private bool _checkForUpdates = true;

    /// <summary>True (the default) to ask GitHub for the newest release number once a day when the editor opens. The host reads this before it starts the check.</summary>
    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set
        {
            if (SetProperty(ref _checkForUpdates, value))
            {
                _settings.SetCheckForUpdates(value);
            }
        }
    }

    /// <summary>
    /// Set by the host: describes the host application ("Autodesk Navisworks Manage 2024 (API 21.0)") for the diagnostics.
    /// May throw; the report then says the host could not be read.
    /// </summary>
    public Func<string>? HostDescriptionProvider { get; set; }

    /// <summary>Copies the diagnostics (versions, installed plug-ins, the end of the error log) to the clipboard.</summary>
    public ICommand CopyDiagnosticsCommand => _copyDiagnosticsCommand ??= new RelayCommand(CopyDiagnostics);

    /// <summary>The diagnostics text: what is installed and what went wrong lately, without the person's name, computer or models.</summary>
    public string BuildDiagnostics()
    {
        string? host = null;
        try
        {
            host = HostDescriptionProvider?.Invoke();
        }
        catch (Exception ex)
        {
            host = "could not be read (" + ex.GetType().Name + ")";
        }

        return DiagnosticsCollector.Build(this, host);
    }

    private void CopyDiagnostics()
    {
        var done = PutOnClipboard(BuildDiagnostics(), "camelgraph-diagnostics.txt", "Copy Diagnostics");
        StatusMessage = done == null ? "Diagnostics copied. Paste them into your bug report." : done;
    }

    // Returns null when the text is on the clipboard, else the sentence to show (the text was saved to a file instead).
    private string? PutOnClipboard(string text, string fallbackFileName, string title)
    {
        try
        {
            Clipboard.SetText(text);
            return null;
        }
        catch (Exception)
        {
            // The clipboard can be held by another program; a file is the next best thing to paste from.
            try
            {
                var path = Path.Combine(Path.GetTempPath(), fallbackFileName);
                File.WriteAllText(path, text);
                return "The clipboard was busy. Saved to " + path;
            }
            catch (Exception ex)
            {
                Dialogs.ShowError("The text could not be copied or saved: " + ex.Message, title);
                return "Could not copy or save the text.";
            }
        }
    }

    // ----- node packs -----------------------------------------------------------------------------------------------------

    private ICommand? _showNodePacksCommand;

    /// <summary>Opens a folder in the file manager; the host or a test can replace it. False when it could not be opened.</summary>
    public Func<string, bool> FolderOpener { get; set; } = OpenFolderInExplorer;

    /// <summary>Help ▸ Node Packs…: says where your own nodes go and what was loaded, and offers to open that folder.</summary>
    public ICommand ShowNodePacksCommand => _showNodePacksCommand ??= new RelayCommand(ShowNodePacks);

    /// <summary>The folder for your own packs, <c>%APPDATA%\CamelGraph\Packages</c>; a test can point it somewhere else.</summary>
    public string NodePackFolder { get; set; } = CamelGraph.Core.Loader.NodePacks.UserFolder;

    /// <summary>The text of the Node Packs dialog: the folder, what was loaded, what failed, and that Navisworks must be restarted.</summary>
    /// <param name="folder">The folder for your own packs.</param>
    /// <param name="report">What the scan at startup found.</param>
    public static string DescribeNodePacks(string folder, CamelGraph.Core.Loader.NodePackReport report)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("Your own nodes go in a node pack: a .dll with the files it needs, in a folder of its own, put here:");
        text.AppendLine();
        text.AppendLine(folder);
        text.AppendLine();
        text.AppendLine("Restart Navisworks after adding or changing a pack. Updating CamelGraph leaves this folder alone.");
        text.AppendLine();
        text.AppendLine("Loaded when Navisworks started: " + report.Summary());
        foreach (var line in report.Lines())
        {
            text.AppendLine("  " + line);
        }

        text.AppendLine();
        text.Append("A pack is code that runs with your rights. Install packs only from authors you trust.");
        return text.ToString();
    }

    private void ShowNodePacks()
    {
        var folder = NodePackFolder;
        if (!Dialogs.Confirm(DescribeNodePacks(folder, CamelGraph.Core.Loader.NodePacks.Last) + "\n\nOpen the folder now?", "Node Packs"))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not create " + folder + ": " + ex.Message;
            return;
        }

        if (!FolderOpener(folder))
        {
            StatusMessage = "Could not open " + folder + ". Open it in File Explorer.";
        }
    }

    private static bool OpenFolderInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ----- self-test ------------------------------------------------------------------------------------------------------

    private ICommand? _runSelfTestCommand;

    /// <summary>Set by the host: says whether at least one model is loaded (the self-test skips the checks that need one when it is not).</summary>
    public Func<bool>? ModelAvailableProvider { get; set; }

    /// <summary>The report of the last self-test, or null.</summary>
    public SelfTestReport? LastSelfTest { get; private set; }

    /// <summary>Runs the self-test: read-only Navisworks nodes against the open model, with the result on the clipboard.</summary>
    public ICommand RunSelfTestCommand => _runSelfTestCommand ??= new RelayCommand(RunSelfTest);

    /// <summary>Runs the self-test now.</summary>
    /// <param name="cases">The checks to run; the built-in ones when null.</param>
    /// <returns>The report.</returns>
    public SelfTestReport RunSelfTest(System.Collections.Generic.IReadOnlyList<SelfTestCase>? cases = null)
    {
        string host = string.Empty;
        try
        {
            host = HostDescriptionProvider?.Invoke() ?? string.Empty;
        }
        catch (Exception)
        {
            // The report then says the host is unknown.
        }

        var runner = new SelfTestRunner(
            Registry,
            EvaluationContextFactory ?? (() => new EvaluationContext()),
            new SelfTestHost { ModelAvailable = ModelAvailableProvider ?? (() => true), Description = host });

        RunProgressText = "Self-test…";
        IsRunning = true;   // raised before the checks block the UI thread, so the busy overlay can paint first
        SelfTestReport report;
        try
        {
            report = runner.Run(
                cases ?? SelfTestCatalog.All,
                (index, total, title) =>
                {
                    RunProgressText = "Self-test " + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " / " +
                                      total.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + title;
                    RenderPump?.Invoke();
                },
                () => CancelPoll());
        }
        finally
        {
            IsRunning = false;
        }

        LastSelfTest = report;
        var text = DiagnosticsReport.Redact(
            report.ToText(host, DiagnosticsCollector.CamelGraphVersion(), DateTime.Now),
            DiagnosticsCollector.Replacements());
        var clipboardProblem = PutOnClipboard(text, "camelgraph-selftest.txt", "Self-Test");
        StatusMessage = "Self-test: " + report.Summary + ". " + (clipboardProblem ?? "The report is on the clipboard.");

        if (report.Failed > 0)
        {
            var failures = report.Results.Where(r => r.Outcome == SelfTestOutcome.Failed).ToList();
            Dialogs.ShowError(
                "The self-test found " + failures.Count + " problem(s):\n\n" +
                string.Join("\n", failures.Take(12).Select(r => "• " + r.Case.Title + ": " + r.Message)) +
                (failures.Count > 12 ? "\n• …" : string.Empty) +
                "\n\nThe full report is on the clipboard: paste it into a bug report.",
                "Self-Test");
        }

        return report;
    }

    private void RunSelfTest() => RunSelfTest(null);
}
