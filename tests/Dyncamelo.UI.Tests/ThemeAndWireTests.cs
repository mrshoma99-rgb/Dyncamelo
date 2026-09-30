using System;
using System.Windows;
using System.Windows.Media;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

public class ThemeAndWireTests
{
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
            wire.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var bounds = wire.RenderedGeometry.Bounds;
            Assert.False(bounds.IsEmpty);
            Assert.True(bounds.Width >= 290 && bounds.Height >= 90, "bounds " + bounds);
        });
    }

    [Fact]
    public void LowDetailWireIsAStraightSegment()
    {
        StaHost.Run(() =>
        {
            var wire = new DycWire { Source = new Point(0, 0), Target = new Point(200, 0), IsLowDetail = true, Stroke = Brushes.White };
            wire.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var bounds = wire.RenderedGeometry.Bounds;
            Assert.True(bounds.Height < 0.5, "straight line should be flat, got " + bounds);
        });
    }

    [Fact]
    public void MutedWireAddsATickAcrossTheMiddle()
    {
        StaHost.Run(() =>
        {
            var plain = new DycWire { Source = new Point(0, 0), Target = new Point(300, 0), Stroke = Brushes.White };
            var muted = new DycWire { Source = new Point(0, 0), Target = new Point(300, 0), IsMuted = true, Stroke = Brushes.White };
            plain.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            muted.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(muted.RenderedGeometry.Bounds.Height > plain.RenderedGeometry.Bounds.Height + 6);
        });
    }

    [Fact]
    public void NonFiniteEndpointsNeverThrow()
    {
        StaHost.Run(() =>
        {
            var wire = new DycWire { Source = new Point(double.NaN, 0), Target = new Point(10, double.PositiveInfinity), Stroke = Brushes.White };
            wire.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.NotNull(wire.RenderedGeometry);
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
