using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Dyncamelo.UI.Views;

/// <summary>
/// Loads a bitmap for the Watch Image node body from (path, version)
/// bindings. The version input exists purely to re-fire the binding when a
/// node run overwrites the SAME file (WPF's default URI cache would keep
/// showing the stale picture); the file itself is read fully on load
/// (<see cref="BitmapCacheOption.OnLoad"/>) so it is never locked afterwards.
/// Returns null — an empty image — for missing or unreadable files.
/// </summary>
public class ImagePathToSourceConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var path = values.Length > 0 ? values[0] as string : null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UriFormatException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
