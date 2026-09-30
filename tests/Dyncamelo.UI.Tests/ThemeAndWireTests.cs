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
                "Dyc.NodeTemplate.Blender", "Dyc.NodeTemplate.Reroute",
                "Dyc.SocketTemplate", "Dyc.Socket.Input", "Dyc.Socket.Output", "Dyc.RowSelector",
                "Row.Input", "Row.Output", "Row.Body", "Row.PanelHeader", "Row.Hidden",
                "Dyc.Wire.Blender", "Dyc.ConnectionTemplate", "Dyc.TopMenuItem",
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
    public void MutedWireHasATickAcrossItsMiddleAndAPlainWireDoesNot()
    {
        StaHost.Run(() =>
        {
            var source = new Point(0, 0);
            var target = new Point(300, 80);
            var plain = new DycWire { Source = source, Target = target, Stroke = Brushes.White };
            var muted = new DycWire { Source = source, Target = target, IsMuted = true, Stroke = Brushes.White };

            var (p1, p2) = DycWire.ControlPoints(source, target);
            var mid = DycWire.Evaluate(source, p1, p2, target, 0.5);
            var ahead = DycWire.Evaluate(source, p1, p2, target, 0.52);
            var tangent = new Vector(ahead.X - mid.X, ahead.Y - mid.Y);
            tangent.Normalize();
            var probe = mid + new Vector(-tangent.Y, tangent.X) * 5d; // 5px off the curve, inside the tick

            Assert.False(Define(plain).StrokeContains(Probe, probe), "plain wire should not reach " + probe);
            Assert.True(Define(muted).StrokeContains(Probe, probe), "muted wire should have a tick through " + probe + " (mid " + mid + ")");
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

    [Fact]
    public void ColourSwatchShowsTheBoundColourAndAHintWhenUnset()
    {
        StaHost.Run(() =>
        {
            var picker = new ColorSwatchPicker { Hex = "#FF102030" };
            var swatch = (System.Windows.Controls.Border)picker.Children[0];
            var hint = (System.Windows.Controls.TextBlock)picker.Children[1];
            Assert.Equal(Color.FromArgb(255, 0x10, 0x20, 0x30), ((SolidColorBrush)swatch.Background).Color);
            Assert.Equal(Visibility.Collapsed, hint.Visibility);

            picker.Hex = string.Empty;
            Assert.Equal(Visibility.Visible, hint.Visibility);

            var byChannels = new ColorSwatchPicker { A = 255, R = 1, G = 2, B = 3 };
            var brush = (SolidColorBrush)((System.Windows.Controls.Border)byChannels.Children[0]).Background;
            Assert.Equal(Color.FromArgb(255, 1, 2, 3), brush.Color);
        });
    }
}
