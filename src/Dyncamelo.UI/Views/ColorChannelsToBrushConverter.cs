using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Dyncamelo.UI.Views;

/// <summary>
/// Multi-value converter turning four ARGB channel values (0-255) into a
/// <see cref="SolidColorBrush"/> for color-swatch previews.
/// </summary>
public class ColorChannelsToBrushConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            ToChannel(values, 0, 255),
            ToChannel(values, 1, 0),
            ToChannel(values, 2, 0),
            ToChannel(values, 3, 0)));
        brush.Freeze();
        return brush;
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static byte ToChannel(object[] values, int index, int fallback)
    {
        if (index < values.Length && values[index] != null)
        {
            try
            {
                var number = System.Convert.ToInt32(values[index], CultureInfo.InvariantCulture);
                return (byte)Math.Max(0, Math.Min(255, number));
            }
            catch (Exception)
            {
                // fall through to the default channel value
            }
        }

        return (byte)fallback;
    }
}
