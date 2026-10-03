using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IoPath = System.IO.Path;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Loader;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>The palettes as data: complete, readable, and never referenced in a way that would freeze them.</summary>
public class PaletteDataTests
{
    private static double Luminance(Color c)
    {
        double F(byte v)
        {
            var s = v / 255d;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static Color C(UiPalette p, string name) => p.Colors["Dyc." + name];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(IoPath.Combine(dir.FullName, "Dyncamelo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void EveryPaletteDefinesEveryKey()
    {
        Assert.True(PaletteCatalog.All.Count >= 4);
        foreach (var palette in PaletteCatalog.All)
        {
            foreach (var key in PaletteCatalog.Keys)
            {
                Assert.True(palette.Colors.ContainsKey(key), palette.Id + " lacks " + key);
            }

            Assert.Equal(PaletteCatalog.Keys.Count, palette.Colors.Count);
        }
    }

    [Fact]
    public void OnlyTheLightPaletteIsLight()
    {
        Assert.Equal(new[] { "Light" }, PaletteCatalog.All.Where(p => p.IsLight).Select(p => p.Id).ToArray());
    }

    [Fact]
    public void TextAndChromeAreReadableInEveryPalette()
    {
        foreach (var p in PaletteCatalog.All)
        {
            void AtLeast(string a, string b, double ratio)
            {
                var actual = Contrast(C(p, a), C(p, b));
                Assert.True(actual >= ratio, p.Id + ": " + a + " on " + b + " is " + actual.ToString("0.00") + ", needs " + ratio);
            }

            AtLeast("TextBrush", "NodeBodyBrush", 4.5);
            AtLeast("TextBrush", "PanelBrush", 4.5);
            AtLeast("TextBrush", "InputBackgroundBrush", 4.5);
            AtLeast("TextBrush", "CanvasBrush", 4.5);
            AtLeast("TextBrush", "HoverBrush", 4.5);
            AtLeast("TextBrush", "NoteBrush", 4.5);
            AtLeast("SubtleTextBrush", "NodeBodyBrush", 3.0);
            AtLeast("SubtleTextBrush", "PanelBrush", 3.0);
            AtLeast("AccentBrush", "NodeBodyBrush", 3.0);
            AtLeast("AccentBrush", "PanelBrush", 3.0);
            AtLeast("WarningBrush", "NodeBodyBrush", 3.0);
            AtLeast("ErrorBrush", "NodeBodyBrush", 3.0);
            AtLeast("FnCreateBrush", "PanelBrush", 3.0);
            AtLeast("FnModifyBrush", "PanelBrush", 3.0);
            AtLeast("FnInfoBrush", "PanelBrush", 3.0);
            AtLeast("OnBrandBrush", "BrandStartColor", 3.5);
            AtLeast("OnBrandBrush", "BrandEndColor", 3.5);
            AtLeast("OnBrandSubtleBrush", "BrandStartColor", 3.0);
            AtLeast("OnBrandSubtleBrush", "BrandEndColor", 3.0);
            AtLeast("WireBrush", "CanvasBrush", 2.5);
            AtLeast("OnPrimaryBrush", "PrimaryBrush", 4.5);
            AtLeast("PrimaryBrush", "PanelBrush", 3.0);   // the Run button must stand out from the toolbar it sits on
        }
    }

    [Fact]
    public void SocketAndWireColoursKeepContrastOnTheirCanvas()
    {
        foreach (var p in PaletteCatalog.All)
        {
            var card = C(p, "NodeBodyBrush");
            foreach (var family in PortKindPalette.Families)
            {
                var hex = p.IsLight ? PortKindPalette.HexOnLight(family) : PortKindPalette.Hex(family);
                var colour = (Color)ColorConverter.ConvertFromString(hex);
                var ratio = Contrast(colour, card);
                Assert.True(ratio >= 2.5, p.Id + ": " + family + " " + hex + " on the node card is " + ratio.ToString("0.00"));
            }
        }
    }

    [Fact]
    public void NoXamlNamesAPaletteColourWithStaticResource()
    {
        // A brush named with StaticResource in a Style or Template is frozen by WPF, so it can never change colour.
        var root = RepoRoot();
        var files = new[]
        {
            "src/Dyncamelo.UI/Themes/DyncameloDark.xaml", "src/Dyncamelo.UI/Views/DyncameloEditorControl.xaml",
            "src/Dyncamelo.UI/Views/TextInputDialog.xaml",
        };
        var keys = new HashSet<string>(PaletteCatalog.Keys);
        var bad = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(IoPath.Combine(root, file));
            foreach (Match m in Regex.Matches(text, @"\{StaticResource (Dyc\.\w+)\}"))
            {
                if (keys.Contains(m.Groups[1].Value))
                {
                    bad.Add(file + ": " + m.Value);
                }
            }
        }

        Assert.True(bad.Count == 0, "Use DynamicResource for palette colours: " + string.Join("; ", bad.Take(10)));
    }

    [Fact]
    public void NoXamlHardCodesAColourOutsideTheKnownDataColours()
    {
        var root = RepoRoot();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "#66000000",                                                   // the busy scrim
            "#FF3D6A99", "#FF3F7249", "#FF9A7B2D", "#FF8A3B3B", "#FF6B4E8E", "#FF5A6273", // frame colour presets (data)
            "#FFFCE9A6", "#FFE6C766", "#FF3A3320",                         // the sticky-note yellow
        };
        var bad = new List<string>();
        foreach (var file in new[] { "src/Dyncamelo.UI/Views/DyncameloEditorControl.xaml", "src/Dyncamelo.UI/Views/TextInputDialog.xaml" })
        {
            var text = File.ReadAllText(IoPath.Combine(root, file));
            foreach (Match m in Regex.Matches(text, @"#[0-9A-Fa-f]{6,8}\b"))
            {
                if (!allowed.Contains(m.Value))
                {
                    bad.Add(file + ": " + m.Value);
                }
            }
        }

        Assert.True(bad.Count == 0, "Hard-coded colours (use a palette key): " + string.Join("; ", bad));
    }

    [Fact]
    public void TheThemeDefinesEveryPaletteKey()
    {
        StaHost.Run(() =>
        {
            var theme = new ResourceDictionary { Source = new Uri("pack://application:,,,/Dyncamelo.UI;component/Themes/DyncameloDark.xaml") };
            foreach (var key in PaletteCatalog.Keys)
            {
                Assert.True(theme.Contains(key), "The theme lacks " + key);
            }
        });
    }
}

/// <summary>Switching palette in a real window recolours everything, including what styles and templates reference.</summary>
public class PaletteWindowTests
{
    private sealed class Host : IDisposable
    {
        public GraphEditorViewModel Vm = null!;
        public Window Window = null!;
        public DyncameloEditorControl Control => (DyncameloEditorControl)Window.Content;

        public void Dispose() => StaHost.Run(() => Window.Close());
    }

    private static Host Build()
    {
        var host = new Host();
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            registry.RegisterNodeType("TestSum", () => new SumNode());
            registry.RegisterNodeType("TestEditors", () => new EditorsNode());
            var settings = new UiSettingsService(IoPath.Combine(IoPath.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            host.Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            var a = new SumNode { X = 60, Y = 60 };
            var b = new SumNode { X = 420, Y = 80 };
            var editors = new EditorsNode { X = 60, Y = 320 };
            var sink = new MultiSinkNode { X = 420, Y = 320 };
            var watch = new Dyncamelo.Core.Nodes.WatchNode { X = 760, Y = 80 };
            foreach (var node in new Dyncamelo.Core.Graph.NodeModel[] { a, b, editors, sink, watch })
            {
                host.Vm.Graph.AddNode(node);
            }

            host.Vm.Graph.Connect(a.OutPorts[0], b.InPorts[0]);
            host.Vm.Graph.Connect(a.OutPorts[0], sink.InPorts[0]);
            host.Vm.Graph.Connect(b.OutPorts[0], sink.InPorts[0]);
            host.Vm.AddNote(new Point(60, 560));
            host.Window = new Window
            {
                Width = 1400,
                Height = 900,
                Content = new DyncameloEditorControl { ViewModel = host.Vm },
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };
            host.Window.Show();
        });
        StaHost.Flush();
        return host;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<(string Property, Brush? Brush)> BrushesOf(DependencyObject d)
    {
        if (d is Control control)
        {
            yield return ("Background", control.Background);
            yield return ("Foreground", control.Foreground);
            yield return ("BorderBrush", control.BorderBrush);
        }

        if (d is Border border)
        {
            yield return ("Border.Background", border.Background);
            yield return ("Border.BorderBrush", border.BorderBrush);
        }

        if (d is TextBlock text)
        {
            yield return ("TextBlock.Foreground", text.Foreground);
            yield return ("TextBlock.Background", text.Background);
        }

        if (d is Panel panel)
        {
            yield return ("Panel.Background", panel.Background);
        }

        if (d is Shape shape)
        {
            yield return ("Shape.Fill", shape.Fill);
            yield return ("Shape.Stroke", shape.Stroke);
        }

        if (d is ScrubNumberBox field)
        {
            yield return ("Scrub.Field", field.FieldBrush);
            yield return ("Scrub.Hover", field.HoverBrush);
            yield return ("Scrub.Fill", field.FillBrush);
            yield return ("Scrub.Text", field.TextBrush);
            yield return ("Scrub.Subtle", field.SubtleBrush);
            yield return ("Scrub.Accent", field.AccentBrush);
        }
    }

    // Colours only the dark palette (or the dark family colours) uses: a colour the Light palette also uses proves nothing.
    private static Dictionary<Color, string> DarkOnlyColours()
    {
        var dark = PaletteCatalog.ById("DyncameloDark")!;
        var light = PaletteCatalog.ById("Light")!;
        var lightColours = new HashSet<Color>(light.Colors.Values);
        foreach (var family in PortKindPalette.Families)
        {
            lightColours.Add((Color)ColorConverter.ConvertFromString(PortKindPalette.HexOnLight(family)));
        }

        var darkOnly = new Dictionary<Color, string>();
        foreach (var pair in dark.Colors.Where(p => !lightColours.Contains(p.Value)))
        {
            darkOnly[pair.Value] = pair.Key;
        }

        foreach (var family in PortKindPalette.Families)
        {
            var colour = (Color)ColorConverter.ConvertFromString(PortKindPalette.Hex(family));
            if (!lightColours.Contains(colour))
            {
                darkOnly[colour] = "family " + family;
            }
        }

        return darkOnly;
    }

    [Fact]
    public void SwitchingToLightLeavesNothingInDarkPaletteColours()
    {
        using var host = Build();
        StaHost.Run(() => host.Vm.PaletteId = "Light");
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var darkOnly = DarkOnlyColours();
            var stale = new List<string>();
            foreach (var d in Visuals(host.Window))
            {
                if (d is UIElement ui && !ui.IsVisible)
                {
                    continue;
                }

                foreach (var (property, brush) in BrushesOf(d))
                {
                    if (brush is SolidColorBrush solid && darkOnly.TryGetValue(solid.Color, out var source))
                    {
                        stale.Add(d.GetType().Name + "." + property + " = " + source + " (" + ((d as FrameworkElement)?.DataContext?.GetType().Name ?? "-") + ")");
                    }
                }
            }

            Assert.True(stale.Count == 0, stale.Count + " element(s) still show dark-palette colours: " + string.Join("; ", stale.Distinct().Take(25)));
        });
    }

    [Fact]
    public void ThemeResourcesFollowThePaletteIncludingTheHeaderGradient()
    {
        using var host = Build();
        foreach (var id in new[] { "Light", "Midnight", "DyncameloDark" })
        {
            StaHost.Run(() => host.Vm.PaletteId = id);
            StaHost.Flush();
            StaHost.Run(() =>
            {
                var palette = PaletteCatalog.ById(id)!;
                foreach (var pair in palette.Colors)
                {
                    var resource = host.Control.FindResource(pair.Key);
                    var actual = resource is SolidColorBrush brush ? brush.Color : (Color)resource;
                    Assert.True(pair.Value == actual, id + ": " + pair.Key + " is " + actual + ", expected " + pair.Value);
                }

                var gradient = (LinearGradientBrush)host.Control.FindResource("Dyc.BrandBrush");
                Assert.Equal(palette.Colors["Dyc.BrandStartColor"], gradient.GradientStops[0].Color);
                Assert.Equal(palette.Colors["Dyc.BrandEndColor"], gradient.GradientStops[1].Color);
            });
        }
    }

    [Fact]
    public void TheLibraryPanelTheHeaderAndTheCanvasAllSwitch()
    {
        using var host = Build();
        StaHost.Run(() => host.Vm.PaletteId = "Light");
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var light = PaletteCatalog.ById("Light")!;
            var panel = (DockPanel)host.Control.FindName("LibraryPanel");
            Assert.Equal(light.Colors["Dyc.PanelBrush"], ((SolidColorBrush)panel.Background).Color);

            var header = Visuals(host.Window).OfType<Border>().First(b => b.Background is LinearGradientBrush);
            var stops = ((LinearGradientBrush)header.Background).GradientStops;
            Assert.Equal(light.Colors["Dyc.BrandStartColor"], stops[0].Color);

            var brandTitle = Visuals(host.Window).OfType<TextBlock>().First(t => t.Text == "Dyncamelo");
            Assert.Equal(light.Colors["Dyc.OnBrandBrush"], ((SolidColorBrush)brandTitle.Foreground).Color);

            var editor = (Nodify.NodifyEditor)host.Control.FindName("Editor");
            var canvas = ((DrawingBrush)editor.Background).Drawing;
            var group = (DrawingGroup)canvas;
            var fill = (SolidColorBrush)((GeometryDrawing)group.Children[0]).Brush;
            Assert.Equal(light.Colors["Dyc.CanvasBrush"], fill.Color);
            var pen = ((GeometryDrawing)group.Children[1]).Pen!;
            Assert.Equal(light.Colors["Dyc.GridLineBrush"], ((SolidColorBrush)pen.Brush).Color);
        });
    }

    [Fact]
    public void SocketsAndWiresUseTheDarkerFamilyColoursOnLight()
    {
        using var host = Build();
        StaHost.Run(() => host.Vm.PaletteId = "Light");
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var wire = host.Vm.Connections.First();
            var expected = (Color)ColorConverter.ConvertFromString(PortKindPalette.HexOnLight(wire.Source.Family));
            Assert.Equal(expected, ((SolidColorBrush)wire.FamilyBrush).Color);
            host.Vm.PaletteId = "DyncameloDark";
            var dark = (Color)ColorConverter.ConvertFromString(PortKindPalette.Hex(wire.Source.Family));
            Assert.Equal(dark, ((SolidColorBrush)wire.FamilyBrush).Color);
        });
    }
}
