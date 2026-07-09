using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dyncamelo.UI.Mvvm;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base class for view models.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property.</summary>
    /// <param name="propertyName">Property name; supplied by the compiler when omitted.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Sets the backing field and raises <see cref="PropertyChanged"/> when the value changed.</summary>
    /// <typeparam name="T">Field type.</typeparam>
    /// <param name="field">Backing field.</param>
    /// <param name="value">New value.</param>
    /// <param name="propertyName">Property name; supplied by the compiler when omitted.</param>
    /// <returns>True when the value changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
