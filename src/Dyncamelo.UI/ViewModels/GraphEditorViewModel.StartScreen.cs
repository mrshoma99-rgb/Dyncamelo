using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>One card of the start screen: starts a new script, or opens a recent script or an example.</summary>
public sealed class StartCardViewModel
{
    /// <summary>Creates a card.</summary>
    /// <param name="kind">"New", "Recent" or "Example" (picks the glyph).</param>
    /// <param name="title">The card's title.</param>
    /// <param name="subtitle">The line under the title.</param>
    /// <param name="toolTip">Hover text (for a file: its full path).</param>
    /// <param name="command">What clicking the card does.</param>
    /// <param name="parameter">The command's parameter (the file path, or the sample).</param>
    public StartCardViewModel(string kind, string title, string subtitle, string toolTip, ICommand command, object? parameter)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        ToolTip = toolTip;
        Command = command;
        Parameter = parameter;
    }

    /// <summary>"New", "Recent" or "Example".</summary>
    public string Kind { get; }

    /// <summary>The card's title.</summary>
    public string Title { get; }

    /// <summary>The line under the title.</summary>
    public string Subtitle { get; }

    /// <summary>Hover text.</summary>
    public string ToolTip { get; }

    /// <summary>What clicking the card does.</summary>
    public ICommand Command { get; }

    /// <summary>The command's parameter.</summary>
    public object? Parameter { get; }
}

public partial class GraphEditorViewModel
{
    /// <summary>The Dyncamelo page on bimcamel.com.</summary>
    public const string WebsiteUrl = "https://www.bimcamel.com/plugins/dyncamelo";

    private const int MaxRecentCards = 4;
    private const int MaxExampleCards = 6;

    // The examples worth opening first, in this order: the gentlest, then the ones most people come for. Any other example follows alphabetically.
    private static readonly string[] FeaturedExamples =
    {
        "Getting Started - Math and Watch",
        "Table Summary from Text",
        "Color Elements by Property",
        "Export Properties to Excel",
        "Bulk Selection Sets from Values",
        "Clash Triage and BCF Export",
    };

    private static int FeaturedRank(string name)
    {
        var index = Array.FindIndex(FeaturedExamples, f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? FeaturedExamples.Length : index;
    }

    private bool _startScreenDismissed;
    private string _updateVersion = string.Empty;
    private string _updateUrl = string.Empty;

    /// <summary>The first card plus the newest recent scripts: what the start screen offers to begin with.</summary>
    public ObservableCollection<StartCardViewModel> StartCards { get; } = new ObservableCollection<StartCardViewModel>();

    /// <summary>The example graphs the start screen offers.</summary>
    public ObservableCollection<StartCardViewModel> ExampleCards { get; } = new ObservableCollection<StartCardViewModel>();

    /// <summary>True when there is at least one recent script.</summary>
    public bool HasRecentCards => StartCards.Count > 1;

    /// <summary>True when there is at least one example.</summary>
    public bool HasExampleCards => ExampleCards.Count > 0;

    /// <summary>Starts a new, empty script (and keeps the start screen out of the way until something is on the canvas again).</summary>
    public ICommand StartNewScriptCommand { get; private set; } = null!;

    /// <summary>Opens the Dyncamelo page on bimcamel.com.</summary>
    public ICommand OpenWebsiteCommand { get; private set; } = null!;

    /// <summary>Opens the download page of the newer version; available only when an update is known.</summary>
    public ICommand OpenUpdateCommand { get; private set; } = null!;

    /// <summary>Opens the Autodesk App Store, where the professional copy is sold.</summary>
    public ICommand OpenStoreCommand { get; private set; } = null!;

    /// <summary>The text of the store button: a link to the store once the listing is live, "coming soon" (greyed out) before.</summary>
    public string StoreButtonText => DistributionChannel.StoreButtonText;

    /// <summary>The tooltip of the store button.</summary>
    public string StoreButtonToolTip => DistributionChannel.AppStoreListed
        ? "Open the Autodesk App Store, where the professional copy is sold"
        : "The copy for professional use is coming soon to the Autodesk App Store";

    /// <summary>"Personal use" or "Professional": which edition this install is (shown beside the version).</summary>
    public string EditionName { get; private set; } = DistributionChannel.EditionName(DistributionChannel.Direct);

    /// <summary>What the licence of this install allows.</summary>
    public string EditionNote { get; private set; } = DistributionChannel.EditionNote(DistributionChannel.Direct);

    /// <summary>True for the free personal-use copy (GitHub, bimcamel.com), which points at the store for professional use.</summary>
    public bool IsPersonalEdition { get; private set; } = true;

    /// <summary>Tells the editor which channel this install came from (the host reads the marker file beside the plug-in).</summary>
    /// <param name="channel">A <see cref="DistributionChannel"/> value.</param>
    public void SetDistribution(string channel)
    {
        EditionName = DistributionChannel.EditionName(channel);
        EditionNote = DistributionChannel.EditionNote(channel);
        IsPersonalEdition = channel != DistributionChannel.AppStore;
        OnPropertyChanged(nameof(EditionName));
        OnPropertyChanged(nameof(EditionNote));
        OnPropertyChanged(nameof(IsPersonalEdition));
    }

    /// <summary>Opens a web address; the host's default launches the browser (tests replace it).</summary>
    public Func<string, bool> UrlOpener { get; set; } = UrlLauncher.Open;

    /// <summary>The installed version as shown on the start screen ("v0.46.0").</summary>
    public string ProductVersionText
    {
        get
        {
            var version = typeof(GraphEditorViewModel).Assembly.GetName().Version;
            return version == null ? string.Empty : "v" + version.Major + "." + version.Minor + "." + Math.Max(version.Build, 0);
        }
    }

    /// <summary>True when the host found a newer release.</summary>
    public bool HasUpdate => _updateVersion.Length > 0;

    /// <summary>The newer version's number ("0.47.0"), or empty.</summary>
    public string UpdateVersion => _updateVersion;

    /// <summary>The line announcing the newer version.</summary>
    public string UpdateText =>
        HasUpdate ? "Version " + _updateVersion + " is available (you have " + ProductVersionText.TrimStart('v') + ")." : string.Empty;

    /// <summary>Tells the start screen (and Help &gt; Get the Newest Version) that a newer release exists.</summary>
    /// <param name="version">The newer version's number, e.g. "0.47.0".</param>
    /// <param name="downloadUrl">Where to get it.</param>
    public void SetAvailableUpdate(string version, string downloadUrl)
    {
        _updateVersion = version ?? string.Empty;
        _updateUrl = downloadUrl ?? string.Empty;
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateVersion));
        OnPropertyChanged(nameof(UpdateText));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>True while the start screen is on show: an empty canvas, hints switched on, and no "New" card pressed yet.</summary>
    public bool IsStartScreenVisible => IsEmptyCanvasHintVisible && !_startScreenDismissed;

    /// <summary>True while only the short getting-started hint is on show (the start screen was put away with the "New" card).</summary>
    public bool IsEmptyHintOnlyVisible => IsEmptyCanvasHintVisible && _startScreenDismissed;

    /// <summary>The getting-started hint as one line.</summary>
    public string EmptyCanvasHintText => string.Join("   ·   ", EmptyCanvasHintLines);

    private void InitStartScreen()
    {
        StartNewScriptCommand = new RelayCommand(StartNewScript);
        OpenWebsiteCommand = new RelayCommand(() => OpenAddress(WebsiteUrl));
        OpenUpdateCommand = new RelayCommand(() => OpenAddress(_updateUrl), () => HasUpdate && _updateUrl.Length > 0);
        OpenStoreCommand = new RelayCommand(() => OpenAddress(DistributionChannel.AppStorePage), () => DistributionChannel.AppStoreListed);
        RecentFiles.CollectionChanged += (_, _) => RefreshStartCards();
        SampleGraphs.CollectionChanged += (_, _) => RefreshStartCards();
        RefreshSampleGraphs();
        RefreshStartCards();
    }

    private void OpenAddress(string url)
    {
        if (url.Length > 0 && !UrlOpener(url))
        {
            StatusMessage = "Could not open " + url + " — copy the address into a browser.";
        }
    }

    private void StartNewScript()
    {
        if (TryNewGraph())
        {
            _startScreenDismissed = true;
            NotifyStartScreen();
        }
    }

    private void RefreshStartCards()
    {
        StartCards.Clear();
        StartCards.Add(new StartCardViewModel("New", "New script", "Start with an empty canvas", "Start a new, empty script", StartNewScriptCommand, null));
        foreach (var path in RecentFiles.Take(MaxRecentCards))
        {
            var folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            StartCards.Add(new StartCardViewModel(
                "Recent",
                System.IO.Path.GetFileNameWithoutExtension(path),
                string.IsNullOrEmpty(folder) ? string.Empty : System.IO.Path.GetFileName(folder),
                path,
                OpenRecentFileCommand,
                path));
        }

        ExampleCards.Clear();
        foreach (var sample in SampleGraphs
                     .OrderBy(s => FeaturedRank(s.Name))
                     .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxExampleCards))
        {
            ExampleCards.Add(new StartCardViewModel("Example", sample.Name, "Example graph", sample.FilePath, OpenSampleCommand, sample));
        }

        OnPropertyChanged(nameof(HasRecentCards));
        OnPropertyChanged(nameof(HasExampleCards));
    }

    private void NotifyStartScreen()
    {
        OnPropertyChanged(nameof(IsStartScreenVisible));
        OnPropertyChanged(nameof(IsEmptyHintOnlyVisible));
        OnPropertyChanged(nameof(EmptyCanvasHintText));
    }
}
