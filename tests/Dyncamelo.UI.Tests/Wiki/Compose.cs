using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dyncamelo.UI.Views;

namespace Dyncamelo.UI.Tests.Wiki;

/// <summary>Putting pictures together and trimming them.</summary>
internal static class Compose
{
    /// <summary>Two pictures next to each other with a gap between them, on a background.</summary>
    public static BitmapSource SideBySide(BitmapSource left, BitmapSource right, double gap, Brush background)
    {
        var width = left.PixelWidth + gap + right.PixelWidth;
        var height = Math.Max(left.PixelHeight, right.PixelHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(background, null, new Rect(0d, 0d, width, height));
            context.DrawImage(left, new Rect(0d, 0d, left.PixelWidth, left.PixelHeight));
            context.DrawImage(right, new Rect(left.PixelWidth + gap, 0d, right.PixelWidth, right.PixelHeight));
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), height, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>
    /// Cuts away the empty bottom of a picture of a pane that is taller than its content: the rows from the last one that differs from
    /// the bottom row, plus a margin. Returns the picture unchanged when it is all one colour.
    /// </summary>
    public static BitmapSource TrimBottom(BitmapSource source, int margin)
    {
        var converted = source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0d);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var reference = (height - 1) * stride;
        var lastContentRow = -1;
        for (var row = height - 2; row >= 0 && lastContentRow < 0; row--)
        {
            var offset = row * stride;
            for (var i = 0; i < stride; i++)
            {
                if (pixels[offset + i] != pixels[reference + i])
                {
                    lastContentRow = row;
                    break;
                }
            }
        }

        if (lastContentRow < 0)
        {
            return source;
        }

        var keep = Math.Min(height, lastContentRow + 1 + margin);
        var cropped = new CroppedBitmap(source, new Int32Rect(0, 0, width, keep));
        cropped.Freeze();
        return cropped;
    }
}

/// <summary>Getting at the parts of controls that live in a popup window, which a picture of the editor does not contain.</summary>
internal static class Popups
{
    /// <summary>
    /// Prepares the popup of a colour swatch as it looks when it opens (the colour of the swatch in the square, the bar and the boxes) and
    /// takes its content out of the popup, so it can be shown in the editor itself where the picture can include it.
    /// </summary>
    public static FrameworkElement TakeColourPickerFrame(ColorSwatchPicker picker)
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ColorSwatchPicker);
        var popupField = type.GetField("_popup", Hidden) ?? throw new InvalidOperationException("The colour picker no longer has a popup field to read.");
        var popup = (Popup)popupField.GetValue(picker)!;
        var begin = type.GetMethod("BeginEdit", Hidden) ?? throw new InvalidOperationException("The colour picker no longer has a BeginEdit method.");
        begin.Invoke(picker, null);
        var frame = popup.Child as FrameworkElement ?? throw new InvalidOperationException("The colour picker popup has no content.");
        popup.Child = null;
        return frame;
    }
}
