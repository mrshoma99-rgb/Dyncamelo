using System.Windows;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Base class for everything placed on the canvas (nodes and notes).
/// The Nodify item container binds <see cref="Location"/> and <see cref="IsSelected"/>.
/// </summary>
public abstract class CanvasItemViewModel : ObservableObject
{
    private Point _location;
    private bool _isSelected;
    private Size _size;

    /// <summary>Graph-space position of the item's top-left corner.</summary>
    public Point Location
    {
        get => _location;
        set
        {
            if (SetProperty(ref _location, value))
            {
                OnLocationChanged(value);
            }
        }
    }

    /// <summary>True when the item is part of the canvas selection.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Rendered size of the item, written by the view (the Nodify item container
    /// binds its ActualSize OneWayToSource). Used to compute group bounds.
    /// </summary>
    public Size Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    /// <summary>Pushes a location change into the wrapped model. Does not raise change notifications.</summary>
    /// <param name="location">The new graph-space location.</param>
    protected abstract void OnLocationChanged(Point location);

    /// <summary>Updates <see cref="Location"/> from the model without writing back to it.</summary>
    /// <param name="location">The model's location.</param>
    protected void SetLocationFromModel(Point location)
    {
        if (_location != location)
        {
            _location = location;
            OnPropertyChanged(nameof(Location));
        }
    }
}
