using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dyncamelo.UI.Views;

/// <summary>
/// A dark-themed modal colour picker: a saturation/value square, a hue bar, a
/// live preview and ARGB / hex fields. Seeded from and returned as 0–255 ARGB
/// channels (via <see cref="A"/>/<see cref="R"/>/<see cref="G"/>/<see cref="B"/>)
/// when <c>ShowDialog()</c> returns true.
/// </summary>
public partial class ColorPickerDialog : Window
{
    private double _hue;      // 0–360
    private double _sat;      // 0–1
    private double _val;      // 0–1
    private int _alpha = 255;
    private int _red;
    private int _green;
    private int _blue;
    private bool _suppress;

    /// <summary>Creates the dialog seeded with an ARGB colour (0–255 channels).</summary>
    public ColorPickerDialog(int a, int r, int g, int b)
    {
        InitializeComponent();
        _alpha = Clamp255(a);
        _red = Clamp255(r);
        _green = Clamp255(g);
        _blue = Clamp255(b);
        RgbToHsv(_red, _green, _blue, out _hue, out _sat, out _val);
        Loaded += (_, _) => ApplyAll();
    }

    /// <summary>Chosen alpha (0–255).</summary>
    public int A => _alpha;

    /// <summary>Chosen red (0–255).</summary>
    public int R => _red;

    /// <summary>Chosen green (0–255).</summary>
    public int G => _green;

    /// <summary>Chosen blue (0–255).</summary>
    public int B => _blue;

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        SvSquare.CaptureMouse();
        UpdateSaturationValue(e);
    }

    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSaturationValue(e);
        }
    }

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        UpdateHue(e);
    }

    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateHue(e);
        }
    }

    private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e) =>
        (sender as UIElement)?.ReleaseMouseCapture();

    private void UpdateSaturationValue(MouseEventArgs e)
    {
        var position = e.GetPosition(SvSquare);
        double width = SvSquare.ActualWidth > 0 ? SvSquare.ActualWidth : 200;
        double height = SvSquare.ActualHeight > 0 ? SvSquare.ActualHeight : 160;
        _sat = Clamp01(position.X / width);
        _val = 1.0 - Clamp01(position.Y / height);
        HsvToRgb(_hue, _sat, _val, out _red, out _green, out _blue);
        ApplyAll();
    }

    private void UpdateHue(MouseEventArgs e)
    {
        double height = HueBar.ActualHeight > 0 ? HueBar.ActualHeight : 160;
        _hue = Clamp01(e.GetPosition(HueBar).Y / height) * 360.0;
        HsvToRgb(_hue, _sat, _val, out _red, out _green, out _blue);
        ApplyAll();
    }

    private void OnChannelChanged(object sender, RoutedEventArgs e) => ReadChannels();

    private void OnChannelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReadChannels();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void ReadChannels()
    {
        if (_suppress)
        {
            return;
        }

        _alpha = ParseByte(ABox.Text, _alpha);
        _red = ParseByte(RBox.Text, _red);
        _green = ParseByte(GBox.Text, _green);
        _blue = ParseByte(BBox.Text, _blue);
        RgbToHsv(_red, _green, _blue, out _hue, out _sat, out _val);
        ApplyAll();
    }

    private void OnHexChanged(object sender, RoutedEventArgs e) => ReadHex();

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReadHex();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void ReadHex()
    {
        if (_suppress)
        {
            return;
        }

        if (TryParseHex(HexBox.Text, out var a, out var r, out var g, out var b))
        {
            _alpha = a;
            _red = r;
            _green = g;
            _blue = b;
            RgbToHsv(_red, _green, _blue, out _hue, out _sat, out _val);
        }

        ApplyAll();
    }

    private void ApplyAll()
    {
        HsvToRgb(_hue, 1.0, 1.0, out var hueR, out var hueG, out var hueB);
        SvHue.Background = new SolidColorBrush(Color.FromRgb((byte)hueR, (byte)hueG, (byte)hueB));
        PreviewBrush.Color = Color.FromArgb((byte)_alpha, (byte)_red, (byte)_green, (byte)_blue);

        double width = SvSquare.ActualWidth > 0 ? SvSquare.ActualWidth : 200;
        double height = SvSquare.ActualHeight > 0 ? SvSquare.ActualHeight : 160;
        Canvas.SetLeft(SvThumb, (_sat * width) - (SvThumb.Width / 2));
        Canvas.SetTop(SvThumb, ((1.0 - _val) * height) - (SvThumb.Height / 2));

        double hueHeight = HueBar.ActualHeight > 0 ? HueBar.ActualHeight : 160;
        Canvas.SetTop(HueThumb, (_hue / 360.0 * hueHeight) - (HueThumb.Height / 2));

        _suppress = true;
        ABox.Text = _alpha.ToString(CultureInfo.InvariantCulture);
        RBox.Text = _red.ToString(CultureInfo.InvariantCulture);
        GBox.Text = _green.ToString(CultureInfo.InvariantCulture);
        BBox.Text = _blue.ToString(CultureInfo.InvariantCulture);
        HexBox.Text = string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", _alpha, _red, _green, _blue);
        _suppress = false;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private static int Clamp255(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private static int ParseByte(string text, int fallback) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Clamp255(value)
            : fallback;

    private static bool TryParseHex(string? text, out int a, out int r, out int g, out int b)
    {
        a = 255;
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            hex = "FF" + hex;
        }

        if (hex.Length != 8 ||
            !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        a = (packed >> 24) & 0xFF;
        r = (packed >> 16) & 0xFF;
        g = (packed >> 8) & 0xFF;
        b = packed & 0xFF;
        return true;
    }

    private static void HsvToRgb(double h, double s, double v, out int r, out int g, out int b)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
        double m = v - c;
        double rp, gp, bp;
        if (h < 60) { rp = c; gp = x; bp = 0; }
        else if (h < 120) { rp = x; gp = c; bp = 0; }
        else if (h < 180) { rp = 0; gp = c; bp = x; }
        else if (h < 240) { rp = 0; gp = x; bp = c; }
        else if (h < 300) { rp = x; gp = 0; bp = c; }
        else { rp = c; gp = 0; bp = x; }

        r = (int)Math.Round((rp + m) * 255);
        g = (int)Math.Round((gp + m) * 255);
        b = (int)Math.Round((bp + m) * 255);
    }

    private static void RgbToHsv(int r, int g, int b, out double h, out double s, out double v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        if (delta < 1e-9)
        {
            h = 0;
        }
        else if (max == rd)
        {
            h = 60 * ((((gd - bd) / delta) % 6 + 6) % 6);
        }
        else if (max == gd)
        {
            h = 60 * (((bd - rd) / delta) + 2);
        }
        else
        {
            h = 60 * (((rd - gd) / delta) + 4);
        }

        s = max < 1e-9 ? 0 : delta / max;
        v = max;
    }
}
