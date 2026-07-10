using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// A titled, colored rectangle drawn behind the nodes it visually contains
/// (Dynamo-style group). Groups are pure canvas annotation: membership is
/// spatial (whatever lies inside the rectangle) and they never participate
/// in execution.
/// </summary>
public class GroupModel : INotifyPropertyChanged
{
    private string _title = "Group";
    private double _x;
    private double _y;
    private double _width = 200d;
    private double _height = 200d;
    private string _color = DefaultColor;

    /// <summary>Default ARGB color of new groups.</summary>
    public const string DefaultColor = "#FF3D6A99";

    /// <summary>Creates a group with a fresh identifier.</summary>
    public GroupModel()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>Stable identifier, persisted in .dyc files.</summary>
    public Guid Id { get; internal set; }

    /// <summary>Group title shown in the header bar.</summary>
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    /// <summary>Canvas X position of the top-left corner.</summary>
    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    /// <summary>Canvas Y position of the top-left corner.</summary>
    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    /// <summary>Width of the group rectangle.</summary>
    public double Width
    {
        get => _width;
        set => SetField(ref _width, value);
    }

    /// <summary>Height of the group rectangle.</summary>
    public double Height
    {
        get => _height;
        set => SetField(ref _height, value);
    }

    /// <summary>Group color as an ARGB hex string (e.g. "#FF3D6A99").</summary>
    public string Color
    {
        get => _color;
        set => SetField(ref _color, value ?? DefaultColor);
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
