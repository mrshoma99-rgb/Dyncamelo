using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Player;
using Dyncamelo.Core.Serialization;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>A script in the Player's list.</summary>
public sealed class ScriptListItem
{
    /// <summary>Creates the row.</summary>
    public ScriptListItem(ScriptEntry entry) => Entry = entry;

    /// <summary>The script file.</summary>
    public ScriptEntry Entry { get; }

    /// <summary>The script's name.</summary>
    public string Name => Entry.Name;

    /// <summary>The folder it sits in (the list groups by it).</summary>
    public string Folder => Entry.Folder;

    /// <summary>Full path.</summary>
    public string Path => Entry.Path;
}

/// <summary>One field of the form: a label and the same inline editor a node would show.</summary>
public sealed class PlayerFieldViewModel : ObservableObject
{
    /// <summary>Creates the field.</summary>
    /// <param name="field">The field of the loaded script.</param>
    /// <param name="host">What the editor talks to (dialogs, messages).</param>
    public PlayerFieldViewModel(PlayerField field, IConnectorHost host)
    {
        Field = field;
        Connector = new ConnectorViewModel(host, field.Port);
        Connector.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectorViewModel.IsModified))
            {
                OnPropertyChanged(nameof(IsChanged));
            }
        };
    }

    /// <summary>The field of the loaded script.</summary>
    public PlayerField Field { get; }

    /// <summary>The connector the inline editors (the same templates the nodes use) bind to.</summary>
    public ConnectorViewModel Connector { get; }

    /// <summary>The label.</summary>
    public string Label => Field.Label;

    /// <summary>The node an exposed input belongs to (empty for an input node).</summary>
    public string Detail => Field.Detail;

    /// <summary>True when there is a detail line.</summary>
    public bool HasDetail => Field.Detail.Length > 0;

    /// <summary>True when the value differs from the one saved in the script.</summary>
    public bool IsChanged => Field.IsChanged;

    /// <summary>Brings back the value saved in the script.</summary>
    public ICommand ResetCommand => Connector.ResetCommand;
}

/// <summary>One result shown after a run.</summary>
public sealed class PlayerOutputViewModel
{
    /// <summary>Creates the row.</summary>
    public PlayerOutputViewModel(ScriptOutput output) => Output = output;

    /// <summary>The result.</summary>
    public ScriptOutput Output { get; }

    /// <summary>The node's name.</summary>
    public string Label => Output.Label;

    /// <summary>The text.</summary>
    public string Text => Output.Text;

    /// <summary>True when the node failed, so the text is an error rather than a value.</summary>
    public bool IsFailed => Output.State == NodeState.Error;
}

/// <summary>One problem listed after a run.</summary>
public sealed class PlayerProblemViewModel
{
    /// <summary>Creates the row.</summary>
    public PlayerProblemViewModel(NodeProblem problem) => Problem = problem;

    /// <summary>The problem.</summary>
    public NodeProblem Problem { get; }

    /// <summary>The node's name.</summary>
    public string Node => Problem.Node.Name;

    /// <summary>What the node said.</summary>
    public string Text => Problem.Text;

    /// <summary>True for an error, false for a warning.</summary>
    public bool IsError => Problem.Severity == ProblemSeverity.Error;
}

/// <summary>A scripts folder the Player looks in.</summary>
public sealed class PlayerFolderItem
{
    /// <summary>Creates the row.</summary>
    public PlayerFolderItem(string path, bool isDefault, ICommand remove, ICommand open)
    {
        Path = path;
        IsDefault = isDefault;
        RemoveCommand = remove;
        OpenCommand = open;
    }

    /// <summary>Full path.</summary>
    public string Path { get; }

    /// <summary>True for the built-in folder, which cannot be removed.</summary>
    public bool IsDefault { get; }

    /// <summary>True when the folder can be taken off the list.</summary>
    public bool CanRemove => !IsDefault;

    /// <summary>True when the folder exists.</summary>
    public bool Exists => Directory.Exists(Path);

    /// <summary>Removes the folder from the list (the files stay where they are).</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>Opens the folder in Explorer.</summary>
    public ICommand OpenCommand { get; }
}

/// <summary>
/// The Script Player: lists the scripts in the scripts folders, shows the form of the one selected, runs it without the node
/// editor and shows the results. It owns no canvas — only <see cref="ScriptSession"/>s — so it is light and fast to open.
/// </summary>
public sealed class PlayerViewModel : ObservableObject, IConnectorHost
{
    private readonly UiSettingsService _settings;
    private readonly UndoManager _history = new UndoManager();
    private readonly List<ScriptEntry> _all = new List<ScriptEntry>();
    private readonly Stopwatch _repaint = Stopwatch.StartNew();
    private ScriptSession? _session;
    private ScriptListItem? _selected;
    private string _search = string.Empty;
    private string _loadError = string.Empty;
    private string _statusText = string.Empty;
    private string _runProgress = string.Empty;
    private string _resultSummary = string.Empty;
    private bool _hasResult;
    private bool _resultSucceeded = true;
    private bool _isRunning;
    private bool _isFoldersOpen;
    private bool _hasScanned;
    private ScriptResult? _lastResult;
    private CancellationTokenSource? _cancellation;

    /// <summary>Creates the Player.</summary>
    /// <param name="registry">The node registry scripts are resolved against.</param>
    /// <param name="dialogs">Dialogs; the default WPF ones when null.</param>
    /// <param name="settings">Persisted settings (folders, remembered values); the default store when null.</param>
    public PlayerViewModel(NodeRegistry registry, IDialogService? dialogs = null, UiSettingsService? settings = null)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Dialogs = dialogs ?? new WpfDialogService();
        _settings = settings ?? new UiSettingsService();
        Scripts = new ObservableCollection<ScriptListItem>();
        Fields = new ObservableCollection<PlayerFieldViewModel>();
        Outputs = new ObservableCollection<PlayerOutputViewModel>();
        Problems = new ObservableCollection<PlayerProblemViewModel>();
        Folders = new ObservableCollection<PlayerFolderItem>();
        RefreshCommand = new RelayCommand(Refresh);
        RunCommand = new RelayCommand(() => Run(), () => _session != null && !_isRunning);
        ResetCommand = new RelayCommand(ResetFields, () => _session != null && Fields.Count > 0);
        CopyResultsCommand = new RelayCommand(CopyResults, () => _lastResult != null);
        OpenInEditorCommand = new RelayCommand(() => OpenInEditorRequested?.Invoke(this, _session!.Path), () => _session != null);
        ShowInExplorerCommand = new RelayCommand(ShowInExplorer, () => _session != null);
        AddFolderCommand = new RelayCommand(() => AddFolder());
        RebuildFolders();
    }

    /// <summary>The node registry.</summary>
    public NodeRegistry Registry { get; }

    /// <summary>The colour palette chosen in the editor's settings (the Player follows it).</summary>
    public string PaletteId => _settings.PaletteId;

    /// <summary>Dialogs.</summary>
    public IDialogService Dialogs { get; }

    /// <summary>Creates the per-run evaluation context with the host's services (set by the host).</summary>
    public Func<EvaluationContext>? EvaluationContextFactory { get; set; }

    /// <summary>Repaints the pane during a run so the progress text moves (set by the view; renders, never handles input).</summary>
    public Action? RenderPump { get; set; }

    /// <summary>Asked between the steps of a run whether Esc was pressed; tests replace it.</summary>
    public Func<bool> CancelPoll { get; set; } = EscapeKey.WasPressed;

    /// <summary>Raised when the user asks to edit the script that is selected; the argument is its path.</summary>
    public event EventHandler<string>? OpenInEditorRequested;

    // ----- IConnectorHost -----------------------------------------------------------------

    bool IConnectorHost.ColourBlindGlyphs => false;

    IDialogService IConnectorHost.Dialogs => Dialogs;

    UndoManager IConnectorHost.History => _history;

    void IConnectorHost.ReportStatus(string message) => StatusText = message;

    void IConnectorHost.ReportProblem(string message) => StatusText = message;

    /// <summary>Shows a message at the bottom of the pane (used by the host's crash guard).</summary>
    /// <param name="message">The message.</param>
    public void ReportProblem(string message) => StatusText = message;

    // ----- the list -----------------------------------------------------------------------

    /// <summary>The scripts shown (filtered by <see cref="SearchText"/>).</summary>
    public ObservableCollection<ScriptListItem> Scripts { get; }

    /// <summary>The scripts folders, the built-in one first.</summary>
    public ObservableCollection<PlayerFolderItem> Folders { get; }

    /// <summary>The folders searched for scripts.</summary>
    public IEnumerable<string> FolderPaths => Folders.Select(f => f.Path);

    /// <summary>Filters the list by part of a script's name or folder.</summary>
    public string SearchText
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>The script whose form is shown.</summary>
    public ScriptListItem? SelectedScript
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                LoadScript(value?.Entry);
            }
        }
    }

    /// <summary>True while the list is empty: no folder has a script in it (or none matches the search).</summary>
    public bool IsListEmpty => Scripts.Count == 0;

    /// <summary>What to tell the user when the list is empty.</summary>
    public string EmptyListText =>
        _all.Count == 0
            ? "No scripts yet. Save a graph from the editor into " + Folders[0].Path + ", or add another folder below."
            : "No script matches '" + _search.Trim() + "'.";

    /// <summary>Looks for scripts again (folders and files may have changed).</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Re-reads the folders and keeps the selection when the script is still there.</summary>
    public void Refresh()
    {
        var keep = _selected?.Path;
        _all.Clear();
        _all.AddRange(ScriptCatalog.Scan(FolderPaths));
        _hasScanned = true;
        ApplyFilter();
        if (keep != null)
        {
            SelectedScript = Scripts.FirstOrDefault(s => string.Equals(s.Path, keep, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Scans the folders the first time the Player is shown.</summary>
    public void EnsureScanned()
    {
        if (!_hasScanned)
        {
            Refresh();
        }
    }

    private void ApplyFilter()
    {
        var words = _search.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var shown = _all
            .Where(e => words.All(w => (e.Name + " " + e.Folder).ToLowerInvariant().Contains(w)))
            .Select(e => new ScriptListItem(e))
            .ToList();
        Scripts.Clear();
        foreach (var item in shown)
        {
            Scripts.Add(item);
        }

        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(EmptyListText));
        if (_selected != null && !Scripts.Any(s => s.Path == _selected.Path))
        {
            SelectedScript = null;
        }
    }

    // ----- folders ------------------------------------------------------------------------

    /// <summary>True while the folder list is expanded.</summary>
    public bool IsFoldersOpen
    {
        get => _isFoldersOpen;
        set => SetProperty(ref _isFoldersOpen, value);
    }

    /// <summary>Asks for a folder and adds it.</summary>
    public ICommand AddFolderCommand { get; }

    private void RebuildFolders()
    {
        Folders.Clear();
        Folders.Add(MakeFolder(UiSettingsService.DefaultScriptsFolder, isDefault: true));
        foreach (var folder in _settings.PlayerFolders)
        {
            if (!string.Equals(folder, UiSettingsService.DefaultScriptsFolder, StringComparison.OrdinalIgnoreCase))
            {
                Folders.Add(MakeFolder(folder, isDefault: false));
            }
        }
    }

    private PlayerFolderItem MakeFolder(string path, bool isDefault) =>
        new PlayerFolderItem(path, isDefault, new RelayCommand(() => RemoveFolder(path), () => !isDefault), new RelayCommand(() => OpenFolder(path)));

    /// <summary>Adds a scripts folder.</summary>
    /// <param name="path">The folder.</param>
    public void AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var all = _settings.PlayerFolders.ToList();
        if (!all.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
        {
            all.Add(path);
            _settings.SetPlayerFolders(all);
        }

        RebuildFolders();
        Refresh();
    }

    private void AddFolder()
    {
        var picked = Dialogs.PickFolder("Choose a folder of scripts", UiSettingsService.DefaultScriptsFolder);
        if (picked != null)
        {
            AddFolder(picked);
        }
    }

    /// <summary>Takes a folder off the list (the files stay where they are).</summary>
    /// <param name="path">The folder.</param>
    public void RemoveFolder(string path)
    {
        _settings.SetPlayerFolders(_settings.PlayerFolders.Where(f => !string.Equals(f, path, StringComparison.OrdinalIgnoreCase)));
        RebuildFolders();
        Refresh();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Could not open the folder: " + ex.Message;
        }
    }

    // ----- the selected script ------------------------------------------------------------

    /// <summary>The form of the script.</summary>
    public ObservableCollection<PlayerFieldViewModel> Fields { get; }

    /// <summary>The script that is loaded, or null.</summary>
    public ScriptSession? Session => _session;

    /// <summary>True when a script is loaded.</summary>
    public bool HasScript => _session != null;

    /// <summary>The loaded script's name.</summary>
    public string Title => _session?.Name ?? string.Empty;

    /// <summary>What the script says about itself.</summary>
    public string Description => _session?.Description ?? string.Empty;

    /// <summary>True when the script has a description.</summary>
    public bool HasDescription => Description.Length > 0;

    /// <summary>True when the form has fields.</summary>
    public bool HasFields => Fields.Count > 0;

    /// <summary>"Changes the model, writes files, runs programs or uses the network: A, B" — or empty for a script that only reads.</summary>
    public string ModifiesText =>
        _session == null || _session.ModifyingNodes.Count == 0
            ? string.Empty
            : "Changes the model, writes files, runs programs or uses the network: " + string.Join(", ", _session.ModifyingNodes.Take(6)) +
              (_session.ModifyingNodes.Count > 6 ? ", … +" + (_session.ModifyingNodes.Count - 6).ToString(CultureInfo.InvariantCulture) : string.Empty);

    /// <summary>True when the script changes things.</summary>
    public bool HasModifies => ModifiesText.Length > 0;

    /// <summary>A warning when nodes of the script are not installed.</summary>
    public string MissingText =>
        _session == null || _session.MissingNodes == 0
            ? string.Empty
            : _session.MissingNodes.ToString(CultureInfo.InvariantCulture) + (_session.MissingNodes == 1 ? " node is" : " nodes are") +
              " not installed, so this script cannot run.";

    /// <summary>True when nodes are missing.</summary>
    public bool HasMissing => MissingText.Length > 0;

    /// <summary>Why the script could not be loaded, or empty.</summary>
    public string LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
            {
                OnPropertyChanged(nameof(HasLoadError));
            }
        }
    }

    /// <summary>True when the script could not be loaded.</summary>
    public bool HasLoadError => _loadError.Length > 0;

    /// <summary>Reads a script and builds its form.</summary>
    /// <param name="entry">The script, or null to show none.</param>
    public void LoadScript(ScriptEntry? entry)
    {
        Fields.Clear();
        ClearResults();
        _session = null;
        LoadError = string.Empty;
        if (entry != null)
        {
            if (!File.Exists(entry.Path))
            {
                LoadError = "The file no longer exists.";
            }
            else
            {
                try
                {
                    _session = ScriptSession.Load(entry.Path, Registry);
                    _session.RestoreValues(_settings.PlayerValues(entry.Path));
                    foreach (var field in _session.Fields)
                    {
                        Fields.Add(new PlayerFieldViewModel(field, this));
                    }
                }
                catch (GraphFormatException ex)
                {
                    LoadError = ex.Message;
                }
                catch (IOException ex)
                {
                    LoadError = ex.Message;
                }
                catch (UnauthorizedAccessException ex)
                {
                    LoadError = ex.Message;
                }
                catch (Exception ex)
                {
                    // A script the loader chokes on must never take down the host application.
                    LoadError = "The script could not be opened: " + ex.Message;
                }
            }
        }

        RaiseScriptChanged();
    }

    private void RaiseScriptChanged()
    {
        foreach (var name in new[]
        {
            nameof(Session), nameof(HasScript), nameof(Title), nameof(Description), nameof(HasDescription), nameof(HasFields),
            nameof(ModifiesText), nameof(HasModifies), nameof(MissingText), nameof(HasMissing),
        })
        {
            OnPropertyChanged(name);
        }

        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Brings every field back to the value saved in the script.</summary>
    public ICommand ResetCommand { get; }

    private void ResetFields()
    {
        _session?.ResetValues();
        StatusText = "Values reset to the ones saved in the script.";
    }

    /// <summary>Opens the selected script in the node editor.</summary>
    public ICommand OpenInEditorCommand { get; }

    /// <summary>Shows the script's file in Explorer.</summary>
    public ICommand ShowInExplorerCommand { get; }

    private void ShowInExplorer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _session!.Path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Could not show the file: " + ex.Message;
        }
    }

    // ----- running ------------------------------------------------------------------------

    /// <summary>Runs the loaded script with the values in the form.</summary>
    public ICommand RunCommand { get; }

    /// <summary>Copies the results to the clipboard as text.</summary>
    public ICommand CopyResultsCommand { get; }

    /// <summary>True while a script runs.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>What the pane says while a script runs ("Running 3 / 12 — Isolate Walls").</summary>
    public string RunProgressText
    {
        get => _runProgress;
        private set => SetProperty(ref _runProgress, value);
    }

    /// <summary>A general line for the bottom of the pane (messages, errors that are not the script's).</summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    /// <summary>True when there is a status line to show.</summary>
    public bool HasStatus => _statusText.Length > 0;

    /// <summary>The results of the last run.</summary>
    public ObservableCollection<PlayerOutputViewModel> Outputs { get; }

    /// <summary>The nodes that failed or warned in the last run.</summary>
    public ObservableCollection<PlayerProblemViewModel> Problems { get; }

    /// <summary>"Finished in 1.2 s — 14 nodes", or why it did not.</summary>
    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    /// <summary>True once a run has produced a summary.</summary>
    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    /// <summary>True when the last run finished without a failed node (the summary is coloured by it).</summary>
    public bool ResultSucceeded
    {
        get => _resultSucceeded;
        private set => SetProperty(ref _resultSucceeded, value);
    }

    /// <summary>True when the last run listed results.</summary>
    public bool HasOutputs => Outputs.Count > 0;

    /// <summary>True when the last run listed problems.</summary>
    public bool HasProblems => Problems.Count > 0;

    private void ClearResults()
    {
        Outputs.Clear();
        Problems.Clear();
        _lastResult = null;
        ResultSummary = string.Empty;
        HasResult = false;
        ResultSucceeded = true;
        OnPropertyChanged(nameof(HasOutputs));
        OnPropertyChanged(nameof(HasProblems));
    }

    /// <summary>Stops the run in progress before its next step (only a call from inside the run reaches it).</summary>
    public void RequestCancel()
    {
        var source = _cancellation;
        if (source != null && !source.IsCancellationRequested)
        {
            RunProgressText = "Stopping…";
            source.Cancel();
        }
    }

    /// <summary>
    /// Runs the loaded script. A script that changes the model asks first, once, and again only if its file changes; a script
    /// with missing nodes is not run. The values in the form are remembered for next time.
    /// </summary>
    /// <returns>True when a run was made (even one that was cancelled or had errors).</returns>
    public bool Run()
    {
        var session = _session;
        if (session == null || _isRunning)
        {
            return false;
        }

        if (session.MissingNodes > 0)
        {
            Dialogs.ShowError(MissingText + "\n\nInstall the node pack it was made with, or open it in the editor to see which nodes are missing.", "Run Script");
            return false;
        }

        if (session.ModifyingNodes.Count > 0 && _settings.PlayerConfirmedHash(session.Path) != session.Hash)
        {
            var message = "'" + session.Name + "' changes the model, writes files, runs programs or uses the network, through:\n\n  • " +
                          string.Join("\n  • ", session.ModifyingNodes.Take(10)) +
                          (session.ModifyingNodes.Count > 10 ? "\n  • …" : string.Empty) +
                          "\n\nRun it? You are asked again only if the script file changes.";
            if (!Dialogs.Confirm(message, "Run Script"))
            {
                return false;
            }

            _settings.SetPlayerConfirmed(session.Path, session.Hash);
        }

        ClearResults();
        StatusText = string.Empty;
        RunProgressText = "Running " + session.Name + "…";
        IsRunning = true;    // raised before the run blocks the UI thread, so the pane can paint first
        ScriptResult result;
        using (var cancellation = new CancellationTokenSource())
        {
            _cancellation = cancellation;
            try
            {
                var context = EvaluationContextFactory != null ? EvaluationContextFactory() : new EvaluationContext();
                ConfigureRunContext(context, cancellation);
                result = session.Run(context);
            }
            catch (Exception ex)
            {
                StatusText = "The run failed: " + ex.Message;
                return true;
            }
            finally
            {
                _cancellation = null;
                IsRunning = false;
            }
        }

        _lastResult = result;
        foreach (var output in result.Outputs)
        {
            Outputs.Add(new PlayerOutputViewModel(output));
        }

        foreach (var problem in result.Problems)
        {
            Problems.Add(new PlayerProblemViewModel(problem));
        }

        ResultSummary = result.Summary;
        ResultSucceeded = result.Succeeded;
        HasResult = true;
        OnPropertyChanged(nameof(HasOutputs));
        OnPropertyChanged(nameof(HasProblems));
        _settings.SetPlayerValues(session.Path, session.CaptureValues());
        _settings.SetPlayerLastScript(session.Path);
        CommandManager.InvalidateRequerySuggested();
        return true;
    }

    private void ConfigureRunContext(EvaluationContext context, CancellationTokenSource cancellation)
    {
        context.UseCancellation(cancellation.Token);
        EscapeKey.Reset();
        context.ProgressCallback = progress => RunProgressText = "Running " + progress.Describe() + " — Esc stops";
        context.Heartbeat = () =>
        {
            if (CancelPoll())
            {
                RequestCancel();
            }

            if (_repaint.ElapsedMilliseconds >= 100)
            {
                _repaint.Restart();
                RenderPump?.Invoke();
            }
        };
    }

    /// <summary>The script run most recently, or empty.</summary>
    public string LastScriptPath => _settings.PlayerLastScript;

    /// <summary>True when the last script run still exists.</summary>
    public bool CanRunLast => LastScriptPath.Length > 0 && File.Exists(LastScriptPath);

    /// <summary>Selects the script run most recently and runs it again.</summary>
    /// <returns>True when a run was made.</returns>
    public bool RunLast()
    {
        if (!CanRunLast)
        {
            StatusText = "No script has been run yet.";
            return false;
        }

        EnsureScanned();
        var item = Scripts.FirstOrDefault(s => string.Equals(s.Path, LastScriptPath, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            SelectedScript = item;
        }
        else
        {
            // Not in any listed folder: load it directly.
            _selected = null;
            OnPropertyChanged(nameof(SelectedScript));
            LoadScript(new ScriptEntry(LastScriptPath, Path.GetFileNameWithoutExtension(LastScriptPath), string.Empty, DateTime.UtcNow));
        }

        return Run();
    }

    /// <summary>Loads a script by its path and runs it (for callers that know the file but not the list).</summary>
    /// <param name="path">The .dyc file.</param>
    /// <returns>True when the script ran and finished without a failed node.</returns>
    public bool RunScript(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "The script does not exist: " + path;
            return false;
        }

        _selected = null;
        OnPropertyChanged(nameof(SelectedScript));
        LoadScript(new ScriptEntry(path, Path.GetFileNameWithoutExtension(path), string.Empty, File.GetLastWriteTimeUtc(path)));
        return Run() && _lastResult != null && _lastResult.Succeeded;
    }

    private void CopyResults()
    {
        if (_lastResult == null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_lastResult.ToReport(Title));
            StatusText = "Results copied.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText = "The clipboard is busy; try again.";
        }
    }
}
