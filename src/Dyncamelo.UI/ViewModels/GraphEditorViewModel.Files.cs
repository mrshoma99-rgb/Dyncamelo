using System;
using System.Linq;
using System.Windows.Threading;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Serialization;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Keeping the user's work safe: knowing when the graph has unsaved changes, asking before something replaces it, and an
/// autosaved copy that survives a crash.
/// </summary>
public partial class GraphEditorViewModel
{
    /// <summary>How long an autosave may be skipped because nothing changed, and how often the timer looks (one minute).</summary>
    public static readonly TimeSpan DefaultAutosaveInterval = TimeSpan.FromMinutes(1);

    // Autosaves older than this are dropped without asking: nobody is waiting for them any more.
    private static readonly TimeSpan OrphanLifetime = TimeSpan.FromDays(14);

    // Every recorded edit, undo and redo counts once. The graph is "modified" while the count differs from the one at the last
    // save; undoing back to the saved state still counts as modified, which errs on the safe side.
    private int _changeCount;
    private int _savedChangeCount;
    private int _autosavedChangeCount;
    private bool _offeredRecovery;
    private RecoveryStore? _recovery;
    private DispatcherTimer? _autosaveTimer;

    /// <summary>True when the graph has changes that are not saved to its file.</summary>
    public bool IsModified => _changeCount != _savedChangeCount;

    /// <summary>The graph's name as shown in prompts: its name, else "Untitled".</summary>
    public string DocumentName => DocumentGraph.Name.Length > 0 ? DocumentGraph.Name : "Untitled";

    /// <summary>True when an autosaved copy is kept while there are unsaved changes (default on).</summary>
    public bool IsAutosaveEnabled
    {
        get => _settings.GetBool(SettingKeys.Autosave, true);
        set => SetPreference(SettingKeys.Autosave, value, true, nameof(IsAutosaveEnabled));
    }

    /// <summary>How often the autosave timer looks for unsaved changes (tests shorten it).</summary>
    public TimeSpan AutosaveInterval
    {
        get => _autosaveTimer?.Interval ?? DefaultAutosaveInterval;
        set
        {
            EnsureAutosaveTimer().Interval = value;
        }
    }

    /// <summary>The autosave folder and files of this session.</summary>
    private RecoveryStore Recovery => _recovery ??= new RecoveryStore(_settings.RecoveryDirectory);

    // One more edit: flips the modified flag (and the title's marker) the first time.
    private void NoteChange()
    {
        var was = IsModified;
        _changeCount++;
        if (was != IsModified)
        {
            OnModifiedChanged();
        }
    }

    private void OnModifiedChanged()
    {
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Declares the graph saved: no unsaved changes, and the autosaved copy is no longer needed.</summary>
    private void MarkSaved()
    {
        var was = IsModified;
        _savedChangeCount = _changeCount;
        _autosavedChangeCount = _changeCount;
        Recovery.Clear();
        if (was)
        {
            OnModifiedChanged();
        }
    }

    // A recovered graph is, by definition, not on disk yet.
    private void MarkUnsaved()
    {
        var was = IsModified;
        _savedChangeCount = _changeCount - 1;
        if (was != IsModified)
        {
            OnModifiedChanged();
        }
    }

    // ----- asking before work is replaced ----------------------------------------------------

    /// <summary>
    /// Makes sure replacing the graph loses nothing: does nothing when nothing is unsaved, otherwise asks whether to save,
    /// discard or cancel. A save that is cancelled or fails also cancels.
    /// </summary>
    /// <param name="action">What is about to happen ("New Graph", "Open") — the dialog caption.</param>
    /// <returns>True when it is fine to replace the graph.</returns>
    public bool ConfirmCloseDocument(string action)
    {
        if (!IsModified)
        {
            return true;
        }

        switch (Dialogs.AskSaveChanges("Save changes to '" + DocumentName + "' before continuing?\n\nYour unsaved changes are lost otherwise.", action))
        {
            case SaveChoice.Save:
                SaveGraph();
                return !IsModified;
            case SaveChoice.DontSave:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Opens a .dyc file dropped on the canvas (or handed over by the host shell), asking about unsaved changes first.
    /// </summary>
    /// <param name="paths">Dropped file paths; the first .dyc one is opened.</param>
    /// <returns>True when a graph was opened.</returns>
    public bool OpenDroppedFiles(System.Collections.Generic.IEnumerable<string> paths)
    {
        var path = paths?.FirstOrDefault(p => p != null && p.EndsWith(".dyc", StringComparison.OrdinalIgnoreCase));
        if (path == null)
        {
            StatusMessage = "Only .dyc graph files can be opened by dropping them here.";
            return false;
        }

        if (!System.IO.File.Exists(path))
        {
            Dialogs.ShowError("The file no longer exists:\n" + path, "Open Graph");
            return false;
        }

        return ConfirmCloseDocument("Open Graph") && OpenFromPath(path);
    }

    // ----- autosave and recovery -----------------------------------------------------------

    private DispatcherTimer EnsureAutosaveTimer()
    {
        if (_autosaveTimer == null)
        {
            _autosaveTimer = new DispatcherTimer { Interval = DefaultAutosaveInterval };
            _autosaveTimer.Tick += (sender, args) => AutosaveNow();
            _autosaveTimer.Start();
        }

        return _autosaveTimer;
    }

    /// <summary>Starts the periodic autosave (the view calls this once it is showing).</summary>
    public void StartAutosave() => EnsureAutosaveTimer();

    /// <summary>
    /// Writes the autosaved copy when there are unsaved changes that were not autosaved yet. Never while a run is in
    /// progress, and never throws.
    /// </summary>
    /// <returns>True when a copy was written.</returns>
    public bool AutosaveNow()
    {
        if (!IsAutosaveEnabled || !IsModified || _changeCount == _autosavedChangeCount || IsRunning || _engine.IsRunning)
        {
            return false;
        }

        return WriteAutosave();
    }

    private bool WriteAutosave()
    {
        var document = DocumentGraph;
        var written = Recovery.Write(
            path => new GraphSerializer(Registry).SaveToFile(document, path),
            CurrentFilePath,
            document.Name);
        if (written)
        {
            _autosavedChangeCount = _changeCount;
        }

        return written;
    }

    /// <summary>
    /// Offers the graph of a session that ended unexpectedly (once per editor, and only while the canvas is still empty and
    /// untouched). Either answer removes that autosave so the question is not asked again.
    /// </summary>
    /// <returns>True when a graph was restored.</returns>
    public bool OfferRecovery()
    {
        if (_offeredRecovery)
        {
            return false;
        }

        _offeredRecovery = true;
        if (IsModified || DocumentGraph.Nodes.Count > 0 || IsInsideGroup)
        {
            return false;
        }

        var orphans = Recovery.FindOrphans();
        foreach (var stale in orphans.Where(o => DateTime.UtcNow - o.SavedAtUtc > OrphanLifetime))
        {
            Recovery.Discard(stale);
        }

        var candidate = orphans.FirstOrDefault(o => DateTime.UtcNow - o.SavedAtUtc <= OrphanLifetime);
        if (candidate == null)
        {
            return false;
        }

        var restore = Dialogs.Confirm(
            "CamelGraph found work that was not saved when the last session ended:\n\n" + candidate.Describe() + "\n\nRestore it?",
            "Recover Graph");
        if (!restore)
        {
            Recovery.Discard(candidate);
            return false;
        }

        try
        {
            var graph = new GraphSerializer(Registry).LoadFromFile(candidate.GraphPath);
            var original = candidate.OriginalPath;
            LoadGraph(graph, original != null && System.IO.File.Exists(original) ? original : null);
            DistrustLoadedGraph();   // whatever was open when Navisworks closed may have come from somebody else's file
            MarkUnsaved();
            Recovery.Discard(candidate);
            StatusMessage = "Restored the unsaved graph" + (original != null ? " — Save writes it to " + System.IO.Path.GetFileName(original) + "." : " — use Save to keep it.");
            return true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("The autosaved graph could not be restored: " + ex.Message, "Recover Graph");
            Recovery.Discard(candidate);
            return false;
        }
    }

    /// <summary>
    /// Ends this editor session (the host is closing the pane). Unsaved changes stay behind as an autosave to be offered next
    /// time; with nothing unsaved the autosave is deleted.
    /// </summary>
    public void EndSession()
    {
        _autosaveTimer?.Stop();
        _autosaveTimer = null;
        if (IsModified && IsAutosaveEnabled)
        {
            WriteAutosave();
        }
        else
        {
            Recovery.Clear();
        }

        Recovery.Dispose();
    }
}
