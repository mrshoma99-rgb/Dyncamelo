using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

public class ThemeAndWireTests
{
    // Shape.RenderedGeometry is empty until a render pass; the defining geometry is what WPF draws.
    internal static Geometry Define(DycWire wire) =>
        (Geometry)typeof(Shape).GetProperty("DefiningGeometry", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wire)!;

    [Fact]
    public void ThemeLoadsAndExposesTheRowLayoutResources()
    {
        StaHost.Run(() =>
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Dyncamelo.UI;component/Themes/DyncameloDark.xaml"),
            };

            foreach (var key in new[]
            {
                "Dyc.NodeTemplate.Blender", "Dyc.NodeTemplate.Classic", "Dyc.NodeTemplate.Reroute",
                "Dyc.SocketTemplate", "Dyc.Socket.Input", "Dyc.Socket.Output", "Dyc.RowSelector",
                "Row.Input", "Row.Output", "Row.Body", "Row.PanelHeader", "Row.Hidden",
                "Dyc.Wire.Blender", "Dyc.Wire.Classic", "Dyc.ConnectionTemplate", "Dyc.TopMenuItem",
            })
            {
                Assert.True(theme.Contains(key), "missing theme resource: " + key);
            }
        });
    }

    [Fact]
    public void WireDrawsACurveBetweenItsEndpoints()
    {
        StaHost.Run(() =>
        {
            var wire = new DycWire { Source = new Point(10, 10), Target = new Point(310, 110), Stroke = Brushes.White, StrokeThickness = 2 };
            var bounds = Define(wire).Bounds;
            Assert.False(bounds.IsEmpty);
            Assert.True(Math.Abs(bounds.Left - 10) < 1 && Math.Abs(bounds.Right - 310) < 1, "horizontal span " + bounds);
            Assert.True(Math.Abs(bounds.Top - 10) < 1 && Math.Abs(bounds.Bottom - 110) < 1, "vertical span " + bounds);
        });
    }

    private static readonly Pen Probe = new Pen(Brushes.Black, 3);

    [Fact]
    public void CurvedWireLeavesTheStraightLineButLowDetailFollowsIt()
    {
        StaHost.Run(() =>
        {
            var curved = new DycWire { Source = new Point(0, 0), Target = new Point(200, 100), Stroke = Brushes.White };
            var straight = new DycWire { Source = new Point(0, 0), Target = new Point(200, 100), IsLowDetail = true, Stroke = Brushes.White };
            var quarter = new Point(50, 25); // on the straight segment, off the S-curve
            Assert.True(Define(straight).StrokeContains(Probe, quarter), "low-detail wire should pass through " + quarter);
            Assert.False(Define(curved).StrokeContains(Probe, quarter), "curved wire should not pass through " + quarter);
            Assert.True(Define(curved).StrokeContains(Probe, new Point(100, 50)), "the S-curve is symmetric about the midpoint");
        });
    }

    [Fact]
    public void MutedWireAddsASecondFigureForTheTick()
    {
        StaHost.Run(() =>
        {
            var plain = new DycWire { Source = new Point(0, 0), Target = new Point(300, 80), Stroke = Brushes.White };
            var muted = new DycWire { Source = new Point(0, 0), Target = new Point(300, 80), IsMuted = true, Stroke = Brushes.White };
            var plainFigures = Define(plain).GetFlattenedPathGeometry().Figures.Count;
            var mutedFigures = Define(muted).GetFlattenedPathGeometry().Figures.Count;
            Assert.Equal(1, plainFigures);
            Assert.Equal(2, mutedFigures);
        });
    }

    [Fact]
    public void NonFiniteEndpointsNeverThrow()
    {
        StaHost.Run(() =>
        {
            var wire = new DycWire { Source = new Point(double.NaN, 0), Target = new Point(10, double.PositiveInfinity), Stroke = Brushes.White };
            Assert.NotNull(Define(wire));
        });
    }

    [Fact]
    public void ControlPointsFollowHorizontalDistanceWithinBounds()
    {
        var near = DycWire.ControlPoints(new Point(0, 0), new Point(10, 0));
        Assert.Equal(40d + 0d, near.P1.X - 0d, 6); // clamped minimum handle
        var far = DycWire.ControlPoints(new Point(0, 0), new Point(2000, 0));
        Assert.Equal(200d, far.P1.X, 6);           // clamped maximum handle
        var back = DycWire.ControlPoints(new Point(500, 0), new Point(100, 50));
        Assert.True(back.P1.X > 500 && back.P2.X < 100, "backward wires loop out of both sockets");
    }
}
