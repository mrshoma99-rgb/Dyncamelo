using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// A named place on the canvas: the centre of the view and the zoom, saved with the graph so a big graph can be toured
/// ("inputs", "clash filters", "export"). Bookmarks are annotation only and never take part in execution.
/// </summary>
public class BookmarkModel : INotifyPropertyChanged
{
    private string _name = "Bookmark";
    private double _x;
    private double _y;
    private double _zoom = 1d;

    /// <summary>Creates a bookmark with a fresh identifier.</summary>
    public BookmarkModel()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>Stable identifier, persisted in .dyc files.</summary>
    public Guid Id { get; internal set; }

    /// <summary>The name shown in the list.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>Canvas X of the centre of the view.</summary>
    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    /// <summary>Canvas Y of the centre of the view.</summary>
    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    /// <summary>Zoom factor of the view.</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetField(ref _zoom, value);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
