using System.Windows.Input;
using Dyncamelo.Core.Graph;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Wraps one <see cref="ConnectionModel"/> wire. The Nodify connection shape
/// binds its endpoints to <c>Source.Anchor</c>/<c>Target.Anchor</c>. Wires are
/// click-selectable (<see cref="IsSelected"/>) and removable via Delete or the
/// context menu.
/// </summary>
public class ConnectionViewModel : ObservableObject
{
    private bool _isSelected;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="owner">The editor that owns this wire.</param>
    /// <param name="model">The wrapped Core connection.</param>
    /// <param name="source">The upstream (output) connector.</param>
    /// <param name="target">The downstream (input) connector.</param>
    public ConnectionViewModel(GraphEditorViewModel owner, ConnectionModel model, ConnectorViewModel source, ConnectorViewModel target)
    {
        Model = model;
        Source = source;
        Target = target;
        DisconnectCommand = new RelayCommand(() => owner.RemoveConnectionCommand.Execute(this));
    }

    /// <summary>The wrapped Core connection.</summary>
    public ConnectionModel Model { get; }

    /// <summary>The upstream (output) connector.</summary>
    public ConnectorViewModel Source { get; }

    /// <summary>The downstream (input) connector.</summary>
    public ConnectorViewModel Target { get; }

    /// <summary>True while the wire is part of the canvas selection.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Removes this wire (context menu "Disconnect").</summary>
    public ICommand DisconnectCommand { get; }
}
