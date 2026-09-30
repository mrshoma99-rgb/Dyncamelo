using System;
using System.Windows;
using System.Windows.Media;

namespace Dyncamelo.UI.Services;

/// <summary>
/// Puts a <see cref="UiPalette"/> into a theme resource dictionary. Every palette colour is referenced from XAML with
/// <c>DynamicResource</c>, so replacing a dictionary entry (or changing an unfrozen brush's colour) recolours everything that uses it —
/// including brushes named in styles and templates, which WPF would freeze if they were referenced with <c>StaticResource</c>.
/// </summary>
public static class ThemeApplier
{
    /// <summary>The palette applied most recently (windows created later start from it).</summary>
    public static string CurrentPaletteId { get; private set; } = PaletteCatalog.Default.Id;

    /// <summary>Applies the palette to the dictionary that defines the theme keys (searched through its merged dictionaries).</summary>
    /// <param name="resources">A resource dictionary that contains, or merges, the theme.</param>
    /// <param name="palette">The palette to apply.</param>
    /// <returns>How many entries were changed.</returns>
    public static int Apply(ResourceDictionary resources, UiPalette palette)
    {
        if (resources == null)
        {
            throw new ArgumentNullException(nameof(resources));
        }

        var changed = 0;
        foreach (var pair in palette.Colors)
        {
            var owner = FindOwner(resources, pair.Key);
            if (owner == null)
            {
                continue;
            }

            if (pair.Key.EndsWith("Color", StringComparison.Ordinal))
            {
                owner[pair.Key] = pair.Value;
            }
            else if (owner[pair.Key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = pair.Value;
            }
            else
            {
                owner[pair.Key] = new SolidColorBrush(pair.Value);
            }

            changed++;
        }

        CurrentPaletteId = palette.Id;
        PortBrushes.OnLight = palette.IsLight;
        return changed;
    }

    private static ResourceDictionary? FindOwner(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
        {
            return dictionary;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            var found = FindOwner(merged, key);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
