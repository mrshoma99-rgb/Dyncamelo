using System.Windows.Media;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// One selectable option in the settings panel (a double-click action or a
/// colour palette). <see cref="IsSelected"/> drives the picker highlight and is
/// kept in sync by the owning view model; <see cref="Swatch"/> is a preview
/// colour shown for palette options (null for plain text options).
/// </summary>
public class ChoiceOption : ObservableObject
{
    private bool _isSelected;

    /// <summary>Creates an option.</summary>
    /// <param name="id">Stable id persisted in settings.</param>
    /// <param name="displayName">Label shown in the picker.</param>
    /// <param name="swatch">Optional preview colour (palette options).</param>
    public ChoiceOption(string id, string displayName, Color? swatch = null)
    {
        Id = id;
        DisplayName = displayName;
        Swatch = swatch.HasValue ? new SolidColorBrush(swatch.Value) : null;
    }

    /// <summary>Stable id persisted in settings.</summary>
    public string Id { get; }

    /// <summary>Label shown in the picker.</summary>
    public string DisplayName { get; }

    /// <summary>Preview colour brush (palette options); null for plain options.</summary>
    public Brush? Swatch { get; }

    /// <summary>True when this is the palette/action swatch has a preview colour.</summary>
    public bool HasSwatch => Swatch != null;

    /// <summary>True when this option is the currently selected one.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
