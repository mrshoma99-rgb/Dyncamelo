using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Dyncamelo.UI.Views;

/// <summary>
/// A drag grip that resizes a sibling element through two-way bound
/// <see cref="TargetWidth"/>/<see cref="TargetHeight"/> values (used by the
/// watch node templates, where the size persists into the .dyc payload).
/// NaN means "size automatically"; the first drag converts it to a concrete
/// size seeded from <see cref="TargetElement"/>'s actual size.
/// </summary>
public class SizeGripThumb : Thumb
{
    /// <summary>Width being edited; NaN = automatic.</summary>
    public static readonly DependencyProperty TargetWidthProperty = DependencyProperty.Register(
        nameof(TargetWidth),
        typeof(double),
        typeof(SizeGripThumb),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Height being edited; NaN = automatic.</summary>
    public static readonly DependencyProperty TargetHeightProperty = DependencyProperty.Register(
        nameof(TargetHeight),
        typeof(double),
        typeof(SizeGripThumb),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Element whose actual size seeds the first drag when the size is automatic.</summary>
    public static readonly DependencyProperty TargetElementProperty = DependencyProperty.Register(
        nameof(TargetElement),
        typeof(FrameworkElement),
        typeof(SizeGripThumb),
        new FrameworkPropertyMetadata(null));

    /// <summary>Smallest width the grip will produce.</summary>
    public static readonly DependencyProperty MinTargetWidthProperty = DependencyProperty.Register(
        nameof(MinTargetWidth),
        typeof(double),
        typeof(SizeGripThumb),
        new FrameworkPropertyMetadata(60d));

    /// <summary>Smallest height the grip will produce.</summary>
    public static readonly DependencyProperty MinTargetHeightProperty = DependencyProperty.Register(
        nameof(MinTargetHeight),
        typeof(double),
        typeof(SizeGripThumb),
        new FrameworkPropertyMetadata(40d));

    /// <summary>Creates the grip with a resize cursor.</summary>
    public SizeGripThumb()
    {
        Cursor = Cursors.SizeNWSE;
        DragDelta += OnDragDelta;
    }

    /// <summary>Width being edited; NaN = automatic.</summary>
    public double TargetWidth
    {
        get => (double)GetValue(TargetWidthProperty);
        set => SetValue(TargetWidthProperty, value);
    }

    /// <summary>Height being edited; NaN = automatic.</summary>
    public double TargetHeight
    {
        get => (double)GetValue(TargetHeightProperty);
        set => SetValue(TargetHeightProperty, value);
    }

    /// <summary>Element whose actual size seeds the first drag.</summary>
    public FrameworkElement? TargetElement
    {
        get => (FrameworkElement?)GetValue(TargetElementProperty);
        set => SetValue(TargetElementProperty, value);
    }

    /// <summary>Smallest width the grip will produce.</summary>
    public double MinTargetWidth
    {
        get => (double)GetValue(MinTargetWidthProperty);
        set => SetValue(MinTargetWidthProperty, value);
    }

    /// <summary>Smallest height the grip will produce.</summary>
    public double MinTargetHeight
    {
        get => (double)GetValue(MinTargetHeightProperty);
        set => SetValue(MinTargetHeightProperty, value);
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        double width = TargetWidth;
        double height = TargetHeight;
        if (double.IsNaN(width))
        {
            width = TargetElement?.ActualWidth ?? MinTargetWidth;
        }

        if (double.IsNaN(height))
        {
            height = TargetElement?.ActualHeight ?? MinTargetHeight;
        }

        TargetWidth = Math.Max(MinTargetWidth, width + e.HorizontalChange);
        TargetHeight = Math.Max(MinTargetHeight, height + e.VerticalChange);
        e.Handled = true;
    }
}
