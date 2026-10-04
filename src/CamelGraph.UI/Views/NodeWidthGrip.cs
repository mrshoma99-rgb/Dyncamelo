using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CamelGraph.UI.ViewModels;

namespace CamelGraph.UI.Views;

/// <summary>
/// A thin drag handle on a node's right edge that sets the node's width
/// (persisted with the graph). Double-click returns to automatic width.
/// </summary>
public sealed class NodeWidthGrip : Thumb
{
    /// <summary>Smallest width a node can be dragged to.</summary>
    public const double MinNodeWidth = 140d;

    /// <summary>Largest width a node can be dragged to.</summary>
    public const double MaxNodeWidth = 900d;

    /// <summary>The element whose rendered width is being changed.</summary>
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target),
        typeof(FrameworkElement),
        typeof(NodeWidthGrip));

    /// <summary>Creates the grip.</summary>
    public NodeWidthGrip()
    {
        Cursor = Cursors.SizeWE;
        Width = 6;
        HorizontalAlignment = HorizontalAlignment.Right;
        Background = System.Windows.Media.Brushes.Transparent;
        Template = CreateTemplate();
        DragDelta += OnDragDelta;
        MouseDoubleClick += OnDoubleClick;
    }

    /// <inheritdoc cref="TargetProperty" />
    public FrameworkElement? Target
    {
        get => (FrameworkElement?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    private NodeViewModel? Node => DataContext as NodeViewModel;

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var node = Node;
        if (node == null)
        {
            return;
        }

        // Same incremental scheme as SizeGripThumb: current width plus this delta.
        var current = double.IsNaN(node.NodeWidth) ? (Target?.ActualWidth ?? 0d) : node.NodeWidth;
        if (current <= 0d)
        {
            return;
        }

        node.NodeWidth = Math.Round(Math.Max(MinNodeWidth, Math.Min(MaxNodeWidth, current + e.HorizontalChange)));
        e.Handled = true;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Node?.ResetWidthCommand.Execute(null);
        e.Handled = true;
    }

    private static ControlTemplate CreateTemplate()
    {
        var template = new ControlTemplate(typeof(NodeWidthGrip));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        template.VisualTree = border;
        return template;
    }
}
