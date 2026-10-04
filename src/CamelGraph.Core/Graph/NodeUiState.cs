using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CamelGraph.Core.Graph;

/// <summary>
/// Purely presentational per-node state that survives save/load: collapsed
/// flag, user-set width, the hide-unused-sockets switch and the panels the
/// user has opened. Nothing here influences evaluation.
/// </summary>
public sealed class NodeUiState : INotifyPropertyChanged
{
    private bool _collapsed;
    private double? _width;
    private bool? _hideUnused;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True when the node is collapsed to its header.</summary>
    public bool Collapsed
    {
        get => _collapsed;
        set => Set(ref _collapsed, value);
    }

    /// <summary>User-chosen width in device-independent pixels, or null for automatic width.</summary>
    public double? Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    /// <summary>Hide-unused-sockets override, or null to follow the editor default.</summary>
    public bool? HideUnused
    {
        get => _hideUnused;
        set => Set(ref _hideUnused, value);
    }

    /// <summary>Names of panels the user has expanded although they start closed.</summary>
    public HashSet<string> OpenPanels { get; } = new HashSet<string>();

    /// <summary>Names of panels the user has closed although they start open.</summary>
    public HashSet<string> ClosedPanels { get; } = new HashSet<string>();

    /// <summary>Raises change notification after <see cref="OpenPanels"/> was mutated.</summary>
    public void NotifyPanelsChanged() => OnPropertyChanged(nameof(OpenPanels));

    /// <summary>True when every field is at its default (nothing needs saving).</summary>
    public bool IsDefault => !_collapsed && _width == null && _hideUnused == null && OpenPanels.Count == 0 && ClosedPanels.Count == 0;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
