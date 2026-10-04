using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nodify;

namespace CamelGraph.UI.Views;

/// <summary>
/// Opt-in diagnostics overlay (Ctrl+Shift+F12): frames per second, frame-time
/// percentiles, visual-tree size and zoom, sampled from
/// <see cref="CompositionTarget.Rendering"/>. It exists so canvas performance
/// can be measured on the user's machine and pasted back as a text report
/// ("Copy report"). Costs nothing while hidden: the render hook is attached
/// only while the overlay is visible.
/// </summary>
public sealed class PerfHud : Border
{
    private const int WindowSize = 240;
    private readonly TextBlock _text;
    private readonly List<double> _frames = new List<double>(WindowSize);
    private readonly NodifyEditor _editor;
    private TimeSpan _lastRender = TimeSpan.MinValue;
    private DateTime _lastSummary = DateTime.MinValue;
    private string _report = string.Empty;
    private bool _hooked;

    /// <summary>Creates the overlay for an editor.</summary>
    /// <param name="editor">The canvas to describe.</param>
    public PerfHud(NodifyEditor editor)
    {
        _editor = editor;
        Visibility = Visibility.Collapsed;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(8);
        Padding = new Thickness(8, 6, 8, 6);
        CornerRadius = new CornerRadius(4);
        SetResourceReference(BackgroundProperty, "Dyc.OverlayBrush");
        SetResourceReference(BorderBrushProperty, "Dyc.AccentBrush");
        BorderThickness = new Thickness(1);
        Panel.SetZIndex(this, 950);

        _text = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Text = "collecting…",
        };
        _text.SetResourceReference(TextBlock.ForegroundProperty, "Dyc.TextBrush");

        var copy = new Button
        {
            Content = "Copy report",
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(6, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Focusable = false,
        };
        copy.Click += (s, e) =>
        {
            try
            {
                Clipboard.SetText(_report.Length > 0 ? _report : _text.Text);
            }
            catch (Exception)
            {
                // Clipboard can be locked by another process; the on-screen numbers still work.
            }
        };

        var panel = new StackPanel();
        panel.Children.Add(_text);
        panel.Children.Add(copy);
        Child = panel;
    }

    /// <summary>Shows or hides the overlay, attaching or detaching the render hook.</summary>
    public void Toggle()
    {
        if (Visibility == Visibility.Visible)
        {
            Visibility = Visibility.Collapsed;
            Unhook();
        }
        else
        {
            _frames.Clear();
            _lastRender = TimeSpan.MinValue;
            _lastSummary = DateTime.MinValue;
            Visibility = Visibility.Visible;
            Hook();
        }
    }

    private void Hook()
    {
        if (!_hooked)
        {
            CompositionTarget.Rendering += OnRendering;
            _hooked = true;
        }
    }

    private void Unhook()
    {
        if (_hooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var args = e as RenderingEventArgs;
        if (args == null)
        {
            return;
        }

        if (_lastRender != TimeSpan.MinValue && args.RenderingTime != _lastRender)
        {
            var ms = (args.RenderingTime - _lastRender).TotalMilliseconds;
            if (_frames.Count >= WindowSize)
            {
                _frames.RemoveAt(0);
            }

            _frames.Add(ms);
        }

        _lastRender = args.RenderingTime;

        var now = DateTime.UtcNow;
        if ((now - _lastSummary).TotalMilliseconds < 1000 || _frames.Count == 0)
        {
            return;
        }

        _lastSummary = now;
        Refresh();
    }

    private void Refresh()
    {
        var sorted = new List<double>(_frames);
        sorted.Sort();
        var mean = 0d;
        foreach (var f in sorted)
        {
            mean += f;
        }

        mean /= sorted.Count;
        var p95 = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.95) - 1)];
        var worst = sorted[sorted.Count - 1];
        var fps = mean > 0 ? 1000d / mean : 0d;

        var visuals = CountVisuals(_editor, 0);
        var nodes = _editor.Items.Count;
        var wires = _editor.Connections is System.Collections.ICollection c ? c.Count : -1;
        var zoom = _editor.ViewportZoom;
        var tier = RenderCapability.Tier >> 16;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;

        var inv = CultureInfo.InvariantCulture;
        var line = "fps " + fps.ToString("F0", inv) +
                   "  frame " + mean.ToString("F1", inv) + " ms (p95 " + p95.ToString("F1", inv) +
                   ", worst " + worst.ToString("F1", inv) + ")" +
                   "\nvisuals " + visuals.ToString(inv) +
                   "  nodes " + nodes.ToString(inv) +
                   "  wires " + wires.ToString(inv) +
                   "  zoom " + zoom.ToString("F2", inv) +
                   "\ninput: mouse captured by " + (Mouse.Captured?.GetType().Name ?? "nothing") +
                   ", keyboard focus on " + (Keyboard.FocusedElement?.GetType().Name ?? "nothing");
        _text.Text = line;

        var sb = new StringBuilder();
        sb.AppendLine("CamelGraph perf report");
        sb.AppendLine(line.Replace("\n", " | "));
        sb.AppendLine("render tier " + tier.ToString(inv) + ", dpi scale " + dpi.ToString("F2", inv) +
                      ", software rendering " + (tier == 0 ? "yes" : "no"));
        sb.AppendLine("os " + Environment.OSVersion + ", clr " + Environment.Version);
        _report = sb.ToString();
    }

    // Bounded walk: stops descending past a depth that no realistic template reaches.
    private static int CountVisuals(DependencyObject root, int depth)
    {
        if (depth > 60)
        {
            return 0;
        }

        var count = 1;
        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < children; i++)
        {
            count += CountVisuals(VisualTreeHelper.GetChild(root, i), depth + 1);
        }

        return count;
    }
}
