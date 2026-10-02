using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.SelfTest;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>Help for reporting a problem: the diagnostics a bug report needs, and a self-test that tells whether the nodes work on this Navisworks.</summary>
public partial class GraphEditorViewModel
{
    private ICommand? _copyDiagnosticsCommand;

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
        var done = PutOnClipboard(BuildDiagnostics(), "dyncamelo-diagnostics.txt", "Copy Diagnostics");
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
            report.ToText(host, DiagnosticsCollector.DyncameloVersion(), DateTime.Now),
            DiagnosticsCollector.Replacements());
        var clipboardProblem = PutOnClipboard(text, "dyncamelo-selftest.txt", "Self-Test");
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
