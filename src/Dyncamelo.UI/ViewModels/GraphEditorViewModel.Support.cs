using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>Help for reporting a problem: the diagnostics a bug report needs, on the clipboard in one command.</summary>
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
        var text = BuildDiagnostics();
        try
        {
            Clipboard.SetText(text);
            StatusMessage = "Diagnostics copied. Paste them into your bug report.";
        }
        catch (Exception)
        {
            // The clipboard can be held by another program; a file is the next best thing to paste from.
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "dyncamelo-diagnostics.txt");
                File.WriteAllText(path, text);
                StatusMessage = "The clipboard was busy. Diagnostics saved to " + path;
            }
            catch (Exception ex)
            {
                Dialogs.ShowError("The diagnostics could not be copied or saved: " + ex.Message, "Copy Diagnostics");
            }
        }
    }
}
