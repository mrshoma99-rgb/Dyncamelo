using System;
using System.Windows;
using System.Windows.Controls;
using Dyncamelo.Core.Editing;
using Dyncamelo.UI.ViewModels;

namespace Dyncamelo.UI.Views;

/// <summary>
/// The rows of a collapsed node, Blender style: the input sockets stack down the left edge and the output sockets down the right
/// edge, side by side, so the node is as tall as its longer side instead of as tall as both together. It asks for no width of its
/// own (the title decides that) and gives every row the full width, so each row's socket still sits on its own edge.
/// </summary>
public sealed class CollapsedSocketPanel : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double inputs = 0, outputs = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (IsOutput(child))
            {
                outputs += child.DesiredSize.Height;
            }
            else
            {
                inputs += child.DesiredSize.Height;
            }
        }

        return new Size(0, Math.Max(inputs, outputs));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double inputs = 0, outputs = 0;
        foreach (UIElement child in InternalChildren)
        {
            var height = child.DesiredSize.Height;
            if (IsOutput(child))
            {
                child.Arrange(new Rect(0, outputs, finalSize.Width, height));
                outputs += height;
            }
            else
            {
                child.Arrange(new Rect(0, inputs, finalSize.Width, height));
                inputs += height;
            }
        }

        return finalSize;
    }

    private static bool IsOutput(UIElement child) =>
        child is ContentPresenter presenter && presenter.Content is NodeRowViewModel row && row.Kind == RowKind.Output;
}
