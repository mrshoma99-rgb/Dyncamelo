using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// One visual row of a node in the row layout: an output socket, an input
/// socket (with its label and inline editor), the node's own body, a panel
/// header, or the "+N hidden" chip. Rows are planned by <see cref="RowPlanner"/>
/// and reused across rebuilds so the visual tree is not recreated needlessly.
/// </summary>
public sealed class NodeRowViewModel : ObservableObject
{
    /// <summary>Height of a normal row in device-independent pixels.</summary>
    public const double NormalHeight = 22d;

    private bool _zeroHeight;
    private bool _isOpen;
    private int _count;
    private bool _panelDefaultOpen;

    /// <summary>Creates a row.</summary>
    public NodeRowViewModel(NodeViewModel node, RowKind kind, string key, ConnectorViewModel? connector, string panel)
    {
        Node = node;
        Kind = kind;
        Key = key;
        Connector = connector;
        Panel = panel;
        TogglePanelCommand = new RelayCommand(TogglePanel);
        RevealCommand = new RelayCommand(() => node.RevealHidden());
    }

    /// <summary>Owning node.</summary>
    public NodeViewModel Node { get; }

    /// <summary>What the row shows.</summary>
    public RowKind Kind { get; }

    /// <summary>Stable identity used to reuse the row across rebuilds.</summary>
    public string Key { get; }

    /// <summary>The socket's view model (input and output rows), else null.</summary>
    public ConnectorViewModel? Connector { get; }

    /// <summary>Panel title (panel headers, and inputs that belong to a panel).</summary>
    public string Panel { get; }

    /// <summary>True for a row kept only so its wire has an anchor (collapsed node, closed panel).</summary>
    public bool ZeroHeight
    {
        get => _zeroHeight;
        private set
        {
            if (SetProperty(ref _zeroHeight, value))
            {
                RaiseHeight();
            }
        }
    }

    /// <summary>Panel headers: whether the panel is expanded.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>Panel headers: inputs with a pinned value. Hidden chip: hidden port count.</summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(HasCount));
                OnPropertyChanged(nameof(HiddenText));
            }
        }
    }

    /// <summary>"(2 set)" beside a panel title.</summary>
    public string CountText => _count > 0 ? "(" + _count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " set)" : string.Empty;

    /// <summary>True when <see cref="CountText"/> has something to show.</summary>
    public bool HasCount => _count > 0;

    /// <summary>Text of the hidden-ports chip.</summary>
    public string HiddenText => "+" + _count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " hidden";

    /// <summary>Height to lay the row out with: 0 for anchor-only rows and in overview, else automatic.</summary>
    public double RowHeight => Collapsed ? 0d : double.NaN;

    /// <summary>Minimum height matching <see cref="RowHeight"/>.</summary>
    public double RowMinHeight => Collapsed ? 0d : NormalHeight;

    /// <summary>True when the label and editors should be shown (sockets always stay laid out).</summary>
    public bool ShowContent => !Collapsed;

    /// <summary>Expands or collapses a panel.</summary>
    public ICommand TogglePanelCommand { get; }

    /// <summary>Reveals the hidden ports of the node.</summary>
    public ICommand RevealCommand { get; }

    /// <summary>Applies a fresh plan entry to this (reused) row.</summary>
    public void Apply(PlannedRow plan)
    {
        _panelDefaultOpen = plan.PanelDefaultOpen;
        ZeroHeight = plan.ZeroHeight;
        IsOpen = plan.IsOpen;
        Count = plan.Count;
    }

    /// <summary>Re-raises the height properties (zoom level changed).</summary>
    public void RaiseHeight()
    {
        OnPropertyChanged(nameof(RowHeight));
        OnPropertyChanged(nameof(RowMinHeight));
        OnPropertyChanged(nameof(ShowContent));
    }

    private bool Collapsed => _zeroHeight || Node.IsOverview;

    private void TogglePanel()
    {
        RowPlanner.SetPanelOpen(Node.Model, Panel, _panelDefaultOpen, !_isOpen);
    }
}
