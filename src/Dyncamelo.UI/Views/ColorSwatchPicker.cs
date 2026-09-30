using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Dyncamelo.Core.Editing;

namespace Dyncamelo.UI.Views;

/// <summary>
/// An in-node colour input: a swatch that opens a small picker popup right under it (saturation/value
/// square, hue bar, hex and A/R/G/B fields). It is part of the node — no separate window, no modal loop —
/// and the value is applied when the popup closes (Esc cancels). Binds either to a hex string
/// (<see cref="Hex"/>, "#AARRGGBB", empty = unset) or to four 0–255 channels (<see cref="A"/>…<see cref="B"/>).
/// </summary>
public sealed class ColorSwatchPicker : Grid
{
    /// <summary>Colour as "#AARRGGBB"; empty means not set.</summary>
    public static readonly DependencyProperty HexProperty = DependencyProperty.Register(
        nameof(Hex), typeof(string), typeof(ColorSwatchPicker),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    /// <summary>Alpha channel (0–255), or -1 when the channels are not bound.</summary>
    public static readonly DependencyProperty AProperty = ChannelProperty(nameof(A));

    /// <summary>Red channel (0–255), or -1 when the channels are not bound.</summary>
    public static readonly DependencyProperty RProperty = ChannelProperty(nameof(R));

    /// <summary>Green channel (0–255), or -1 when the channels are not bound.</summary>
    public static readonly DependencyProperty GProperty = ChannelProperty(nameof(G));

    /// <summary>Blue channel (0–255), or -1 when the channels are not bound.</summary>
    public static readonly DependencyProperty BProperty = ChannelProperty(nameof(B));

    private static readonly Size SquareSize = new Size(190, 140);
    private const double BarWidth = 18d;

    private readonly Border _swatch;
    private readonly TextBlock _hint;
    private readonly SolidColorBrush _swatchBrush = new SolidColorBrush(Colors.Transparent);
    private readonly Popup _popup;
    private readonly Border _hueFill = new Border();
    private readonly Ellipse _svThumb;
    private readonly Border _hueThumb;
    private readonly SolidColorBrush _previewBrush = new SolidColorBrush(Colors.Black);
    private readonly TextBox _hexBox;
    private readonly TextBox _aBox;
    private readonly TextBox _rBox;
    private readonly TextBox _gBox;
    private readonly TextBox _bBox;
    private readonly Border _square;
    private readonly Border _bar;

    private byte _wa = 255, _wr, _wg, _wb;
    private double _h, _s, _v;
    private bool _cancel;
    private bool _suppress;
    private bool _dirty;
    private DateTime _closedAt = DateTime.MinValue;

    /// <summary>Creates the swatch and its popup.</summary>
    public ColorSwatchPicker()
    {
        Height = 22;
        Cursor = Cursors.Hand;
        Focusable = false;

        _swatch = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = _swatchBrush,
            ToolTip = "Click to pick a colour",
        };
        _swatch.SetResourceReference(Border.BorderBrushProperty, "Dyc.InputBorderBrush");
        _hint = new TextBlock
        {
            Text = "pick colour",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Dyc.SubtleTextBrush");
        Children.Add(_swatch);
        Children.Add(_hint);
        _swatch.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _swatch.MouseLeftButtonUp += (_, e) =>
        {
            // A click on the swatch while the popup is open first closes it (click-away); do not reopen straight away.
            if (!_popup.IsOpen && DateTime.UtcNow - _closedAt > TimeSpan.FromMilliseconds(250))
            {
                _popup.IsOpen = true;
            }
            else
            {
                _popup.IsOpen = false;
            }

            e.Handled = true;
        };
        _swatch.MouseEnter += (_, _) => _swatch.SetResourceReference(Border.BorderBrushProperty, "Dyc.AccentBrush");
        _swatch.MouseLeave += (_, _) => _swatch.SetResourceReference(Border.BorderBrushProperty, "Dyc.InputBorderBrush");

        _square = new Border { Width = SquareSize.Width, Height = SquareSize.Height, CornerRadius = new CornerRadius(3), ClipToBounds = true, BorderThickness = new Thickness(1) };
        _square.SetResourceReference(Border.BorderBrushProperty, "Dyc.InputBorderBrush");
        _svThumb = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        var squareGrid = new Grid();
        squareGrid.Children.Add(_hueFill);
        squareGrid.Children.Add(new Border { Background = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0d) });
        squareGrid.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90d) });
        var squareCanvas = new Canvas { IsHitTestVisible = false };
        squareCanvas.Children.Add(_svThumb);
        squareGrid.Children.Add(squareCanvas);
        _square.Child = squareGrid;
        _square.MouseLeftButtonDown += (_, e) => { _square.CaptureMouse(); PickSquare(e); };
        _square.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && _square.IsMouseCaptured) PickSquare(e); };
        _square.MouseLeftButtonUp += (_, _) => _square.ReleaseMouseCapture();

        _bar = new Border { Width = BarWidth, Height = SquareSize.Height, CornerRadius = new CornerRadius(3), ClipToBounds = true, Margin = new Thickness(10, 0, 0, 0), BorderThickness = new Thickness(1) };
        _bar.SetResourceReference(Border.BorderBrushProperty, "Dyc.InputBorderBrush");
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        var stops = new[] { "#FFFF0000", "#FFFFFF00", "#FF00FF00", "#FF00FFFF", "#FF0000FF", "#FFFF00FF", "#FFFF0000" };
        for (var i = 0; i < stops.Length; i++)
        {
            rainbow.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i / (double)(stops.Length - 1)));
        }

        var barGrid = new Grid { Background = rainbow };
        _hueThumb = new Border { Width = BarWidth, Height = 4, Background = Brushes.White, CornerRadius = new CornerRadius(1), IsHitTestVisible = false };
        var barCanvas = new Canvas { IsHitTestVisible = false };
        barCanvas.Children.Add(_hueThumb);
        barGrid.Children.Add(barCanvas);
        _bar.Child = barGrid;
        _bar.MouseLeftButtonDown += (_, e) => { _bar.CaptureMouse(); PickHue(e); };
        _bar.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && _bar.IsMouseCaptured) PickHue(e); };
        _bar.MouseLeftButtonUp += (_, _) => _bar.ReleaseMouseCapture();

        _hexBox = MakeBox(double.NaN, "#RRGGBB or #AARRGGBB");
        _aBox = MakeBox(double.NaN, "Alpha 0-255");
        _rBox = MakeBox(double.NaN, "Red 0-255");
        _gBox = MakeBox(double.NaN, "Green 0-255");
        _bBox = MakeBox(double.NaN, "Blue 0-255");

        var preview = new Border { Width = 34, Height = 22, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Background = _previewBrush };
        preview.SetResourceReference(Border.BorderBrushProperty, "Dyc.InputBorderBrush");
        var hexRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(preview, Dock.Left);
        hexRow.Children.Add(preview);
        _hexBox.Margin = new Thickness(8, 0, 0, 0);
        hexRow.Children.Add(_hexBox);

        var channels = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 8, 0, 0) };
        channels.Children.Add(Labelled("A", _aBox));
        channels.Children.Add(Labelled("R", _rBox));
        channels.Children.Add(Labelled("G", _gBox));
        channels.Children.Add(Labelled("B", _bBox));

        var pickers = new StackPanel { Orientation = Orientation.Horizontal };
        pickers.Children.Add(_square);
        pickers.Children.Add(_bar);
        var content = new StackPanel { Width = SquareSize.Width + BarWidth + 12 };
        content.Children.Add(pickers);
        content.Children.Add(hexRow);
        content.Children.Add(channels);

        var frame = new Border { Margin = new Thickness(6), Padding = new Thickness(10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = content };
        frame.SetResourceReference(Border.BackgroundProperty, "Dyc.PanelBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "Dyc.PanelBorderBrush");
        frame.PreviewKeyDown += OnPopupKeyDown;

        _popup = new Popup
        {
            PlacementTarget = _swatch,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = frame,
        };
        _popup.Opened += (_, _) => BeginEdit();
        _popup.Closed += (_, _) =>
        {
            _closedAt = DateTime.UtcNow;
            EndEdit();
        };
        Children.Add(_popup);

        _hexBox.LostKeyboardFocus += (_, _) => ReadHex();
        foreach (var box in new[] { _aBox, _rBox, _gBox, _bBox })
        {
            box.LostKeyboardFocus += (_, _) => ReadChannels();
        }

        Refresh();
    }

    /// <inheritdoc cref="HexProperty" />
    public string Hex
    {
        get => (string)GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    /// <inheritdoc cref="AProperty" />
    public int A
    {
        get => (int)GetValue(AProperty);
        set => SetValue(AProperty, value);
    }

    /// <inheritdoc cref="RProperty" />
    public int R
    {
        get => (int)GetValue(RProperty);
        set => SetValue(RProperty, value);
    }

    /// <inheritdoc cref="GProperty" />
    public int G
    {
        get => (int)GetValue(GProperty);
        set => SetValue(GProperty, value);
    }

    /// <inheritdoc cref="BProperty" />
    public int B
    {
        get => (int)GetValue(BProperty);
        set => SetValue(BProperty, value);
    }

    /// <summary>True while the popup is open.</summary>
    public bool IsPickerOpen => _popup.IsOpen;

    private bool UsesChannels => A >= 0 && R >= 0 && G >= 0 && B >= 0;

    private static DependencyProperty ChannelProperty(string name) => DependencyProperty.Register(
        name, typeof(int), typeof(ColorSwatchPicker),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ColorSwatchPicker)d).Refresh();

    private static Panel Labelled(string label, TextBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
        var text = new TextBlock { Text = label, FontSize = 10 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Dyc.SubtleTextBrush");
        panel.Children.Add(text);
        panel.Children.Add(box);
        return panel;
    }

    private TextBox MakeBox(double width, string tip)
    {
        var box = new TextBox { FontSize = 11, Height = 22, Padding = new Thickness(4, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip };
        if (!double.IsNaN(width))
        {
            box.Width = width;
        }

        if (TryFindResource("Dyc.TextBox") is Style style)
        {
            box.Style = style;
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (ReferenceEquals(box, _hexBox))
                {
                    ReadHex();
                }
                else
                {
                    ReadChannels();
                }

                e.Handled = true;
            }
        };
        return box;
    }

    private void ApplyBoxStyles()
    {
        // The boxes are built before the control sits in a resource scope, so the themed style is applied when the popup opens.
        if (TryFindResource("Dyc.TextBox") is Style style)
        {
            foreach (var box in new[] { _hexBox, _aBox, _rBox, _gBox, _bBox })
            {
                box.Style = style;
            }
        }
    }

    // ----- bound value → display ------------------------------------------------------------------------

    private bool TryReadBound(out byte a, out byte r, out byte g, out byte b)
    {
        if (UsesChannels)
        {
            a = (byte)Math.Min(255, A);
            r = (byte)Math.Min(255, R);
            g = (byte)Math.Min(255, G);
            b = (byte)Math.Min(255, B);
            return true;
        }

        return PortEditors.TryParseHex(Hex, out a, out r, out g, out b);
    }

    private void Refresh()
    {
        if (_popup == null || _popup.IsOpen)
        {
            return;
        }

        if (TryReadBound(out var a, out var r, out var g, out var b))
        {
            _swatchBrush.Color = Color.FromArgb(a, r, g, b);
            _hint.Visibility = Visibility.Collapsed;
        }
        else
        {
            _swatchBrush.Color = Colors.Transparent;
            _hint.Visibility = Visibility.Visible;
        }
    }

    // ----- popup editing ---------------------------------------------------------------------------------

    private void BeginEdit()
    {
        ApplyBoxStyles();
        _cancel = false;
        _dirty = false;
        if (!TryReadBound(out _wa, out _wr, out _wg, out _wb))
        {
            _wa = 255;
            _wr = _wg = _wb = 128;
        }

        ColourMath.RgbToHsv(_wr, _wg, _wb, out _h, out _s, out _v);
        UpdateEditor();
    }

    private void EndEdit()
    {
        ReadPendingText();
        if (!_cancel && _dirty)
        {
            Apply();
        }

        _cancel = false;
        _dirty = false;
        Refresh();
    }

    private void Apply()
    {
        if (UsesChannels)
        {
            A = _wa;
            R = _wr;
            G = _wg;
            B = _wb;
        }
        else
        {
            Hex = PortEditors.ToHex(_wa, _wr, _wg, _wb);
        }
    }

    private void ReadPendingText()
    {
        // A box still holding the caret has not been read yet (closing the popup by clicking away).
        if (_hexBox.IsKeyboardFocused)
        {
            ReadHex();
        }
        else if (_aBox.IsKeyboardFocused || _rBox.IsKeyboardFocused || _gBox.IsKeyboardFocused || _bBox.IsKeyboardFocused)
        {
            ReadChannels();
        }
    }

    private void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _cancel = true;
            _popup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !(Keyboard.FocusedElement is TextBox))
        {
            _popup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void PickSquare(MouseEventArgs e)
    {
        var p = e.GetPosition(_square);
        _s = Clamp01(p.X / SquareSize.Width);
        _v = 1d - Clamp01(p.Y / SquareSize.Height);
        ColourMath.HsvToRgb(_h, _s, _v, out _wr, out _wg, out _wb);
        _dirty = true;
        UpdateEditor();
    }

    private void PickHue(MouseEventArgs e)
    {
        _h = Clamp01(e.GetPosition(_bar).Y / SquareSize.Height) * 360d;
        ColourMath.HsvToRgb(_h, _s, _v, out _wr, out _wg, out _wb);
        _dirty = true;
        UpdateEditor();
    }

    private void ReadHex()
    {
        if (_suppress || !_popup.IsOpen)
        {
            return;
        }

        if (PortEditors.TryParseHex(_hexBox.Text, out var a, out var r, out var g, out var b))
        {
            _wa = a;
            _wr = r;
            _wg = g;
            _wb = b;
            ColourMath.RgbToHsv(_wr, _wg, _wb, out _h, out _s, out _v);
            _dirty = true;
        }

        UpdateEditor();
    }

    private void ReadChannels()
    {
        if (_suppress || !_popup.IsOpen)
        {
            return;
        }

        _wa = ParseByte(_aBox.Text, _wa);
        _wr = ParseByte(_rBox.Text, _wr);
        _wg = ParseByte(_gBox.Text, _wg);
        _wb = ParseByte(_bBox.Text, _wb);
        ColourMath.RgbToHsv(_wr, _wg, _wb, out _h, out _s, out _v);
        _dirty = true;
        UpdateEditor();
    }

    private void UpdateEditor()
    {
        ColourMath.HsvToRgb(_h, 1d, 1d, out var hr, out var hg, out var hb);
        _hueFill.Background = new SolidColorBrush(Color.FromRgb(hr, hg, hb));
        _previewBrush.Color = Color.FromArgb(_wa, _wr, _wg, _wb);
        _swatchBrush.Color = _previewBrush.Color;
        _hint.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(_svThumb, (_s * SquareSize.Width) - (_svThumb.Width / 2d));
        Canvas.SetTop(_svThumb, ((1d - _v) * SquareSize.Height) - (_svThumb.Height / 2d));
        Canvas.SetTop(_hueThumb, (_h / 360d * SquareSize.Height) - (_hueThumb.Height / 2d));

        _suppress = true;
        _aBox.Text = _wa.ToString(CultureInfo.InvariantCulture);
        _rBox.Text = _wr.ToString(CultureInfo.InvariantCulture);
        _gBox.Text = _wg.ToString(CultureInfo.InvariantCulture);
        _bBox.Text = _wb.ToString(CultureInfo.InvariantCulture);
        _hexBox.Text = PortEditors.ToHex(_wa, _wr, _wg, _wb);
        _suppress = false;
    }

    private static byte ParseByte(string text, byte fallback) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? (byte)Math.Max(0, Math.Min(255, value))
            : fallback;

    private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;
}
