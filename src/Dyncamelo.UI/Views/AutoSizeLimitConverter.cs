using System;
using System.Globalization;
using System.Windows.Data;

namespace Dyncamelo.UI.Views;

/// <summary>
/// Turns a user-chosen size into a Max* constraint: while the size is automatic
/// (NaN) the constraint is the limit given as the converter parameter, once the
/// user picked an explicit size the constraint is lifted (infinity). Used by the
/// resizable watch templates so auto-sized watches stay compact but manual
/// resizing is unbounded.
/// </summary>
public class AutoSizeLimitConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double size = value is double d ? d : double.NaN;
        if (!double.IsNaN(size))
        {
            return double.PositiveInfinity;
        }

        return parameter != null &&
               double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var limit)
            ? limit
            : double.PositiveInfinity;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
