using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// A free-floating text note on the canvas. Notes do not participate in execution.
/// </summary>
public class NoteModel : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private double _x;
    private double _y;

    /// <summary>Creates an empty note.</summary>
    public NoteModel()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>Stable identifier, persisted in .dyc files.</summary>
    public Guid Id { get; internal set; }

    /// <summary>Note text.</summary>
    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    /// <summary>Canvas X position.</summary>
    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    /// <summary>Canvas Y position.</summary>
    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
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
