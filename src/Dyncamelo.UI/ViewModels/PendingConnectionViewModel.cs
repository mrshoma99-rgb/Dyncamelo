using System.Windows;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// State of the wire currently being dragged. All properties are written by the
/// Nodify <c>PendingConnection</c> control via OneWayToSource bindings.
/// </summary>
public class PendingConnectionViewModel : ObservableObject
{
    private ConnectorViewModel? _source;
    private ConnectorViewModel? _target;
    private bool _isVisible;
    private Point _targetLocation;

    /// <summary>The connector the drag started from.</summary>
    public ConnectorViewModel? Source
    {
        get => _source;
        set => SetProperty(ref _source, value);
    }

    /// <summary>The connector the wire was dropped on (null when dropped on empty canvas).</summary>
    public ConnectorViewModel? Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    /// <summary>True while a wire is being dragged.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>Graph-space position of the dragged wire end.</summary>
    public Point TargetLocation
    {
        get => _targetLocation;
        set => SetProperty(ref _targetLocation, value);
    }
}
