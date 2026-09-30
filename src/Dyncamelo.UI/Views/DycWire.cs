using System;
using System.Windows;
using System.Windows.Media;
using Nodify;

namespace Dyncamelo.UI.Views;

/// <summary>
/// The row-layout wire: a soft cubic Bézier whose handle length follows the
/// horizontal distance (long, gentle S-curves like Blender's noodles), a
/// straight hairline in overview, and a short tick across the middle when muted.
/// Colour, dash and thickness come from the style (family colour, dashed when
/// the wire replicates, accent when selected).
/// </summary>
public sealed class DycWire : BaseConnection
{
    /// <summary>Draw a straight line instead of a curve (overview zoom).</summary>
    public static readonly DependencyProperty IsLowDetailProperty = DependencyProperty.Register(
        nameof(IsLowDetail),
        typeof(bool),
        typeof(DycWire),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Mark the wire as muted with a tick across its middle.</summary>
    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(
        nameof(IsMuted),
        typeof(bool),
        typeof(DycWire),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    static DycWire()
    {
        // Nodify's cutting tool only slices wire types it knows about.
        NodifyEditor.CuttingConnectionTypes.Add(typeof(DycWire));
    }

    /// <inheritdoc cref="IsLowDetailProperty" />
    public bool IsLowDetail
    {
        get => (bool)GetValue(IsLowDetailProperty);
        set => SetValue(IsLowDetailProperty, value);
    }

    /// <inheritdoc cref="IsMutedProperty" />
    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    /// <summary>Bézier control points for a wire from <paramref name="source"/> to <paramref name="target"/>.</summary>
    internal static (Point P1, Point P2) ControlPoints(Point source, Point target)
    {
        var dx = Math.Abs(target.X - source.X);
        var offset = Math.Max(40d, Math.Min(dx * 0.5d + 20d, 200d));
        return (new Point(source.X + offset, source.Y), new Point(target.X - offset, target.Y));
    }

    /// <summary>Point of the cubic at parameter <paramref name="t"/>.</summary>
    internal static Point Evaluate(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1d - t;
        var b0 = u * u * u;
        var b1 = 3d * u * u * t;
        var b2 = 3d * u * t * t;
        var b3 = t * t * t;
        return new Point(
            b0 * p0.X + b1 * p1.X + b2 * p2.X + b3 * p3.X,
            b0 * p0.Y + b1 * p1.Y + b2 * p2.Y + b3 * p3.Y);
    }

    /// <inheritdoc />
    protected override ((Point ArrowStartSource, Point ArrowStartTarget), (Point ArrowEndSource, Point ArrowEndTarget)) DrawLineGeometry(
        StreamGeometryContext context, Point source, Point target)
    {
        var result = ((target, source), (source, target));
        if (!IsFinite(source) || !IsFinite(target))
        {
            return result;
        }

        if (IsLowDetail)
        {
            context.BeginFigure(source, false, false);
            context.LineTo(target, true, true);
            return result;
        }

        var (p1, p2) = ControlPoints(source, target);
        context.BeginFigure(source, false, false);
        context.BezierTo(p1, p2, target, true, true);

        if (IsMuted)
        {
            var mid = Evaluate(source, p1, p2, target, 0.5d);
            var ahead = Evaluate(source, p1, p2, target, 0.52d);
            var tangent = new Vector(ahead.X - mid.X, ahead.Y - mid.Y);
            if (tangent.Length > 0d)
            {
                tangent.Normalize();
                var normal = new Vector(-tangent.Y, tangent.X) * 6d;
                context.BeginFigure(mid + normal, false, false);
                context.LineTo(mid - normal, true, false);
            }
        }

        return result;
    }

    private static bool IsFinite(Point p) =>
        !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y);
}
