using System;
using System.IO;
using Dyncamelo.Core.Editing;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Asks before a graph that came from a file runs nodes that start programs, use the network or change existing files. A graph file can
/// come from anybody; running it is running their instructions on this computer. A graph made in the editor, a built-in sample, or a
/// file the user has already agreed to run (remembered with the file's hash, so a changed file asks again) runs without asking.
/// </summary>
public partial class GraphEditorViewModel
{
    private bool _confirmUntrustedRuns = true;

    // False while the graph in the editor came from a file the user has not agreed to run.
    private bool _graphTrusted = true;

    // Hash of the graph file as it was read or last saved, or null.
    private string? _fileHash;

    /// <summary>
    /// True (the default) to ask before running a graph opened from a file that holds nodes which start programs, use the network or
    /// change existing files.
    /// </summary>
    public bool ConfirmUntrustedRuns
    {
        get => _confirmUntrustedRuns;
        set
        {
            if (SetProperty(ref _confirmUntrustedRuns, value))
            {
                _settings.SetConfirmUntrustedRuns(value);
            }
        }
    }

    // A graph handed to the editor with a file path is not trusted until the file has been checked (OpenFromPath does that);
    // one without a path (a new graph) is the user's own.
    private void ResetRunTrust(string? filePath)
    {
        _graphTrusted = filePath == null;
        _fileHash = null;
    }

    private void TrustOpenedFile(string path, byte[] bytes)
    {
        _fileHash = FileFingerprint.Of(bytes);
        _graphTrusted = string.Equals(_settings.EditorConfirmedHash(path), _fileHash, StringComparison.Ordinal);
    }

    private void TrustBuiltInSample()
    {
        _graphTrusted = true;
    }

    private void DistrustLoadedGraph()
    {
        _graphTrusted = false;
        _fileHash = null;
    }

    // Saving a graph that is trusted makes the saved file trusted too; saving one that is not does not make it so.
    private void NoteSavedFile(string path)
    {
        try
        {
            _fileHash = FileFingerprint.OfFile(path);
            if (_graphTrusted)
            {
                _settings.SetEditorConfirmed(path, _fileHash);
            }
        }
        catch (IOException)
        {
            _fileHash = null;
        }
        catch (UnauthorizedAccessException)
        {
            _fileHash = null;
        }
    }

    private void NoteRenamedFile(string newPath)
    {
        if (_graphTrusted && _fileHash != null)
        {
            _settings.SetEditorConfirmed(newPath, _fileHash);
        }
    }

    // Called at the start of every run, whether from the Run button, F5, Run to here or the debounced auto-run.
    private bool AllowRun(bool interactive)
    {
        if (!_confirmUntrustedRuns || _graphTrusted)
        {
            return true;
        }

        var findings = GraphEffects.Survey(DocumentGraph);
        if (findings.Count == 0)
        {
            return true;
        }

        var lines = string.Join("\n  • ", GraphEffects.Describe(findings));
        if (!interactive)
        {
            // Never a surprise dialog while a file is opening, and never a run: wait for the user to press Run.
            StatusMessage = "Not run automatically: this graph came from a file and contains nodes that " + GraphEffects.Describe(findings)[0] +
                            (findings.Count > 1 ? " (and more)" : string.Empty) + ". Press Run to review it.";
            return false;
        }

        var name = CurrentFilePath != null ? Path.GetFileName(CurrentFilePath) : DocumentGraph.Name;
        var message = "'" + name + "' came from a file and contains nodes that:\n\n  • " + lines +
                      "\n\nOnly run graphs from people you trust." +
                      (CurrentFilePath != null ? " Run it? You are asked again only if the file changes." : " Run it?");
        if (!Dialogs.Confirm(message, "Run Graph"))
        {
            StatusMessage = "Not run.";
            return false;
        }

        _graphTrusted = true;
        if (CurrentFilePath != null && _fileHash != null)
        {
            _settings.SetEditorConfirmed(CurrentFilePath, _fileHash);
        }

        return true;
    }
}
