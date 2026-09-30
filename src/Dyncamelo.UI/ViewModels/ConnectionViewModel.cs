using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.Core.Editing;
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
    private bool _isInsertTarget;
    private bool _isHidden;

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
        _owner = owner;
        source.PropertyChanged += OnEndpointChanged;
        target.PropertyChanged += OnEndpointChanged;
    }

    private readonly GraphEditorViewModel _owner;

    /// <summary>Stops listening to the endpoints and the editor (called when the wire is removed).</summary>
    public void Detach()
    {
        Source.PropertyChanged -= OnEndpointChanged;
        Target.PropertyChanged -= OnEndpointChanged;
    }

    /// <summary>Wire colour: the family of the port it leaves.</summary>
    public Brush FamilyBrush => Source.FamilyBrush;

    /// <summary>
    /// True when the wire feeds a list into a single-item input and the engine will
    /// replicate the node over it — drawn dashed so automatic replication is visible.
    /// </summary>
    public bool IsReplicating =>
        Source.Depth != PortDepth.Item && Source.Depth != PortDepth.Unknown && Target.Depth == PortDepth.Item &&
        !Target.Port.UseLevels;

    /// <summary>True when the wire is muted (ignored by evaluation).</summary>
    public bool IsMuted => Model.IsMuted;

    /// <summary>Re-raises <see cref="IsMuted"/> after the model flag changed.</summary>
    public void RefreshMuted() => OnPropertyChanged(nameof(IsMuted));


    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectorViewModel.Kind))
        {
            OnPropertyChanged(nameof(FamilyBrush));
            OnPropertyChanged(nameof(IsReplicating));
        }
        else if (e.PropertyName == nameof(ConnectorViewModel.Depth))
        {
            OnPropertyChanged(nameof(IsReplicating));
        }
    }

    /// <summary>The wrapped Core connection.</summary>
    public ConnectionModel Model { get; }

    /// <summary>The upstream (output) connector.</summary>
    public ConnectorViewModel Source { get; }

    /// <summary>The downstream (input) connector.</summary>
    public ConnectorViewModel Target { get; }

    /// <summary>True while a dragged node hovers this wire and would be inserted on it when dropped.</summary>
    public bool IsInsertTarget
    {
        get => _isInsertTarget;
        set => SetProperty(ref _isInsertTarget, value);
    }

    /// <summary>True while the wire is picked up and being dragged elsewhere (drawn as the pending wire instead).</summary>
    public bool IsHidden
    {
        get => _isHidden;
        set => SetProperty(ref _isHidden, value);
    }

    /// <summary>True while the wire is part of the canvas selection.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Removes this wire (context menu "Disconnect").</summary>
    public ICommand DisconnectCommand { get; }
}
